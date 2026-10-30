using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// AC11 and AC12: what the Zitadel provisioner creates and the headless sign-in script, read from the files. 5.1 AC7: the
/// operations actor mapping it writes, and the operations token script.
/// </summary>
public partial class PlatformScriptTests
{
    private static readonly string ProvisionScript = File.ReadAllText(Path.Combine(SourceRoots.Repo, "infra/zitadel/provision/provision.sh"));
    private static readonly string Compose = File.ReadAllText(Path.Combine(SourceRoots.Repo, "docker-compose.yml"));
    private static readonly string DevLoginPath = Path.Combine(SourceRoots.Repo, "scripts/dev-login.sh");

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
    [InlineData("Bff: { ClientId: $bffId, ClientSecret: .[0] }")]
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
    public void OperationsTokenScript_Source_RunsTheClientCredentialsGrantWithTheSecretOnStdinOnly()
    {
        string script = File.ReadAllText(Path.Combine(SourceRoots.Repo, "scripts/operations-token.sh"));

        // RFC 6749 §4.4 with the AD-9 scope, against the local auth origin unless told otherwise.
        Assert.Contains("AUTH=\"${AUTH_ORIGIN:-http://auth.localhost:8080}\"", script, StringComparison.Ordinal);
        Assert.Contains("\"$AUTH/oauth/v2/token\"", script, StringComparison.Ordinal);
        Assert.Contains("--data-urlencode grant_type=client_credentials", script, StringComparison.Ordinal);
        Assert.Contains("scope=openid urn:zitadel:iam:org:project:id:$project:aud urn:zitadel:iam:org:projects:roles", script, StringComparison.Ordinal);
        // The credentials come from the zitadel-clients volume, through the provisioner image, and reach curl as a config
        // on stdin (-K -): never an argument, which any local user could read from the process list.
        Assert.Contains("--entrypoint cat provision /out/zitadel-clients.json", script, StringComparison.Ordinal);
        Assert.Contains("curl -fsS --max-time 30 -K -", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\s(-u|--user)\s", script);
        Assert.DoesNotContain("client_secret=", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(LiteralBearerToken(), script);
        // Only the access token reaches stdout.
        Assert.Contains("jq -er .access_token", script, StringComparison.Ordinal);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")] // the stub docker needs its execute bit
    public async Task OperationsTokenScript_ClientsFileUnreadable_PrintsOneShortMessageAndFails()
    {
        // A stub docker on PATH stands in for a stack that is down: it writes its own error (with a would-be secret) and
        // fails. The script prints only its own message, nothing from docker, and nothing on stdout.
        string bin = Directory.CreateTempSubdirectory("operations-token-").FullName;
        string docker = Path.Combine(bin, "docker");
        await File.WriteAllTextAsync(docker, "#!/usr/bin/env bash\necho 'stub-secret-from-docker' >&2\nexit 1\n", TestContext.Current.CancellationToken);
        File.SetUnixFileMode(docker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var start = new ProcessStartInfo("bash", [Path.Combine(SourceRoots.Repo, "scripts/operations-token.sh")])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment = { ["PATH"] = $"{bin}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH")}" },
        };

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("bash did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        string errors = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Directory.Delete(bin, recursive: true);

        Assert.NotEqual(0, process.ExitCode);
        Assert.Equal("could not read the clients file from the provision volume; is the stack up?\n", errors);
        Assert.Empty(await output);
    }

    [Fact]
    public void ProvisionScript_DevelopmentMode_ComesFromConfiguration()
    {
        Assert.Contains("\\\"developmentMode\\\":$BFF_DEVELOPMENT_MODE,", ProvisionScript, StringComparison.Ordinal);
        Assert.Contains("BFF_DEVELOPMENT_MODE: ${BFF_DEVELOPMENT_MODE:-true}", Compose, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionScript_BffApp_UsesClientSecretPostAndOnlyTheCodeGrant()
    {
        // The ASP.NET Core handler sends the client secret in the form body and keeps no refresh token.
        Assert.Contains("\\\"authMethodType\\\":\\\"OIDC_AUTH_METHOD_TYPE_POST\\\"", ProvisionScript, StringComparison.Ordinal);
        Assert.Contains("\\\"grantTypes\\\":[\\\"OIDC_GRANT_TYPE_AUTHORIZATION_CODE\\\"]", ProvisionScript, StringComparison.Ordinal);
        Assert.DoesNotContain("OIDC_GRANT_TYPE_REFRESH_TOKEN", ProvisionScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionScript_DevelopmentModeNotABoolean_FailsBeforeCallingZitadel()
    {
        var start = new ProcessStartInfo("bash", [Path.Combine(SourceRoots.Repo, "infra/zitadel/provision/provision.sh")])
        {
            RedirectStandardError = true,
            Environment =
            {
                ["ZITADEL_URL"] = "http://zitadel.invalid",
                ["OUT_DIR"] = Path.GetTempPath(),
                ["OUT_UID"] = "1654",
                ["FIXTURES_FILE"] = "unused.json",
                ["APP_ORIGINS"] = "http://localhost:8080",
                ["BFF_DEVELOPMENT_MODE"] = "yes",
            },
        };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("bash did not start.");
        string errors = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, process.ExitCode);
        Assert.Contains("BFF_DEVELOPMENT_MODE must be true or false", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("infra/zitadel/provision/provision.sh")]
    [InlineData("infra/zitadel/provision/verify.sh")]
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
    public void Compose_ZitadelExternalSecure_ComesFromConfiguration()
    {
        Assert.Contains("ZITADEL_EXTERNALSECURE: ${ZITADEL_EXTERNALSECURE:-false}", Compose, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_PinnedDefaultNetworkSubnet_IsTheApisTrustedNetwork()
    {
        // 6.6 AC1 (AD-23): a mismatch fails silently, every client becoming the gateway's address, so the two must agree.
        // Both read DISPUTEDESK_SUBNET with the same default, so a second stack that moves its subnet moves both.
        string subnet = DefaultNetworkSubnet().Match(Compose).Groups["subnet"].Value;
        string trusted = ApiTrustedNetwork().Match(Compose).Groups["network"].Value;

        Assert.Equal("${DISPUTEDESK_SUBNET:-10.201.89.0/24}", subnet);
        Assert.Equal(subnet, trusted);
        Assert.Contains("      ForwardedHeaders__AllowedHosts: localhost\n", Compose, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("core-banking")]
    [InlineData("api")]
    [InlineData("worker")]
    public void Compose_DotNetService_RunsUnderAnInitProcess(string service)
    {
        // A .NET crash (a failed start included) ends in abort(). As a container's PID 1, dotnet never receives that
        // SIGABRT (pid_namespaces(7)) and spins instead of exiting, so a restart policy never sees the crash.
        Assert.Matches(new Regex(@"^    init: true\b", RegexOptions.Multiline, TimeSpan.FromSeconds(1)), ComposeService(service));
    }

    [Fact]
    public void GatewayTemplate_IdentityOriginAccessLog_RecordsThePathWithoutTheQuery()
    {
        // 6.6 AC2 (AD-20, ASVS 5.0 V14.2.1): the end-session GET carries the ID token as id_token_hint in its query.
        string template = GatewayTemplate();
        int serverName = template.IndexOf("server_name ${GATEWAY_AUTH_SERVER_NAME};", StringComparison.Ordinal);
        // The identity server block, from its `server {` to its closing brace (two-space indent), its locations included.
        int start = template.LastIndexOf("\n  server {", serverName, StringComparison.Ordinal);
        string identityServer = template[start..template.IndexOf("\n  }\n", serverName, StringComparison.Ordinal)];

        // Every access_log in the block, at server or location level, so a location cannot log with another format.
        string[] formats = [.. AccessLog().Matches(identityServer).Select(match => match.Groups["format"].Value)];
        string definition = LogFormat("identity").Match(template).Groups["definition"].Value;

        // The server-level line (four-space indent) covers every location, so one location's own line is not enough.
        Assert.Matches(ServerLevelIdentityAccessLog(), identityServer);
        Assert.All(formats, format => Assert.Equal("identity", format));
        Assert.Contains("$uri", definition, StringComparison.Ordinal);
        Assert.DoesNotMatch(QueryVariables(), definition);
    }

    [Fact]
    public void GatewayTemplate_ProxiedLocations_DropTheClientsForwardedHeader()
    {
        // 6.6 QA O1: Zitadel resolves its instance from an RFC 7239 Forwarded header, so a client's own must not reach an
        // upstream. An empty proxy_set_header value is not passed on, and a location that sets any proxy_set_header
        // inherits none from its server (nginx, proxy_set_header), so each proxied location drops it itself.
        string template = GatewayTemplate();
        string[] proxied = [.. MultiLineLocation().Matches(template)
            .Select(match => match.Value)
            .Where(block => block.Contains("proxy_pass ", StringComparison.Ordinal))];

        // Every proxy_pass in the file sits in one of the blocks found, so none is missed.
        Assert.Equal(ProxyPass().Count(template), proxied.Length);
        Assert.All(proxied, block => Assert.Contains("\n      proxy_set_header Forwarded \"\";\n", block, StringComparison.Ordinal));
    }

    [Fact]
    public void ApiProgram_ForwardedHeaders_RunFirstInThePipeline()
    {
        // 6.6 AC1: before anything reads the client address, the scheme or the host.
        string program = File.ReadAllText(Path.Combine(SourceRoots.Src, "DisputeDesk.Api/Program.cs"));
        string pipeline = program[program.IndexOf("builder.Build();", StringComparison.Ordinal)..];

        Assert.Equal("app.UseForwardedHeaders();", FirstMiddleware().Match(pipeline).Value);
    }

    [Fact]
    public async Task DevLoginScript_Help_DescribesAHeadlessSignInThatWritesACookieJar()
    {
        var start = new ProcessStartInfo("bash", [DevLoginPath, "--help"]) { RedirectStandardOutput = true };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("bash did not start.");
        string help = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("Usage: scripts/dev-login.sh <login> [cookie-jar]", help, StringComparison.Ordinal);
        Assert.Contains("no browser", help, StringComparison.Ordinal);
        Assert.Contains("cookie jar", help, StringComparison.Ordinal);
    }

    [Fact]
    public void DevLoginScript_Source_HoldsNoSecretAndNoBrowser()
    {
        string script = File.ReadAllText(DevLoginPath);

        // Passwords come from fixtures/local.json; the login client's PAT is read inside its container at run time.
        Assert.DoesNotContain("Dev-Only-", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(LiteralBearerToken(), script);
        Assert.DoesNotContain("playwright", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fixtures/local.json", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("heartbeat-relay", 30)] // AD-25: the relay's loop is at most 10 seconds
    [InlineData("heartbeat-sweep", 90)] // story 5.3 AC7: three missed 30-second sweep loops (decided 2026-10-27)
    public void Compose_WorkerHealthCheck_FailsWhenTheHeartbeatIsOlderThanItsThreshold(string file, int seconds)
    {
        // The worker's health check compares each file's modification time with now, in seconds, as one shell test.
        Match check = Regex.Match(
            Compose,
            @"\$\$\(\( now - \$\$\(/usr/local/bin/busybox stat -c %Y /tmp/" + Regex.Escape(file) + @"\) \)\) -le (?<seconds>\d+)",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        Assert.True(check.Success, $"The worker's health check does not test /tmp/{file}.");
        Assert.Equal(seconds, int.Parse(check.Groups["seconds"].Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("now=$$(/usr/local/bin/busybox date +%s) && test", Compose, StringComparison.Ordinal);
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

    [GeneratedRegex(@"Bearer [A-Za-z0-9_\-\.]{20,}")]
    private static partial Regex LiteralBearerToken();

    /// <summary>The lines of one service in docker-compose.yml, its name line excluded, up to the blank line after it.</summary>
    private static string ComposeService(string name) =>
        new Regex(@"\n  " + Regex.Escape(name) + @":[^\n]*\n(?<body>(?:    [^\n]*\n)+)", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Match(Compose).Groups["body"].Value;

    /// <summary>The top-level networks.default.ipam.config subnet in docker-compose.yml.</summary>
    [GeneratedRegex(@"\nnetworks:\n  default:\n    ipam:\n      config:\n        - subnet: (?<subnet>\S+)\n")]
    private static partial Regex DefaultNetworkSubnet();

    [GeneratedRegex(@"\n      ForwardedHeaders__TrustedNetwork: (?<network>\S+)\n")]
    private static partial Regex ApiTrustedNetwork();

    /// <summary>An access_log directive anywhere on a line; the format is empty when none is named (nginx's combined).</summary>
    [GeneratedRegex(@"\baccess_log\s+(?<path>[^\s;]+)\s*(?<format>[^\s;]*)[^;]*;")]
    private static partial Regex AccessLog();

    /// <summary>A server-level access_log (four-space indent) with the identity format.</summary>
    [GeneratedRegex(@"\n    access_log\s+[^\s;]+\s+identity\s*;")]
    private static partial Regex ServerLevelIdentityAccessLog();

    /// <summary>A location written over several lines: from its opening line to its closing brace (four-space indent).</summary>
    [GeneratedRegex(@"^    location [^\n]*\{\n.*?^    \}$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex MultiLineLocation();

    [GeneratedRegex(@"\bproxy_pass\s")]
    private static partial Regex ProxyPass();

    private static string GatewayTemplate() =>
        File.ReadAllText(Path.Combine(SourceRoots.Repo, "infra/nginx/templates/nginx.conf.template"));

    /// <summary>The definition of a log_format: its quoted strings, up to the semicolon.</summary>
    private static Regex LogFormat(string name) => new(@"\n  log_format\s+" + Regex.Escape(name) + @"\s+(?<definition>[^;]+);", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>nginx variables that hold the query string, or the Referer, which can hold a URL with the query.</summary>
    [GeneratedRegex(@"\$(request|request_uri|args|query_string|arg_\w+|http_referer)\b")]
    private static partial Regex QueryVariables();

    /// <summary>The first app.Use... call.</summary>
    [GeneratedRegex(@"app\.Use\w+\([^)]*\);")]
    private static partial Regex FirstMiddleware();
}
