using System.Net;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Linking;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.IntegrationTests.ForwardedHeaders;

/// <summary>
/// 6.6 AC1 (AD-23): the Api takes <c>X-Forwarded-For</c>, <c>-Proto</c> and <c>-Host</c> only from a connection inside
/// <c>ForwardedHeaders:TrustedNetwork</c>, and <c>X-Forwarded-Host</c> only when it is the public app host. The client IP
/// is the one a linking start audits (6.1); the scheme and host are those of the sign-in's <c>redirect_uri</c>.
/// </summary>
public class ForwardedHeadersTests(PostgresFixture postgres)
{
    /// <summary>A gateway address inside <see cref="ApiFactory"/>'s trusted network, IPv4-mapped as Kestrel reports it.</summary>
    private static readonly IPAddress Gateway = IPAddress.Parse("::ffff:10.201.89.5");

    [Fact]
    public async Task Start_ForwardedForFromOutsideTheTrustedNetwork_AuditsTheConnectionAddress()
    {
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, new LinkingFakes.Ports(), new TestClock());

        List<AuditLogEntry> entries = await StartWithForwardedForAsync(factory, "203.0.113.9");

        Assert.All(entries, entry => Assert.Equal(IPAddress.Parse("203.0.113.7"), entry.ClientIp));
    }

    [Theory]
    [InlineData("203.0.113.9")]
    [InlineData("192.0.2.1, 203.0.113.9")] // ForwardLimit 1: only the rightmost entry, the one the gateway added
    public async Task Start_ForwardedForFromInsideTheTrustedNetwork_AuditsTheForwardedAddress(string forwardedFor)
    {
        await using ApiFactory factory = await LinkingApi.StartApiAsync(postgres, new LinkingFakes.Ports(), new TestClock(), FromAddress(Gateway));

        List<AuditLogEntry> entries = await StartWithForwardedForAsync(factory, forwardedFor);

        Assert.All(entries, entry => Assert.Equal(IPAddress.Parse("203.0.113.9"), entry.ClientIp));
    }

    [Fact]
    public async Task Login_ForwardedProtoAndTheAppHostFromInside_BuildTheRedirectUriFromThem()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, configureServices: FromAddress(Gateway));

        string redirectUri = await LoginRedirectUriAsync(factory, ("X-Forwarded-Proto", "http"), ("X-Forwarded-Host", "localhost:8080"));

        Assert.Equal("http://localhost:8080/api/signin-oidc", redirectUri);
    }

    [Theory]
    [InlineData("attacker.example")]
    [InlineData("attacker.example:8080")]
    [InlineData("localhost.attacker.example")]
    public async Task Login_AnotherForwardedHostFromInside_KeepsTheRequestHost(string forwardedHost)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, configureServices: FromAddress(Gateway));

        string redirectUri = await LoginRedirectUriAsync(factory, ("X-Forwarded-Host", forwardedHost));

        Assert.Equal("https://localhost/api/signin-oidc", redirectUri);
    }

    [Theory]
    [InlineData("::ffff:203.0.113.7")] // ApiFactory's client
    [InlineData("::ffff:10.201.90.5")] // the next /24
    [InlineData("::1")] // KnownProxies' default
    [InlineData("::ffff:127.0.0.1")] // KnownIPNetworks' default, 127.0.0.0/8
    public async Task Login_ForwardedProtoAndHostFromOutside_AreIgnored(string connection)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, configureServices: FromAddress(IPAddress.Parse(connection)));

        string redirectUri = await LoginRedirectUriAsync(factory, ("X-Forwarded-Proto", "http"), ("X-Forwarded-Host", "localhost:8080"));

        Assert.Equal("https://localhost/api/signin-oidc", redirectUri);
    }

    [Fact]
    public void Options_TrustOnlyTheConfiguredNetworkForOneHopOfForProtoAndHostFromTheAppHost()
    {
        using var factory = new ApiFactory(connectionString: null);

        ForwardedHeadersOptions options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        // Qualified: inside this namespace, ForwardedHeaders names the namespace, not the enum.
        Assert.Equal(
            Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
            options.ForwardedHeaders);
        Assert.Empty(options.KnownProxies);
        Assert.Equal([IPNetwork.Parse(ApiFactory.TrustedNetwork)], options.KnownIPNetworks);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Equal(["localhost"], options.AllowedHosts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-network")]
    [InlineData("10.201.89.0")] // no prefix length
    [InlineData("10.201.89.0/33")]
    [InlineData("10.201.89.0/24 ")]
    public async Task ApiStart_InvalidTrustedNetwork_StopsTheApiWithAnErrorNamingTheKey(string? network)
    {
        (int exitCode, string output) = await ApiProcess.RunUntilExitAsync(new Dictionary<string, string?> { ["ForwardedHeaders:TrustedNetwork"] = network }, TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("ForwardedHeaders:TrustedNetwork must be a network in CIDR notation", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ApiStart_EmptyAllowedHosts_StopsTheApiWithAnErrorNamingTheKey(string? host)
    {
        (int exitCode, string output) = await ApiProcess.RunUntilExitAsync(new Dictionary<string, string?> { ["ForwardedHeaders:AllowedHosts"] = host }, TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("ForwardedHeaders:AllowedHosts must hold the public app host", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("localhost:8080")] // the middleware compares the host without its port, so this never matches
    [InlineData("[::1]")]
    [InlineData("*")] // the middleware takes * as any host
    [InlineData("*.localhost")] // and *.x as any subdomain of x
    public async Task ApiStart_AllowedHostsWithAPortOrAWildcard_StopsTheApiWithAnErrorNamingTheKey(string host)
    {
        (int exitCode, string output) = await ApiProcess.RunUntilExitAsync(new Dictionary<string, string?> { ["ForwardedHeaders:AllowedHosts"] = host }, TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("ForwardedHeaders:AllowedHosts must be one host name, without a port or a wildcard", output, StringComparison.Ordinal);
    }

    /// <summary>Every request's connection address is <paramref name="address"/>, registered over ApiFactory's.</summary>
    private static Action<IServiceCollection> FromAddress(IPAddress address) =>
        services => services.AddTransient<IStartupFilter>(_ => new ClientAddressStartupFilter(address));

    private static async Task<List<AuditLogEntry>> StartWithForwardedForAsync(ApiFactory factory, string forwardedFor)
    {
        string login = LinkingApi.NewLogin("forwarded-for");
        using HttpClient browser = await factory.SignInUnlinkedAsync(login);

        await browser.StartLinkingAsync(LinkingApi.Body(LinkingFakes.NoMatch), configure: request => request.Headers.Add("X-Forwarded-For", forwardedFor));

        return await factory.WaitForAuditAsync(login, AuditAction.LinkingNoMatch, AuditResult.Rejected);
    }

    private static async Task<string> LoginRedirectUriAsync(ApiFactory factory, params (string Name, string Value)[] headers)
    {
        using HttpClient browser = factory.CreateBrowser(new CookieContainer());
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/bff/login", UriKind.Relative));
        foreach ((string name, string value) in headers)
        {
            request.Headers.Add(name, value);
        }

        HttpResponseMessage login = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Uri authorize = Assert.IsType<Uri>(login.Headers.Location);
        return QueryHelpers.ParseQuery(authorize.Query)["redirect_uri"].ToString();
    }
}
