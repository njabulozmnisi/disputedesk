namespace DisputeDesk.Application.Abstractions;

/// <summary>The customer an ID number and account number belong to, and the cellphone on the bank's record.</summary>
public sealed record CustomerMatch(string CustomerId, string Cellphone);
