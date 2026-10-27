namespace DisputeDesk.CoreBankingStub;

/// <summary>
/// One of a customer's accounts. The full account number stays inside the stub: it is only matched against,
/// and responses carry the opaque <see cref="AccountRef"/> and the last 4 digits.
/// </summary>
public sealed record FixtureAccount(string AccountRef, string AccountNumber);
