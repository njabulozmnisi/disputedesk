using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using DisputeDesk.Api.Errors;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Linking;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Domain.Disputes;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.Linking.RateLimiting;

/// <summary>
/// The linking rate limits (FR-8, AD-12): ASP.NET Core's rate limiting middleware with a per-client-IP sliding window
/// chained before a per-login one as the global limiter, and no limiter outside the linking routes (Microsoft Learn,
/// "Rate limiting middleware in ASP.NET Core": sliding window limiter, create chained limiters). Chained limiters run in
/// order and a time-based limiter keeps a permit it granted, so a request the login limit rejects has spent IP permits:
/// two, because the middleware asks the global limiter twice (AttemptAcquire, then AcquireAsync) before it rejects.
/// Limits are held in memory on the one Api instance.
/// </summary>
public static partial class LinkingRateLimits
{
    /// <summary>
    /// The partition key of every request outside the linking routes. It is not ASCII, so no IP partition and no <c>sub</c>
    /// (ASCII, OpenID Connect Core 1.0 §2) can equal it and share its unlimited limiter.
    /// </summary>
    private const string NotLinkingPartition = "not linking ∅";

    public static IServiceCollection AddLinkingRateLimits(this IServiceCollection services)
    {
        // The error names the key, never its value (Microsoft Learn, Options pattern: options validation).
        services.AddOptions<LinkingRateLimitOptions>()
            .BindConfiguration(LinkingRateLimitOptions.SectionName)
            .Validate(
                options => options.PermitsPerLogin is >= 1 and <= LinkingRateLimitOptions.MaximumPermits,
                $"Linking:RateLimits:PermitsPerLogin must be from 1 to {LinkingRateLimitOptions.MaximumPermits}.")
            .Validate(
                options => options.PermitsPerIp is >= 1 and <= LinkingRateLimitOptions.MaximumPermits,
                $"Linking:RateLimits:PermitsPerIp must be from 1 to {LinkingRateLimitOptions.MaximumPermits}.")
            .Validate(
                options => options.Window > TimeSpan.Zero && options.Window <= LinkingRateLimitOptions.MaximumWindow,
                "Linking:RateLimits:Window must be more than 0 and at most 1 hour.")
            .Validate(
                options => options.SegmentsPerWindow is >= 1 and <= LinkingRateLimitOptions.MaximumSegments && options.HasWholeSecondSegments(),
                $"Linking:RateLimits:SegmentsPerWindow must be from 1 to {LinkingRateLimitOptions.MaximumSegments} and split the window into whole seconds.")
            .ValidateOnStart();

        services.AddSingleton<FirstRejections>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = RejectAsync;
        });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<LinkingRateLimitOptions>>((options, limits) => options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PerPartition(http => IpPartition(LinkingEndpoints.ClientIp(http)), limits.Value.PermitsPerIp, limits.Value),
                PerPartition(LoginPartition, limits.Value.PermitsPerLogin, limits.Value)));
        return services;
    }

    /// <summary>
    /// The partition of a client IP: an IPv4 address as itself, an IPv6 address as its /64 prefix, which one subscriber
    /// usually holds whole (AD-23). The address is already IPv4 when the socket reported it IPv4-mapped. Connections with
    /// no IP address (a Unix socket) share the one partition <c>""</c>.
    /// </summary>
    private static string IpPartition(IPAddress? clientIp)
    {
        if (clientIp is not { AddressFamily: AddressFamily.InterNetworkV6 })
        {
            return clientIp?.ToString() ?? "";
        }

        byte[] prefix = clientIp.GetAddressBytes();
        Array.Clear(prefix, 8, 8);
        return $"{new IPAddress(prefix)}/64";
    }

    /// <summary>A sliding-window limiter per partition key on the linking routes, none elsewhere.</summary>
    private static PartitionedRateLimiter<HttpContext> PerPartition(Func<HttpContext, string> partitionKey, int permits, LinkingRateLimitOptions limits) =>
        PartitionedRateLimiter.Create<HttpContext, string>(http =>
            http.GetEndpoint()?.Metadata.GetMetadata<LinkingRateLimitMetadata>() is null
                ? RateLimitPartition.GetNoLimiter(NotLinkingPartition)
                : RateLimitPartition.GetSlidingWindowLimiter(partitionKey(http), _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = limits.Window,
                    SegmentsPerWindow = limits.SegmentsPerWindow,
                    // A rejected request waits for nothing and holds nothing.
                    QueueLimit = 0,
                }));

    /// <summary>The login's <c>sub</c>: the limiter runs after authorization, which let only a signed-in login through.</summary>
    private static string LoginPartition(HttpContext http) =>
        http.User.FindFirst("sub")?.Value ?? throw new InvalidOperationException("The signed-in principal has no sub claim.");

    /// <summary>
    /// Answers a rejection by either limiter alike: the <c>rate-limited</c> problem (AD-3; RFC 6585 §4) and
    /// <c>Retry-After</c> as the window length in whole seconds (RFC 9110 §10.2.3), because a sliding-window lease carries
    /// no retry-after metadata and the window is the one wait that is always long enough. Audits only the first rejection
    /// of the IP or the login in the window.
    /// </summary>
    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        HttpContext http = context.HttpContext;
        TimeSpan window = http.RequestServices.GetRequiredService<IOptions<LinkingRateLimitOptions>>().Value.Window;
        IPAddress? clientIp = LinkingEndpoints.ClientIp(http);
        string loginSub = LoginPartition(http);
        if (http.RequestServices.GetRequiredService<FirstRejections>().Record(IpPartition(clientIp), loginSub, window))
        {
            await RecordAuditAsync(http.RequestServices, loginSub, clientIp);
        }

        http.Response.Headers.RetryAfter = Math.Ceiling(window.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        await LinkingErrors.RateLimited.ToProblem().ExecuteAsync(http);
    }

    /// <summary>
    /// Records and saves <c>LinkingRateLimited</c>. A failed save leaves the 429 unchanged and logs only the action: a
    /// database error can quote the row, which holds the login and the address.
    /// </summary>
    private static async Task RecordAuditAsync(IServiceProvider services, string loginSub, IPAddress? clientIp)
    {
        IAppDbContext db = services.GetRequiredService<IAppDbContext>();
        DateTimeOffset now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        AuditLog.Record(db, AuditAction.LinkingRateLimited, AuditResult.Rejected, new Actor(ActorType.Customer, loginSub), now, clientIp: clientIp);
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            LogAuditFailed(services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(LinkingRateLimits)), AuditAction.LinkingRateLimited);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the {Action} audit entry of a rate-limited linking request failed")]
    private static partial void LogAuditFailed(ILogger logger, AuditAction action);
}
