namespace DisputeDesk.Contracts.Transactions;

/// <summary>An amount on the wire (AD-4): a decimal string with exactly two decimals, such as <c>849.00</c>, and its ISO 4217 code.</summary>
public sealed record Money(string Amount, string Currency);
