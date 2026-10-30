using System.Net;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AC1, AD-8: each credential works only on its own endpoint group. The session cookie gets 401 on the operations
/// read with the Bearer challenge, and a valid operations token gets the cookie scheme's 401, with no challenge header,
/// on a customer route, on the default-policy antiforgery endpoint and on an unmatched path under the operations prefix.
/// Never a redirect. <see cref="Authorization.EndpointAuthorizationTests"/> proves the same for every mapped endpoint.
/// </summary>
public class OperationsCredentialTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task ReadDispute_SessionCookieOnly_Returns401WithTheBearerChallenge()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();

        HttpResponseMessage response = await browser.GetPathAsync($"{OperationsApi.DisputesPath}/{disputeId}");

        await AssertUnauthorizedAsync(response);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).ToString());
    }

    [Theory]
    [InlineData("/api/v1/disputes/{0}")]                  // a customer route (Linked, cookie only)
    [InlineData("/api/v1/bff/antiforgery")]               // the default policy
    [InlineData("/api/v1/operations/disputes/{0}/notes")] // no endpoint under the operations prefix: the fallback policy
    [InlineData("/api/v1/operations/unknown")]
    public async Task CookieRoute_ValidOperationsTokenOnly_Returns401WithoutAChallengeHeader(string pathFormat)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();
        string token = factory.Zitadel.Issue(roles: OperationsApi.ReadRole);
        // The same token reads the dispute on its own route.
        Assert.Equal(HttpStatusCode.OK, (await tool.ReadDisputeAsync(disputeId.ToString(), token)).StatusCode);

        HttpResponseMessage response = await tool.GetWithAsync(string.Format(null, pathFormat, disputeId), $"Bearer {token}");

        await AssertUnauthorizedAsync(response);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    private static async Task AssertUnauthorizedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(ProblemType.For(CommonProblems.Unauthorized), await response.ProblemTypeAsync());
    }
}
