namespace DisputeDesk.Application.Abstractions;

/// <summary>
/// The linked customer a request acts for (AD-10). Scoped, one per request. The <c>Linked</c> policy's
/// <c>LinkedCustomerHandler</c> in the Api sets it from <c>CustomerLinks</c> by the session's login before a customer
/// route runs; handlers read only this, never the request or the token subject.
/// </summary>
public sealed class CurrentCustomer
{
    private string? _customerId;
    private string? _loginSub;

    /// <summary>The customer's ID in core banking. Reading it before the handler set it is a programming error.</summary>
    public string CustomerId => _customerId ?? throw new InvalidOperationException("No linked customer was resolved for this request.");

    /// <summary>The login's <c>sub</c>, the actor identity of the customer's actions (AD-14a).</summary>
    public string LoginSub => _loginSub ?? throw new InvalidOperationException("No linked customer was resolved for this request.");

    /// <summary>
    /// The customer's ID, or null before one is resolved. Only for the ownership filter (AD-11), which must match
    /// nothing when no customer was resolved, rather than throw.
    /// </summary>
    public string? CustomerIdOrNull => _customerId;

    public void Set(string customerId, string loginSub)
    {
        _customerId = customerId;
        _loginSub = loginSub;
    }
}
