using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using DisputeDesk.Infrastructure;
using DisputeDesk.Infrastructure.Observability;
using DisputeDesk.Infrastructure.Persistence;
using DisputeDesk.Worker.IntegrationTests.Outbox;
using DisputeDesk.Worker.Sla;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace DisputeDesk.Worker.IntegrationTests.Sla;

/// <summary>
/// One SLA sweep on one test database and a fake clock, registered the way the Worker registers it, with the sweep's
/// spans, metrics and log lines captured. The sweep is resolved, not started: a test calls <see cref="SweepAsync"/> for one
/// loop, or starts the hosted service itself. Disputes are written through the commit (AD-14), as the Api writes them.
/// </summary>
public sealed class SweepRig : IAsyncDisposable
{
    /// <summary>When the test disputes are raised by default: a whole second, so instants survive Postgres's microseconds.</summary>
    public static readonly DateTimeOffset RaisedAt = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    public static readonly TimeSpan Microsecond = TimeSpan.FromTicks(TimeSpan.TicksPerMicrosecond);

    public const string CustomerId = "customer-sla-1";
    public const string AccountRef = "ACC-SLA-2";

    private static readonly Actor Customer = new(ActorType.Customer, "login-sla");
    private static readonly Actor Operations = new(ActorType.Operations, "ops-sla");

    private readonly ServiceProvider _services;
    private readonly ActivityListener _spanListener;
    private readonly MeterListener _meterListener = new();

