namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>The instants the test clocks start at, and the value Postgres stores for one.</summary>
public static class TestInstants
{
    /// <summary>
    /// Now, at .1234567 s past the second: a tick finer than the microseconds Postgres keeps, so a test sees any instant
    /// that storing would change, on every platform's clock (macOS clocks read whole microseconds; story 2.8).
    /// </summary>
    public static DateTimeOffset NotMicrosecondAligned() =>
        DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()).AddTicks(1_234_567);

    /// <summary><paramref name="instant"/> at the whole microseconds a Postgres <c>timestamptz</c> keeps.</summary>
    public static DateTimeOffset AsStored(DateTimeOffset instant) =>
        instant.AddTicks(-(instant.Ticks % TimeSpan.TicksPerMicrosecond));
}
