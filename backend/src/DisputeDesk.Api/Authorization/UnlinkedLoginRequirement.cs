using Microsoft.AspNetCore.Authorization;

namespace DisputeDesk.Api.Authorization;

/// <summary>The <c>Unlinked</c> policy's requirement (AD-10): the signed-in login is not linked to a customer.</summary>
public sealed class UnlinkedLoginRequirement : IAuthorizationRequirement;
