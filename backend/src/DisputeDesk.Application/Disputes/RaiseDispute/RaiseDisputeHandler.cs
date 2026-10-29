using System.Globalization;
using System.Text.Json;
using DisputeDesk.Application.Abstractions;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Contracts.Json;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;
using Microsoft.EntityFrameworkCore;
using Reason = DisputeDesk.Domain.Disputes.Reason;

namespace DisputeDesk.Application.Disputes.RaiseDispute;

/// <summary>
/// Raises a dispute for the current customer (FR-13, FR-14). Built-in validation has already checked the body's shape.
/// The transaction is read from core banking now, so its snapshot never comes from the request; core banking answers
/// only for the customer's own transactions, so another customer's is not found (404). The aggregate then decides
/// (409, 422, 400) and one commit writes the dispute with its timeline, audit and outbox rows (AD-14), and the
/// idempotency key completed with the 201 (AD-16).
/// </summary>
public sealed class RaiseDisputeHandler(IAppDbContext db, CurrentCustomer currentCustomer, ICoreBankingClient coreBanking, TimeProvider time)
{
    // The API's JSON settings (AD-5), so the stored 201 is spelt like every other response body.
    private static readonly JsonSerializerOptions ResponseJson = CreateResponseJson();

    /// <summary>
    /// Raises the dispute under <paramref name="key"/>, the idempotency key this request holds the lease on. The 201 is
    /// built before the one save and stored on the key, so the dispute and the completed key commit together or not at
    /// all; a lease lost to a take-over makes that save throw <see cref="DbUpdateConcurrencyException"/> and roll back.
    /// </summary>
    public async Task<Result<DisputeResponse>> HandleAsync(RaiseDisputeRequest request, IdempotencyKey key, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        Reason reason = DisputeWire.ToDomain(request.Reason ?? throw new ArgumentException("Built-in validation requires a reason.", nameof(request)));

        Result<Transaction> found = await coreBanking.GetTransactionAsync(
            currentCustomer.CustomerId, request.AccountRef, request.TransactionId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        Transaction transaction = found.Value;
        Dispute? earlier = await FindDisputeAsync(transaction, cancellationToken);

        Result<Dispute> raised = Dispute.Raise(
            currentCustomer.CustomerId,
            ToSnapshot(transaction),
            everDisputed: earlier is not null,
            reason,
            request.Note,
            ParseAmount(request.ExpectedAmount),
            new Actor(ActorType.Customer, currentCustomer.LoginSub),
            now);
        if (raised.IsFailure)
        {
            return earlier is not null && raised.Error == DisputeErrors.AlreadyDisputed ? AlreadyDisputed(earlier) : raised.Error;
        }

        DisputeResponse response = CustomerView.ToResponse(raised.Value);
        // shortcut: the GET this names is built in story 3.3 (a known gap against AD-5); 3.3 proves it serves the dispute
        key.CompleteCreated(JsonSerializer.Serialize(response, ResponseJson), $"/api/v1/disputes/{response.DisputeId}");
        db.Entities<Dispute>().Add(raised.Value);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueViolationException violation) when (violation.Error == DisputeErrors.AlreadyDisputed)
        {
            // Another raise for the same transaction committed between the read above and this commit. It is this
            // customer's own: core banking returned the transaction only to them, and accountRef is stable (AD-21).
            // Nothing of the lost save committed, the completed key included, and the context no longer tracks it.
            Dispute winner = await FindDisputeAsync(transaction, cancellationToken)
                ?? throw new InvalidOperationException("The unique index reported a dispute the ownership filter does not show.");
            return AlreadyDisputed(winner);
        }

        return response;
    }

    // The ownership filter limits this to the customer's own disputes, which is enough: the transaction is theirs.
    private Task<Dispute?> FindDisputeAsync(Transaction transaction, CancellationToken cancellationToken) =>
        db.Entities<Dispute>().AsNoTracking().FirstOrDefaultAsync(
            dispute => dispute.AccountRef == transaction.AccountRef && dispute.TransactionId == transaction.TransactionId,
            cancellationToken);

    // AD-13: a customer 409 carries the dispute the customer already has, as they see it.
    private static Error AlreadyDisputed(Dispute dispute) =>
        DisputeErrors.AlreadyDisputedAs(dispute.Id, DisputeWire.ToCustomerStatus(dispute.Status));

    private static TransactionSnapshot ToSnapshot(Transaction transaction) => new(
        transaction.AccountRef,
        transaction.TransactionId,
        transaction.AccountLast4,
        transaction.TransactionDate,
        transaction.Description,
        transaction.Amount,
        transaction.Currency,
        transaction.MaskedCard,
        transaction.Direction,
        transaction.Kind,
        transaction.State);

    // Built-in validation has matched RaiseDisputeRequest.AmountPattern, so the string is a plain non-negative decimal.
    private static decimal? ParseAmount(string? amount) =>
        amount is null ? null : decimal.Parse(amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private static JsonSerializerOptions CreateResponseJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        DisputeDeskJson.Apply(options);
        return options;
    }
}
