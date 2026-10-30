namespace DisputeDesk.Application.Abstractions;

/// <summary>A customer's bank account: an opaque, stable reference and the last 4 digits of the account number.</summary>
public sealed record Account(string AccountRef, string AccountLast4)
{
    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and this one holds the account
    /// reference (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public sealed override string ToString() => nameof(Account);
}
