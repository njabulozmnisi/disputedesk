using System.Runtime.Versioning;
using System.Text.Json.Nodes;

namespace DisputeDesk.PlatformTests;

/// <summary>
/// Story 7.5 criterion 6: the instance's deploy script, offline. The unchanged infra/aws/deploy.sh runs in a fresh
/// throwaway copy per test with aws, curl, docker, iptables, iptables-save and systemctl stubbed (<see cref="DeployCopy"/>);
/// no AWS endpoint, container or firewall is touched. Each input's bound (infra/aws/input-bounds.sh, story 7.4) is tested
/// here once, through the deploy, on both sides.
/// </summary>
[UnsupportedOSPlatform("windows")] // the stubs need their execute bit
public sealed class DeployScriptTests
{
    private const UnixFileMode Mode0644 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private const UnixFileMode Mode0600 = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // ---- a first deploy

    [Fact]
    public async Task FirstDeploy_ReadsSsmWritesTheFilesInitsSetsTheRulesThenStartsCompose()
    {
        using var deploy = new DeployCopy();

        ProcessResult run = await deploy.RunAsync();
        StubCall[] calls = deploy.Stubs.Calls();

        Assert.True(run.ExitCode == 0, $"The deploy failed: {run.Output}{run.Errors}");
        // Reads the parameters under SSM_PATH with get-parameters-by-path --recursive --with-decryption.
        Assert.Equal(deploy.ParametersCall(), Assert.Single(deploy.Stubs.Calls("aws")).Arguments);
        // The region's CA bundle and CloudFront's ranges, in that order.
        Assert.Equal([deploy.CaBundleCall(), DeployCopy.RangesCall], deploy.Stubs.Calls("curl").Select(call => call.Arguments));
        // The count query, the init (the unchanged script, in deploy.sh's pinned postgres image) and Compose up last.
        Assert.Equal([deploy.CountCall(), deploy.InitCall(), DeployCopy.ComposeUpCall], deploy.Stubs.Calls("docker").Select(call => call.Arguments));
        // DOCKER-USER: RETURN for the api (.10) and collector (.11), then DROP for the Compose subnet, saved for boot.
        Assert.Equal([DeployCopy.RuleCheckCall, .. DeployCopy.RuleInsertCalls], deploy.Stubs.Calls("iptables").Select(call => call.Arguments));
        Assert.Equal(["-f", "/etc/sysconfig/iptables"], Assert.Single(deploy.Stubs.Calls("iptables-save")).Arguments);
        Assert.Equal(["enable", "iptables"], Assert.Single(deploy.Stubs.Calls("systemctl")).Arguments);
        // Reads SSM, fetches, inits, sets the rules, then runs Compose up --wait with the AWS override, last.
        Assert.Equal(["aws", "curl", "docker", "iptables", "iptables-save", "systemctl", "docker"], CommandRuns(calls));
        Assert.Equal(DeployCopy.ComposeUpCall, calls[^1].Arguments);
        // No --profile and no COMPOSE_PROFILES: Redpanda Console (profile console) never starts in AWS.
        Assert.False(calls[^1].Environment.ContainsKey("COMPOSE_PROFILES"));
        AssertNoSecretShown(deploy, run);
    }

