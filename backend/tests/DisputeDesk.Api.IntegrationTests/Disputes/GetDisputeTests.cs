using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AC1 and AC7: the detail of a dispute, its timeline from the stored events in version order, its outcome, the raise's
/// <c>Location</c> serving it, a malformed ID, and the detail while core banking is down.
/// </summary>
public class GetDisputeTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task GetDispute_TheRaisesLocation_ServesTheSameBodyAsTheRaise()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        HttpResponseMessage raised = await browser.RaiseAsync(DisputesApi.Body(note: "Not me"));
        Assert.Equal(HttpStatusCode.Created, raised.StatusCode);
        string created = await raised.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Uri location = raised.Headers.Location ?? throw new InvalidOperationException("The raise gave no Location.");

        HttpResponseMessage response = await browser.GetPathAsync(location.ToString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The same mapping as the 201 (CustomerView), from the stored row and stream: the same bytes.
        Assert.Equal(created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using JsonDocument body = await response.ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("outcome").ValueKind);
    }

    [Fact]
    public async Task GetDispute_SeveralEntries_ReturnsTheTimelineInVersionOrderOldestFirst()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        // Version 3 is inserted before version 2, so the stored row order is not the version order.
        await factory.AppendAsync(disputeId, DisputeStatus.Escalated, outcome: null,
            At(new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated), 3, ActorType.Automation, "worker:sla-sweep"));
        await factory.AppendAsync(disputeId, DisputeStatus.Escalated, outcome: null,
            At(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), 2, ActorType.Operations, "ops-sub"));
        // The read model is at the stream's highest version, as an action leaves it, though version 2 was appended last.
        Assert.Equal(3, await factory.DisputeVersionAsync(disputeId));

        HttpResponseMessage response = await browser.GetDisputeAsync(disputeId.ToString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        JsonElement dispute = body.RootElement;
        Assert.Equal("underReviewPrioritised", dispute.GetProperty("customerStatus").GetString());
        Assert.Equal(JsonValueKind.Null, dispute.GetProperty("outcome").ValueKind);
        List<JsonElement> timeline = [.. dispute.GetProperty("timeline").EnumerateArray()];
        Assert.Equal(["raise", "startReview", "escalate"], timeline.Select(entry => entry.GetProperty("action").GetString()));
        Assert.Equal(["submitted", "underReview", "underReviewPrioritised"], timeline.Select(entry => entry.GetProperty("customerStatus").GetString()));
        Assert.Equal(["you", "theBank", "theBank"], timeline.Select(entry => entry.GetProperty("actor").GetString()));
        Assert.Equal(stub.NowAsStored.AddHours(2), timeline[1].GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(stub.NowAsStored.AddHours(3), timeline[2].GetProperty("occurredAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(Outcome.Upheld, "upheld")]
    [InlineData(Outcome.Rejected, "rejected")]
    public async Task GetDispute_Resolved_ReturnsItsOutcomeAndTheResolveEntryWithItsNote(Outcome outcome, string wireOutcome)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid disputeId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        await factory.AppendAsync(disputeId, DisputeStatus.Resolved, outcome,
            At(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), 2, ActorType.Operations, "ops-sub"),
            At(new DisputeResolved(DisputeStatus.UnderReview, DisputeStatus.Resolved, outcome, "We looked into it"), 3, ActorType.Operations, "ops-sub"));

        HttpResponseMessage response = await browser.GetDisputeAsync(disputeId.ToString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        Assert.Equal("resolved", body.RootElement.GetProperty("customerStatus").GetString());
        Assert.Equal(wireOutcome, body.RootElement.GetProperty("outcome").GetString());
        JsonElement resolve = body.RootElement.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal("resolve", resolve.GetProperty("action").GetString());
        Assert.Equal("resolved", resolve.GetProperty("customerStatus").GetString());
        Assert.Equal("theBank", resolve.GetProperty("actor").GetString());
        Assert.Equal("We looked into it", resolve.GetProperty("note").GetString());
    }

    [Theory]
    [InlineData("not-a-guid")]  // wrong type
    [InlineData("12345")]       // wrong type, digits only
    [InlineData("0196f0a2-7c1e-7b3a-9d4e-5f6a7b8c9d0e1")] // one character too many
    [InlineData("0196f0a2-7c1e-7b3a-9d4e-5f6a7b8c9d0")]   // one character too few
    public async Task GetDispute_DisputeIdNotAGuid_Returns400ValidationWithoutEchoingIt(string disputeId)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.GetDisputeAsync(disputeId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Validation), await response.ProblemTypeAsync());
        Assert.DoesNotContain(disputeId, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDispute_EmptyGuid_Returns404NotFound()
    {
        // The all-zero GUID binds, and no dispute has it: not found, like any unknown ID.
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.GetDisputeAsync(Guid.Empty.ToString());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.NotFound), await response.ProblemTypeAsync());
    }

    [Fact]
    public async Task GetDispute_DisputeIdOf2000Characters_Returns400Validation()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        HttpResponseMessage response = await browser.GetDisputeAsync(new string('a', 2000));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Validation), await response.ProblemTypeAsync());
    }

    [Fact]
    public async Task GetDispute_CoreBankingDownAfterTheRaise_Returns200FromTheStoredSnapshot()
    {
        // A core-banking stub of this test's own, so stopping it leaves the other tests' stub running.
        await using var coreBanking = new TransactionsStub();
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, coreBanking);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        JsonElement created = await browser.RaiseCreatedAsync(DisputesApi.Body());
        await coreBanking.DisposeAsync();

        HttpResponseMessage response = await browser.GetDisputeAsync(created.GetProperty("disputeId").GetString() ?? "");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await response.ReadJsonAsync();
        Assert.Equal(created.GetProperty("transaction").GetRawText(), body.RootElement.GetProperty("transaction").GetRawText());
    }

    private StoredEvent At(StoredEvent storedEvent, int version, ActorType actorType, string actorId) =>
        storedEvent with { Version = version, OccurredAtUtc = stub.NowAsStored.AddHours(version), ActorType = actorType, ActorId = actorId };
}
