using System.Diagnostics;
using System.Linq.Expressions;
using DisputeDesk.Domain;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Infrastructure.Seeding;

/// <summary>
/// One fixture dispute, checked against the fixtures and with each action's instant resolved (AD-30): days ago from the
/// anchor (the start of the current South African day, as the core-banking stub counts), or for the SLA demo dispute
/// from the seeding instant. <see cref="Write"/> builds it through the <see cref="Dispute"/> aggregate only (AD-13).
/// A fixture entry that cannot be seeded throws, naming only its position in the file.
/// </summary>
internal sealed record SeedDispute(
    int Position,
    string CustomerId,
    string LoginSub,
    TransactionSnapshot Transaction,
    FixtureDispute Fixture,
    IReadOnlyList<DateTimeOffset> Instants)
{
    /// <summary>The actor ID of the seeder's start review, escalate and resolve, an automation actor (AD-14a).</summary>
    public const string SeederActorId = "worker:seeder";

    // The SLA demo dispute falls due this long after the seeding instant (FR-26, story 5.4).
    private static readonly TimeSpan SlaDemoDueIn = TimeSpan.FromMinutes(5);

    // South Africa has no daylight saving time, so its offset is always UTC+02:00 (AD-4).
    private static readonly TimeSpan SouthAfricanOffset = TimeSpan.FromHours(2);

    /// <summary>The fixture disputes in file order, checked and resolved against <paramref name="seedingInstant"/>.</summary>
    public static List<SeedDispute> Plan(Fixtures fixtures, DateTimeOffset seedingInstant)
    {
        int slaDemos = fixtures.Disputes.Count(dispute => dispute.SlaDemo);
        if (slaDemos != 1)
        {
            throw new InvalidOperationException($"Fixture disputes cannot be seeded: {slaDemos} entries are the SLA demo dispute, not exactly one.");
        }

        var anchor = new DateTimeOffset(seedingInstant.ToOffset(SouthAfricanOffset).Date, SouthAfricanOffset);
        var planned = new List<SeedDispute>();
        for (int position = 0; position < fixtures.Disputes.Count; position++)
        {
            planned.Add(PlanOne(fixtures, position, anchor, seedingInstant));
        }

        return planned;
    }

    /// <summary>Selects this dispute's row by its transaction; through the ownership filter it finds only the customer's own.</summary>
    public Expression<Func<Dispute, bool>> Matches()
    {
        string accountRef = Transaction.AccountRef;
        string transactionId = Transaction.TransactionId;
        return dispute => dispute.AccountRef == accountRef && dispute.TransactionId == transactionId;
    }

    /// <summary>
    /// Raises the dispute and takes it through its actions, each at its own instant, all pending for one commit. The
    /// customer raises and withdraws; the seeder starts the review, escalates and resolves as an automation actor.
    /// Planning has checked each action's shape, so only the aggregate can refuse one here.
    /// </summary>
    public Dispute Write(bool everDisputed)
    {
        var customer = new Actor(ActorType.Customer, LoginSub);
        var seeder = new Actor(ActorType.Automation, SeederActorId);
        Result<Dispute> raised = Dispute.Raise(
            CustomerId, Transaction, everDisputed, Fixture.Reason, Fixture.Note, Fixture.ExpectedAmount, customer, Instants[0]);
        if (raised.IsFailure)
        {
            throw Invalid(Position, $"the raise was refused ({raised.Error.Code})");
        }

        Dispute dispute = raised.Value;
        for (int index = 1; index < Fixture.Actions.Count; index++)
        {
            FixtureAction action = Fixture.Actions[index];
            Result result = action.Action switch
            {
                FixtureActionType.StartReview => dispute.StartReview(seeder, Instants[index]),
                FixtureActionType.Escalate => dispute.Escalate(seeder, Instants[index]),
                FixtureActionType.Resolve when action.Outcome is Outcome outcome => dispute.Resolve(outcome, action.Note, seeder, Instants[index]),
                FixtureActionType.Withdraw => dispute.Withdraw(action.Note, customer, Instants[index]),
                _ => throw new UnreachableException("Planning admits no second raise and no resolve without an outcome."),
            };
            if (result.IsFailure)
            {
                throw Invalid(Position, $"actions[{index}] was refused ({result.Error.Code})");
            }
        }

        return dispute;
    }

    /// <summary>Only the position: a record's generated <c>ToString</c> would print the customer, the login and the note.</summary>
    public sealed override string ToString() => $"disputes[{Position}]";

    private static SeedDispute PlanOne(Fixtures fixtures, int position, DateTimeOffset anchor, DateTimeOffset seedingInstant)
    {
        FixtureDispute fixture = fixtures.Disputes[position];
        FixtureCustomer customer = fixtures.Customers.SingleOrDefault(candidate => candidate.Key == fixture.Customer && candidate.Linked)
            ?? throw Invalid(position, "its customer is not a linked fixture customer");
        // Single, as the link seeder resolves it: a login typo fails the seed.
        string loginSub = fixtures.Logins.Single(login => login.Key == customer.Login).UserId;
        FixtureTransaction transaction = fixtures.Transactions.SingleOrDefault(candidate =>
                candidate.AccountRef == fixture.AccountRef && candidate.TransactionId == fixture.TransactionId)
            ?? throw Invalid(position, "its accountRef and transactionId name no fixture transaction");
        FixtureAccount account = customer.Accounts.SingleOrDefault(candidate => candidate.AccountRef == transaction.AccountRef)
            ?? throw Invalid(position, "its transaction is not on its customer's account");
        bool sameTransactionEarlier = fixtures.Disputes.Take(position).Any(earlier =>
            earlier.AccountRef == fixture.AccountRef && earlier.TransactionId == fixture.TransactionId);
        if (sameTransactionEarlier)
        {
            throw Invalid(position, "an earlier fixture dispute has the same transaction");
        }

        DateTimeOffset transactionDate = DaysAgo(anchor, transaction.DaysAgo, transaction.Time);
        List<DateTimeOffset> instants = ResolveInstants(fixture, position, anchor, seedingInstant);
        DateTimeOffset previous = transactionDate;
        for (int index = 0; index < instants.Count; index++)
        {
            if (instants[index] <= previous)
            {
                throw Invalid(position, $"actions[{index}] is not after the transaction and every earlier action");
            }

            if (instants[index] > seedingInstant)
            {
                throw Invalid(position, $"actions[{index}] is after the seeding instant");
            }

            previous = instants[index];
        }

        var snapshot = new TransactionSnapshot(
            account.AccountRef,
            transaction.TransactionId,
            account.AccountNumber[^4..],
            transactionDate,
            transaction.Description,
            transaction.Amount,
            transaction.Currency,
            transaction.MaskedCard,
            transaction.Direction,
            transaction.Kind,
            transaction.State);
        return new SeedDispute(position, customer.CustomerId, loginSub, snapshot, fixture, instants);
    }

    /// <summary>
    /// The instant of each action, once its shape is checked. The first must be the raise: days ago and a time, or for
    /// the SLA demo dispute no instant, as the seeder makes it due 5 minutes after seeding. Later actions are days ago
    /// and a time, or for the SLA demo dispute hours after the raise.
    /// </summary>
    private static List<DateTimeOffset> ResolveInstants(FixtureDispute fixture, int position, DateTimeOffset anchor, DateTimeOffset seedingInstant)
    {
        if (fixture.Actions.Count == 0 || fixture.Actions[0].Action != FixtureActionType.Raise)
        {
            throw Invalid(position, "its actions do not start with a raise");
        }

        var instants = new List<DateTimeOffset>();
        for (int index = 0; index < fixture.Actions.Count; index++)
        {
            FixtureAction action = fixture.Actions[index];
            CheckShape(action, index, position);
            bool hasDayOrTime = action.DaysAgo is not null || action.Time is not null;
            if (index == 0 && fixture.SlaDemo)
            {
                if (hasDayOrTime || action.HoursAfterRaise is not null)
                {
                    throw Invalid(position, "actions[0] is the SLA demo raise, whose instant the seeder sets");
                }

                instants.Add(seedingInstant + SlaDemoDueIn - Dispute.Sla);
            }
            else if (action.HoursAfterRaise is int hours)
            {
                if (!fixture.SlaDemo)
                {
                    throw Invalid(position, $"actions[{index}] has hoursAfterRaise, which only the SLA demo dispute's later actions use");
                }

                if (hasDayOrTime)
                {
                    throw Invalid(position, $"actions[{index}] has both hoursAfterRaise and daysAgo or time");
                }

                instants.Add(instants[0].AddHours(hours));
            }
            else if (action.DaysAgo is int days && action.Time is TimeOnly time)
            {
                instants.Add(DaysAgo(anchor, days, time));
            }
            else
            {
                throw Invalid(position, $"actions[{index}] has no instant");
            }
        }

        return instants;
    }

    // Every field an action carries is used, so a fixture mistake fails the seed instead of being ignored.
    private static void CheckShape(FixtureAction action, int index, int position)
    {
        if (index > 0 && action.Action == FixtureActionType.Raise)
        {
            throw Invalid(position, $"actions[{index}] is a second raise");
        }

        if (action.Action == FixtureActionType.Resolve && action.Outcome is null)
        {
            throw Invalid(position, $"actions[{index}] is a resolve with no outcome");
        }

        if (action.Action != FixtureActionType.Resolve && action.Outcome is not null)
        {
            throw Invalid(position, $"actions[{index}] has an outcome, which only a resolve carries");
        }

        if (action.Note is not null && action.Action is not (FixtureActionType.Resolve or FixtureActionType.Withdraw))
        {
            throw Invalid(position, $"actions[{index}] has a note, which only a resolve or a withdraw carries");
        }
    }

    private static DateTimeOffset DaysAgo(DateTimeOffset anchor, int days, TimeOnly time) =>
        anchor.AddDays(-days).Add(time.ToTimeSpan()).ToUniversalTime();

    // The position only: never a customer ID, an account reference or a note (AD-20).
    private static InvalidOperationException Invalid(int position, string problem) =>
        new($"Fixture disputes[{position}] cannot be seeded: {problem}.");
}
