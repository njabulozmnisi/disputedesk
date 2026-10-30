using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Linking.RateLimits;

/// <summary>
/// An Api with the production rate limits (5 per login, 20 per client IP) over a window of the test's choosing, and a
/// client address the test can change between requests.
/// </summary>
public sealed class RateLimitApi : IAsyncDisposable
{
    /// <summary>A window no test outlasts, in 15 segments of whole seconds like the production one.</summary>
    public const string LongWindow = "00:01:00";

    private readonly ClientAddressStartupFilter _clientAddress;

    private RateLimitApi(ApiFactory factory, LinkingFakes.Ports ports, ClientAddressStartupFilter clientAddress)
    {
        Factory = factory;
        Ports = ports;
        _clientAddress = clientAddress;
    }

    public ApiFactory Factory { get; }

    public LinkingFakes.Ports Ports { get; }

    /// <summary>The address of every request from now on; null for a connection with no IP address.</summary>
    public IPAddress? ClientAddress
    {
        get => _clientAddress.Address;
        set => _clientAddress.Address = value;
    }

    public static async Task<RateLimitApi> StartAsync(
        PostgresFixture postgres,
        string window = LongWindow,
        string segments = "15",
        Action<IServiceCollection>? configureServices = null)
    {
        var ports = new LinkingFakes.Ports();
        var clientAddress = new ClientAddressStartupFilter(ApiFactory.ClientAddress);
        ApiFactory factory = await LinkingApi.StartApiAsync(
            postgres,
            ports,
            new TestClock(),
            services =>
            {
                // Registered after ApiFactory's filter, so it runs later and its address wins.
                services.AddTransient<IStartupFilter>(_ => clientAddress);
                configureServices?.Invoke(services);
            },
            new Dictionary<string, string?>
            {
                ["Linking:RateLimits:PermitsPerLogin"] = "5",
                ["Linking:RateLimits:PermitsPerIp"] = "20",
                ["Linking:RateLimits:Window"] = window,
                ["Linking:RateLimits:SegmentsPerWindow"] = segments,
            });
        return new RateLimitApi(factory, ports, clientAddress);
    }

    /// <summary>A verify whose code is malformed: 400 before any handler work, but it takes a permit like any other.</summary>
    public static Task<HttpResponseMessage> MalformedVerifyAsync(HttpClient browser) => browser.VerifyAsync(VerifyApi.Body("x"));

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}
