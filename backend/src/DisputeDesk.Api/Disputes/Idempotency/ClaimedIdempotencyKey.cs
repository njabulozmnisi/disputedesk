using DisputeDesk.Application.Disputes.RaiseDispute;

namespace DisputeDesk.Api.Disputes.Idempotency;

/// <summary>
/// The idempotency key this request holds the lease on. Scoped, one per request, like <c>CurrentCustomer</c>:
/// <see cref="IdempotencyFilter"/> sets it before the raise endpoint runs, and the endpoint hands it to the handler.
/// </summary>
public sealed class ClaimedIdempotencyKey
{
    private IdempotencyKey? _key;

    public IdempotencyKey Key => _key ?? throw new InvalidOperationException("No idempotency key was claimed for this request.");

    public void Set(IdempotencyKey key) => _key = key;
}
