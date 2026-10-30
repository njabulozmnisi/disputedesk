using System.Net;
using System.Text;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Disputes.Idempotency;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AC2 and AC4, the input table: each body that breaks it gets 400 <c>validation</c> before the transaction is looked
/// up. Core banking here is a fake that fails and counts requests, so "before the lookup" is proved by the count not
/// moving. One Api serves the class, and no test on it writes a row; the reason-case tests raise on an Api of their own.
/// </summary>
public sealed class RaiseDisputeInputTests(PostgresFixture postgres, RaiseDisputeInputTests.LinkedCustomerA api)
    : IClassFixture<RaiseDisputeInputTests.LinkedCustomerA>
{
    private static readonly string Id33 = new('A', 33);

    private FakeCoreBankingServer CoreBanking => api.CoreBanking;

    private ApiFactory Factory => api.Factory;

    private HttpClient Browser => api.Browser;

    public static TheoryData<string, string> BodiesBreakingAFieldRule => new()
    {
        // accountRef: missing, null, empty, whitespace, oversized
        { """{"transactionId":"TXN-A-0029","reason":"unrecognised"}""", "accountRef" },
        { """{"accountRef":null,"transactionId":"TXN-A-0029","reason":"unrecognised"}""", "accountRef" },
        { """{"accountRef":"","transactionId":"TXN-A-0029","reason":"unrecognised"}""", "accountRef" },
        { """{"accountRef":"   ","transactionId":"TXN-A-0029","reason":"unrecognised"}""", "accountRef" },
        { $$"""{"accountRef":"{{Id33}}","transactionId":"TXN-A-0029","reason":"unrecognised"}""", "accountRef" },
        // transactionId: missing, empty, oversized
        { """{"accountRef":"ACC-A-1","reason":"unrecognised"}""", "transactionId" },
        { """{"accountRef":"ACC-A-1","transactionId":"","reason":"unrecognised"}""", "transactionId" },
        { $$"""{"accountRef":"ACC-A-1","transactionId":"{{Id33}}","reason":"unrecognised"}""", "transactionId" },
        // reason: missing or null
        { """{"accountRef":"ACC-A-1","transactionId":"TXN-A-0029"}""", "reason" },
        { """{"accountRef":"ACC-A-1","transactionId":"TXN-A-0029","reason":null}""", "reason" },
        // note: over 1000 after trimming, including a very large one
        { Body(note: new string('a', 1001)), "note" },
        { Body(note: "  " + new string('a', 1001) + "  "), "note" },
        { Body(note: new string('a', 200_000)), "note" },
        // expectedAmount: required for wrongAmount, rejected for any other reason
        { Body(reason: "wrongAmount"), "expectedAmount" },
        { Body(expectedAmount: "10.00"), "expectedAmount" },
        { Body(reason: "duplicateCharge", expectedAmount: "0.00"), "expectedAmount" },
        // expectedAmount: shape (negative, more than 2 decimals, not a number, oversized, padded)
        { Body(reason: "wrongAmount", expectedAmount: "-1.00"), "expectedAmount" },
        { Body(reason: "wrongAmount", expectedAmount: "1.001"), "expectedAmount" },
        { Body(reason: "wrongAmount", expectedAmount: "abc"), "expectedAmount" },
        { Body(reason: "wrongAmount", expectedAmount: ""), "expectedAmount" },
        { Body(reason: "wrongAmount", expectedAmount: "1e3"), "expectedAmount" },
        { Body(reason: "wrongAmount", expectedAmount: "1."), "expectedAmount" },
        { Body(reason: "wrongAmount", expectedAmount: ".50"), "expectedAmount" },
        { Body(reason: "wrongAmount", expectedAmount: "1,50"), "expectedAmount" },
        { Body(reason: "wrongAmount", expectedAmount: " 1.00"), "expectedAmount" },
        { Body(reason: "wrongAmount", expectedAmount: "1.00\n"), "expectedAmount" },
        { Body(reason: "wrongAmount", expectedAmount: "+1.00"), "expectedAmount" },
        { Body(reason: "wrongAmount", expectedAmount: "123456789012345678"), "expectedAmount" },
        { Body(reason: "wrongAmount", expectedAmount: "١٢٣"), "expectedAmount" },
    };

    [Theory]
    [MemberData(nameof(BodiesBreakingAFieldRule))]
    public async Task Raise_BodyBreakingAFieldRule_Returns400NamingTheFieldBeforeTheLookup(string json, string field)
    {
        int coreBankingRequests = CoreBanking.RequestCount;

        HttpResponseMessage response = await Browser.RaiseAsync(json);

        await response.AssertValidationProblemAsync(field);
        Assert.Equal(coreBankingRequests, CoreBanking.RequestCount);
        Assert.Equal(RowCounts.None, await Factory.CountRowsAsync());
    }

    // Story 3.9 AC7: a name of no member ("fraud") and comma lists of names are refused here; any casing of one name
    // binds (Raise_ReasonInEitherCase_BindsToThatReason).
    public static TheoryData<string> BodiesThatDoNotBind => new()
    {
        Body(reason: "fraud"),
        Body(reason: ""),
        // Two names: the built-in enum converter alone binds these as notReceived and as the undefined value 6.
        Body(reason: "duplicateCharge, wrongAmount"),
        Body(reason: "wrongAmount, cancelledSubscription"),
        """{"accountRef":"ACC-A-1","transactionId":"TXN-A-0029","reason":2}""",
        """{"accountRef":"ACC-A-1","transactionId":"TXN-A-0029","reason":"2"}""",
        """{"accountRef":1,"transactionId":"TXN-A-0029","reason":"unrecognised"}""",
        """{"accountRef":"ACC-A-1","transactionId":["TXN-A-0029"],"reason":"unrecognised"}""",
        """{"accountRef":"ACC-A-1","transactionId":"TXN-A-0029","reason":"unrecognised","note":42}""",
        """{"accountRef":"ACC-A-1","transactionId":"TXN-A-0029","reason":"wrongAmount","expectedAmount":80.00}""",
        """{"accountRef":"ACC-A-1",""",
        "not json",
        "null",
        "[]",
        "",
    };

    [Theory]
    [MemberData(nameof(BodiesThatDoNotBind))]
    public async Task Raise_BodyThatDoesNotBind_Returns400ValidationBeforeTheLookup(string json)
    {
        // An unknown reason, a wrong JSON type or broken JSON fails to bind: 400 validation, with no field named.
        int coreBankingRequests = CoreBanking.RequestCount;

        HttpResponseMessage response = await Browser.RaiseAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Validation), await response.ProblemTypeAsync());
        Assert.Equal(coreBankingRequests, CoreBanking.RequestCount);
        // An empty or null body still runs the endpoint filters (with status 400), and must not claim the key.
        Assert.Empty(await Factory.KeyRowsAsync());
    }

    [Fact]
    public async Task Raise_NotJsonContentType_Returns415ValidationBeforeTheLookup()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(DisputesApi.RaisePath, UriKind.Relative))
        {
            Content = new StringContent(Body(), Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("X-XSRF-TOKEN", await Browser.AntiforgeryTokenAsync());
        request.Headers.Add("Idempotency-Key", DisputesApi.NewKey());
        int coreBankingRequests = CoreBanking.RequestCount;

        HttpResponseMessage response = await Browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Validation), await response.ProblemTypeAsync());
        Assert.Equal(coreBankingRequests, CoreBanking.RequestCount);
    }

    [Fact]
    public async Task Raise_ValidBody_ReachesCoreBankingAndGets503WhenItFails()
    {
        // The control for the tests above: a body that passes validation does reach core banking.
        int coreBankingRequests = CoreBanking.RequestCount;

        HttpResponseMessage response = await Browser.RaiseAsync(Body());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(CoreBanking.RequestCount > coreBankingRequests);
    }

    [Theory]
    [InlineData("wrongAmount")]
    [InlineData("WrongAmount")]
    public async Task Raise_ReasonInEitherCase_BindsToThatReason(string reason)
    {
        // SingleNameEnumConverter wraps the framework's JsonStringEnumConverter, whose reads ignore case (AD-5). An Api
        // of its own against the real stub, so the class's failing core banking keeps its circuit closed.
        await using var coreBanking = new TransactionsStub();
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, coreBanking);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        JsonElement created = await browser.RaiseCreatedAsync(Body(reason: reason, expectedAmount: "80.00"));

        Assert.Equal("wrongAmount", created.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Raise_SeveralBrokenFields_Returns400NamingEach()
    {
        HttpResponseMessage response = await Browser.RaiseAsync($$"""{"accountRef":"","transactionId":"{{Id33}}","reason":null}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(
            ["accountRef", "reason", "transactionId"],
            problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Raise_BrokenField_DoesNotEchoTheValue()
    {
        HttpResponseMessage response = await Browser.RaiseAsync(Body(reason: "wrongAmount", expectedAmount: "echo-marker"));

        Assert.DoesNotContain("echo-marker", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    private static string Body(string reason = "unrecognised", string? note = null, string? expectedAmount = null) =>
        DisputesApi.Body(reason: reason, note: note, expectedAmount: expectedAmount);

    /// <summary>One Api for the class, against a failing core banking that counts requests, with customer A signed in.</summary>
    public sealed class LinkedCustomerA(PostgresFixture postgres) : IAsyncLifetime
    {
        private FakeCoreBankingServer? _coreBanking;
        private ApiFactory? _factory;
        private HttpClient? _browser;

        public FakeCoreBankingServer CoreBanking => _coreBanking ?? throw new InvalidOperationException("Not initialised.");

        public ApiFactory Factory => _factory ?? throw new InvalidOperationException("Not initialised.");

        public HttpClient Browser => _browser ?? throw new InvalidOperationException("Not initialised.");

        public async ValueTask InitializeAsync()
        {
            _coreBanking = await FakeCoreBankingServer.FailingAsync();
            _factory = await TransactionsApi.StartAsync(postgres, _coreBanking.BaseUrl, TimeProvider.System.GetUtcNow());
            _browser = await _factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        }

        public async ValueTask DisposeAsync()
        {
            _browser?.Dispose();
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
            }

            if (_coreBanking is not null)
            {
                await _coreBanking.DisposeAsync();
            }
        }
    }
}
