using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using DisputeDesk.Infrastructure.Outbox;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redpanda;

namespace DisputeDesk.Worker.IntegrationTests.Outbox;

/// <summary>
/// One real PostgreSQL and one Redpanda for the relay tests. The database is migrated once and shared, so every test
/// inserts its own rows for fresh dispute IDs and asserts only on those rows and on Kafka records with those keys; none
/// assumes an empty outbox or topic (AD-17, Seeded rows).
/// </summary>
public sealed class OutboxFixture : IAsyncLifetime
{
    // The Compose image, pinned by tag and digest (AD-27), not the module's default tag.
    private const string RedpandaImage =
        "docker.redpanda.com/redpandadata/redpanda:v26.2.2@sha256:468bd13a9f2bd24794cb7fddc867c767fb1008b9a07b297b89fde48c564d7d96";

    /// <summary>Marker text for a hand-built row of the marker dispute (<see cref="CommitMarkerDisputeAsync"/>).</summary>
    public const string MarkerText = "Shop PMARK description, note PMARK text, customer-pmark-1, ACC-PMARK-2, TXN-PMARK-3, QZXW";

    private const string MarkerCustomerId = "customer-pmark-1";
    private const string MarkerAccountRef = "ACC-PMARK-2";
    private const string MarkerTransactionId = "TXN-PMARK-3";
    private const string MarkerDescription = "Shop PMARK description";
    private const string MarkerMaskedCard = "492181******PMRK";
    // Not hex digits, so no trace ID, span ID or GUID can hold it by chance.
    private const string MarkerAccountLast4 = "QZXW";
    private const string MarkerNote = "note PMARK text";
    private const string MarkerActorId = "login-pmark-4";
    private const decimal MarkerAmount = 9876.53m;
    private const decimal MarkerExpectedAmount = 1234.56m;

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
    private readonly RedpandaContainer _redpanda = new RedpandaBuilder(RedpandaImage).Build();

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>
    /// For a test connection that holds the advisory lock. Unpooled, as the relay's own, so disposing it ends the session
    /// and releases the lock; a pooled one would go back to the pool still holding it (PostgreSQL, Advisory Locks).
    /// </summary>
    public string LockHolderConnectionString => new NpgsqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString;

