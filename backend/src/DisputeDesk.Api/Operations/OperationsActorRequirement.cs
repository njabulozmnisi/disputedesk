using DisputeDesk.Contracts.Audit;
using Microsoft.AspNetCore.Authorization;

namespace DisputeDesk.Api.Operations;

/// <summary>
/// Every operations policy's requirement (AD-9): the token's <c>sub</c> is mapped to the operations actor. It carries the
/// policy's <see cref="AuditAction"/>, which the authorization result handler records when the policy refuses with 403
/// (AD-14a).
/// </summary>
public sealed class OperationsActorRequirement(AuditAction action) : IAuthorizationRequirement
{
    public AuditAction Action { get; } = action;
}
