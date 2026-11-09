using System.Collections.Concurrent;
using System.Globalization;
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
/// The provision.sh runs here stop at its first Zitadel call, which a stub curl cannot answer (its arguments name a
/// random temporary file); the cases after that point read the file, because the script then runs
/// /provision/label-policy.sh and reads /brand/, which only the zitadel image holds. infra/tests/smoke.sh runs it whole.
/// The branding cases run a copy with those two paths moved, against a stand-in for Zitadel, up to the call after the
/// activation.
/// </summary>
[UnsupportedOSPlatform("windows")] // the stubs need their execute bit
public partial class PlatformScriptTests
{
    private const string PublicOrigin = "https://auth.public.test";
    private const string LogoPath = "/assets/v1/1/policy/label/logo-1";
    private const string DarkLogoPath = "/assets/v1/1/policy/label/logo-dark-1";
    private const string UploadFont = "POST /assets/v1/instance/policy/label/font";
    private const string UploadLogo = "POST /assets/v1/instance/policy/label/logo";
    private const string UploadDarkLogo = "POST /assets/v1/instance/policy/label/logo/dark";
    private const string Activate = "POST /admin/v1/policies/label/_activate";
    private const int ClosesTheConnection = 0;

    private static readonly string ProvisionScript = File.ReadAllText(Path.Combine(SourceRoots.Repo, "infra/images/zitadel/provision.sh"));
    private static readonly byte[] MountedLogo = "the mounted logo"u8.ToArray();
    private static readonly byte[] MountedDarkLogo = "the mounted dark logo"u8.ToArray();
    private static readonly byte[] OlderLogo = "an older logo"u8.ToArray();

    [Fact]
    public void GitHubDownloads_EveryCurl_BoundsItsRetries()
    {
        // Each is a GET of a release file, so a retry is safe; --retry-max-time ends the retries inside the job's timeout.
        // The shortest timeout of a job that downloads, floor-guard aside (the next test), is gitleaks' 10 minutes.
        string[] curls = [.. Directory.GetFiles(Path.Combine(SourceRoots.Repo, ".github"), "*", SearchOption.AllDirectories)
            .SelectMany(File.ReadLines)
            .Where(line => line.Contains("curl -", StringComparison.Ordinal))];

        Assert.NotEmpty(curls);
        Assert.All(curls, curl => Assert.Contains("--retry-max-time", curl, StringComparison.Ordinal));
        Assert.All(curls, curl => Assert.True(WorstCaseSeconds(curl) < 600, curl));
    }

    [Fact]
    public void FloorGuardDownloads_WorstCase_LeavesRoomInsideTheJobTimeout()
    {
        // Its other steps take about a minute on main.
        const int RoomForOtherStepsSeconds = 100;
        string job = FloorGuardJob().Match(File.ReadAllText(Path.Combine(SourceRoots.Repo, ".github/workflows/ci.yml"))).Groups["job"].Value;
        int timeoutSeconds = 60 * int.Parse(JobTimeout().Match(job).Groups["minutes"].Value, CultureInfo.InvariantCulture);
        string[] curls = [.. job.Split('\n').Where(line => line.Contains("curl -", StringComparison.Ordinal))];

        Assert.Equal(2, curls.Length);
        Assert.All(curls, curl => Assert.Contains("--max-time 60 ", curl, StringComparison.Ordinal));
        Assert.True(curls.Sum(WorstCaseSeconds) + RoomForOtherStepsSeconds <= timeoutSeconds, string.Join('\n', curls));
    }

    /// <summary>Curl starts no retry after --retry-max-time, so the last attempt can start just inside it, wait --retry-delay and run for --max-time.</summary>
    private static int WorstCaseSeconds(string curl)
    {
        MatchCollection times = CurlTimes().Matches(curl);

        Assert.Equal(3, times.Count);
        return times.Sum(time => int.Parse(time.Groups["seconds"].Value, CultureInfo.InvariantCulture));
    }

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

