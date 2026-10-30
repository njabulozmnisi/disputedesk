using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.2 AC5, the code's bounds: each malformed body gets 400 <c>validation</c> naming no field but <c>code</c>, never
/// echoes the value, counts no attempt and writes no audit entry. One Api, and one login with a blank challenge, serve
/// the whole class.
/// </summary>
public sealed class VerifyOtpInputTests(VerifyOtpInputTests.LoginWithChallenge api) : IClassFixture<VerifyOtpInputTests.LoginWithChallenge>
{
    /// <summary>A JSON body whose <c>code</c> is a string, or null, or missing, that breaks the bounds.</summary>
    public static TheoryData<string> BodiesBreakingTheCode => new()
    {
        "{}",
        """{"code":null}""",
        """{"code":""}""",
        """{"code":"12345"}""",
        """{"code":"1234567"}""",
        """{"code":"12345a"}""",
        """{"code":" 12345"}""",
        """{"code":"123456 "}""",
        """{"code":"123456\n"}""",
        """{"code":"١٢٣٤٥٦"}""",
        JsonSerializer.Serialize(new { code = new string('1', 10_000) }),
    };

    [Theory]
    [MemberData(nameof(BodiesBreakingTheCode))]
    public async Task Verify_CodeBreakingItsBounds_Returns400NamingOnlyCodeWithoutTheValueAndCountsNothing(string json)
    {
        HttpResponseMessage response = await api.Browser.VerifyAsync(json);

        await response.AssertValidationProblemAsync("code");
        string? value = JsonDocument.Parse(json).RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
        if (value is { Length: >= 5 })
        {
            Assert.DoesNotContain(await StringsWithoutTraceIdAsync(response), text => text.Contains(value.Trim(), StringComparison.Ordinal));
        }

        await AssertNothingCountedAsync();
    }

    /// <summary>
    /// Bodies the framework cannot bind (AD-8): its 400 <c>validation</c> comes before any handler code and names no field,
    /// so the problem has no <c>errors</c>.
    /// </summary>
    public static TheoryData<string> BodiesThatDoNotBind => new()
    {
        """{"code":123456}""",
        """{"code":["123456"]}""",
        """{"code":"123456",""",
        "not json",
        "null",
        "",
    };

    [Theory]
    [MemberData(nameof(BodiesThatDoNotBind))]
    public async Task Verify_BodyThatDoesNotBind_Returns400ValidationNamingNoFieldAndCountsNothing(string json)
    {
        HttpResponseMessage response = await api.Browser.VerifyAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(CommonProblems.Validation), problem.RootElement.GetProperty("type").GetString());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        Assert.DoesNotContain(await StringsWithoutTraceIdAsync(response), text => text.Contains("123456", StringComparison.Ordinal));
        await AssertNothingCountedAsync();
    }

    [Fact]
    public async Task Verify_SixDigitsWithLeadingZeros_IsWellFormedAndCounts()
    {
        // Its own login, so the counted attempt leaves the class's login untouched.
        string login = LinkingApi.NewLogin("leading-zeros");
        using HttpClient browser = await api.Factory.SignInUnlinkedAsync(login);
        await browser.StartAcceptedAsync(LinkingFakes.NoMatch);
        await api.Factory.WaitForAuditAsync(login, AuditAction.LinkingNoMatch, AuditResult.Rejected);

        HttpResponseMessage response = await browser.VerifyCodeAsync("000123");

        await response.AssertOtpIncorrectAsync(attemptsRemaining: 2);
    }

    /// <summary>
    /// Every name and string value of the problem body, decoded, without the random <c>traceId</c>: the raw text would
    /// show an Arabic-Indic value JSON-escaped, so a search of it could never find the echo.
    /// </summary>
    private static async Task<List<string>> StringsWithoutTraceIdAsync(HttpResponseMessage response)
    {
        JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            ?? throw new InvalidOperationException("No body.");
        Assert.NotNull(body["traceId"]);
        body.AsObject().Remove("traceId");
        List<string> strings = [];
        Collect(body, strings);
        return strings;
    }

    private static void Collect(JsonNode? node, List<string> strings)
    {
        switch (node)
        {
            case JsonObject properties:
                foreach (KeyValuePair<string, JsonNode?> property in properties)
                {
                    strings.Add(property.Key);
                    Collect(property.Value, strings);
                }

                break;
            case JsonArray items:
                foreach (JsonNode? item in items)
                {
                    Collect(item, strings);
                }

                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                strings.Add(value.GetValue<string>());
                break;
        }
    }

    private async Task AssertNothingCountedAsync()
    {
        Assert.Equal(0, (await api.Factory.CurrentChallengeAsync(api.Login))?.Attempts);
        Assert.Empty(await api.Factory.VerifyAuditsAsync(api.Login));
    }

    /// <summary>One Api for the class, with one unlinked login holding a blank challenge.</summary>
    public sealed class LoginWithChallenge(PostgresFixture postgres) : IAsyncLifetime
    {
        private ApiFactory? _factory;
        private HttpClient? _browser;

        public string Login { get; } = LinkingApi.NewLogin("verify-input");

        public ApiFactory Factory => _factory ?? throw new InvalidOperationException("Not initialised.");

        public HttpClient Browser => _browser ?? throw new InvalidOperationException("Not initialised.");

        public async ValueTask InitializeAsync()
        {
            _factory = await LinkingApi.StartApiAsync(postgres, new LinkingFakes.Ports(), new TestClock());
            _browser = await _factory.SignInUnlinkedAsync(Login);
            await _browser.StartAcceptedAsync(LinkingFakes.NoMatch);
            await _factory.WaitForAuditAsync(Login, AuditAction.LinkingNoMatch, AuditResult.Rejected);
        }

        public async ValueTask DisposeAsync()
        {
            _browser?.Dispose();
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
            }
        }
    }
}
