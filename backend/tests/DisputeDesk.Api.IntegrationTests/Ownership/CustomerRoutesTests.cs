using DisputeDesk.Api.Authorization;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Ownership;

/// <summary>
/// AC5: every customer route requires the <c>Linked</c> policy, checked against a list this test owns rather than
/// against the endpoints that already require it, so a new customer route missing from the list, or a listed route
/// without <c>Linked</c>, fails (Microsoft Learn, Routing in ASP.NET Core: <see cref="EndpointDataSource"/>). Story 3.9
/// added <c>GET /api/v1/disputes</c>; story 3.4 adds the withdraw.
/// </summary>
public class CustomerRoutesTests(PostgresFixture postgres)
{
    private static readonly string[] ListedRoutes =
    [
        "GET /api/v1/transactions",
        "POST /api/v1/disputes",
        "GET /api/v1/disputes",
        "GET /api/v1/disputes/{disputeId}",
    ];

    [Fact]
    public async Task CustomerRoutes_EachListedRoute_ExistsAndRequiresLinked()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        IReadOnlyList<RouteEndpoint> endpoints = Endpoints(factory);

        foreach (string route in ListedRoutes)
        {
            RouteEndpoint endpoint = Assert.Single(endpoints, candidate => Route(candidate) == route);
            Assert.True(RequiresLinked(endpoint), $"{route} does not require the {CustomerRoutes.LinkedPolicy} policy.");
        }
    }

    [Fact]
    public async Task EndpointsRequiringLinked_AreExactlyTheListedCustomerRoutes()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);

        IEnumerable<string> requiringLinked = Endpoints(factory).Where(RequiresLinked).Select(Route);

        Assert.Equal(ListedRoutes.Order(StringComparer.Ordinal), requiringLinked.Order(StringComparer.Ordinal));
    }

    private static List<RouteEndpoint> Endpoints(ApiFactory factory) =>
        [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];

    /// <summary>The method and route pattern, such as <c>GET /api/v1/transactions</c>; each endpoint here has one method.</summary>
    private static string Route(RouteEndpoint endpoint)
    {
        string methods = string.Join(',', endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        return $"{methods} {endpoint.RoutePattern.RawText}";
    }

    private static bool RequiresLinked(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(authorize => authorize.Policy == CustomerRoutes.LinkedPolicy);
}
