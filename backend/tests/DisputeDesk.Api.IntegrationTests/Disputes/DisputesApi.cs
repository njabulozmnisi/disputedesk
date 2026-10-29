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

/// <summary>Row counts of <c>Disputes</c>, <c>StoredEvents</c>, dispute <c>AuditLogEntries</c> and <c>OutboxMessages</c>.</summary>
public sealed record RowCounts(int Disputes, int StoredEvents, int AuditLogEntries, int OutboxMessages)
{
    public static RowCounts None { get; } = new(0, 0, 0, 0);

    /// <summary>What one raise writes: the dispute, its stored event and its outbox row, and no audit row (ad-009).</summary>
    public static RowCounts One { get; } = new(1, 1, 0, 1);
}
