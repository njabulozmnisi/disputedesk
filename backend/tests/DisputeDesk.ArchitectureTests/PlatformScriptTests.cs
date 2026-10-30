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

    // From the "is the file current" test up to the log line it ends with.
    [GeneratedRegex(@"if \[ -s ""\$OUT_FILE"" \][^;]*;[^\n]*then\s*\n\s*log ""\$OUT_FILE is current; secrets not rotated""")]
    private static partial Regex CurrentCheck();

    /// <summary>The script's RETRY_OPTS array, one option or value per item.</summary>
    private static string[] RetryOptions() =>
        RetryOptionsArray().Match(ProvisionScript).Groups["options"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Runs curl with the script's retry options against the preview path and returns the final HTTP status.</summary>
    private static async Task<string> CurlWithRetryOptionsAsync(Uri zitadel)
    {
        var start = new ProcessStartInfo("curl") { RedirectStandardOutput = true };
        foreach (string argument in RetryOptions().Concat(["-sS", "-o", "/dev/null", "-w", "%{http_code}", $"{zitadel}admin/v1/policies/label/_preview"]))
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("curl did not start.");
        string status = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
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
}
