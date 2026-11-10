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

    /// <summary>The inputs that are secrets (input-bounds.sh generates each from [A-Za-z0-9]).</summary>
    private static readonly string[] SecretInputs =
        ["APP_DB_PASSWORD", "ZITADEL_DB_PASSWORD", "ZITADEL_MASTERKEY", "ZITADEL_ADMIN_PASSWORD", "LINKING_OTP_HMAC_KEY", "MAILPIT_UI_AUTH", "GATEWAY_ORIGIN_VERIFY"];

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
            ["ZITADEL_MASTERKEY"] = 1,
            ["ZITADEL_ADMIN_PASSWORD"] = 1,
            ["LINKING_OTP_HMAC_KEY"] = 1,
            ["MAILPIT_UI_AUTH"] = 1,
            ["GATEWAY_ORIGIN_VERIFY"] = 1,
            ["APP_HOST"] = 3,
            ["AUTH_HOST"] = 5,
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
        ProcessResult render;
        try
        {
            await File.WriteAllLinesAsync(envFile, TestValues.Where(input => input.Key != name).Select(input => $"{input.Key}={input.Value}"), TestContext.Current.CancellationToken);

            render = await Processes.RunAsync(
                "docker", ["compose", "-f", AwsComposeModel.Files[0], "-f", AwsComposeModel.Files[1], "--env-file", envFile, "config", "--quiet"],
                Processes.BaseEnvironment(), CommandTimeout);
        }
        finally
        {
            File.Delete(envFile);
        }

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
    public async Task LocalAndAws_RunTheSameServices_ExceptTheListedOnes()
    {
        // The listed exceptions (compose.yml's header): RDS and the collector stand in for postgres and the dashboard in
        // AWS, and the worker runs inside the api locally.
        string[] localOnly = ["aspire-dashboard", "postgres"];
        string[] awsOnly = ["otel-collector", "worker"];
        JsonNode local = (await ComposeModel.RenderAsync(["docker-compose.yml"]))["services"]!;

        string[] localServices = [.. local.AsObject().Select(service => service.Key).Except(localOnly).Order(StringComparer.Ordinal)];
        string[] awsServices = [.. aws.Services.AsObject().Select(service => service.Key).Except(awsOnly).Order(StringComparer.Ordinal)];

        Assert.Equal(localServices, awsServices);
        Assert.All(localOnly, service => Assert.True(local.AsObject().ContainsKey(service), $"{service} is not a local service."));
        Assert.All(awsOnly, service => Assert.True(aws.Services.AsObject().ContainsKey(service), $"{service} is not an AWS service."));
    }

    [Fact]
    public async Task Worker_RunsInsideTheApiLocally_AndInItsOwnContainerInAws()
    {
        JsonNode local = (await ComposeModel.RenderAsync(["docker-compose.yml"]))["services"]!;

        Assert.False(local.AsObject().ContainsKey("worker"));
        Assert.Equal("true", local["api"]!["environment"]!["Worker__InProcess"]!.GetValue<string>());
        Assert.Equal("redpanda:9092", local["api"]!["environment"]!["Kafka__BootstrapServers"]!.GetValue<string>());
        Assert.Null(aws.Services["api"]!["environment"]!["Worker__InProcess"]);
        Assert.Equal("redpanda:9092", EnvironmentValue("worker", "Kafka__BootstrapServers"));
    }

    public static TheoryData<string> Renders() => ["local", "aws"];

    [Theory]
    [MemberData(nameof(Renders))]
    public async Task Zitadel_ProvisionsInItsPostStartHookAsItsOwnUser_AndTheApiWaitsForTheMarker(string render)
    {
        // Compose, "Lifecycle hooks": the hook runs inside the container, as the hook's user; dependents wait for it.
        JsonNode services = render == "aws" ? aws.Services : (await ComposeModel.RenderAsync(["docker-compose.yml"]))["services"]!;
        JsonNode zitadel = services["zitadel"]!;
        JsonNode hook = Assert.Single(zitadel["post_start"]!.AsArray())!;
        string marker = zitadel["environment"]!["PROVISION_MARKER_FILE"]!.GetValue<string>();

        Assert.Equal(["bash", "/provision/provision.sh"], Strings(hook["command"]!));
        Assert.Equal("zitadel", hook["user"]!.GetValue<string>());
        Assert.False(zitadel.AsObject().ContainsKey("user"), "zitadel overrides its image's non-root user.");
        Assert.Equal(["CMD-SHELL", $"test -f {marker} && zitadel ready"], Strings(zitadel["healthcheck"]!["test"]!));
        Assert.StartsWith("/provision/state/", marker, StringComparison.Ordinal);
        Assert.Equal("service_healthy", services["api"]!["depends_on"]!["zitadel"]!["condition"]!.GetValue<string>());
        Assert.Equal(["1000"], Strings(services["api"]!["group_add"]!));
    }

    [Theory]
    [MemberData(nameof(Renders))]
    public async Task Zitadel_ProvisionerSettings_CallItselfWithThePublicHost_AndHoldNoSecret(string render)
    {
        // Zitadel binds ZITADEL_* variables to its own configuration, so the provisioner's are PROVISION_*; none holds a
        // secret, because the hook shares Zitadel's environment.
        JsonNode services = render == "aws" ? aws.Services : (await ComposeModel.RenderAsync(["docker-compose.yml"]))["services"]!;
        JsonObject environment = services["zitadel"]!["environment"]!.AsObject();
        string[] secrets = [.. SecretInputs.Select(name => TestValues[name])];
        string authHost = render == "aws" ? TestValues["AUTH_HOST"] : "auth.localhost:8080";
        string port = render == "aws" ? "80" : "8080";

        string[] provisionValues = [.. environment.Where(variable => variable.Key.StartsWith("PROVISION_", StringComparison.Ordinal))
            .Select(variable => variable.Value!.GetValue<string>())];
        Assert.Equal(9, provisionValues.Length);
        Assert.DoesNotContain(provisionValues, value => secrets.Any(secret => value.Contains(secret, StringComparison.Ordinal)));
        Assert.DoesNotContain(provisionValues, value => DevOnlyValue().IsMatch(value));
        Assert.Equal($"http://{authHost}", environment["PROVISION_CALL_URL"]!.GetValue<string>());
        Assert.Equal($"{authHost.Split(':')[0]}:{port}:127.0.0.1:8080", environment["PROVISION_CONNECT_TO"]!.GetValue<string>());
        Assert.Equal(render == "aws" ? "false" : "true", environment["PROVISION_AUTH_DEVELOPMENT_MODE"]!.GetValue<string>());
        Assert.Equal("false", environment["ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_REQUIRED"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(Renders))]
    public async Task Zitadel_HumanAdministrators_AreDeactivatedInAwsOnly(string render)
    {
        // aws-admin-lockdown: in AWS the Mailpit inbox, where Zitadel sends password-reset emails, may be public, so the
        // provisioner deactivates every human administrator there; locally the console admin stays (infra/README.md).
        JsonNode services = render == "aws" ? aws.Services : (await ComposeModel.RenderAsync(["docker-compose.yml"]))["services"]!;

        Assert.Equal(render == "aws" ? "true" : "false", services["zitadel"]!["environment"]!["PROVISION_DEACTIVATE_HUMAN_ADMINS"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(Renders))]
    public async Task Zitadel_PublicOrganizationRegistration_IsClosedOnANewInstance(string render)
    {
        // aws-admin-lockdown H1: Zitadel's DefaultInstance restriction (cmd/defaults.yaml, v4.20.0) closes
        // /ui/login/register/org when the instance is created; provision.sh closes it on an existing one.
        JsonNode services = render == "aws" ? aws.Services : (await ComposeModel.RenderAsync(["docker-compose.yml"]))["services"]!;

        Assert.Equal("true", services["zitadel"]!["environment"]!["ZITADEL_DEFAULTINSTANCE_RESTRICTIONS_DISALLOWPUBLICORGREGISTRATION"]?.GetValue<string>());
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
    /// keys back in (Compose, "Merge Compose files"). The worker, which only compose.yml defines, is listed too.
    /// </summary>
    public static TheoryData<string, string[], string[]> OverriddenServices() => new()
    {
        {
            "zitadel",
            [
                "GOMEMLIMIT", "PROVISION_APP_ORIGINS", "PROVISION_AUTH_DEVELOPMENT_MODE", "PROVISION_CALL_URL", "PROVISION_CONNECT_TO",
                "PROVISION_DEACTIVATE_HUMAN_ADMINS", "PROVISION_FIXTURES_FILE", "PROVISION_MARKER_FILE", "PROVISION_OUT_DIR", "PROVISION_PAT_FILE",
                "ZITADEL_DATABASE_POSTGRES_DSN", "ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_REQUIRED",
                "ZITADEL_DEFAULTINSTANCE_RESTRICTIONS_DISALLOWPUBLICORGREGISTRATION", "ZITADEL_DEFAULTINSTANCE_SMTPCONFIGURATION_FROM", "ZITADEL_DEFAULTINSTANCE_SMTPCONFIGURATION_FROMNAME", "ZITADEL_DEFAULTINSTANCE_SMTPCONFIGURATION_SMTP_HOST",
                "ZITADEL_DEFAULTINSTANCE_SMTPCONFIGURATION_TLS", "ZITADEL_EXTERNALDOMAIN", "ZITADEL_EXTERNALPORT", "ZITADEL_EXTERNALSECURE", "ZITADEL_FIRSTINSTANCE_INSTANCENAME",
                "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_EMAIL_ADDRESS", "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_EMAIL_VERIFIED",
                "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_FIRSTNAME", "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_LASTNAME", "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_PASSWORD",
                "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_PASSWORDCHANGEREQUIRED", "ZITADEL_FIRSTINSTANCE_ORG_HUMAN_USERNAME", "ZITADEL_FIRSTINSTANCE_ORG_MACHINE_MACHINE_NAME",
                "ZITADEL_FIRSTINSTANCE_ORG_MACHINE_MACHINE_USERNAME", "ZITADEL_FIRSTINSTANCE_ORG_MACHINE_PAT_EXPIRATIONDATE", "ZITADEL_FIRSTINSTANCE_ORG_NAME",
                "ZITADEL_FIRSTINSTANCE_PATPATH", "ZITADEL_MASTERKEY", "ZITADEL_PORT", "ZITADEL_TLS_ENABLED",
            ],
            []
        },
        { "mailpit", ["MP_SMTP_AUTH_ACCEPT_ANY", "MP_SMTP_AUTH_ALLOW_INSECURE", "MP_UI_AUTH"], [] },
        {
            "api",
            [
                "ASPNETCORE_ENVIRONMENT", "AWS_REGION", "ConnectionStrings__DisputeDesk", "CoreBanking__BaseUrl", "DataProtection__KmsKeyId", "DataProtection__SsmParameterPath",
                "ForwardedHeaders__AllowedHosts", "ForwardedHeaders__TrustedNetwork", "Linking__OtpHmacKey", "OTEL_EXPORTER_OTLP_ENDPOINT", "OTEL_EXPORTER_OTLP_PROTOCOL",
                "OTEL_SERVICE_NAME", "Scalar__Enabled", "Seed__Enabled", "Seed__FixturesPath", "Sms__Host", "Sms__Port", "Sms__UseTls",
            ],
            ["core-banking", "zitadel"]
        },
        {
            "worker",
            ["ConnectionStrings__DisputeDesk", "DOTNET_ENVIRONMENT", "Kafka__BootstrapServers", "OTEL_EXPORTER_OTLP_ENDPOINT", "OTEL_EXPORTER_OTLP_PROTOCOL", "OTEL_SERVICE_NAME"],
            ["api", "redpanda"]
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
        Assert.Equal("false", EnvironmentValue("zitadel", "PROVISION_AUTH_DEVELOPMENT_MODE"));
        // The one plain-http public URL: the provisioner's call, which goes to its own container's 127.0.0.1:8080
        // (Zitadel_ProvisionerSettings_CallItselfWithThePublicHost_AndHoldNoSecret), never over the network.
        string provisionCall = $"\"PROVISION_CALL_URL\":\"http://{TestValues["AUTH_HOST"]}\"";
        Assert.Contains(provisionCall, render, StringComparison.Ordinal);
        string rest = render.Replace(provisionCall, "", StringComparison.Ordinal);
        foreach (string name in new[] { "APP_HOST", "AUTH_HOST", "MAILPIT_HOST" })
        {
            Assert.DoesNotContain($"http://{TestValues[name]}", rest, StringComparison.Ordinal);
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
    [InlineData("zitadel", "ZITADEL_IMAGE")]
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
        // Every service runs for as long as the stack does; their mem_limit total stays within 3482 MiB (3.4 GiB).
        const long BudgetMib = 3482;
        string[] longRunning = [.. aws.Services.AsObject().Select(service => service.Key)];

        Assert.Contains("--memory=512M", Strings(aws.Services["redpanda"]!["command"]!));
        Assert.All(longRunning, service => Assert.NotNull(aws.Services[service]!["mem_limit"]));
        long totalMib = longRunning.Sum(service => long.Parse(aws.Services[service]!["mem_limit"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture) / 1048576);
        Assert.True(totalMib <= BudgetMib, $"The long-running services' mem_limit total {totalMib} MiB, over {BudgetMib}.");
    }

    [Fact]
    public void AwsServices_SetTheirOwnMemoryLimits_NoneFromTheLocalFile()
    {
        // The figures in infra/deploy/aws/compose.yml's header; the local limits are sized for a laptop and differ.
        Dictionary<string, long> expectedMib = new()
        {
            ["redpanda"] = 768,
            ["zitadel"] = 704,
            ["mailpit"] = 128,
            ["core-banking"] = 192,
            ["api"] = 512,
            ["worker"] = 320,
            ["otel-collector"] = 128,
            ["gateway"] = 64,
        };

        Assert.Equal(
            expectedMib.OrderBy(service => service.Key),
            aws.Services.AsObject().ToDictionary(
                service => service.Key,
                service => long.Parse(service.Value!["mem_limit"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture) / 1048576)
                .OrderBy(service => service.Key));
    }

    // ---- criterion 5: the collector config

    [Fact]
    public void CollectorConfig_HasNoDebugExporterAndLimitsMemoryFirstInEveryPipeline()
    {
        // Text: the pipelines' processor order and listen addresses are the config's contract; validate checks the rest.
        string[] config = File.ReadAllLines(Path.Combine(SourceRoots.Repo, "infra/images/otel-collector/config.yaml"));

        Assert.DoesNotContain(config, line => DebugWord().IsMatch(line));
        Assert.Equal(3, config.Count(line => line.StartsWith("      processors: ", StringComparison.Ordinal)));
        Assert.Equal(2, config.Count(line => line == "      processors: [memory_limiter, batch]"));
        Assert.Contains("      processors: [memory_limiter, filter/app-metrics, batch]", config);
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

    [Fact]
    public async Task CollectorConfig_ExportsOnlyTheAppsMetricsEachWithItsOwnAttributesAsItsOneDimensionSet()
    {
        // cloudwatch-metric-filter: the pinned collector runs this config with one change, awsemf printing its EMF records
        // to stdout instead of sending them (awsemf README, output_destination), and gets two of the app's counters and
        // one ASP.NET Core counter twice, as the Api's periodic export sends them. Stopping the container flushes the
        // batch processor, so its stdout then holds every record the requests produced.
        string container = $"disputedesk-collector-test-{Environment.ProcessId}";
        try
        {
            ProcessResult run = await Processes.DockerAsync(CommandTimeout,
                "run", "-d", "--name", container, "-p", "127.0.0.1::4318",
                "-e", $"AWS_REGION={TestValues["AWS_REGION"]}", "-e", $"OTEL_LOG_GROUP={TestValues["OTEL_LOG_GROUP"]}",
                "-e", "AWS_ACCESS_KEY_ID=placeholder", "-e", "AWS_SECRET_ACCESS_KEY=placeholder",
                "-v", $"{Path.Combine(SourceRoots.Repo, "infra/images/otel-collector/config.yaml")}:/etc/otelcol-contrib/config.yaml:ro",
                aws.Services["otel-collector"]!["image"]!.GetValue<string>(),
                "--config=/etc/otelcol-contrib/config.yaml", "--set=exporters::awsemf::output_destination=stdout");
            Assert.True(run.ExitCode == 0, $"The collector did not start: {run.Errors}");
            await WaitForCollectorLogAsync(container, "Everything is ready");

            ProcessResult port = await Processes.DockerAsync(CommandTimeout, "port", container, "4318/tcp");
            var endpoint = new Uri($"http://{port.Output.Trim()}/v1/metrics");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            long start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // awsemf turns a cumulative sum into the change since its last point, so the first point is only the baseline.
            foreach (int count in new[] { 1, 2 })
            {
                using var request = new StringContent(CollectorTestMetrics(start, count), System.Text.Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await client.PostAsync(endpoint, request, TestContext.Current.CancellationToken);
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            }

            await Processes.DockerAsync(CommandTimeout, "stop", container);
            ProcessResult logs = await Processes.DockerAsync(CommandTimeout, "logs", container);
            // Dropped before the exporter, so not even written to the log group as a record without a CloudWatch metric,
            // which is what awsemf's metric_declarations would do with it.
            Assert.DoesNotContain("aspnetcore.routing.match_attempts", logs.Output, StringComparison.Ordinal);

            // Each exported metric with each of its dimension sets. Resource attributes (service.name) are not
            // dimensions; OTelLib is the label awsemf adds for the instrumentation scope, one value (the Meter's name).
            var exported = new List<string>();
            foreach (string record in logs.Output.Split('\n').Where(line => line.StartsWith('{')))
            {
                foreach (JsonNode? measurement in JsonNode.Parse(record)!["_aws"]!["CloudWatchMetrics"]!.AsArray())
                {
                    string[] dimensionSets = [.. measurement!["Dimensions"]!.AsArray()
                        .Select(set => string.Join(',', Strings(set!).Order(StringComparer.Ordinal)))];
                    exported.AddRange(measurement["Metrics"]!.AsArray()
                        .SelectMany(metric => dimensionSets.Select(set => $"{metric!["Name"]}: {set}")));
                }
            }

            Assert.Equal(
                ["disputedesk.dispute_actions: OTelLib,action,actor_type", "disputedesk.disputes.raised: OTelLib,reason"],
                exported.Order(StringComparer.Ordinal));
        }
        finally
        {
            await Processes.DockerAsync(CommandTimeout, "rm", "-f", container);
        }
    }

    /// <summary>Waits up to a minute for the text in the container's log.</summary>
    private static async Task WaitForCollectorLogAsync(string container, string text)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            ProcessResult logs = await Processes.DockerAsync(CommandTimeout, "logs", container);
            if (logs.Output.Contains(text, StringComparison.Ordinal) || logs.Errors.Contains(text, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }

        ProcessResult last = await Processes.DockerAsync(CommandTimeout, "logs", container);
        Assert.Fail($"The collector did not log \"{text}\" within a minute: {last.Output}{last.Errors}");
    }

    /// <summary>
    /// One OTLP/JSON metrics request (OTLP specification, JSON Protobuf encoding) as the Api sends it: the DisputeDesk
    /// Meter's counters and an ASP.NET Core one, cumulative (the OpenTelemetry .NET SDK's default), each at the given
    /// count, a second after the start per count.
    /// </summary>
    private static string CollectorTestMetrics(long startMilliseconds, int count)
    {
        string start = (startMilliseconds * 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string time = ((startMilliseconds + (count * 1000)) * 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string Sum(string name, string attributes) => $$"""
            {"name": "{{name}}", "sum": {"aggregationTemporality": 2, "isMonotonic": true, "dataPoints": [{"asInt": "{{count}}",
              "startTimeUnixNano": "{{start}}", "timeUnixNano": "{{time}}", "attributes": [{{attributes}}]}]} }
            """;
        string Attribute(string key, string value) => $$"""{"key": "{{key}}", "value": {"stringValue": "{{value}}"} }""";

        return $$"""
            {"resourceMetrics": [{
              "resource": {"attributes": [{{Attribute("service.name", "disputedesk-api")}}]},
              "scopeMetrics": [
                {"scope": {"name": "DisputeDesk"}, "metrics": [
                  {{Sum("disputedesk.disputes.raised", Attribute("reason", "duplicateCharge"))}},
                  {{Sum("disputedesk.dispute_actions", $"{Attribute("action", "withdraw")}, {Attribute("actor_type", "customer")}")}}]},
                {"scope": {"name": "Microsoft.AspNetCore.Routing"}, "metrics": [
                  {{Sum("aspnetcore.routing.match_attempts", Attribute("aspnetcore.routing.match_status", "success"))}}]}]}]}
            """;
    }

    // ---- ci-e2e-regression criterion 7: the list that starts the regression suite covers every stack input

    [Fact]
    public async Task RegressionPathsList_CoversEveryStackInputAndTheJobsOwnFiles()
    {
        string[] lines = await File.ReadAllLinesAsync(Path.Combine(SourceRoots.Repo, "scripts/regression-paths.list"), TestContext.Current.CancellationToken);
        var profiles = new Dictionary<string, string> { ["COMPOSE_PROFILES"] = "build,console" };
        JsonNode model = await ComposeModel.RenderAsync(["docker-compose.yml"], variables: profiles);

        string[] uncovered = [.. StackSources(model).Where(source => !lines.Any(line => LineMatches(line, source)))];
        string[] jobFiles =
            ["docker-compose.yml", "e2e/", ".nvmrc", ".github/workflows/ci.yml", ".github/actions/install-compose/", "scripts/regression-paths.sh", "scripts/regression-paths.list"];

        Assert.True(uncovered.Length == 0, $"No line of scripts/regression-paths.list matches: {string.Join(", ", uncovered)}");
        Assert.Empty(jobFiles.Except(lines, StringComparer.Ordinal));
    }

    /// <summary>Every build context, path-valued additional context and bind-mount source, relative to the repository root.</summary>
    private static IEnumerable<string> StackSources(JsonNode model)
    {
        foreach ((string _, JsonNode? service) in model["services"]!.AsObject())
        {
            if (service!["build"] is { } build)
            {
                yield return Path.GetRelativePath(SourceRoots.Repo, build["context"]!.GetValue<string>());
                foreach ((string _, JsonNode? context) in build["additional_contexts"]?.AsObject() ?? [])
                {
                    string value = context!.GetValue<string>();
                    if (!value.StartsWith("service:", StringComparison.Ordinal))
                    {
                        yield return Path.GetRelativePath(SourceRoots.Repo, value);
                    }
                }
            }

            foreach (JsonNode? volume in service["volumes"]?.AsArray() ?? [])
            {
                if (volume!["type"]!.GetValue<string>() == "bind")
                {
                    yield return Path.GetRelativePath(SourceRoots.Repo, volume["source"]!.GetValue<string>());
                }
            }
        }
    }

    /// <summary>The list's rule: a line ending in / matches that directory and every path under it, any other line exactly that path.</summary>
    private static bool LineMatches(string line, string source) =>
        line.EndsWith('/') ? $"{source}/".StartsWith(line, StringComparison.Ordinal) : source == line;

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
