using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace DisputeDesk.PlatformTests;

/// <summary>
/// AC11: what the Zitadel provisioner creates, read from the files. 5.1 AC7: the operations actor mapping it writes.
/// 7.12 AC6 and AC7: the gateway templates' access log format and their /api error pages.
/// </summary>
public partial class PlatformScriptTests
{
    private static readonly string ProvisionScript = File.ReadAllText(Path.Combine(SourceRoots.Repo, "infra/images/zitadel/provision.sh"));
    private static readonly string Compose = File.ReadAllText(Path.Combine(SourceRoots.Repo, "docker-compose.yml"));

    [Fact]
    public void ProvisionScript_ProjectRoles_AreTheFourCamelCaseKeys()
    {
        string roles = RolesBlock().Match(ProvisionScript).Groups["roles"].Value;
        IEnumerable<string> keys = roles.Split('\n').Select(line => line.Split('|')[0].Trim());

        Assert.Equal(["dispute.escalate", "dispute.read", "dispute.resolve", "dispute.startReview"], keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("start_review", ProvisionScript, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Issuer: $issuer")]
    [InlineData("MetadataAddress: \"\\($issuer)/.well-known/openid-configuration\"")]
    [InlineData("ProjectId: $project")]
    [InlineData("WebApp: { ClientId: $webAppId, ClientSecret: .[0] }")]
    public void ProvisionScript_ClientsFile_HoldsTheAd26ZitadelKeys(string key)
    {
        Assert.Contains(key, ProvisionScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionScript_ClientsFile_MapsTheMachineUserIdToTheOperationsActor()
    {
        // 5.1 AC7, AD-26: Operations:Actors:<the machine user's ID, its tokens' sub> = operations.
        Assert.Contains("--arg opsUserId \"$OPS_ID\"", ProvisionScript, StringComparison.Ordinal);
        Assert.Contains("Operations: { Actors: { ($opsUserId): \"operations\" } }", ProvisionScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionScript_CurrentCheck_AlsoRequiresTheActorMapping()
    {
        // Otherwise a clients file written before 5.1 counts as current and never gains the mapping.
        string currentCheck = CurrentCheck().Match(ProvisionScript).Value;

        Assert.Contains("is current; secrets not rotated", currentCheck, StringComparison.Ordinal);
        Assert.Contains("[ \"$(jq -r --arg ops \"$OPS_ID\" '.Operations.Actors[$ops]' \"$OUT_FILE\")\" = operations ]", currentCheck, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionScript_DevelopmentMode_ComesFromConfiguration()
    {
        Assert.Contains("\\\"developmentMode\\\":$AUTH_DEVELOPMENT_MODE,", ProvisionScript, StringComparison.Ordinal);
        Assert.Contains("AUTH_DEVELOPMENT_MODE: ${AUTH_DEVELOPMENT_MODE:-true}", Compose, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionScript_WebApp_UsesClientSecretPostAndOnlyTheCodeGrant()
    {
        // The ASP.NET Core handler sends the client secret in the form body and keeps no refresh token.
        Assert.Contains("\\\"authMethodType\\\":\\\"OIDC_AUTH_METHOD_TYPE_POST\\\"", ProvisionScript, StringComparison.Ordinal);
        Assert.Contains("\\\"grantTypes\\\":[\\\"OIDC_GRANT_TYPE_AUTHORIZATION_CODE\\\"]", ProvisionScript, StringComparison.Ordinal);
        Assert.DoesNotContain("OIDC_GRANT_TYPE_REFRESH_TOKEN", ProvisionScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionScript_DevelopmentModeNotABoolean_FailsBeforeCallingZitadel()
    {
        var start = new ProcessStartInfo("bash", [Path.Combine(SourceRoots.Repo, "infra/images/zitadel/provision.sh")])
        {
            RedirectStandardError = true,
            Environment =
            {
                ["ZITADEL_URL"] = "http://zitadel.invalid",
                ["OUT_DIR"] = Path.GetTempPath(),
                ["OUT_UID"] = "1654",
                ["FIXTURES_FILE"] = "unused.json",
                ["APP_ORIGINS"] = "http://localhost:8080",
                ["AUTH_DEVELOPMENT_MODE"] = "yes",
            },
        };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("bash did not start.");
        string errors = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, process.ExitCode);
        Assert.Contains("AUTH_DEVELOPMENT_MODE must be true or false", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("infra/images/zitadel/provision.sh")]
    [InlineData("infra/images/zitadel/verify.sh")]
    public void ProvisioningScript_CurlOptions_SetAMaxTime(string script)
    {
        // Every Zitadel call in the script goes through curl "${CURL_OPTS[@]}", so this one option times them all out.
        string text = File.ReadAllText(Path.Combine(SourceRoots.Repo, script));

        Assert.Contains("CURL_OPTS=(--max-time ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionScript_FirstZitadelCall_IsTheOnlyOneThatRetries()
    {
        // Zitadel can refuse connections or answer 503 for a moment after its health check passes (story 2.6, QA F1).
        string firstCall = ZitadelCall().Match(ProvisionScript).Value;

        Assert.Equal("RETRY=1 call GET /admin/v1/policies/label/_preview", firstCall);
        Assert.Single(ZitadelCall().Matches(ProvisionScript), call => call.Value.StartsWith("RETRY=1 ", StringComparison.Ordinal));
        Assert.Equal(["--retry", "5", "--retry-connrefused", "--retry-max-time", "30"], RetryOptions());
    }

    [Fact]
    public void ProvisionScript_CallCurl_AddsRetryOptionsWhenRetryIsSet()
    {
        // RETRY=1 on the first call does nothing unless call's curl line expands RETRY_OPTS.
        string callCurl = CallCurlLine().Match(ProvisionScript).Groups["curl"].Value;

        Assert.Contains("curl \"${CURL_OPTS[@]}\" ${RETRY:+\"${RETRY_OPTS[@]}\"} ", callCurl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionScript_RetryOptions_SucceedAfterTwo503Answers()
    {
        int requests = 0;
        await using WebApplication zitadel = await StartStubZitadelAsync(0, context =>
        {
            bool starting = Interlocked.Increment(ref requests) <= 2;
            context.Response.StatusCode = starting ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK;
            return context.Response.WriteAsync(starting ? "connection refused" : "{\"policy\":{}}", context.RequestAborted);
        });

        string status = await CurlWithRetryOptionsAsync(new Uri(zitadel.Urls.First()));

        Assert.Equal("200", status);
        Assert.Equal(3, Volatile.Read(ref requests));
    }

    [Fact]
    public async Task ProvisionScript_RetryOptions_SucceedAfterTwoRefusedConnections()
    {
        // Nothing listens on the port at first; curl tries at 0 s and 1 s (refused), then at 3 s, when the stub listens.
        int port = FreeLoopbackPort();
        Task<string> curl = CurlWithRetryOptionsAsync(new Uri($"http://127.0.0.1:{port}"));
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await using WebApplication zitadel = await StartStubZitadelAsync(port, context => context.Response.WriteAsync("{\"policy\":{}}", context.RequestAborted));

        Assert.Equal("200", await curl);
    }

    [Fact]
    public async Task ProvisionScript_RetryOptions_DoNotRetryABadRequest()
    {
        int requests = 0;
        await using WebApplication zitadel = await StartStubZitadelAsync(0, context =>
        {
            Interlocked.Increment(ref requests);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });

        string status = await CurlWithRetryOptionsAsync(new Uri(zitadel.Urls.First()));

        Assert.Equal("400", status);
        Assert.Equal(1, Volatile.Read(ref requests));
    }

    [Fact]
    public void Compose_Api_StartsAfterTheGatewayThatHoldsTheIssuerAlias()
    {
        // Before the gateway's auth.localhost alias exists, Docker's DNS passes the name to the host's resolver, which may
        // answer loopback for *.localhost (RFC 6761): the api then fetches the issuer's discovery document from itself.
        // So the api waits for the gateway, and the gateway, which cannot wait for the api too, finds it at run time.
        // The api waits only for the gateway to start: the gateway's health includes the api (the backend case
        // Compose_GatewayHealth_WaitsUntilItReachesTheApi), so healthy would deadlock.
        Assert.Contains("      gateway: { condition: service_started }\n", ComposeService("api"), StringComparison.Ordinal);
        Assert.DoesNotContain("      api: {", ComposeService("gateway"), StringComparison.Ordinal);
        Assert.Contains("server api:8080 resolve;", GatewayTemplate(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("infra/images/gateway/templates/nginx.conf.template")]
    [InlineData("infra/images/gateway/templates-aws/nginx.conf.template")]
    public void GatewayTemplate_EveryAccessLog_RecordsThePathWithoutTheQueryOrTheReferer(string templatePath)
    {
        // 7.12 AC6 (NFR-SEC-5, NFR-OBS-2; replaces 6.6 AC2's identity-only test, AD-20, ASVS 5.0 V14.2.1): a query can
        // carry the sign-in callback's code, the end-session id_token_hint or a Mailpit search, and a Referer can carry a
        // URL with its query, so every origin logs with one path-only format.
        string template = File.ReadAllText(Path.Combine(SourceRoots.Repo, templatePath));
        string[] formats = [.. AccessLog().Matches(template).Select(match => match.Groups["format"].Value)];
        string definition = LogFormat("path_only").Match(template).Groups["definition"].Value;

        // The http-level line (two-space indent) covers every server block; a line without a format name logs nginx's
        // combined format, so every line at http, server or location level must name this one.
        Assert.Matches(HttpLevelPathOnlyAccessLog(), template);
        Assert.All(formats, format => Assert.Equal("path_only", format));
        Assert.Contains("\"$request_method $uri $server_protocol\"", definition, StringComparison.Ordinal);
        Assert.DoesNotMatch(QueryVariables(), definition);
    }

    [Theory]
    [InlineData("infra/images/gateway/templates/nginx.conf.template")]
    [InlineData("infra/images/gateway/templates-aws/nginx.conf.template")]
    public void GatewayTemplate_ApiLocations_SendTheGatewaysOwnErrorsWithNoStore(string templatePath)
    {
        // 7.12 AC7 (AD-23 as amended 2026-10-31): the Api sets Cache-Control: no-store on what it produces, nginx on the
        // /api responses it generates itself. A location that sets error_page inherits none from its server (nginx,
        // error_page), so each /api location repeats the 429 line.
        string template = File.ReadAllText(Path.Combine(SourceRoots.Repo, templatePath));
        string[] locations = [.. MultiLineLocation().Matches(template).Select(match => match.Value)];
        string[] apiLocations = [.. locations.Where(block => block.StartsWith("    location /api/", StringComparison.Ordinal))];

        // /api/ and /api/health/.
        Assert.Equal(2, apiLocations.Length);
        Assert.All(apiLocations, block =>
        {
            Assert.Contains("\n      error_page 429 @rate_limited;\n", block, StringComparison.Ordinal);
            Assert.Contains("\n      error_page 413 500 502 504 @gateway_error;\n", block, StringComparison.Ordinal);
            // The Api's own responses pass through untouched: the gateway neither adds nor hides their Cache-Control,
            // and proxy_intercept_errors stays off, so an upstream error never reaches error_page.
            Assert.DoesNotContain("Cache-Control", block, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("proxy_intercept_errors", block, StringComparison.Ordinal);
        });
        foreach (string name in new[] { "@rate_limited", "@gateway_error" })
        {
            string named = Assert.Single(locations, block => block.StartsWith($"    location {name} {{\n", StringComparison.Ordinal));
            Assert.Contains("\n      include /etc/nginx/security-headers.conf;\n", named, StringComparison.Ordinal);
            Assert.Contains("\n      add_header Cache-Control \"no-store\" always;\n", named, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("infra/images/gateway/templates/nginx.conf.template")]
    [InlineData("infra/images/gateway/templates-aws/nginx.conf.template")]
    public void GatewayTemplate_ProxiedLocations_DropTheClientsForwardedHeader(string templatePath)
    {
        // 6.6 QA O1: Zitadel resolves its instance from an RFC 7239 Forwarded header, so a client's own must not reach an
        // upstream. An empty proxy_set_header value is not passed on, and a location that sets any proxy_set_header
        // inherits none from its server (nginx, proxy_set_header), so each proxied location drops it itself.
        string template = File.ReadAllText(Path.Combine(SourceRoots.Repo, templatePath));
        string[] proxied = [.. MultiLineLocation().Matches(template)
            .Select(match => match.Value)
            .Where(block => block.Contains("proxy_pass ", StringComparison.Ordinal))];

        // Every proxy_pass in the file sits in one of the blocks found, so none is missed.
        Assert.Equal(ProxyPass().Count(template), proxied.Length);
        Assert.All(proxied, block => Assert.Contains("\n      proxy_set_header Forwarded \"\";\n", block, StringComparison.Ordinal));
    }

    // From the "is the file current" test up to the log line it ends with.
    [GeneratedRegex(@"if \[ -s ""\$OUT_FILE"" \][^;]*;[^\n]*then\s*\n\s*log ""\$OUT_FILE is current; secrets not rotated""")]
    private static partial Regex CurrentCheck();

    /// <summary>The script's RETRY_OPTS array, one option or value per item.</summary>
    private static string[] RetryOptions() =>
        RetryOptionsArray().Match(ProvisionScript).Groups["options"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Runs curl with the script's retry options against the preview path and returns the final HTTP status.</summary>
    private static async Task<string> CurlWithRetryOptionsAsync(Uri zitadel)
    {
        // The body goes to a temporary file, as in the script's call(). Not /dev/null: before a retry curl truncates the
        // output file, and curl 8.5.0 (ubuntu-24.04) fails that on /dev/null with exit 23 and prints no status.
        string body = Path.GetTempFileName();
        var start = new ProcessStartInfo("curl") { RedirectStandardOutput = true };
        foreach (string argument in RetryOptions().Concat(["-sS", "-o", body, "-w", "%{http_code}", $"{zitadel}admin/v1/policies/label/_preview"]))
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("curl did not start.");
        string status = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        File.Delete(body);
        return status;
    }

    /// <summary>A stand-in for Zitadel on a loopback port (0 picks a free one): every request gets the given answer.</summary>
    private static async Task<WebApplication> StartStubZitadelAsync(int port, RequestDelegate answer)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port));
        WebApplication app = builder.Build();
        app.Run(answer);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>A Zitadel call in provision.sh with a literal path: call METHOD /path, rpc zitadel.Service/Method or upload /path.</summary>
    [GeneratedRegex(@"(?:RETRY=1 )?(?:call (?:GET|PUT|POST) /|rpc zitadel\.|upload /)\S+")]
    private static partial Regex ZitadelCall();

    /// <summary>The first curl line inside provision.sh's call function.</summary>
    [GeneratedRegex(@"\ncall\(\)\{\n(?:.*\n)*?(?<curl>.*\bcurl\b.*)")]
    private static partial Regex CallCurlLine();

    [GeneratedRegex(@"RETRY_OPTS=\((?<options>[^)]*)\)")]
    private static partial Regex RetryOptionsArray();

    [GeneratedRegex(@"ROLES='(?<roles>[^']*)'")]
    private static partial Regex RolesBlock();

    /// <summary>The lines of one service in docker-compose.yml, its name line excluded, up to the blank line after it.</summary>
    private static string ComposeService(string name) =>
        new Regex(@"\n  " + Regex.Escape(name) + @":[^\n]*\n(?<body>(?:    [^\n]*\n)+)", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Match(Compose).Groups["body"].Value;

    /// <summary>An access_log directive anywhere on a line; the format is empty when none is named (nginx's combined).</summary>
    [GeneratedRegex(@"\baccess_log\s+(?<path>[^\s;]+)\s*(?<format>[^\s;]*)[^;]*;")]
    private static partial Regex AccessLog();

    /// <summary>An http-level access_log (two-space indent) with the path_only format.</summary>
    [GeneratedRegex(@"\n  access_log\s+[^\s;]+\s+path_only\s*;")]
    private static partial Regex HttpLevelPathOnlyAccessLog();

    /// <summary>A location written over several lines: from its opening line to its closing brace (four-space indent).</summary>
    [GeneratedRegex(@"^    location [^\n]*\{\n.*?^    \}$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex MultiLineLocation();

    [GeneratedRegex(@"\bproxy_pass\s")]
    private static partial Regex ProxyPass();

    private static string GatewayTemplate() =>
        File.ReadAllText(Path.Combine(SourceRoots.Repo, "infra/images/gateway/templates/nginx.conf.template"));

    /// <summary>The definition of a log_format: its quoted strings, up to the semicolon.</summary>
    private static Regex LogFormat(string name) => new(@"\n  log_format\s+" + Regex.Escape(name) + @"\s+(?<definition>[^;]+);", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>nginx variables that hold the query string, or the Referer, which can hold a URL with the query.</summary>
    [GeneratedRegex(@"\$(request|request_uri|args|query_string|arg_\w+|http_referer)\b")]
    private static partial Regex QueryVariables();
}
