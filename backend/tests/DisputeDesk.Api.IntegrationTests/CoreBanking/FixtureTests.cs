using System.Text.Json;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Api.IntegrationTests.CoreBanking;

/// <summary>Fixtures for customers A to D, resolved from the start of the South African day (AC8).</summary>
public sealed class FixtureTests(CoreBankingStubFactory stub) : IClassFixture<CoreBankingStubFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string> CustomerIds => ["CUS-000001", "CUS-000002", "CUS-000003", "CUS-000004"];

    [Theory]
    [MemberData(nameof(CustomerIds))]
    public async Task Transactions_EachSeededCustomer_CoverAbout90DaysWithEveryKindOfRow(string customerId)
    {
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        Result<IReadOnlyList<Account>> accounts = await client.GetAccountsAsync(customerId, CancellationToken);
        Result<TransactionPage> page = await client.GetTransactionsAsync(customerId, cursor: null, limit: 50, CancellationToken);
        IReadOnlyList<Transaction> rows = page.Value.Items;
        DateTimeOffset ninetyDaysAgo = CoreBankingStubFactory.StartOfToday.AddDays(-90);

        Assert.InRange(accounts.Value.Count, 1, 2);
        Assert.Null(page.Value.NextCursor);
        Assert.Contains(rows, row => row.State == TransactionState.Pending);
        Assert.Contains(rows, row => row.Direction == TransactionDirection.Credit);
        Assert.Contains(rows, row => row.Kind == TransactionKind.OwnAccountTransfer);
        Assert.Contains(rows, row => row.TransactionDate < ninetyDaysAgo);
        Assert.Contains(rows, row => row.TransactionDate >= ninetyDaysAgo && row.TransactionDate < ninetyDaysAgo.AddDays(10));
        Assert.All(rows, row => Assert.True(row.TransactionDate < CoreBankingStubFactory.Now, $"{row.TransactionId} is in the future."));
    }

    [Fact]
    public async Task GetTransactionAsync_FixtureDaysAgoAndTime_ResolvesFromTheStartOfTheSouthAfricanDay()
    {
        await using ServiceProvider services = CoreBankingServices.Build(stub.BaseUrl);
        ICoreBankingClient client = services.GetRequiredService<ICoreBankingClient>();

        // TXN-A-0001 is 95 days ago at 16:42. The stub's clock reads 01:00 on 26 October South African time
        // (still 25 October in UTC), so the day it counts back from is 26 October.
        Result<Transaction> result = await client.GetTransactionAsync("CUS-000001", "ACC-A-1", "TXN-A-0001", CancellationToken);

        Assert.Equal(new DateTimeOffset(2026, 7, 23, 14, 42, 0, TimeSpan.Zero), result.Value.TransactionDate);
    }

    [Fact]
    public void Fixtures_EveryAccountAndTransaction_HasAnExplicitAccountRefAndTransactionId()
    {
        using JsonDocument fixtures = FixturesFile.Read();
        JsonElement[] customers = [.. fixtures.RootElement.GetProperty("customers").EnumerateArray()];
        string[] accountRefs = [.. customers
            .SelectMany(customer => customer.GetProperty("accounts").EnumerateArray())
            .Select(account => account.GetProperty("accountRef").GetString() ?? string.Empty)];
        JsonElement[] transactions = [.. fixtures.RootElement.GetProperty("transactions").EnumerateArray()];
        string[] transactionIds = [.. transactions.Select(transaction => transaction.GetProperty("transactionId").GetString() ?? string.Empty)];

        Assert.Equal(["A", "B", "C", "D"], customers.Select(customer => customer.GetProperty("key").GetString()));
        Assert.All(accountRefs, accountRef => Assert.False(string.IsNullOrWhiteSpace(accountRef)));
        Assert.Equal(accountRefs.Length, accountRefs.Distinct().Count());
        Assert.All(transactionIds, transactionId => Assert.False(string.IsNullOrWhiteSpace(transactionId)));
        Assert.Equal(transactionIds.Length, transactionIds.Distinct().Count());
        Assert.All(transactions, transaction => Assert.Contains(transaction.GetProperty("accountRef").GetString(), accountRefs));
        // CoreBankingClient answers not-found for any value outside this shape, so every fixture ID must fit it.
        Assert.All([.. accountRefs, .. transactionIds], id => Assert.Matches(@"^[A-Z0-9-]{1,32}\z", id));
    }
}
