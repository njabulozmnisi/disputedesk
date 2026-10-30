using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// Raising a dispute as a browser would: a linked login, its session cookie and the antiforgery header. Customer A's
/// <see cref="Disputable"/> is a settled card debit 2 days before the stub's instant, for R87.23.
/// </summary>
public static class DisputesApi
{
    public const string RaisePath = "/api/v1/disputes";

    public const string Disputable = "TXN-A-0029";
    public const string DisputableAmount = "87.23";
    public const string Pending = "TXN-A-0030";
    public const string Old = "TXN-A-0001";
    public const string AccountA = "ACC-A-1";

    private static readonly AuditAction[] DisputeAuditActions =
        [AuditAction.Raise, AuditAction.StartReview, AuditAction.Escalate, AuditAction.Resolve, AuditAction.Withdraw];

    /// <summary>A browser signed in as <paramref name="loginSub"/>, linked to <paramref name="customerId"/>.</summary>
    public static async Task<HttpClient> SignInLinkedAsync(this ApiFactory factory, string loginSub, string customerId)
    {
        await factory.LinkAsync(loginSub, customerId);
        HttpClient browser = factory.CreateBrowser(new CookieContainer());
        await browser.SignInAsync(factory.Zitadel, loginSub);
        return browser;
    }

    /// <summary>The JSON body of a raise; null fields are left out.</summary>
    public static string Body(
        string transactionId = Disputable,
        string accountRef = AccountA,
        string reason = "unrecognised",
        string? note = null,
        string? expectedAmount = null)
    {
        var body = new Dictionary<string, string> { ["accountRef"] = accountRef, ["transactionId"] = transactionId, ["reason"] = reason };
        if (note is not null)
        {
            body["note"] = note;
        }

        if (expectedAmount is not null)
        {
            body["expectedAmount"] = expectedAmount;
        }

        return JsonSerializer.Serialize(body);
    }

    /// <summary>A new <c>Idempotency-Key</c> header value: a UUID as an RFC 9651 String, so quoted.</summary>
    public static string NewKey() => $"\"{Guid.NewGuid()}\"";

    /// <summary>
    /// POSTs <paramref name="json"/> as the raise body under a new idempotency key, with a fresh antiforgery token unless
    /// told not to.
    /// </summary>
    public static async Task<HttpResponseMessage> RaiseAsync(this HttpClient browser, string json, bool withAntiforgeryToken = true) =>
        await browser.RaiseAsync(json, withAntiforgeryToken ? await browser.AntiforgeryTokenAsync() : null);

    /// <summary>
    /// POSTs <paramref name="json"/> under a new idempotency key with the given antiforgery token. Parallel raises share
    /// one token: fetching a token also renews the antiforgery cookie, so tokens fetched in parallel would not all match it.
    /// </summary>
    public static Task<HttpResponseMessage> RaiseAsync(this HttpClient browser, string json, string? antiforgeryToken) =>
        browser.RaiseAsync(json, antiforgeryToken, NewKey());

