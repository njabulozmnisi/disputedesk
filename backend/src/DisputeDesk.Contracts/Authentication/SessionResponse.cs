namespace DisputeDesk.Contracts.Authentication;

/// <summary>The body of <c>GET /api/v1/authentication/session</c>.</summary>
public sealed record SessionResponse(SessionState State);
