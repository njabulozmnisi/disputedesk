using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Contracts.Events;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using DisputeDesk.Infrastructure.Persistence;
using Json.Schema;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// The one exhaustive publish mapping (AD-14, AD-19): every stored event type maps to its integration event or to
/// nothing, an unmapped type throws, and no payload carries what a stored event holds privately.
/// </summary>
public class PublishMappingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 41, 12, TimeSpan.Zero);
    private static readonly Guid DisputeId = Guid.CreateVersion7(Now.AddDays(-1));
    private static readonly Guid EventId = Guid.CreateVersion7(Now);

    private static readonly string SchemaPath = RepoFile($"contracts/events/{DisputeEvents.StatusChangedV1}.schema.json");
    private static readonly string SamplePath = RepoFile($"contracts/events/{DisputeEvents.StatusChangedV1}.sample.json");

    // Built once: the schema's $id is registered globally on the first build.
    private static readonly JsonSchema StatusChangedSchema = JsonSchema.FromText(File.ReadAllText(SchemaPath));

    /// <summary>One stored event of each type, built here, with the integration event type it maps to.</summary>
    public static TheoryData<StoredEvent, string?> OneOfEachType => new()
    {
        { Raised(), DisputeEvents.RaisedV1 },
        { At(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), 2, ActorType.Operations, "ops-marker"), DisputeEvents.StatusChangedV1 },
        { At(new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated), 3, ActorType.Automation, "worker:sla-sweep"), DisputeEvents.StatusChangedV1 },
        { At(new DisputeResolved(DisputeStatus.Escalated, DisputeStatus.Resolved, Outcome.Rejected, "note-marker"), 4, ActorType.Operations, "ops-marker"), DisputeEvents.StatusChangedV1 },
        { At(new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, "note-marker"), 2, ActorType.Customer, "login-marker"), DisputeEvents.StatusChangedV1 },
        { At(new SlaWarningRaised(), 2, ActorType.Automation, "worker:sla-sweep"), null },
    };

    [Fact]
    public void OneOfEachType_CoversEveryStoredEventTypeInTheDomain()
    {
        // A new type in the Domain fails here until it is added above, where the mapping then has to name it.
        IEnumerable<string> domainTypes = typeof(StoredEvent).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(StoredEvent))).Select(type => type.Name);

        Assert.Equal(
            domainTypes.Order(StringComparer.Ordinal),
            OneOfEachType.Select(row => row.Data.Item1.GetType().Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(OneOfEachType))]
    public void OutboxMessage_EachType_MapsToItsIntegrationEventOrNothing(StoredEvent storedEvent, string? expectedType)
    {
        OutboxMessage? message = CommitRows.OutboxMessage(EventId, storedEvent, activity: null);

        Assert.Equal(expectedType, message?.EventType);
        if (message is null)
        {
            return;
        }

        Assert.Equal(EventId, message.Id);
        Assert.Equal(DisputeId, message.DisputeId);
        Assert.Equal(storedEvent.Version, message.DisputeVersion);
        Assert.Equal(storedEvent.OccurredAtUtc, message.CreatedAtUtc);
        using var payload = JsonDocument.Parse(message.Payload);
        Assert.Equal(EventId.ToString(), payload.RootElement.GetProperty("id").GetString());
        Assert.Equal(expectedType, payload.RootElement.GetProperty("type").GetString());
        Assert.Equal(DisputeEvents.SchemaFor(message.EventType), payload.RootElement.GetProperty("dataschema").GetString());
    }

    [Theory]
    [MemberData(nameof(OneOfEachType))]
    public void OutboxMessage_EachType_PayloadHoldsNoPrivateField(StoredEvent storedEvent, string? expectedType)
    {
        string payload = CommitRows.OutboxMessage(EventId, storedEvent, activity: null)?.Payload ?? "";

        // Markers that cannot occur in a UUID or an instant by chance.
        string[] forbidden = ["CUS-000001", "ACC-A-1", "TXN-A-0029", "492181******4417", "87.23", "80.00", "7.23", "note-marker", "ops-marker", "login-marker", "worker:", "Steers"];
        Assert.All(forbidden, value => Assert.DoesNotContain(value, payload, StringComparison.Ordinal));
        Assert.True(expectedType is not null || payload.Length == 0);
    }

    [Fact]
    public void OutboxMessage_Raise_HoldsExactlyTheRaisedDataFields()
    {
        string payload = CommitRows.OutboxMessage(EventId, Raised(), activity: null)?.Payload ?? "";

        using var document = JsonDocument.Parse(payload);
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(["disputeId", "reason", "status", "occurredAt", "disputeVersion"], data.EnumerateObject().Select(property => property.Name));
        Assert.Equal("wrongAmount", data.GetProperty("reason").GetString());
        Assert.Equal("submitted", data.GetProperty("status").GetString());
        Assert.Equal(1, data.GetProperty("disputeVersion").GetInt32());
    }

    [Theory]
    [InlineData(0, "submitted", "underReview", "startReview", "operations", null)]
    [InlineData(1, "underReview", "escalated", "escalate", "automation", null)]
    [InlineData(2, "escalated", "resolved", "resolve", "operations", "rejected")]
    [InlineData(3, "submitted", "withdrawn", "withdraw", "customer", null)]
    public void OutboxMessage_StatusChange_HoldsTheAd19FieldsAndValidatesAgainstTheSchema(
        int index, string from, string to, string action, string actorType, string? outcome)
    {
        StoredEvent storedEvent = StatusChanges()[index];

        string payload = CommitRows.OutboxMessage(EventId, storedEvent, activity: null)?.Payload ?? "";

        using var document = JsonDocument.Parse(payload);
        JsonElement data = document.RootElement.GetProperty("data");
        string[] expectedFields = outcome is null
            ? ["disputeId", "fromStatus", "toStatus", "action", "actorType", "occurredAt", "disputeVersion"]
            : ["disputeId", "fromStatus", "toStatus", "action", "actorType", "outcome", "occurredAt", "disputeVersion"];
        Assert.Equal(expectedFields, data.EnumerateObject().Select(property => property.Name));
        Assert.Equal(DisputeId.ToString(), data.GetProperty("disputeId").GetString());
        Assert.Equal(from, data.GetProperty("fromStatus").GetString());
        Assert.Equal(to, data.GetProperty("toStatus").GetString());
        Assert.Equal(action, data.GetProperty("action").GetString());
        Assert.Equal(actorType, data.GetProperty("actorType").GetString());
        Assert.Equal(storedEvent.Version, data.GetProperty("disputeVersion").GetInt32());
        Assert.EndsWith("Z", data.GetProperty("occurredAt").GetString(), StringComparison.Ordinal);
        if (outcome is not null)
        {
            Assert.Equal(outcome, data.GetProperty("outcome").GetString());
        }

        AssertValid(payload);
    }

    [Fact]
    public void OutboxMessage_TypeTheMappingDoesNotName_Throws()
    {
        StoredEvent unmapped = At(new UnmappedStoredEvent(), 2, ActorType.Customer, "login-marker");

        Assert.Throws<ArgumentOutOfRangeException>(() => CommitRows.OutboxMessage(EventId, unmapped, activity: null));
    }

    [Fact]
    public void CommittedStatusChangedSample_ValidatesAgainstTheSchemaAndStillDeserialises()
    {
        string sample = File.ReadAllText(SamplePath);

        AssertValid(sample);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { RespectRequiredConstructorParameters = true };
        Contracts.Json.DisputeDeskJson.Apply(options);
        CloudEvent<DisputeStatusChangedV1>? changed = JsonSerializer.Deserialize<CloudEvent<DisputeStatusChangedV1>>(sample, options);
        Assert.NotNull(changed);
        Assert.Equal(DisputeEvents.StatusChangedV1, changed.Type);
        Assert.Equal(Contracts.Disputes.Outcome.Upheld, changed.Data.Outcome);
    }

    [Theory]
    [InlineData("action", "\"raise\"")]
    [InlineData("toStatus", "\"submitted\"")]
    [InlineData("actorType", "\"agent\"")]
    [InlineData("outcome", "\"partly\"")]
    [InlineData("disputeVersion", "1")]
    public void StatusChangedSchema_SampleWithABrokenDataField_IsInvalid(string field, string value)
    {
        // Proves the schema is really evaluated and is not a schema everything passes.
        System.Text.Json.Nodes.JsonNode sample = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(SamplePath))
            ?? throw new InvalidOperationException("Empty sample.");
        sample["data"]![field] = System.Text.Json.Nodes.JsonNode.Parse(value);

        Assert.False(Evaluate(sample.ToJsonString()).IsValid);
    }

    [Fact]
    public void StatusChangedSchema_OutcomeOnAnotherAction_IsInvalid()
    {
        System.Text.Json.Nodes.JsonNode sample = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(SamplePath))
            ?? throw new InvalidOperationException("Empty sample.");
        sample["data"]!["action"] = "escalate";

        Assert.False(Evaluate(sample.ToJsonString()).IsValid);
    }

    private static DisputeRaised Raised()
    {
        var transaction = new TransactionSnapshot(
            "ACC-A-1", "TXN-A-0029", "0011", Now.AddDays(-2), "Steers Braamfontein", 87.23m, "ZAR", "492181******4417",
            TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);
        return At(
            new DisputeRaised("CUS-000001", transaction, Reason.WrongAmount, "note-marker", 80.00m, 7.23m, DisputeStatus.Submitted),
            1, ActorType.Customer, "login-marker");
    }

    private static StoredEvent[] StatusChanges() =>
        [.. OneOfEachType.Select(row => row.Data.Item1).Where(storedEvent => storedEvent is not (DisputeRaised or SlaWarningRaised))];

    private static TEvent At<TEvent>(TEvent storedEvent, int version, ActorType actorType, string actorId)
        where TEvent : StoredEvent =>
        storedEvent with { DisputeId = DisputeId, Version = version, OccurredAtUtc = Now, ActorType = actorType, ActorId = actorId };

    /// <summary>Asserts <paramref name="json"/> is valid against the committed status-changed schema, built once here.</summary>
    internal static void AssertValid(string json)
    {
        EvaluationResults results = Evaluate(json);
        Assert.True(results.IsValid, JsonSerializer.Serialize(results));
    }

    private static EvaluationResults Evaluate(string json)
    {
        using var document = JsonDocument.Parse(json);
        return StatusChangedSchema.Evaluate(
            document.RootElement, new EvaluationOptions { RequireFormatValidation = true, OutputFormat = OutputFormat.List });
    }

    /// <summary>A file under the repository root, found from <c>fixtures/local.json</c>.</summary>
    private static string RepoFile(string relativePath)
    {
        string repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(FixturesFile.FindPath()) ?? "", ".."));
        return Path.Combine(repo, relativePath);
    }

    /// <summary>A stored event type the publish mapping does not name.</summary>
    private sealed record UnmappedStoredEvent : StoredEvent;
}
