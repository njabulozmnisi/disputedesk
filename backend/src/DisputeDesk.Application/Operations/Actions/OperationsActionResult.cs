using DisputeDesk.Contracts.Operations;

namespace DisputeDesk.Application.Operations.Actions;

/// <summary>
/// A committed operations action: the dispute as operations see it, and whether the SLA sweep had escalated it, which
/// only the stream's actor IDs show and the resolved metric's <c>sla_escalated</c> label needs (AD-20).
/// </summary>
public sealed record OperationsActionResult(OperationsDisputeResponse Dispute, bool SlaEscalated)
{
    /// <summary>Only the type name, like the dispute it holds (OWASP Logging Cheat Sheet: data to exclude).</summary>
    public sealed override string ToString() => nameof(OperationsActionResult);
}
