using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Application.Transactions;
using DisputeDesk.Contracts.Problems;

namespace DisputeDesk.Api.IntegrationTests.Transactions;

/// <summary>
/// AC3 to AC5 for customer A, whose 48 fixture transactions the stub dates from <see cref="TransactionsStub.Now"/>:
/// keyset paging, the row's fields and wire formats, and disputability on each row.
/// </summary>
public sealed partial class TransactionsListTests(PostgresFixture postgres, TransactionsStub stub)
    : IClassFixture<TransactionsStub>, IAsyncLifetime
{
    private const int CustomerATransactionCount = 48;

    private static readonly string[] RowFields =
    [
        "accountLast4", "accountRef", "amount", "customerStatus", "description", "direction", "disputable", "disputeId", "kind",
        "maskedCard", "notDisputableReason", "state", "transactionDate", "transactionId",
    ];

    private ApiFactory? _factory;
    private HttpClient? _browser;

    private HttpClient Browser => _browser ?? throw new InvalidOperationException("Not initialised.");

    public async ValueTask InitializeAsync()
    {
        _factory = await TransactionsApi.StartAsync(postgres, stub);
        await _factory.LinkAsync("login-a", TransactionsApi.CustomerA);
        _browser = _factory.CreateBrowser(new CookieContainer());
        await _browser.SignInAsync(_factory.Zitadel, "login-a");
    }

    public async ValueTask DisposeAsync()
    {
        _browser?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task ListTransactions_NoLimitOrCursor_Returns20AndTheLastPageEndsWithANullCursor()
    {
        using JsonDocument first = await GetPageAsync("/api/v1/transactions");
        string? secondCursor = first.RootElement.GetProperty("nextCursor").GetString();
        Assert.Equal(20, first.RootElement.GetProperty("items").GetArrayLength());
        Assert.NotNull(secondCursor);

        using JsonDocument second = await GetPageAsync($"/api/v1/transactions?cursor={Uri.EscapeDataString(secondCursor)}");
        string? thirdCursor = second.RootElement.GetProperty("nextCursor").GetString();
        Assert.Equal(20, second.RootElement.GetProperty("items").GetArrayLength());
        Assert.NotNull(thirdCursor);

        using JsonDocument third = await GetPageAsync($"/api/v1/transactions?cursor={Uri.EscapeDataString(thirdCursor)}");

        Assert.Equal(CustomerATransactionCount - 40, third.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, third.RootElement.GetProperty("nextCursor").ValueKind);
        using JsonDocument everything = await GetPageAsync("/api/v1/transactions?limit=50");
        Assert.Equal(TransactionIds(everything), [.. TransactionIds(first), .. TransactionIds(second), .. TransactionIds(third)]);
    }

    [Fact]
    public async Task ListTransactions_Limit50_ReturnsEveryRowNewestFirstByDateThenId()
    {
        using JsonDocument page = await GetPageAsync("/api/v1/transactions?limit=50");

        var rows = page.RootElement.GetProperty("items").EnumerateArray()
            .Select(row => (Date: row.GetProperty("transactionDate").GetDateTimeOffset(), Id: row.GetProperty("transactionId").GetString() ?? ""))
            .ToList();
        Assert.Equal(CustomerATransactionCount, rows.Count);
        Assert.Equal(rows.OrderByDescending(row => row.Date).ThenByDescending(row => row.Id, StringComparer.Ordinal), rows);
        Assert.Equal(JsonValueKind.Null, page.RootElement.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task ListTransactions_SmallPages_VisitEveryRowOnceThroughOpaqueCursors()
    {
        var seen = new List<string>();
        string? cursor = null;
        do
        {
            string path = cursor is null ? "/api/v1/transactions?limit=7" : $"/api/v1/transactions?limit=7&cursor={Uri.EscapeDataString(cursor)}";
            using JsonDocument page = await GetPageAsync(path);
            seen.AddRange(TransactionIds(page));
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
            // Opaque: the stub's raw cursor is a transaction ID, which must not show through.
            Assert.DoesNotContain("TXN", cursor ?? "", StringComparison.Ordinal);
        }
        while (cursor is not null);

        Assert.Equal(CustomerATransactionCount, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("limit=51", "limit")]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=-1", "limit")]
    [InlineData("cursor=not*base64url", "cursor")]
    [InlineData("cursor=", "cursor")]
    public async Task ListTransactions_LimitAbove50OrUndecodableCursor_Returns400ValidationNamingTheField(string query, string field)
    {
        HttpResponseMessage response = await Browser.GetPathAsync($"/api/v1/transactions?{query}");

        await AssertValidationProblemAsync(response, field);
    }

    [Theory]
    [InlineData("limit=abc")]
    [InlineData("limit=")]
    [InlineData("limit=20&limit=30")]
    public async Task ListTransactions_LimitNotAWholeNumber_Returns400ValidationNamingTheLimit(string query)
    {
        // Not an int, so binding fails. With ThrowOnBadRequest off, the framework sets 400 and still runs the endpoint's
        // filters (Learn: "Filters and parameter binding failures"); the built-in validation filter then finds limit,
        // never bound, outside 1 to 50 and names it. With ThrowOnBadRequest on (Development's default) this was a 500.
        HttpResponseMessage response = await Browser.GetPathAsync($"/api/v1/transactions?{query}");

        await AssertValidationProblemAsync(response, "limit");
    }

    [Fact]
    public async Task ListTransactions_CursorCoreBankingDoesNotKnow_Returns400ValidationNamingTheCursor()
    {
        // Well-formed base64url, but not a cursor the Api ever handed out.
        string unknown = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("TXN-Z-9999"));

        HttpResponseMessage response = await Browser.GetPathAsync($"/api/v1/transactions?cursor={unknown}");

        await AssertValidationProblemAsync(response, "cursor");
    }

    [Fact]
    public async Task ListTransactions_CursorLongerThanAnyRealOne_Returns400ValidationNamingTheCursor()
    {
        // Well-formed base64url, but 2000 characters. It decodes to bytes that are not UTF-8, which would make a request
        // line too long for core banking (a 503); it must stop at the Api instead.
        HttpResponseMessage response = await Browser.GetPathAsync($"/api/v1/transactions?cursor={new string('_', 2000)}");

        await AssertValidationProblemAsync(response, "cursor");
    }

    [Fact]
    public async Task ListTransactions_CursorForAnotherCustomersTransaction_Returns400ValidationNamingTheCursor()
    {
        // Encoded exactly as the Api encodes cursors, but pointing at customer B's transaction.
        string cursorForB = TransactionCursor.Encode("TXN-B-0001");

        HttpResponseMessage response = await Browser.GetPathAsync($"/api/v1/transactions?cursor={cursorForB}");

        await AssertValidationProblemAsync(response, "cursor");
    }

    [Fact]
    public async Task ListTransactions_LinkedCustomer_SendsCacheControlNoStore()
    {
        HttpResponseMessage response = await Browser.GetPathAsync("/api/v1/transactions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["no-store"], response.Headers.GetValues("Cache-Control"));
    }

    [Fact]
    public async Task ListTransactions_AnyRow_HasMaskedNumbersMoneyAsAStringAndCamelCaseEnums()
    {
        using JsonDocument page = await GetPageAsync("/api/v1/transactions?limit=50");

        foreach (JsonElement row in page.RootElement.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(RowFields, row.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));
            Assert.Matches(Last4(), row.GetProperty("accountLast4").GetString());
            JsonElement maskedCard = row.GetProperty("maskedCard");
            if (maskedCard.ValueKind != JsonValueKind.Null)
            {
                Assert.Matches(First6AndLast4(), maskedCard.GetString());
            }

            JsonElement amount = row.GetProperty("amount");
            Assert.Equal(["amount", "currency"], amount.EnumerateObject().Select(field => field.Name));
            Assert.Matches(TwoDecimals(), amount.GetProperty("amount").GetString());
            Assert.Equal("ZAR", amount.GetProperty("currency").GetString());
        }

        JsonElement transfer = Row(page, "TXN-A-0024");
        Assert.Equal("""{"amount":"2000.00","currency":"ZAR"}""", transfer.GetProperty("amount").GetRawText());
        Assert.Equal("debit", transfer.GetProperty("direction").GetString());
        Assert.Equal("ownAccountTransfer", transfer.GetProperty("kind").GetString());
        Assert.Equal("settled", transfer.GetProperty("state").GetString());
        Assert.Equal("notEligible", transfer.GetProperty("notDisputableReason").GetString());
        Assert.Equal("0011", transfer.GetProperty("accountLast4").GetString());
        Assert.Equal("492181******4417", Row(page, "TXN-A-0029").GetProperty("maskedCard").GetString());
    }

    [Theory]
    [InlineData("TXN-A-0029", true, null)]               // settled card debit, 2 days ago
    [InlineData("TXN-A-0001", false, "outsideWindow")]   // settled card debit, 95 days ago
    [InlineData("TXN-A-0030", false, "notEligible")]     // pending card debit
    [InlineData("TXN-A-0002", false, "notEligible")]     // credit
    [InlineData("TXN-A-0024", false, "notEligible")]     // own-account transfer
    public async Task ListTransactions_EachRow_CarriesItsDisputability(string transactionId, bool disputable, string? reason)
    {
        using JsonDocument page = await GetPageAsync("/api/v1/transactions?limit=50");

        JsonElement row = Row(page, transactionId);

        Assert.Equal(disputable, row.GetProperty("disputable").GetBoolean());
        Assert.Equal(reason, row.GetProperty("notDisputableReason").GetString());
    }

    private async Task<JsonDocument> GetPageAsync(string path)
    {
        HttpResponseMessage response = await Browser.GetPathAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(CommonProblems.Validation), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal([field], problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    private static List<string> TransactionIds(JsonDocument page) =>
        [.. page.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("transactionId").GetString() ?? "")];

    private static JsonElement Row(JsonDocument page, string transactionId) =>
        page.RootElement.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("transactionId").GetString() == transactionId);

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex Last4();

    [GeneratedRegex(@"^\d{6}\*{6}\d{4}$")]
    private static partial Regex First6AndLast4();

    [GeneratedRegex(@"^\d+\.\d{2}$")]
    private static partial Regex TwoDecimals();
}
