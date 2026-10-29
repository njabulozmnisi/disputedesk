using DisputeDesk.Application.Disputes.RaiseDispute;
using Microsoft.EntityFrameworkCore;

namespace DisputeDesk.Infrastructure.Persistence;

/// <summary>Taking the lease on an idempotency key (AD-16).</summary>
public static class IdempotencyKeyClaims
{
    /// <summary>
    /// Claims <paramref name="key"/> for the customer under a new lease, in one statement that commits on its own: it
    /// inserts the row, or replaces the existing one when it has expired, or when it is still in progress with the same
    /// request hash and older than <see cref="IdempotencyKey.TakeOverAfter"/>. Returns the claimed row, tracked, or
    /// null when another request holds the key or has completed it.
    /// </summary>
    /// <remarks>
    /// Raw SQL because EF Core has no upsert: PostgreSQL's <c>INSERT ... ON CONFLICT ... DO UPDATE ... WHERE</c> claims or
    /// takes over atomically, where a read and then a write would let two requests both claim. <c>FromSql</c> keeps it
    /// parameterised, and EF Core tracks the returned row, so the raise's one save can complete it under the lease. Not
    /// composed on (no LINQ operator after <c>FromSql</c>), because an INSERT cannot be a subquery.
    /// </remarks>
    public static async Task<IdempotencyKey?> ClaimAsync(
        this DbSet<IdempotencyKey> keys, string customerId, string key, byte[] requestHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7(now);
        var leaseId = Guid.NewGuid();
        DateTimeOffset expiresAt = now + IdempotencyKey.Lifetime;
        DateTimeOffset staleBefore = now - IdempotencyKey.TakeOverAfter;
        string inProgress = nameof(IdempotencyKeyStatus.InProgress);

        List<IdempotencyKey> claimed = await keys.FromSql($"""
            INSERT INTO "IdempotencyKeys" ("Id", "CustomerId", "Key", "LeaseId", "RequestHash", "Status", "CreatedAtUtc", "ExpiresAtUtc")
            VALUES ({id}, {customerId}, {key}, {leaseId}, {requestHash}, {inProgress}, {now}, {expiresAt})
            ON CONFLICT ("CustomerId", "Key") DO UPDATE
            SET "LeaseId" = EXCLUDED."LeaseId",
                "RequestHash" = EXCLUDED."RequestHash",
                "Status" = EXCLUDED."Status",
                "ResponseStatus" = NULL,
                "ResponseLocation" = NULL,
                "ResponseBody" = NULL,
                "CreatedAtUtc" = EXCLUDED."CreatedAtUtc",
                "ExpiresAtUtc" = EXCLUDED."ExpiresAtUtc"
            WHERE "IdempotencyKeys"."ExpiresAtUtc" <= {now}
               OR ("IdempotencyKeys"."Status" = {inProgress}
                   AND "IdempotencyKeys"."RequestHash" = EXCLUDED."RequestHash"
                   AND "IdempotencyKeys"."CreatedAtUtc" < {staleBefore})
            RETURNING *
            """).ToListAsync(cancellationToken);

        return claimed.SingleOrDefault();
    }
}
