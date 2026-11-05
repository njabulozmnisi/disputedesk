using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DisputeDesk.PlatformTests;

/// <summary>infra/deploy/aws/compose.yml merged onto docker-compose.yml, rendered once with the dummy inputs in compose.test.env.</summary>
public sealed class AwsComposeModel : IAsyncLifetime
{
    public static readonly string[] Files = ["docker-compose.yml", "infra/deploy/aws/compose.yml"];

    public JsonNode Model { get; private set; } = new JsonObject();

    public JsonNode Services => Model["services"]!;

    public async ValueTask InitializeAsync() => Model = await ComposeModel.RenderAsync(Files, AwsInputs.TestEnvFile);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Story 7.4 criteria 1, 2 and 5: the AWS override, as `docker compose config --format json` renders it with the dummy
/// inputs (the child's environment holds none of them), and the collector config. Two things a rendered model cannot
/// show stay source checks on compose.yml: which inputs it references, and that every reference is required. The render
/// is authoritative with CI's Compose 5.6.1; locally it matches only on the same version.
/// </summary>
public sealed partial class ComposeRenderTests(AwsComposeModel aws) : IClassFixture<AwsComposeModel>
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    /// <summary>compose.yml without its comments: the ${...} references and the values it sets.</summary>
    private static readonly string OverrideSource = string.Join('\n', File.ReadAllLines(Path.Combine(SourceRoots.Repo, "infra/deploy/aws/compose.yml"))
        .Where(line => !line.TrimStart().StartsWith('#'))
        .Select(line => TrailingComment().Replace(line, "")));

    private static readonly Dictionary<string, string> TestValues = AwsInputs.TestValues();

    // ---- criterion 1: every input is required, and only the inputs are

