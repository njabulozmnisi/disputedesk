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

Adds the three-per-hour send cap to story 6.1's job and the audited 429 when the bounded channel is full (AD-12). The job's order becomes: core-banking match, link check, then one EF Core transaction that takes `pg_advisory_xact_lock` on the customer, counts the cap, runs the SIM-swap check and claims the send; the transaction commits, then the send runs, and on failure the release, as in 6.1. Every claim counts toward the cap, delivered or not: the release now clears the OTP but keeps `OtpSentAtUtc` (human decision, 2026-10-30). The full-queue 429 goes through a new `ErrorKind.RateLimited` (AD-3).

## Requirements

Verify: Integration tests prove that every claim counts toward the cap, the cap's one-hour edge, the cap across logins and IPs and under parallel starts, an audited stop on an unexpected job exception, the audited 429 on a full queue with no `Retry-After`, and the `LinkingOptions` bounds.

1. **Every claim counts toward the cap of 3 per customer per hour** (R6, R36; FR-4, NFR-SEC-4, NFR-OBS-3)
   **Given** customer D with 2 delivered sends and 1 claim whose send failed and was released, all with `OtpSentAtUtc` within the last hour, from 3 logins and 2 client IPs
   **When** a 4th start matches customer D and passes the link check
   **Then** nothing is claimed or sent, the 202 is unchanged, `LinkingSendCapped` `Rejected` is audited with customer D's `CustomerId`, `disputedesk.linking.events` counts `capped`, and the SIM-swap check is not called
   **And** the release (story 6.1, criterion 5) now clears only `OtpHmac` and `ExpiresAtUtc` and keeps `OtpSentAtUtc`, so a failed send starts no lifetime and can never verify, but still holds its cap slot (human decision, 2026-10-30; AD-12 and FR-4 amended); 6.1's release tests change to expect `OtpSentAtUtc` kept
   **And** the cap counts the customer's challenges with `OtpSentAtUtc` greater than now minus 1 hour (`TimeProvider`): with the clock at a microsecond-aligned instant, a send stored exactly 1 hour earlier does not count and one 59 minutes 59 seconds earlier does

2. **The cap holds under parallel starts** (R6; FR-4, NFR-TEST-3)
   **Given** customer D with 2 sends in the last hour, and the SIM-swap fake holding the first job that reaches it (`HoldNext`), so that job sits between its count and its claim
   **When** 6 logins start linking with customer D's details in parallel and the held job is then released
   **Then** exactly 1 more send is claimed and 5 starts audit `LinkingSendCapped` `Rejected`
   **And** the lock, the count, the SIM-swap check and the claim run in one explicit EF Core transaction on the job's `AppDbContext` (Microsoft Learn, EF Core *Transactions*, controlling transactions) at the default Read Committed level, whose first statement is the two-key form `pg_advisory_xact_lock(<fixed int namespace>, <int from the first 4 bytes of the SHA-256 of the customer ID>)` (PostgreSQL, *Advisory Lock Functions*: the one-key and two-key spaces never overlap, so it never meets the relay's single-key lock); it is one named Infrastructure method sending parameterised SQL through `ExecuteSqlAsync`, and the lock wait runs under the job's budget token, so a wait past the budget ends in 6.1's `LinkingStart` `Failed`
   **And** the transaction commits before `ISmsSender` runs, so no database transaction or connection is held during the send

3. **An unexpected job exception is audited and claims nothing** (R10; FR-10)
   **Given** customer D, with the SIM-swap fake throwing an unexpected exception type once, and in a second case the core-banking fake throwing one
   **When** each start's job runs
   **Then** the transaction rolls back (releasing the advisory lock), nothing is claimed or sent, one `LinkingStart` `Failed` entry is recorded from a fresh scope on `CancellationToken.None`, the log line holds only the challenge ID and the exception type (OWASP Logging Cheat Sheet), and the next job still runs
   **And** a later start for customer D proceeds to a sent OTP

