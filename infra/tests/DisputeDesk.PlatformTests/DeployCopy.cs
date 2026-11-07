using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DisputeDesk.PlatformTests;

/// <summary>
/// The unchanged infra/deploy/aws/deploy.sh in a fresh throwaway copy (with input-bounds.sh and the database init script it
/// runs), beside a stale .env (mode 0644), with aws, curl, docker, iptables, iptables-save and systemctl stubbed. The
/// stubs answer exactly the calls deploy.sh makes, as the properties say; any other call exits 97. Dispose removes the
/// copy and the stubs.
/// </summary>
[UnsupportedOSPlatform("windows")] // the stubs need their execute bit
internal sealed partial class DeployCopy : IDisposable
{
    public const string SsmPath = "/disputedesk/dev";
    public const string QaPassword = "TestOnlyQaDbPasswordNotASecret00";

    /// <summary>64 letters and digits, the database module's random_password shape.</summary>
    public const string MasterPassword = "TestOnlyRdsMasterPasswordNotASecret00000000000000000000000000000";

    public const string Stale = "STALE=1\n";

    /// <summary>deploy.sh's "&lt;roles&gt; &lt;databases&gt;" count of the init's three roles and two databases.</summary>
    public const string CountQuery = "SELECT (SELECT count(*) FROM pg_roles WHERE rolname IN ('disputedesk', 'zitadel', 'qa_readonly')) || ' ' || (SELECT count(*) FROM pg_database WHERE datname IN ('disputedesk', 'zitadel'))";

    /// <summary>The names deploy.sh reads from SSM: story 7.4's inputs that SSM holds, and the two the init alone uses.</summary>
    public static readonly string[] SsmNames =
    [
        "APP_DB_PASSWORD", "ZITADEL_DB_PASSWORD", "ZITADEL_MASTERKEY", "ZITADEL_ADMIN_PASSWORD",
        "LINKING_OTP_HMAC_KEY", "MAILPIT_UI_AUTH", "APP_HOST", "AUTH_HOST", "MAILPIT_HOST", "DB_HOST", "OTEL_LOG_GROUP",
        "DATAPROTECTION_SSM_PATH", "DATAPROTECTION_KMS_KEY_ID", "GATEWAY_ORIGIN_VERIFY", "QA_DB_PASSWORD", "RDS_MASTER_PASSWORD",
    ];

    /// <summary>deploy.sh's five arguments, in order.</summary>
    public static readonly string[] ImageNames = ["API_IMAGE", "WORKER_IMAGE", "GATEWAY_IMAGE", "CORE_BANKING_IMAGE", "ZITADEL_IMAGE"];

    public static readonly string[] ComposeUpCall = ["compose", "-f", "docker-compose.yml", "-f", "infra/deploy/aws/compose.yml", "up", "-d", "--wait"];

    public static readonly string[] RuleCheckCall = ["-C", "DOCKER-USER", "-s", "10.201.89.0/24", "-d", "169.254.169.254/32", "-j", "DROP"];

    public static readonly string[][] RuleInsertCalls =
    [
        ["-I", "DOCKER-USER", "1", "-s", "10.201.89.10/32", "-d", "169.254.169.254/32", "-j", "RETURN"],
        ["-I", "DOCKER-USER", "2", "-s", "10.201.89.11/32", "-d", "169.254.169.254/32", "-j", "RETURN"],
        ["-I", "DOCKER-USER", "3", "-s", "10.201.89.0/24", "-d", "169.254.169.254/32", "-j", "DROP"],
    ];

    public static readonly string[] RangesCall = ["-fsS", "--max-time", "60", "--retry", "3", "https://ip-ranges.amazonaws.com/ip-ranges.json"];

    /// <summary>Every secret value, none of which may appear in any output or in any stubbed command's arguments.</summary>
    public static readonly string[] Secrets =
    [
        .. new[]
        {
            "APP_DB_PASSWORD", "ZITADEL_DB_PASSWORD", "ZITADEL_MASTERKEY", "ZITADEL_ADMIN_PASSWORD",
            "LINKING_OTP_HMAC_KEY", "MAILPIT_UI_AUTH", "GATEWAY_ORIGIN_VERIFY",
        }.Select(name => AwsInputs.TestValues()[name]),
        QaPassword, MasterPassword,
    ];

