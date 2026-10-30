using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// One Api and one operations tool for a test class whose tests each act on disputes of their own, committed directly in
/// the status they need. Core banking is never called: the actions read only stored events.
/// </summary>
public sealed class OperationsToolFixture(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Uri NoCoreBanking = new("http://core-banking.invalid");

    private ApiFactory? _factory;
    private HttpClient? _tool;
    private int _disputes;

    // Not microsecond-aligned, as TransactionsStub's clock, so a test sees any instant that storing would change.
    public DateTimeOffset Now { get; } = DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()).AddTicks(1_234_567);

    public ApiFactory Factory => _factory ?? throw new InvalidOperationException("Not initialised.");

    public HttpClient Tool => _tool ?? throw new InvalidOperationException("Not initialised.");

    /// <summary>A new dispute of customer A, raised an hour ago and moved to <paramref name="status"/> by operations.</summary>
    public async Task<Guid> DisputeInAsync(DisputeStatus status)
    {
        DateTimeOffset at = Now.AddHours(-1);
        Guid disputeId = await Factory.RaiseDirectlyAsync("login-a", at, $"TXN-A-8{Interlocked.Increment(ref _disputes):D3}");
        StoredEvent[] path = status switch
        {
            DisputeStatus.Submitted => [],
            DisputeStatus.UnderReview => [Next(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), disputeId, 2, at)],
            DisputeStatus.Escalated => [Next(new DisputeEscalated(DisputeStatus.Submitted, DisputeStatus.Escalated), disputeId, 2, at)],
            DisputeStatus.Resolved =>
            [
                Next(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), disputeId, 2, at),
                Next(new DisputeResolved(DisputeStatus.UnderReview, DisputeStatus.Resolved, Outcome.Upheld, "Refunded"), disputeId, 3, at),
            ],
            DisputeStatus.Withdrawn =>
            [
                Next(new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, null), disputeId, 2, at) with { ActorType = ActorType.Customer, ActorId = "login-a" },
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped status."),
        };
        if (path.Length > 0)
        {
            await Factory.AppendAsync(disputeId, status, status == DisputeStatus.Resolved ? Outcome.Upheld : null, path);
        }

        return disputeId;
    }

    /// <summary>The audit entries about <paramref name="disputeId"/>, oldest first.</summary>
    public async Task<List<AuditLogEntry>> AuditRowsAsync(Guid disputeId)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLogEntry>().AsNoTracking()
            .Where(entry => entry.DisputeId == disputeId)
            .OrderBy(entry => entry.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask InitializeAsync()
    {
        _factory = await TransactionsApi.StartAsync(postgres, NoCoreBanking, Now);
        _tool = _factory.CreateTool();
    }

    public async ValueTask DisposeAsync()
    {
        _tool?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private static TEvent Next<TEvent>(TEvent storedEvent, Guid disputeId, int version, DateTimeOffset at)
        where TEvent : StoredEvent =>
        storedEvent with { DisputeId = disputeId, Version = version, OccurredAtUtc = at, ActorType = ActorType.Operations, ActorId = "ops-a" };
}