4. **A full queue answers 429 to everyone, without `Retry-After`** (R4, R10; FR-4, AD-12)
   **Given** the bounded channel full at a test capacity, with the job held
   **When** a well-formed start arrives, once with matching details and once with details that match nothing
   **Then** `TryWrite` returns false (Microsoft Learn, *System.Threading.Channels*, bounded channels), and both get 429 `rate-limited` ProblemDetails with the same body except `traceId` (RFC 6585 §4), through a new `ErrorKind.RateLimited` that `ErrorResults.ToProblem` maps to 429 `rate-limited` (human decision, 2026-10-30; AD-3 amended), with no `Retry-After` header
   **And** both audit `LinkingRateLimited` `Rejected` with the client IP in a second save, beside the `LinkingStart` `Succeeded` the request already saved, and neither is queued; the blank challenge each wrote stays current and unclaimed (story 6.3 proves it verifies as a wrong code, once this story and 6.2 have merged)
   **And** when that second save fails, the response is still the same 429 and one error log line holds only the challenge ID and the action

5. **`LinkingOptions` are bounded at start-up** (R4; FR-4, AD-26)
   **Given** `Linking:QueueCapacity` and `Linking:Budget`
   **When** the Api starts with a capacity of 0, -1 or 1001, or a budget of 0, a negative value, 999 milliseconds or more than 60 seconds
   **Then** it stops with an error naming the key, and starts with a capacity from 1 to 1000 and a budget from 1 to 60 seconds inclusive; a budget under one second is refused like 0 (Microsoft Learn, *Options pattern*, Options validation with `ValidateOnStart`)

## Boundaries

