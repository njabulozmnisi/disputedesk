using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;
using Microsoft.Net.Http.Headers;

namespace DisputeDesk.Api.IntegrationTests.Ownership;

/// <summary>
/// AC3 (AD-11, Not found; RFC 9457): on every customer route that takes a resource ID, the detail and the raise, another
/// customer's resource and an unknown ID get the same 404 <c>not-found</c>: the same status, <c>Content-Type</c>, headers
/// and body byte for byte apart from <c>traceId</c>, and neither echoes the ID (AD-3).
/// </summary>
public class NotFoundResponsesTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    // Response headers that differ per request by design: the time and the trace context.
    private static readonly string[] PerRequestHeaders = ["Date", "traceparent", "tracestate"];

    [Fact]
    public async Task GetDispute_AnotherCustomersDisputeAndAnUnknownId_GetTheSame404()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid theirs = await RaiseWithEveryStoredEventTypeAsync(factory);
        string unknown = Guid.CreateVersion7().ToString();
        using HttpClient customerA = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage forTheirs = await customerA.GetDisputeAsync(theirs.ToString());
        HttpResponseMessage forUnknown = await customerA.GetDisputeAsync(unknown);

        await AssertSameNotFoundAsync(forTheirs, forUnknown, theirs.ToString(), unknown);
    }

    [Fact]
    public async Task Raise_AnotherCustomersTransactionAndAnUnknownOne_GetTheSame404()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient customerA = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage forTheirs = await customerA.RaiseAsync(DisputesApi.Body("TXN-B-0026", "ACC-B-1"));
        HttpResponseMessage forUnknown = await customerA.RaiseAsync(DisputesApi.Body("TXN-B-9999", "ACC-B-1"));

        await AssertSameNotFoundAsync(forTheirs, forUnknown, "TXN-B-0026", "TXN-B-9999");
    }

    /// <summary>Customer B's dispute, its stream holding every stored event type, so the detail reads stored events through the filter.</summary>
    private async Task<Guid> RaiseWithEveryStoredEventTypeAsync(ApiFactory factory)
    {
        using HttpClient customerB = await factory.SignInLinkedAsync("login-b", TransactionsApi.CustomerB);
        Guid disputeId = (await customerB.RaiseCreatedAsync(DisputesApi.Body("TXN-B-0027", "ACC-B-1"))).GetProperty("disputeId").GetGuid();
        StoredEvent[] later =
        [
            At(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), 2, ActorType.Operations),
            At(new SlaWarningRaised(), 3, ActorType.Automation),
            At(new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated), 4, ActorType.Automation),
            At(new DisputeResolved(DisputeStatus.Escalated, DisputeStatus.Resolved, Outcome.Rejected, "Checked"), 5, ActorType.Operations),
            At(new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, "Found it"), 6, ActorType.Customer),
        ];
        // Every type the Domain declares is in the stream: DisputeRaised from the raise, the rest above.
        Assert.Equal(
            typeof(StoredEvent).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(StoredEvent))).Select(type => type.Name).Order(StringComparer.Ordinal),
            later.Select(storedEvent => storedEvent.GetType().Name).Append(nameof(DisputeRaised)).Order(StringComparer.Ordinal));
        await factory.AppendAsync(disputeId, DisputeStatus.Withdrawn, Outcome.Rejected, later);
        return disputeId;
    }

    private static async Task AssertSameNotFoundAsync(HttpResponseMessage forTheirs, HttpResponseMessage forUnknown, string theirId, string unknownId)
    {
        Assert.Equal(HttpStatusCode.NotFound, forTheirs.StatusCode);
        Assert.Equal(forTheirs.StatusCode, forUnknown.StatusCode);
        Assert.Equal("application/problem+json", forTheirs.Content.Headers.ContentType?.MediaType);
        Assert.Equal(forTheirs.Content.Headers.ContentType, forUnknown.Content.Headers.ContentType);
        Assert.Equal(Headers(forTheirs), Headers(forUnknown));

        string theirsBody = await WithoutTraceIdAsync(forTheirs);
        Assert.Equal(theirsBody, await WithoutTraceIdAsync(forUnknown));
        using var problem = JsonDocument.Parse(theirsBody);
        Assert.Equal(ProblemType.For(CommonProblems.NotFound), problem.RootElement.GetProperty("type").GetString());
        Assert.DoesNotContain(theirId, theirsBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(unknownId, theirsBody, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every response and content header as name and values, apart from the per-request ones, in name order. The session
    /// cookie is renewed on every signed-in request (<c>BffAuthentication</c>), so its value and expiry are new each
    /// time: each <c>Set-Cookie</c> is compared without them.
    /// </summary>
    private static List<string> Headers(HttpResponseMessage response) =>
    [
        .. response.Headers.Concat(response.Content.Headers)
            .Where(header => !PerRequestHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
            .Select(header => $"{header.Key}: {string.Join(", ", header.Key == "Set-Cookie" ? header.Value.Select(WithoutValueAndExpiry) : header.Value)}")
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// The <c>Set-Cookie</c> header (RFC 6265) with its value and its <c>expires</c> and <c>max-age</c> taken out, so the
    /// name and every other attribute (<c>path</c>, <c>httponly</c>, <c>samesite</c>, <c>secure</c>, any other) still count.
    /// </summary>
    private static string WithoutValueAndExpiry(string setCookie)
    {
        var cookie = SetCookieHeaderValue.Parse(setCookie);
        cookie.Value = "";
        cookie.Expires = null;
        cookie.MaxAge = null;
        return cookie.ToString();
    }

    /// <summary>The body exactly as sent, with only the value of <c>traceId</c> taken out.</summary>
    private static async Task<string> WithoutTraceIdAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var problem = JsonDocument.Parse(body);
        string traceId = problem.RootElement.GetProperty("traceId").GetString() ?? "";
        Assert.NotEmpty(traceId);
        return body.Replace(traceId, "", StringComparison.Ordinal);
    }

    private StoredEvent At(StoredEvent storedEvent, int version, ActorType actorType) =>
        storedEvent with { Version = version, OccurredAtUtc = stub.NowAsStored.AddHours(version), ActorType = actorType, ActorId = "actor-" + version };
}
