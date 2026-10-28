using System.Globalization;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Wire = DisputeDesk.Contracts.Transactions;

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
    public async Task<Result<Wire.TransactionPageResponse>> HandleAsync(string? cursor, int limit, CancellationToken cancellationToken)
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
        Wire.TransactionResponse[] rows = [.. page.Value.Items.Select(transaction => ToResponse(
            transaction,
            disputes.Find(dispute => dispute.AccountRef == transaction.AccountRef && dispute.TransactionId == transaction.TransactionId),
            now))];
        return new Wire.TransactionPageResponse(rows, nextCursor);
    }

    private static Wire.TransactionResponse ToResponse(Transaction transaction, RowDispute? dispute, DateTimeOffset now)
    {
        NotDisputableReason? reason = Disputability.ReasonNotDisputable(
            dispute is not null, transaction.Direction, transaction.Kind, transaction.State, transaction.TransactionDate, now);

        return new Wire.TransactionResponse(
            transaction.TransactionId,
            transaction.AccountRef,
            transaction.AccountLast4,
            transaction.TransactionDate,
            transaction.Description,
            new Wire.Money(transaction.Amount.ToString("0.00", CultureInfo.InvariantCulture), transaction.Currency),
            ToWire(transaction.Direction),
            ToWire(transaction.Kind),
            ToWire(transaction.State),
            transaction.MaskedCard,
            Disputable: reason is null,
            reason is null ? null : ToWire(reason.Value),
            dispute?.Id,
            dispute is null ? null : DisputeWire.ToCustomerStatus(dispute.Status));
    }

    // Contracts reference no other project (AD-1), so the wire enums are their own types with the same members.
    private static Wire.TransactionDirection ToWire(TransactionDirection direction) => direction switch
    {
        TransactionDirection.Debit => Wire.TransactionDirection.Debit,
        TransactionDirection.Credit => Wire.TransactionDirection.Credit,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unmapped transaction direction."),
    };

    private static Wire.TransactionKind ToWire(TransactionKind kind) => kind switch
    {
        TransactionKind.Card => Wire.TransactionKind.Card,
        TransactionKind.OwnAccountTransfer => Wire.TransactionKind.OwnAccountTransfer,
        TransactionKind.Transfer => Wire.TransactionKind.Transfer,
        TransactionKind.Other => Wire.TransactionKind.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped transaction kind."),
    };

    private static Wire.TransactionState ToWire(TransactionState state) => state switch
    {
        TransactionState.Settled => Wire.TransactionState.Settled,
        TransactionState.Pending => Wire.TransactionState.Pending,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unmapped transaction state."),
    };

    private static Wire.NotDisputableReason ToWire(NotDisputableReason reason) => reason switch
    {
        NotDisputableReason.AlreadyDisputed => Wire.NotDisputableReason.AlreadyDisputed,
        NotDisputableReason.NotEligible => Wire.NotDisputableReason.NotEligible,
        NotDisputableReason.OutsideWindow => Wire.NotDisputableReason.OutsideWindow,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unmapped reason."),
    };

    /// <summary>The dispute of one transaction on the page.</summary>
    private sealed record RowDispute(string AccountRef, string TransactionId, Guid Id, DisputeStatus Status);
}
