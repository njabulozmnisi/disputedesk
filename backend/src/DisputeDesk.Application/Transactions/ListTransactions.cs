using System.Globalization;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Transactions;
using Wire = DisputeDesk.Contracts.Transactions;

namespace DisputeDesk.Application.Transactions;

/// <summary>
/// Lists the current customer's transactions, newest first, one keyset page at a time (FR-11), each row marked
/// disputable or not (FR-12). Core banking answers only for the given customer, so ownership needs no other check.
/// </summary>
public sealed class ListTransactions(CurrentCustomer currentCustomer, ICoreBankingClient coreBanking, TimeProvider time)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    private static readonly Error InvalidCursor = Error.Validation("validation", "The cursor is not valid.", "Cursor");

    /// <param name="cursor">The previous page's <c>nextCursor</c>, or null for the first page.</param>
    /// <param name="limit">1 to <see cref="MaxPageSize"/>, already checked by built-in validation at the edge.</param>
    /// <param name="cancellationToken">Cancels the call to core banking.</param>
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

        string? nextCursor = page.Value.NextCursor is null ? null : TransactionCursor.Encode(page.Value.NextCursor);
        return new Wire.TransactionPageResponse([.. page.Value.Items.Select(transaction => ToResponse(transaction, now))], nextCursor);
    }

    private static Wire.TransactionResponse ToResponse(Transaction transaction, DateTimeOffset now)
    {
        NotDisputableReason? reason = Disputability.ReasonNotDisputable(
            transaction.Direction, transaction.Kind, transaction.State, transaction.TransactionDate, now);

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
            reason is null ? null : ToWire(reason.Value));
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
        NotDisputableReason.NotEligible => Wire.NotDisputableReason.NotEligible,
        NotDisputableReason.OutsideWindow => Wire.NotDisputableReason.OutsideWindow,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unmapped reason."),
    };
}
