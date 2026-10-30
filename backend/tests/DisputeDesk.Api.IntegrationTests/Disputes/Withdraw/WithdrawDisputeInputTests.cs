using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Disputes.Withdraw;

/// <summary>
/// AC2, the input table and the check order (AD-8: 401, 403, 404, 400, 409). Binding failures answer 400 before the
/// lookup; the note's 500-unit rule runs in the aggregate after the dispute is loaded and before its status. One Api
/// serves the class; each test withdraws its own dispute, committed directly, so none sees another's.
/// </summary>
public sealed class WithdrawDisputeInputTests(WithdrawDisputeInputTests.LinkedCustomerA api) : IClassFixture<WithdrawDisputeInputTests.LinkedCustomerA>
{
    private static readonly string Note500 = new('a', 500);
    private static readonly string Note501 = new('a', 501);

    private ApiFactory Factory => api.Factory;

    private HttpClient Browser => api.Browser;

    public static TheoryData<string, string?> BodiesThatSucceed => new()
    {
        { DisputesApi.WithdrawBody(Note500), Note500 },
        { DisputesApi.WithdrawBody("   " + Note500 + "   "), Note500 },
        { DisputesApi.WithdrawBody("   "), null },
        { "{}", null },
        { """{"note":null}""", null },
        { """{"note":"Found it","unknown":42}""", "Found it" },
    };

    [Theory]
    [MemberData(nameof(BodiesThatSucceed))]
    public async Task Withdraw_NoteWithinTheLimitOrNone_Returns200WithTheTrimmedNoteOrNone(string json, string? note)
    {
        Guid disputeId = await api.OpenDisputeAsync();

        HttpResponseMessage response = await Browser.WithdrawAsync(disputeId.ToString(), json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        JsonElement last = body.RootElement.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal("withdraw", last.GetProperty("action").GetString());
        Assert.Equal(note, last.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Withdraw_NoteOf501UnitsAfterTrimming_Returns400NamingNoteWith500AndChangesNothing()
    {
        Guid disputeId = await api.OpenDisputeAsync();

        HttpResponseMessage response = await Browser.WithdrawAsync(disputeId.ToString(), DisputesApi.WithdrawBody("  " + Note501 + "  "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(CommonProblems.Validation), problem.RootElement.GetProperty("type").GetString());
        JsonProperty error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal("note", error.Name);
        Assert.Equal(DisputeErrors.WithdrawNoteTooLong.Message, Assert.Single(error.Value.EnumerateArray()).GetString());
        Assert.Contains("500", DisputeErrors.WithdrawNoteTooLong.Message, StringComparison.Ordinal);
        Assert.Equal(DisputeStatus.Submitted, (await Factory.DisputeRowAsync(disputeId)).Status);
        Assert.Single(await Factory.StreamAsync(disputeId));
    }

    public static TheoryData<string> BodiesThatDoNotBind => new()
    {
        "",
        "null",
        "[]",
        """{"note":""",
        "not json",
        """{"note":5}""",
        """{"note":["a"]}""",
    };

    [Theory]
    [MemberData(nameof(BodiesThatDoNotBind))]
    public async Task Withdraw_BodyThatDoesNotBind_Returns400ValidationWithNoFieldBeforeTheLookup(string json)
    {
        // The ID is unknown, so a 400 here proves binding answered before the lookup's 404.
        HttpResponseMessage response = await Browser.WithdrawAsync(Guid.CreateVersion7().ToString(), json);

        await response.AssertValidationProblemWithNoFieldAsync();
    }

    [Fact]
    public async Task Withdraw_NoBody_Returns400ValidationWithNoFieldBeforeTheLookup()
    {
        HttpResponseMessage response = await Browser.WithdrawAsync(Guid.CreateVersion7().ToString(), json: null);

        await response.AssertValidationProblemWithNoFieldAsync();
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("123")]
    [InlineData("00000000-0000-0000-0000-00000000000g")]
    public async Task Withdraw_DisputeIdNotAGuid_Returns400ValidationWithNoField(string disputeId)
    {
        HttpResponseMessage response = await Browser.WithdrawAsync(disputeId, DisputesApi.WithdrawBody(Note501));

        await response.AssertValidationProblemWithNoFieldAsync();
    }

    [Fact]
    public async Task Withdraw_TextPlainBody_Returns415Validation()
    {
        Guid disputeId = await api.OpenDisputeAsync();

        HttpResponseMessage response = await Browser.WithdrawAsync(
            disputeId.ToString(), "{}", await Browser.AntiforgeryTokenAsync(), mediaType: "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Validation), await response.ProblemTypeAsync());
        Assert.Equal(DisputeStatus.Submitted, (await Factory.DisputeRowAsync(disputeId)).Status);
    }

    [Fact]
    public async Task Withdraw_NoteTooLongOnAnUnknownId_Returns404Before400()
    {
        HttpResponseMessage response = await Browser.WithdrawAsync(Guid.CreateVersion7().ToString(), DisputesApi.WithdrawBody(Note501));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.NotFound), await response.ProblemTypeAsync());
    }

    [Fact]
    public async Task Withdraw_NoteTooLongOnTheOwnersResolvedDispute_Returns400Before409()
    {
        Guid disputeId = await api.OpenDisputeAsync();
        await Factory.AppendAsync(disputeId, DisputeStatus.Resolved, Outcome.Upheld, new DisputeResolved(DisputeStatus.Submitted, DisputeStatus.Resolved, Outcome.Upheld, "Refunded")
        {
            Version = 2,
            OccurredAtUtc = api.Now,
            ActorType = ActorType.Operations,
            ActorId = "ops-a",
        });

        HttpResponseMessage response = await Browser.WithdrawAsync(disputeId.ToString(), DisputesApi.WithdrawBody(Note501));

        await response.AssertValidationProblemAsync("note");
    }

    /// <summary>Customer A signed in on one Api whose core banking is never called: withdraw reads only stored events.</summary>
    public sealed class LinkedCustomerA(PostgresFixture postgres) : IAsyncLifetime
    {
        private static readonly Uri NoCoreBanking = new("http://core-banking.invalid");

        private ApiFactory? _factory;
        private HttpClient? _browser;
        private int _disputes;

        public DateTimeOffset Now { get; } = TimeProvider.System.GetUtcNow();

        public ApiFactory Factory => _factory ?? throw new InvalidOperationException("Not initialised.");

        public HttpClient Browser => _browser ?? throw new InvalidOperationException("Not initialised.");

        /// <summary>A new Submitted dispute of customer A on a transaction of its own.</summary>
        public Task<Guid> OpenDisputeAsync() =>
            Factory.RaiseDirectlyAsync("login-a", Now, $"TXN-A-9{Interlocked.Increment(ref _disputes):D3}");

        public async ValueTask InitializeAsync()
        {
            _factory = await TransactionsApi.StartAsync(postgres, NoCoreBanking, Now);
            _browser = await _factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
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
