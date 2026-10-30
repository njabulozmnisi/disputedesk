using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>Calling the operations routes as a tool would: a bearer token in the <c>Authorization</c> header and no cookies.</summary>
public static class OperationsApi
{
    public const string DisputesPath = "/api/v1/operations/disputes";
    public const string ReadRole = "dispute.read";
    public const string StartReviewRole = "dispute.startReview";
    public const string EscalateRole = "dispute.escalate";
    public const string ResolveRole = "dispute.resolve";

    /// <summary>The three actions, each as its route segment and its role.</summary>
    public static TheoryData<string, string> Actions => new()
    {
        { "start-review", StartReviewRole },
        { "escalate", EscalateRole },
        { "resolve", ResolveRole },
    };

    /// <summary>A valid resolve body.</summary>
    public static string ResolveBody(string outcome = "upheld", string note = "We've reversed this debit.") =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["outcome"] = outcome, ["note"] = note });

    /// <summary>The body each action is sent with in a test that is not about the body: none, or a valid resolve.</summary>
    public static string? BodyFor(string action) => action == "resolve" ? ResolveBody() : null;

    /// <summary>
    /// POSTs the <paramref name="action"/> (<c>start-review</c>, <c>escalate</c> or <c>resolve</c>) of <paramref name="disputeId"/>
    /// with <paramref name="token"/> as the bearer token, or none when null, and <paramref name="json"/> as the body sent as
    /// <paramref name="mediaType"/>, or no body when null.
    /// </summary>
    public static async Task<HttpResponseMessage> ActAsync(
        this HttpClient tool, string disputeId, string action, string? token, string? json = null, string mediaType = "application/json")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{DisputesPath}/{disputeId}/{action}", UriKind.Relative));
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, mediaType);
        }

        return await tool.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The <paramref name="action"/> of <paramref name="disputeId"/> by the mapped operations token holding the action's role.</summary>
    public static Task<HttpResponseMessage> ActAsMappedAsync(this ApiFactory factory, HttpClient tool, Guid disputeId, string action, string? json = null) =>
        tool.ActAsync(disputeId.ToString(), action, factory.Zitadel.Issue(roles: RoleOf(action)), json ?? BodyFor(action));

    public static string RoleOf(string action) => action switch
    {
        "start-review" => StartReviewRole,
        "escalate" => EscalateRole,
        "resolve" => ResolveRole,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action."),
    };

    /// <summary>Every audit entry, oldest first.</summary>
    public static async Task<List<AuditLogEntry>> AuditRowsAsync(this ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLogEntry>().AsNoTracking()
            .OrderBy(entry => entry.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>409 <c>action-not-allowed</c> carrying <paramref name="disputeStatus"/>, the internal status, and no <c>customerStatus</c>.</summary>
    public static async Task AssertNotAllowedInAsync(this HttpResponseMessage response, string disputeStatus)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using JsonDocument problem = await response.ReadJsonAsync();
        Assert.Equal(ProblemType.For(CommonProblems.ActionNotAllowed), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(disputeStatus, problem.RootElement.GetProperty("disputeStatus").GetString());
        Assert.Equal(409, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(problem.RootElement.TryGetProperty("customerStatus", out _));
    }

    /// <summary>A client with no cookie jar that does not follow redirects, so a 302 would show.</summary>
    public static HttpClient CreateTool(this ApiFactory factory) => factory.CreateBrowser(new CookieContainer());

    /// <summary>GETs <paramref name="path"/> with <paramref name="authorization"/> sent as it is, or no header when null.</summary>
    public static async Task<HttpResponseMessage> GetWithAsync(this HttpClient client, string path, string? authorization)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The operations read of <paramref name="disputeId"/>, written as the tool sends it, with <paramref name="token"/> as the bearer token.</summary>
    public static Task<HttpResponseMessage> ReadDisputeAsync(this HttpClient tool, string disputeId, string? token) =>
        tool.GetWithAsync($"{DisputesPath}/{disputeId}", token is null ? null : $"Bearer {token}");

    /// <summary>Customer A raises a dispute through the customer API; returns its ID.</summary>
    public static async Task<Guid> RaiseForCustomerAAsync(this ApiFactory factory, string? note = null)
    {
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        return (await browser.RaiseCreatedAsync(DisputesApi.Body(note: note))).GetProperty("disputeId").GetGuid();
    }

    /// <summary>The <c>DisputeRead</c> audit entries about <paramref name="disputeId"/>.</summary>
    public static async Task<List<AuditLogEntry>> DisputeReadEntriesAsync(this ApiFactory factory, Guid disputeId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLogEntry>()
            .Where(entry => entry.Action == AuditAction.DisputeRead && entry.DisputeId == disputeId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The response's JSON body without its <c>traceId</c>, which differs on every request.</summary>
    public static async Task<string> BodyWithoutTraceIdAsync(this HttpResponseMessage response)
    {
        JsonObject body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))?.AsObject()
            ?? throw new InvalidOperationException("The response has no JSON object body.");
        Assert.True(body.Remove("traceId"), "The problem body has no traceId.");
        return body.ToJsonString();
    }

    /// <summary>The names of <paramref name="element"/>'s properties, in order.</summary>
    public static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];
}
