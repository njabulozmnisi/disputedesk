using System.Runtime.Versioning;
using System.Text.Json.Nodes;

namespace DisputeDesk.PlatformTests;

/// <summary>
/// Both gateway images on a throwaway Docker network with no host port (stories 7.4 and 7.12): the AWS gateway with the
/// environment infra/deploy/aws/compose.yml gives it, the local gateway with docker-compose.yml's, a real Mailpit, and nginx
/// echo servers standing in for the api, Zitadel, the login and the OTLP receiver, which answer with every header they
/// received. Clients run on the network at fixed addresses, one inside test-realip.conf's range (as CloudFront would be)
/// and one outside it. A second network holds a second AWS gateway whose Mailpit is the echo, so the Mailpit location's
/// upstream request can be seen too. Everything it starts is removed when the class ends, also when a start fails.
/// </summary>
[UnsupportedOSPlatform("windows")] // the echo server's files need their Unix modes
public sealed class GatewayFixture : IAsyncLifetime
{
    /// <summary>A documentation range (RFC 5737), so it clashes with no real network; test-realip.conf trusts part of it.</summary>
    public const string Subnet = "192.0.2.0/24";

    /// <summary>Inside test-realip.conf's 192.0.2.16/28.</summary>
    public const string TrustedIp = "192.0.2.20";

