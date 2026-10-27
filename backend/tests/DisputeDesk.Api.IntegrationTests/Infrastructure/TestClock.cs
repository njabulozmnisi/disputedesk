namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>A clock the test moves by hand. It starts at the real time, so tokens Zitadel would issue are still valid.</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = System.GetUtcNow();

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
