using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Domain.Disputes;

/// <summary>
/// A customer's dispute of one transaction (AD-13): the snapshot of the transaction, the reason and amounts, the
/// status and the timeline. Its methods are the only way to create or change one; each records a pending change that
/// the commit (AD-14) turns into an audit entry and a dispute event.
/// </summary>
public sealed class Dispute
{
    public const int NoteMaxLength = 1000;

    /// <summary>An unresolved dispute is escalated this long after it was raised (FR-26).</summary>
    public static readonly TimeSpan Sla = TimeSpan.FromHours(240);

    private readonly List<TimelineEntry> _timeline = [];
    private readonly List<DisputeChange> _pendingChanges = [];

    private Dispute(string customerId, TransactionSnapshot transaction, Reason reason, string? note, decimal? expectedAmount, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        CustomerId = customerId;
        AccountRef = transaction.AccountRef;
        TransactionId = transaction.TransactionId;
        TransactionDateUtc = transaction.TransactionDate.ToUniversalTime();
        Description = transaction.Description;
        TransactionAmount = transaction.Amount;
        MaskedCard = transaction.MaskedCard;
        AccountLast4 = transaction.AccountLast4;
        // shortcut: amount assumed ZAR billed, add a currency check when a non-ZAR source exists
        Currency = transaction.Currency;
        Reason = reason;
        Note = note;
        ExpectedAmount = expectedAmount;
        DisputedAmount = expectedAmount is decimal expected ? transaction.Amount - expected : transaction.Amount;
        Status = DisputeStatus.Submitted;
        Version = 1;
        CreatedAtUtc = now;
        SlaDueAtUtc = now + Sla;
    }

    // EF Core materialises disputes through this constructor and then sets every property from the row.
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

    /// <summary>1 when raised, plus one per action; events carry it as <c>disputeVersion</c>.</summary>
    public int Version { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset SlaDueAtUtc { get; private set; }

    public IReadOnlyList<TimelineEntry> Timeline => _timeline;

    /// <summary>
    /// Raises a dispute on <paramref name="transaction"/>, read from core banking just now (FR-13, FR-14). The checks run
    /// in the AD-8 order: the input's own rules (400), an earlier dispute (409), disputability (422), and last the
    /// expected amount against the transaction amount (400).
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

        var dispute = new Dispute(customerId, transaction, reason, trimmedNote, expectedAmount, now);
        var change = new DisputeChange(DisputeAction.Raise, FromStatus: null, DisputeStatus.Submitted, actor, now, dispute.Version);
        dispute._timeline.Add(new TimelineEntry(dispute, change, trimmedNote));
        dispute._pendingChanges.Add(change);
        return dispute;
    }

    /// <summary>The changes not yet committed, in order; the commit (AD-14) takes them once.</summary>
    public IReadOnlyList<DisputeChange> TakePendingChanges()
    {
        DisputeChange[] changes = [.. _pendingChanges];
        _pendingChanges.Clear();
        return changes;
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
