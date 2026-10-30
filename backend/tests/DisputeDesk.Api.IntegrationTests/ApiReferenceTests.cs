using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DisputeDesk.Api.IntegrationTests.Infrastructure;

namespace DisputeDesk.Api.IntegrationTests;

/// <summary>
/// Scalar and the runtime OpenAPI document, behind <c>Scalar:Enabled</c> in every environment (AD-26, story 5.5). Every
/// host here runs as Production, so nothing depends on <c>IsDevelopment()</c>.
/// </summary>
public partial class ApiReferenceTests(PostgresFixture postgres)
{
    private static readonly string[] ServedPaths =
        ["/scalar/", "/scalar/scalar.js", "/scalar/scalar.aspnetcore.js", "/scalar/favicon.svg", "/openapi/v1.json"];

    private static readonly string ScalarHeadersTemplate =
        Path.Combine(RepoRoot.Repo, "infra", "nginx", "templates", "scalar-headers.conf.template");

    // null: not overridden: appsettings.json's false.
    public static TheoryData<string?, string> SwitchOnPaths() => PathsFor("true", "TRUE");

    public static TheoryData<string?, string> SwitchOffPaths() => PathsFor("false", null);

    [Theory]
    [MemberData(nameof(SwitchOnPaths))]
    public async Task ApiReference_SwitchOn_IsServedAnonymously(string? enabled, string path)
    {
        await using ApiFactory factory = Production(enabled);
        using HttpClient anonymous = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage response = await anonymous.GetPathAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    public async Task ScalarWithoutSlash_SwitchOn_RedirectsToThePage(string enabled)
    {
        await using ApiFactory factory = Production(enabled);
        using HttpClient anonymous = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage response = await anonymous.GetPathAsync("/scalar");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("scalar/", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [MemberData(nameof(SwitchOffPaths))]
    public async Task ApiReference_SwitchOffSignedIn_Returns404(string? enabled, string path)
    {
        // Signed in: an anonymous request that matches no endpoint gets the fallback policy's 401 instead (2.8 AC9).
        await using ApiFactory factory = Production(enabled);
        using HttpClient client = factory.CreateBrowser(new CookieContainer());
        await client.SignInAsync(factory.Zitadel, $"api-reference-{Guid.NewGuid():N}");

        HttpResponseMessage response = await client.GetPathAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(SwitchOffPaths))]
    public async Task ApiReference_SwitchOffAnonymous_Returns401(string? enabled, string path)
    {
        // No endpoint matches, so the authorization middleware applies the fallback policy: 401 (2.8 AC9).
        await using ApiFactory factory = Production(enabled);
        using HttpClient anonymous = factory.CreateBrowser(new CookieContainer());

        HttpResponseMessage response = await anonymous.GetPathAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Start_SwitchNotABoolean_Fails()
    {
        await using ApiFactory factory = Production("yes");

        Exception failure = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("Scalar:Enabled", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PageAndDocument_TwoHostsWithDifferentSettings_AreIdenticalAndHoldNoConfiguredValue()
    {
        var other = new Dictionary<string, string?>(ApiFactory.ApiReferenceOn)
        {
            ["Zitadel:Issuer"] = "https://issuer-other.invalid",
            ["Zitadel:ProjectId"] = "project-other",
            ["Operations:Actors:ops-tool-other"] = "operations",
        };
        await using ApiFactory first = Production("true");
        await using var second = new ApiFactory(postgres.ConnectionString, "Production", settings: other);

        string firstPage = await GetTextAsync(first, "/scalar/");
        string secondPage = await GetTextAsync(second, "/scalar/");
        string firstDocument = await GetTextAsync(first, "/openapi/v1.json");
        string secondDocument = await GetTextAsync(second, "/openapi/v1.json");

        Assert.Equal(firstPage, secondPage);
        string[] configured =
        [
            FakeZitadel.Issuer, FakeZitadel.ProjectId, FakeZitadel.OperationsSub, FakeZitadel.ClientId, FakeZitadel.ClientSecret,
            "issuer-other.invalid", "project-other", "ops-tool-other", "clientId", "clientSecret",
        ];
        foreach (string served in new[] { firstPage, firstDocument, secondDocument })
        {
            Assert.All(configured, value => Assert.DoesNotContain(value, served, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Page_InitializeConfiguration_TurnsOffFontsTelemetryAgentMcpAndHoldsNoCredential()
    {
        await using ApiFactory factory = Production("true");
        string script = SingleInlineScript(await GetTextAsync(factory, "/scalar/"));

        // scalar.aspnetcore.js is called as initialize('<encoded path>', <bool>, {configuration}, '<...>').
        Match call = InitializeCall().Match(script);
        Assert.True(call.Success, "No initialize call in the inline script:\n" + script);
        using var configuration = JsonDocument.Parse(call.Groups["configuration"].Value);
        JsonElement root = configuration.RootElement;

        Assert.False(root.GetProperty("withDefaultFonts").GetBoolean());
        Assert.False(root.GetProperty("telemetry").GetBoolean());
        Assert.True(root.GetProperty("agent").GetProperty("disabled").GetBoolean());
        Assert.True(root.GetProperty("mcp").GetProperty("disabled").GetBoolean());
        Assert.Equal("never", root.GetProperty("showDeveloperTools").GetString());
        Assert.False(root.TryGetProperty("persistAuth", out JsonElement persistAuth) && persistAuth.GetBoolean());
        Assert.False(root.TryGetProperty("proxyUrl", out _));
        Assert.False(root.TryGetProperty("authentication", out _));
    }

    [Fact]
    public async Task Page_InlineScript_MatchesTheOneHashInTheScalarCsp()
    {
        // The inline script embeds the request path, so the hash holds for exactly /scalar/ (AD-26).
        await using ApiFactory factory = Production("true");
        string script = SingleInlineScript(await GetTextAsync(factory, "/scalar/"));
        string hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));

        Dictionary<string, string[]> csp = TemplateCsp();

        // CSP Level 3 hash-source: one hash and never 'unsafe-inline' in script-src; 'unsafe-inline' and never a hash in style-src.
        string expected = $"'self' 'sha256-{hash}'";
        Assert.True(
            string.Join(' ', csp["script-src"]) == expected,
            $"scalar-headers.conf.template's script-src must be exactly: {expected}");
        Assert.Equal(["'self'", "'unsafe-inline'"], csp["style-src"]);
        Assert.Single(csp["script-src"], source => source.StartsWith("'sha256-", StringComparison.Ordinal));
        Assert.DoesNotContain("'unsafe-inline'", csp["script-src"]);
        Assert.DoesNotContain(csp["style-src"], source => source.StartsWith("'sha256-", StringComparison.Ordinal));
    }

    private ApiFactory Production(string? enabled) =>
        new(postgres.ConnectionString, "Production", settings: enabled is null ? null : new Dictionary<string, string?> { ["Scalar:Enabled"] = enabled });

    private static TheoryData<string?, string> PathsFor(params string?[] values)
    {
        var data = new TheoryData<string?, string>();
        foreach (string? value in values)
        {
            foreach (string path in ServedPaths)
            {
                data.Add(value, path);
            }
        }

        return data;
    }

    private static async Task<string> GetTextAsync(ApiFactory factory, string path)
    {
        using HttpClient anonymous = factory.CreateBrowser(new CookieContainer());
        HttpResponseMessage response = await anonymous.GetPathAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static string SingleInlineScript(string html)
    {
        string[] inline = [.. ScriptElement().Matches(html)
            .Where(script => !script.Groups["attributes"].Value.Contains("src=", StringComparison.OrdinalIgnoreCase))
            .Select(script => script.Groups["content"].Value)];
        return Assert.Single(inline);
    }

    /// <summary>The Content-Security-Policy in the template, as its directives' source lists.</summary>
    private static Dictionary<string, string[]> TemplateCsp()
    {
        string header = Assert.Single(File.ReadAllLines(ScalarHeadersTemplate), line => line.StartsWith("add_header Content-Security-Policy ", StringComparison.Ordinal));
        string policy = header.Split('"')[1];
        return policy.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(directive => directive.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(parts => parts[0], parts => parts[1..]);
    }

    [GeneratedRegex(@"<script(?<attributes>[^>]*)>(?<content>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptElement();

    [GeneratedRegex(@"initialize\(\s*'[^']*',\s*\w+,\s*(?<configuration>\{.*\}),\s*'[^']*'\s*\)", RegexOptions.Singleline)]
    private static partial Regex InitializeCall();
}
