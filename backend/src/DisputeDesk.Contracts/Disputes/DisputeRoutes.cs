namespace DisputeDesk.Contracts.Disputes;

/// <summary>
/// The dispute routes. The detail is in one place for the endpoint that serves it and the raise that names it in its
/// <c>Location</c>, so that <c>Location</c> always points to a real GET (AD-5).
/// </summary>
public static class DisputeRoutes
{
    /// <summary>The history list's route pattern, relative to the <c>/api/v1</c> group it is mapped on.</summary>
    public const string List = "/disputes";

    /// <summary>The detail's route pattern, relative to the <c>/api/v1</c> group it is mapped on.</summary>
    public const string Detail = "/disputes/{disputeId}";

    /// <summary>The path of the detail of <paramref name="disputeId"/>, as the raise's <c>Location</c> carries it.</summary>
    public static string DetailPath(Guid disputeId) => $"/api/v1/disputes/{disputeId}";
}
