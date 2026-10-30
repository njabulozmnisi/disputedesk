namespace DisputeDesk.Api.Linking.RateLimiting;

/// <summary>
/// Endpoint metadata on the linking group: the global limiter limits the endpoints that carry it and no others. Matching
/// the routed endpoint, not a path string, covers every casing of the path.
/// </summary>
public sealed class LinkingRateLimitMetadata
{
    public static LinkingRateLimitMetadata Instance { get; } = new();

    private LinkingRateLimitMetadata()
    {
    }
}
