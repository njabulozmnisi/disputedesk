using DisputeDesk.Api.IntegrationTests.CoreBanking;

namespace DisputeDesk.Api.IntegrationTests.Transactions;

/// <summary>
/// The real core-banking stub with its clock fixed at the moment the fixture starts. The Api under test reads the same
/// instant, so a fixture's <c>daysAgo</c> means the same to both. It is today, not <see cref="CoreBankingStubFactory.Now"/>,
/// because the sign-in cookies expire 15 minutes after the Api's clock and a browser drops a cookie already expired.
/// </summary>
public sealed class TransactionsStub : IAsyncDisposable
{
    // South Africa has no daylight saving time, so its offset is always UTC+02:00 (glossary: transaction date).
    private static readonly TimeSpan SouthAfricanOffset = TimeSpan.FromHours(2);

    private readonly CoreBankingStubFactory _stub;

    // .1234567 s past the second is a tick finer than the microseconds Postgres keeps, so a test sees any instant
    // that storing would change; the raise stamps it as .123456 s.
    public TransactionsStub()
        : this(DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()).AddTicks(1_234_567))
    {
    }

    /// <summary>The stub with its clock fixed at <paramref name="now"/>, for a test that needs a chosen instant.</summary>
    internal TransactionsStub(DateTimeOffset now)
    {
        Now = now;
        NowAsStored = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
        _stub = new CoreBankingStubFactory { ClockNow = Now };
    }

    /// <summary>The instant the stub and the Api read.</summary>
    public DateTimeOffset Now { get; }

    /// <summary><see cref="Now"/> at the whole microseconds a raise stamps it with.</summary>
    public DateTimeOffset NowAsStored { get; }

    public Uri BaseUrl => _stub.BaseUrl;

    /// <summary>
    /// The date the stub gives a fixture transaction: <paramref name="daysAgo"/> days before the start of the South
    /// African day <see cref="Now"/> falls in, at <paramref name="time"/> South African time. Between 22:00 and 24:00
    /// UTC that day is already tomorrow's UTC date.
    /// </summary>
    public DateTimeOffset FixtureTransactionDate(int daysAgo, TimeOnly time)
    {
        var startOfSouthAfricanToday = new DateTimeOffset(Now.ToOffset(SouthAfricanOffset).Date, SouthAfricanOffset);
        return startOfSouthAfricanToday.AddDays(-daysAgo).Add(time.ToTimeSpan());
    }

    public ValueTask DisposeAsync() => _stub.DisposeAsync();
}
