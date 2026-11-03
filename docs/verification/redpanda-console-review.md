# Review: redpanda-console (Small tier)

Brief: `redpanda-console-brief.md`. Builder: built, all six criteria met; reviewer: approve, no high, medium or low findings.

## Builder evidence

- Redpanda `v26.2.6` (`sha256:8b1076e0…`) and Console `v3.13.1` (`sha256:d72608f6…`), both read from the registry and confirmed again by the reviewer; Console runs as its image's non-root user.
- Worker integration tests (relay, broker outage) pass on v26.2.6. Full suite: 1624 tests, 1622 passed; the 2 failures (3 on a rerun of the Api project) were timing-sensitive Api tests (`RateLimitWindowTests`, `IdempotencyConflictTests`) under a load average near 17 on 12 cores, passing 14/14 alone; no Api code or test changed. CI is the authoritative run.
- Four deliberate breaks (profile `local`, profile removed, `COMPOSE_PROFILES=console` in deploy.sh, `--profile console` in deploy.sh) each fail `check-compose.sh` or `deploy.test.sh`.
- Smoke under the stack lock: plain `up --wait` starts no console; `--profile console up -d --wait redpanda-console` is healthy on `127.0.0.1:8081`, `/api/topics` lists `disputedesk.disputes` (3 partitions); idle 50 MiB against a 128 MiB limit.

## Reviewer nits (accepted as they stand)

1. `check-compose.sh` line 147 repeats a case the exact service list already fails; kept for its clearer message.
2. The no-profile service list is rendered twice; left as two plain lines.
3. README: a plain `stop` or `down` leaves a running console up. The builder confirmed this on a throwaway project (Compose acts only on services of active profiles; Docker, *Using profiles*).

## Escapes

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
| --- | --- | --- | --- | --- |
| The architecture stack table still named Redpanda v26.2.2 | builder (handed to supervisor) | brief | spec gap | none; a one-off doc row |

## Metrics

Fix rounds 0; full-suite runs 1 (plus one Api project rerun); builder about 23 min, reviewer about 3 min.

## Follow-ups on main (2026-11-03)

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
| --- | --- | --- | --- | --- |
| Security and `publish` red on 50d1332: libexpat 2.9.0-r0 in the pinned nginx base, two HIGH CVEs published after the last green run; fixed in cbe5f4a by adding libexpat to the gateway's `apk upgrade` line | CI (Security, publish) | none: disclosed after the last scan | environment or tooling | none; the existing `shortcut:` line covers it until the base is rebuilt |
| `SlaBatchTests.Sweep_TwoAtOnceWithOverlappingTransactions_…` failed on cbe5f4a: it read the two sweeps' first batches by span end order, which is not fixed; fixed in 08318fe by ordering by start time (forced interleaving reproduced the CI failure; 30 of 30 runs pass) | CI (backend) | story 5.3 review | flaky | proposed for the human: a review-story pitfall line, "a race test relies only on an order the code guarantees or on an identifier, never on the order concurrent work records spans or events" |
