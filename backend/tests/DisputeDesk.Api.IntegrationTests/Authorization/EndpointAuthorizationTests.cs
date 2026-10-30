using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.Operations;
using DisputeDesk.Contracts.Problems;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.IntegrationTests.Authorization;

/// <summary>
/// 2.8 AC7 and AC8: deny by default (OWASP Authorization Cheat Sheet). The fallback policy is the enforcement; the
/// enumeration proves every mapped endpoint states its access explicitly, in each environment and with Scalar on or off. 5.1 AC1: each endpoint's
/// combined policy pins its credential, JwtBearer exactly on the operations routes and nowhere else (AD-8).
/// </summary>
public class EndpointAuthorizationTests(PostgresFixture postgres)
{
    private const string OperationsPrefix = "/api/v1/operations/";

    private static readonly string[] AnonymousEverywhere =
        ["/api/v1/bff/login", "/api/v1/bff/logout", "/api/v1/bff/session", "/health/live", "/health/ready"];

    // Program.cs maps the OpenAPI document and Scalar (its page and the three files the page loads) only when
    // Scalar:Enabled is true, in any environment (AD-26).
    private static readonly string[] AnonymousWhenScalarEnabled =
        ["/openapi/{documentName}.json", "/scalar/{documentName?}", "/scalar/favicon.svg", "/scalar/scalar.aspnetcore.js", "/scalar/scalar.js"];

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("Production", true)]
    public async Task MappedEndpoints_EachEnvironmentAndScalarSwitch_AreAnonymousOnlyByNameAndOtherwiseNameAPolicy(string environment, bool scalarEnabled)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment, settings: scalarEnabled ? ApiFactory.ApiReferenceOn : null);

        List<RouteEndpoint> endpoints = [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];

        string[] expectedAnonymous = scalarEnabled ? [.. AnonymousEverywhere, .. AnonymousWhenScalarEnabled] : AnonymousEverywhere;
        string[] anonymous = [.. endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Name)
            .Distinct()
            .Order(StringComparer.Ordinal)];
        Assert.Equal(expectedAnonymous.Order(StringComparer.Ordinal), anonymous);

        string[] withNeither = [.. endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null && endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(Name)];
        Assert.True(withNeither.Length == 0, "Endpoints with neither AllowAnonymous nor a policy:\n" + string.Join('\n', withNeither));

        // The antiforgery endpoint takes the default policy (no name); the customer routes name Linked, the linking routes Unlinked.
        Assert.Equal([null], AuthorizeData(endpoints, "/api/v1/bff/antiforgery").Select(data => data.Policy));
        Assert.Contains("Linked", AuthorizeData(endpoints, "/api/v1/transactions").Select(data => data.Policy));
        Assert.Contains("Unlinked", AuthorizeData(endpoints, "/api/v1/linking/start").Select(data => data.Policy));
        Assert.Contains("Unlinked", AuthorizeData(endpoints, "/api/v1/linking/verify").Select(data => data.Policy));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task FallbackProbe_EndpointNamingNoPolicy_Returns401AnonymousAnd200SignedIn(string environment)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment, configureServices: FallbackProbe.Register);
        using HttpClient anonymous = factory.CreateBrowser(new CookieContainer());
        using HttpClient signedIn = factory.CreateBrowser(new CookieContainer());
        await signedIn.SignInAsync(factory.Zitadel, $"probe-{Guid.NewGuid():N}");

        HttpResponseMessage refused = await anonymous.GetPathAsync(FallbackProbe.Path);
        HttpResponseMessage allowed = await signedIn.GetPathAsync(FallbackProbe.Path);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await refused.ReadJsonAsync();
        Assert.Equal(ProblemType.For(CommonProblems.Unauthorized), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        // The probe is a routed endpoint with no authorization metadata of its own.
        RouteEndpoint probe = Assert.Single(
            factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>(),
            endpoint => Name(endpoint) == FallbackProbe.Path);
        Assert.Null(probe.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Empty(probe.Metadata.GetOrderedMetadata<IAuthorizeData>());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task MappedEndpoints_CombinedPolicy_PinsJwtBearerExactlyUnderOperationsAndNowhereElse(string environment)
    {
        // Microsoft Learn, "Authorize with a specific scheme": the schemes an endpoint authenticates with are those of the
        // policy its IAuthorizeData combine into, as the authorization middleware combines them. Scalar on, so its
        // endpoints are in the set too.
        await using var factory = new ApiFactory(postgres.ConnectionString, environment, settings: ApiFactory.ApiReferenceOn);
        IAuthorizationPolicyProvider policies = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        List<RouteEndpoint> endpoints = [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];

        var schemesByEndpoint = new List<(string Name, IReadOnlyList<string> Schemes)>();
        foreach (RouteEndpoint endpoint in endpoints)
        {
            AuthorizationPolicy? policy = await AuthorizationPolicy.CombineAsync(policies, endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            if (policy is not null)
            {
                schemesByEndpoint.Add((Name(endpoint), policy.AuthenticationSchemes));
            }
        }

        var operations = schemesByEndpoint.Where(entry => entry.Name.StartsWith(OperationsPrefix, StringComparison.Ordinal)).ToList();
        Assert.Contains(operations, entry => entry.Name == "/api/v1/operations/disputes/{disputeId}");
        Assert.Contains(operations, entry => entry.Name == "/api/v1/operations/disputes/{disputeId}/start-review");
        Assert.Contains(operations, entry => entry.Name == "/api/v1/operations/disputes/{disputeId}/escalate");
        Assert.Contains(operations, entry => entry.Name == "/api/v1/operations/disputes/{disputeId}/resolve");
        Assert.All(operations, entry => Assert.Equal([JwtBearerDefaults.AuthenticationScheme], entry.Schemes));
        Assert.All(
            schemesByEndpoint.Except(operations),
            entry => Assert.DoesNotContain(JwtBearerDefaults.AuthenticationScheme, entry.Schemes));
    }

    [Fact]
    public async Task OperationsEndpoints_CombinedPolicy_RequiresTheOperationsActor()
    {
        // AD-9: every operations policy, whichever role it names, also requires the token's sub to map to the operations actor.
        await using var factory = new ApiFactory(postgres.ConnectionString);
        IAuthorizationPolicyProvider policies = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        List<RouteEndpoint> operations =
        [
            .. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
                .Where(endpoint => Name(endpoint).StartsWith(OperationsPrefix, StringComparison.Ordinal)),
        ];

        Assert.NotEmpty(operations);
        foreach (RouteEndpoint endpoint in operations)
        {
            AuthorizationPolicy? policy = await AuthorizationPolicy.CombineAsync(policies, endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.True(
                policy?.Requirements.OfType<OperationsActorRequirement>().Any() == true,
                $"{Name(endpoint)} has no {nameof(OperationsActorRequirement)} in its combined policy.");
        }
    }

    [Fact]
    public async Task AuthenticationSchemes_Defaults_StayTheCookieWithNoPolicySchemeOrForwarding()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        IAuthenticationSchemeProvider schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        // The default and fallback policies, which name no scheme, keep authenticating and challenging with the cookie.
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name);
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, (await schemes.GetDefaultChallengeSchemeAsync())?.Name);
        // AD-8 Rejected: no policy scheme choosing a credential per request, and no forwarding from the cookie to it.
        Assert.Equal(
            ["Bearer", "Cookies", "OpenIdConnect"],
            (await schemes.GetAllSchemesAsync()).Select(scheme => scheme.Name).Order(StringComparer.Ordinal));
        CookieAuthenticationOptions cookie = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.Null(cookie.ForwardDefaultSelector);
        Assert.Null(cookie.ForwardDefault);
    }

    private static string Name(RouteEndpoint endpoint) => endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "";

    private static IReadOnlyList<IAuthorizeData> AuthorizeData(List<RouteEndpoint> endpoints, string name) =>
        endpoints.Single(endpoint => Name(endpoint) == name).Metadata.GetOrderedMetadata<IAuthorizeData>();
}
