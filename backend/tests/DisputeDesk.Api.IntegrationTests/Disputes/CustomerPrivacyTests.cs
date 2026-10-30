using System.Diagnostics;
using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Json;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AC2, AD-13 (Customer wire view; OWASP Authorization Cheat Sheet, deny by default): the detail of a dispute that
/// operations and automation acted on, escalated and with an SLA warning in its stream, shows the customer only the
/// allowed fields, and none of the private values of its <c>Disputes</c> row or its <c>StoredEvents</c> rows.
/// </summary>
public class CustomerPrivacyTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private const string LoginSub = "login-a";
    private const string OperationsSub = "ops-sub-7f3c";

    [Fact]
    public async Task GetDispute_EveryObject_HasExactlyTheAllowedPropertyNames()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync(LoginSub, TransactionsApi.CustomerA);
        Guid disputeId = await RaiseActedOnDisputeAsync(factory, browser);

        using JsonDocument body = await (await browser.GetDisputeAsync(disputeId.ToString())).ReadJsonAsync();

        JsonElement dispute = body.RootElement;
        Assert.Equal(
            ["disputeId", "customerStatus", "outcome", "reason", "note", "transaction", "expectedAmount", "disputedAmount", "createdAt", "timeline"],
            Names(dispute));
        Assert.Equal(
            ["transactionId", "accountRef", "accountLast4", "transactionDate", "description", "amount", "maskedCard"],
            Names(dispute.GetProperty("transaction")));
        string[] money = ["amount", "currency"];
        Assert.Equal(money, Names(dispute.GetProperty("transaction").GetProperty("amount")));
        Assert.Equal(money, Names(dispute.GetProperty("expectedAmount")));
        Assert.Equal(money, Names(dispute.GetProperty("disputedAmount")));
        List<JsonElement> timeline = [.. dispute.GetProperty("timeline").EnumerateArray()];
        Assert.Equal(3, timeline.Count);
        Assert.All(timeline, entry => Assert.Equal(["action", "customerStatus", "actor", "occurredAt", "note"], Names(entry)));
    }

    [Fact]
    public async Task GetDispute_EveryStringValue_HoldsNoActorTypeInternalStatusIdentitySlaOrTraceValue()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync(LoginSub, TransactionsApi.CustomerA);
        Guid disputeId = await RaiseActedOnDisputeAsync(factory, browser);
        (string customerId, string slaDueAt, string[] actorIds, string[] traceIds) = await PrivateValuesAsync(factory, disputeId);
        Assert.Equal([LoginSub, "worker:seeder", "worker:sla-sweep", OperationsSub], actorIds);
        Assert.Equal(4, traceIds.Length);

        HttpResponseMessage response = await browser.GetDisputeAsync(disputeId.ToString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument body = await response.ReadJsonAsync();
        List<string> values = [.. StringValues(body.RootElement)];
        string[] forbiddenValues = ["customer", "operations", "automation", "escalated"];
        Assert.All(values, value => Assert.DoesNotContain(value, forbiddenValues));
        string[] forbiddenParts = [customerId, slaDueAt, .. actorIds, .. traceIds, nameof(SlaWarningRaised), "slaWarning"];
        Assert.All(forbiddenParts, part => Assert.DoesNotContain(part, raw, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("underReviewPrioritised", body.RootElement.GetProperty("customerStatus").GetString());
        Assert.Equal(
            ["you", "theBank", "theBank"],
            body.RootElement.GetProperty("timeline").EnumerateArray().Select(entry => entry.GetProperty("actor").GetString()));
    }

    [Fact]
    public async Task ListDisputes_EveryObject_HasExactlyTheAllowedPropertyNames()
    {
        // Story 3.9 AC5: the page, each summary, and the snapshot and money objects named as in the detail.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync(LoginSub, TransactionsApi.CustomerA);
        await RaiseActedOnDisputeAsync(factory, browser);

        using JsonDocument page = await browser.ListDisputesPageAsync();

        Assert.Equal(["items", "nextCursor"], Names(page.RootElement));
        JsonElement item = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(["disputeId", "customerStatus", "outcome", "reason", "transaction", "disputedAmount", "createdAt"], Names(item));
        Assert.Equal(
            ["transactionId", "accountRef", "accountLast4", "transactionDate", "description", "amount", "maskedCard"],
            Names(item.GetProperty("transaction")));
        string[] money = ["amount", "currency"];
        Assert.Equal(money, Names(item.GetProperty("transaction").GetProperty("amount")));
        Assert.Equal(money, Names(item.GetProperty("disputedAmount")));
    }

    [Fact]
    public async Task ListDisputes_EveryStringValue_HoldsNoActorTypeInternalStatusIdentitySlaOrTraceValue()
    {
        // Story 3.9 AC5: the value scan of the detail, over list pages.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync(LoginSub, TransactionsApi.CustomerA);
        Guid disputeId = await RaiseActedOnDisputeAsync(factory, browser);
        (string customerId, string slaDueAt, string[] actorIds, string[] traceIds) = await PrivateValuesAsync(factory, disputeId);

        HttpResponseMessage response = await browser.ListDisputesAsync("limit=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument page = await response.ReadJsonAsync();
        string[] forbiddenValues = ["customer", "operations", "automation", "escalated"];
        Assert.All([.. StringValues(page.RootElement)], value => Assert.DoesNotContain(value, forbiddenValues));
        string[] forbiddenParts = [customerId, slaDueAt, .. actorIds, .. traceIds, nameof(SlaWarningRaised), "slaWarning", "CustomerId", "SlaDueAtUtc", "ActorId", "TraceId"];
        Assert.All(forbiddenParts, part => Assert.DoesNotContain(part, raw, StringComparison.OrdinalIgnoreCase));
        JsonElement item = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("underReviewPrioritised", item.GetProperty("customerStatus").GetString());
    }

    /// <summary>
    /// A wrong-amount dispute (so every money object is present) raised by the customer, then started by the seeder,
    /// warned by the SLA sweep and escalated by operations, each with a trace ID of its own.
    /// </summary>
    private async Task<Guid> RaiseActedOnDisputeAsync(ApiFactory factory, HttpClient browser)
    {
        JsonElement created = await browser.RaiseCreatedAsync(DisputesApi.Body(reason: "wrongAmount", expectedAmount: "80.00", note: "Not me"));
        Guid disputeId = created.GetProperty("disputeId").GetGuid();
        await factory.AppendAsync(disputeId, DisputeStatus.Escalated, outcome: null,
            At(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), 2, ActorType.Automation, "worker:seeder"),
            At(new SlaWarningRaised(), 3, ActorType.Automation, "worker:sla-sweep"),
            At(new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated), 4, ActorType.Operations, OperationsSub));
        return disputeId;
    }

    /// <summary>The private values of the dispute's <c>Disputes</c> row and <c>StoredEvents</c> rows, the due date as the wire writes it.</summary>
    private static async Task<(string CustomerId, string SlaDueAt, string[] ActorIds, string[] TraceIds)> PrivateValuesAsync(ApiFactory factory, Guid disputeId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Dispute dispute = await db.Set<Dispute>().IgnoreQueryFilters().SingleAsync(row => row.Id == disputeId, cancellationToken);
        List<StoredEventRow> rows = await db.Set<StoredEventRow>().IgnoreQueryFilters()
            .Where(row => row.DisputeId == disputeId).OrderBy(row => row.Version).ToListAsync(cancellationToken);
        string slaDueAt = JsonSerializer.Serialize(dispute.SlaDueAtUtc, DisputeDeskJson.Options).Trim('"');
        return (dispute.CustomerId, slaDueAt, [.. rows.Select(row => row.ActorId)], [.. rows.Select(row => row.TraceId).OfType<string>()]);
    }

    private StoredEvent At(StoredEvent storedEvent, int version, ActorType actorType, string actorId) => storedEvent with
    {
        Version = version,
        OccurredAtUtc = stub.NowAsStored.AddHours(version),
        ActorType = actorType,
        ActorId = actorId,
        TraceId = ActivityTraceId.CreateRandom().ToHexString(),
    };

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    private static IEnumerable<string> StringValues(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => StringValues(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(StringValues),
        JsonValueKind.String => [element.GetString() ?? ""],
        _ => [],
    };
}
