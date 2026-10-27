namespace DisputeDesk.Application.Abstractions;

/// <summary>
/// The linked customer a request acts for (AD-10). Scoped, one per request. The <c>Linked</c> policy's
/// <c>LinkedCustomerHandler</c> in the Api sets it from <c>CustomerLinks</c> by the session's login before a customer
/// route runs; handlers read only this, never the request or the token subject.
/// </summary>
public sealed class CurrentCustomer
{
    private string? _customerId;

    /// <summary>The customer's ID in core banking. Reading it before the handler set it is a programming error.</summary>
    public string CustomerId => _customerId ?? throw new InvalidOperationException("No linked customer was resolved for this request.");

    public void Set(string customerId) => _customerId = customerId;
}
