namespace DisputeDesk.CoreBankingStub;

/// <summary>The body of a SIM-swap check: the cellphone number on the bank's record.</summary>
public sealed record SimSwapRequest(string Cellphone);
