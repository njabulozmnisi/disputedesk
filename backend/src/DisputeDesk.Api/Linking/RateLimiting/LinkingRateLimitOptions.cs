namespace DisputeDesk.Api.Linking.RateLimiting;

/// <summary>The <c>Linking:RateLimits</c> configuration section (AD-26): the two sliding windows on the linking routes (FR-8).</summary>
public sealed class LinkingRateLimitOptions
{
    public const string SectionName = "Linking:RateLimits";

    public const int MaximumPermits = 1000;

    public const int MaximumSegments = 60;

    public static readonly TimeSpan MaximumWindow = TimeSpan.FromHours(1);

    /// <summary>Start and verify requests per login in any window, 1 to <see cref="MaximumPermits"/>.</summary>
    public int PermitsPerLogin { get; set; } = 5;

    /// <summary>Start and verify requests per client IP (an IPv6 /64) in any window, 1 to <see cref="MaximumPermits"/>.</summary>
    public int PermitsPerIp { get; set; } = 20;

    /// <summary>The window both limits slide over, more than zero and at most <see cref="MaximumWindow"/>.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How many segments the window slides by, 1 to <see cref="MaximumSegments"/>, each a whole number of seconds. A
    /// permit comes back when the segment it was taken in leaves the window (Microsoft Learn, sliding window limiter).
    /// </summary>
    public int SegmentsPerWindow { get; set; } = 15;

    /// <summary>True when the window splits into <see cref="SegmentsPerWindow"/> segments of whole seconds.</summary>
    public bool HasWholeSecondSegments() =>
        SegmentsPerWindow > 0 && Window.Ticks % (SegmentsPerWindow * TimeSpan.TicksPerSecond) == 0;
}
