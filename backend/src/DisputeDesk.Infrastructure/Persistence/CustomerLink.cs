namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>
/// A login linked to a customer (AD-10): the link table every customer request resolves the customer from. One link
/// per login and one per customer.
/// </summary>
public sealed class CustomerLink
{
    public Guid Id { get; set; }

    /// <summary>The login's <c>sub</c> claim.</summary>
    public required string LoginSub { get; set; }

    /// <summary>The customer's ID in core banking.</summary>
    public required string CustomerId { get; set; }

    public DateTimeOffset LinkedAtUtc { get; set; }
}
