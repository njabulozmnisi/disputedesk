using DisputeDesk.Api.IntegrationTests.CoreBanking;

namespace DisputeDesk.Api.IntegrationTests.Transactions;

/// <summary>
/// The real core-banking stub with its clock fixed at the moment the fixture starts. The Api under test reads the same
/// instant, so a fixture's <c>daysAgo</c> means the same to both. It is today, not <see cref="CoreBankingStubFactory.Now"/>,
/// because the sign-in cookies expire 15 minutes after the Api's clock and a browser drops a cookie already expired.
/// </summary>
public sealed class TransactionsStub : IAsyncDisposable
{
    public DateTimeOffset Now { get; } = TimeProvider.System.GetUtcNow();

    private readonly CoreBankingStubFactory _stub;

    public TransactionsStub() => _stub = new CoreBankingStubFactory { ClockNow = Now };

    public Uri BaseUrl => _stub.BaseUrl;

    public ValueTask DisposeAsync() => _stub.DisposeAsync();
}