    /// <summary>Read once at start: Testcontainers cannot read the mapped port of a paused container, though it keeps it.</summary>
    public string BootstrapServers { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redpanda.StartAsync());
        BootstrapServers = new Uri(_redpanda.GetBootstrapAddress()).Authority;
        await using (AppDbContext db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        // As the Compose redpanda-topics service does: the dispute events topic with 3 partitions.
        using IAdminClient admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = OutboxRelay.Topic, NumPartitions = 3, ReplicationFactor = 1 }]);
    }

    public async ValueTask DisposeAsync()
    {
        await _redpanda.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>A test clock at the current whole second, so instants survive Postgres's microsecond precision and file times exactly.</summary>
    public static RelayClock NewClock()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        return new RelayClock(now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond)));
    }

    /// <summary>
    /// Freezes the broker (Testcontainers <c>PauseAsync</c>): it keeps its mapped port and accepts connections, but answers
    /// nothing until <see cref="UnpauseBrokerAsync"/>, so a send waits out its delivery timeout. A test that pauses it
    /// unpauses it in a <c>finally</c>, because the broker is shared.
    /// </summary>
    public Task PauseBrokerAsync() => _redpanda.PauseAsync(TestContext.Current.CancellationToken);

    public Task UnpauseBrokerAsync() => _redpanda.UnpauseAsync(TestContext.Current.CancellationToken);

    /// <summary>Moves the test clock on with real time, 100 ms at a time, until stopped: for a test where librdkafka's own, real, timeouts run.</summary>
    public static async Task FollowRealTimeAsync(FakeTimeProvider clock, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                clock.Advance(TimeSpan.FromMilliseconds(100));
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    /// <summary>Starts a relay host; <paramref name="buildProducer"/> replaces how it builds its producer (<see cref="FailingProducer"/>).</summary>
    public Task<RelayHost> StartRelayAsync(
        TimeProvider clock,
        Func<ProducerBuilder<string, string>, IProducer<string, string>>? buildProducer = null) =>
        RelayHost.StartAsync(clock, ConnectionString, BootstrapServers, buildProducer);

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>Raises a dispute through the commit (AD-14), which writes its version 1 outbox row due at <paramref name="at"/>.</summary>
    public async Task<OutboxMessage> RaiseAsync(DateTimeOffset at)
    {
        var transaction = new TransactionSnapshot(
            "ACC-T-1", $"TXN-{Guid.NewGuid():N}", "0011", at.AddDays(-2), "Test shop", 87.23m, "ZAR", null,
            TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);
        Dispute dispute = Dispute.Raise(
            "customer-t", transaction, everDisputed: false, Reason.Unrecognised, note: null, expectedAmount: null,
            new Actor(ActorType.Customer, "login-t"), at).Value;
        return await CommitAsync(dispute);
    }

    /// <summary>Commits a newly raised dispute and returns its outbox row.</summary>
    public async Task<OutboxMessage> CommitAsync(Dispute dispute)
    {
        await using AppDbContext db = CreateDbContext();
        db.Set<Dispute>().Add(dispute);
        await db.SaveChangesAsync();
        return await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(message => message.DisputeId == dispute.Id);
    }

    /// <summary>
    /// Raises the marker dispute at <paramref name="at"/> through the commit and returns its version 1 outbox row: every
    /// private field of <see cref="DisputeRaised"/> and the actor ID carry a marker (story 4.1's privacy test).
    /// </summary>
    public async Task<OutboxMessage> CommitMarkerDisputeAsync(DateTimeOffset at)
    {
        // A fresh transaction per raise, still holding the marker: a transaction can be disputed only once.
        var transaction = new TransactionSnapshot(
            MarkerAccountRef, $"{MarkerTransactionId}-{Guid.NewGuid():N}", MarkerAccountLast4, MarkerTransactionDate(at), MarkerDescription,
            MarkerAmount, "ZAR", MarkerMaskedCard, TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);
        Dispute dispute = Dispute.Raise(
            MarkerCustomerId, transaction, everDisputed: false, Reason.WrongAmount, MarkerNote, MarkerExpectedAmount,
            new Actor(ActorType.Customer, MarkerActorId), at).Value;
        return await CommitAsync(dispute);
    }

    /// <summary>The markers of a marker dispute raised at <paramref name="at"/>.</summary>
    public static string[] Markers(DateTimeOffset at) =>
    [
        MarkerCustomerId, MarkerAccountRef, MarkerTransactionId, MarkerDescription, MarkerMaskedCard, MarkerAccountLast4, MarkerNote, MarkerActorId, "PMARK",
        MarkerTransactionDate(at).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        // The amounts; the disputed amount is the transaction amount less the expected one.
        "9876.53", "1234.56", "8641.97",
    ];

    /// <summary>A hand-built later version of a dispute's outbox row; only <c>Raise</c> exists to write one through the commit.</summary>
    public async Task<OutboxMessage> AddVersionAsync(Guid disputeId, int version, DateTimeOffset at)
    {
        var id = Guid.CreateVersion7(at);
        var message = new OutboxMessage
        {
            Id = id,
            EventType = "disputedesk.dispute.status-changed.v1",
            DisputeId = disputeId,
            DisputeVersion = version,
            Payload = JsonSerializer.Serialize(new { id, data = new { disputeVersion = version } }),
            Status = OutboxStatus.Pending,
            NextAttemptAtUtc = at,
            CreatedAtUtc = at,
        };
        await using AppDbContext db = CreateDbContext();
        db.Set<OutboxMessage>().Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    /// <summary>
    /// A hand-built later version whose payload holds <paramref name="content"/> and is padded past the producer's
    /// <c>message.max.bytes</c> (librdkafka default 1,000,000 bytes), so the client refuses it locally with no broker round trip.
    /// </summary>
    public async Task<OutboxMessage> AddOversizedVersionAsync(Guid disputeId, int version, DateTimeOffset at, string content)
    {
        var id = Guid.CreateVersion7(at);
        var message = new OutboxMessage
        {
            Id = id,
            EventType = "disputedesk.dispute.status-changed.v1",
            DisputeId = disputeId,
            DisputeVersion = version,
            Payload = JsonSerializer.Serialize(new { id, data = new { disputeVersion = version, content, padding = new string('x', 1_100_000) } }),
            Status = OutboxStatus.Pending,
            NextAttemptAtUtc = at,
            CreatedAtUtc = at,
        };
        await using AppDbContext db = CreateDbContext();
        db.Set<OutboxMessage>().Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    /// <summary>
    /// For a row the client refuses on every send and that is due now: waits for the relay's first refusal, then moves the
    /// clock on 10 seconds (one idle wait, more than any retry delay before the 5th) while the relay waits, four times.
    /// Returns the instant of each refusal, which is the batch's instant, with the row as that refusal left it.
    /// </summary>
    public async Task<List<(DateTimeOffset RefusedAt, OutboxMessage Row)>> RefuseFiveTimesAsync(RelayClock clock, Guid id)
    {
        await WaitUntilAsync(async () => clock.RelayWaitsNow && (await RowAsync(id)).Attempts == 1);
        var refusals = new List<(DateTimeOffset, OutboxMessage)> { (clock.GetUtcNow(), await RowAsync(id)) };
        for (int refusal = 2; refusal <= 5; refusal++)
        {
            await clock.StepAsync(TimeSpan.FromSeconds(10));
            refusals.Add((clock.GetUtcNow(), await RowAsync(id)));
        }

        return refusals;
    }

    /// <summary>Changes one outbox row directly, as only a test does.</summary>
    public async Task UpdateRowAsync(Guid id, Action<OutboxMessage> change)
    {
        await using AppDbContext db = CreateDbContext();
        OutboxMessage message = await db.Set<OutboxMessage>().SingleAsync(row => row.Id == id);
        change(message);
        await db.SaveChangesAsync();
    }

    /// <summary>Sets the stored trace context columns, which the commit takes from <c>Activity.Current</c>.</summary>
    public async Task SetTraceContextAsync(Guid id, string? traceParent, string? traceState)
    {
        await using AppDbContext db = CreateDbContext();
        await db.Set<OutboxMessage>()
            .Where(row => row.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.TraceParent, traceParent)
                .SetProperty(row => row.TraceState, traceState));
    }

    /// <summary>Ends the session that holds the relay's advisory lock, as a dropped connection would (PostgreSQL, pg_terminate_backend).</summary>
    public async Task TerminateLockConnectionAsync()
    {
        await using var admin = new NpgsqlConnection(ConnectionString);
        await admin.OpenAsync(TestContext.Current.CancellationToken);
        await using var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_locks WHERE locktype = 'advisory' AND objid::bigint = @key", admin);
        terminate.Parameters.Add(new NpgsqlParameter<long>("key", OutboxRelay.LockKey));
        Assert.True(await terminate.ExecuteScalarAsync(TestContext.Current.CancellationToken) is true);
    }

    public async Task<OutboxMessage> RowAsync(Guid id)
    {
        await using AppDbContext db = CreateDbContext();
        return await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(row => row.Id == id);
    }

    /// <summary>Waits until every given row is Sent, moving <paramref name="clock"/> on by 1 second per step when given.</summary>
    public async Task WaitUntilSentAsync(IEnumerable<Guid> ids, FakeTimeProvider? clock = null)
    {
        Guid[] wanted = [.. ids];
        await WaitUntilAsync(
            async () =>
            {
                await using AppDbContext db = CreateDbContext();
                int sent = await db.Set<OutboxMessage>().CountAsync(row => wanted.Contains(row.Id) && row.Status == OutboxStatus.Sent);
                return sent == wanted.Length;
            },
            clock);
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, FakeTimeProvider? clock = null)
    {
        var elapsed = Stopwatch.StartNew();
        while (!await condition())
        {
            if (elapsed.Elapsed > WaitLimit)
            {
                throw new TimeoutException($"The condition was not met within {WaitLimit}.");
            }

            clock?.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(50);
        }
    }

    public static Task WaitUntilAsync(Func<bool> condition, FakeTimeProvider? clock = null) =>
        WaitUntilAsync(() => Task.FromResult(condition()), clock);

    /// <summary>Every record now in the topic whose key is one of <paramref name="disputeIds"/>, in partition offset order.</summary>
    public List<ConsumeResult<string, string>> RecordsFor(params Guid[] disputeIds)
    {
        HashSet<string> keys = [.. disputeIds.Select(id => id.ToString())];
        var config = new ConsumerConfig { BootstrapServers = BootstrapServers, GroupId = $"tests-{Guid.NewGuid():N}", EnableAutoCommit = false };
        using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(config).Build();
        var records = new List<ConsumeResult<string, string>>();
        for (int partition = 0; partition < 3; partition++)
        {
            var topicPartition = new TopicPartition(OutboxRelay.Topic, partition);
            WatermarkOffsets offsets = consumer.QueryWatermarkOffsets(topicPartition, TimeSpan.FromSeconds(10));
            if (offsets.High <= offsets.Low)
            {
                continue;
            }

            consumer.Assign(new TopicPartitionOffset(topicPartition, offsets.Low));
            ConsumeResult<string, string> record;
            do
            {
                record = consumer.Consume(TimeSpan.FromSeconds(10)) ?? throw new TimeoutException($"Partition {partition} ended early.");
                if (keys.Contains(record.Message.Key))
                {
                    records.Add(record);
                }
            }
            while (record.Offset < offsets.High - 1);
        }

        return records;
    }

    /// <summary>The <c>disputeVersion</c> in a record's CloudEvent data.</summary>
    public static int DisputeVersion(ConsumeResult<string, string> record)
    {
        using var payload = JsonDocument.Parse(record.Message.Value);
        return payload.RootElement.GetProperty("data").GetProperty("disputeVersion").GetInt32();
    }

    /// <summary>A record's CloudEvent <c>id</c>.</summary>
    public static string CloudEventId(ConsumeResult<string, string> record)
    {
        using var payload = JsonDocument.Parse(record.Message.Value);
        return payload.RootElement.GetProperty("id").GetString() ?? "";
    }

    private static DateTimeOffset MarkerTransactionDate(DateTimeOffset at) => at.AddDays(-2);
}