    public const string UntrustedIp = "192.0.2.100";

    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);
    private static readonly string[] ContainerNames = ["gateway", "gateway-mail", "local", "echo", "otlp", "mailpit", "trusted", "untrusted"];
    private static readonly string[] AwsTestValueNames = ["APP_HOST", "AUTH_HOST", "MAILPIT_HOST", "GATEWAY_ORIGIN_VERIFY", "MAILPIT_UI_AUTH"];

    private readonly string _work = Directory.CreateTempSubdirectory("disputedesk-gateway-test-").FullName;

    /// <summary>The prefix of every container and network this fixture starts.</summary>
    public string Run { get; } = $"disputedesk-gateway-test-{Environment.ProcessId}";

    private string MailNetwork => $"{Run}-mail";

    /// <summary>The AWS gateway, as its clients reach it.</summary>
    public string Aws => $"http://{Run}-gateway:8080";

    /// <summary>The local gateway, as its clients reach it.</summary>
    public string Local => $"http://{Run}-local:8080";

    /// <summary>The values of infra/tests/fixtures/compose.test.env.</summary>
    private Dictionary<string, string> AwsTestValues { get; set; } = [];

    public string AppHost => AwsTestValues["APP_HOST"];

    public string AuthHost => AwsTestValues["AUTH_HOST"];

    public string MailHost => AwsTestValues["MAILPIT_HOST"];

    public string OriginVerify => AwsTestValues["GATEWAY_ORIGIN_VERIFY"];

    public string MailpitCredentials => AwsTestValues["MAILPIT_UI_AUTH"];

    /// <summary>The AWS gateway's environment, as Compose renders it with the test inputs.</summary>
    public IReadOnlyDictionary<string, string> AwsGatewayEnvironment { get; private set; } = new Dictionary<string, string>();

    /// <summary>The local gateway's environment, as Compose renders docker-compose.yml.</summary>
    public IReadOnlyDictionary<string, string> LocalGatewayEnvironment { get; private set; } = new Dictionary<string, string>();

    /// <summary>The AWS gateway's health check command, as the container's shell runs it.</summary>
    public string AwsHealthCheck { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        try
        {
            await StartAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        string[] containers = [.. ContainerNames.Select(name => $"{Run}-{name}")];
        await Processes.DockerAsync(CommandTimeout, ["rm", "-f", .. containers]);
        await Processes.DockerAsync(CommandTimeout, "network", "rm", Run, MailNetwork);
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    private async Task StartAsync()
    {
        // Each gateway takes its SPA files from an SPA image through the named context spa, as CI and Compose pass it:
        // the AWS one built with BROWSER_TELEMETRY=off, as the publish job builds it, the local one with the default.
        await MustAsync(BuildTimeout, "build", "-q", "--build-arg", "BROWSER_TELEMETRY=off", "-t", "disputedesk-spa:aws-test", "frontend");
        await MustAsync(BuildTimeout, "build", "-q", "-t", "disputedesk-spa:local-test", "frontend");
        await MustAsync(BuildTimeout, "build", "-q", "--build-context", "spa=docker-image://disputedesk-spa:aws-test", "-t", "disputedesk-gateway:aws-test", "infra/images/gateway");
        await MustAsync(BuildTimeout, "build", "-q", "--build-context", "spa=docker-image://disputedesk-spa:local-test", "-t", "disputedesk-gateway:local-test", "infra/images/gateway");

        string testEnvFile = Path.Combine(SourceRoots.Repo, "infra/tests/fixtures/compose.test.env");
        AwsTestValues = (await File.ReadAllLinesAsync(testEnvFile, TestContext.Current.CancellationToken))
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2 && AwsTestValueNames.Contains(pair[0]))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
        JsonNode aws = await ComposeModel.RenderAsync(["docker-compose.yml", "infra/deploy/aws/compose.yml"], testEnvFile);
        JsonNode local = await ComposeModel.RenderAsync(["docker-compose.yml"]);
        AwsGatewayEnvironment = EnvironmentOf(aws, "gateway");
        LocalGatewayEnvironment = EnvironmentOf(local, "gateway");
        // Compose turns $$ into $ when it runs the health check.
        AwsHealthCheck = aws["services"]!["gateway"]!["healthcheck"]!["test"]![1]!.GetValue<string>().Replace("$$", "$", StringComparison.Ordinal);

        WriteEchoServer();
        ProcessResult network = await Processes.DockerAsync(CommandTimeout, "network", "create", "--subnet", Subnet, Run);
        Assert.True(network.ExitCode == 0, $"Could not create the test network on {Subnet} (is another network using it?): {network.Errors}");
        await MustAsync(CommandTimeout, "network", "create", MailNetwork);

        string echo = $"{_work}:/echo:ro";
        string[] echoCommand = ["--entrypoint", "nginx", "disputedesk-gateway:aws-test", "-c", "/echo/echo.conf", "-g", "daemon off;"];
        await MustAsync(CommandTimeout, ["run", "-d", "--name", $"{Run}-echo", "--network", Run, "--network-alias", "api",
            "--network-alias", "zitadel", "--network-alias", "-v", echo, .. echoCommand]);
        await MustAsync(CommandTimeout, "network", "connect", "--alias", "mailpit", MailNetwork, $"{Run}-echo");
        // The OTLP receiver's stand-in, under the name the local gateway sends browser telemetry to.
        await MustAsync(CommandTimeout, ["run", "-d", "--name", $"{Run}-otlp", "--network", Run, "--network-alias", "aspire-dashboard", "-v", echo, .. echoCommand]);
        await MustAsync(CommandTimeout, ["run", "-d", "--name", $"{Run}-mailpit", "--network", Run, "--network-alias", "mailpit",
            .. EnvironmentArguments(EnvironmentOf(aws, "mailpit")), aws["services"]!["mailpit"]!["image"]!.GetValue<string>()]);
        string realIp = $"{Path.Combine(SourceRoots.Repo, "infra/tests/fixtures/test-realip.conf")}:/etc/nginx/cloudfront-realip.conf:ro";
        await MustAsync(CommandTimeout, ["run", "-d", "--name", $"{Run}-gateway", "--network", Run, .. EnvironmentArguments(AwsGatewayEnvironment),
            "-v", realIp, "disputedesk-gateway:aws-test"]);
        await MustAsync(CommandTimeout, ["run", "-d", "--name", $"{Run}-gateway-mail", "--network", MailNetwork, .. EnvironmentArguments(AwsGatewayEnvironment),
            "-v", realIp, "disputedesk-gateway:aws-test"]);
        await MustAsync(CommandTimeout, ["run", "-d", "--name", $"{Run}-local", "--network", Run, .. EnvironmentArguments(LocalGatewayEnvironment),
            "disputedesk-gateway:local-test"]);
        foreach ((string client, string ip) in new[] { ("trusted", TrustedIp), ("untrusted", UntrustedIp) })
        {
            await MustAsync(CommandTimeout, "run", "-d", "--name", $"{Run}-{client}", "--network", Run, "--ip", ip, "--entrypoint", "sleep", "disputedesk-gateway:aws-test", "1800");
        }

        await WaitUntilUpAsync();
    }

    /// <summary>Waits up to a minute for both gateways' SPA, the Mailpit-echo gateway and Mailpit.</summary>
    private async Task WaitUntilUpAsync()
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            if (await IsUpAsync())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }

        ProcessResult logs = await Processes.DockerAsync(CommandTimeout, "logs", $"{Run}-gateway");
        Assert.Fail($"The gateways or Mailpit did not start within a minute. AWS gateway log: {logs.Output}{logs.Errors}");
    }

    private async Task<bool> IsUpAsync()
    {
        ProcessResult aws = await Processes.DockerAsync(CommandTimeout, "exec", $"{Run}-trusted", "curl", "-s", "-o", "/dev/null", "-w", "%{http_code}",
            "-H", $"Host: {AppHost}", "-H", $"X-Origin-Verify: {OriginVerify}", $"{Aws}/");
        ProcessResult local = await Processes.DockerAsync(CommandTimeout, "exec", $"{Run}-trusted", "curl", "-s", "-o", "/dev/null", "-w", "%{http_code}", $"{Local}/");
        ProcessResult mailEcho = await Processes.DockerAsync(CommandTimeout, "exec", $"{Run}-gateway-mail", "curl", "-s", "-o", "/dev/null", "-w", "%{http_code}",
            "-H", $"Host: {AppHost}", "-H", $"X-Origin-Verify: {OriginVerify}", "http://127.0.0.1:8080/");
        ProcessResult mailpit = await Processes.DockerAsync(CommandTimeout, "exec", $"{Run}-mailpit", "/mailpit", "readyz");
        return aws.Output == "200" && local.Output == "200" && mailEcho.Output == "200" && mailpit.ExitCode == 0;
    }

    /// <summary>
    /// The echo server: nginx with its JavaScript module (njs, in the gateway image), so each answer is the request's
    /// port, path, HTTP version (njs r.httpVersion) and every header exactly as received (njs r.rawHeadersIn) as JSON,
    /// with Cache-Control: no-store as the Api sends on every response.
    /// </summary>
    private void WriteEchoServer()
    {
        File.WriteAllText(Path.Combine(_work, "echo.js"), """
            function dump(r) {
              r.headersOut['Content-Type'] = 'application/json';
              r.return(200, JSON.stringify({ port: r.variables.server_port, uri: r.uri, version: r.httpVersion, headers: r.rawHeadersIn }));
            }
            export default { dump };
            """);
        File.WriteAllText(Path.Combine(_work, "echo.conf"), """
            load_module /usr/lib/nginx/modules/ngx_http_js_module.so;
            pid /tmp/echo.pid;
            events {}
            http {
              client_body_temp_path /tmp/echo_client;
              proxy_temp_path /tmp/echo_proxy;
              fastcgi_temp_path /tmp/echo_fastcgi;
              uwsgi_temp_path /tmp/echo_uwsgi;
              scgi_temp_path /tmp/echo_scgi;
              access_log /dev/stdout;
              js_path /echo;
              js_import echo from echo.js;
              # The api and Zitadel (8080), Mailpit on the second network (8025) and the OTLP receiver (18890).
              server {
                listen 8080;
                listen 8025;
                listen 18890;
                add_header Cache-Control "no-store" always;
                location / { js_content echo.dump; }
                # A failing api for the gateway's own 502 (7.12 criterion 7): the connection is closed without a response
                # (nginx's non-standard code 444), which the gateway reports at once as 502. /api/health/* arrives here
                # as /health/*.
                location = /api/upstream-closes    { return 444; }
                location = /health/upstream-closes { return 444; }
                # The api's own error, which the gateway passes through untouched.
                location = /api/upstream-fails {
                  default_type text/plain;
                  return 500 "the api's own error\n";
                }
                location = /health/upstream-fails {
                  default_type text/plain;
                  return 500 "the api's own error\n";
                }
                # Zitadel's v1 login sends its own Referrer-Policy, which the identity origin passes through.
                location /ui/login/ {
                  add_header Referrer-Policy "same-origin";
                  js_content echo.dump;
                }
              }
              # The login (3000): the headers zitadel:v4.20.0 sends on /ui/login/* (its routes-manifest.json).
              server {
                listen 3000;
                location / {
                  add_header Strict-Transport-Security "max-age=63072000; includeSubDomains; preload";
                  add_header Referrer-Policy "origin-when-cross-origin";
                  add_header X-Content-Type-Options "nosniff";
                  add_header X-Frame-Options "deny";
                  add_header X-XSS-Protection "1; mode=block";
                  js_content echo.dump;
                }
              }
            }
            """);
        // The echo server runs as the image's non-root user, which must be able to read its files through the mount.
        const UnixFileMode Readable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        File.SetUnixFileMode(_work, Readable | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        foreach (string file in Directory.GetFiles(_work))
        {
            File.SetUnixFileMode(file, Readable);
        }
    }

    private static async Task MustAsync(TimeSpan timeout, params string[] arguments)
    {
        ProcessResult result = await Processes.DockerAsync(timeout, arguments);
        Assert.True(result.ExitCode == 0, $"docker {string.Join(' ', arguments)} failed: {result.Errors}");
    }

    private static Dictionary<string, string> EnvironmentOf(JsonNode model, string service) =>
        model["services"]![service]!["environment"]!.AsObject()
            .ToDictionary(variable => variable.Key, variable => variable.Value!.GetValue<string>(), StringComparer.Ordinal);

    private static IEnumerable<string> EnvironmentArguments(IReadOnlyDictionary<string, string> environment) =>
        environment.SelectMany(variable => new[] { "-e", $"{variable.Key}={variable.Value}" });
}
