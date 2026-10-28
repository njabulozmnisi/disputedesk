namespace DisputeDesk.Contracts.Audit;

/// <summary>How an audited attempt ended (AD-14a). A dispute's outcome is never stored here.</summary>
public enum AuditResult
{
    Succeeded,

    /// <summary>A rule or the current status refused it.</summary>
    Rejected,

    /// <summary>Authorisation refused it.</summary>
    Denied,

    /// <summary>A dependency failed.</summary>
    Failed,
}
