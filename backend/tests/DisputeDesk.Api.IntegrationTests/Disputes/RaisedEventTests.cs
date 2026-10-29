using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Events;
using DisputeDesk.Contracts.Json;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using DisputeDesk.Infrastructure.Persistence;
using Json.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AC7: the outbox row of a raise holds the <c>raised.v1</c> CloudEvent (AD-19, AD-19a) with the request's trace
/// context, and the payload and the committed sample both validate against the committed JSON Schema (NFR-TEST-4).
/// </summary>
public class RaisedEventTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private static readonly string SchemaPath = RepoFile($"contracts/events/{DisputeEvents.RaisedV1}.schema.json");
    private static readonly string SamplePath = RepoFile($"contracts/events/{DisputeEvents.RaisedV1}.sample.json");

    // Built once: the schema's $id is registered globally on the first build.
    private static readonly JsonSchema Schema = JsonSchema.FromText(File.ReadAllText(SchemaPath));

    [Fact]
    public async Task Raise_Succeeds_OutboxPayloadIsTheStructuredRaisedEventWithExactlyItsDataFields()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        HttpResponseMessage response = await browser.RaiseAsync(DisputesApi.Body(reason: "wrongAmount", expectedAmount: "80.00", note: "Charged twice the price"));
        using JsonDocument body = await response.ReadJsonAsync();
        string disputeId = body.RootElement.GetProperty("disputeId").GetString() ?? "";

        OutboxMessage outbox = await SingleOutboxMessageAsync(factory);

        using var payload = JsonDocument.Parse(outbox.Payload);
        JsonElement cloudEvent = payload.RootElement;
        Assert.Equal(
            ["specversion", "id", "source", "type", "subject", "time", "datacontenttype", "dataschema", "data"],
            cloudEvent.EnumerateObject().Select(property => property.Name));
        Assert.Equal("1.0", cloudEvent.GetProperty("specversion").GetString());
        Assert.Equal(outbox.Id.ToString(), cloudEvent.GetProperty("id").GetString());
        Assert.Equal("/disputedesk/api", cloudEvent.GetProperty("source").GetString());
        Assert.Equal("disputedesk.dispute.raised.v1", cloudEvent.GetProperty("type").GetString());
        Assert.Equal(disputeId, cloudEvent.GetProperty("subject").GetString());
        Assert.Equal("application/json", cloudEvent.GetProperty("datacontenttype").GetString());
        Assert.Equal(
            "https://github.com/njabulozmnisi/disputedesk/blob/main/contracts/events/disputedesk.dispute.raised.v1.schema.json",
            cloudEvent.GetProperty("dataschema").GetString());
        Assert.Equal(stub.NowAsStored, cloudEvent.GetProperty("time").GetDateTimeOffset());
        // RFC 3339 UTC with Z (the architecture's consistency conventions), not +00:00.
        Assert.EndsWith("Z", cloudEvent.GetProperty("time").GetString(), StringComparison.Ordinal);

        JsonElement data = cloudEvent.GetProperty("data");
        Assert.Equal(["disputeId", "reason", "status", "occurredAt", "disputeVersion"], data.EnumerateObject().Select(property => property.Name));
        Assert.Equal(disputeId, data.GetProperty("disputeId").GetString());
        Assert.Equal("wrongAmount", data.GetProperty("reason").GetString());
        Assert.Equal("submitted", data.GetProperty("status").GetString());
        Assert.Equal(stub.NowAsStored, data.GetProperty("occurredAt").GetDateTimeOffset());
        Assert.EndsWith("Z", data.GetProperty("occurredAt").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, data.GetProperty("disputeVersion").GetInt32());

        Assert.Equal(DisputeEvents.RaisedV1, outbox.EventType);
        Assert.Equal(Guid.Parse(disputeId), outbox.DisputeId);
        Assert.Equal(1, outbox.DisputeVersion);
        Assert.Equal(OutboxStatus.Pending, outbox.Status);
        Assert.Equal(0, outbox.Attempts);
        Assert.Null(outbox.SentAtUtc);
        Assert.Null(outbox.LastError);
    }

    [Fact]
    public async Task Raise_Succeeds_PayloadCarriesNoPersonalDataAmountOrNote()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        await browser.RaiseAsync(DisputesApi.Body(reason: "wrongAmount", expectedAmount: "80.00", note: "note-marker"));

        string payload = (await SingleOutboxMessageAsync(factory)).Payload;

        string[] forbidden = [TransactionsApi.CustomerA, "login-a", "ACC-A-1", "TXN-A-0029", "0011", "492181", "87.23", "80.00", "7.23", "note-marker", "Steers"];
        Assert.All(forbidden, value => Assert.DoesNotContain(value, payload, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Raise_WithTraceContext_StoresTheRequestActivitysTraceParentAndTraceState()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(DisputesApi.RaisePath, UriKind.Relative))
        {
            Content = new StringContent(DisputesApi.Body(), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", await browser.AntiforgeryTokenAsync());
        request.Headers.Add("Idempotency-Key", DisputesApi.NewKey());
        request.Headers.Add("traceparent", $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        request.Headers.Add("tracestate", "disputedesk=test");

        HttpResponseMessage response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        OutboxMessage outbox = await SingleOutboxMessageAsync(factory);
        Assert.Matches($"^00-{traceId.ToHexString()}-[0-9a-f]{{16}}-01$", outbox.TraceParent);
        Assert.Equal("disputedesk=test", outbox.TraceState);
    }

    [Fact]
    public async Task Commit_NoActivity_LeavesTraceParentAndTraceStateNull()
    {
        string database = await postgres.CreateMigratedDatabaseAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options);
        Activity.Current = null;
        var transaction = new TransactionSnapshot(
            "ACC-A-1", "TXN-A-0029", "0011", stub.Now.AddDays(-2), "Steers Braamfontein", 87.23m, "ZAR", null,
            TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);
        Dispute dispute = Dispute.Raise(
            TransactionsApi.CustomerA, transaction, everDisputed: false, Reason.Unrecognised, note: null, expectedAmount: null,
            new Actor(ActorType.Customer, "login-a"), stub.Now).Value;
        db.Set<Dispute>().Add(dispute);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        OutboxMessage outbox = await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(outbox.TraceParent);
        Assert.Null(outbox.TraceState);
    }

    [Fact]
    public async Task Raise_Succeeds_PayloadValidatesAgainstTheCommittedSchema()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        await browser.RaiseAsync(DisputesApi.Body(reason: "refundNotProcessed"));

        string payload = (await SingleOutboxMessageAsync(factory)).Payload;

        AssertValid(payload);
    }

    [Fact]
    public void CommittedSample_ValidatesAgainstTheSchemaAndStillDeserialises()
    {
        string sample = File.ReadAllText(SamplePath);

        AssertValid(sample);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { RespectRequiredConstructorParameters = true };
        DisputeDeskJson.Apply(options);
        CloudEvent<DisputeRaisedV1>? raised = JsonSerializer.Deserialize<CloudEvent<DisputeRaisedV1>>(sample, options);
        Assert.NotNull(raised);
        Assert.Equal(DisputeEvents.RaisedV1, raised.Type);
        Assert.Equal(1, raised.Data.DisputeVersion);
    }

    [Theory]
    [InlineData("status", "\"resolved\"")]
    [InlineData("reason", "\"fraud\"")]
    [InlineData("disputeVersion", "2")]
    [InlineData("disputeId", "\"not-a-uuid\"")]
    [InlineData("occurredAt", "\"2026-10-26T08:15:30.123456+00:00\"")]
    public void Schema_SampleWithABrokenDataField_IsInvalid(string field, string value)
    {
        // Proves the schema is really evaluated, formats included, and is not a schema everything passes.
        JsonNode sample = JsonNode.Parse(File.ReadAllText(SamplePath)) ?? throw new InvalidOperationException("Empty sample.");
        sample["data"]![field] = JsonNode.Parse(value);

        Assert.False(Evaluate(sample.ToJsonString()).IsValid);
    }

    [Fact]
    public void Schema_SampleWithATimeNotInUtcZ_IsInvalid()
    {
        JsonNode sample = JsonNode.Parse(File.ReadAllText(SamplePath)) ?? throw new InvalidOperationException("Empty sample.");
        sample["time"] = "2026-10-26T08:15:30.123456+00:00";

        Assert.False(Evaluate(sample.ToJsonString()).IsValid);
    }

    [Fact]
    public void DataSchema_Url_NamesTheCommittedSchemaFile()
    {
        string relative = DisputeEvents.SchemaFor(DisputeEvents.RaisedV1)["https://github.com/njabulozmnisi/disputedesk/blob/main/".Length..];

        Assert.True(File.Exists(RepoFile(relative)), $"No committed schema at {relative}.");
    }

    private static void AssertValid(string json)
    {
        EvaluationResults results = Evaluate(json);
        Assert.True(results.IsValid, JsonSerializer.Serialize(results));
    }

    private static EvaluationResults Evaluate(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Schema.Evaluate(document.RootElement, new EvaluationOptions { RequireFormatValidation = true, OutputFormat = OutputFormat.List });
    }

    private static async Task<OutboxMessage> SingleOutboxMessageAsync(ApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<OutboxMessage>().AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A file under the repository root, found from <c>fixtures/local.json</c>.</summary>
    private static string RepoFile(string relativePath)
    {
        string repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(FixturesFile.FindPath()) ?? "", ".."));
        return Path.Combine(repo, relativePath);
    }
}
