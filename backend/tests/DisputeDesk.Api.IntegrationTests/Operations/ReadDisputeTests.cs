using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// AC4, FR-30: the operations read of one dispute, past the ownership filter, with every stored event type on its
/// timeline in version order and a row of an unknown type left out (AD-13); an unknown ID is the customer's identical
/// 404, and a <c>disputeId</c> that is not a GUID is 400, after 401 and 403 (AD-8).
/// </summary>
public class ReadDisputeTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task ReadDispute_StreamOfEveryStoredEventType_ReturnsTheDisputeAndOneEntryPerKnownEventInVersionOrder()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        JsonElement raised = await browser.RaiseCreatedAsync(DisputesApi.Body(note: "Not me"));
        Guid disputeId = raised.GetProperty("disputeId").GetGuid();
        // The read maps each stored row and decides nothing, so the stream need not be a lifecycle a dispute could have.
        // Version 4 is appended before version 3, so the stored row order is not the version order.
        await factory.AppendAsync(disputeId, DisputeStatus.Resolved, Outcome.Upheld,
            At(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), 2, ActorType.Operations),
            At(new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated), 4, ActorType.Automation),
            At(new SlaWarningRaised(), 3, ActorType.Automation),
            At(new DisputeWithdrawn(DisputeStatus.Escalated, DisputeStatus.Withdrawn, "Sorted it out"), 5, ActorType.Customer),
            At(new DisputeResolved(DisputeStatus.Withdrawn, DisputeStatus.Resolved, Outcome.Upheld, "We refunded you"), 6, ActorType.Operations));
        await factory.AddRowsAsync(new StoredEventRow
        {
            Id = Guid.CreateVersion7(stub.Now),
            DisputeId = disputeId,
            Version = 7,
            EventType = "DisputeReopened",
            OccurredAtUtc = stub.NowAsStored.AddHours(7),
            ActorType = ActorType.Operations,
            ActorId = FakeZitadel.OperationsSub,
            Data = """{"note":"unknown-row"}""",
        });
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage response = await tool.ReadDisputeAsync(disputeId.ToString(), factory.Zitadel.Issue(roles: OperationsApi.ReadRole));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        JsonElement dispute = body.RootElement;
        Assert.Equal(
            ["disputeId", "status", "outcome", "reason", "transaction", "expectedAmount", "disputedAmount", "createdAt", "timeline"],
            OperationsApi.Names(dispute));
        Assert.Equal(disputeId, dispute.GetProperty("disputeId").GetGuid());
        Assert.Equal("resolved", dispute.GetProperty("status").GetString());
        Assert.Equal("upheld", dispute.GetProperty("outcome").GetString());
        Assert.Equal("unrecognised", dispute.GetProperty("reason").GetString());
        // The stored snapshot, exactly as the customer's raise returned it (story 3.3's TransactionSnapshotResponse).
        Assert.Equal(raised.GetProperty("transaction").GetRawText(), dispute.GetProperty("transaction").GetRawText());
        Assert.Equal(JsonValueKind.Null, dispute.GetProperty("expectedAmount").ValueKind);
        Assert.Equal(raised.GetProperty("disputedAmount").GetRawText(), dispute.GetProperty("disputedAmount").GetRawText());
        Assert.Equal(raised.GetProperty("createdAt").GetDateTimeOffset(), dispute.GetProperty("createdAt").GetDateTimeOffset());

        List<JsonElement> timeline = [.. dispute.GetProperty("timeline").EnumerateArray()];
        Assert.All(timeline, entry => Assert.Equal(["eventType", "actorType", "occurredAt", "fromStatus", "toStatus", "note"], OperationsApi.Names(entry)));
        Assert.Equal(
            ["disputeRaised", "reviewStarted", "slaWarningRaised", "disputeEscalated", "disputeWithdrawn", "disputeResolved"],
            timeline.Select(entry => entry.GetProperty("eventType").GetString()));
        Assert.Equal(
            ["customer", "operations", "automation", "automation", "customer", "operations"],
            timeline.Select(entry => entry.GetProperty("actorType").GetString()));
        Assert.Equal(
            [null, "submitted", null, "underReview", "escalated", "withdrawn"],
            timeline.Select(entry => entry.GetProperty("fromStatus").GetString()));
        Assert.Equal(
            ["submitted", "underReview", null, "escalated", "withdrawn", "resolved"],
            timeline.Select(entry => entry.GetProperty("toStatus").GetString()));
        // Only the bank's note meant for the customer; never a note the customer wrote.
        Assert.Equal([null, null, null, null, null, "We refunded you"], timeline.Select(entry => entry.GetProperty("note").GetString()));
        Assert.Equal(raised.GetProperty("createdAt").GetDateTimeOffset(), timeline[0].GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(
            [2, 3, 4, 5, 6],
            timeline.Skip(1).Select(entry => (int)(entry.GetProperty("occurredAt").GetDateTimeOffset() - stub.NowAsStored).TotalHours));
    }

    [Fact]
    public async Task ReadDispute_AnotherCustomersDispute_ReturnsItPastTheOwnershipFilter()
    {
        // Customer A raised it; the token belongs to no customer at all.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage response = await tool.ReadDisputeAsync(disputeId.ToString(), factory.Zitadel.Issue(roles: OperationsApi.ReadRole));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        Assert.Equal("submitted", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("outcome").ValueKind);
    }

    [Fact]
    public async Task ReadDispute_UnknownId_Returns404WithTheCustomersNotFoundBody()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        using HttpClient tool = factory.CreateTool();
        var unknown = Guid.NewGuid();

        HttpResponseMessage operations = await tool.ReadDisputeAsync(unknown.ToString(), factory.Zitadel.Issue(roles: OperationsApi.ReadRole));
        HttpResponseMessage customer = await browser.GetDisputeAsync(unknown.ToString());

        Assert.Equal(HttpStatusCode.NotFound, operations.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.NotFound), await operations.ProblemTypeAsync());
        Assert.Equal(await customer.BodyWithoutTraceIdAsync(), await operations.BodyWithoutTraceIdAsync());
    }

    [Theory]
    [InlineData("not-a-guid")]                                // wrong type
    [InlineData("12345")]                                     // wrong type, digits only
    [InlineData("0196f0a2-7c1e-7b3a-9d4e-5f6a7b8c9d0e1")]     // one character too many
    [InlineData("0196f0a2-7c1e-7b3a-9d4e-5f6a7b8c9d0")]       // one character too few
    public async Task ReadDispute_DisputeIdNotAGuid_Returns400ValidationNamingItWithoutEchoingIt(string disputeId)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage response = await tool.ReadDisputeAsync(disputeId, factory.Zitadel.Issue(roles: OperationsApi.ReadRole));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(ProblemType.For(CommonProblems.Validation), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(["disputeId"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.DoesNotContain(disputeId, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadDispute_DisputeIdOf2000Characters_Returns400Validation()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage response = await tool.ReadDisputeAsync(new string('a', 2000), factory.Zitadel.Issue(roles: OperationsApi.ReadRole));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Validation), await response.ProblemTypeAsync());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("no roles", HttpStatusCode.Forbidden)]
    public async Task ReadDispute_DisputeIdNotAGuidWithoutAValidReader_Returns401Or403Before400(string? token, HttpStatusCode expected)
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage response = await tool.ReadDisputeAsync("not-a-guid", token is null ? null : factory.Zitadel.Issue());

        Assert.Equal(expected, response.StatusCode);
    }

    private StoredEvent At(StoredEvent storedEvent, int version, ActorType actorType) => storedEvent with
    {
        Version = version,
        OccurredAtUtc = stub.NowAsStored.AddHours(version),
        ActorType = actorType,
        ActorId = actorType == ActorType.Customer ? "login-a" : FakeZitadel.OperationsSub,
    };
}