    private SweepRig(string connectionString, IInterceptor[] interceptors)
    {
        ConnectionString = connectionString;
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([new($"ConnectionStrings:{DependencyInjection.ConnectionStringName}", connectionString)])
            .Build());
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(new LineCapture(LogLines)));
        services.AddMetrics();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddInfrastructure();
        services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(interceptors));
        services.AddSingleton<SlaSweep>();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        // Process-wide: the SLA tests run one at a time (SlaTestGroup), so every batch span seen is this test's.
        _spanListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = Spans.Add,
        };
        ActivitySource.AddActivityListener(_spanListener);

        IMeterFactory meterFactory = _services.GetRequiredService<IMeterFactory>();
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Scope == meterFactory && instrument.Meter.Name == Telemetry.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            Measurements.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString()))));
        _meterListener.Start();
    }

    /// <summary>The sweep's clock, at <see cref="RaisedAt"/> until a test moves it.</summary>
    public FakeTimeProvider Clock { get; } = new(RaisedAt);

    public string ConnectionString { get; }

    public SlaSweep Sweep => _services.GetRequiredService<SlaSweep>();

    public LockedCollection<Activity> Spans { get; } = new();

    public ConcurrentQueue<(string Instrument, long Value, Dictionary<string, string?> Tags)> Measurements { get; } = new();

    /// <summary>Every log line as written, followed by each of its structured values as <c>name=value</c>.</summary>
    public ConcurrentQueue<(string Category, string Line)> LogLines { get; } = new();

    /// <summary>The sweep's own log lines and values, without EF Core's or Npgsql's.</summary>
    public IReadOnlyList<string> SweepLogLines => [.. LogLines.Where(line => line.Category == typeof(SlaSweep).FullName).Select(line => line.Line)];

    /// <summary>The ended batch spans whose claim was <paramref name="claim"/> (<c>overdue</c> or <c>warning</c>), in order.</summary>
    public IReadOnlyList<Activity> BatchSpans(string claim) =>
        [.. Spans.Snapshot().Where(span => span.OperationName == SlaSweep.BatchSpanName && span.GetTagItem("disputedesk.sla_sweep.claim") as string == claim)];

    public static async Task<SweepRig> CreateAsync(SlaFixture fixture, params IInterceptor[] interceptors) =>
        new(await fixture.CreateMigratedDatabaseAsync(), interceptors);

    /// <summary>A second sweep on the same database, as a second Worker would be.</summary>
    public static SweepRig OnSameDatabase(SweepRig other, params IInterceptor[] interceptors) => new(other.ConnectionString, interceptors);

    /// <summary>One loop of the sweep: the escalation batches, then the warning batches.</summary>
    public Task SweepAsync() => Sweep.SweepAsync(TestContext.Current.CancellationToken);

    /// <summary>The sum of the measurements of <paramref name="instrument"/> with all of <paramref name="tags"/>.</summary>
    public long Total(string instrument, params (string Name, string Value)[] tags) =>
        Measurements.Where(measurement => measurement.Instrument == instrument
                && tags.All(tag => measurement.Tags.TryGetValue(tag.Name, out string? value) && value == tag.Value))
            .Sum(measurement => measurement.Value);

    public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>
    /// Raises a dispute at <paramref name="raisedAt"/> (so its SLA is due 240 hours later) and takes it to
    /// <paramref name="status"/> an hour later as operations or the customer would, then commits it.
    /// </summary>
    public async Task<Guid> RaiseAsync(DateTimeOffset raisedAt, DisputeStatus status = DisputeStatus.Submitted)
    {
        Dispute dispute = NewDispute(raisedAt, status);
        await using AppDbContext db = CreateDbContext();
        db.Set<Dispute>().Add(dispute);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return dispute.Id;
    }

    /// <summary>Raises <paramref name="count"/> submitted disputes, one second apart from <paramref name="raisedAt"/>, in one commit.</summary>
    public async Task<List<Guid>> RaiseManyAsync(int count, DateTimeOffset raisedAt)
    {
        List<Dispute> disputes = [.. Enumerable.Range(0, count).Select(index => NewDispute(raisedAt.AddSeconds(index), DisputeStatus.Submitted))];
        await using AppDbContext db = CreateDbContext();
        db.Set<Dispute>().AddRange(disputes);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return [.. disputes.Select(dispute => dispute.Id)];
    }

    public async Task<List<StoredEventRow>> StreamAsync(Guid disputeId)
    {
        await using AppDbContext db = CreateDbContext();
        return await db.Set<StoredEventRow>().IgnoreQueryFilters([AppDbContext.OwnershipFilter]).AsNoTracking()
            .Where(row => row.DisputeId == disputeId).OrderBy(row => row.Version).ToListAsync(TestContext.Current.CancellationToken);
    }

    public async Task<List<OutboxMessage>> OutboxAsync(Guid disputeId)
    {
        await using AppDbContext db = CreateDbContext();
        return await db.Set<OutboxMessage>().AsNoTracking()
            .Where(row => row.DisputeId == disputeId).OrderBy(row => row.DisputeVersion).ToListAsync(TestContext.Current.CancellationToken);
    }

    public async Task<Dispute> DisputeRowAsync(Guid disputeId)
    {
        await using AppDbContext db = CreateDbContext();
        return await db.Set<Dispute>().IgnoreQueryFilters([AppDbContext.OwnershipFilter]).AsNoTracking()
            .SingleAsync(dispute => dispute.Id == disputeId, TestContext.Current.CancellationToken);
    }

    /// <summary>How many <c>DisputeEscalated</c> stored events each dispute has, and how many outbox rows they have (a row shares its stored event's ID).</summary>
    public async Task<Dictionary<Guid, (int Escalated, int Published)>> EscalationCountsAsync(IEnumerable<Guid> disputeIds)
    {
        Guid[] ids = [.. disputeIds];
        await using AppDbContext db = CreateDbContext();
        IQueryable<StoredEventRow> escalations = db.Set<StoredEventRow>().IgnoreQueryFilters([AppDbContext.OwnershipFilter])
            .Where(row => ids.Contains(row.DisputeId) && row.EventType == nameof(DisputeEscalated));
        Dictionary<Guid, int> escalated = await escalations
            .GroupBy(row => row.DisputeId).ToDictionaryAsync(group => group.Key, group => group.Count(), TestContext.Current.CancellationToken);
        Dictionary<Guid, int> published = await db.Set<OutboxMessage>()
            .Where(message => escalations.Any(row => row.Id == message.Id))
            .GroupBy(message => message.DisputeId).ToDictionaryAsync(group => group.Key, group => group.Count(), TestContext.Current.CancellationToken);
        return ids.ToDictionary(id => id, id => (escalated.GetValueOrDefault(id), published.GetValueOrDefault(id)));
    }

    public async ValueTask DisposeAsync()
    {
        _spanListener.Dispose();
        _meterListener.Dispose();
        await _services.DisposeAsync();
    }

    private static Dispute NewDispute(DateTimeOffset raisedAt, DisputeStatus status)
    {
        var transaction = new TransactionSnapshot(
            AccountRef, $"TXN-SLA-{Guid.NewGuid():N}", "0042", raisedAt.AddDays(-2), "Shop SLA description", 87.23m, "ZAR", null,
            TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);
        Dispute dispute = Dispute.Raise(
            CustomerId, transaction, everDisputed: false, Reason.Unrecognised, note: null, expectedAmount: null, Customer, raisedAt).Value;
        DateTimeOffset later = raisedAt.AddHours(1);
        _ = status switch
        {
            DisputeStatus.Submitted => true,
            DisputeStatus.UnderReview => dispute.StartReview(Operations, later).IsSuccess,
            DisputeStatus.Escalated => dispute.Escalate(Operations, later).IsSuccess,
            DisputeStatus.Resolved => dispute.StartReview(Operations, later).IsSuccess
                && dispute.Resolve(Outcome.Upheld, "Refunded", Operations, later).IsSuccess,
            DisputeStatus.Withdrawn => dispute.Withdraw(null, Customer, later).IsSuccess,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped status."),
        };
        Assert.Equal(status, dispute.Status);
        return dispute;
    }

    /// <summary>Keeps every log line, and each structured value logged with it, as text.</summary>
    private sealed class LineCapture(ConcurrentQueue<(string Category, string Line)> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, lines);

        public void Dispose()
        {
            // Nothing to release: the lines stay readable after the provider is disposed.
        }

        private sealed class Logger(string category, ConcurrentQueue<(string Category, string Line)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lines.Enqueue((category, formatter(state, exception)));
                if (exception is not null)
                {
                    lines.Enqueue((category, $"exception={exception}"));
                }

                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                {
                    foreach ((string key, object? value) in values)
                    {
                        lines.Enqueue((category, $"{key}={value}"));
                    }
                }
            }
        }
    }
}
