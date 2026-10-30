using System.Text.Json;
using System.Text.Json.Serialization;
using DisputeDesk.Domain.Disputes;
using DisputeDesk.Domain.Transactions;

namespace DisputeDesk.Infrastructure.Seeding;

/// <summary>The parts of <c>fixtures/local.json</c> the seeder reads; everything else in the file is ignored.</summary>
internal sealed record Fixtures(
    IReadOnlyList<FixtureCustomer> Customers,
    IReadOnlyList<FixtureLogin> Logins,
    IReadOnlyList<FixtureTransaction> Transactions,
    IReadOnlyList<FixtureDispute> Disputes)
{
    // Enums by their camelCase names only, as the file writes them, and a missing required value fails the read
    // (System.Text.Json: JsonStringEnumConverter, RespectRequiredConstructorParameters, RespectNullableAnnotations).
    private static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    // A small local file, read synchronously so the sync and async seeding paths share one method.
    public static Fixtures Read(string path) =>
        JsonSerializer.Deserialize<Fixtures>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException("The fixtures file holds no fixtures.");
}

// The records below hold personal data (customer IDs, login subs, account numbers and references, transaction IDs,
// masked cards, notes), so each prints only its type name, never its fields (security pitfall 3.8; AD-20).

internal sealed record FixtureCustomer(string Key, string CustomerId, bool Linked, string? Login, IReadOnlyList<FixtureAccount> Accounts)
{
    public sealed override string ToString() => nameof(FixtureCustomer);
}

internal sealed record FixtureAccount(string AccountRef, string AccountNumber)
{
    public sealed override string ToString() => nameof(FixtureAccount);
}

internal sealed record FixtureLogin(string Key, string UserId)
{
    public sealed override string ToString() => nameof(FixtureLogin);
}

/// <summary>A core-banking transaction; its date is <see cref="DaysAgo"/> days before the anchor, at <see cref="Time"/> South African time.</summary>
internal sealed record FixtureTransaction(
    string TransactionId,
    string AccountRef,
    int DaysAgo,
    TimeOnly Time,
    string Description,
    decimal Amount,
    string Currency,
    TransactionDirection Direction,
    TransactionKind Kind,
    TransactionState State,
    string? MaskedCard)
{
    public sealed override string ToString() => nameof(FixtureTransaction);
}

/// <summary>
/// A seeded dispute: the customer (by fixture key), the transaction, the raise inputs, and its actions in order. The
/// one SLA demo dispute is raised so that it falls due 5 minutes after the seeding instant.
/// </summary>
internal sealed record FixtureDispute(
    string Customer,
    string AccountRef,
    string TransactionId,
    Reason Reason,
    IReadOnlyList<FixtureAction> Actions,
    decimal? ExpectedAmount = null,
    string? Note = null,
    bool SlaDemo = false)
{
    public sealed override string ToString() => nameof(FixtureDispute);
}

/// <summary>
/// One action on a seeded dispute, at <see cref="DaysAgo"/> + <see cref="Time"/>, or for the SLA demo dispute's later
/// actions <see cref="HoursAfterRaise"/>. Only a resolve carries an <see cref="Outcome"/>, and only a resolve or a
/// withdraw a note.
/// </summary>
internal sealed record FixtureAction(
    FixtureActionType Action,
    int? DaysAgo = null,
    TimeOnly? Time = null,
    int? HoursAfterRaise = null,
    Outcome? Outcome = null,
    string? Note = null)
{
    public sealed override string ToString() => nameof(FixtureAction);
}

internal enum FixtureActionType
{
    Raise,
    StartReview,
    Escalate,
    Resolve,
    Withdraw,
}
