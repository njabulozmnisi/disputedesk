namespace DisputeDesk.CoreBankingStub;

/// <summary>The parts of <c>fixtures/local.json</c> the stub reads. Other fields (logins, display names) are ignored.</summary>
public sealed record Fixtures(IReadOnlyList<FixtureCustomer> Customers, IReadOnlyList<FixtureTransaction> Transactions);
