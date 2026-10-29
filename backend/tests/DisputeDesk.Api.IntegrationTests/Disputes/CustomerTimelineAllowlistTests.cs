using DisputeDesk.Application.Disputes;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AD-13: the customer timeline is built from the stored events through one deny-by-default allowlist that admits only
/// the five lifecycle types (OWASP Authorization Cheat Sheet: deny by default). No database: the view is a decision.
/// </summary>
public class CustomerTimelineAllowlistTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Timeline_EveryTypeAndAnUnknownOne_AdmitsOnlyTheFiveLifecycleTypesInStreamOrder()
    {
        StoredEvent[] stream =
        [
            At(new DisputeRaised("CUS-000001", Transaction(), Reason.Unrecognised, "Not me", null, 10m, DisputeStatus.Submitted), 1, ActorType.Customer),
            At(new SlaWarningRaised(), 2, ActorType.Automation),
            At(new ReviewStarted(DisputeStatus.Submitted, DisputeStatus.UnderReview), 3, ActorType.Operations),
            At(new UnknownStoredEvent(), 4, ActorType.Operations),
            At(new DisputeEscalated(DisputeStatus.UnderReview, DisputeStatus.Escalated), 5, ActorType.Automation),
            At(new DisputeResolved(DisputeStatus.Escalated, DisputeStatus.Resolved, Outcome.Upheld, "Refunded"), 6, ActorType.Operations),
        ];

        IReadOnlyList<Contracts.Disputes.TimelineEntryResponse> timeline = CustomerView.Timeline(stream);

        Assert.Equal(
            [
                new Contracts.Disputes.TimelineEntryResponse(Contracts.Disputes.DisputeAction.Raise, Contracts.Disputes.CustomerStatus.Submitted, Contracts.Disputes.TimelineActor.You, Now.AddHours(1), "Not me"),
                new Contracts.Disputes.TimelineEntryResponse(Contracts.Disputes.DisputeAction.StartReview, Contracts.Disputes.CustomerStatus.UnderReview, Contracts.Disputes.TimelineActor.TheBank, Now.AddHours(3), null),
                new Contracts.Disputes.TimelineEntryResponse(Contracts.Disputes.DisputeAction.Escalate, Contracts.Disputes.CustomerStatus.UnderReviewPrioritised, Contracts.Disputes.TimelineActor.TheBank, Now.AddHours(5), null),
                new Contracts.Disputes.TimelineEntryResponse(Contracts.Disputes.DisputeAction.Resolve, Contracts.Disputes.CustomerStatus.Resolved, Contracts.Disputes.TimelineActor.TheBank, Now.AddHours(6), "Refunded"),
            ],
            timeline);
    }

    [Fact]
    public void Timeline_Withdrawn_IsTheCustomersWithTheirNote()
    {
        StoredEvent withdrawn = At(new DisputeWithdrawn(DisputeStatus.Submitted, DisputeStatus.Withdrawn, "Found it"), 2, ActorType.Customer);

        Contracts.Disputes.TimelineEntryResponse entry = Assert.Single(CustomerView.Timeline([withdrawn]));

        Assert.Equal(new Contracts.Disputes.TimelineEntryResponse(Contracts.Disputes.DisputeAction.Withdraw, Contracts.Disputes.CustomerStatus.Withdrawn, Contracts.Disputes.TimelineActor.You, Now.AddHours(2), "Found it"), entry);
    }

    [Fact]
    public void Timeline_SlaWarning_GivesNoEntry()
    {
        Assert.Empty(CustomerView.Timeline([At(new SlaWarningRaised(), 2, ActorType.Automation)]));
    }

    [Fact]
    public void Timeline_TypeNotOnTheAllowlist_GivesNoEntry()
    {
        Assert.Empty(CustomerView.Timeline([At(new UnknownStoredEvent(), 2, ActorType.Operations)]));
    }

    private static TransactionSnapshot Transaction() => new(
        "ACC-A-1", "TXN-A-0029", "0011", Now.AddDays(-2), "Shop", 10m, "ZAR", null,
        TransactionDirection.Debit, TransactionKind.Card, TransactionState.Settled);

    private static StoredEvent At(StoredEvent storedEvent, int version, ActorType actorType) =>
        storedEvent with { Version = version, OccurredAtUtc = Now.AddHours(version), ActorType = actorType, ActorId = "actor-" + version };

    /// <summary>A stored event type declared outside the Domain, so the allowlist cannot name it.</summary>
    private sealed record UnknownStoredEvent : StoredEvent;
}
