using System.Net;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;
using Microsoft.IdentityModel.Tokens;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AC3, AD-9: a valid token reads only with the <c>dispute.read</c> role under this project's roles claim and a
/// <c>sub</c> listed in <c>Operations:AllowedClients</c>. Every other valid token gets 403 <c>forbidden</c>, with the same
/// body for a real and an unknown dispute ID apart from <c>traceId</c> (FR-24: 403 before 404). The list's own edges are
/// in <see cref="OperationsAllowlistTests"/>.
/// </summary>
public class OperationsRolesTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private const string CustomerLogin = "login-a";

    [Theory]
    [InlineData("no roles")]
    [InlineData("dispute.read only under the generic roles claim")]
    [InlineData("dispute.read only as a role claim of its own")]
    [InlineData("dispute.read with an unlisted sub")]
    [InlineData("customer ID token, no roles")]
    [InlineData("customer ID token with dispute.read")]
    public async Task ReadDispute_ValidTokenNotAllowedToRead_Returns403WithTheSameBodyForARealAndAnUnknownId(string token)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();
        string signed = FakeZitadel.Sign(Token(factory.Zitadel, token));

        HttpResponseMessage real = await tool.ReadDisputeAsync(disputeId.ToString(), signed);
        HttpResponseMessage unknown = await tool.ReadDisputeAsync(Guid.NewGuid().ToString(), signed);

        Assert.Equal(HttpStatusCode.Forbidden, real.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Forbidden), await real.ProblemTypeAsync());
        Assert.Equal(await real.BodyWithoutTraceIdAsync(), await unknown.BodyWithoutTraceIdAsync());
    }

    [Fact]
    public async Task ReadDispute_ListedTokenWithDisputeRead_Returns200()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage response = await tool.ReadDisputeAsync(disputeId.ToString(), factory.Zitadel.Issue(roles: OperationsApi.ReadRole));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The valid token for one case; each would read if it carried the role and a listed <c>sub</c>.</summary>
    private static SecurityTokenDescriptor Token(FakeZitadel zitadel, string token)
    {
        switch (token)
        {
            case "no roles":
                return zitadel.AccessToken();
            case "dispute.read only under the generic roles claim":
                // Not bound to this project, so another project's roles could arrive under it (AD-9; ASVS 5.0 V10.3.2).
                SecurityTokenDescriptor generic = zitadel.AccessToken();
                generic.Claims["urn:zitadel:iam:org:project:roles"] = FakeZitadel.RolesValue(OperationsApi.ReadRole);
                return generic;
            case "dispute.read only as a role claim of its own":
                SecurityTokenDescriptor roleClaim = zitadel.AccessToken();
                roleClaim.Claims["role"] = OperationsApi.ReadRole;
                return roleClaim;
            case "dispute.read with an unlisted sub":
                return zitadel.AccessToken("unlisted-machine-user", OperationsApi.ReadRole);
            case "customer ID token, no roles":
                return CustomerIdToken(zitadel);
            case "customer ID token with dispute.read":
                return CustomerIdToken(zitadel, OperationsApi.ReadRole);
            default:
                throw new ArgumentOutOfRangeException(nameof(token), token, "Unknown case.");
        }
    }

    /// <summary>
    /// A token shaped like the web app's ID token for a customer: the same issuer, the web app client and the project as
    /// audience, the customer's login as <c>sub</c>, and the ID token's own claims.
    /// </summary>
    private static SecurityTokenDescriptor CustomerIdToken(FakeZitadel zitadel, params string[] roles)
    {
        SecurityTokenDescriptor idToken = zitadel.AccessToken(CustomerLogin, roles);
        idToken.Audience = null;
        idToken.Claims["aud"] = new[] { FakeZitadel.ClientId, FakeZitadel.ProjectId };
        idToken.Claims["azp"] = FakeZitadel.ClientId;
        idToken.Claims["email"] = $"{CustomerLogin}@disputedesk.test";
        idToken.Claims["email_verified"] = true;
        idToken.Claims["nonce"] = "nonce-test";
        return idToken;
    }
}
