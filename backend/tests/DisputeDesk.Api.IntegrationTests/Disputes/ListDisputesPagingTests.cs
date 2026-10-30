using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// Story 3.9 AC2: the list's inputs and their bounds (<c>limit</c> 1 to 50, default 20; <c>cursor</c> at most 128
/// characters in the layout <c>base64url("&lt;"O" instant&gt;|&lt;"D" ID&gt;")</c>), each refusal a 400 <c>validation</c>
/// naming the field and echoing nothing (AD-3, AD-5), and keyset paging that takes <c>limit + 1</c> rows, so every
/// dispute appears once and a full last page has no <c>nextCursor</c>.
/// </summary>
public sealed class ListDisputesPagingTests(PostgresFixture postgres, ListDisputesPagingTests.LinkedCustomerA api)
    : IClassFixture<ListDisputesPagingTests.LinkedCustomerA>
{
    private const string Instant = "2026-10-01T10:00:00.0000000+00:00";

    private static readonly string Id = Guid.CreateVersion7(DisputesApi.SeedInstant).ToString("D");

    private HttpClient Browser => api.Browser;

    [Theory]
    [InlineData("limit=0", "0")]
    [InlineData("limit=-1", "-1")]
    [InlineData("limit=51", "51")]
    [InlineData("limit=2147483648", "2147483648")] // overflows int, so it does not bind
    [InlineData("limit=abc", "abc")]
    public async Task ListDisputes_LimitOutOfRangeOrNotAnInt_Returns400ValidationNamingTheLimitWithoutEchoingIt(string query, string value)
    {
        HttpResponseMessage response = await Browser.ListDisputesAsync(query);

        await response.AssertValidationProblemAsync("limit");
        // The random traceId is removed first: its hex could hold "abc" or a short number by chance.
        string body = await BodyWithoutTraceIdAsync(response);
        Assert.DoesNotContain($"\"{value}\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", body, StringComparison.Ordinal);
    }

    /// <summary>The problem body with its <c>traceId</c>, the one value that differs per request, removed.</summary>
    private static async Task<string> BodyWithoutTraceIdAsync(HttpResponseMessage response)
    {
        JsonNode body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            ?? throw new InvalidOperationException("No body.");
        Assert.NotNull(body["traceId"]);
        body.AsObject().Remove("traceId");
        return body.ToJsonString();
    }

    [Theory]
    [InlineData("limit=1")]
    [InlineData("limit=50")]
    public async Task ListDisputes_LimitAtABound_Returns200(string query)
    {
        HttpResponseMessage response = await Browser.ListDisputesAsync(query);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Each cursor that breaks the layout, apart from the length.</summary>
    public static TheoryData<string> MalformedCursors => new()
    {
        "",                                                       // empty: decodes to nothing
        "not*base64url",                                          // not base64url
        DisputesApi.ListCursorOf(Instant),                        // no |
        DisputesApi.ListCursorOf($"yesterday|{Id}"),              // the instant fails "O"
        DisputesApi.ListCursorOf($"2026-10-01T10:00:00+00:00|{Id}"), // not the "O" layout (no fraction)
        DisputesApi.ListCursorOf($"2026-10-01T10:00:00.0000000+02:00|{Id}"), // a non-zero offset
        DisputesApi.ListCursorOf($"{Instant}|not-a-guid"),        // the ID fails "D"
        DisputesApi.ListCursorOf($"{Instant}|{Id.Replace("-", "", StringComparison.Ordinal)}"), // the ID in "N", not "D"
        DisputesApi.ListCursorOf($"{Instant}|{Id}|x"),            // content after the ID
        DisputesApi.ListCursorOf($"{Instant}|{Id}x"),             // content after the ID, no separator
        DisputesApi.ListCursorOf($"|{Id}"),                       // no instant
        DisputesApi.ListCursorOf($"{Instant}|"),                  // no ID
    };

    [Theory]
    [MemberData(nameof(MalformedCursors))]
    public async Task ListDisputes_MalformedCursor_Returns400ValidationNamingTheCursorWithoutEchoingIt(string cursor)
    {
        HttpResponseMessage response = await Browser.ListDisputesAsync($"cursor={Uri.EscapeDataString(cursor)}");

        await response.AssertValidationProblemAsync("cursor");
        if (cursor.Length > 0)
        {
            Assert.DoesNotContain(cursor, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ListDisputes_EveryMalformedCursor_GetsTheSameBodyApartFromTheTraceId()
    {
        // One decode that returns null for every failure, so the answer reveals no parse step (ASVS 5.0 V16.5.1).
        var bodies = new HashSet<string>(StringComparer.Ordinal);
        foreach (string cursor in MalformedCursors.Select(row => row.Data))
        {
            HttpResponseMessage response = await Browser.ListDisputesAsync($"cursor={Uri.EscapeDataString(cursor)}");
            bodies.Add(await BodyWithoutTraceIdAsync(response));
        }

        Assert.Single(bodies);
    }

    [Fact]
    public async Task ListDisputes_CursorOf129Characters_Returns400NamingTheCursorWhere128IsRead()
    {
        // A well-formed cursor of 94 characters padded with spaces, which base64url decoding skips (RFC 4648 §3.3 lets an
        // implementation ignore them; System.Buffers.Text.Base64Url does): 128 characters still name the position, so
        // only the length refuses 129.
        string cursor = DisputesApi.ListCursor(DisputesApi.SeedInstant, Guid.Parse(Id));
        string of128 = cursor.PadRight(128);
        string of129 = cursor.PadRight(129);

        HttpResponseMessage at128 = await Browser.ListDisputesAsync($"cursor={Uri.EscapeDataString(of128)}");
        HttpResponseMessage at129 = await Browser.ListDisputesAsync($"cursor={Uri.EscapeDataString(of129)}");

        Assert.Equal(HttpStatusCode.OK, at128.StatusCode);
        await at129.AssertValidationProblemAsync("cursor");
        Assert.DoesNotContain(cursor, await at129.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0001-01-01T00:00:00.0000000+00:00")] // DateTimeOffset.MinValue
    [InlineData("9999-12-31T23:59:59.9999999+00:00")] // DateTimeOffset.MaxValue
    [InlineData("2026-10-01T10:00:00.1234567+00:00")] // a 7th fractional digit, finer than PostgreSQL keeps
    public async Task ListDisputes_WellFormedCursorAtAnExtremeOrFinerInstant_Returns200(string instant)
    {
        // Npgsql sends DateTimeOffset.MinValue and MaxValue as -infinity and infinity, and drops the 7th digit, so
        // each is a position like any other: 200, never 500.
        HttpResponseMessage response = await Browser.ListDisputesAsync($"cursor={DisputesApi.ListCursorOf($"{instant}|{Id}")}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListDisputes_CursorAtMinAndMaxValue_ReturnsNothingAndEverythingAfterThem()
    {
        await using ApiFactory factory = await DisputesApi.StartWithoutCoreBankingAsync(postgres);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        SeededDispute older = await factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-1", DisputesApi.SeedInstant.AddDays(-1));
        SeededDispute newer = await factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-2", DisputesApi.SeedInstant);

        using JsonDocument afterMin = await browser.ListDisputesPageAsync($"cursor={DisputesApi.ListCursor(DateTimeOffset.MinValue, Guid.Empty)}");
        using JsonDocument afterMax = await browser.ListDisputesPageAsync($"cursor={DisputesApi.ListCursor(DateTimeOffset.MaxValue, Guid.Empty)}");

        Assert.Empty(DisputesApi.DisputeIds(afterMin));
        Assert.Equal([newer.Id, older.Id], DisputesApi.DisputeIds(afterMax));
    }

    [Fact]
    public async Task ListDisputes_TiedInstantSplitAcrossPages_ShowsEachOnceInIdOrderAndEachCursorNamesTheLastItem()
    {
        await using ApiFactory factory = await DisputesApi.StartWithoutCoreBankingAsync(postgres);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        List<SeededDispute> seeded =
        [
            await factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-1", DisputesApi.SeedInstant),
            await factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-2", DisputesApi.SeedInstant),
            await factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-3", DisputesApi.SeedInstant.AddHours(-1)),
        ];

        List<JsonElement> items = await browser.ListEveryDisputeAsync("limit=1");

        Assert.Equal(DisputesApi.NewestFirst(seeded).Select(dispute => dispute.Id), items.Select(item => item.GetProperty("disputeId").GetGuid()));
    }

    [Fact]
    public async Task ListDisputes_FullLastPage_HasANullNextCursor()
    {
        // limit + 1 rows are read: a page as long as the limit has a next page only when the extra row exists.
        await using ApiFactory factory = await DisputesApi.StartWithoutCoreBankingAsync(postgres);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        SeededDispute newer = await factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-1", DisputesApi.SeedInstant);
        SeededDispute older = await factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-2", DisputesApi.SeedInstant.AddHours(-1));

        using JsonDocument both = await browser.ListDisputesPageAsync("limit=2");
        using JsonDocument first = await browser.ListDisputesPageAsync("limit=1");

        Assert.Equal([newer.Id, older.Id], DisputesApi.DisputeIds(both));
        Assert.Equal(JsonValueKind.Null, both.RootElement.GetProperty("nextCursor").ValueKind);
        Assert.Equal([newer.Id], DisputesApi.DisputeIds(first));
        Assert.Equal(
            (newer.CreatedAt, newer.Id),
            DisputesApi.ReadListCursor(first.RootElement.GetProperty("nextCursor").GetString() ?? ""));
    }

    [Fact]
    public async Task ListDisputes_NoLimit_Returns20AndTheNextPageTheRestWithANullCursor()
    {
        await using ApiFactory factory = await DisputesApi.StartWithoutCoreBankingAsync(postgres);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        var seeded = new List<SeededDispute>();
        for (int index = 0; index < 21; index++)
        {
            seeded.Add(await factory.SeedDisputeAsync(TransactionsApi.CustomerA, $"TXN-{index}", DisputesApi.SeedInstant.AddMinutes(-index)));
        }

        using JsonDocument first = await browser.ListDisputesPageAsync();
        string nextCursor = first.RootElement.GetProperty("nextCursor").GetString() ?? "";
        using JsonDocument second = await browser.ListDisputesPageAsync($"cursor={Uri.EscapeDataString(nextCursor)}");

        Assert.Equal(seeded.Take(20).Select(dispute => dispute.Id), DisputesApi.DisputeIds(first));
        Assert.Equal([seeded[20].Id], DisputesApi.DisputeIds(second));
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("nextCursor").ValueKind);
    }

    /// <summary>One Api for the input cases, which write no rows, with customer A signed in and core banking unreachable.</summary>
    public sealed class LinkedCustomerA(PostgresFixture postgres) : IAsyncLifetime
    {
        private ApiFactory? _factory;
        private HttpClient? _browser;

        public HttpClient Browser => _browser ?? throw new InvalidOperationException("Not initialised.");

        public async ValueTask InitializeAsync()
        {
            _factory = await DisputesApi.StartWithoutCoreBankingAsync(postgres);
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
