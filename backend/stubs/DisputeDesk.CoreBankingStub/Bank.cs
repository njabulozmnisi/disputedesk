using System.Text.Json;

namespace DisputeDesk.CoreBankingStub;

/// <summary>
/// The bank's records as the fixtures describe them, read once at start-up. Each transaction's date is its
/// <c>daysAgo</c> and <c>time</c> counted back from the start of the current South African day (AD-30).
/// </summary>
public sealed class Bank
{
    // South Africa has no daylight saving time, so its offset is always UTC+02:00.
    private static readonly TimeSpan SouthAfricanOffset = TimeSpan.FromHours(2);

    private readonly IReadOnlyList<FixtureCustomer> _customers;
    private readonly Dictionary<string, IReadOnlyList<TransactionRow>> _transactionsByCustomerId;

    public Bank(Fixtures fixtures, DateTimeOffset now)
    {
        DateTimeOffset southAfricanNow = now.ToOffset(SouthAfricanOffset);
        var startOfToday = new DateTimeOffset(southAfricanNow.Date, SouthAfricanOffset);

        _customers = fixtures.Customers;
        _transactionsByCustomerId = fixtures.Customers.ToDictionary(
            customer => customer.CustomerId,
            customer => TransactionsNewestFirst(customer, fixtures.Transactions, startOfToday));
    }

    public static Bank Load(string fixturesPath, TimeProvider time)
    {
        using FileStream file = File.OpenRead(fixturesPath);
        Fixtures fixtures = JsonSerializer.Deserialize<Fixtures>(file, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"{fixturesPath} holds no fixtures.");
        return new Bank(fixtures, time.GetUtcNow());
    }

    public FixtureCustomer? FindByCustomerId(string customerId) =>
        _customers.FirstOrDefault(customer => customer.CustomerId == customerId);

    public FixtureCustomer? FindByCellphone(string cellphone) =>
        _customers.FirstOrDefault(customer => customer.Cellphone == cellphone);

    public FixtureCustomer? FindByIdNumberAndAccountNumber(string idNumber, string accountNumber) =>
        _customers.FirstOrDefault(customer =>
            customer.IdNumber == idNumber && customer.Accounts.Any(account => account.AccountNumber == accountNumber));

    /// <summary>The customer's transactions on all their accounts, newest first; null for an unknown customer.</summary>
    public IReadOnlyList<TransactionRow>? TransactionsOf(string customerId) =>
        _transactionsByCustomerId.GetValueOrDefault(customerId);

    private static IReadOnlyList<TransactionRow> TransactionsNewestFirst(
        FixtureCustomer customer,
        IReadOnlyList<FixtureTransaction> allTransactions,
        DateTimeOffset startOfToday)
    {
        var rows = new List<TransactionRow>();
        foreach (FixtureAccount account in customer.Accounts)
        {
            foreach (FixtureTransaction transaction in allTransactions.Where(transaction => transaction.AccountRef == account.AccountRef))
            {
                DateTimeOffset transactionDate = startOfToday.AddDays(-transaction.DaysAgo).Add(transaction.Time.ToTimeSpan());
                rows.Add(new TransactionRow(
                    transaction.TransactionId,
                    account.AccountRef,
                    account.AccountNumber[^4..],
                    transactionDate.ToUniversalTime(),
                    transaction.Description,
                    transaction.Amount,
                    transaction.Currency,
                    transaction.Direction,
                    transaction.Kind,
                    transaction.State,
                    transaction.MaskedCard));
            }
        }

        return [.. rows
            .OrderByDescending(row => row.TransactionDate)
            .ThenByDescending(row => row.TransactionId, StringComparer.Ordinal)];
    }
}