        Assert.Equal(expected, model["services"]!["zitadel"]!["environment"]!["PROVISION_AUTH_DEVELOPMENT_MODE"]!.GetValue<string>());
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
    public async Task ProvisionScript_DevelopmentModeNotABoolean_RemovesTheMarkerAndFailsBeforeCallingZitadel()
    {
        // Expected: the marker from an earlier run gone (so the container turns unhealthy), no call, exit 1, one message.
        using var stubs = new PathStubs("curl", "sleep", "zitadel");
        Dictionary<string, string> environment = ProvisionEnvironment(stubs);
        environment["PROVISION_AUTH_DEVELOPMENT_MODE"] = "yes";
        await File.WriteAllTextAsync(environment["PROVISION_MARKER_FILE"], "", TestContext.Current.CancellationToken);

        ProcessResult run = await RunProvisionAsync(environment);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("[provision] PROVISION_AUTH_DEVELOPMENT_MODE must be true or false, not 'yes'\n", run.Errors);
        Assert.Empty(run.Output);
        Assert.Empty(stubs.Calls());
        Assert.False(File.Exists(environment["PROVISION_MARKER_FILE"]));
    }

    [Fact]
    public async Task ProvisionScript_WaitsForZitadelReady_BeforeReadingThePat()
    {
        // The hook starts before Zitadel listens, and Zitadel writes the PAT during its setup. Here `zitadel ready` fails
        // twice, then writes the PAT and succeeds, as Zitadel does; reading the PAT earlier would fail the run on it.
        using var stubs = new PathStubs("curl", "sleep");
        Dictionary<string, string> environment = ProvisionEnvironment(stubs);
        WriteZitadelReadyStub(stubs, failures: 2, writesPat: environment["PROVISION_PAT_FILE"]);
        stubs.Answer("sleep", ["2"]);

        ProcessResult run = await RunProvisionAsync(environment);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Equal(3, ZitadelReadyCalls(stubs));
        Assert.Equal(2, stubs.Calls("sleep").Length);
        Assert.Contains("[provision] Zitadel is ready\n", run.Errors, StringComparison.Ordinal);
        // The stub curl fails the call (exit 97), and a failed call ends the run there, inside $(...) too.
        Assert.EndsWith("[provision] FAIL GET /admin/v1/policies/label/_preview -> curl exit 97\n", run.Errors, StringComparison.Ordinal);
        // The first call: the label policy preview, at the public URL, connected to this container, with the time limit
        // every Zitadel call takes from CURL_OPTS.
        string[] call = Assert.Single(stubs.Calls("curl")).Arguments;
        Assert.Equal(["--max-time", "30", "--connect-to", "auth.localhost:8080:127.0.0.1:8080"], call[..4]);
        Assert.Contains("http://auth.localhost:8080/admin/v1/policies/label/_preview", call);
        Assert.False(File.Exists(environment["PROVISION_MARKER_FILE"]));
    }

    [Fact]
    public async Task ProvisionScript_ZitadelReadyWithoutAPat_FailsBeforeAnyCall()
    {
        using var stubs = new PathStubs("curl", "sleep");
        Dictionary<string, string> environment = ProvisionEnvironment(stubs);
        WriteZitadelReadyStub(stubs, failures: 0, writesPat: null);

        ProcessResult run = await RunProvisionAsync(environment);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal($"[provision] Zitadel is ready but {environment["PROVISION_PAT_FILE"]} is missing or empty\n", run.Errors);
        Assert.Empty(stubs.Calls());
    }

    [Fact]
    public async Task ProvisionScript_ZitadelNeverReady_FailsAtTheDeadline()
    {
        // The deadline is 300 s since the script started (bash's SECONDS). bash takes SECONDS from its environment as
        // an assignment, so the run starts at the deadline and fails on its first unready answer instead of waiting.
        using var stubs = new PathStubs("curl", "sleep");
        Dictionary<string, string> environment = ProvisionEnvironment(stubs);
        environment["SECONDS"] = "300";
        WriteZitadelReadyStub(stubs, failures: int.MaxValue, writesPat: null);

        ProcessResult run = await RunProvisionAsync(environment);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("[provision] Zitadel was not ready within 300 s\n", run.Errors);
        Assert.Equal(1, ZitadelReadyCalls(stubs));
        Assert.Empty(stubs.Calls());
    }

