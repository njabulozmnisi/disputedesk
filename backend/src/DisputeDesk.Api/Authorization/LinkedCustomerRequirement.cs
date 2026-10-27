using Microsoft.AspNetCore.Authorization;

namespace DisputeDesk.Api.Authorization;

/// <summary>The <c>Linked</c> policy's requirement (AD-10): the signed-in login is linked to a customer.</summary>
public sealed class LinkedCustomerRequirement : IAuthorizationRequirement;
