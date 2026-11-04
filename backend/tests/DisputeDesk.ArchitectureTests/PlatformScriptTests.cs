using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// AC12: the headless sign-in script, read from the file. 5.1 AC7: the operations token script. Also the Compose file's
/// settings for the Api, the Worker and the gateway. The provisioner and gateway template cases test infra/'s own files,
/// so they are in infra/tests/DisputeDesk.PlatformTests (ad-011).
/// </summary>
public partial class PlatformScriptTests
{
    private static readonly string Compose = File.ReadAllText(Path.Combine(SourceRoots.Repo, "docker-compose.yml"));
    private static readonly string DevLoginPath = Path.Combine(SourceRoots.Repo, "scripts/dev-login.sh");

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
    public void Compose_GatewayHealth_WaitsUntilItReachesTheApi()
    {
        // nginx retries a failed lookup of the api only after a delay (30 s for a resolver error), and `up --wait` trusts
        // health alone, so the gateway is healthy only once /api/ answers through it (CI e2e, UJ-1 on a cold stack).
        Assert.Matches(new Regex(@"^      test: \[.*&& wget -qO- http://127\.0\.0\.1:8080/api/health/live >/dev/null""\]$", RegexOptions.Multiline, TimeSpan.FromSeconds(1)), ComposeService("gateway"));
    }

    [Fact]
    public void ApiProgram_ForwardedHeaders_RunFirstInThePipeline()
    {
        // 6.6 AC1: before anything reads the client address, the scheme or the host.
        List<InvocationExpressionSyntax> calls =
            [.. SourceTrees.Root(Path.Combine(SourceRoots.Src, "DisputeDesk.Api", "Program.cs")).DescendantNodes().OfType<InvocationExpressionSyntax>()];
        InvocationExpressionSyntax build = calls.Single(call => IsCallOn(call, "builder") && SourceTrees.LastName(call.Expression) == "Build");

        string? firstMiddleware = calls
            .Where(call => call.SpanStart > build.SpanStart && IsCallOn(call, "app"))
            .Select(call => SourceTrees.LastName(call.Expression))
            .FirstOrDefault(name => name is not null && name.StartsWith("Use", StringComparison.Ordinal));

        Assert.Equal("UseForwardedHeaders", firstMiddleware);
    }

    /// <summary>Whether <paramref name="call"/> calls a method on the variable <paramref name="variable"/> (<c>app.UseX()</c>).</summary>
    private static bool IsCallOn(InvocationExpressionSyntax call, string variable) =>
        call.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } && receiver.Identifier.ValueText == variable;

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

        // Passwords come from backend/fixtures/local.json; the login client's PAT is read inside its container at run time.
        Assert.DoesNotContain("Dev-Only-", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(LiteralBearerToken(), script);
        Assert.DoesNotContain("playwright", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"$ROOT/backend/fixtures/local.json\"", script, StringComparison.Ordinal);
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
}