    [Fact]
    public void ProvisionScript_Issuer_IsZitadelsDiscoveryAnswerNotTheCallUrl()
    {
        // In AWS the call URL is http://AUTH_HOST and the issuer https://AUTH_HOST (ComposeRenderTests). Text: no run
        // here gets past the first call (the class summary says why).
        Assert.Contains("ISSUER=$(call GET /.well-known/openid-configuration | jq -er .issuer)", ProvisionScript, StringComparison.Ordinal);
        Assert.Contains("--arg issuer \"$ISSUER\"", ProvisionScript, StringComparison.Ordinal);
        Assert.Contains("[ \"$(jq -r '.Zitadel.Issuer' \"$OUT_FILE\")\" = \"$ISSUER\" ]", ProvisionScript, StringComparison.Ordinal);
        Assert.DoesNotContain("--arg issuer \"$Z\"", ProvisionScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionScript_Marker_IsRemovedFirstAndWrittenOnlyAtTheEnd_AndTheClientsFileIsOwnerAndGroupReadOnly()
    {
        // Text: no run here reaches the end (the class summary says why). set -e ends the run at any failed step, so a
        // marker written last means every step passed. The api reads the file through Zitadel's group (group_add).
        string[] lines = ProvisionScript.Split('\n');
        string[] code = [.. lines.Where(line => line.Length > 0 && !line.StartsWith('#'))];

        Assert.Equal("set -euo pipefail", code[0]);
        Assert.Equal("MARKER=\"${PROVISION_MARKER_FILE:?}\"", code[1]);
        Assert.Equal("rm -f \"$MARKER\"", code[2]);
        Assert.Equal(["touch \"$MARKER\"", "log \"done\""], code[^2..]);
        Assert.Single(lines, line => line.Contains("$MARKER", StringComparison.Ordinal) && line.Contains("touch", StringComparison.Ordinal));
        Assert.Contains("  chmod 0440 \"$OUT_FILE.tmp\"", lines);
        Assert.DoesNotContain("chown", ProvisionScript, StringComparison.Ordinal);
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
        environment["PROVISION_CALL_URL"] = "http://zitadel.invalid";
        environment["PROVISION_CONNECT_TO"] = "zitadel.invalid:80:127.0.0.1:8080";
        environment["PROVISION_FIXTURES_FILE"] = "unused.json";
        environment["PROVISION_OUT_DIR"] = stubs.Directory;
        environment["PROVISION_PAT_FILE"] = Path.Combine(stubs.Directory, "pat");

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
        // Zitadel can refuse connections or answer 503 for a moment after it reports ready (story 2.6, QA F1).
        // Text: the stub curl cannot answer the first call (the class summary says why); the retry cases below run the options.
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
    public async Task ProvisionScript_ServedLogosMatchTheMountedFiles_UploadsNothingAndStillActivates()
    {
        // The second run of infra/tests/smoke.sh: nothing uploaded. The preview's URLs are https on the public host, as in
        // AWS, and still reach the stand-in, because only their path is used. The font is never downloaded.
        BrandingRun branding = await RunProvisionBrandingAsync(MountedLogo, MountedDarkLogo);

        Assert.Contains("[provision] label policy activated\n", branding.Run.Errors, StringComparison.Ordinal);
        Assert.DoesNotContain("uploaded", branding.Run.Errors, StringComparison.Ordinal);
        Assert.Empty(branding.Uploads);
        Assert.Equal([$"GET {LogoPath}", $"GET {DarkLogoPath}"], branding.Downloads.Select(download => download.Line));
        Assert.All(branding.Downloads, download => Assert.False(download.HasToken));
        Assert.DoesNotContain(branding.Requests, request => request.Line.Contains("font", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProvisionScript_OneServedLogoDiffers_UploadsOnlyThatOneThenActivates(bool lightDiffers)
    {
        BrandingRun branding = await RunProvisionBrandingAsync(
            lightDiffers ? OlderLogo : MountedLogo,
            lightDiffers ? MountedDarkLogo : OlderLogo);

        string upload = lightDiffers ? UploadLogo : UploadDarkLogo;
        Assert.Equal([upload], branding.Uploads);
        string[] requests = [.. branding.Requests.Select(request => request.Line)];
        Assert.True(Array.IndexOf(requests, upload) < Array.IndexOf(requests, Activate), string.Join('\n', requests));
        Assert.Contains("[provision] label policy activated\n", branding.Run.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionScript_NothingUploadedYet_UploadsTheFontAndBothLogosWithoutDownloading()
    {
        BrandingRun branding = await RunProvisionBrandingAsync(null, null, hasFont: false);

        Assert.Equal([UploadFont, UploadLogo, UploadDarkLogo], branding.Uploads);
        Assert.Empty(branding.Downloads);
        Assert.Contains("[provision] label policy activated\n", branding.Run.Errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(StatusCodes.Status302Found, "302")]
    [InlineData(StatusCodes.Status404NotFound, "404")]
    [InlineData(StatusCodes.Status500InternalServerError, "500")]
    [InlineData(ClosesTheConnection, "curl exit")] // the code depends on how the platform reports the closed connection
    public async Task ProvisionScript_ServedLogoDownloadFails_FailsTheRunWithoutUploadingOrActivating(int answer, string reason)
    {
        BrandingRun branding = await RunProvisionBrandingAsync(MountedLogo, MountedDarkLogo, downloadAnswer: answer);

        Assert.Equal(1, branding.Run.ExitCode);
        Assert.Contains($"[provision] FAIL GET {LogoPath} -> {reason}", branding.Run.Errors, StringComparison.Ordinal);
        Assert.Empty(branding.Uploads);
        Assert.DoesNotContain(branding.Requests, request => request.Line == Activate);
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

    /// <summary>provision.sh's inputs as docker-compose.yml sets them, with the files in the stubs' folder.</summary>
    private static Dictionary<string, string> ProvisionEnvironment(PathStubs stubs)
    {
        Dictionary<string, string> environment = stubs.Environment();
        environment["PROVISION_CALL_URL"] = "http://auth.localhost:8080";
        environment["PROVISION_CONNECT_TO"] = "auth.localhost:8080:127.0.0.1:8080";
        environment["PROVISION_PAT_FILE"] = Path.Combine(stubs.Directory, "admin.pat");
        environment["PROVISION_OUT_DIR"] = stubs.Directory;
        environment["PROVISION_FIXTURES_FILE"] = "unused.json";
        environment["PROVISION_MARKER_FILE"] = Path.Combine(stubs.Directory, "provisioned");
        environment["PROVISION_APP_ORIGINS"] = "http://localhost:8080";
        environment["PROVISION_AUTH_DEVELOPMENT_MODE"] = "true";
        return environment;
    }

    private static Task<ProcessResult> RunProvisionAsync(Dictionary<string, string> environment) =>
        Processes.RunAsync("bash", ["infra/images/zitadel/provision.sh"], environment, TimeSpan.FromSeconds(30));

    /// <summary>
    /// A stand-in for `zitadel ready` that fails the first FAILURES calls, then succeeds, writing the PAT file first when
    /// one is named, as Zitadel writes it during setup before it reports ready. Each call adds a line to zitadel.calls.
    /// </summary>
    private static void WriteZitadelReadyStub(PathStubs stubs, int failures, string? writesPat)
    {
        string stub = Path.Combine(stubs.Directory, "zitadel");
        string writePat = writesPat is null ? "" : $"printf 'test-only-pat\\n' > '{writesPat}'";
        File.WriteAllText(stub, $"""
            #!/usr/bin/env bash
            [ "$*" = ready ] || exit 97
            echo ready >> "$(dirname "$0")/zitadel.calls"
            [ "$(wc -l < "$(dirname "$0")/zitadel.calls")" -gt {failures} ] || exit 1
            {writePat}

            """);
        File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static int ZitadelReadyCalls(PathStubs stubs) => File.ReadAllLines(Path.Combine(stubs.Directory, "zitadel.calls")).Length;

    /// <summary>
    /// Runs a copy of provision.sh, with /brand/ and /provision/label-policy.sh moved to files the test holds, against a
    /// stand-in for Zitadel that serves the given logos (null: none yet). A download answers with the given status, or
    /// closes the connection. The run stops at the first call after the activation, which the stand-in refuses.
    /// </summary>
    private static async Task<BrandingRun> RunProvisionBrandingAsync(
        byte[]? servedLogo, byte[]? servedDarkLogo, bool hasFont = true, int downloadAnswer = StatusCodes.Status200OK)
    {
        var policy = new JsonObject();
        if (hasFont)
        {
            policy["fontUrl"] = $"{PublicOrigin}/assets/v1/1/policy/label/font-1";
        }

        if (servedLogo is not null)
        {
            policy["logoUrl"] = PublicOrigin + LogoPath;
        }

        if (servedDarkLogo is not null)
        {
            policy["logoUrlDark"] = PublicOrigin + DarkLogoPath;
        }

        string preview = new JsonObject { ["policy"] = policy }.ToJsonString();

        var requests = new ConcurrentQueue<BrandingRequest>();
        await using WebApplication zitadel = await StartStubZitadelAsync(0, async context =>
        {
            string request = $"{context.Request.Method} {context.Request.Path}";
            requests.Enqueue(new BrandingRequest(request, context.Request.Headers.Authorization.Count > 0));
            if (request == "GET /admin/v1/policies/label/_preview")
            {
                await context.Response.WriteAsync(preview, context.RequestAborted);
            }
            else if (request == "GET /.well-known/openid-configuration")
            {
                await context.Response.WriteAsync("{\"issuer\":\"https://auth.public.test\"}", context.RequestAborted);
            }
            else if (request is $"GET {LogoPath}" or $"GET {DarkLogoPath}")
            {
                if (downloadAnswer == ClosesTheConnection)
                {
                    context.Abort();
                    return;
                }

                context.Response.StatusCode = downloadAnswer;
                byte[]? served = request == $"GET {LogoPath}" ? servedLogo : servedDarkLogo;
                await context.Response.Body.WriteAsync(served, context.RequestAborted);
            }
            else if (request == "GET /auth/v1/users/me")
            {
                context.Response.StatusCode = StatusCodes.Status418ImATeapot;
            }
            else
            {
                await context.Response.WriteAsync("{}", context.RequestAborted); // the label policy's PUT, the uploads and the activation
            }
        });

        using var stubs = new PathStubs();
        Dictionary<string, string> environment = ProvisionEnvironment(stubs);
        environment["PROVISION_CONNECT_TO"] = $"auth.localhost:8080:127.0.0.1:{new Uri(zitadel.Urls.First()).Port}";
        WriteZitadelReadyStub(stubs, failures: 0, writesPat: environment["PROVISION_PAT_FILE"]);
        string script = Path.Combine(stubs.Directory, "provision.sh");
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(script, ProvisionScript
            .Replace("/brand/", $"{stubs.Directory}/", StringComparison.Ordinal)
            .Replace("/provision/label-policy.sh", Path.Combine(SourceRoots.Repo, "infra/images/zitadel/label-policy.sh"), StringComparison.Ordinal), cancellation);
        File.Copy(Path.Combine(SourceRoots.Repo, "frontend/src/theme/tokens.css"), Path.Combine(stubs.Directory, "tokens.css"));
        await File.WriteAllTextAsync(Path.Combine(stubs.Directory, "nunito-sans-latin-400-normal.woff2"), "font", cancellation);
        await File.WriteAllBytesAsync(Path.Combine(stubs.Directory, "wordmark.png"), MountedLogo, cancellation);
        await File.WriteAllBytesAsync(Path.Combine(stubs.Directory, "wordmark-dark.png"), MountedDarkLogo, cancellation);

        ProcessResult run = await Processes.RunAsync("bash", [script], environment, TimeSpan.FromSeconds(30));

        return new BrandingRun(run, [.. requests]);
    }

    /// <summary>One request the stand-in received, and whether it carried a bearer token.</summary>
    private sealed record BrandingRequest(string Line, bool HasToken);

    /// <summary>How a branding run ended and what it asked of the stand-in, in order.</summary>
    private sealed record BrandingRun(ProcessResult Run, BrandingRequest[] Requests)
    {
        public BrandingRequest[] Downloads => [.. Requests.Where(request => request.Line.Contains("/logo-", StringComparison.Ordinal))];

        public string[] Uploads => [.. Requests.Select(request => request.Line).Where(line => line.StartsWith("POST /assets/", StringComparison.Ordinal))];
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

    [GeneratedRegex(@"--(?:max-time|retry-delay|retry-max-time) (?<seconds>\d+)")]
    private static partial Regex CurlTimes();

    [GeneratedRegex(@"^  floor-guard:\n(?<job>.*?)(?=^  [a-z-]+:\n)", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex FloorGuardJob();

    [GeneratedRegex(@"timeout-minutes: (?<minutes>\d+)")]
    private static partial Regex JobTimeout();

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
