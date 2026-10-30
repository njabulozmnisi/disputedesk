using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// Story 3.9 AC1: <c>GET /api/v1/disputes</c> is one page of the customer's disputes, newest first, each a
/// <c>DisputeSummaryResponse</c> mapped like the detail from the <c>Disputes</c> read model (AD-5, AD-13).
/// </summary>
public class ListDisputesTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Fact]
    public async Task ListDisputes_OneResolvedAndOneOpen_ReturnsAPageOfSummariesMatchingTheirDetail()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        Guid resolvedId = (await browser.RaiseCreatedAsync(DisputesApi.Body())).GetProperty("disputeId").GetGuid();
        Guid openId = (await browser.RaiseCreatedAsync(DisputesApi.Body(transactionId: "TXN-A-0028", reason: "wrongAmount", expectedAmount: "200.00")))
            .GetProperty("disputeId").GetGuid();
        await factory.AppendAsync(resolvedId, DisputeStatus.Resolved, Outcome.Rejected,
            At(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), 2),
            At(new DisputeResolved(DisputeStatus.UnderReview, DisputeStatus.Resolved, Outcome.Rejected, "Not a match"), 3));

        using JsonDocument page = await browser.ListDisputesPageAsync();

        Assert.Equal(["items", "nextCursor"], page.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.Null, page.RootElement.GetProperty("nextCursor").ValueKind);
        // The fixed clock gives both raises one CreatedAtUtc, so the ID, as PostgreSQL orders it, decides.
        Assert.Equal(
            [.. new[] { resolvedId, openId }.OrderByDescending(id => id.ToString("D"), StringComparer.Ordinal)],
            DisputesApi.DisputeIds(page));
        foreach (JsonElement item in page.RootElement.GetProperty("items").EnumerateArray())
        {
            using JsonDocument detail = await (await browser.GetDisputeAsync(item.GetProperty("disputeId").GetString() ?? "")).ReadJsonAsync();
            foreach (string field in new[] { "disputeId", "customerStatus", "outcome", "reason", "transaction", "disputedAmount", "createdAt" })
            {
                Assert.Equal(detail.RootElement.GetProperty(field).GetRawText(), item.GetProperty(field).GetRawText());
            }
        }

        JsonElement resolved = Item(page, resolvedId);
        Assert.Equal("resolved", resolved.GetProperty("customerStatus").GetString());
        Assert.Equal("rejected", resolved.GetProperty("outcome").GetString());
        JsonElement open = Item(page, openId);
        Assert.Equal("submitted", open.GetProperty("customerStatus").GetString());
        Assert.Equal(JsonValueKind.Null, open.GetProperty("outcome").ValueKind);
        Assert.Equal("wrongAmount", open.GetProperty("reason").GetString());
        Assert.Equal("""{"amount":"59.70","currency":"ZAR"}""", open.GetProperty("disputedAmount").GetRawText());
    }

    [Fact]
    public async Task ListDisputes_DisputesRaisedAtDifferentInstants_ComeNewestFirst()
    {
        await using ApiFactory factory = await DisputesApi.StartWithoutCoreBankingAsync(postgres);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        SeededDispute oldest = await factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-1", DisputesApi.SeedInstant.AddHours(-2));
        SeededDispute newest = await factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-2", DisputesApi.SeedInstant);
        SeededDispute middle = await factory.SeedDisputeAsync(TransactionsApi.CustomerA, "TXN-3", DisputesApi.SeedInstant.AddHours(-1));

        using JsonDocument page = await browser.ListDisputesPageAsync();

        Assert.Equal([newest.Id, middle.Id, oldest.Id], DisputesApi.DisputeIds(page));
    }

    [Fact]
    public async Task ListDisputes_NoDisputes_ReturnsAnEmptyPageWithANullCursor()
    {
        await using ApiFactory factory = await DisputesApi.StartWithoutCoreBankingAsync(postgres);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        using JsonDocument page = await browser.ListDisputesPageAsync();

        Assert.Equal(0, page.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, page.RootElement.GetProperty("nextCursor").ValueKind);
    }

    private static JsonElement Item(JsonDocument page, Guid disputeId) =>
        page.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("disputeId").GetGuid() == disputeId);

    private StoredEvent At(StoredEvent storedEvent, int version) => storedEvent with
    {
        Version = version,
        OccurredAtUtc = stub.NowAsStored.AddHours(version),
        ActorType = ActorType.Operations,
        ActorId = "ops-sub",
    };
}
