using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Domain.Disputes;

/// <summary>
/// A customer's dispute of one transaction (AD-13): the snapshot of the transaction, the reason and amounts, and the
/// status. Its methods are the only way to create or change one. Each decides, appends one pending stored event and
/// changes the dispute only by applying that event; <see cref="Replay"/> applies a stream through the same cases. The
/// commit (AD-14) writes each pending stored event, its outbox row when published, and this dispute as the read model.
/// </summary>
public sealed class Dispute
{
    public const int NoteMaxLength = 1000;

    /// <summary>An unresolved dispute is escalated this long after it was raised (FR-26).</summary>
    public static readonly TimeSpan Sla = TimeSpan.FromHours(240);

    private readonly List<StoredEvent> _pendingChanges = [];

    // EF Core materialises the read model through this constructor and then sets every property from the row; Raise
    // and Replay start from it too and set every property by applying DisputeRaised.
    private Dispute()
    {
        CustomerId = "";
        AccountRef = "";
        TransactionId = "";
        Description = "";
        AccountLast4 = "";
        Currency = "";
    }

    public Guid Id { get; private set; }

    /// <summary>The customer's ID in core banking; the ownership filter (AD-11) matches on it.</summary>
    public string CustomerId { get; private set; }

    public string AccountRef { get; private set; }

    public string TransactionId { get; private set; }

    public DateTimeOffset TransactionDateUtc { get; private set; }

    public string Description { get; private set; }

    public decimal TransactionAmount { get; private set; }

    /// <summary>The card's first 6 and last 4 digits, or null when there is no card.</summary>
    public string? MaskedCard { get; private set; }

    public string AccountLast4 { get; private set; }

    /// <summary>The ISO 4217 code of every amount on the dispute.</summary>
    public string Currency { get; private set; }

    public Reason Reason { get; private set; }

    /// <summary>The trimmed note, or null when none was given or it was only whitespace.</summary>
    public string? Note { get; private set; }

    /// <summary>What the customer expected to pay; only for <see cref="Reason.WrongAmount"/>.</summary>
    public decimal? ExpectedAmount { get; private set; }

    /// <summary>The transaction amount, or for a wrong amount the transaction amount minus the expected amount.</summary>
    public decimal DisputedAmount { get; private set; }

    public DisputeStatus Status { get; private set; }

    /// <summary>The outcome of a resolved dispute; null until it is resolved.</summary>
    public Outcome? Outcome { get; private set; }

    /// <summary>The version of the latest stored event applied: 1 when raised; events carry it as <c>disputeVersion</c>.</summary>
    public int Version { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset SlaDueAtUtc { get; private set; }

    /// <summary>The stored events appended since the dispute was raised or replayed and not yet committed, in order.</summary>
    public IReadOnlyList<StoredEvent> PendingChanges => _pendingChanges;

    /// <summary>
    /// Raises a dispute on <paramref name="transaction"/>, read from core banking just now (FR-13, FR-14). The checks run
    /// in the AD-8 order: the input's own rules (400), an earlier dispute (409), disputability (422), and last the
    /// expected amount against the transaction amount (400). A raise appends one <see cref="DisputeRaised"/> at version 1.
    /// </summary>
    /// <param name="customerId">The linked customer, who owns the transaction.</param>
    /// <param name="transaction">The transaction as core banking reports it now.</param>
    /// <param name="everDisputed">Whether the transaction already has a dispute, in any status.</param>
    /// <param name="reason">Why the customer disputes it.</param>
    /// <param name="note">The customer's note, trimmed here; empty or whitespace becomes no note.</param>
    /// <param name="expectedAmount">The amount the customer expected; only for <see cref="Reason.WrongAmount"/>.</param>
    /// <param name="actor">The customer, by their login.</param>
    /// <param name="now">The use case's one instant (AD-4).</param>
    public static Result<Dispute> Raise(
        string customerId,
        TransactionSnapshot transaction,
        bool everDisputed,
        Reason reason,
        string? note,
        decimal? expectedAmount,
        Actor actor,
        DateTimeOffset now)
    {
        string? trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote?.Length > NoteMaxLength)
        {
            return DisputeErrors.NoteTooLong;
        }

        Error? expectedAmountError = CheckExpectedAmount(reason, expectedAmount);
        if (expectedAmountError is not null)
        {
            return expectedAmountError;
        }

        NotDisputableReason? notDisputable = Disputability.ReasonNotDisputable(
            everDisputed, transaction.Direction, transaction.Kind, transaction.State, transaction.TransactionDate, now);
        if (notDisputable == NotDisputableReason.AlreadyDisputed)
        {
            return DisputeErrors.AlreadyDisputed;
        }

        if (notDisputable is NotDisputableReason notDisputableReason)
        {
            return DisputeErrors.NotDisputable(notDisputableReason);
        }

        if (expectedAmount >= transaction.Amount)
        {
            return DisputeErrors.ExpectedAmountNotBelowTransactionAmount;
        }

        decimal disputedAmount = expectedAmount is decimal expected ? transaction.Amount - expected : transaction.Amount;
        var raised = new DisputeRaised(
            customerId, transaction, reason, trimmedNote, expectedAmount, disputedAmount, DisputeStatus.Submitted)
        {
            DisputeId = Guid.CreateVersion7(now),
            Version = 1,
            OccurredAtUtc = now,
            ActorType = actor.Type,
            ActorId = actor.Id,
        };
        var dispute = new Dispute();
        dispute.Append(raised);
        return dispute;
    }

