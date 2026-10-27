using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>The core-banking client against the real stub and fixtures: matching (AC1) and own transactions only (AC2).</summary>
public sealed class CoreBankingClientTests(CoreBankingStubFactory stub) : IClassFixture<CoreBankingStubFactory>
{
    private const string CustomerA = "CUS-000001";
    private const string CustomerAIdNumber = "9001015000085";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MatchCustomerAsync_IdNumberAndAccountNumberOnRecord_ReturnsCustomerIdAndCellphone()
    {
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<CustomerMatch> result = await client.MatchCustomerAsync(CustomerAIdNumber, "1000000012", CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(new CustomerMatch(CustomerA, "+27820000001"), result.Value);
    }

    [Theory]
    [InlineData(CustomerAIdNumber, "1000000021")] // customer A's ID number with customer B's account number
    [InlineData("8001015009087", "1000000011")]  // an ID number the bank does not hold
    public async Task MatchCustomerAsync_NoCustomerHoldsBoth_ReturnsNotFound(string idNumber, string accountNumber)
    {
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<CustomerMatch> result = await client.MatchCustomerAsync(idNumber, accountNumber, CancellationToken);

        Assert.Equal(CoreBankingErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task MatchCustomerAsync_AnyCall_SendsIdNumberOnlyInTheBodyAndNeverLogsIt()
    {
        var logs = new LogCapture();
        var recorder = new RequestRecorder();
        await using ServiceProvider services = CoreBankingServices.Build(
            stub.BaseUrl,
            logs,
            configure => configure.ConfigureHttpClientDefaults(client => client.AddHttpMessageHandler(() => recorder)));
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        await client.MatchCustomerAsync(CustomerAIdNumber, "1000000011", CancellationToken);

        RecordedRequest request = Assert.Single(recorder.Requests);
        Assert.Contains(CustomerAIdNumber, request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(CustomerAIdNumber, request.Url, StringComparison.Ordinal);
        Assert.DoesNotContain(CustomerAIdNumber, request.Headers, StringComparison.Ordinal);
        Assert.NotEmpty(logs.Lines);
        Assert.DoesNotContain(logs.Lines, line => line.Contains(CustomerAIdNumber, StringComparison.Ordinal));
        Assert.DoesNotContain(stub.Logs.Lines, line => line.Contains(CustomerAIdNumber, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetTransactionsAsync_AnyCall_SendsCustomerIdOnlyInAHeaderAndNeverLogsIt()
    {
        var logs = new LogCapture();
        var recorder = new RequestRecorder();
        await using ServiceProvider services = CoreBankingServices.Build(
            stub.BaseUrl,
            logs,
            configure => configure.ConfigureHttpClientDefaults(client => client.AddHttpMessageHandler(() => recorder)));
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        await client.GetTransactionsAsync(CustomerA, cursor: null, limit: 20, CancellationToken);

        RecordedRequest request = Assert.Single(recorder.Requests);
        Assert.Contains(CustomerA, request.Headers, StringComparison.Ordinal);
        Assert.DoesNotContain(CustomerA, request.Url, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Lines, line => line.Contains(CustomerA, StringComparison.Ordinal));
        Assert.DoesNotContain(stub.Logs.Lines, line => line.Contains(CustomerA, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAccountsAsync_CustomerWithTwoAccounts_ReturnsBothByReferenceAndLast4()
    {
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<IReadOnlyList<Account>> result = await client.GetAccountsAsync(CustomerA, CancellationToken);

        Assert.Equal([new Account("ACC-A-1", "0011"), new Account("ACC-A-2", "0012")], result.Value);
    }

    [Fact]
    public async Task GetTransactionsAsync_CustomerWithTwoAccounts_PagesThroughBothAccountsMergedNewestFirst()
    {
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        var paged = new List<Transaction>();
        string? cursor = null;
        do
        {
            Result<TransactionPage> page = await client.GetTransactionsAsync(CustomerA, cursor, limit: 7, CancellationToken);
            Assert.True(page.Value.Items.Count <= 7);
            paged.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        Result<TransactionPage> onePage = await client.GetTransactionsAsync(CustomerA, cursor: null, limit: 50, CancellationToken);

        Assert.Null(onePage.Value.NextCursor);
        Assert.Equal(onePage.Value.Items, paged);
        Assert.Equal(paged.Count, paged.Select(transaction => transaction.TransactionId).Distinct().Count());
        Assert.Equal(["ACC-A-1", "ACC-A-2"], paged.Select(transaction => transaction.AccountRef).Distinct().Order());
        Assert.Equal(
            paged.OrderByDescending(transaction => transaction.TransactionDate).ThenByDescending(transaction => transaction.TransactionId, StringComparer.Ordinal),
            paged);
    }

    [Fact]
    public async Task GetTransactionsAsync_CursorFromAnotherCustomer_ReturnsInvalidPaging()
    {
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<TransactionPage> result = await client.GetTransactionsAsync(CustomerA, cursor: "TXN-B-0001", limit: 20, CancellationToken);

        Assert.Equal(CoreBankingErrors.InvalidPaging, result.Error);
        Assert.Equal(["Cursor"], CoreBankingErrors.InvalidPaging.FieldErrors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task GetTransactionsAsync_LimitOutsideOneTo50_ReturnsInvalidPaging(int limit)
    {
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<TransactionPage> result = await client.GetTransactionsAsync(CustomerA, cursor: null, limit, CancellationToken);

        Assert.Equal(CoreBankingErrors.InvalidPaging, result.Error);
    }

    [Fact]
    public async Task GetTransactionAsync_OwnTransaction_ReturnsIt()
    {
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<Transaction> result = await client.GetTransactionAsync(CustomerA, "ACC-A-1", "TXN-A-0001", CancellationToken);

        Assert.Equal("TXN-A-0001", result.Value.TransactionId);
        Assert.Equal("ACC-A-1", result.Value.AccountRef);
    }

    [Theory]
    [InlineData("ACC-Ä-1", "TXN-A-0001")]                   // non-ASCII: HttpClient would refuse to send it
    [InlineData("ACC-A-1", "TXN-A-0001\r\nX-Injected: 1")] // a line break: header injection
    public async Task GetTransactionAsync_ValueNotShapedLikeAnId_ReturnsNotFoundWithoutSendingAndCircuitStaysClosed(string accountRef, string transactionId)
    {
        var recorder = new RequestRecorder();
        await using ServiceProvider services = CoreBankingServices.Build(
            stub.BaseUrl,
            configure: configure => configure.ConfigureHttpClientDefaults(client => client.AddHttpMessageHandler(() => recorder)));
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        // More calls than the 5 failures that would open the circuit, had they reached the HTTP layer.
        for (int call = 0; call < 6; call++)
        {
            Result<Transaction> result = await client.GetTransactionAsync(CustomerA, accountRef, transactionId, CancellationToken);
            Assert.Equal(CoreBankingErrors.NotFound, result.Error);
        }

        Assert.Empty(recorder.Requests);
        Result<Transaction> valid = await client.GetTransactionAsync(CustomerA, "ACC-A-1", "TXN-A-0001", CancellationToken);
        Assert.True(valid.IsSuccess);
    }

    [Theory]
    [InlineData("ACC-B-1", "TXN-B-0001")] // customer B's account and transaction
    [InlineData("ACC-B-1", "TXN-A-0001")] // customer A's transaction under customer B's account
    [InlineData("ACC-A-2", "TXN-A-0001")] // customer A's transaction under A's other account
    public async Task GetTransactionAsync_NotThisCustomersAccountAndTransaction_ReturnsNotFound(string accountRef, string transactionId)
    {
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<Transaction> result = await client.GetTransactionAsync(CustomerA, accountRef, transactionId, CancellationToken);

        Assert.Equal(CoreBankingErrors.NotFound, result.Error);
    }
}
