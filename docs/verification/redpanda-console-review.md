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