    [Fact]
    public async Task FirstDeploy_WritesEnvWithExactlyTheInputsMode0600ByARename()
    {
        using var deploy = new DeployCopy();

        ProcessResult run = await deploy.RunAsync();

        Assert.True(run.ExitCode == 0, $"The deploy failed: {run.Output}{run.Errors}");
        // Exactly input-bounds.sh's inputs, from SSM, the arguments and the two fixed paths: no QA_DB_PASSWORD, no
        // RDS_MASTER_PASSWORD, no parameter off the allow-list or in a deeper path, nothing of the stale file.
        Dictionary<string, string> testValues = AwsInputs.TestValues();
        IEnumerable<string> expected = (await AwsInputs.NamesAsync()).Select(name => name switch
        {
            "RDS_CA_BUNDLE" => $"{name}={deploy.Repo}/rds-ca-bundle.pem",
            "GATEWAY_REALIP_CONF" => $"{name}={deploy.Repo}/cloudfront-realip.conf",
            "AWS_REGION" => $"{name}=eu-west-1",
            _ => $"{name}={testValues[name]}",
        });
        string env = Path.Combine(deploy.Repo, ".env");
        Assert.Equal(expected, await File.ReadAllLinesAsync(env, TestContext.Current.CancellationToken));
        Assert.Equal(Mode0600, File.GetUnixFileMode(env));
        // The old file's second name still holds the old content: .env was renamed over it, not written in place.
        Assert.Equal(DeployCopy.Stale, await File.ReadAllTextAsync(deploy.StaleEnv, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(Directory.GetFiles(deploy.Repo), file => Path.GetFileName(file).StartsWith(".env.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FirstDeploy_WritesTheCaBundleAndTheOriginFacingIpv4RangesMode0644()
    {
        using var deploy = new DeployCopy();

        ProcessResult run = await deploy.RunAsync();

        Assert.True(run.ExitCode == 0, $"The deploy failed: {run.Output}{run.Errors}");
        string bundle = Path.Combine(deploy.Repo, "rds-ca-bundle.pem");
        Assert.Contains("-----BEGIN CERTIFICATE-----", await File.ReadAllTextAsync(bundle, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(Mode0644, File.GetUnixFileMode(bundle));
        // CLOUDFRONT_ORIGIN_FACING IPv4 ranges only: not CLOUDFRONT, not EC2, not IPv6.
        string realIp = Path.Combine(deploy.Repo, "cloudfront-realip.conf");
        Assert.Equal("set_real_ip_from 192.0.2.0/26;\nset_real_ip_from 198.51.100.0/24;\n", await File.ReadAllTextAsync(realIp, TestContext.Current.CancellationToken));
        Assert.Equal(Mode0644, File.GetUnixFileMode(realIp));
    }

    [Fact]
    public async Task FirstDeploy_PassesEveryDatabaseValueToTheContainersByNameOnly()
    {
        // docker hands each -e NAME the value in its own environment: the count sees only the master's connection, the
        // init adds POSTGRES_USER, PGOPTIONS and the three role passwords; verify-full as dbmaster both times.
        using var deploy = new DeployCopy();
        Dictionary<string, string> testValues = AwsInputs.TestValues();
        var connection = new Dictionary<string, string>
        {
            ["PGHOST"] = testValues["DB_HOST"],
            ["PGSSLMODE"] = "verify-full",
            ["PGSSLROOTCERT"] = "/run/rds/ca-bundle.pem",
            ["PGUSER"] = "dbmaster",
            ["PGDATABASE"] = "postgres",
            ["PGPASSWORD"] = DeployCopy.MasterPassword,
            ["POSTGRES_USER"] = "",
            ["PGOPTIONS"] = "",
            ["QA_DB_PASSWORD"] = "",
            ["APP_DB_PASSWORD"] = "",
            ["ZITADEL_DB_PASSWORD"] = "",
        };
        var init = new Dictionary<string, string>(connection)
        {
            ["POSTGRES_USER"] = "dbmaster",
            ["PGOPTIONS"] = "-c createrole_self_grant=set,inherit",
            ["QA_DB_PASSWORD"] = DeployCopy.QaPassword,
            ["APP_DB_PASSWORD"] = testValues["APP_DB_PASSWORD"],
            ["ZITADEL_DB_PASSWORD"] = testValues["ZITADEL_DB_PASSWORD"],
        };

        ProcessResult run = await deploy.RunAsync();
        StubCall[] docker = deploy.Stubs.Calls("docker");

        Assert.True(run.ExitCode == 0, $"The deploy failed: {run.Output}{run.Errors}");
        Assert.Equal(connection, ValuesOf(docker[0], connection.Keys));
        Assert.Equal(init, ValuesOf(docker[1], init.Keys));
    }

    // ---- a later deploy

    [Fact]
    public async Task LaterDeploy_SkipsTheInitAndInsertsTheRulesOnlyOnce()
    {
        using var deploy = new DeployCopy { RolesAndDatabases = "3 2", RuleExists = true };

        ProcessResult run = await deploy.RunAsync();

        Assert.True(run.ExitCode == 0, $"The deploy failed: {run.Output}{run.Errors}");
        Assert.Equal([deploy.CountCall(), DeployCopy.ComposeUpCall], deploy.Stubs.Calls("docker").Select(call => call.Arguments));
        Assert.Equal(DeployCopy.RuleCheckCall, Assert.Single(deploy.Stubs.Calls("iptables")).Arguments);
        Assert.Empty(deploy.Stubs.Calls("iptables-save"));
        Assert.Empty(deploy.Stubs.Calls("systemctl"));
        AssertNoSecretShown(deploy, run);
    }

    [Fact]
    public async Task LaterDeploy_FailedComposeUp_ExitsWithComposesExitCode()
    {
        using var deploy = new DeployCopy { RolesAndDatabases = "3 2", RuleExists = true, ComposeExit = 7 };

        ProcessResult run = await deploy.RunAsync();

        Assert.Equal(7, run.ExitCode);
        AssertNoSecretShown(deploy, run);
    }

    // ---- every failure exits non-zero, naming its cause

    [Theory]
    [InlineData("APP_DB_PASSWORD")]
    [InlineData("QA_DB_PASSWORD")]
    [InlineData("RDS_MASTER_PASSWORD")]
    public async Task MissingParameter_FailsBeforeDockerNamingIt(string name)
    {
        using var deploy = new DeployCopy();
        deploy.Parameters.Remove(name);

        await AssertFailsAsync(deploy, $"{name} is missing", beforeDocker: true);
    }

    [Fact]
    public async Task TwoMissingParameters_FailBeforeDockerNamingTheFirst()
    {
        using var deploy = new DeployCopy();
        deploy.Parameters.Remove("MAILPIT_UI_AUTH");
        deploy.Parameters.Remove("APP_HOST");

        await AssertFailsAsync(deploy, "MAILPIT_UI_AUTH is missing", beforeDocker: true);
    }

    [Fact]
    public async Task FourImageArguments_FailBeforeDockerWithTheUsage()
    {
        using var deploy = new DeployCopy();
        deploy.Images = deploy.Images[..4];

        await AssertFailsAsync(deploy, "usage:", beforeDocker: true);
    }

    [Fact]
    public async Task MissingRegion_FailsBeforeDocker()
    {
        using var deploy = new DeployCopy { Region = null };

        await AssertFailsAsync(deploy, "AWS_REGION is missing or outside its bound", beforeDocker: true);
    }

    [Fact]
    public async Task SsmPathWithATrailingSlash_FailsBeforeDocker()
    {
        using var deploy = new DeployCopy { DeploySsmPath = $"{DeployCopy.SsmPath}/" };

        await AssertFailsAsync(deploy, "SSM_PATH is missing", beforeDocker: true);
    }

    [Fact]
    public async Task FailedSsmRead_FailsBeforeDocker()
    {
        using var deploy = new DeployCopy { AwsExit = 255 };

        await AssertFailsAsync(deploy, $"reading the parameters under {DeployCopy.SsmPath} failed", beforeDocker: true);
    }

    [Fact]
    public async Task FailedCaBundleFetch_FailsBeforeDocker()
    {
        using var deploy = new DeployCopy { CaExit = 22 };

        await AssertFailsAsync(deploy, "fetching the RDS CA bundle failed", beforeDocker: true);
    }

    [Fact]
    public async Task FailedRangesFetch_FailsBeforeDocker()
    {
        using var deploy = new DeployCopy { RangesExit = 22 };

        await AssertFailsAsync(deploy, "fetching ip-ranges.json failed", beforeDocker: true);
    }

    [Fact]
    public async Task NoOriginFacingIpv4Range_FailsBeforeDocker()
    {
        using var deploy = new DeployCopy { Ranges = """{ "prefixes": [{ "ip_prefix": "203.0.113.0/25", "service": "CLOUDFRONT" }], "ipv6_prefixes": [] }""" };

        await AssertFailsAsync(deploy, "lists no CLOUDFRONT_ORIGIN_FACING IPv4 range", beforeDocker: true);
    }

    [Fact]
    public async Task RangeThatIsNotIpv4_FailsBeforeDocker()
    {
        using var deploy = new DeployCopy { Ranges = """{ "prefixes": [{ "ip_prefix": "192.0.2.0/26;", "service": "CLOUDFRONT_ORIGIN_FACING" }], "ipv6_prefixes": [] }""" };

        await AssertFailsAsync(deploy, "holds a range that is not IPv4", beforeDocker: true);
    }

    [Theory]
    [InlineData("1 1")] // a role present with a database missing
    [InlineData("3 1")] // all three roles with one database
    public async Task HalfDoneDatabaseInit_FailsBeforeCompose(string rolesAndDatabases)
    {
        using var deploy = new DeployCopy { RolesAndDatabases = rolesAndDatabases };

        await AssertFailsAsync(deploy, "A half-done database init", beforeDocker: false);
    }

    [Fact]
    public async Task FailedInit_FailsBeforeCompose()
    {
        using var deploy = new DeployCopy { InitExit = 1 };

        await AssertFailsAsync(deploy, "the database init failed", beforeDocker: false);
    }

    // ---- each input's bound, once, on both sides (story 7.4's Inputs, input-bounds.sh)

    /// <summary>The shortest and longest accepted lengths, and the test env's value where the bound has no upper length.</summary>
    public static TheoryData<string, string> AtTheirBounds()
    {
        var data = new TheoryData<string, string>();
        foreach (string name in new[] { "APP_DB_PASSWORD", "ZITADEL_DB_PASSWORD", "LOGIN_SESSION_COOKIE_SECRET", "GATEWAY_ORIGIN_VERIFY", "QA_DB_PASSWORD" })
        {
            data.Add(name, Repeat('a', 32));
            data.Add(name, Repeat('Z', 128));
        }

        data.Add("ZITADEL_MASTERKEY", Repeat('a', 32));
        data.Add("ZITADEL_ADMIN_PASSWORD", "Aa1-" + Repeat('a', 28));
        data.Add("ZITADEL_ADMIN_PASSWORD", "Aa1-" + Repeat('a', 124));
        data.Add("LINKING_OTP_HMAC_KEY", Convert.ToBase64String(new byte[32]));
        data.Add("MAILPIT_UI_AUTH", "u:" + Repeat('p', 32));
        data.Add("MAILPIT_UI_AUTH", Repeat('u', 32) + ":" + Repeat('p', 128));
        foreach (string name in new[] { "APP_HOST", "AUTH_HOST", "MAILPIT_HOST", "DB_HOST" })
        {
            data.Add(name, HostName(253));
        }

        data.Add("OTEL_LOG_GROUP", "a");
        data.Add("OTEL_LOG_GROUP", "/" + Repeat('a', 511));
        data.Add("DATAPROTECTION_KMS_KEY_ID", KmsKeyArn(Repeat('0', 12))); // the ARN form; the test env holds the key ID form
        data.Add("RDS_MASTER_PASSWORD", Repeat('a', 64));
        // No upper length: the test env's values (the first deploy runs with every other one).
        data.Add("AWS_REGION", AwsInputs.TestValues()["AWS_REGION"]);
        return data;
    }

    /// <summary>One character shorter and one longer than the bound, and a forbidden character or a missing class.</summary>
    public static TheoryData<string, string> PastTheirBounds()
    {
        var data = new TheoryData<string, string>();
        Dictionary<string, string> testValues = AwsInputs.TestValues();
        foreach (string name in new[] { "APP_DB_PASSWORD", "ZITADEL_DB_PASSWORD", "LOGIN_SESSION_COOKIE_SECRET", "GATEWAY_ORIGIN_VERIFY", "QA_DB_PASSWORD" })
        {
            data.Add(name, Repeat('a', 31));
            data.Add(name, Repeat('a', 129));
        }

        // 32 characters, one outside [A-Za-z0-9]: ; adds a connection-string keyword, $ breaks .env interpolation.
        foreach (string name in new[] { "APP_DB_PASSWORD", "ZITADEL_DB_PASSWORD", "LOGIN_SESSION_COOKIE_SECRET", "GATEWAY_ORIGIN_VERIFY" })
        {
            data.Add(name, Repeat('a', 31) + "$");
            data.Add(name, Repeat('a', 31) + "-");
        }

        foreach (string name in new[] { "ZITADEL_DB_PASSWORD", "LOGIN_SESSION_COOKIE_SECRET", "GATEWAY_ORIGIN_VERIFY" })
        {
            data.Add(name, Repeat('a', 31) + ";");
        }

        data.Add("APP_DB_PASSWORD", testValues["APP_DB_PASSWORD"] + ";");
        data.Add("QA_DB_PASSWORD", DeployCopy.QaPassword + "-");
        data.Add("ZITADEL_MASTERKEY", Repeat('a', 31));
        data.Add("ZITADEL_MASTERKEY", Repeat('a', 33));
        data.Add("ZITADEL_ADMIN_PASSWORD", "Aa1-" + Repeat('a', 27));
        data.Add("ZITADEL_ADMIN_PASSWORD", "Aa1-" + Repeat('a', 125));
        data.Add("ZITADEL_ADMIN_PASSWORD", "aa1-" + Repeat('a', 28)); // no upper-case letter
        data.Add("ZITADEL_ADMIN_PASSWORD", "AA1-" + Repeat('A', 28)); // no lower-case letter
        data.Add("ZITADEL_ADMIN_PASSWORD", "Aaa-" + Repeat('a', 28)); // no digit
        data.Add("ZITADEL_ADMIN_PASSWORD", "Aa1" + Repeat('a', 29)); // no -
        data.Add("ZITADEL_ADMIN_PASSWORD", "$" + testValues["ZITADEL_ADMIN_PASSWORD"]);
        data.Add("LINKING_OTP_HMAC_KEY", Convert.ToBase64String(new byte[31]));
        data.Add("LINKING_OTP_HMAC_KEY", Convert.ToBase64String(new byte[32]).TrimEnd('=')); // not padded
        data.Add("LINKING_OTP_HMAC_KEY", Convert.ToBase64String(new byte[32])[..^4] + "AAA!"); // a character outside base64
        data.Add("MAILPIT_UI_AUTH", ":" + Repeat('p', 32));
        data.Add("MAILPIT_UI_AUTH", Repeat('u', 33) + ":" + Repeat('p', 32));
        data.Add("MAILPIT_UI_AUTH", "u:" + Repeat('p', 31));
        data.Add("MAILPIT_UI_AUTH", "u:" + Repeat('p', 129));
        data.Add("MAILPIT_UI_AUTH", Repeat('p', 32)); // no colon
        data.Add("MAILPIT_UI_AUTH", "u:" + Repeat('p', 32) + " v:" + Repeat('p', 32)); // two pairs
        foreach (string name in new[] { "APP_HOST", "AUTH_HOST", "MAILPIT_HOST", "DB_HOST" })
        {
            data.Add(name, HostName(254));
            data.Add(name, testValues[name] + ":443"); // a port
            data.Add(name, "localhost"); // a single label
            data.Add(name, "https://" + testValues[name]); // a scheme
        }

        data.Add("OTEL_LOG_GROUP", "/" + Repeat('a', 512));
        data.Add("OTEL_LOG_GROUP", "/disputedesk otel");
        data.Add("DATAPROTECTION_SSM_PATH", testValues["DATAPROTECTION_SSM_PATH"] + "/");
        data.Add("DATAPROTECTION_SSM_PATH", testValues["DATAPROTECTION_SSM_PATH"] + "#");
        data.Add("DATAPROTECTION_SSM_PATH", testValues["DATAPROTECTION_SSM_PATH"].TrimStart('/')); // no leading /
        data.Add("DATAPROTECTION_SSM_PATH", "/");
        data.Add("DATAPROTECTION_KMS_KEY_ID", "alias/aws/ssm");
        data.Add("DATAPROTECTION_KMS_KEY_ID", $"arn:aws:kms:af-south-1:{Repeat('0', 12)}:alias/aws/ssm"); // an alias ARN
        data.Add("DATAPROTECTION_KMS_KEY_ID", KmsKeyArn(Repeat('0', 11))); // an 11-digit account
        data.Add("AWS_REGION", "af-south");
        data.Add("AWS_REGION", "af_south-1");
        foreach (string name in DeployCopy.ImageNames)
        {
            data.Add(name, testValues[name].Replace("@sha256:", ":sha-", StringComparison.Ordinal)); // a tag, not a digest
        }

        data.Add("API_IMAGE", testValues["API_IMAGE"][..^1]); // a 63-digit digest
        data.Add("API_IMAGE", testValues["API_IMAGE"].Replace("ghcr.io/", "docker.io/", StringComparison.Ordinal)); // another registry

        data.Add("RDS_MASTER_PASSWORD", Repeat('a', 63));
        data.Add("RDS_MASTER_PASSWORD", Repeat('a', 65));
        data.Add("RDS_MASTER_PASSWORD", DeployCopy.MasterPassword[1..] + "/");
        return data;
    }

    [Theory]
    [MemberData(nameof(AtTheirBounds))]
    public async Task Input_AtItsBound_IsAccepted(string name, string value)
    {
        using var deploy = new DeployCopy();
        deploy.Set(name, value);

        ProcessResult run = await deploy.RunAsync();

        Assert.True(run.ExitCode == 0, $"{name} was refused: {run.Output}{run.Errors}");
    }

    [Theory]
    [MemberData(nameof(PastTheirBounds))]
    public async Task Input_PastItsBound_IsRefusedBeforeDockerNamingIt(string name, string value)
    {
        using var deploy = new DeployCopy();
        deploy.Set(name, value);

        await AssertFailsAsync(deploy, name == "AWS_REGION" ? "AWS_REGION is missing or outside its bound" : $"{name} is outside its bound", beforeDocker: true);
    }

    [Fact]
    public async Task EmptyLogGroup_IsRefusedBeforeDockerAsMissing()
    {
        // One character shorter than OTEL_LOG_GROUP's 1 to 512: deploy.sh refuses an empty value as missing.
        using var deploy = new DeployCopy();
        deploy.Set("OTEL_LOG_GROUP", "");

        await AssertFailsAsync(deploy, "OTEL_LOG_GROUP is missing", beforeDocker: true);
    }

    [Theory]
    [InlineData("RDS_CA_BUNDLE")]
    [InlineData("GATEWAY_REALIP_CONF")]
    public async Task DerivedPath_IsCheckedAgainstItsBound(string name)
    {
        // deploy.sh sets these two itself (the files beside .env), so no deploy input reaches their bound; within_bounds,
        // which deploy.sh calls on them, is called directly.
        string value = AwsInputs.TestValues()[name];

        Assert.True(await AwsInputs.WithinBoundsAsync(name, value));
        Assert.False(await AwsInputs.WithinBoundsAsync(name, value + "/"));
        Assert.False(await AwsInputs.WithinBoundsAsync(name, Path.GetFileName(value))); // a relative path
        Assert.False(await AwsInputs.WithinBoundsAsync(name, value.Replace("/opt/", "/opt dir/", StringComparison.Ordinal)));
    }

    // ---- the script never prints what it reads

    [Fact]
    public void Source_NeverPrintsWhatItReads()
    {
        // Text: no run can show that no branch traces or prints the values.
        string code = string.Join('\n', File.ReadAllLines(Path.Combine(SourceRoots.Repo, "infra/aws/deploy.sh")).Where(line => !line.TrimStart().StartsWith('#')));

        foreach (string pattern in new[] { "set -x", "set -o xtrace", "cat .env", "compose config", "compose logs", "compose -f docker-compose.yml -f infra/aws/compose.yml config" })
        {
            Assert.DoesNotContain(pattern, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PostgresImage_IsDockerComposesPinnedPostgres()
    {
        JsonNode model = await ComposeModel.RenderAsync(["docker-compose.yml"]);

        Assert.Equal(model["services"]!["postgres"]!["image"]!.GetValue<string>(), DeployCopy.PostgresImage);
    }

    /// <summary>
    /// The deploy exits non-zero with the message on standard error, starts no Compose and shows no secret; before any
    /// docker call it also leaves the stale .env untouched.
    /// </summary>
    private static async Task AssertFailsAsync(DeployCopy deploy, string message, bool beforeDocker)
    {
        ProcessResult run = await deploy.RunAsync();
        StubCall[] docker = deploy.Stubs.Calls("docker");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains(message, run.Errors, StringComparison.Ordinal);
        Assert.DoesNotContain(docker, call => call.Arguments is ["compose", ..]);
        if (beforeDocker)
        {
            Assert.Empty(docker);
            Assert.Equal(DeployCopy.Stale, await File.ReadAllTextAsync(Path.Combine(deploy.Repo, ".env"), TestContext.Current.CancellationToken));
            Assert.Equal(DeployCopy.Stale, await File.ReadAllTextAsync(deploy.StaleEnv, TestContext.Current.CancellationToken));
        }

        AssertNoSecretShown(deploy, run);
    }

    /// <summary>No secret in the script's output or errors, or in any stubbed command's arguments.</summary>
    private static void AssertNoSecretShown(DeployCopy deploy, ProcessResult run)
    {
        string shown = run.Output + run.Errors + string.Join('\n', deploy.Stubs.Calls().SelectMany(call => call.Arguments));
        foreach (string secret in DeployCopy.Secrets)
        {
            Assert.False(shown.Contains(secret, StringComparison.Ordinal), "The output or a command's arguments hold a secret.");
        }
    }

    /// <summary>The commands in call order, with consecutive calls to one command counted once.</summary>
    private static string[] CommandRuns(StubCall[] calls) =>
        [.. calls.Select(call => call.Command).Where((command, index) => index == 0 || calls[index - 1].Command != command)];

    /// <summary>What docker would hand the container for each -e NAME: the value in its environment, or empty.</summary>
    private static Dictionary<string, string> ValuesOf(StubCall call, IEnumerable<string> names) =>
        names.ToDictionary(name => name, name => call.Environment.GetValueOrDefault(name, ""), StringComparer.Ordinal);

    private static string Repeat(char character, int count) => new(character, count);

    /// <summary>The test env's KMS key by its ARN, in the given account.</summary>
    private static string KmsKeyArn(string account) =>
        $"arn:aws:kms:af-south-1:{account}:key/{AwsInputs.TestValues()["DATAPROTECTION_KMS_KEY_ID"]}";

    /// <summary>A host name of the given length: 63-character labels, and a shorter last one.</summary>
    private static string HostName(int length)
    {
        string labels = $"{Repeat('a', 63)}.{Repeat('b', 63)}.{Repeat('c', 63)}.";
        return labels + Repeat('d', length - labels.Length);
    }
}
