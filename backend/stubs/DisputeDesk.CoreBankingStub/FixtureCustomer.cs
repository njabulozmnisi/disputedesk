namespace DisputeDesk.CoreBankingStub;

/// <summary>A customer as the bank records them, including the details linking checks against.</summary>
public sealed record FixtureCustomer(
    string CustomerId,
    string IdNumber,
    string Cellphone,
    bool RecentSimSwap,
    IReadOnlyList<FixtureAccount> Accounts);
