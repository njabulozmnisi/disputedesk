using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DisputeDesk.ArchitectureTests;

/// <summary>AC11 and AC12: what the Zitadel provisioner creates and the headless sign-in script, read from the files.</summary>
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

    [GeneratedRegex(@"ROLES='(?<roles>[^']*)'")]
    private static partial Regex RolesBlock();

    [GeneratedRegex(@"Bearer [A-Za-z0-9_\-\.]{20,}")]
    private static partial Regex LiteralBearerToken();
}
