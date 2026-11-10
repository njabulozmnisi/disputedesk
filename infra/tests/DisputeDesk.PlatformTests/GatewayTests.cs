using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DisputeDesk.PlatformTests;

/// <summary>
/// The gateway's edge rules, by response, on both images (story 7.4 criteria 3, 4, 6 and 7; story 7.12 criteria 1 to 4,
/// 6 and 7): the one place that checks the gateway's own headers and what each upstream receives. The real Api's Scalar
/// page and Zitadel's own headers stay in infra/tests/smoke.sh, because the echo upstream sends neither.
/// </summary>
[UnsupportedOSPlatform("windows")] // the echo server's files need their Unix modes
public sealed partial class GatewayTests(GatewayFixture gateway) : IClassFixture<GatewayFixture>
{
    private const string Hsts = "max-age=63072000; includeSubDomains"; // two years (RFC 6797 6.1.1, 6.1.2)
    private const string Viewer = "198.51.100.10";
    private const string GatewayErrorBody = "The gateway could not complete the request.";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);
    private static readonly string[] AppSecurityHeaders =
        ["content-security-policy", "x-content-type-options", "x-frame-options", "referrer-policy", "permissions-policy", "cross-origin-opener-policy"];

    // ------------------------------------------------------------------ 7.4 criterion 3: the rendered AWS config

    [Fact]
    public async Task AwsGateway_RenderedConfig_PassesNginxTestAndTrustsCloudFrontOnlyFromTheIncludedFile()
    {
        ProcessResult test = await Processes.DockerAsync(CommandTimeout, "exec", $"{gateway.Run}-gateway", "nginx", "-t");
        string rendered = await RenderedAwsConfigAsync();
        string template = await File.ReadAllTextAsync(Path.Combine(SourceRoots.Repo, "infra/images/gateway/templates-aws/nginx.conf.template"), TestContext.Current.CancellationToken);

        Assert.Contains("test is successful", test.Errors, StringComparison.Ordinal);
        Assert.DoesNotMatch(UncommentedSetRealIpFrom(), template);
        // Two spaces: at http level, so for all three server blocks.
        Assert.Matches(HttpLevel("include /etc/nginx/cloudfront-realip.conf;"), rendered);
        Assert.Matches(HttpLevel("real_ip_header CloudFront-Viewer-Address;"), rendered);
        Assert.Matches(HttpLevel("real_ip_recursive off;"), rendered);
        // No location appends to a client-sent X-Forwarded-For.
        Assert.DoesNotContain("proxy_add_x_forwarded_for", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("app", "/api/realip-app")]
    [InlineData("auth", "/oauth/v2/realip-auth")]
    public async Task AwsGateway_ViewerAddressFromATrustedClient_IsTheForwardedForWithTheHostFixed(string origin, string path)
    {
        string host = origin == "app" ? gateway.AppHost : gateway.AuthHost;

        Echoed echoed = await AwsEchoedAsync("trusted", host, path, "-H", $"CloudFront-Viewer-Address: {Viewer}:46532", "-H", "CloudFront-Forwarded-Proto: https");

        Assert.Equal(host, echoed.Header("Host"));
        Assert.Equal(host, echoed.Header("X-Forwarded-Host"));
        Assert.Equal(Viewer, echoed.Header("X-Forwarded-For"));
        Assert.Equal("https", echoed.Header("X-Forwarded-Proto"));
        Assert.Null(echoed.Header("X-Origin-Verify"));
    }

    [Fact]
    public async Task AwsGateway_ViewerAddressFromOutsideTheRanges_IsIgnored()
    {
        Echoed echoed = await AwsEchoedAsync("untrusted", gateway.AppHost, "/api/realip-untrusted", "-H", $"CloudFront-Viewer-Address: {Viewer}:46532");

        Assert.Equal(GatewayFixture.UntrustedIp, echoed.Header("X-Forwarded-For"));
    }

    [Fact]
    public async Task AwsGateway_ClientsOwnForwardedFor_IsDropped()
    {
        Echoed echoed = await AwsEchoedAsync("trusted", gateway.AppHost, "/api/realip-chain",
            "-H", $"CloudFront-Viewer-Address: {Viewer}:46532", "-H", "X-Forwarded-For: 203.0.113.9");

        Assert.Equal(Viewer, echoed.Header("X-Forwarded-For"));
    }

    [Theory]
    [InlineData("https", "https")]
    [InlineData("http", "http")]
    [InlineData("none", "https")]
    [InlineData("gopher", "https")]
    public async Task AwsGateway_ForwardedProto_ComesFromCloudFrontAndIsHttpsUnlessHttp(string sent, string expected)
    {
        // None (or anything but http) is https, never empty.
        string[] proto = sent == "none" ? [] : ["-H", $"CloudFront-Forwarded-Proto: {sent}"];

        Echoed echoed = await AwsEchoedAsync("trusted", gateway.AppHost, $"/api/proto-{sent}", proto);

        Assert.Equal(expected, echoed.Header("X-Forwarded-Proto"));
    }

    // ------------------------------------------------------------------ 7.4 criterion 3: the origin-verify header

    [Theory]
    [InlineData("app", "/api/verify-app")]
    [InlineData("auth", "/oauth/v2/verify-auth")]
    [InlineData("mail", "/api/v1/verify-mail")]
    public async Task AwsGateway_OriginVerifyMissingOrWrong_Gets403BeforeAnyUpstream(string origin, string path)
    {
        string host = HostOf("aws", origin);

        Reply missing = await SendAsync("trusted", $"{gateway.Aws}{path}-missing", "-H", $"Host: {host}");
        Reply wrong = await SendAsync("trusted", $"{gateway.Aws}{path}-wrong", "-H", $"Host: {host}", "-H", $"X-Origin-Verify: wrong-{gateway.OriginVerify}");

        Assert.Equal(403, missing.Status);
        Assert.Equal(403, wrong.Status);
        Assert.DoesNotContain($"{path}-missing", await UpstreamLogAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain($"{path}-wrong", await UpstreamLogAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AwsGateway_RightOriginVerify_PassesOnAllThreeHosts()
    {
        Reply app = await AwsAsync(gateway.AppHost, "/api/verify-app-right");
        Reply auth = await AwsAsync(gateway.AuthHost, "/oauth/v2/verify-auth-right");
        Reply mail = await AwsAsync(gateway.MailHost, "/api/v1/messages");

        Assert.Equal(200, app.Status);
        Assert.Equal(200, auth.Status);
        // Mailpit's own 401: the request reached it.
        Assert.Equal(401, mail.Status);
        await AssertUpstreamSawAsync("/api/verify-app-right");
        await AssertUpstreamSawAsync("/oauth/v2/verify-auth-right");
    }

    [Fact]
    public async Task AwsGateway_OriginVerifyValue_IsInNoLogFormatAndNoLogLine()
    {
        await AwsAsync(gateway.AppHost, "/api/verify-logged");
        string rendered = await RenderedAwsConfigAsync();
        ProcessResult log = await Processes.DockerAsync(CommandTimeout, "logs", $"{gateway.Run}-gateway");

        Assert.All(LogFormat().Matches(rendered), format => Assert.DoesNotContain("origin_verify", format.Value, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(gateway.OriginVerify, log.Output + log.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AwsGateway_HealthCheck_SendsTheOriginVerifyHeaderAndPasses()
    {
        ProcessResult check = await Processes.DockerAsync(CommandTimeout, "exec", $"{gateway.Run}-gateway", "sh", "-c", gateway.AwsHealthCheck);

        Assert.Contains("X-Origin-Verify: $GATEWAY_ORIGIN_VERIFY", gateway.AwsHealthCheck, StringComparison.Ordinal);
        Assert.True(check.ExitCode == 0, $"The gateway's health check fails: {gateway.AwsHealthCheck}\n{check.Errors}");
    }

    // ------------------------------------------------------------------ 7.12 criterion 4 (this story): what each upstream receives

    /// <summary>
    /// The proxied locations that send Host and X-Forwarded-Host as their origin, X-Forwarded-Proto and X-Forwarded-For.
    /// With the local /otlp/ and the AWS Mailpit location (their own cases below), this is every proxy_pass in each template.
    /// </summary>
    public static readonly TheoryData<string, string, string, string> ForwardingRoutes = new()
    {
        { "local", "app", "/api/health/route", "8080" },
        { "local", "app", "/api/route", "8080" },
        { "local", "app", "/scalar", "8080" },
        { "local", "app", "/scalar/route", "8080" },
        { "local", "app", "/openapi/v1.json", "8080" },
        { "local", "auth", "/oauth/v2/route", "8080" },
        { "aws", "app", "/api/health/route", "8080" },
        { "aws", "app", "/api/route", "8080" },
        { "aws", "app", "/scalar", "8080" },
        { "aws", "app", "/scalar/route", "8080" },
        { "aws", "app", "/openapi/v1.json", "8080" },
        { "aws", "auth", "/oauth/v2/route", "8080" },
    };

    [Theory]
    [MemberData(nameof(ForwardingRoutes))]
    public async Task ProxiedLocation_UpstreamRequest_HoldsExactlyTheHeadersItsLocationSets(string gatewayName, string origin, string path, string port)
    {
        // A client's RFC 7239 Forwarded must never reach an upstream (Zitadel resolves its instance from it), nor the
        // origin-verify header: AWS requests carry it already, and the local edge drops a client's one the same way.
        // The full set of names shows a header a location adds or stops dropping.
        string host = HostOf(gatewayName, origin);
        string[] localOriginVerify = gatewayName == "local" ? ["-H", "X-Origin-Verify: client-sent"] : [];

        Reply reply = await GatewayAsync(gatewayName, host, path, ["-H", "Forwarded: for=203.0.113.9;host=evil.example;proto=http", .. localOriginVerify]);
        var echoed = Echoed.Parse(reply.Body);

        Assert.Equal(port, echoed.Port);
        Assert.Equal("1.1", echoed.Version);
        Assert.Equal(["accept", "host", "user-agent", "x-forwarded-for", "x-forwarded-host", "x-forwarded-proto"], echoed.Names());
        Assert.Equal(host, echoed.Header("Host"));
        Assert.Equal(host, echoed.Header("X-Forwarded-Host"));
        Assert.Equal(gatewayName == "aws" ? "https" : "http", echoed.Header("X-Forwarded-Proto"));
        Assert.Equal(GatewayFixture.TrustedIp, echoed.Header("X-Forwarded-For"));
    }

    [Fact]
    public async Task LocalOtlpLocation_UpstreamRequest_HoldsNoCookieNoAuthorizationAndNoForwardedHost()
    {
        // The receiver needs no credentials, so the session cookie, any Authorization and a client's X-Origin-Verify stay
        // at the edge; the location sends no X-Forwarded-Host or X-Forwarded-Proto, and the receiver's own name as Host.
        Reply reply = await GatewayAsync("local", HostOf("local", "app"), "/otlp/v1/traces", "-X", "POST", "-H", "Content-Type: application/json",
            "--data", "{}", "-H", "Cookie: __Host-disputedesk=session", "-H", "Authorization: Bearer client-token",
            "-H", "Forwarded: for=203.0.113.9;host=evil.example;proto=http", "-H", "X-Origin-Verify: client");
        var echoed = Echoed.Parse(reply.Body);

        Assert.Equal("18890", echoed.Port);
        Assert.Equal("/v1/traces", echoed.Uri);
        Assert.Equal("1.1", echoed.Version);
        Assert.Equal(["accept", "content-length", "content-type", "host", "user-agent", "x-forwarded-for"], echoed.Names());
        Assert.Equal("aspire-dashboard:18890", echoed.Header("Host"));
        Assert.Equal(GatewayFixture.TrustedIp, echoed.Header("X-Forwarded-For"));
    }

    [Fact]
    public async Task AwsMailpitLocation_UpstreamRequest_HoldsNoForwardedHostNoForwardedAndNoOriginVerify()
    {
        // On the second network the echo answers as Mailpit; the request comes from inside that gateway (127.0.0.1).
        ProcessResult curl = await Processes.DockerAsync(CommandTimeout, "exec", $"{gateway.Run}-gateway-mail", "curl", "-sS", "--max-time", "5",
            "-H", $"Host: {gateway.MailHost}", "-H", $"X-Origin-Verify: {gateway.OriginVerify}",
            "-H", "Forwarded: for=203.0.113.9;host=evil.example;proto=http", "http://127.0.0.1:8080/route");
        var echoed = Echoed.Parse(curl.Output);

        Assert.Equal("8025", echoed.Port);
        Assert.Equal("1.1", echoed.Version);
        // Connection: close, from the WebSocket map when the client asks for no upgrade.
        Assert.Equal(["accept", "connection", "host", "user-agent", "x-forwarded-for", "x-forwarded-proto"], echoed.Names());
        Assert.Equal(gateway.MailHost, echoed.Header("Host"));
        Assert.Equal("https", echoed.Header("X-Forwarded-Proto"));
        Assert.Equal("127.0.0.1", echoed.Header("X-Forwarded-For"));
    }

    [Theory]
    [InlineData("infra/images/gateway/templates/nginx.conf.template", "local")]
    [InlineData("infra/images/gateway/templates-aws/nginx.conf.template", "aws")]
    public void ProxiedLocationCases_CoverEveryProxyPassInTheTemplate(string templatePath, string gatewayName)
    {
        // A new proxied location fails here until it has a case above.
        string template = File.ReadAllText(Path.Combine(SourceRoots.Repo, templatePath));
        int cases = ForwardingRoutes.Count(row => row.Data.Item1 == gatewayName) + 1; // + the /otlp/ or Mailpit case

        Assert.Equal(ProxyPass().Count(template), cases);
    }

    // ------------------------------------------------------------------ 7.12 criterion 1: HSTS in AWS only

    [Theory]
    [InlineData("app", "/")]
    [InlineData("auth", "/.well-known/openid-configuration")]
    [InlineData("mail", "/")]
    public async Task AwsGateway_PageAndOwn403_SendHstsOnce(string origin, string path)
    {
        string host = HostOf("aws", origin);

        Reply page = await AwsAsync(host, path);
        Reply refused = await SendAsync("trusted", $"{gateway.Aws}{path}", "-H", $"Host: {host}");

        AssertExactly(page, "strict-transport-security", Hsts, $"{host}{path}");
        Assert.Equal(403, refused.Status);
        AssertExactly(refused, "strict-transport-security", Hsts, $"{host}{path} 403");
    }

    [Theory]
    [InlineData("app", "/otlp/v1/traces")]
    [InlineData("auth", "/ui/console")]
    public async Task AwsGateway_Own404_SendsHstsOnce(string origin, string path)
    {
        Reply reply = await AwsAsync(HostOf("aws", origin), path);

        Assert.Equal(404, reply.Status);
        AssertExactly(reply, "strict-transport-security", Hsts, path);
    }

    [Theory]
    [InlineData("app", "/")]
    [InlineData("auth", "/.well-known/openid-configuration")]
    public async Task LocalGateway_PlainHttp_SendsNoHsts(string origin, string path)
    {
        Reply reply = await GatewayAsync("local", HostOf("local", origin), path);

        Assert.Empty(reply.Values("strict-transport-security"));
    }

    // ------------------------------------------------------------------ 7.4 criterion 3: the console stays closed

    [Theory]
    [InlineData("/ui/console")]
    [InlineData("/ui/console/")]
    [InlineData("/ui/console/assets/probe.js")]
    public async Task AwsGateway_Console_Is404WithoutReachingZitadel(string path)
    {
        Reply reply = await AwsAsync(gateway.AuthHost, path);

        Assert.Equal(404, reply.Status);
        Assert.DoesNotContain($"GET {path} ", await UpstreamLogAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/.well-known/openid-configuration", "8080")]
    [InlineData("/oauth/v2/authorize", "8080")]
    [InlineData("/ui/login/loginname", "8080")]
    public async Task AwsGateway_IdentityRoutes_ReachTheirUpstream(string path, string port)
    {
        Echoed echoed = await AwsEchoedAsync("trusted", gateway.AuthHost, path);

        Assert.Equal(port, echoed.Port);
    }

    [Theory]
    [InlineData("aws")]
    [InlineData("local")]
    public async Task IdentityOrigin_Root_RedirectsToTheLoginNamePage(string gatewayName)
    {
        // Zitadel's own /ui/login/ forwards to the console, which the AWS gateway closes; the login name page starts a sign-in.
        Reply reply = await GatewayAsync(gatewayName, HostOf(gatewayName, "auth"), "/");

        Assert.Equal(302, reply.Status);
        Assert.Equal(["/ui/login/loginname"], reply.Values("location").Select(location => new Uri(new Uri("http://identity.test"), location).AbsolutePath));
    }

    // ------------------------------------------------------------------ 7.12 criterion 3: the identity origin's baseline

    [Fact]
    public async Task AwsIdentityOrigin_OwnResponses_SendTheBaselineOnce()
    {
        Reply refused = await SendAsync("trusted", $"{gateway.Aws}/", "-H", $"Host: {gateway.AuthHost}");
        Reply console = await AwsAsync(gateway.AuthHost, "/ui/console");

        AssertIdentityBaseline(refused, 403, 1, "strict-origin-when-cross-origin", "AWS / without X-Origin-Verify");
        AssertIdentityBaseline(console, 404, 1, "strict-origin-when-cross-origin", "AWS /ui/console");
    }

    [Theory]
    [InlineData("aws", "/", 302, "strict-origin-when-cross-origin")]
    [InlineData("aws", "/.well-known/openid-configuration", 200, "strict-origin-when-cross-origin")]
    [InlineData("aws", "/ui/login/loginname", 200, "same-origin")]
    [InlineData("local", "/", 302, "strict-origin-when-cross-origin")]
    [InlineData("local", "/.well-known/openid-configuration", 200, "strict-origin-when-cross-origin")]
    [InlineData("local", "/ui/login/loginname", 200, "same-origin")]
    public async Task IdentityOrigin_Responses_SendTheBaselineOnce(string gatewayName, string path, int status, string referrerPolicy)
    {
        // The echo sends the hosted login's own HSTS, nosniff and same-origin, of which the gateway hides the first two
        // and passes the Referrer-Policy through alone, and no Referrer-Policy on discovery.
        Reply reply = await GatewayAsync(gatewayName, HostOf(gatewayName, "auth"), path);

        AssertIdentityBaseline(reply, status, HstsCount(gatewayName), referrerPolicy, $"{gatewayName} {path}");
        if (path == "/ui/login/loginname")
        {
            // The login's own X-Frame-Options passes through once.
            AssertExactly(reply, "x-frame-options", "deny", $"{gatewayName} {path}");
        }
    }

    // ------------------------------------------------------------------ 7.4 criterion 4: Mailpit on its own origin

    [Theory]
    [InlineData("/")]
    [InlineData("/api/v1/messages")]
    public async Task AwsMailpitOrigin_BasicAuth_RefusesNoAndWrongCredentials(string path)
    {
        string user = gateway.MailpitCredentials.Split(':')[0];

        Reply none = await AwsAsync(gateway.MailHost, path);
        Reply wrong = await AwsAsync(gateway.MailHost, path, "-u", $"{user}:wrong-password");
        Reply right = await AwsAsync(gateway.MailHost, path, "-u", gateway.MailpitCredentials);

        Assert.Equal(401, none.Status);
        Assert.Equal(401, wrong.Status);
        Assert.Equal(200, right.Status);
    }

    [Fact]
    public async Task AwsMailpitOrigin_SendApi_RefusesTheUiLoginAndTakesItsOwn()
    {
        // aws-admin-lockdown L2: with MP_SEND_API_AUTH set, Mailpit checks the Send API against it alone (Mailpit
        // v1.32.0, server.go sendAPIAuthMiddleware), so a reader holding the published UI login cannot send mail.
        string[] message = ["-X", "POST", "-H", "Content-Type: application/json",
            "--data", """{"From":{"Email":"sender@disputedesk.test"},"To":[{"Email":"reader@disputedesk.test"}],"Subject":"Send API test"}"""];

        Reply uiLogin = await AwsAsync(gateway.MailHost, "/api/v1/send", ["-u", gateway.MailpitCredentials, .. message]);
        Reply sendLogin = await AwsAsync(gateway.MailHost, "/api/v1/send", ["-u", gateway.MailpitSendCredentials, .. message]);

        Assert.Equal(401, uiLogin.Status);
        Assert.Equal(200, sendLogin.Status);
    }

    [Theory]
    [InlineData("/mailpit/")]
    [InlineData("/mailpit/api/v1/messages")]
    public async Task AwsAppOrigin_MailpitPath_Is404WithoutReachingAnUpstream(string path)
    {
        Reply reply = await AwsAsync(gateway.AppHost, path, "-u", gateway.MailpitCredentials);

        Assert.Equal(404, reply.Status);
        Assert.DoesNotContain($"{path} ", await UpstreamLogAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AwsMailpitOrigin_Page_SendsItsHeadersOnceAndLoadsItsScripts()
    {
        Reply page = await AwsAsync(gateway.MailHost, "/", "-u", gateway.MailpitCredentials);
        Reply app = await AwsAsync(gateway.AppHost, "/");
        string[] policies = page.Values("content-security-policy");
        string[] scripts = [.. MailpitScript().Matches(page.Body).Select(match => match.Groups["src"].Value)];

        AssertExactly(page, "strict-transport-security", Hsts, "Mailpit /");
        AssertExactly(page, "x-content-type-options", "nosniff", "Mailpit /");
        AssertExactly(page, "x-frame-options", "DENY", "Mailpit /");
        AssertExactly(page, "referrer-policy", "no-referrer", "Mailpit /");
        // Two policies, both enforced: the gateway's frame-ancestors 'none' and Mailpit's own (its scripts by nonce).
        Assert.Equal(2, policies.Length);
        Assert.Contains("frame-ancestors 'none'", policies);
        Assert.Contains(policies, policy => policy.Contains("script-src 'nonce-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Assert.Single(app.Values("content-security-policy")), policies);
        Assert.NotEmpty(scripts);
        foreach (string script in scripts)
        {
            Reply loaded = await AwsAsync(gateway.MailHost, script.StartsWith('/') ? script : $"/{script}", "-u", gateway.MailpitCredentials);
            Assert.True(loaded.Status == 200, $"Mailpit's {script} does not load: {loaded.Status}");
        }
    }

    [Fact]
    public async Task AwsMailpitOrigin_RapidRequests_AreUnderTheCoarseLimit()
    {
        ProcessResult burst = await Processes.DockerAsync(CommandTimeout, "exec", $"{gateway.Run}-trusted", "sh", "-c",
            "for _ in $(seq 40); do curl -s -o /dev/null -w '%{http_code}\\n' --max-time 5 \"$@\"; done", "sh",
            "-H", $"Host: {gateway.MailHost}", "-H", $"X-Origin-Verify: {gateway.OriginVerify}", "-H", "CloudFront-Viewer-Address: 198.51.100.77:1",
            $"{gateway.Aws}/api/v1/messages");

        Assert.Contains("429", burst.Output.Split('\n'));
    }

    // ------------------------------------------------------------------ 7.4 criterion 6: no browser telemetry intake in AWS

    [Theory]
    [InlineData("traces")]
    [InlineData("metrics")]
    [InlineData("logs")]
    public async Task AwsGateway_OtlpPost_Is404WithoutReachingAnUpstream(string signal)
    {
        Reply reply = await AwsAsync(gateway.AppHost, $"/otlp/v1/{signal}", "-X", "POST", "-H", "Content-Type: application/json", "--data", "{}");

        Assert.Equal(404, reply.Status);
        Assert.DoesNotContain($"/otlp/v1/{signal}", await UpstreamLogAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpaBuilds_BrowserTelemetry_IsOffOnlyInTheAwsBuild()
    {
        ProcessResult aws = await Processes.DockerAsync(CommandTimeout, "exec", $"{gateway.Run}-gateway", "grep", "-rqF", "/otlp/v1/traces", "/usr/share/nginx/html");
        ProcessResult local = await Processes.DockerAsync(CommandTimeout, "exec", $"{gateway.Run}-local", "grep", "-rqF", "/otlp/v1/traces", "/usr/share/nginx/html");

        Assert.Equal(1, aws.ExitCode);
        Assert.Equal(0, local.ExitCode);
    }

    // ------------------------------------------------------------------ 7.4 criterion 7 and smoke.sh: the SPA's headers

    [Theory]
    [InlineData("aws", "/")]
    [InlineData("aws", "/api/health/live")]
    [InlineData("local", "/")]
    [InlineData("local", "/api/health/live")]
    public async Task AppOrigin_SpaAndProxiedAnswers_SendEachSecurityHeaderOnce(string gatewayName, string path)
    {
        Reply reply = await GatewayAsync(gatewayName, HostOf(gatewayName, "app"), path);

        Assert.Equal(200, reply.Status);
        AssertAppHeadersOnce(reply, HstsCount(gatewayName), $"{gatewayName} {path}");
    }

    [Theory]
    [InlineData("aws")]
    [InlineData("local")]
    public async Task Spa_CacheHeaders_RevalidateTheIndexAndKeepBuildFilesForAYear(string gatewayName)
    {
        // index.html (for / and client routes) is revalidated on every load; the hashed build files are cached for a year
        // (RFC 9111 5.2.2.4, RFC 8246). Each build has its own hashed names, so each gateway's index.html names its own.
        string host = HostOf(gatewayName, "app");
        Reply index = await GatewayAsync(gatewayName, host, "/");
        string asset = AssetScript().Match(index.Body).Value;

        Assert.NotEmpty(asset);
        // The CSP's base-uri 'none' would block a base element, so the page must hold none (CSP Level 3, base-uri).
        Assert.DoesNotContain("<base", index.Body, StringComparison.OrdinalIgnoreCase);
        foreach ((string path, string cacheControl) in new[] { ("/", "no-cache"), ("/disputes", "no-cache"), (asset, "public, max-age=31536000, immutable") })
        {
            Reply reply = await GatewayAsync(gatewayName, host, path);
            Assert.Equal(200, reply.Status);
            AssertExactly(reply, "cache-control", cacheControl, $"{gatewayName} {path}");
            AssertAppHeadersOnce(reply, HstsCount(gatewayName), $"{gatewayName} {path}");
        }

        Reply missing = await GatewayAsync(gatewayName, host, "/assets/missing-probe.js");
        Assert.Equal(404, missing.Status);
        Assert.Empty(missing.Values("cache-control"));
    }

    [Theory]
    [InlineData("aws")]
    [InlineData("local")]
    public async Task AppOrigin_ContentSecurityPolicies_HoldBaseUriNone(string gatewayName)
    {
        string host = HostOf(gatewayName, "app");
        string authOrigin = EnvironmentOf(gatewayName)["GATEWAY_AUTH_ORIGIN"];
        string appCsp = $"default-src 'self'; script-src 'self'; style-src 'self' 'sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU='; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self' {authOrigin}";
        string scalarHash = ScalarHash().Match(await File.ReadAllTextAsync(Path.Combine(SourceRoots.Repo, "infra/images/gateway/templates/scalar-headers.conf.template"), TestContext.Current.CancellationToken)).Value;
        string scalarCsp = $"default-src 'self'; script-src 'self' {scalarHash}; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";

        foreach (string path in new[] { "/", "/disputes" })
        {
            AssertExactly(await GatewayAsync(gatewayName, host, path), "content-security-policy", appCsp, $"{gatewayName} {path}");
        }

        AssertExactly(await GatewayAsync(gatewayName, host, "/scalar/"), "content-security-policy", scalarCsp, $"{gatewayName} /scalar/");
    }

    [Fact]
    public async Task AppOrigin_StyleSrcHash_IsTheHashOfTheEmptyString()
    {
        // ui-linking criterion 5: the one hash admits only an empty style element (input-otp's), never CSS text
        // (CSP Level 3 hash-source: base64 of the SHA-256 digest of the element's content).
        string template = await File.ReadAllTextAsync(Path.Combine(SourceRoots.Repo, "infra/images/gateway/templates/security-headers.conf.template"), TestContext.Current.CancellationToken);
        string emptyStringHash = "'sha256-" + Convert.ToBase64String(SHA256.HashData([])) + "'";

        Assert.Equal(emptyStringHash, AppStyleSrcHash().Match(template).Groups["hash"].Value);
    }

    [Theory]
    [InlineData("aws")]
    [InlineData("local")]
    public async Task Spa_TextFiles_NameTheirCharset(string gatewayName)
    {
        string host = HostOf(gatewayName, "app");
        Reply index = await GatewayAsync(gatewayName, host, "/");
        string css = AssetStylesheet().Match(index.Body).Value;
        string js = AssetScript().Match(index.Body).Value;

        Assert.NotEmpty(css);
        Assert.NotEmpty(js);
        foreach ((string path, string type) in new[] { ("/", "text/html"), (css, "text/css"), (js, "application/javascript"), ("/assets/missing-probe.css", "text/html") })
        {
            AssertExactly(await GatewayAsync(gatewayName, host, path), "content-type", $"{type}; charset=utf-8", $"{gatewayName} {path}");
        }
    }

    [Fact]
    public async Task AwsGateway_ProxiedAnswers_KeepTheirUpstreamsContentType()
    {
        // The charset is set only in the SPA's own locations.
        Reply mailpitDirect = await SendAsync("trusted", $"http://{gateway.Run}-mailpit:8025/", "-u", gateway.MailpitCredentials);
        Reply mailpit = await AwsAsync(gateway.MailHost, "/", "-u", gateway.MailpitCredentials);
        Reply discoveryDirect = await SendAsync("trusted", $"http://{gateway.Run}-echo:8080/.well-known/openid-configuration");
        Reply discovery = await AwsAsync(gateway.AuthHost, "/.well-known/openid-configuration");

        Assert.Equal(mailpitDirect.Values("content-type"), mailpit.Values("content-type"));
        Assert.Equal(discoveryDirect.Values("content-type"), discovery.Values("content-type"));
    }

    // ------------------------------------------------------------------ 7.12 criterion 6: the access logs

    [Fact]
    public async Task AccessLogs_RecordThePath_NeverTheQueryOrTheReferer()
    {
        string marker = $"LogMarker712x{Environment.ProcessId}";
        string[] referer = ["-H", $"Referer: https://example.test/?ref={marker}"];
        // A viewer address no other case limits, so no request here is refused (AWS); locally the client's own address.
        string[] viewer = ["-H", "CloudFront-Viewer-Address: 198.51.100.66:1"];
        (string Container, (string Host, string Path, string Logged)[] Requests)[] gateways =
        [
            ($"{gateway.Run}-gateway", [(gateway.AppHost, $"/api/signin-oidc?code={marker}", "/api/signin-oidc"), (gateway.AppHost, $"/?state={marker}", "/index.html"),
                (gateway.AuthHost, $"/oidc/v1/end_session?id_token_hint={marker}", "/oidc/v1/end_session"), (gateway.MailHost, $"/api/v1/messages?query={marker}", "/api/v1/messages")]),
            ($"{gateway.Run}-local", [(HostOf("local", "app"), $"/api/signin-oidc?code={marker}", "/api/signin-oidc"), (HostOf("local", "app"), $"/?state={marker}", "/index.html"),
                (HostOf("local", "auth"), $"/oidc/v1/end_session?id_token_hint={marker}", "/oidc/v1/end_session")]),
        ];

        foreach ((string container, (string Host, string Path, string Logged)[] requests) in gateways)
        {
            string gatewayName = container.EndsWith("-local", StringComparison.Ordinal) ? "local" : "aws";
            Dictionary<string, int> before = [];
            foreach ((_, _, string logged) in requests)
            {
                before[logged] = CountLines(await AccessLogAsync(container), logged);
            }

            foreach ((string host, string path, _) in requests)
            {
                await GatewayAsync(gatewayName, host, path, [.. viewer, .. referer]);
            }

            foreach ((_, _, string logged) in requests)
            {
                await AssertEventuallyAsync(async () => CountLines(await AccessLogAsync(container), logged) > before[logged], $"{container}'s access log has no new GET {logged}");
            }

            Assert.DoesNotContain(marker, await AccessLogAsync(container), StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------ 7.12 criterion 7 and smoke.sh: the gateway's own /api answers

    [Theory]
    [InlineData("aws", "/api/limit-probe.json", "198.51.100.70")]
    [InlineData("aws", "/api/health/limit-probe.json", "198.51.100.74")]
    [InlineData("aws", "/scalar/", "198.51.100.75")]
    [InlineData("local", "/api/limit-probe.json", "")]
    [InlineData("local", "/api/health/limit-probe.json", "")]
    [InlineData("local", "/scalar/", "")]
    public async Task CoarseLimit_429_IsTheGatewaysProblemWithNoStore(string gatewayName, string path, string viewer)
    {
        // Limited per client: in AWS per viewer address (one per case), locally per container address (the untrusted
        // client, which no other local case uses). The path ends in .json, so its Content-Type does not come from the
        // extension. RFC 8259 11: application/json-based types have no charset. Retry-After in seconds (RFC 9110 10.2.3).
        string host = HostOf(gatewayName, "app");
        Reply spa = await GatewayAsync(gatewayName, host, "/");

        Reply limited = await First429Async(gatewayName == "aws" ? "trusted" : "untrusted", $"{BaseOf(gatewayName)}{path}",
            "-H", $"Host: {host}", "-H", $"X-Origin-Verify: {gateway.OriginVerify}", "-H", $"CloudFront-Viewer-Address: {viewer}:1");
        JsonNode problem = JsonNode.Parse(limited.Body)!;

        string label = $"{gatewayName} {path} 429";
        Assert.Equal(429, limited.Status);
        AssertExactly(limited, "cache-control", "no-store", label);
        AssertExactly(limited, "content-type", "application/problem+json", label);
        AssertExactly(limited, "retry-after", "1", label);
        AssertAppHeadersOnce(limited, HstsCount(gatewayName), label);
        Assert.Equal(spa.Values("content-security-policy"), limited.Values("content-security-policy"));
        // The gateway's fixed RFC 9457 problem; no traceId, since nginx has no trace context.
        Assert.Equal(["status", "title", "type"], problem.AsObject().Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal("https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/rate-limited.md", problem["type"]!.GetValue<string>());
        Assert.Equal("Too many requests", problem["title"]!.GetValue<string>());
        Assert.Equal(429, problem["status"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("aws", "/api/v1/disputes", "198.51.100.71")]
    [InlineData("aws", "/api/health/disputes", "198.51.100.76")]
    [InlineData("local", "/api/v1/disputes", "")]
    [InlineData("local", "/api/health/disputes", "")]
    public async Task OversizedBody_413_IsTheGatewaysFixedTextWithNoStore(string gatewayName, string path, string viewer)
    {
        // 2 MB, above nginx's default client_max_body_size of 1 MB.
        string host = HostOf(gatewayName, "app");

        ProcessResult post = await Processes.DockerAsync(CommandTimeout, "exec", $"{gateway.Run}-trusted", "sh", "-c",
            "head -c 2097152 /dev/zero | curl -sS -D - --max-time 5 -X POST --data-binary @- \"$@\"", "sh",
            "-H", $"Host: {host}", "-H", $"X-Origin-Verify: {gateway.OriginVerify}", "-H", $"CloudFront-Viewer-Address: {viewer}:1", $"{BaseOf(gatewayName)}{path}");

        AssertGatewayError(Reply.Parse(post.Output), 413, HstsCount(gatewayName), $"{gatewayName} {path} 413");
    }

    [Theory]
    [InlineData("aws", "/api/upstream-closes")]
    [InlineData("aws", "/api/health/upstream-closes")]
    [InlineData("local", "/api/upstream-closes")]
    [InlineData("local", "/api/health/upstream-closes")]
    public async Task UpstreamClosesTheConnection_502_IsTheGatewaysFixedTextWithNoStore(string gatewayName, string path)
    {
        Reply reply = await GatewayAsync(gatewayName, HostOf(gatewayName, "app"), path, "-H", "CloudFront-Viewer-Address: 198.51.100.73:1");

        AssertGatewayError(reply, 502, HstsCount(gatewayName), $"{gatewayName} {path} 502");
    }

    [Theory]
    [InlineData("aws", "/api/v1/disputes", "198.51.100.72")]
    [InlineData("aws", "/api/health/live", "198.51.100.78")]
    [InlineData("local", "/api/v1/disputes", "")]
    [InlineData("local", "/api/health/live", "")]
    public async Task UpstreamAnswer_KeepsItsOwnSingleCacheControl(string gatewayName, string path, string viewer)
    {
        // The echo sends no-store, as the Api does on every response; the gateway adds none of its own.
        Reply reply = await GatewayAsync(gatewayName, HostOf(gatewayName, "app"), path, "-H", $"CloudFront-Viewer-Address: {viewer}:1");

        Assert.Equal(200, reply.Status);
        AssertExactly(reply, "cache-control", "no-store", $"{gatewayName} {path} from the upstream");
    }

    [Theory]
    [InlineData("aws", "/api/upstream-fails")]
    [InlineData("aws", "/api/health/upstream-fails")]
    [InlineData("local", "/api/upstream-fails")]
    [InlineData("local", "/api/health/upstream-fails")]
    public async Task UpstreamError_PassesThroughUntouched(string gatewayName, string path)
    {
        // proxy_intercept_errors stays off, so the Api's own error never reaches error_page.
        Reply reply = await GatewayAsync(gatewayName, HostOf(gatewayName, "app"), path, "-H", "CloudFront-Viewer-Address: 198.51.100.79:1");

        Assert.Equal(500, reply.Status);
        Assert.Equal("the api's own error", reply.Body.Trim());
        AssertExactly(reply, "cache-control", "no-store", $"{gatewayName} {path} upstream 500");
    }

    // ------------------------------------------------------------------ requests

    private string BaseOf(string gatewayName) => gatewayName == "aws" ? gateway.Aws : gateway.Local;

    private IReadOnlyDictionary<string, string> EnvironmentOf(string gatewayName) =>
        gatewayName == "aws" ? gateway.AwsGatewayEnvironment : gateway.LocalGatewayEnvironment;

    private static int HstsCount(string gatewayName) => gatewayName == "aws" ? 1 : 0;

    /// <summary>The Host a client sends to reach this origin of this gateway.</summary>
    private string HostOf(string gatewayName, string origin) => (gatewayName, origin) switch
    {
        ("aws", "app") => gateway.AppHost,
        ("aws", "auth") => gateway.AuthHost,
        ("aws", "mail") => gateway.MailHost,
        ("local", "app") => gateway.LocalGatewayEnvironment["GATEWAY_APP_HOST"],
        ("local", "auth") => gateway.LocalGatewayEnvironment["GATEWAY_AUTH_HOST"],
        _ => throw new ArgumentException($"No {origin} origin on the {gatewayName} gateway.", nameof(origin)),
    };

    /// <summary>One request from the trusted client to this gateway, with the origin-verify header in AWS.</summary>
    private Task<Reply> GatewayAsync(string gatewayName, string host, string path, params string[] options) =>
        gatewayName == "aws" ? AwsAsync(host, path, options) : SendAsync("trusted", $"{gateway.Local}{path}", ["-H", $"Host: {host}", .. options]);

    private Task<Reply> AwsAsync(string host, string path, params string[] options) =>
        SendAsync("trusted", $"{gateway.Aws}{path}", ["-H", $"Host: {host}", "-H", $"X-Origin-Verify: {gateway.OriginVerify}", .. options]);

    private async Task<Echoed> AwsEchoedAsync(string client, string host, string path, params string[] options)
    {
        Reply reply = await SendAsync(client, $"{gateway.Aws}{path}", ["-H", $"Host: {host}", "-H", $"X-Origin-Verify: {gateway.OriginVerify}", .. options]);
        return Echoed.Parse(reply.Body);
    }

    /// <summary>curl from a client container: the status line, headers and body of one request.</summary>
    private async Task<Reply> SendAsync(string client, string url, params string[] options)
    {
        ProcessResult curl = await Processes.DockerAsync(CommandTimeout, ["exec", $"{gateway.Run}-{client}", "curl", "-sS", "--max-time", "5", "-D", "-", .. options, url]);
        Assert.True(curl.ExitCode == 0, $"curl {url} failed: {curl.Errors}");
        return Reply.Parse(curl.Output);
    }

    /// <summary>The first 429 in up to 60 rapid requests from this client.</summary>
    private async Task<Reply> First429Async(string client, string url, params string[] options)
    {
        ProcessResult burst = await Processes.DockerAsync(CommandTimeout, ["exec", $"{gateway.Run}-{client}", "sh", "-c",
            """
            for _ in $(seq 60); do
              response=$(curl -sS -D - --max-time 5 "$@")
              case "$response" in "HTTP/1.1 429 "*) printf '%s' "$response"; exit 0 ;; esac
            done
            exit 1
            """, "sh", .. options, url]);
        Assert.True(burst.ExitCode == 0, $"60 rapid requests from {client} to {url} got no 429: {burst.Errors}");
        return Reply.Parse(burst.Output);
    }

    private async Task<string> RenderedAwsConfigAsync() =>
        (await Processes.DockerAsync(CommandTimeout, "exec", $"{gateway.Run}-gateway", "nginx", "-T")).Output;

    /// <summary>The access logs of the echo upstream and the OTLP stand-in: what reached an upstream.</summary>
    private async Task<string> UpstreamLogAsync() =>
        await AccessLogAsync($"{gateway.Run}-echo") + await AccessLogAsync($"{gateway.Run}-otlp");

    /// <summary>A container's access log, its standard output (the error log on standard error is left out).</summary>
    private static async Task<string> AccessLogAsync(string container) =>
        (await Processes.DockerAsync(CommandTimeout, "logs", container)).Output;

    private async Task AssertUpstreamSawAsync(string path) =>
        await AssertEventuallyAsync(async () => (await UpstreamLogAsync()).Contains(path, StringComparison.Ordinal), $"{path} did not reach the upstream");

    /// <summary>nginx writes the access log line after the response, so a log check waits up to 5 seconds for it.</summary>
    private static async Task AssertEventuallyAsync(Func<Task<bool>> condition, string message)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        }

        Assert.Fail(message);
    }

    private static int CountLines(string log, string path) =>
        log.Split('\n').Count(line => line.Contains($"\"GET {path} HTTP/1.1\"", StringComparison.Ordinal));

    // ------------------------------------------------------------------ assertions

    /// <summary>The header is sent exactly once, with this value (compared without case).</summary>
    private static void AssertExactly(Reply reply, string name, string value, string label)
    {
        string[] values = reply.Values(name);
        Assert.True(values.Length == 1, $"{label}: {name} is not sent exactly once:\n{reply}");
        Assert.True(values[0].Equals(value, StringComparison.OrdinalIgnoreCase), $"{label}: {name} is not {value}:\n{reply}");
    }

    /// <summary>Each of the app's security headers once, and HSTS the given number of times.</summary>
    private static void AssertAppHeadersOnce(Reply reply, int hsts, string label)
    {
        foreach (string name in AppSecurityHeaders)
        {
            Assert.True(reply.Values(name).Length == 1, $"{label}: {name} is not sent exactly once:\n{reply}");
        }

        Assert.True(reply.Values("strict-transport-security").Length == hsts, $"{label}: Strict-Transport-Security not sent {hsts} time(s):\n{reply}");
    }

    /// <summary>The gateway's own error: its status, no-store, the security headers once and @gateway_error's fixed text.</summary>
    private static void AssertGatewayError(Reply reply, int status, int hsts, string label)
    {
        Assert.Equal(status, reply.Status);
        AssertExactly(reply, "cache-control", "no-store", label);
        AssertAppHeadersOnce(reply, hsts, label);
        // Nothing from the request or the upstream is echoed back.
        Assert.Equal(GatewayErrorBody, reply.Body.Trim());
    }

    /// <summary>nosniff and frame-ancestors 'none' once each, the given Referrer-Policy once, and HSTS the given number of times.</summary>
    private static void AssertIdentityBaseline(Reply reply, int status, int hsts, string referrerPolicy, string label)
    {
        Assert.Equal(status, reply.Status);
        AssertExactly(reply, "x-content-type-options", "nosniff", label);
        AssertExactly(reply, "referrer-policy", referrerPolicy, label);
        Assert.True(reply.Values("content-security-policy").Count(policy => policy == "frame-ancestors 'none'") == 1, $"{label}: not exactly one frame-ancestors 'none' policy:\n{reply}");
        Assert.True(reply.Values("strict-transport-security").Length == hsts, $"{label}: Strict-Transport-Security not sent {hsts} time(s):\n{reply}");
        if (hsts > 0)
        {
            AssertExactly(reply, "strict-transport-security", Hsts, label);
        }
    }

    private static Regex HttpLevel(string directive) =>
        new($"^  {Regex.Escape(directive)}$", RegexOptions.Multiline, TimeSpan.FromSeconds(1));

    [GeneratedRegex(@"^[^#\n]*set_real_ip_from", RegexOptions.Multiline)]
    private static partial Regex UncommentedSetRealIpFrom();

    [GeneratedRegex(@"\blog_format\b[^;]*;")]
    private static partial Regex LogFormat();

    [GeneratedRegex(@"\bproxy_pass\s")]
    private static partial Regex ProxyPass();

    [GeneratedRegex(@"<script[^>]* src=""(?<src>[^""]+)""")]
    private static partial Regex MailpitScript();

    [GeneratedRegex(@"/assets/[^""]+\.js")]
    private static partial Regex AssetScript();

    [GeneratedRegex(@"/assets/[^""]+\.css")]
    private static partial Regex AssetStylesheet();

    [GeneratedRegex(@"'sha256-[^']*'")]
    private static partial Regex ScalarHash();

    [GeneratedRegex(@"style-src 'self' (?<hash>'sha256-[^']*');")]
    private static partial Regex AppStyleSrcHash();
}
