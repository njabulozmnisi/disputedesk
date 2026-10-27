namespace DisputeDesk.Application.Abstractions;

/// <summary>A customer's bank account: an opaque, stable reference and the last 4 digits of the account number.</summary>
public sealed record Account(string AccountRef, string AccountLast4);
