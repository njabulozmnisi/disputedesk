using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace DisputeDesk.PlatformTests;

/// <summary>
/// AC11: what the Zitadel provisioner creates, and the operations allowlist it writes. 7.12 AC6 and AC7: the
/// gateway templates' access log format and their /api error pages; GatewayTests proves the rest by response.
/// The provision.sh cases read the file because no run on this machine reaches a Zitadel call: the script first runs
/// /label-policy.sh and reads /brand/, which only the provisioner image holds.
/// </summary>
[UnsupportedOSPlatform("windows")] // the stubs need their execute bit
public partial class PlatformScriptTests
{
    private static readonly string ProvisionScript = File.ReadAllText(Path.Combine(SourceRoots.Repo, "infra/images/zitadel/provision.sh"));

    [Fact]
    public void ProvisionScript_ProjectRoles_AreTheFourCamelCaseKeys()
    {
        // Text: no run here reaches the role calls (the class summary says why).
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
        // Text: no run here reaches the clients file (the class summary says why).
        Assert.Contains(key, ProvisionScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionScript_ClientsFile_ListsTheMachineUserIdAsTheOnlyAllowedClient()
    {
        // AD-26: Operations:AllowedClients = [the machine user's ID, its tokens' sub].
        Assert.Contains("--arg opsUserId \"$OPS_ID\"", ProvisionScript, StringComparison.Ordinal);
        Assert.Contains("Operations: { AllowedClients: [$opsUserId] }", ProvisionScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionScript_CurrentCheck_AlsoRequiresExactlyTheAllowedClientsList()
    {
        // Exact array equality: a file in the old shape (a map of subs to actor types), or with any other list, is not
        // current, so it is rewritten, with new secrets, as any file that is not current is.
        string currentCheck = CurrentCheck().Match(ProvisionScript).Value;

        Assert.Contains("is current; secrets not rotated", currentCheck, StringComparison.Ordinal);
        Assert.Contains("jq -e --arg ops \"$OPS_ID\" '.Operations.AllowedClients == [$ops]' \"$OUT_FILE\" >/dev/null; then", currentCheck, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "true")]
    [InlineData("false", "false")]
    public async Task ProvisionScript_DevelopmentMode_ComesFromConfiguration(string? set, string expected)
    {
        // Compose gives the provisioner ${AUTH_DEVELOPMENT_MODE:-true}: true unless the variable says otherwise.
        Dictionary<string, string> variables = set is null ? [] : new() { ["AUTH_DEVELOPMENT_MODE"] = set };

        JsonNode model = await ComposeModel.RenderAsync(["docker-compose.yml"], variables: variables);

        Assert.Equal(expected, model["services"]!["provision"]!["environment"]!["AUTH_DEVELOPMENT_MODE"]!.GetValue<string>());
        // Text: no run here reaches the app's creation (the class summary says why).
        Assert.Contains("\\\"developmentMode\\\":$AUTH_DEVELOPMENT_MODE,", ProvisionScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionScript_WebApp_UsesClientSecretPostAndOnlyTheCodeGrant()
    {
        // The ASP.NET Core handler sends the client secret in the form body and keeps no refresh token. Text: no run here
        // reaches the app's creation (the class summary says why).
        Assert.Contains("\\\"authMethodType\\\":\\\"OIDC_AUTH_METHOD_TYPE_POST\\\"", ProvisionScript, StringComparison.Ordinal);
        Assert.Contains("\\\"grantTypes\\\":[\\\"OIDC_GRANT_TYPE_AUTHORIZATION_CODE\\\"]", ProvisionScript, StringComparison.Ordinal);
        Assert.DoesNotContain("OIDC_GRANT_TYPE_REFRESH_TOKEN", ProvisionScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionScript_DevelopmentModeNotABoolean_FailsBeforeCallingZitadel()
    {
        // Expected: no curl call, exit 1, one message on standard error.
        using var stubs = new PathStubs("curl");
        Dictionary<string, string> environment = stubs.Environment();
        environment["ZITADEL_URL"] = "http://zitadel.invalid";
        environment["OUT_DIR"] = stubs.Directory;
        environment["OUT_UID"] = "1654";
        environment["FIXTURES_FILE"] = "unused.json";
        environment["APP_ORIGINS"] = "http://localhost:8080";
        environment["AUTH_DEVELOPMENT_MODE"] = "yes";

        ProcessResult run = await Processes.RunAsync("bash", ["infra/images/zitadel/provision.sh"], environment, TimeSpan.FromSeconds(30));

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("[provision] AUTH_DEVELOPMENT_MODE must be true or false, not 'yes'\n", run.Errors);
        Assert.Empty(run.Output);
        Assert.Empty(stubs.Calls());
    }

    [Fact]
    public void ProvisionScript_CurlOptions_SetAMaxTime()
    {
        // Every Zitadel call in the script goes through curl "${CURL_OPTS[@]}", so this one option times them all out.
        // Text: no run here reaches a curl call (the class summary says why).
        Assert.Contains("CURL_OPTS=(--max-time ", ProvisionScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyScript_FirstZitadelCall_SetsAMaxTime()
    {
        // Every Zitadel call in verify.sh goes through curl "${CURL_OPTS[@]}". Expected: one curl call, to GetProject with
        // --max-time 30 first. Its arguments name a random header file, so the stub has no answer for it: the call exits
        // 97, which ends the run with that code.
        using var stubs = new PathStubs("curl");
        await File.WriteAllTextAsync(Path.Combine(stubs.Directory, "zitadel-clients.json"), """{"Zitadel":{"ProjectId":"p1","WebApp":{"ClientId":"c1"}}}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(stubs.Directory, "pat"), "test-only-pat", TestContext.Current.CancellationToken);
        Dictionary<string, string> environment = stubs.Environment();
        environment["ZITADEL_URL"] = "http://zitadel.invalid";
        environment["FIXTURES_FILE"] = "unused.json";
        environment["OUT_DIR"] = stubs.Directory;
        environment["PAT_FILE"] = Path.Combine(stubs.Directory, "pat");

        ProcessResult run = await Processes.RunAsync("bash", ["infra/images/zitadel/verify.sh"], environment, TimeSpan.FromSeconds(30));

        Assert.Equal(97, run.ExitCode);
        Assert.Empty(run.Output);
        string[] call = Assert.Single(stubs.Calls("curl")).Arguments;
        Assert.Equal(["--max-time", "30"], call[..2]);
        Assert.Contains("http://zitadel.invalid/zitadel.project.v2.ProjectService/GetProject", call);
    }

    [Fact]
    public void ProvisionScript_FirstZitadelCall_IsTheOnlyOneThatRetries()
    {
        // Zitadel can refuse connections or answer 503 for a moment after its health check passes (story 2.6, QA F1).
        // Text: no run here reaches a curl call (the class summary says why); the retry cases below run the options.
        string firstCall = ZitadelCall().Match(ProvisionScript).Value;

        Assert.Equal("RETRY=1 call GET /admin/v1/policies/label/_preview", firstCall);
        Assert.Single(ZitadelCall().Matches(ProvisionScript), call => call.Value.StartsWith("RETRY=1 ", StringComparison.Ordinal));
        Assert.Equal(["--retry", "5", "--retry-connrefused", "--retry-max-time", "30"], RetryOptions());
    }

    [Fact]
    public void ProvisionScript_CallCurl_AddsRetryOptionsWhenRetryIsSet()
    {
        // RETRY=1 on the first call does nothing unless call's curl line expands RETRY_OPTS. Text: no run here reaches a
        // curl call (the class summary says why).
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
    public async Task Compose_Api_StartsAfterTheGatewayThatHoldsTheIssuerAlias()
    {
        // Before the gateway's auth.localhost alias exists, Docker's DNS passes the name to the host's resolver, which may
        // answer loopback for *.localhost (RFC 6761): the api then fetches the issuer's discovery document from itself.
        // So the api waits for the gateway, and the gateway, which cannot wait for the api too, finds it at run time.
        // The api waits only for the gateway to start: the gateway's health includes the api (the backend case
        // Compose_GatewayHealth_WaitsUntilItReachesTheApi), so healthy would deadlock.
        JsonNode services = (await ComposeModel.RenderAsync(["docker-compose.yml"]))["services"]!;
        string template = await File.ReadAllTextAsync(Path.Combine(SourceRoots.Repo, "infra/images/gateway/templates/nginx.conf.template"), TestContext.Current.CancellationToken);

        Assert.Equal("service_started", services["api"]!["depends_on"]!["gateway"]!["condition"]!.GetValue<string>());
        Assert.False(services["gateway"]!["depends_on"]!.AsObject().ContainsKey("api"));
        // Text: the template's upstream line is the contract that the api is found at run time.
        Assert.Matches(ApiUpstreamResolvedAtRunTime(), template);
    }

    [Theory]
    [InlineData("infra/images/gateway/templates/nginx.conf.template")]
    [InlineData("infra/images/gateway/templates-aws/nginx.conf.template")]
    public void GatewayTemplate_EveryAccessLog_RecordsThePathWithoutTheQueryOrTheReferer(string templatePath)
    {
        // 7.12 AC6 (NFR-SEC-5, NFR-OBS-2; replaces 6.6 AC2's identity-only test, AD-20, ASVS 5.0 V14.2.1): a query can
        // carry the sign-in callback's code, the end-session id_token_hint or a Mailpit search, and a Referer can carry a
        // URL with its query, so every origin logs with one path-only format. Text: the log_format definition is the
        // contract (GatewayTests.AccessLogs_RecordThePath_NeverTheQueryOrTheReferer shows it on both images).
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
    public void GatewayTemplate_ApiLocations_SendTheGatewaysOwnErrorsToGatewayError(string templatePath)
    {
        // 7.12 AC7 (AD-23 as amended 2026-10-31): GatewayTests shows the 413 and 502 with no-store on /api/ and
        // /api/health/ of both images. Text: a 504 needs a 60-second upstream stall per image (nginx's default
        // proxy_read_timeout, which no template sets), so this directive's status list is the contract.
        string template = File.ReadAllText(Path.Combine(SourceRoots.Repo, templatePath));

        // /api/ and /api/health/, each with the full list in its own block.
        Match[] locations = ApiLocation().Matches(template).ToArray();

        Assert.Equal(["/api/", "/api/health/"], locations.Select(location => location.Groups["path"].Value).Order(StringComparer.Ordinal));
        Assert.All(locations, location => Assert.Matches(GatewayErrorPage(), location.Groups["body"].Value));
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
        ProcessResult curl = await Processes.RunAsync("curl", [.. RetryOptions(), "-sS", "-o", body, "-w", "%{http_code}", $"{zitadel}admin/v1/policies/label/_preview"],
            Processes.BaseEnvironment(), TimeSpan.FromSeconds(60));
        File.Delete(body);
        return curl.Output;
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

    /// <summary>An access_log directive anywhere on a line; the format is empty when none is named (nginx's combined).</summary>
    [GeneratedRegex(@"\baccess_log\s+(?<path>[^\s;]+)\s*(?<format>[^\s;]*)[^;]*;")]
    private static partial Regex AccessLog();

    /// <summary>An http-level access_log (two-space indent) with the path_only format.</summary>
    [GeneratedRegex(@"\n  access_log\s+[^\s;]+\s+path_only\s*;")]
    private static partial Regex HttpLevelPathOnlyAccessLog();

    /// <summary>A prefix location under /api/: its path and its block, which holds no nested block, only ${VARIABLE}s.</summary>
    [GeneratedRegex(@"\blocation\s+(?<path>/api/[^\s{]*)\s*\{(?<body>(?:[^{}]|\$\{\w+\})*)\}")]
    private static partial Regex ApiLocation();

    [GeneratedRegex(@"\berror_page\s+413\s+500\s+502\s+504\s+@gateway_error\s*;")]
    private static partial Regex GatewayErrorPage();

    [GeneratedRegex(@"\bserver\s+api:8080\s+resolve\s*;")]
    private static partial Regex ApiUpstreamResolvedAtRunTime();

    /// <summary>The definition of a log_format: its quoted strings, up to the semicolon.</summary>
    private static Regex LogFormat(string name) => new(@"\n  log_format\s+" + Regex.Escape(name) + @"\s+(?<definition>[^;]+);", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>nginx variables that hold the query string, or the Referer, which can hold a URL with the query.</summary>
    [GeneratedRegex(@"\$(request|request_uri|args|query_string|arg_\w+|http_referer)\b")]
    private static partial Regex QueryVariables();
}
