using System.Net;
using DisputeDesk.Contracts.Audit;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Application.Audit;

/// <summary>
/// One row of the internal audit log (AD-14, AD-14a), which the application only ever appends to: the commit refuses
/// to update or delete one. The customer ID goes only in <see cref="CustomerId"/>, never in <see cref="ActorId"/>.
/// </summary>
public sealed class AuditLogEntry
{
    public required Guid Id { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required AuditAction Action { get; init; }

    public required AuditResult Result { get; init; }

    public required ActorType ActorType { get; init; }

    /// <summary>The login's <c>sub</c> for a customer, the token's <c>sub</c> for operations, <c>worker:sla-sweep</c> for automation.</summary>
    public required string ActorId { get; init; }

    public string? CustomerId { get; init; }

    /// <summary>The dispute the entry is about; null for linking entries, so there is no foreign key.</summary>
    public Guid? DisputeId { get; init; }

    /// <summary>The W3C trace ID of the request or batch that wrote the entry.</summary>
    public string? TraceId { get; init; }

    /// <summary>The client's address on a linking entry (FR-10), stored as <c>inet</c>; null on every other entry.</summary>
    public IPAddress? ClientIp { get; init; }
}