    /// <summary>
    /// POSTs <paramref name="json"/> with the given antiforgery token and <c>Idempotency-Key</c> values, each sent as it
    /// is (no header validation), so malformed values reach the Api; several values send the header once per value.
    /// </summary>
    public static async Task<HttpResponseMessage> RaiseAsync(this HttpClient browser, string json, string? antiforgeryToken, params string?[] idempotencyKeys)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(RaisePath, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (antiforgeryToken is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        }

        string[] keys = [.. idempotencyKeys.OfType<string>()];
        if (keys.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", keys);
        }

        return await browser.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>POSTs <paramref name="json"/> with a fresh antiforgery token and the given <c>Idempotency-Key</c> value, or none when null.</summary>
    public static async Task<HttpResponseMessage> RaiseWithKeyAsync(this HttpClient browser, string json, string? idempotencyKey) =>
        await browser.RaiseAsync(json, await browser.AntiforgeryTokenAsync(), idempotencyKey);

    /// <summary>The 201 body of a raise of <paramref name="json"/>, which must succeed.</summary>
    public static async Task<JsonElement> RaiseCreatedAsync(this HttpClient browser, string json)
    {
        HttpResponseMessage response = await browser.RaiseAsync(json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument created = await response.ReadJsonAsync();
        return created.RootElement.Clone();
    }

    /// <summary>GETs the detail of the dispute <paramref name="disputeId"/>, written as the client sends it.</summary>
    public static Task<HttpResponseMessage> GetDisputeAsync(this HttpClient browser, string disputeId) =>
        browser.GetPathAsync($"{RaisePath}/{disputeId}");

    /// <summary>GETs the history list with <paramref name="query"/> as given, such as <c>limit=1&amp;cursor=...</c>.</summary>
    public static Task<HttpResponseMessage> ListDisputesAsync(this HttpClient browser, string query = "") =>
        browser.GetPathAsync(query.Length == 0 ? RaisePath : $"{RaisePath}?{query}");

    /// <summary>The body of a history list page, which must answer 200.</summary>
    public static async Task<JsonDocument> ListDisputesPageAsync(this HttpClient browser, string query = "")
    {
        HttpResponseMessage response = await browser.ListDisputesAsync(query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    /// <summary>The <c>disputeId</c> of each item on a history list page, in page order.</summary>
    public static List<Guid> DisputeIds(JsonDocument page) =>
        [.. page.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("disputeId").GetGuid())];

    /// <summary>
    /// Every item from the first page on, following each <c>nextCursor</c>; each cursor must decode to exactly the last
    /// item of its page (ASVS 5.0 V8.2.2), and the last page's must be null.
    /// </summary>
    public static async Task<List<JsonElement>> ListEveryDisputeAsync(this HttpClient browser, string limitQuery)
    {
        var items = new List<JsonElement>();
        string? cursor = null;
        do
        {
            string query = cursor is null ? limitQuery : $"{limitQuery}&cursor={Uri.EscapeDataString(cursor)}";
            using JsonDocument page = await browser.ListDisputesPageAsync(query);
            List<JsonElement> pageItems = [.. page.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone())];
            items.AddRange(pageItems);
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
            if (cursor is not null)
            {
                JsonElement last = pageItems[^1];
                Assert.Equal(
                    (last.GetProperty("createdAt").GetDateTimeOffset(), last.GetProperty("disputeId").GetGuid()),
                    ReadListCursor(cursor));
            }
        }
        while (cursor is not null);

        return items;
    }

    /// <summary>
    /// A list cursor written by hand in the layout the story fixes, not by the Api's own code: base64url (RFC 4648 §5)
    /// of the UTF-8 text <c>&lt;instant in "O"&gt;|&lt;ID in "D"&gt;</c>.
    /// </summary>
    public static string ListCursor(DateTimeOffset createdAt, Guid disputeId) =>
        ListCursorOf($"{createdAt.ToString("O", CultureInfo.InvariantCulture)}|{disputeId:D}");

    /// <summary>The base64url of <paramref name="text"/>, for cursors that break the layout.</summary>
    public static string ListCursorOf(string text) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// The position a list cursor names, decoded by hand: the instant and the dispute ID between the <c>|</c>.
    /// </summary>
    public static (DateTimeOffset CreatedAt, Guid DisputeId) ReadListCursor(string cursor)
    {
        string[] parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Split('|');
        Assert.Equal(2, parts.Length);
        return (DateTimeOffset.ParseExact(parts[0], "O", CultureInfo.InvariantCulture), Guid.ParseExact(parts[1], "D"));
    }

    /// <summary>
    /// The newest-first order of the list (AD-5): <c>CreatedAtUtc</c>, then the ID as PostgreSQL orders a <c>uuid</c>,
    /// which is the ordinal order of its lowercase canonical text, both descending.
    /// </summary>
    public static List<SeededDispute> NewestFirst(IEnumerable<SeededDispute> disputes) =>
        [.. disputes.OrderByDescending(dispute => dispute.CreatedAt).ThenByDescending(dispute => dispute.Id.ToString("D"), StringComparer.Ordinal)];

    /// <summary>
    /// The Api with core banking unreachable and its clock fixed at today's whole second (the sign-in cookies expire 15
    /// minutes after it). The history list reads only the database, so it needs no core banking.
    /// </summary>
    public static Task<ApiFactory> StartWithoutCoreBankingAsync(PostgresFixture postgres, Action<IServiceCollection>? configureServices = null) =>
        TransactionsApi.StartAsync(
            postgres,
            new Uri("http://127.0.0.1:1"),
            DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()),
            configureServices);

    /// <summary>A whole-second instant that seeded disputes are raised around, so each is stored exactly as given.</summary>
    public static DateTimeOffset SeedInstant { get; } = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Raises a dispute for <paramref name="customerId"/> at <paramref name="createdAt"/> through the aggregate and the
    /// one commit path (AD-14), without HTTP, so a test chooses the instant and the customer. The transaction is a
    /// settled card debit two days before.
    /// </summary>
    public static async Task<SeededDispute> SeedDisputeAsync(this ApiFactory factory, string customerId, string transactionId, DateTimeOffset createdAt)
    {
        var snapshot = new TransactionSnapshot("ACC-" + customerId, transactionId, "0011", createdAt.AddDays(-2), "Shop", 10m, "ZAR", null,
            TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);
        Dispute dispute = Dispute.Raise(customerId, snapshot, everDisputed: false, Reason.Unrecognised, note: null, expectedAmount: null,
            new Actor(ActorType.Customer, "login-" + customerId), createdAt).Value;
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<Dispute>().Add(dispute);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new SeededDispute(dispute.Id, customerId, createdAt);
    }

    /// <summary>The withdraw path of the dispute <paramref name="disputeId"/>, written as the client sends it.</summary>
    public static string WithdrawPath(string disputeId) => $"{RaisePath}/{disputeId}/withdraw";

    /// <summary>The JSON body of a withdraw: <c>{}</c>, or the note alone.</summary>
    public static string WithdrawBody(string? note = null) =>
        note is null ? "{}" : JsonSerializer.Serialize(new Dictionary<string, string> { ["note"] = note });

    /// <summary>POSTs <paramref name="json"/> as the withdraw body, with a fresh antiforgery token unless told not to.</summary>
    public static async Task<HttpResponseMessage> WithdrawAsync(this HttpClient browser, string disputeId, string? json = "{}", bool withAntiforgeryToken = true) =>
        await browser.WithdrawAsync(disputeId, json, withAntiforgeryToken ? await browser.AntiforgeryTokenAsync() : null);

    /// <summary>
    /// POSTs <paramref name="json"/> as the withdraw body with the given antiforgery token, as <paramref name="mediaType"/>;
    /// a null <paramref name="json"/> sends no body. Parallel withdraws share one token, as parallel raises do.
    /// </summary>
    public static async Task<HttpResponseMessage> WithdrawAsync(
        this HttpClient browser, string disputeId, string? json, string? antiforgeryToken, string mediaType = "application/json")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(WithdrawPath(disputeId), UriKind.Relative));
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, mediaType);
        }

        if (antiforgeryToken is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        }

        return await browser.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The 200 body of a withdraw of <paramref name="disputeId"/>, which must succeed.</summary>
    public static async Task<JsonElement> WithdrawOkAsync(this HttpClient browser, string disputeId, string json = "{}")
    {
        HttpResponseMessage response = await browser.WithdrawAsync(disputeId, json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        return body.RootElement.Clone();
    }

    /// <summary>409 <c>action-not-allowed</c> carrying <paramref name="customerStatus"/> (AD-13).</summary>
    public static async Task AssertActionNotAllowedAsync(this HttpResponseMessage response, string customerStatus)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(CommonProblems.ActionNotAllowed), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(customerStatus, problem.RootElement.GetProperty("customerStatus").GetString());
    }

    /// <summary>
    /// Raises a dispute of customer A on <paramref name="transactionId"/> straight through the commit path at <paramref name="at"/>,
    /// without core banking: for a raise earlier than the Api's clock (the session cookie follows that clock), or for
    /// many open disputes in one test.
    /// </summary>
    public static async Task<Guid> RaiseDirectlyAsync(this ApiFactory factory, string loginSub, DateTimeOffset at, string transactionId = Disputable)
    {
        var transaction = new TransactionSnapshot(
            AccountA, transactionId, "4417", at.AddDays(-2), "Steers Braamfontein", 87.23m, "ZAR", null,
            TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);
        Dispute dispute = Dispute.Raise(
            TransactionsApi.CustomerA, transaction, everDisputed: false, Reason.Unrecognised, note: null, expectedAmount: null,
            new Actor(ActorType.Customer, loginSub), at).Value;
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<Dispute>().Add(dispute);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return dispute.Id;
    }

    /// <summary>The dispute's stored event rows in version order, read past the ownership filter.</summary>
    public static async Task<List<StoredEventRow>> StreamAsync(this ApiFactory factory, Guid disputeId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<StoredEventRow>().IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.DisputeId == disputeId)
            .OrderBy(row => row.Version)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The dispute's <c>Disputes</c> row, read past the ownership filter.</summary>
    public static async Task<Dispute> DisputeRowAsync(this ApiFactory factory, Guid disputeId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Dispute>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(dispute => dispute.Id == disputeId, TestContext.Current.CancellationToken);
    }

    /// <summary>The dispute's outbox rows in version order.</summary>
    public static async Task<List<OutboxMessage>> OutboxRowsAsync(this ApiFactory factory, Guid disputeId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<OutboxMessage>().AsNoTracking()
            .Where(message => message.DisputeId == disputeId)
            .OrderBy(message => message.DisputeVersion)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Every audit row of a dispute action (<c>Raise</c> to <c>Withdraw</c>), oldest first.</summary>
    public static async Task<List<AuditLogEntry>> DisputeAuditRowsAsync(this ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLogEntry>().AsNoTracking()
            .Where(entry => DisputeAuditActions.Contains(entry.Action))
            .OrderBy(entry => entry.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Appends <paramref name="storedEvents"/> to the stream of the dispute <paramref name="disputeId"/>, each with its
    /// own version, instant, actor and trace ID, and moves that dispute's <c>Disputes</c> row to <paramref name="status"/>
    /// and <paramref name="outcome"/> at the stream's highest stored version, as the later stories' actions will. Only the
    /// raise exists yet, so tests write the later stored events by hand; the insert-only guard allows adds.
    /// </summary>
    public static async Task AppendAsync(this ApiFactory factory, Guid disputeId, DisputeStatus status, Outcome? outcome, params StoredEvent[] storedEvents)
    {
        await factory.AddRowsAsync([.. storedEvents.Select(storedEvent => StoredEventRows.For(disputeId, storedEvent))]);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int version = await db.Set<StoredEventRow>().IgnoreQueryFilters()
            .Where(row => row.DisputeId == disputeId)
            .MaxAsync(row => row.Version, TestContext.Current.CancellationToken);
        await db.Set<Dispute>().IgnoreQueryFilters()
            .Where(dispute => dispute.Id == disputeId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(dispute => dispute.Status, status)
                    .SetProperty(dispute => dispute.Outcome, outcome)
                    .SetProperty(dispute => dispute.Version, version),
                TestContext.Current.CancellationToken);
    }

    /// <summary>The <c>Disputes</c> row's <c>Version</c> of the dispute <paramref name="disputeId"/>, read past the ownership filter.</summary>
    public static async Task<int> DisputeVersionAsync(this ApiFactory factory, Guid disputeId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Dispute>().IgnoreQueryFilters()
            .Where(dispute => dispute.Id == disputeId)
            .Select(dispute => dispute.Version)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Inserts <paramref name="rows"/> into <c>StoredEvents</c> exactly as given, as a damaged or a newer writer might have.</summary>
    public static async Task AddRowsAsync(this ApiFactory factory, params StoredEventRow[] rows)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<StoredEventRow>().AddRange(rows);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The rows a raise writes, and the dispute audit rows it must not write (ad-009), read past the ownership filter.
    /// Only audit rows of a dispute action count, so a sign-in or linking audit row never changes the counts.
    /// </summary>
    public static async Task<RowCounts> CountRowsAsync(this ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        return new RowCounts(
            await db.Set<Dispute>().IgnoreQueryFilters().CountAsync(cancellationToken),
            await db.Set<StoredEventRow>().IgnoreQueryFilters().CountAsync(cancellationToken),
            await db.Set<AuditLogEntry>().CountAsync(entry => DisputeAuditActions.Contains(entry.Action), cancellationToken),
            await db.Set<OutboxMessage>().CountAsync(cancellationToken));
    }

    /// <summary>400, the <c>validation</c> type, and exactly one field error, under <paramref name="field"/>.</summary>
    public static async Task AssertValidationProblemAsync(this HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(CommonProblems.Validation), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal([field], problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    /// <summary>
    /// Every column of a <c>Rejected</c> audit row of customer A (login <c>login-a</c>) for <paramref name="action"/>,
    /// recorded at <paramref name="at"/> as stored; the per-request <c>Id</c> and <c>TraceId</c> are only shaped (AD-14a).
    /// </summary>
    public static void AssertRejectedOfA(this AuditLogEntry row, AuditAction action, DateTimeOffset at, Guid? disputeId)
    {
        Assert.Equal(7, row.Id.Version);
        Assert.Equal(at, row.OccurredAtUtc);
        Assert.Equal(action, row.Action);
        Assert.Equal(AuditResult.Rejected, row.Result);
        Assert.Equal(ActorType.Customer, row.ActorType);
        Assert.Equal("login-a", row.ActorId);
        Assert.Equal(TransactionsApi.CustomerA, row.CustomerId);
        Assert.Equal(disputeId, row.DisputeId);
        Assert.Matches("^[0-9a-f]{32}$", row.TraceId);
    }

    /// <summary>400, the <c>validation</c> type, and no field named: <c>errors</c> absent or empty.</summary>
    public static async Task AssertValidationProblemWithNoFieldAsync(this HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(CommonProblems.Validation), problem.RootElement.GetProperty("type").GetString());
        Assert.False(problem.RootElement.TryGetProperty("errors", out JsonElement errors) && errors.EnumerateObject().Any());
    }

    /// <summary>409 <c>already-disputed</c> naming the customer's existing dispute and its customer status (AD-13).</summary>
    public static async Task AssertAlreadyDisputedAsync(this HttpResponseMessage response, string disputeId, string customerStatus)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(DisputeProblems.AlreadyDisputed), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(disputeId, problem.RootElement.GetProperty("disputeId").GetString());
        Assert.Equal(customerStatus, problem.RootElement.GetProperty("customerStatus").GetString());
    }

    public static async Task<string> ProblemTypeAsync(this HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = await response.ReadJsonAsync();
        return problem.RootElement.GetProperty("type").GetString() ?? "";
    }
}

/// <summary>A dispute a test seeded: its ID, its customer and the instant it was raised.</summary>
public sealed record SeededDispute(Guid Id, string CustomerId, DateTimeOffset CreatedAt);

/// <summary>Row counts of <c>Disputes</c>, <c>StoredEvents</c>, dispute <c>AuditLogEntries</c> and <c>OutboxMessages</c>.</summary>
public sealed record RowCounts(int Disputes, int StoredEvents, int AuditLogEntries, int OutboxMessages)
{
    public static RowCounts None { get; } = new(0, 0, 0, 0);

    /// <summary>What one raise writes: the dispute, its stored event and its outbox row, and no audit row (ad-009).</summary>
    public static RowCounts One { get; } = new(1, 1, 0, 1);

    /// <summary>What one refused raise writes: its <c>Rejected</c> audit row and nothing else (AD-14a).</summary>
    public static RowCounts OneRefusal { get; } = new(0, 0, 1, 0);
}
