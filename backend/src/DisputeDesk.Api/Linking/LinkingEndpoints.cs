using System.Net;
using DisputeDesk.Application.Linking.StartLinking;
using DisputeDesk.Contracts.Linking;
using DisputeDesk.Infrastructure.Linking;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DisputeDesk.Api.Linking;

/// <summary><c>POST /api/v1/linking/start</c>, on the linking routes.</summary>
public static class LinkingEndpoints
{
    public static IServiceCollection AddLinkingStart(this IServiceCollection services) =>
        services.AddScoped<StartLinkingHandler>();

    public static RouteGroupBuilder MapLinkingStart(this RouteGroupBuilder linkingRoutes)
    {
        linkingRoutes.MapPost("/start", StartAsync)
            .ProducesValidationProblem();
        return linkingRoutes;
    }

    /// <summary>
    /// Starts linking the signed-in, unlinked login to the customer holding this ID number and account number. Every
    /// well-formed start gets 202 with an empty body, whether or not the details match a customer: the checks and the OTP
    /// to the cellphone on the bank's record happen afterwards. 400 <c>validation</c> names the field and never repeats
    /// its value.
    /// </summary>
    /// <remarks>
    /// The checks run 401, 403 (<c>already-linked</c> for a linked login), then 400 (AD-8): the <c>Unlinked</c> policy
    /// runs in the authorization middleware, before binding. A start replaces the login's earlier one.
    /// </remarks>
    private static async Task<Accepted> StartAsync(
        StartLinkingRequest request,
        StartLinkingHandler handler,
        LinkingMetrics metrics,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        // The Unlinked policy has checked this login; the handler takes its sub, never the principal (AD-10).
        string loginSub = http.User.FindFirst("sub")?.Value ?? throw new InvalidOperationException("The signed-in principal has no sub claim.");
        await handler.HandleAsync(request, loginSub, ClientIp(http), cancellationToken);
        metrics.RecordStarted();
        return TypedResults.Accepted((string?)null);
    }

    /// <summary>
    /// The connection's address, as an IPv4 address when a dual-stack socket reports it IPv4-mapped. Only the connection
    /// counts: forwarded headers are trusted from story 6.3 (AD-23).
    /// </summary>
    private static IPAddress? ClientIp(HttpContext http)
    {
        IPAddress? address = http.Connection.RemoteIpAddress;
        return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
    }
}
