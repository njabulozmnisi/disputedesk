namespace DisputeDesk.Application.Disputes.RaiseDispute;

/// <summary>
/// One <c>Idempotency-Key</c> a customer sent on a raise (AD-16, draft-ietf-httpapi-idempotency-key-header), with the
/// hash of the request it was first used with and, once answered, the stored response. Only the request holding
/// <see cref="LeaseId"/> may complete or release it: <see cref="LeaseId"/> is the row's EF Core concurrency token, so a
/// save made under a lost lease fails and rolls back.
/// </summary>
public sealed class IdempotencyKey
{
    /// <summary>The longest key, counted on the decoded string.</summary>
    public const int KeyMaxLength = 255;

    /// <summary>How long a key is kept; an older row is replaced by the next request that uses the key.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a request may hold the lease before the same request with the key takes it over. Longer than the raise
    /// endpoint's 30-second request timeout, so a live request is never taken over.
    /// </summary>
    public static readonly TimeSpan TakeOverAfter = TimeSpan.FromSeconds(60);

    public Guid Id { get; init; }

    public required string CustomerId { get; init; }

    public required string Key { get; init; }

    public Guid LeaseId { get; init; }

    /// <summary>The SHA-256 of the validated request, re-serialised, so the same request gives the same hash.</summary>
    public required byte[] RequestHash { get; init; }

    public IdempotencyKeyStatus Status { get; private set; }

    public int? ResponseStatus { get; private set; }

    public string? ResponseLocation { get; private set; }

    /// <summary>The response body exactly as it was sent (<c>text</c>, not <c>jsonb</c>, which re-orders keys).</summary>
    public string? ResponseBody { get; private set; }

    /// <summary>When the current lease was taken: the first use, or the latest take-over or replacement.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>Stores the 201 of a raise; the raise's one save then commits it with the dispute.</summary>
    public void CompleteCreated(string body, string location)
    {
        Status = IdempotencyKeyStatus.Completed;
        ResponseStatus = 201;
        ResponseBody = body;
        ResponseLocation = location;
    }
}
