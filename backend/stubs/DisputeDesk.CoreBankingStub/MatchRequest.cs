namespace DisputeDesk.CoreBankingStub;

/// <summary>The body of a customer match. The ID number only ever travels in this body.</summary>
public sealed record MatchRequest(string IdNumber, string AccountNumber);
