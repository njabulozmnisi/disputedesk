using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Problems;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>
/// 6.1 AC2, the input table: each body that breaks it gets 400 <c>validation</c> naming only the failing field, never
/// echoing the value, and writes no challenge, no audit entry and no job. One Api serves the whole class.
/// </summary>
public sealed class StartLinkingInputTests(StartLinkingInputTests.UnlinkedLogin api) : IClassFixture<StartLinkingInputTests.UnlinkedLogin>
{
    private const string ValidId = "8804125123080";
    private const string ValidAccount = "1000000041";

    public static TheoryData<string?, string?, string> BodiesBreakingOneField => new()
    {
        // idNumber: missing (null leaves it out), JSON null, empty, a letter, a space, one short, one long, oversized,
        // and a wrong check digit.
        { null, ValidAccount, "idNumber" },
        { "null", ValidAccount, "idNumber" },
        { "", ValidAccount, "idNumber" },
        { "880412512308A", ValidAccount, "idNumber" },
        { "880412 123080", ValidAccount, "idNumber" },
        { "880412512308", ValidAccount, "idNumber" },
        { "88041251230800", ValidAccount, "idNumber" },
        { new string('8', 10_000), ValidAccount, "idNumber" },
        { "8804125123081", ValidAccount, "idNumber" },
        // Leading or trailing space, and digits of another script: not trimmed or normalised.
        { " 8804125123080", ValidAccount, "idNumber" },
        { "8804125123080\n", ValidAccount, "idNumber" },
        { "٨٨٠٤١٢٥١٢٣٠٨٠", ValidAccount, "idNumber" },
        // accountNumber: missing, JSON null, empty, a letter, a space, one short, one long, oversized.
        { ValidId, null, "accountNumber" },
        { ValidId, "null", "accountNumber" },
        { ValidId, "", "accountNumber" },
        { ValidId, "100000004A", "accountNumber" },
        { ValidId, "10000 0041", "accountNumber" },
        { ValidId, "10000", "accountNumber" },
        { ValidId, "10000000410000000", "accountNumber" },
        { ValidId, new string('1', 10_000), "accountNumber" },
    };

    [Theory]
    [MemberData(nameof(BodiesBreakingOneField))]
    public async Task Start_BodyBreakingOneField_Returns400NamingOnlyThatFieldWithoutTheValueAndWritesNothing(string? idNumber, string? accountNumber, string field)
    {
        HttpResponseMessage response = await api.Browser.StartLinkingAsync(Body(idNumber, accountNumber));

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        await response.AssertValidationProblemAsync(field);
        string? value = field == "idNumber" ? idNumber : accountNumber;
        if (value is { Length: > 5 } and not "null")
        {
            Assert.DoesNotContain(value, body, StringComparison.Ordinal);
        }

        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task Start_BothFieldsBroken_Returns400NamingBoth()
    {
        HttpResponseMessage response = await api.Browser.StartLinkingAsync(Body("1", "2"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(
            ["accountNumber", "idNumber"],
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).Order(StringComparer.Ordinal));
        await AssertNothingWrittenAsync();
    }

    public static TheoryData<string> BodiesThatDoNotBind => new()
    {
        """{"idNumber":8804125123080,"accountNumber":"1000000041"}""",
        """{"idNumber":"8804125123080","accountNumber":1000000041}""",
        """{"idNumber":["8804125123080"],"accountNumber":"1000000041"}""",
        """{"idNumber":"8804125123080",""",
        "not json",
        "null",
        "[]",
        "",
    };

    [Theory]
    [MemberData(nameof(BodiesThatDoNotBind))]
    public async Task Start_BodyThatDoesNotBind_Returns400ValidationAsTheRaiseDoesAndWritesNothing(string json)
    {
        HttpResponseMessage response = await api.Browser.StartLinkingAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Validation), await response.ProblemTypeAsync());
        await AssertNothingWrittenAsync();
    }

    private async Task AssertNothingWrittenAsync()
    {
        Assert.Empty(await api.Factory.ChallengesAsync(api.Login));
        Assert.Empty(await api.Factory.LinkingAuditsAsync(api.Login));
        Assert.Equal(0, api.Ports.CoreBanking.Calls);
    }

    /// <summary>The body with each field as a JSON string, left out when null; the text "null" is written as JSON null.</summary>
    private static string Body(string? idNumber, string? accountNumber)
    {
        var body = new Dictionary<string, string?>();
        if (idNumber is not null)
        {
            body["idNumber"] = idNumber == "null" ? null : idNumber;
        }

        if (accountNumber is not null)
        {
            body["accountNumber"] = accountNumber == "null" ? null : accountNumber;
        }

        return JsonSerializer.Serialize(body);
    }

    /// <summary>One Api for the class, with one unlinked login signed in.</summary>
    public sealed class UnlinkedLogin(PostgresFixture postgres) : IAsyncLifetime
    {
        private ApiFactory? _factory;
        private HttpClient? _browser;

        public LinkingFakes.Ports Ports { get; } = new();

        public string Login { get; } = LinkingApi.NewLogin("input");

        public ApiFactory Factory => _factory ?? throw new InvalidOperationException("Not initialised.");

        public HttpClient Browser => _browser ?? throw new InvalidOperationException("Not initialised.");

        public async ValueTask InitializeAsync()
        {
            _factory = await LinkingApi.StartApiAsync(postgres, Ports, new TestClock());
            _browser = await _factory.SignInUnlinkedAsync(Login);
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