    [Fact]
    public async Task Inputs_AreTheSameInTheOverrideTheTestEnvAndInputBounds()
    {
        string[] inputs = [.. (await AwsInputs.NamesAsync()).Order(StringComparer.Ordinal)];

        Assert.Equal(inputs, TestValues.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(inputs, References().Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EachInput_IsReferencedWhereverItIsUsed()
    {
        // Source: a literal in a reference's place renders the same with the test inputs, so each input's count of
        // references is fixed here; a dropped one fails naming the input.
        var expected = new Dictionary<string, int>
        {
            ["APP_DB_PASSWORD"] = 1,
            ["ZITADEL_DB_PASSWORD"] = 1,
            ["LOGIN_SESSION_COOKIE_SECRET"] = 1,
            ["ZITADEL_MASTERKEY"] = 1,
            ["ZITADEL_ADMIN_PASSWORD"] = 1,
            ["LINKING_OTP_HMAC_KEY"] = 1,
            ["MAILPIT_UI_AUTH"] = 1,
            ["GATEWAY_ORIGIN_VERIFY"] = 1,
            ["APP_HOST"] = 3,
            ["AUTH_HOST"] = 9,
            ["MAILPIT_HOST"] = 1,
            ["DB_HOST"] = 2,
            ["AWS_REGION"] = 2,
            ["OTEL_LOG_GROUP"] = 1,
            ["DATAPROTECTION_SSM_PATH"] = 1,
            ["DATAPROTECTION_KMS_KEY_ID"] = 1,
            ["RDS_CA_BUNDLE"] = 1,
            ["GATEWAY_REALIP_CONF"] = 1,
            ["API_IMAGE"] = 1,
            ["WORKER_IMAGE"] = 1,
            ["GATEWAY_IMAGE"] = 1,
            ["CORE_BANKING_IMAGE"] = 1,
            ["ZITADEL_IMAGE"] = 1,
        };
        Dictionary<string, int> references = References();

        foreach ((string name, int count) in expected)
        {
            Assert.True(references.GetValueOrDefault(name) == count, $"compose.yml references {name} {references.GetValueOrDefault(name)} times, not {count}.");
        }
    }

    [Fact]
    public void EveryReference_IsRequiredWithAMessage()
    {
        // Source: ${VAR:?message} only: no :- or - default, and no bare $VAR ($$ is a literal $).
        string[] notRequired = [.. AnyReference().Matches(OverrideSource).Select(match => match.Value).Where(reference => !RequiredReference().IsMatch(reference))];
        string[] unbraced = [.. UnbracedReference().Matches(OverrideSource.Replace("$$", "", StringComparison.Ordinal)).Select(match => match.Value)];

        Assert.True(notRequired.Length == 0, $"References without :?message: {string.Join(", ", notRequired)}");
        Assert.True(unbraced.Length == 0, $"Unbraced references: {string.Join(", ", unbraced)}");
    }

    [Fact]
    public async Task TestEnvValues_MeetTheirBounds()
    {
        // The bounds themselves are tested once, through the deploy (DeployScriptTests).
        List<string> outside = [];
        foreach ((string name, string value) in TestValues)
        {
            if (!await AwsInputs.WithinBoundsAsync(name, value))
            {
                outside.Add(name);
            }
        }

        Assert.Empty(outside);
    }

    public static TheoryData<string> InputNames() => [.. AwsInputs.TestValues().Keys];

    [Theory]
    [MemberData(nameof(InputNames))]
    public async Task Render_WithoutAnInput_FailsNamingIt(string name)
    {
        string envFile = Path.GetTempFileName();
        await File.WriteAllLinesAsync(envFile, TestValues.Where(input => input.Key != name).Select(input => $"{input.Key}={input.Value}"), TestContext.Current.CancellationToken);

        ProcessResult render = await Processes.RunAsync(
            "docker", ["compose", "-f", AwsComposeModel.Files[0], "-f", AwsComposeModel.Files[1], "--env-file", envFile, "config", "--quiet"],
            Processes.BaseEnvironment(), CommandTimeout);
        File.Delete(envFile);

        Assert.NotEqual(0, render.ExitCode);
        Assert.Contains($"required variable {name} is missing a value", render.Errors, StringComparison.Ordinal);
    }

    // ---- criterion 2: only the AWS services start, and nothing local is left

    [Fact]
    public void Render_StartsOnlyTheAwsServices()
    {
        Assert.Equal(
            ["api", "core-banking", "gateway", "mailpit", "otel-collector", "redpanda", "worker", "zitadel"],
            aws.Services.AsObject().Select(service => service.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task LocalProfile_HoldsPostgresAndTheDashboard_AndNoStartedServiceDependsOnThem()
    {
        JsonNode local = (await ComposeModel.RenderAsync(AwsComposeModel.Files, AwsInputs.TestEnvFile, new Dictionary<string, string> { ["COMPOSE_PROFILES"] = "local" }))["services"]!;

        foreach (string name in new[] { "postgres", "aspire-dashboard" })
        {
            Assert.Equal(["local"], Strings(local[name]!["profiles"]!));
        }

        Assert.DoesNotContain(aws.Services.AsObject(), service => service.Value!["depends_on"] is JsonObject dependsOn
            && (dependsOn.ContainsKey("postgres") || dependsOn.ContainsKey("aspire-dashboard")));
        // Redpanda Console is a local tool: the AWS render never starts it, not even under the local profile.
        Assert.False(aws.Services.AsObject().ContainsKey("redpanda-console"));
        Assert.False(local.AsObject().ContainsKey("redpanda-console"));
    }

    [Fact]
    public async Task RedpandaConsole_StartsLocallyOnlyUnderItsProfileOnLoopbackWithTheImagesUser()
    {
        JsonNode plain = (await ComposeModel.RenderAsync(["docker-compose.yml"]))["services"]!;
        JsonNode console = (await ComposeModel.RenderAsync(["docker-compose.yml"], variables: new Dictionary<string, string> { ["COMPOSE_PROFILES"] = "console" }))["services"]!["redpanda-console"]!;

        Assert.False(plain.AsObject().ContainsKey("redpanda-console"));
        Assert.Equal(["console"], Strings(console["profiles"]!));
        Assert.Equal(["127.0.0.1"], console["ports"]!.AsArray().Select(port => port!["host_ip"]!.GetValue<string>()).Distinct());
        Assert.False(console.AsObject().ContainsKey("user"));
    }

    [Fact]
    public async Task Spa_IsBuildOnlyAndAbsentFromTheAwsRender()
    {
        // ad-011: the gateway's build names it, and its profile keeps it from getting a container.
        JsonNode plain = (await ComposeModel.RenderAsync(["docker-compose.yml"]))["services"]!;
        JsonNode build = (await ComposeModel.RenderAsync(["docker-compose.yml"], variables: new Dictionary<string, string> { ["COMPOSE_PROFILES"] = "build" }))["services"]!;

        Assert.False(plain.AsObject().ContainsKey("spa"));
        Assert.Equal(["build"], Strings(build["spa"]!["profiles"]!));
        Assert.Equal("service:spa", build["gateway"]!["build"]!["additional_contexts"]!["spa"]!.GetValue<string>());
        Assert.False(aws.Services.AsObject().ContainsKey("spa"));
    }

    /// <summary>
    /// Each service whose environment compose.yml replaces with !override, with the exact keys it sets, and its
    /// depends_on keys (replaced with !override, or cleared with !reset, so empty): a dropped tag merges the base file's
    /// keys back in (Compose, "Merge Compose files").
    /// </summary>
    public static TheoryData<string, string[], string[]> OverriddenServices() => new()
    {
        {
            "zitadel",
            [
                "GOMEMLIMIT", "ZITADEL_DATABASE_POSTGRES_DSN", "ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_REQUIRED",
                "ZITADEL_DEFAULTINSTANCE_SMTPCONFIGURATION_FROM", "ZITADEL_DEFAULTINSTANCE_SMTPCONFIGURATION_FROMNAME", "ZITADEL_DEFAULTINSTANCE_SMTPCONFIGURATION_SMTP_HOST",
                "ZITADEL_DEFAULTINSTANCE_SMTPCONFIGURATION_TLS", "ZITADEL_EXTERNALDOMAIN", "ZITADEL_EXTERNALPORT", "ZITADEL_EXTERNALSECURE", "ZITADEL_FIRSTINSTANCE_INSTANCENAME",
                "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_EMAIL_ADDRESS", "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_EMAIL_VERIFIED",
                "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_FIRSTNAME", "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_LASTNAME", "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_PASSWORD",
                "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_PASSWORDCHANGEREQUIRED", "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_USERNAME", 
                "ZITADEL_FIRSTINSTANCE_ORG_MACHINE_MACHINE_NAME",
                "ZITADEL_FIRSTINSTANCE_ORG_MACHINE_MACHINE_USERNAME", "ZITADEL_FIRSTINSTANCE_ORG_MACHINE_PAT_EXPIRATIONDATE", "ZITADEL_FIRSTINSTANCE_ORG_NAME",
                "ZITADEL_FIRSTINSTANCE_PATPATH", "ZITADEL_MASTERKEY", "ZITADEL_PORT",
                "ZITADEL_TLS_ENABLED",
            ],
            []
        },
        { "provision", ["APP_ORIGINS", "AUTH_DEVELOPMENT_MODE", "FIXTURES_FILE", "OUT_DIR", "OUT_UID", "PAT_FILE", "ZITADEL_URL"], ["gateway", "zitadel"] },
        {
            
            ["CUSTOM_REQUEST_HEADERS", "EMAIL_VERIFICATION", "NEXT_PUBLIC_BASE_PATH", "ZITADEL_API_URL", "ZITADEL_SERVICE_USER_TOKEN_FILE", "ZITADEL_SESSION_COOKIE_SECRET"],
            ["zitadel"]
        },
        { "mailpit", ["MP_SMTP_AUTH_ACCEPT_ANY", "MP_SMTP_AUTH_ALLOW_INSECURE", "MP_UI_AUTH"], [] },
        {
            "api",
            [
                "ASPNETCORE_ENVIRONMENT", "AWS_REGION", "ConnectionStrings__DisputeDesk", "CoreBanking__BaseUrl", "DataProtection__KmsKeyId", "DataProtection__SsmParameterPath",
                "ForwardedHeaders__AllowedHosts", "ForwardedHeaders__TrustedNetwork", "Linking__OtpHmacKey", "OTEL_EXPORTER_OTLP_ENDPOINT", "OTEL_EXPORTER_OTLP_PROTOCOL",
                "OTEL_SERVICE_NAME", "Scalar__Enabled", "Seed__Enabled", "Seed__FixturesPath", "Sms__Host", "Sms__Port", "Sms__UseTls",
            ],
            ["core-banking"]
        },
        {
            "worker",
            ["ConnectionStrings__DisputeDesk", "DOTNET_ENVIRONMENT", "Kafka__BootstrapServers", "OTEL_EXPORTER_OTLP_ENDPOINT", "OTEL_EXPORTER_OTLP_PROTOCOL", "OTEL_SERVICE_NAME"],
            ["api"]
        },
        {
            "gateway",
            ["GATEWAY_APP_HOST", "GATEWAY_AUTH_HOST", "GATEWAY_AUTH_ORIGIN", "GATEWAY_HSTS", "GATEWAY_MAILPIT_HOST", "GATEWAY_ORIGIN_VERIFY", "NGINX_ENVSUBST_TEMPLATE_DIR"],
            ["zitadel"]
        },
    };

    [Theory]
    [MemberData(nameof(OverriddenServices))]
    public void OverriddenService_EnvironmentAndDependsOn_AreExactlyTheOverrides(string service, string[] environment, string[] dependsOn)
    {
        JsonNode rendered = aws.Services[service]!;

        Assert.True(environment.SequenceEqual(Keys(rendered["environment"]), StringComparer.Ordinal), $"{service}'s environment is not exactly compose.yml's: {string.Join(' ', Keys(rendered["environment"]))}");
        Assert.True(dependsOn.SequenceEqual(Keys(rendered["depends_on"]), StringComparer.Ordinal), $"{service}'s depends_on is not exactly compose.yml's: {string.Join(' ', Keys(rendered["depends_on"]))}");
    }

    [Fact]
    public void OnlyPublishedPort_IsTheGateways80To8080()
    {
        string[] published = [.. aws.Services.AsObject().SelectMany(service => (service.Value!["ports"]?.AsArray() ?? [])
            .Select(port => $"{service.Key} {port!["published"]}:{port["target"]}"))];

        Assert.Equal(["gateway 80:8080"], published);
    }

    [Fact]
    public void Render_HoldsNothingLocal()
    {
        string render = aws.Model.ToJsonString();

        foreach (string local in new[] { "localhost", "Development", "sslmode=disable" })
        {
            Assert.DoesNotContain(local, render, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(DevOnlyValue(), render);
        Assert.Equal("false", aws.Services["provision"]!["environment"]!["AUTH_DEVELOPMENT_MODE"]!.GetValue<string>());
        foreach (string name in new[] { "APP_HOST", "AUTH_HOST", "MAILPIT_HOST" })
        {
            Assert.DoesNotContain($"http://{TestValues[name]}", render, StringComparison.Ordinal);
            Assert.DoesNotContain($"{TestValues[name]}:8080", render, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DatabaseConnections_VerifyRdsWithTheCaBundleMountedReadOnly()
    {
        const string Ca = "/run/rds/ca-bundle.pem";

        Assert.Equal(
            $"postgresql://zitadel:{TestValues["ZITADEL_DB_PASSWORD"]}@{TestValues["DB_HOST"]}:5432/zitadel?sslmode=verify-full&sslrootcert={Ca}",
            EnvironmentValue("zitadel", "ZITADEL_DATABASE_POSTGRES_DSN"));
        foreach (string service in new[] { "api", "worker" })
        {
            Assert.Equal(
                $"Host={TestValues["DB_HOST"]};Port=5432;Database=disputedesk;Username=disputedesk;Password={TestValues["APP_DB_PASSWORD"]};SSL Mode=VerifyFull;Root Certificate={Ca}",
                EnvironmentValue(service, "ConnectionStrings__DisputeDesk"));
        }

        foreach (string service in new[] { "zitadel", "api", "worker" })
        {
            JsonNode mount = Assert.Single(aws.Services[service]!["volumes"]!.AsArray(), volume => volume!["target"]!.GetValue<string>() == Ca)!;
            Assert.Equal(TestValues["RDS_CA_BUNDLE"], mount["source"]!.GetValue<string>());
            Assert.True(mount["read_only"]!.GetValue<bool>(), $"{service} mounts the CA bundle writable.");
        }
    }

    [Fact]
    public void Zitadel_IsTheAuthHostOnHttpsBehindCloudFront()
    {
        Assert.Equal(TestValues["AUTH_HOST"], EnvironmentValue("zitadel", "ZITADEL_EXTERNALDOMAIN"));
        Assert.Equal("443", EnvironmentValue("zitadel", "ZITADEL_EXTERNALPORT"));
        Assert.Equal("true", EnvironmentValue("zitadel", "ZITADEL_EXTERNALSECURE"));
        Assert.Equal("false", EnvironmentValue("zitadel", "ZITADEL_TLS_ENABLED"));
    }

    [Fact]
    public void Api_TrustsForwardedHeadersOnlyFromTheGatewayAndKeepsItsCookieSettings()
    {
        // Only the gateway may set X-Forwarded-*: any wider network lets another container on the subnet spoof them.
        Assert.Equal(TestValues["APP_HOST"], EnvironmentValue("api", "ForwardedHeaders__AllowedHosts"));
        Assert.Equal("10.201.89.12/32", EnvironmentValue("api", "ForwardedHeaders__TrustedNetwork"));
        Assert.DoesNotContain(Keys(aws.Services["api"]!["environment"]), key => key.StartsWith("Authentication__", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("api", "API_IMAGE")]
    [InlineData("worker", "WORKER_IMAGE")]
    [InlineData("gateway", "GATEWAY_IMAGE")]
    [InlineData("core-banking", "CORE_BANKING_IMAGE")]
    [InlineData("provision", "ZITADEL_IMAGE")]
    public void Service_RunsItsPublishedImageAndBuildsNothing(string service, string image)
    {
        Assert.Equal(TestValues[image], aws.Services[service]!["image"]!.GetValue<string>());
        Assert.False(aws.Services[service]!.AsObject().ContainsKey("build"));
    }

    [Fact]
    public void Network_IsOneFixedSubnetWithThreePinnedAddresses()
    {
        // Dynamic addresses come from 10.201.89.128/25, so only api (.10), otel-collector (.11) and gateway (.12) are pinned.
        string[] pinned = [.. aws.Services.AsObject()
            .Where(service => service.Value!["networks"]?["default"]?["ipv4_address"] is not null)
            .Select(service => $"{service.Key}={service.Value!["networks"]!["default"]!["ipv4_address"]}")
            .Order(StringComparer.Ordinal)];

        Assert.Equal("""[{"subnet":"10.201.89.0/24","ip_range":"10.201.89.128/25"}]""", aws.Model["networks"]!["default"]!["ipam"]!["config"]!.ToJsonString());
        Assert.Equal(["api=10.201.89.10", "gateway=10.201.89.12", "otel-collector=10.201.89.11"], pinned);
    }

    [Fact]
    public void CollectorAndMailpit_PublishNoPort_MailpitHasBasicAuth_GatewayHasNoAlias()
    {
        Assert.Null(aws.Services["otel-collector"]!["ports"]);
        Assert.Null(aws.Services["mailpit"]!["ports"]);
        Assert.Null(aws.Services["mailpit"]!["environment"]!["MP_WEBROOT"]);
        Assert.Equal(TestValues["MAILPIT_UI_AUTH"], EnvironmentValue("mailpit", "MP_UI_AUTH"));
        Assert.Empty(aws.Services["gateway"]!["networks"]!["default"]?["aliases"]?.AsArray() ?? []);
    }

    [Fact]
    public void Gateway_RendersTheAwsTemplatesAndMountsTheRealIpFileReadOnly()
    {
        JsonNode mount = Assert.Single(aws.Services["gateway"]!["volumes"]!.AsArray(), volume => volume!["target"]!.GetValue<string>() == "/etc/nginx/cloudfront-realip.conf")!;

        Assert.Equal("/etc/nginx/templates-aws", EnvironmentValue("gateway", "NGINX_ENVSUBST_TEMPLATE_DIR"));
        Assert.Equal(TestValues["GATEWAY_REALIP_CONF"], mount["source"]!.GetValue<string>());
        Assert.True(mount["read_only"]!.GetValue<bool>());
    }

    [Fact]
    public void ApiAndWorker_ExportToTheCollector_ApiKeepsItsKeyRingInSsm()
    {
        Assert.Equal("http://otel-collector:4317", EnvironmentValue("api", "OTEL_EXPORTER_OTLP_ENDPOINT"));
        Assert.Equal("http://otel-collector:4317", EnvironmentValue("worker", "OTEL_EXPORTER_OTLP_ENDPOINT"));
        Assert.Equal(TestValues["DATAPROTECTION_SSM_PATH"], EnvironmentValue("api", "DataProtection__SsmParameterPath"));
        Assert.Equal(TestValues["DATAPROTECTION_KMS_KEY_ID"], EnvironmentValue("api", "DataProtection__KmsKeyId"));
        Assert.Equal(TestValues["AWS_REGION"], EnvironmentValue("api", "AWS_REGION"));
    }

    [Fact]
    public void LongRunningServices_FitTheInstancesMemoryBudget()
    {
        // The long-running services' mem_limit total stays within 3482 MiB (3.4 GiB); the one-shots finish before the api starts.
        const long BudgetMib = 3482;
        string[] longRunning = ["redpanda", "zitadel", "mailpit", "core-banking", "api", "worker", "otel-collector", "gateway"];

        Assert.Contains("--memory=512M", Strings(aws.Services["redpanda"]!["command"]!));
        Assert.All(longRunning, service => Assert.NotNull(aws.Services[service]!["mem_limit"]));
        long totalMib = longRunning.Sum(service => long.Parse(aws.Services[service]!["mem_limit"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture) / 1048576);
        Assert.True(totalMib <= BudgetMib, $"The long-running services' mem_limit total {totalMib} MiB, over {BudgetMib}.");
    }

    // ---- criterion 5: the collector config

    [Fact]
    public void CollectorConfig_HasNoDebugExporterAndLimitsMemoryFirstInEveryPipeline()
    {
        // Text: the pipelines' processor order and listen addresses are the config's contract; validate checks the rest.
        string[] config = File.ReadAllLines(Path.Combine(SourceRoots.Repo, "infra/images/otel-collector/config.yaml"));

        Assert.DoesNotContain(config, line => DebugWord().IsMatch(line));
        Assert.Equal(3, config.Count(line => line.StartsWith("      processors: ", StringComparison.Ordinal)));
        Assert.Equal(3, config.Count(line => line == "      processors: [memory_limiter, batch]"));
        Assert.Contains("        endpoint: 0.0.0.0:4317", config);
        Assert.Contains("        endpoint: 0.0.0.0:4318", config);
    }

    [Fact]
    public async Task CollectorConfig_ValidatesWithThePinnedCollectorImage()
    {
        // validate builds the sigv4auth extensions, which look up credentials then (never sending anything): placeholders
        // stand in for the instance role, so it never tries the instance metadata service.
        ProcessResult validate = await Processes.DockerAsync(TimeSpan.FromMinutes(5),
            "run", "--rm", "-e", $"AWS_REGION={TestValues["AWS_REGION"]}", "-e", $"OTEL_LOG_GROUP={TestValues["OTEL_LOG_GROUP"]}",
            "-e", "AWS_ACCESS_KEY_ID=placeholder", "-e", "AWS_SECRET_ACCESS_KEY=placeholder",
            "-v", $"{Path.Combine(SourceRoots.Repo, "infra/images/otel-collector/config.yaml")}:/etc/otelcol-contrib/config.yaml:ro",
            aws.Services["otel-collector"]!["image"]!.GetValue<string>(), "validate", "--config=/etc/otelcol-contrib/config.yaml");

        Assert.True(validate.ExitCode == 0, $"otelcol-contrib validate failed: {validate.Output}{validate.Errors}");
    }

    /// <summary>How often compose.yml references each input, by name.</summary>
    private static Dictionary<string, int> References() =>
        InputReference().Matches(OverrideSource)
            .GroupBy(match => match.Groups["name"].Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private string EnvironmentValue(string service, string name) => aws.Services[service]!["environment"]![name]!.GetValue<string>();

    /// <summary>The keys of a map in the model, in order; none when the model has no such map.</summary>
    private static string[] Keys(JsonNode? map) => map is null ? [] : [.. map.AsObject().Select(entry => entry.Key).Order(StringComparer.Ordinal)];

    private static IEnumerable<string> Strings(JsonNode array) => array.AsArray().Select(item => item!.GetValue<string>());

    /// <summary>A comment after a value: a space, then #.</summary>
    [GeneratedRegex(@"\s#.*$")]
    private static partial Regex TrailingComment();

    [GeneratedRegex(@"\$\{(?<name>[A-Z0-9_]+)")]
    private static partial Regex InputReference();

    [GeneratedRegex(@"\$\{[^}]*\}")]
    private static partial Regex AnyReference();

    [GeneratedRegex(@"^\$\{[A-Z0-9_]+:\?[^}]+\}$")]
    private static partial Regex RequiredReference();

    [GeneratedRegex(@"\$[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex UnbracedReference();

    [GeneratedRegex("dev-only|devonly", RegexOptions.IgnoreCase)]
    private static partial Regex DevOnlyValue();

    [GeneratedRegex(@"\bdebug\b")]
    private static partial Regex DebugWord();
}
