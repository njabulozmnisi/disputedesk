using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>Calling the operations routes as a tool would: a bearer token in the <c>Authorization</c> header and no cookies.</summary>
public static class OperationsApi
{
    public const string DisputesPath = "/api/v1/operations/disputes";
    public const string ReadRole = "dispute.read";

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
