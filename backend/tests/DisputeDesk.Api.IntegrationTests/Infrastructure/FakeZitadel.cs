using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Plays Zitadel's part in the code flow for the Api's real OpenID Connect handler: a fixed discovery document, a
/// signing key, a token endpoint and an end-session endpoint. The test acts as the browser and the hosted login: it
/// reads the authorize redirect, asks for a code here, and sends the code to the Api's callback; at sign-out it brings
/// the end-session redirect here and follows the redirect back.
/// </summary>
public sealed class FakeZitadel
{
    public const string Issuer = "https://zitadel.test";
    public const string ClientId = "bff-client";
    public const string ClientSecret = "bff-secret";

    /// <summary>The post-logout redirect URI registered for the app: the Api origin's signed-out callback path.</summary>
    public const string PostLogoutRedirectUri = "https://localhost/api/signout-callback-oidc";

    private readonly RsaSecurityKey _signingKey = new(RSA.Create(2048)) { KeyId = "test-key" };
    private readonly ConcurrentDictionary<string, PendingCode> _codes = new();

    /// <summary>Makes discovery fail, as when Zitadel is down.</summary>
    public bool DiscoveryFails { get; set; }

    /// <summary>The form of the last token request the Api sent.</summary>
    public IReadOnlyDictionary<string, string> LastTokenRequest { get; private set; } = new Dictionary<string, string>();

    /// <summary>The ID token the last successful token request received.</summary>
    public string? LastIdToken { get; private set; }

    public OpenIdConnectConfiguration Configuration { get; } = new()
    {
        Issuer = Issuer,
        AuthorizationEndpoint = $"{Issuer}/oauth/v2/authorize",
        TokenEndpoint = $"{Issuer}/oauth/v2/token",
        EndSessionEndpoint = $"{Issuer}/oidc/v1/end_session",
    };

    public FakeZitadel() => Configuration.SigningKeys.Add(_signingKey);

    /// <summary>Points the Api's OpenID Connect handler at this fake instead of a real discovery document.</summary>
    public void AddTo(IServiceCollection services)
    {
        services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, oidc =>
        {
            oidc.ConfigurationManager = new Discovery(this);
            oidc.Backchannel = new HttpClient(new TokenEndpoint(this));
        });
    }

    /// <summary>
    /// What Zitadel does after a successful hosted login: remembers the authorize request and returns a code for the
    /// login <paramref name="sub"/>.
    /// </summary>
    public string IssueCode(Uri authorizeRedirect, string sub, bool emailVerified = true)
    {
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query = QueryHelpers.ParseQuery(authorizeRedirect.Query);
        string code = Guid.NewGuid().ToString("N");
        _codes[code] = new PendingCode(sub, emailVerified, query["nonce"].ToString(), query["code_challenge"].ToString());
        return code;
    }

    /// <summary>
    /// What Zitadel's end-session endpoint does with the browser's request (OpenID Connect RP-Initiated Logout 1.0,
    /// sections 2 and 3): checks the ID token hint is the one it issued at sign-in, the client ID, a state and the
    /// registered post-logout redirect URI, then returns where it redirects the browser: that URI with the same state.
    /// </summary>
    public Uri EndSession(Uri endSessionRequest)
    {
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query = QueryHelpers.ParseQuery(endSessionRequest.Query);
        string state = query.GetValueOrDefault("state").ToString();
        if (endSessionRequest.GetLeftPart(UriPartial.Path) != Configuration.EndSessionEndpoint
            || query.GetValueOrDefault("id_token_hint").ToString() != LastIdToken
            || query.GetValueOrDefault("client_id").ToString() != ClientId
            || string.IsNullOrEmpty(state)
            || query.GetValueOrDefault("post_logout_redirect_uri").ToString() != PostLogoutRedirectUri)
        {
            throw new InvalidOperationException($"Zitadel rejects the end-session request {endSessionRequest}.");
        }

        return new Uri($"{PostLogoutRedirectUri}?state={Uri.EscapeDataString(state)}");
    }

    private string CreateIdToken(PendingCode pending)
    {
        DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = pending.Sub,
                ["nonce"] = pending.Nonce,
                ["email"] = $"{pending.Sub}@disputedesk.test",
                ["email_verified"] = pending.EmailVerified,
            },
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed record PendingCode(string Sub, bool EmailVerified, string Nonce, string CodeChallenge);

    private sealed class Discovery(FakeZitadel zitadel) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            zitadel.DiscoveryFails
                ? throw new HttpRequestException("Zitadel is down.")
                : Task.FromResult(zitadel.Configuration);

        public void RequestRefresh() { }
    }

    /// <summary>
    /// Answers the code redemption, as Zitadel does for a client_secret_post app: only when the client ID and secret
    /// are in the form body and correct, and the PKCE verifier matches the challenge.
    /// </summary>
    private sealed class TokenEndpoint(FakeZitadel zitadel) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var form = QueryHelpers.ParseQuery(body).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
            zitadel.LastTokenRequest = form;

            if (form.GetValueOrDefault("client_id") != ClientId || form.GetValueOrDefault("client_secret") != ClientSecret)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("""{"error":"invalid_client"}""", Encoding.UTF8, "application/json") };
            }

            if (request.RequestUri?.ToString() != zitadel.Configuration.TokenEndpoint
                || !zitadel._codes.TryRemove(form.GetValueOrDefault("code") ?? "", out PendingCode? pending)
                || Challenge(form.GetValueOrDefault("code_verifier") ?? "") != pending.CodeChallenge)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"invalid_grant"}""", Encoding.UTF8, "application/json") };
            }

            zitadel.LastIdToken = zitadel.CreateIdToken(pending);
            string json = $$"""{"id_token":"{{zitadel.LastIdToken}}","access_token":"opaque-access-token","token_type":"Bearer","expires_in":300}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private static string Challenge(string verifier) => Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }
}
