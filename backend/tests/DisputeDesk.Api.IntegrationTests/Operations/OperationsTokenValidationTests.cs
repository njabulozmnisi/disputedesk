using System.Net;
using System.Security.Cryptography;
using System.Text;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Problems;
using Microsoft.IdentityModel.Tokens;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AC2: the <c>Authorization</c> header is the operations read's one credential input. Each missing, malformed or
/// invalid token gets 401 <c>unauthorized</c> with <c>WWW-Authenticate: Bearer</c> and never an <c>error_description</c>
/// (RFC 6750 §3; RFC 7519 §4.1.4, §4.1.5), and writes no audit row. Tokens are minted from the system clock, which is
/// the clock JwtBearer 10.0.14 validates against: its handler does not pass the injected <see cref="TimeProvider"/> to
/// <c>TokenValidationParameters.TimeProvider</c>.
/// </summary>
public class OperationsTokenValidationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ReadDispute_NoAuthorizationHeader_Returns401WithABareBearerChallenge()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient tool = factory.CreateTool();
        var disputeId = Guid.NewGuid();

        HttpResponseMessage response = await tool.ReadDisputeAsync(disputeId.ToString(), token: null);

        await AssertUnauthorizedAsync(response);
        // RFC 6750 §3.1: no error code when the request carries no authentication information.
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).ToString());
        Assert.Empty(await factory.DisputeReadEntriesAsync(disputeId));
    }

    [Theory]
    [InlineData("non-bearer scheme")]
    [InlineData("empty bearer")]
    [InlineData("not a JWT")]
    [InlineData("wrong issuer")]
    [InlineData("wrong audience")]
    [InlineData("wrong signature")]
    [InlineData("unknown kid")]
    [InlineData("alg none")]
    [InlineData("HS256 with a symmetric key")]
    [InlineData("RS512 with the real key")]
    [InlineData("PS256 with the real key")]
    [InlineData("no exp")]
    [InlineData("exp more than the clock skew ago")]
    [InlineData("nbf more than the clock skew ahead")]
    public async Task ReadDispute_InvalidCredential_Returns401BearerWithoutErrorDescriptionAndNoAuditRow(string credential)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient tool = factory.CreateTool();
        var disputeId = Guid.NewGuid();

        HttpResponseMessage response = await tool.GetWithAsync($"{OperationsApi.DisputesPath}/{disputeId}", Authorization(factory.Zitadel, credential));

        await AssertUnauthorizedAsync(response);
        string challenge = Assert.Single(response.Headers.WwwAuthenticate).ToString();
        Assert.StartsWith("Bearer", challenge, StringComparison.Ordinal);
        Assert.DoesNotContain("error_description", challenge, StringComparison.Ordinal);
        Assert.Empty(await factory.DisputeReadEntriesAsync(disputeId));
    }

    /// <summary>The <c>Authorization</c> value for one case: a token valid in every way but the one named.</summary>
    private static string Authorization(FakeZitadel zitadel, string credential)
    {
        SecurityTokenDescriptor token = zitadel.AccessToken(roles: OperationsApi.ReadRole);
        DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
        switch (credential)
        {
            case "non-bearer scheme":
                return $"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes("ops:not-a-password"))}";
            case "empty bearer":
                return "Bearer ";
            case "not a JWT":
                return "Bearer not-a-jwt";
            case "wrong issuer":
                token.Issuer = "https://other-issuer.test";
                break;
            case "wrong audience":
                token.Audience = "other-project";
                break;
            case "wrong signature":
                // The fake's key ID, but another key: the signature does not verify.
                token.SigningCredentials = new SigningCredentials(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" }, SecurityAlgorithms.RsaSha256);
                break;
            case "unknown kid":
                token.SigningCredentials = new SigningCredentials(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "unknown-key" }, SecurityAlgorithms.RsaSha256);
                break;
            case "alg none":
                return $"Bearer {Unsigned(FakeZitadel.Sign(token))}";
            case "HS256 with a symmetric key":
                // Algorithm confusion: a key the attacker chose, under the fake's key ID.
                token.SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "test-key" }, SecurityAlgorithms.HmacSha256);
                break;
            case "RS512 with the real key":
                // The fake's own key under an algorithm other than RS256: only ValidAlgorithms rejects it.
                token.SigningCredentials = new SigningCredentials(token.SigningCredentials.Key, SecurityAlgorithms.RsaSha512);
                break;
            case "PS256 with the real key":
                token.SigningCredentials = new SigningCredentials(token.SigningCredentials.Key, SecurityAlgorithms.RsaSsaPssSha256);
                break;
            case "no exp":
                token.Expires = null;
                break;
            case "exp more than the clock skew ago":
                // The documented default ClockSkew is 5 minutes.
                token.IssuedAt = now.AddMinutes(-20);
                token.NotBefore = now.AddMinutes(-20);
                token.Expires = now.AddMinutes(-6);
                break;
            case "nbf more than the clock skew ahead":
                token.NotBefore = now.AddMinutes(6);
                token.Expires = now.AddMinutes(20);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(credential), credential, "Unknown case.");
        }

        return $"Bearer {FakeZitadel.Sign(token)}";
    }

    /// <summary>The token's claims under an <c>alg: none</c> header with the signature dropped (RFC 7519 §6).</summary>
    private static string Unsigned(string token)
    {
        string payload = token.Split('.')[1];
        return $"{Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""")}.{payload}.";
    }

    private static async Task AssertUnauthorizedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Unauthorized), await response.ProblemTypeAsync());
    }
}
