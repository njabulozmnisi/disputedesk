namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>A clock the test moves by hand. It starts at the real time, so tokens Zitadel would issue are still valid.</summary>
public sealed class TestClock : TimeProvider
{
    // .1234567 s past the second is a tick finer than the microseconds Postgres keeps, so a test sees any instant that
    // storing would change, on every platform's clock.
    private DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(System.GetUtcNow().ToUnixTimeSeconds()).AddTicks(1_234_567);

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>The clock's reading at the whole microseconds a Postgres timestamptz keeps.</summary>
    public DateTimeOffset NowAsStored => _now.AddTicks(-(_now.Ticks % TimeSpan.TicksPerMicrosecond));

    public void Advance(TimeSpan by) => _now += by;
}