    /// <summary>deploy.sh's psql image (the server in DatabaseInitTests runs it too).</summary>
    public static readonly string PostgresImage = PostgresImageLine().Match(File.ReadAllText(Path.Combine(SourceRoots.Repo, "infra/deploy/aws/deploy.sh"))).Groups["image"].Value;

    /// <summary>CloudFront's origin-facing IPv4 ranges among others deploy.sh must leave out (RFC 5737 documentation ranges).</summary>
    public const string DefaultRanges = """
        {
          "prefixes": [
            { "ip_prefix": "192.0.2.0/26", "service": "CLOUDFRONT_ORIGIN_FACING" },
            { "ip_prefix": "198.51.100.0/24", "service": "CLOUDFRONT_ORIGIN_FACING" },
            { "ip_prefix": "203.0.113.0/25", "service": "CLOUDFRONT" },
            { "ip_prefix": "203.0.113.128/25", "service": "EC2" }
          ],
          "ipv6_prefixes": [{ "ipv6_prefix": "2001:db8::/32", "service": "CLOUDFRONT_ORIGIN_FACING" }]
        }
        """;

    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(120);

    private readonly string _root = Directory.CreateTempSubdirectory("disputedesk-deploy-").FullName;

    public DeployCopy()
    {
        Directory.CreateDirectory(Path.Combine(Repo, "infra/deploy/aws"));
        Directory.CreateDirectory(Path.Combine(Repo, "infra/images/postgres"));
        foreach (string file in new[] { "infra/deploy/aws/deploy.sh", "infra/deploy/aws/input-bounds.sh", "infra/images/postgres/10-roles-and-databases.sh" })
        {
            File.Copy(Path.Combine(SourceRoots.Repo, file), Path.Combine(Repo, file));
        }

        File.WriteAllText(Path.Combine(Repo, ".env"), Stale);
        File.SetUnixFileMode(Path.Combine(Repo, ".env"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        Dictionary<string, string> testValues = AwsInputs.TestValues();
        foreach (string name in SsmNames)
        {
            Parameters[name] = name switch
            {
                "QA_DB_PASSWORD" => QaPassword,
                "RDS_MASTER_PASSWORD" => MasterPassword,
                _ => testValues[name],
            };
        }

        Images = [.. ImageNames.Select(name => testValues[name])];
    }

    /// <summary>The throwaway copy deploy.sh runs from.</summary>
    public string Repo => Path.Combine(_root, "repo");

    /// <summary>A second name for the stale .env: if it still reads <see cref="Stale"/>, .env was replaced, not rewritten.</summary>
    public string StaleEnv => Path.Combine(_root, "stale.env");

    public PathStubs Stubs { get; } = new("aws", "curl", "docker", "iptables", "iptables-save", "systemctl");

    /// <summary>The SSM parameters by name; a removed name is missing from SSM.</summary>
    public Dictionary<string, string> Parameters { get; } = new(StringComparer.Ordinal);

    public string[] Images { get; set; }

    /// <summary>AWS_REGION in deploy.sh's environment; null leaves it out.</summary>
    public string? Region { get; set; } = "eu-west-1";

    /// <summary>SSM_PATH in deploy.sh's environment.</summary>
    public string DeploySsmPath { get; set; } = SsmPath;

    public string CaBundle { get; set; } = "-----BEGIN CERTIFICATE-----\nTestOnlyCertificate\n-----END CERTIFICATE-----\n";

    public string Ranges { get; set; } = DefaultRanges;

    public int AwsExit { get; set; }

    public int CaExit { get; set; }

    public int RangesExit { get; set; }

    /// <summary>The count query's answer, "&lt;roles&gt; &lt;databases&gt;".</summary>
    public string RolesAndDatabases { get; set; } = "0 0";

    public int InitExit { get; set; }

    /// <summary>Whether `iptables -C` finds the instance metadata rule.</summary>
    public bool RuleExists { get; set; }

    public int ComposeExit { get; set; }

    /// <summary>Sets one input: an SSM parameter, AWS_REGION or an image argument.</summary>
    public void Set(string name, string value)
    {
        if (name == "AWS_REGION")
        {
            Region = value;
        }
        else if (ImageNames.Contains(name))
        {
            Images[Array.IndexOf(ImageNames, name)] = value;
        }
        else
        {
            Parameters[name] = value;
        }
    }

    /// <summary>The count query's docker arguments, as deploy.sh builds them.</summary>
    public string[] CountCall() =>
    [
        "run", "--rm", .. ConnectionArguments(), PostgresImage, "psql", "-v", "ON_ERROR_STOP=1", "-tA", "-c", CountQuery,
    ];

    /// <summary>The database init's docker arguments, as deploy.sh builds them.</summary>
    public string[] InitCall() =>
    [
        "run", "--rm", .. ConnectionArguments(), "-e", "POSTGRES_USER", "-e", "APP_DB_PASSWORD", "-e", "ZITADEL_DB_PASSWORD",
        "-e", "QA_DB_PASSWORD", "-v", $"{Repo}/infra/images/postgres/10-roles-and-databases.sh:/init/10-roles-and-databases.sh:ro", PostgresImage,
        "sh", "/init/10-roles-and-databases.sh",
    ];

    public string[] ParametersCall() =>
        ["ssm", "get-parameters-by-path", "--region", Region ?? "", "--path", SsmPath, "--recursive", "--with-decryption", "--output", "json"];

    public string[] CaBundleCall() =>
        ["-fsS", "--max-time", "60", "--retry", "3", $"https://truststore.pki.rds.amazonaws.com/{Region}/{Region}-bundle.pem"];

    /// <summary>Sets up the stubs' answers and runs deploy.sh by its path in the copy.</summary>
    public async Task<ProcessResult> RunAsync()
    {
        ProcessResult link = await Processes.RunAsync("ln", [Path.Combine(Repo, ".env"), StaleEnv], Processes.BaseEnvironment(), RunTimeout);
        Assert.True(link.ExitCode == 0, $"ln failed: {link.Errors}");
        Stubs.Answer("aws", ParametersCall(), ParametersJson(), exit: AwsExit);
        Stubs.Answer("curl", CaBundleCall(), CaBundle, exit: CaExit);
        Stubs.Answer("curl", RangesCall, Ranges, exit: RangesExit);
        Stubs.Answer("docker", CountCall(), RolesAndDatabases + "\n");
        Stubs.Answer("docker", InitCall(), exit: InitExit);
        Stubs.Answer("docker", ComposeUpCall, exit: ComposeExit);
        Stubs.Answer("iptables", RuleCheckCall, exit: RuleExists ? 0 : 1);
        foreach (string[] insert in RuleInsertCalls)
        {
            Stubs.Answer("iptables", insert);
        }

        Stubs.Answer("iptables-save", ["-f", "/etc/sysconfig/iptables"]);
        Stubs.Answer("systemctl", ["enable", "iptables"]);

        Dictionary<string, string> environment = Stubs.Environment();
        environment["SSM_PATH"] = DeploySsmPath;
        if (Region is not null)
        {
            environment["AWS_REGION"] = Region;
        }

        return await Processes.RunAsync(Path.Combine(Repo, "infra/deploy/aws/deploy.sh"), Images, environment, RunTimeout);
    }

    public void Dispose()
    {
        Stubs.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private string[] ConnectionArguments() =>
    [
        "-e", "PGHOST", "-e", "PGSSLMODE", "-e", "PGSSLROOTCERT", "-e", "PGUSER", "-e", "PGPASSWORD", "-e", "PGDATABASE",
        "-v", $"{Repo}/rds-ca-bundle.pem:/run/rds/ca-bundle.pem:ro",
    ];

    /// <summary>get-parameters-by-path's answer: every parameter set, plus two deploy.sh must ignore.</summary>
    private string ParametersJson()
    {
        var answer = Parameters.ToDictionary(parameter => $"{SsmPath}/{parameter.Key}", parameter => parameter.Value);
        answer[$"{SsmPath}/EXTRA_NOT_ALLOWED"] = "ExtraValueNotAllowed0";
        answer[$"{SsmPath}/old/APP_HOST"] = "stale.disputedesk.test";
        return JsonSerializer.Serialize(new { Parameters = answer.Select(parameter => new { Name = parameter.Key, parameter.Value, Type = "SecureString" }) });
    }

    [GeneratedRegex(@"^POSTGRES_IMAGE=(?<image>\S+)$", RegexOptions.Multiline)]
    private static partial Regex PostgresImageLine();
}
