namespace DisputeDesk.Contracts.Authentication;

/// <summary>Where the caller stands (AD-10): not signed in, signed in but not yet a linked customer, or linked.</summary>
public enum SessionState
{
    Anonymous,
    Unlinked,
    Linked,
}
