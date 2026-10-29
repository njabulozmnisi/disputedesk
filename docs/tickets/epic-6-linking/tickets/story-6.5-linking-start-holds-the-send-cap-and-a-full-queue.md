---
id: 5
type: story
title: "Linking start holds the send cap and a full queue"
parent: epic-6-linking
covers: [R4, R6, R10, R36]
after: [1]
refined: false
hitl: false
risk: high
---

# Linking start holds the send cap and a full queue

## Description

Adds the three-per-hour send cap to story 6.1's job and the audited 429 when the bounded channel is full (AD-12). The job's order becomes: core-banking match, link check, then in one transaction holding `pg_advisory_xact_lock` on the customer the cap count, the SIM-swap check and the claim; the transaction commits, then the send, and the release on failure, as in 6.1. The SIM-swap call therefore runs inside the locked transaction, bounded by its own whole-call deadline (AD-21) and the job's 10-second budget.

## Requirements

Verify: Integration tests prove the cap at its edges, across logins and IPs and under parallel starts, that a released send does not count, and the audited 429 on a full queue.

1. **At most 3 OTPs per customer per hour** (R6, R36; FR-4, NFR-SEC-4, NFR-OBS-3)
   **Given** customer D with 3 challenges whose `OtpSentAtUtc` falls within the last hour, from 3 logins and 2 client IPs, and one send that failed and was released (story 6.1, criterion 5)
   **When** a 4th start matches customer D and passes the link check
   **Then** nothing is claimed or sent, the 202 is unchanged, `LinkingSendCapped` `Rejected` is audited with customer D's `CustomerId`, and `disputedesk.linking.events` counts `capped`
   **And** the cap counts only rows with `OtpSentAtUtc` greater than now minus 1 hour (`TimeProvider`), so a send 59 minutes 59 seconds ago counts and one exactly 1 hour ago does not, and the released send counts nothing (decided 2026-10-30)
   **And** the SIM-swap check is not called for a capped start

2. **The cap holds under parallel starts** (R6; FR-4, NFR-TEST-3)
   **Given** customer D with 2 sends in the last hour
   **When** 6 logins start linking with customer D's details in parallel
   **Then** exactly 1 more send is claimed and 5 starts audit `LinkingSendCapped`, because each job counts and claims in one transaction holding `pg_advisory_xact_lock` keyed on the customer (PostgreSQL, *Advisory Locks*; AD-12), and the SMTP send runs after that transaction commits

3. **A full queue answers 429 to everyone** (R4, R10; FR-4, AD-12)
   **Given** the bounded channel full at its capacity of 100, with the job held
   **When** a well-formed start arrives, with matching details and with details that match nothing
   **Then** `TryWrite` returns false (Microsoft Learn, *System.Threading.Channels*, bounded channels) and both get 429 `rate-limited` ProblemDetails with the same body except `traceId` (RFC 6585 §4; AD-3), both audit `LinkingRateLimited` `Rejected` with the client IP in a second save, beside the `LinkingStart` `Succeeded` the request already saved, and neither is queued; the blank challenge each wrote stays current and verifies as a wrong code (story 6.2)

## Boundaries

- The cap counts sent challenges for the customer across all logins and IPs (R6); the per-login and per-IP limits are story 6.3.
- Story 6.1's uniform 202, the claim's condition, the send, the release and the audit fields do not change; only the claim moves into the locked transaction.
- The full-queue 429 carries no `Retry-After`: it has no window to report. Story 6.3's limiter 429 carries one.
- The 429 replaces story 6.1's `// shortcut:` (a full queue answered 202 with a log line); the claim's budget, `CancellationToken.None` on the release and audits, and the personal-data rules of 6.1 criteria 5 and 8 apply unchanged.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- In a throwaway copy, take the cap count outside the advisory-lock transaction: criterion 2's parallel test fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Test setup: `LinkingOptions` carries the channel capacity (100 by default, AD-12), and the full-queue test sets a small capacity and holds the job, so the test does not need over 100 starts.
- Touches: `backend/src/DisputeDesk.Infrastructure/Linking/` (the cap in the job, the `capped` count), `backend/src/DisputeDesk.Application/Linking/StartLinking/` (the handler, which calls `TryWrite`, returns the full-queue result and saves the `LinkingRateLimited` entry), `backend/src/DisputeDesk.Api/Linking/` (the 429 on a full channel), `backend/tests/DisputeDesk.Api.IntegrationTests/Linking/` (new cap and full-queue tests). No migration, no contract change beyond the 429 response on the start operation in `backend/openapi.json` and `frontend/src/api/generated/`.

## References

- parent — docs/tickets/epic-6-linking/epic-6-linking.md
- architecture — docs/architecture/architecture.md, AD-3, AD-12, AD-14a
- prd — docs/prd/prd.md, FR-4, FR-10, NFR-SEC-4, NFR-OBS-3, NFR-TEST-3
- standard — <https://www.rfc-editor.org/rfc/rfc6585#section-4>
- standard — <https://learn.microsoft.com/dotnet/core/extensions/channels>
- challenge — docs/verification/6.1-challenge.md
- standard — <https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS>

## Notes

- Risk high: the cap is the per-customer guard against SMS flooding; beyond the criteria, the security agent runs the parallel test against the Compose stack.
- Decision: 2026-10-30 (Sofia, 6.1 challenge) — split from 6.1 to keep each story within about 8 criteria (AGENTS.md, Definition of ready).
- Decision: 2026-10-30 (human, 6.1 challenge) — a failed send is released and counts nothing toward the cap (story 6.1, Notes).
- Assumption: 2026-10-30 (Sofia) — a full-queue start keeps its `LinkingStart` `Succeeded` entry and `started` count and adds `LinkingRateLimited` `Rejected`; the challenge must be saved before `TryWrite`, so a queued job never runs ahead of its row. Confirm at the human review of the diff.