    /// <summary>
    /// Rebuilds a dispute from its whole stream, given in <see cref="StoredEvent.Version"/> order (AD-13; Microsoft
    /// Learn, Event Sourcing pattern). A malformed stream throws, so a partial dispute is never returned: it must start
    /// with its only <see cref="DisputeRaised"/>, run from version 1 to n with no gap or repeat, and hold only types
    /// with an <c>Apply</c> case. The replayed dispute has no pending changes.
    /// </summary>
    public static Dispute Replay(IReadOnlyList<StoredEvent> storedEvents)
    {
        CheckStream(storedEvents);
        var dispute = new Dispute();
        foreach (StoredEvent storedEvent in storedEvents)
        {
            dispute.Apply(storedEvent);
        }

        return dispute;
    }

    /// <summary>The stored events not yet committed, in order; the commit (AD-14) takes them once.</summary>
    public IReadOnlyList<StoredEvent> TakePendingChanges()
    {
        StoredEvent[] changes = [.. _pendingChanges];
        _pendingChanges.Clear();
        return changes;
    }

    private static void CheckStream(IReadOnlyList<StoredEvent> storedEvents)
    {
        if (storedEvents.Count == 0 || storedEvents[0] is not DisputeRaised)
        {
            throw new InvalidOperationException("A dispute's stream must start with DisputeRaised.");
        }

        for (int index = 0; index < storedEvents.Count; index++)
        {
            if (storedEvents[index].Version != index + 1)
            {
                throw new InvalidOperationException($"Stored event {index + 1} of the stream has version {storedEvents[index].Version}.");
            }

            if (index > 0 && storedEvents[index] is DisputeRaised)
            {
                throw new InvalidOperationException("A dispute's stream holds only one DisputeRaised.");
            }
        }
    }

    private void Append(StoredEvent storedEvent)
    {
        Apply(storedEvent);
        _pendingChanges.Add(storedEvent);
    }

    /// <summary>The one place a stored event changes the dispute, for its own methods and for replay alike.</summary>
    private void Apply(StoredEvent storedEvent)
    {
        switch (storedEvent)
        {
            case DisputeRaised raised:
                ApplyRaised(raised);
                break;
            case ReviewStarted started:
                Status = started.ToStatus;
                break;
            case DisputeEscalated escalated:
                Status = escalated.ToStatus;
                break;
            case DisputeResolved resolved:
                Status = resolved.ToStatus;
                Outcome = resolved.Outcome;
                break;
            case DisputeWithdrawn withdrawn:
                Status = withdrawn.ToStatus;
                break;
            default:
                // SlaWarningRaised too: story 5.3 adds its case with RecordSlaWarning (AD-18).
                throw new InvalidOperationException($"No Apply case for the stored event type {storedEvent.GetType().Name}.");
        }

        Version = storedEvent.Version;
    }

    private void ApplyRaised(DisputeRaised raised)
    {
        TransactionSnapshot transaction = raised.Transaction;
        Id = raised.DisputeId;
        CustomerId = raised.CustomerId;
        AccountRef = transaction.AccountRef;
        TransactionId = transaction.TransactionId;
        TransactionDateUtc = transaction.TransactionDate.ToUniversalTime();
        Description = transaction.Description;
        TransactionAmount = transaction.Amount;
        MaskedCard = transaction.MaskedCard;
        AccountLast4 = transaction.AccountLast4;
        // shortcut: amount assumed ZAR billed, add a currency check when a non-ZAR source exists
        Currency = transaction.Currency;
        Reason = raised.Reason;
        Note = raised.Note;
        ExpectedAmount = raised.ExpectedAmount;
        DisputedAmount = raised.DisputedAmount;
        Status = raised.ToStatus;
        CreatedAtUtc = raised.OccurredAtUtc;
        // shortcut: SlaDueAtUtc is derived from the constant Sla, store it on DisputeRaised (additive) if the SLA ever varies
        SlaDueAtUtc = raised.OccurredAtUtc + Sla;
    }

    /// <summary>The rules on the expected amount that need no transaction: who may send one, and its shape.</summary>
    private static Error? CheckExpectedAmount(Reason reason, decimal? expectedAmount)
    {
        if (reason != Reason.WrongAmount)
        {
            return expectedAmount is null ? null : DisputeErrors.ExpectedAmountNotAllowed;
        }

        if (expectedAmount is not decimal expected)
        {
            return DisputeErrors.ExpectedAmountRequired;
        }

        bool hasAtMostTwoDecimals = decimal.Round(expected, 2) == expected;
        return expected >= 0 && hasAtMostTwoDecimals ? null : DisputeErrors.ExpectedAmountMalformed;
    }
}
