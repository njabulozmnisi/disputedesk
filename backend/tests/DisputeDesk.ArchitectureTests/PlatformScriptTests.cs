using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DisputeDesk.ArchitectureTests;

/// <summary>
/// AC12: the headless sign-in script. 5.1 AC7: the operations token script. Also the Compose model's settings for the
/// Api, the Worker and the gateway, as `docker compose config --format json` renders docker-compose.yml. The scripts run
/// with stand-ins for curl and docker first on PATH, and stay read as text only for what no run can show. The provisioner
/// and gateway cases test infra/'s own files, so they are in infra/tests/DisputeDesk.PlatformTests (ad-011).
/// </summary>
[UnsupportedOSPlatform("windows")] // the stub commands need their execute bit
public partial class PlatformScriptTests
{
    private static readonly string DevLoginPath = Path.Combine(SourceRoots.Repo, "scripts/dev-login.sh");
    private static readonly string OperationsTokenPath = Path.Combine(SourceRoots.Repo, "scripts/operations-token.sh");
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(null, "http://auth.localhost:8080/oauth/v2/token")]
    [InlineData("https://auth.example.test", "https://auth.example.test/oauth/v2/token")]
    public async Task OperationsTokenScript_Run_SendsTheClientCredentialsGrantWithTheSecretOnStdinOnly(string? authOrigin, string tokenUrl)
    {
        // RFC 6749 §4.4 with the AD-9 scope, against the local auth origin unless AUTH_ORIGIN names another. Expected: one
        // docker call that reads the clients file through the provisioner image, one curl call with the credentials as a
        // config on stdin (-K -), never an argument, which any local user could read from the process list; exit 0, and
        // only the token on stdout.
        string bin = Directory.CreateTempSubdirectory("operations-token-").FullName;
        await WriteStubAsync(bin, "docker", ("""{"Zitadel":{"ProjectId":"p1","OperationsTools":{"ClientId":"operations-tools","ClientSecret":"stub-secret"}}}""", "", 0));
        await WriteStubAsync(bin, "curl", ("""{"access_token":"stub-access-token"}""", "", 0));
        Dictionary<string, string> environment = StubEnvironment(bin);
        if (authOrigin is not null)
        {
            environment["AUTH_ORIGIN"] = authOrigin;
        }

        (int exitCode, string output, string errors) = await RunAsync("bash", [OperationsTokenPath], environment);
        string[][] docker = StubCalls(bin, "docker");
        string[][] curl = StubCalls(bin, "curl");
        string curlInput = await File.ReadAllTextAsync(Path.Combine(bin, "curl.1.stdin"), TestContext.Current.CancellationToken);
        Directory.Delete(bin, recursive: true);

        Assert.Equal(0, exitCode);
        Assert.Equal("stub-access-token\n", output);
        Assert.Empty(errors);
        Assert.Equal(["compose", "-f", Path.Combine(SourceRoots.Repo, "docker-compose.yml"), "run", "--rm", "--no-deps", "-T", "--entrypoint", "cat", "provision", "/out/zitadel-clients.json"], Assert.Single(docker));
        Assert.Equal(["-fsS", "--max-time", "30", "-K", "-", tokenUrl, "--data-urlencode", "grant_type=client_credentials",
            "--data-urlencode", "scope=openid urn:zitadel:iam:org:project:id:p1:aud urn:zitadel:iam:org:projects:roles"], Assert.Single(curl));
        Assert.Equal("user = \"operations-tools:stub-secret\"\n", curlInput);
    }

    [Fact]
    public void OperationsTokenScript_Source_HoldsNoLiteralToken()
    {
        // Text: no run can show that the file holds no token.
        string script = File.ReadAllText(OperationsTokenPath);

        Assert.DoesNotMatch(LiteralBearerToken(), script);
    }

    [Fact]
    public async Task OperationsTokenScript_ClientsFileUnreadable_PrintsOneShortMessageAndFails()
    {
        // A stub docker stands in for a stack that is down: it writes its own error (with a would-be secret) and fails.
        // Expected: no curl call, exit 1, only the script's own message, nothing from docker, and nothing on stdout.
        string bin = Directory.CreateTempSubdirectory("operations-token-").FullName;
        await WriteStubAsync(bin, "docker", ("", "stub-secret-from-docker\n", 1));
        await WriteStubAsync(bin, "curl");

        (int exitCode, string output, string errors) = await RunAsync("bash", [OperationsTokenPath], StubEnvironment(bin));
        string[][] curl = StubCalls(bin, "curl");
        Directory.Delete(bin, recursive: true);

        Assert.Equal(1, exitCode);
        Assert.Equal("could not read the clients file from the provision volume; is the stack up?\n", errors);
        Assert.Empty(output);
        Assert.Empty(curl);
    }

    [Theory]
    [InlineData(null, "false")]
    [InlineData("true", "true")]
    public async Task Compose_ZitadelExternalSecure_ComesFromConfiguration(string? set, string expected)
    {
        Dictionary<string, string> variables = set is null ? [] : new() { ["ZITADEL_EXTERNALSECURE"] = set };

        JsonNode services = (await RenderComposeAsync(variables))["services"]!;

        Assert.Equal(expected, services["zitadel"]!["environment"]!["ZITADEL_EXTERNALSECURE"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null, "10.201.89.0/24")]
    [InlineData("10.201.90.0/24", "10.201.90.0/24")]
    public async Task Compose_PinnedDefaultNetworkSubnet_IsTheApisTrustedNetwork(string? set, string expected)
    {
        // 6.6 AC1 (AD-23): a mismatch fails silently, every client becoming the gateway's address, so the two must agree.
        // Both read DISPUTEDESK_SUBNET with the same default, so a second stack that moves its subnet moves both.
        Dictionary<string, string> variables = set is null ? [] : new() { ["DISPUTEDESK_SUBNET"] = set };

        JsonNode model = await RenderComposeAsync(variables);
        JsonNode api = model["services"]!["api"]!["environment"]!;

        Assert.Equal(expected, model["networks"]!["default"]!["ipam"]!["config"]![0]!["subnet"]!.GetValue<string>());
        Assert.Equal(expected, api["ForwardedHeaders__TrustedNetwork"]!.GetValue<string>());
        Assert.Equal("localhost", api["ForwardedHeaders__AllowedHosts"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("core-banking")]
    [InlineData("api")]
    [InlineData("worker")]
    public async Task Compose_DotNetService_RunsUnderAnInitProcess(string service)
    {
        // A .NET crash (a failed start included) ends in abort(). As a container's PID 1, dotnet never receives that
        // SIGABRT (pid_namespaces(7)) and spins instead of exiting, so a restart policy never sees the crash.
        JsonNode services = (await RenderComposeAsync())["services"]!;

        Assert.True(services[service]!["init"]?.GetValue<bool>(), $"{service} does not run under an init process.");
    }

    [Fact]
    public async Task Compose_GatewayHealth_WaitsUntilItReachesTheApi()
    {
        // nginx retries a failed lookup of the api only after a delay (30 s for a resolver error), and `up --wait` trusts
        // health alone, so the gateway is healthy only once /api/ answers through it (CI e2e, UJ-1 on a cold stack).
        JsonNode test = (await RenderComposeAsync())["services"]!["gateway"]!["healthcheck"]!["test"]!;
        string[] steps = [.. test[1]!.GetValue<string>().Split("&&").Select(step => step.Trim())];

        Assert.Equal("CMD-SHELL", test[0]!.GetValue<string>());
        Assert.Equal("wget -qO- http://127.0.0.1:8080/api/health/live >/dev/null", steps[^1]);
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
        (int exitCode, string help, _) = await RunAsync("bash", [DevLoginPath, "--help"], BaseEnvironment());

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: scripts/dev-login.sh <login> [cookie-jar]", help, StringComparison.Ordinal);
        Assert.Contains("no browser", help, StringComparison.Ordinal);
        Assert.Contains("cookie jar", help, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevLoginScript_Run_FindsTheLoginInTheFixturesFileThenStartsAtTheApi()
    {
        // Passwords come from backend/fixtures/local.json: the login is found there, so the first call is the Api's
        // sign-in. Expected: one curl call, which the stub answers with no redirect, exit 1 and the script's message.
        string bin = Directory.CreateTempSubdirectory("dev-login-").FullName;
        await WriteStubAsync(bin, "curl", ("", "", 0));
        Dictionary<string, string> environment = StubEnvironment(bin);
        environment["TMPDIR"] = bin;
        string jar = Path.Combine(bin, "disputedesk-customer-a.cookies");

        (int exitCode, string output, string errors) = await RunAsync("bash", [DevLoginPath, "customer-a"], environment);
        string[][] curl = StubCalls(bin, "curl");
        Directory.Delete(bin, recursive: true);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Equal("dev-login: the Api did not redirect to Zitadel (got '')\n", errors);
        Assert.Equal(["-sS", "-c", jar, "-b", jar, "-o", "/dev/null", "-w", "%{redirect_url}", "http://localhost:8080/api/v1/authentication/login"], Assert.Single(curl));
    }

    [Fact]
    public void DevLoginScript_Source_HoldsNoSecretAndNoBrowser()
    {
        // Text: no run can show that the file holds no password, no token and no browser call. The login client's PAT
        // is read inside its container at run time.
        string script = File.ReadAllText(DevLoginPath);

        Assert.DoesNotContain("Dev-Only-", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(LiteralBearerToken(), script);
        Assert.DoesNotContain("playwright", script, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("heartbeat-relay", 30)] // AD-25: the relay's loop is at most 10 seconds
    [InlineData("heartbeat-sweep", 90)] // story 5.3 AC7: three missed 30-second sweep loops (decided 2026-10-27)
    public async Task Compose_WorkerHealthCheck_FailsWhenTheHeartbeatIsOlderThanItsThreshold(string file, int seconds)
    {
        // The worker's health check compares each file's modification time with now, in seconds, as one shell test.
        // Compose keeps $$ in the model and runs it as $.
        JsonArray test = (await RenderComposeAsync())["services"]!["worker"]!["healthcheck"]!["test"]!.AsArray();
        string command = test[test.Count - 1]!.GetValue<string>();
        Match check = new Regex(
            @"\$\$\(\(\s*now\s*-\s*\$\$\(/usr/local/bin/busybox\s+stat\s+-c\s+%Y\s+/tmp/" + Regex.Escape(file) + @"\)\s*\)\)\s+-le\s+(?<seconds>\d+)",
            RegexOptions.None,
            TimeSpan.FromSeconds(1)).Match(command);

        Assert.True(check.Success, $"The worker's health check does not test /tmp/{file}: {command}");
        Assert.Equal(seconds, int.Parse(check.Groups["seconds"].Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Matches(WorkerNow(), command);
    }

    [GeneratedRegex(@"Bearer [A-Za-z0-9_\-\.]{20,}")]
    private static partial Regex LiteralBearerToken();

    /// <summary>The worker's health check takes now once, in seconds, and then tests each file.</summary>
    [GeneratedRegex(@"^now=\$\$\(/usr/local/bin/busybox\s+date\s+\+%s\)\s*&&\s*test\b")]
    private static partial Regex WorkerNow();

    /// <summary>
    /// docker-compose.yml's model (Docker, "docker compose config"), rendered with an empty env file so no local .env is
    /// read, and with only the base environment plus these variables.
    /// </summary>
    private static async Task<JsonNode> RenderComposeAsync(Dictionary<string, string>? variables = null)
    {
        string emptyEnvFile = Path.GetTempFileName();
        Dictionary<string, string> environment = BaseEnvironment();
        foreach ((string name, string value) in variables ?? [])
        {
            environment[name] = value;
        }

        try
        {
            (int exitCode, string output, string errors) = await RunAsync(
                "docker", ["compose", "-f", Path.Combine(SourceRoots.Repo, "docker-compose.yml"), "--env-file", emptyEnvFile, "config", "--format", "json"], environment);
            Assert.True(exitCode == 0, $"docker compose config failed: {errors}");
            return JsonNode.Parse(output) ?? throw new InvalidOperationException("docker compose config printed no model.");
        }
        finally
        {
            File.Delete(emptyEnvFile);
        }
    }

    /// <summary>
    /// Runs a program with no shell, the arguments as a list, only this environment, standard input closed and both
    /// outputs read while it runs; the process tree is killed when the timeout passes.
    /// </summary>
    private static async Task<(int ExitCode, string Output, string Errors)> RunAsync(string program, string[] arguments, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(program, arguments)
        {
            WorkingDirectory = SourceRoots.Repo,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment.Clear();
        foreach ((string name, string value) in environment)
        {
            start.Environment[name] = value;
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(ScriptTimeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            return (process.ExitCode, await output.WaitAsync(deadline.Token), await errors.WaitAsync(deadline.Token));
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{program} {string.Join(' ', arguments)} did not finish within {ScriptTimeout}.");
        }
    }

    /// <summary>What docker, bash, curl and jq need (PATH, HOME, TMPDIR and Docker's own settings); nothing else is inherited.</summary>
    private static Dictionary<string, string> BaseEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in new[] { "PATH", "HOME", "TMPDIR", "DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_CONFIG" })
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                environment[name] = value;
            }
        }

        return environment;
    }

    /// <summary>The base environment with the stubs in this folder first on PATH.</summary>
    private static Dictionary<string, string> StubEnvironment(string bin)
    {
        Dictionary<string, string> environment = BaseEnvironment();
        environment["PATH"] = $"{bin}{Path.PathSeparator}{environment["PATH"]}";
        return environment;
    }

    /// <summary>
    /// A stand-in for a command, first on PATH: each call writes its arguments (one per line) to NAME.N.args and its
    /// standard input to NAME.N.stdin, then prints the N-th answer's output and errors and exits with its code. A call
    /// with no answer fails, naming itself.
    /// </summary>
    private static async Task WriteStubAsync(string bin, string name, params (string Output, string Errors, int Exit)[] answers)
    {
        string stub = Path.Combine(bin, name);
        await File.WriteAllTextAsync(stub, """
            #!/usr/bin/env bash
            dir=$(dirname "$0"); name=$(basename "$0")
            n=1; while [ -e "$dir/$name.$n.args" ]; do n=$((n + 1)); done
            printf '%s\n' "$@" > "$dir/$name.$n.args"
            cat > "$dir/$name.$n.stdin"
            if [ ! -e "$dir/$name.$n.exit" ]; then echo "unexpected $name call $n: $*" >&2; exit 99; fi
            cat "$dir/$name.$n.out"; cat "$dir/$name.$n.err" >&2
            exit "$(cat "$dir/$name.$n.exit")"
            """, TestContext.Current.CancellationToken);
        File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        for (int call = 1; call <= answers.Length; call++)
        {
            (string output, string errors, int exit) = answers[call - 1];
            await File.WriteAllTextAsync(Path.Combine(bin, $"{name}.{call}.out"), output, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(bin, $"{name}.{call}.err"), errors, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(bin, $"{name}.{call}.exit"), $"{exit}", TestContext.Current.CancellationToken);
        }
    }

    /// <summary>The arguments of each call the stub received, in order.</summary>
    private static string[][] StubCalls(string bin, string name)
    {
        List<string[]> calls = [];
        for (int call = 1; File.Exists(Path.Combine(bin, $"{name}.{call}.args")); call++)
        {
            calls.Add(File.ReadAllLines(Path.Combine(bin, $"{name}.{call}.args")));
        }

        return [.. calls];
    }
}
