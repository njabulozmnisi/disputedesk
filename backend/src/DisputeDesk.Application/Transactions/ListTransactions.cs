using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Transactions;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Application.Transactions;

/// <summary>
/// Lists the current customer's transactions, newest first, one keyset page at a time (FR-11), each row marked
/// disputable or not and carrying its dispute when it has one (FR-12). Core banking answers only for the given
/// customer, so ownership needs no other check; the disputes come through the ownership filter (AD-11).
/// </summary>
public sealed class ListTransactions(CurrentCustomer currentCustomer, ICoreBankingClient coreBanking, IAppDbContext db, TimeProvider time)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    private static readonly Error InvalidCursor = Error.Validation("validation", "The cursor is not valid.", "Cursor");

    /// <param name="cursor">The previous page's <c>nextCursor</c>, or null for the first page.</param>
    /// <param name="limit">1 to <see cref="MaxPageSize"/>, already checked by built-in validation at the edge.</param>
    /// <param name="cancellationToken">Cancels the call to core banking and the dispute query.</param>
    public async Task<Result<TransactionPageResponse>> HandleAsync(string? cursor, int limit, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();

        string? coreBankingCursor = null;
        if (cursor is not null)
        {
            coreBankingCursor = TransactionCursor.Decode(cursor);
            if (coreBankingCursor is null)
            {
                return InvalidCursor;
            }
        }

        Result<TransactionPage> page = await coreBanking.GetTransactionsAsync(
            currentCustomer.CustomerId, coreBankingCursor, limit, cancellationToken);
        if (page.IsFailure)
        {
            return page.Error;
        }

        // One query for the whole page. A transaction ID is unique only within its account, so rows match on both.
        string[] transactionIds = [.. page.Value.Items.Select(transaction => transaction.TransactionId)];
        List<RowDispute> disputes = await db.Entities<Dispute>()
            .Where(dispute => transactionIds.Contains(dispute.TransactionId))
            .Select(dispute => new RowDispute(dispute.AccountRef, dispute.TransactionId, dispute.Id, dispute.Status))
            .ToListAsync(cancellationToken);

        string? nextCursor = page.Value.NextCursor is null ? null : TransactionCursor.Encode(page.Value.NextCursor);
        TransactionResponse[] rows = [.. page.Value.Items.Select(transaction => ToResponse(
            transaction,
            disputes.Find(dispute => dispute.AccountRef == transaction.AccountRef && dispute.TransactionId == transaction.TransactionId),
            now))];
        return new TransactionPageResponse(rows, nextCursor);
    }

    private static TransactionResponse ToResponse(Transaction transaction, RowDispute? dispute, DateTimeOffset now)
    {
        NotDisputableReason? reason = Disputability.ReasonNotDisputable(
            dispute is not null, transaction.Direction, transaction.Kind, transaction.State, transaction.TransactionDate, now);

        return new TransactionResponse(
            transaction.TransactionId,
            transaction.AccountRef,
            transaction.AccountLast4,
            transaction.TransactionDate,
            transaction.Description,
            DisputeWire.ToMoney(transaction.Amount, transaction.Currency),
            transaction.Direction,
            transaction.Kind,
            transaction.State,
            transaction.MaskedCard,
            Disputable: reason is null,
            reason,
            dispute?.Id,
            dispute is null ? null : DisputeWire.ToCustomerStatus(dispute.Status));
    }

    /// <summary>The dispute of one transaction on the page.</summary>
    private sealed record RowDispute(string AccountRef, string TransactionId, Guid Id, DisputeStatus Status)
    {
        /// <summary>Only the type name: a record's generated <c>ToString</c> would print the account reference and transaction ID.</summary>
        public sealed override string ToString() => nameof(RowDispute);
    }
}
