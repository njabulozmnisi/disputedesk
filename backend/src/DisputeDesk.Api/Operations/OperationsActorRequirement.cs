using Microsoft.AspNetCore.Authorization;

namespace DisputeDesk.Api.Operations;

/// <summary>Every operations policy's requirement (AD-9): the token's <c>sub</c> is mapped to the operations actor.</summary>
public sealed class OperationsActorRequirement : IAuthorizationRequirement;
