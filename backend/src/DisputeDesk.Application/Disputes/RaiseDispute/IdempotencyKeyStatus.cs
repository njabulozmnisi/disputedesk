namespace DisputeDesk.Application.Disputes.RaiseDispute;

/// <summary>Where a raise under an <see cref="IdempotencyKey"/> is (AD-16).</summary>
public enum IdempotencyKeyStatus
{
    /// <summary>A request holds the lease and has not answered yet.</summary>
    InProgress,

    /// <summary>The response is stored; the same request with the key gets it again.</summary>
    Completed,
}
