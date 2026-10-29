using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Problems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Authorization;

/// <summary>
/// 2.8 AC7 and AC8: deny by default (OWASP Authorization Cheat Sheet). The fallback policy is the enforcement; the
/// enumeration proves every mapped endpoint states its access explicitly, in each environment.
/// </summary>
public class EndpointAuthorizationTests(PostgresFixture postgres)
{
    private static readonly string[] AnonymousEverywhere =
        ["/api/v1/bff/login", "/api/v1/bff/logout", "/api/v1/bff/session", "/health/live", "/health/ready"];

    // Program.cs maps the OpenAPI document and Scalar (its page and the three files the page loads) only in Development.
    private static readonly string[] AnonymousInDevelopmentOnly =
        ["/openapi/{documentName}.json", "/scalar/{documentName?}", "/scalar/favicon.svg", "/scalar/scalar.aspnetcore.js", "/scalar/scalar.js"];

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task MappedEndpoints_EachEnvironment_AreAnonymousOnlyByNameAndOtherwiseNameAPolicy(string environment)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString, environment);

        List<RouteEndpoint> endpoints = [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];

        string[] expectedAnonymous = environment == "Development" ? [.. AnonymousEverywhere, .. AnonymousInDevelopmentOnly] : AnonymousEverywhere;
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

        // The antiforgery endpoint takes the default policy (no name); the customer routes name Linked.
        Assert.Equal([null], AuthorizeData(endpoints, "/api/v1/bff/antiforgery").Select(data => data.Policy));
        Assert.Contains("Linked", AuthorizeData(endpoints, "/api/v1/transactions").Select(data => data.Policy));
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

    private static string Name(RouteEndpoint endpoint) => endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "";

    private static IReadOnlyList<IAuthorizeData> AuthorizeData(List<RouteEndpoint> endpoints, string name) =>
        endpoints.Single(endpoint => Name(endpoint) == name).Metadata.GetOrderedMetadata<IAuthorizeData>();
}
