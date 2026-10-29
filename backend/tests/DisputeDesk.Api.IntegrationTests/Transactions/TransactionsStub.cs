using DisputeDesk.Api.IntegrationTests.CoreBanking;

namespace DisputeDesk.Api.IntegrationTests.Transactions;

/// <summary>
/// The real core-banking stub with its clock fixed at the moment the fixture starts. The Api under test reads the same
/// instant, so a fixture's <c>daysAgo</c> means the same to both. It is today, not <see cref="CoreBankingStubFactory.Now"/>,
/// because the sign-in cookies expire 15 minutes after the Api's clock and a browser drops a cookie already expired.
/// </summary>
public sealed class TransactionsStub : IAsyncDisposable
{
    private readonly CoreBankingStubFactory _stub;

    public TransactionsStub()
    {
        // .1234567 s past the second is a tick finer than the microseconds Postgres keeps, so a test sees any instant
        // that storing would change; the raise stamps it as .123456 s.
        var second = DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds());
        Now = second.AddTicks(1_234_567);
        NowAsStored = second.AddTicks(1_234_560);
        _stub = new CoreBankingStubFactory { ClockNow = Now };
    }

    /// <summary>The instant the stub and the Api read.</summary>
    public DateTimeOffset Now { get; }

    /// <summary><see cref="Now"/> at the whole microseconds a raise stamps it with.</summary>
    public DateTimeOffset NowAsStored { get; }

    public Uri BaseUrl => _stub.BaseUrl;

    public ValueTask DisposeAsync() => _stub.DisposeAsync();
}
