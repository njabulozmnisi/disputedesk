namespace DisputeDesk.Application.Abstractions;

/// <summary>The customer an ID number and account number belong to, and the cellphone on the bank's record.</summary>
public sealed record CustomerMatch(string CustomerId, string Cellphone)
{
    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and this one holds the cellphone
    /// (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public override string ToString() => nameof(CustomerMatch);
}
