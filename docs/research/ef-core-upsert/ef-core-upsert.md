# Claiming an idempotency key: upsert options in EF Core 10

Question: what is the best way to "insert, or take over when allowed" an `IdempotencyKeys` row (AD-16), judged on correctness under concurrency, readability for people and agents, maintenance, and efficiency? Researched 2026-10-28 against EF Core 10.0.13 and Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3.

## What the claim must do

One request wins a key per customer. A new key is inserted. An existing key is taken over only when it has expired, or when it is still in progress with the same request hash and is older than 60 seconds. Otherwise the caller learns who holds the key (409, 422 or a replay). The won row must be tracked so the raise's one `SaveChangesAsync` completes it under the `LeaseId` concurrency token.

## Facts

- EF Core has no upsert. `ExecuteUpdate`/`ExecuteDelete` support only update and delete; "insertion must be done via `DbSet.Add` and `SaveChanges()`" ([EF Core, ExecuteUpdate and ExecuteDelete, Limitations](https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete#limitations)). The upsert request ([dotnet/efcore#4526](https://github.com/aspnet/EntityFramework/issues/4526)) has been open since 2016; EF Core 10's release notes add nothing for it ([What's new in EF Core 10](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew)).
- An `Add` that hits a unique index throws a provider exception, not `DbUpdateConcurrencyException`; an `UPDATE` whose concurrency token changed throws `DbUpdateConcurrencyException` and applies nothing ([EF Core, Handling concurrency conflicts](https://learn.microsoft.com/ef/core/saving/concurrency#optimistic-concurrency)).
- PostgreSQL, READ COMMITTED: a second `UPDATE` waiting on a row lock re-evaluates its `WHERE` against the committed new version, so a stale lease token matches nothing ([PostgreSQL, Transaction isolation, Read Committed](https://www.postgresql.org/docs/current/transaction-iso.html#XACT-READ-COMMITTED)).
- `INSERT … ON CONFLICT DO UPDATE … WHERE … RETURNING` returns no row when the `WHERE` is false ([PostgreSQL, INSERT](https://www.postgresql.org/docs/current/sql-insert.html)).
- FlexLabs.EntityFrameworkCore.Upsert (MIT, ~700 stars, 36 open issues, last push 2026-08-11) has an EF Core 10 line, `WhenMatched`, `UpdateIf` and `RunAndReturnAsync`; the PostgreSQL runner emits `ON CONFLICT DO UPDATE` and reads results with `FromSqlRaw(...).AsNoTracking()` (source: `Runners/RelationalUpsertCommandRunner.cs`).

## Options

| | A. Raw SQL upsert (built) | B. EF only: read, then insert or token-guarded update | C. FlexLabs.Upsert |
|---|---|---|---|
| Mechanism | `FromSql` with `INSERT … ON CONFLICT DO UPDATE … WHERE … RETURNING` | `FirstOrDefaultAsync`; `Add` + save, or change the tracked row + save; the unique index and the `LeaseId` concurrency token arbitrate races | Library builds the same `ON CONFLICT` SQL from expressions |
| Correct under races | Yes (one atomic statement) | Yes (unique index for two inserts; token re-check for two takeovers) | Likely; `UpdateIf` translation of `byte[]` equality and `DateTimeOffset` comparison unverified |
| Where the claim rule lives | In SQL, apart from the C# that answers 409/422 | One C# method on `IdempotencyKey`, unit-testable with `FakeTimeProvider` | In an expression tree |
| Round trips | New key 1; replay 2 (failed upsert, then read); takeover 1 | New key 2 (read, insert); replay 1 (read); takeover 2 (read, update) | Same as A |
| Result tracked for the one save | Yes (`FromSql` tracks) | Yes (ordinary tracked entity) | No: `AsNoTracking`, so an extra `Attach` |
| Compiler and rename safety | None: a renamed column fails at runtime (tests catch it) | Full | Full |
| Conventions | Allowed as "a feature EF can't express", one named method | Plain ORM, the documented optimistic-concurrency pattern | A new package must earn its place (AGENTS.md); one more thing to upgrade with every EF major |
| Failure noise | None | A lost first-insert race logs EF `fail:` lines (rare: two first uses in the same instant) | None |

Efficiency: the difference is one indexed point read on a unique index, well under a millisecond, inside a request that already calls core banking over HTTP. B is cheaper on the replay path, the one retries actually take.

## Recommendation: B

B uses only documented EF Core features the codebase already relies on (the unique index mapping and the `LeaseId` concurrency token), puts the claim rule in one plain C# method beside the entity, and removes the one raw-SQL statement and its second copy of the rule. A new reader understands it in two sentences: "read the key; insert it if missing, take it over if the rules allow, otherwise answer from it. The unique index stops two inserts and the lease token stops two takeovers."

Sketch (shape only):

```csharp
IdempotencyKey? existing = await keys.FirstOrDefaultAsync(k => k.CustomerId == customerId && k.Key == key, ct);
if (existing is null)
{
    keys.Add(IdempotencyKey.Claim(customerId, key, requestHash, now));     // unique index arbitrates
    // save; on UniqueViolationException: ChangeTracker.Clear(), re-read once, answer from it
}
else if (existing.CanBeTakenOver(requestHash, now))                       // expired, or stale with the same hash
{
    existing.TakeOver(now);                                               // new LeaseId, reset response and times
    // save; on DbUpdateConcurrencyException: someone else took it, re-read, answer from it
}
else
{
    // answer from existing: replay, 409 in progress or 422 mismatch
}
```

Choose A instead only if the single round trip on a new key is measured to matter; it is correct as built. C adds a dependency without removing the SQL's hidden rule (it moves into an expression) and still needs an `Attach`; not recommended.

## Cost of switching now

3.2 has passed every stage with A. B is a contained rework of `IdempotencyKeyClaims.cs` and the claim part of `IdempotencyFilter`, plus a domain method and its unit tests, covered by the existing integration tests (takeover, races, 10 parallel submits). It would need one more fix round, the verifier, a reviewer delta, and a QA re-run of the concurrency section, and AD-16's "an expired row is replaced in the same statement" would change.
