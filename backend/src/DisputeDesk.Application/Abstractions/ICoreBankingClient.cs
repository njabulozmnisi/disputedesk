using DisputeDesk.Domain;

namespace DisputeDesk.Application.Abstractions;

/// <summary>
/// The bank's core banking system (AD-21). Every method answers only for the given customer: an account or
/// transaction that is not theirs is <c>not-found</c>. When core banking is still down after the resilience
/// policies, every method returns <see cref="CoreBankingErrors.Unavailable"/>.
/// </summary>
public interface ICoreBankingClient
{
    /// <summary>Finds the customer holding this ID number and account number. The ID number is sent in a request body only.</summary>
    Task<Result<CustomerMatch>> MatchCustomerAsync(string idNumber, string accountNumber, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<Account>>> GetAccountsAsync(string customerId, CancellationToken cancellationToken);

    /// <summary>One page of the customer's transactions across all their accounts, newest first by transaction date then transaction ID.</summary>
    Task<Result<TransactionPage>> GetTransactionsAsync(string customerId, string? cursor, int limit, CancellationToken cancellationToken);

    Task<Result<Transaction>> GetTransactionAsync(string customerId, string accountRef, string transactionId, CancellationToken cancellationToken);
}
