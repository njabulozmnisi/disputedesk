namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The clock a test Api or stub host runs on: its reading stays where the test puts it, but its timers run in real time,
/// because the host's own waits must still end (the resilience handler's timeouts and delays, the migrator's retry delay,
/// the expired-session clean-up). <c>FakeTimeProvider</c> would fire those only when the test advanced it, so it is used
/// only where no host timer runs. By default it starts at <see cref="TestInstants.NotMicrosecondAligned"/>, today, so
/// tokens the fake Zitadel issues are still valid.
/// </summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public TestClock()
        : this(TestInstants.NotMicrosecondAligned())
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>The clock's reading at the whole microseconds a Postgres timestamptz keeps.</summary>
    public DateTimeOffset NowAsStored => TestInstants.AsStored(_now);

    public void Advance(TimeSpan by) => _now += by;
}
