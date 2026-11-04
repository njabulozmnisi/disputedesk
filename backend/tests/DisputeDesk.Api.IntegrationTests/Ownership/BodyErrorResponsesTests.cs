using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Operations;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Contracts.Problems;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Ownership;

/// <summary>
/// FR-24, AD-8, NFR-SEC-6: the built-in validation answers a body error before the lookup, so an
/// unknown ID, another customer's dispute and a dispute the caller may act on get the same 400 <c>validation</c> body
/// apart from <c>traceId</c>, naming the field and never echoing its value, and nothing is written.
/// </summary>
public class BodyErrorResponsesTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private const string Marker = "echo-marker-7f3 ";

    [Fact]
    public async Task Withdraw_NoteTooLongOnAnUnknownIdAnotherCustomersAndTheOwnersDispute_GetTheSame400AndWriteNothing()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid theirs = (await factory.DisputeInAsync(DisputeStatus.Submitted, TransactionsApi.CustomerB, "TXN-B-0027")).Id;
        Guid own = (await factory.DisputeInAsync(DisputeStatus.Submitted, transactionId: "TXN-A-0027")).Id;
        var unknown = Guid.CreateVersion7();
        using HttpClient customerA = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        // 32 markers are 512 units, 511 after trimming: over the 500-unit limit.
        string note = string.Concat(Enumerable.Repeat(Marker, 32));
        List<string> before = [await RowsOfAsync(factory, theirs), await RowsOfAsync(factory, own)];

        List<HttpResponseMessage> responses = [];
        foreach (Guid disputeId in new[] { unknown, theirs, own })
        {
            responses.Add(await customerA.WithdrawAsync(disputeId.ToString(), DisputesApi.WithdrawBody(note)));
        }

        await AssertSameValidationProblemAsync(responses, "note");
        await AssertNothingWrittenAsync(factory, unknown);
        Assert.Equal(before, [await RowsOfAsync(factory, theirs), await RowsOfAsync(factory, own)]);
    }

    [Theory]
    [InlineData("""{"note":"Refunded"}""")]
    [InlineData("""{"outcome":null,"note":"Refunded"}""")]
    public async Task Resolve_NoOutcomeOnAnUnknownIdAndADisputeTheCallerMayResolve_GetTheSame400AndWriteNothing(string json)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid underReview = (await factory.DisputeInAsync(DisputeStatus.UnderReview)).Id;
        var unknown = Guid.CreateVersion7();
        using HttpClient tool = factory.CreateTool();
        string token = factory.Zitadel.Issue(roles: OperationsApi.ResolveRole);
        string before = await RowsOfAsync(factory, underReview);

        List<HttpResponseMessage> responses =
        [
            await tool.ActAsync(unknown.ToString(), "resolve", token, json),
            await tool.ActAsync(underReview.ToString(), "resolve", token, json),
        ];

        await AssertSameValidationProblemAsync(responses, "outcome");
        await AssertNothingWrittenAsync(factory, unknown);
        Assert.Equal(before, await RowsOfAsync(factory, underReview));
    }

    /// <summary>Each response is 400 <c>validation</c> naming only <paramref name="field"/>, all with one body bar <c>traceId</c>, none echoing the marker.</summary>
    private static async Task AssertSameValidationProblemAsync(List<HttpResponseMessage> responses, string field)
    {
        List<string> bodies = [];
        foreach (HttpResponseMessage response in responses)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            bodies.Add(await response.BodyWithoutTraceIdAsync());
        }

        Assert.All(bodies, body => Assert.Equal(bodies[0], body));
        using var problem = JsonDocument.Parse(bodies[0]);
        Assert.Equal(ProblemType.For(CommonProblems.Validation), problem.RootElement.GetProperty("type").GetString());
        Assert.Equal([field], problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.DoesNotContain(Marker.Trim(), bodies[0], StringComparison.Ordinal);
    }

    /// <summary>No dispute audit row at all, and no stored event or outbox row for the unknown ID.</summary>
    private static async Task AssertNothingWrittenAsync(ApiFactory factory, Guid unknown)
    {
        Assert.Empty(await factory.DisputeAuditRowsAsync());
        Assert.Empty(await factory.StreamAsync(unknown));
        Assert.Empty(await factory.OutboxRowsAsync(unknown));
    }

    /// <summary>The dispute's status and version, and the IDs of its stored events and outbox rows, as one comparable line.</summary>
    private static async Task<string> RowsOfAsync(ApiFactory factory, Guid disputeId)
    {
        Dispute row = await factory.DisputeRowAsync(disputeId);
        IEnumerable<Guid> storedEvents = (await factory.StreamAsync(disputeId)).Select(storedEvent => storedEvent.Id);
        IEnumerable<Guid> outbox = (await factory.OutboxRowsAsync(disputeId)).Select(message => message.Id);
        return $"{row.Status} v{row.Version} events {string.Join(',', storedEvents)} outbox {string.Join(',', outbox)}";
    }
}
