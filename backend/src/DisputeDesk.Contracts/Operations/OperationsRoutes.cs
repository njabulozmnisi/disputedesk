namespace DisputeDesk.Contracts.Operations;

/// <summary>The operations action routes (FR-21 to FR-23), relative to the <c>/api/v1/operations</c> group they are mapped on.</summary>
public static class OperationsRoutes
{
    public const string StartReview = "/disputes/{disputeId}/start-review";

    public const string Escalate = "/disputes/{disputeId}/escalate";

    public const string Resolve = "/disputes/{disputeId}/resolve";
}
