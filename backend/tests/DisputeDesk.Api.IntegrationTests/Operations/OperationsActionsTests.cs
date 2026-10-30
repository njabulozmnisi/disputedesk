using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Audit;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Contracts.Events;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Infrastructure.Persistence;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AC2 and AC7 (FR-21 to FR-23, FR-28): a mapped token with each action's role starts review, escalates and resolves.
/// Each 200 is the operations view of the dispute, equal to a following read; each writes one stored event and one
/// <c>status-changed.v1</c> outbox row through the one publish mapping, and no audit row (ad-009). The customer's
/// timeline shows each as the bank.
/// </summary>
public class OperationsActionsTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task StartReviewEscalateResolve_EachAllowed_Returns200EqualToAFollowingReadAndWritesNoActionAuditRow()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();

        foreach (string action in new[] { "start-review", "escalate", "resolve" })
        {
            HttpResponseMessage response = await factory.ActAsMappedAsync(tool, disputeId, action);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var acted = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            HttpResponseMessage read = await tool.ReadDisputeAsync(disputeId.ToString(), factory.Zitadel.Issue(roles: OperationsApi.ReadRole));
            var readBack = JsonNode.Parse(await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.True(JsonNode.DeepEquals(acted, readBack), $"{action}: {acted?.ToJsonString()} != {readBack?.ToJsonString()}");
        }

        // The actions wrote no audit row of their own and no DisputeRead: the only DisputeRead rows are the three reads'.
        List<AuditLogEntry> rows = await factory.AuditRowsAsync();
        Assert.Equal(
            [AuditResult.Succeeded, AuditResult.Succeeded, AuditResult.Succeeded],
            rows.Where(entry => entry.Action == AuditAction.DisputeRead).Select(entry => entry.Result));
        Assert.DoesNotContain(rows, entry => entry.Action is AuditAction.StartReview or AuditAction.Escalate or AuditAction.Resolve);
        Dispute row = await factory.DisputeRowAsync(disputeId);
        Assert.Equal(DisputeStatus.Resolved, row.Status);
        Assert.Equal(Outcome.Upheld, row.Outcome);
        Assert.Equal(4, row.Version);
    }

    [Fact]
    public async Task Resolve_ClockNotMicrosecondAligned_BodyStoredEventAndOutboxHoldTheSameStoredInstant()
    {
        // The stub's clock is a tick finer than Postgres keeps (TransactionsStub), so NowAsStored differs from Now.
        Assert.NotEqual(stub.Now, stub.NowAsStored);
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, disputeId, "start-review")).StatusCode);

        HttpResponseMessage response = await factory.ActAsMappedAsync(tool, disputeId, "resolve");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        JsonElement last = body.RootElement.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal("disputeResolved", last.GetProperty("eventType").GetString());
        Assert.Equal(stub.NowAsStored, last.GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(stub.NowAsStored, (await factory.StreamAsync(disputeId))[^1].OccurredAtUtc);
        using var payload = JsonDocument.Parse((await factory.OutboxRowsAsync(disputeId))[^1].Payload);
        Assert.Equal(stub.NowAsStored, payload.RootElement.GetProperty("time").GetDateTimeOffset());
        Assert.Equal(stub.NowAsStored, payload.RootElement.GetProperty("data").GetProperty("occurredAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task StartReviewEscalateResolve_EachWritesOneStatusChangedEventWithItsStoredEventIdAndNoNoteOrActorIdentity()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();

        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, disputeId, "start-review")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, disputeId, "escalate")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, disputeId, "resolve", OperationsApi.ResolveBody("rejected", "  note-marker-5c2  "))).StatusCode);

        List<StoredEventRow> stream = await factory.StreamAsync(disputeId);
        List<OutboxMessage> outbox = await factory.OutboxRowsAsync(disputeId);
        Assert.Equal([nameof(DisputeRaised), nameof(ReviewStarted), nameof(DisputeEscalated), nameof(DisputeResolved)], stream.Select(row => row.EventType));
        Assert.Equal(4, outbox.Count);
        (string From, string To, string Action, string? Outcome)[] expected =
        [
            ("submitted", "underReview", "startReview", null),
            ("underReview", "escalated", "escalate", null),
            ("escalated", "resolved", "resolve", "rejected"),
        ];
        for (int index = 0; index < expected.Length; index++)
        {
            OutboxMessage message = outbox[index + 1];
            StoredEventRow storedEvent = stream[index + 1];
            Assert.Equal(storedEvent.Id, message.Id);
            Assert.Equal(DisputeEvents.StatusChangedV1, message.EventType);
            PublishMappingTests.AssertValid(message.Payload);
            using var payload = JsonDocument.Parse(message.Payload);
            JsonElement root = payload.RootElement;
            Assert.Equal("1.0", root.GetProperty("specversion").GetString());
            Assert.Equal(storedEvent.Id.ToString(), root.GetProperty("id").GetString());
            Assert.Equal(DisputeEvents.StatusChangedV1, root.GetProperty("type").GetString());
            Assert.Equal(disputeId.ToString(), root.GetProperty("subject").GetString());
            JsonElement data = root.GetProperty("data");
            Assert.Equal(expected[index].From, data.GetProperty("fromStatus").GetString());
            Assert.Equal(expected[index].To, data.GetProperty("toStatus").GetString());
            Assert.Equal(expected[index].Action, data.GetProperty("action").GetString());
            Assert.Equal("operations", data.GetProperty("actorType").GetString());
            Assert.Equal(storedEvent.Version, data.GetProperty("disputeVersion").GetInt32());
            Assert.Equal(storedEvent.OccurredAtUtc, data.GetProperty("occurredAt").GetDateTimeOffset());
            Assert.Equal(expected[index].Outcome, data.TryGetProperty("outcome", out JsonElement outcome) ? outcome.GetString() : null);
            Assert.DoesNotContain("note-marker-5c2", message.Payload, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeZitadel.OperationsSub, message.Payload, StringComparison.Ordinal);
        }

        // The stored resolve keeps the trimmed note and the outcome for the customer and replay.
        DisputeResolved resolved = Assert.IsType<DisputeResolved>(stream[3].ToStoredEvent());
        Assert.Equal("note-marker-5c2", resolved.Note);
        Assert.Equal(Outcome.Rejected, resolved.Outcome);
        Assert.Equal(FakeZitadel.OperationsSub, resolved.ActorId);
    }

    [Fact]
    public async Task CustomerTimeline_AfterTheThreeActions_ShowsEachAsTheBankWithTheEscalationPrioritisedAndTheResolveNote()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        using HttpClient tool = factory.CreateTool();
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, disputeId, "start-review")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, disputeId, "escalate")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ActAsMappedAsync(tool, disputeId, "resolve", OperationsApi.ResolveBody("upheld", "  We've reversed this debit.  "))).StatusCode);

        HttpResponseMessage detail = await browser.GetDisputeAsync(disputeId.ToString());

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using JsonDocument body = await detail.ReadJsonAsync();
        Assert.Equal("resolved", body.RootElement.GetProperty("customerStatus").GetString());
        Assert.Equal("upheld", body.RootElement.GetProperty("outcome").GetString());
        JsonElement[] timeline = [.. body.RootElement.GetProperty("timeline").EnumerateArray()];
        Assert.Equal(
            [("raise", "submitted", "you"), ("startReview", "underReview", "theBank"), ("escalate", "underReviewPrioritised", "theBank"), ("resolve", "resolved", "theBank")],
            timeline.Select(entry => (entry.GetProperty("action").GetString(), entry.GetProperty("customerStatus").GetString(), entry.GetProperty("actor").GetString())));
        Assert.Equal("We've reversed this debit.", timeline[3].GetProperty("note").GetString());
        Assert.Equal(JsonValueKind.Null, timeline[1].GetProperty("note").ValueKind);
        Assert.Equal(JsonValueKind.Null, timeline[2].GetProperty("note").ValueKind);
        // The customer sees no operations identity anywhere.
        Assert.DoesNotContain(FakeZitadel.OperationsSub, body.RootElement.GetRawText(), StringComparison.Ordinal);
    }
}