- The cap counts claims for the customer across all logins and IPs (R6), delivered or not; the per-login and per-IP limits are story 6.3.
- Story 6.1's uniform 202, the claim's condition, the send and the audit fields do not change. Only the claim moves into the locked transaction and the release keeps `OtpSentAtUtc`.
- The full-queue 429 carries no `Retry-After`: it has no window to report. Story 6.3's limiter 429 carries one; `docs/problems/rate-limited.md` says so.
- The 429 replaces story 6.1's `// shortcut:` (a full queue answered 202 with a log line); the claim's budget, `CancellationToken.None` on the release and audits, and the personal-data rules of 6.1 criteria 5 and 8 apply unchanged.
- No migration: the cap count has no index on (`CustomerId`, `OtpSentAtUtc`). The count method carries `// shortcut: no index for the cap count, add one when LinkingChallenges outgrows a sequential scan` (AD-22).

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- In a throwaway copy, take the cap count outside the advisory-lock transaction: criterion 2's parallel test fails every time, because the held job sits between count and claim.
- In a throwaway copy, make the release clear `OtpSentAtUtc` again: criterion 1 fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Build order: after 6.1 merges (the job, `OtpSends` and the handler are 6.1's uncommitted files until then); it may build beside 6.2, 6.6 and 5.3, and 6.3 waits on it (for `ErrorKind.RateLimited`). With 6.2 it shares `Api/Linking/LinkingEndpoints.cs`, `Infrastructure/Linking/` and the generated contract files in different sections (story 6.2, Build facts); the later merge rebases and regenerates `openapi.json` and `frontend/src/api/generated/`. It shares no file with 6.6 or 5.3.
- Test setup: the full-queue test sets a small `Linking:QueueCapacity` and holds the job, so the test does not need over 100 starts. The exactly-1-hour case seeds at a microsecond-aligned instant so the stored value equals now minus 1 hour exactly (review-story Known pitfall 2).
- Touches:
  - `backend/src/DisputeDesk.Domain/ErrorKind.cs` (`RateLimited`)
  - `backend/src/DisputeDesk.Api/Errors/ErrorResults.cs` (429 `rate-limited`)
  - `backend/src/DisputeDesk.Infrastructure/Linking/` (the locked transaction, the cap count, the `capped` count, `OtpSends.cs` release, `LinkingOptions` bounds)
  - `backend/src/DisputeDesk.Application/Linking/LinkingChallenge.cs` (doc on `OtpSentAtUtc`: when the send was claimed) and `Linking/StartLinking/` (the full-queue result and the `LinkingRateLimited` save)
  - `backend/src/DisputeDesk.Api/Linking/` (the 429 on the start operation)
  - `docs/problems/rate-limited.md` (the full-queue case has no `Retry-After`)
  - `backend/openapi.json`, `frontend/src/api/generated/` (the 429 response on start)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Linking/` (new cap, parallel, exception and full-queue tests; `LinkingOtpSendTests.cs` release assertions change), `backend/tests/DisputeDesk.ArchitectureTests/ProblemCatalogueTests.cs`
- No migration.

## References

- parent — docs/tickets/epic-6-linking/epic-6-linking.md
- architecture — docs/architecture/architecture.md, AD-3, AD-12, AD-14a, AD-22, AD-26
- prd — docs/prd/prd.md, FR-4, FR-10, NFR-SEC-4, NFR-OBS-3, NFR-TEST-3
- challenge — docs/verification/6.5-challenge.md
- review — docs/verification/6.5-prebuild-review.md
- security — docs/verification/6.5-prebuild-security.md
- standard — <https://www.rfc-editor.org/rfc/rfc6585#section-4>
- standard — <https://learn.microsoft.com/dotnet/core/extensions/channels>
- standard — <https://learn.microsoft.com/ef/core/saving/transactions>
- standard — <https://www.postgresql.org/docs/current/functions-admin.html#FUNCTIONS-ADVISORY-LOCKS>
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options>
- standard — <https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html>

## Notes

- Risk high: the cap is the per-customer guard against SMS flooding; beyond the criteria, the security agent runs the parallel test against the Compose stack.
- Decision: 2026-10-30 (Sofia, 6.1 challenge) — split from 6.1 to keep each story within about 8 criteria (AGENTS.md, Definition of ready).
- Decision: 2026-10-30 (human, 6.5 challenge) — every claim counts toward the send cap, delivered or not; a failed send clears the OTP but never frees a cap slot, so at most three SMS reach a customer per hour (FR-4). It replaces the 6.1-challenge line "a failed send counts nothing toward the cap"; AD-12 and FR-4 are amended (docs/verification/6.5-challenge.md, Human decision).
- Decision: 2026-10-30 (human, 6.5 challenge) — the full-queue 429 uses a new `ErrorKind.RateLimited` mapped to 429 `rate-limited` in AD-3, so `ErrorResults.ToProblem` stays the one place an error becomes HTTP; the limiter's `OnRejected` (story 6.3) and the full queue share the problem type and page.
- Decision: 2026-10-30 (Sofia, 6.5 pre-build reviews) — 6.1 security F2 is settled: a stamped, undelivered claim counts (decision above), and an unexpected job exception records `LinkingStart` `Failed` (criterion 3). The "verifies as a wrong code" check of the full-queue blank moves to story 6.2, which builds after this story.
- Decision: 2026-10-30 (Sofia, 6.5 pre-build reviews) — build order 6.1 → 6.5 → 6.2 → 6.3, one at a time: all four change the linking job, handler or endpoint files.
- Resolution: 2026-10-30 (Sofia, parallel re-slice) — 6.5 and 6.2 no longer build one at a time: the full-queue blank check, the one thing 6.2 needed from this story, moved to story 6.3, which waits on both; this story and 6.2 edit different sections of the files they share. It replaces the build-order decision above (6.1 → 6.5 → 6.2 → 6.3) and its line that 6.2 holds the full-queue check. No criterion changed here apart from that pointer, and no PRD requirement or architecture decision changed.
- Assumption: 2026-10-30 (Sofia) — a full-queue start keeps its `LinkingStart` `Succeeded` entry and `started` count and adds `LinkingRateLimited` `Rejected`; the challenge is saved before `TryWrite`, so a queued job never runs ahead of its row. Confirm at the human review of the diff.
- Assumption: 2026-10-30 (Sofia) — a failed `LinkingRateLimited` save still answers the 429 and logs an error, as 6.1 criterion 7 treats a failed job audit save. Confirm at the human review of the diff.
- Assumption: 2026-10-30 (Sofia) — the bounds of `QueueCapacity` (1 to 1000) and `Budget` (1 to 60 seconds) are wide enough for tests and production defaults of 100 and 10 seconds.
- Resolution: 2026-10-30 (Sofia, after the 6.5 verify) — criterion 5 now says plainly that the budget is 1 to 60 seconds inclusive and that sub-second values (999 milliseconds named in the When list) are refused at start-up; the bound itself is unchanged (Assumption above), and no PRD requirement or architecture decision changed (docs/verification/6.5-verify.md, Supervisor disposition).
