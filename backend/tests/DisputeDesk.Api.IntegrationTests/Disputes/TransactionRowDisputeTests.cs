using System.Net;
using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>AC10: a transaction with a dispute, in any status, shows it on its row and is not disputable.</summary>
public class TransactionRowDisputeTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    [Theory]
    [InlineData(DisputeStatus.Submitted, "submitted")]
    [InlineData(DisputeStatus.UnderReview, "underReview")]
    [InlineData(DisputeStatus.Escalated, "underReviewPrioritised")]
    [InlineData(DisputeStatus.Resolved, "resolved")]
    [InlineData(DisputeStatus.Withdrawn, "withdrawn")]
    public async Task ListTransactions_RowWithADisputeInAnyStatus_CarriesItAndIsAlreadyDisputed(DisputeStatus status, string customerStatus)
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        using JsonDocument raised = await (await browser.RaiseAsync(DisputesApi.Body())).ReadJsonAsync();
        await factory.SetStatusAsync(status);

        using JsonDocument page = await ListAsync(browser);

        JsonElement row = Row(page, DisputesApi.Disputable);
        Assert.Equal(raised.RootElement.GetProperty("disputeId").GetString(), row.GetProperty("disputeId").GetString());
        Assert.Equal(customerStatus, row.GetProperty("customerStatus").GetString());
        Assert.False(row.GetProperty("disputable").GetBoolean());
        Assert.Equal("alreadyDisputed", row.GetProperty("notDisputableReason").GetString());
    }

    [Fact]
    public async Task ListTransactions_RowsWithoutADispute_HaveBothFieldsNull()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient browser = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);
        await browser.RaiseAsync(DisputesApi.Body());

        using JsonDocument page = await ListAsync(browser);

        IEnumerable<JsonElement> others = page.RootElement.GetProperty("items").EnumerateArray()
            .Where(row => row.GetProperty("transactionId").GetString() != DisputesApi.Disputable);
        Assert.All(others, row =>
        {
            Assert.Equal(JsonValueKind.Null, row.GetProperty("disputeId").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("customerStatus").ValueKind);
        });
        Assert.True(Row(page, "TXN-A-0028").GetProperty("disputable").GetBoolean());
    }

    [Fact]
    public async Task ListTransactions_AnotherCustomersDispute_NeverShowsOnTheirRows()
    {
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        using HttpClient customerB = await factory.SignInLinkedAsync("login-b", TransactionsApi.CustomerB);
        await customerB.RaiseAsync(DisputesApi.Body("TXN-B-0027", "ACC-B-1"));
        using HttpClient customerA = await factory.SignInLinkedAsync("login-a", TransactionsApi.CustomerA);

        using JsonDocument page = await ListAsync(customerA);

        Assert.All(page.RootElement.GetProperty("items").EnumerateArray(),
            row => Assert.Equal(JsonValueKind.Null, row.GetProperty("disputeId").ValueKind));
    }

    private static async Task<JsonDocument> ListAsync(HttpClient browser)
    {
        HttpResponseMessage response = await browser.GetPathAsync("/api/v1/transactions?limit=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static JsonElement Row(JsonDocument page, string transactionId) =>
        page.RootElement.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("transactionId").GetString() == transactionId);
}
