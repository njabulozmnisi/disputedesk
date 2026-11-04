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
    public DateTimeOffset Now { get; } = TestInstants.NotMicrosecondAligned();

    public ApiFactory Factory => _factory ?? throw new InvalidOperationException("Not initialised.");

    public HttpClient Tool => _tool ?? throw new InvalidOperationException("Not initialised.");

    /// <summary>A new dispute of customer A, raised an hour ago and moved to <paramref name="status"/> through the aggregate.</summary>
    public async Task<Guid> DisputeInAsync(DisputeStatus status)
    {
        DateTimeOffset at = Now.AddHours(-1);
        string transactionId = $"TXN-A-8{Interlocked.Increment(ref _disputes):D3}";
        return (await Factory.DisputeInAsync(status, transactionId: transactionId, raisedAt: at, actedAt: at)).Id;
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
}
