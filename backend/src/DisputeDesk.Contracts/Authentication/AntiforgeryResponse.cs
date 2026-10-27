namespace DisputeDesk.Contracts.Authentication;

/// <summary>The body of <c>GET /api/v1/antiforgery</c>: the value the SPA sends in the <c>X-XSRF-TOKEN</c> header.</summary>
public sealed record AntiforgeryResponse(string RequestToken);
