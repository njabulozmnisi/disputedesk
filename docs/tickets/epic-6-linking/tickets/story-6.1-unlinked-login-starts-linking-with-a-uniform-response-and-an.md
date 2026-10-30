---
id: 1
type: story
title: "Unlinked login starts linking with a uniform response and an OTP sent after the SIM-swap check"
parent: epic-6-linking
covers: [R4, R5, R7, R9, R10, R31, R36]
after: [2.1, 2.3, 2.8, 3.2, 4.1]
refined: false
hitl: false
risk: high
---

# Unlinked login starts linking with a uniform response and an OTP sent after the SIM-swap check

## Description

Adds `LinkingChallenges`, the `Unlinked` policy, `POST /api/v1/linking/start` and the background job: a `BackgroundService` reading a bounded `Channel`, the documented queued-background-task pattern (AD-12). Every well-formed start writes a challenge and gets the same 202; off the request path the job matches, checks the link, runs the SIM-swap check, then claims, sends and, on an SMTP failure, releases the OTP. Every stop is audited with the client IP, which this story adds to `AuditLogEntries`. The job's span uses the `DisputeDesk` ActivitySource that story 4.1 adds (`Telemetry.ActivitySourceName`), so 6.1 builds once 4.1 merges. Story 6.5 adds the send cap and the full-queue 429 on the same path; story 6.2 verifies the code.

## Requirements

Verify: Integration tests prove the identical responses and writes, the format errors, the linking-route states, the check order and fail-closed SIM swap, claim-send-release, supersession before and during a send, every audited stop with its client IP, and that no ID number, full account number, cellphone or plaintext OTP appears in any table, log or span; `openapi.json` changes only by the start operation and its request schema.

1. **One response and one challenge for every well-formed start** (R4, R36; FR-4, NFR-SEC-4, NFR-OBS-3)
   **Given** customer D's details (a match with no linked login), details that match nothing, and customer A's details (a match with a linked login)
   **When** three unlinked logins each start linking
   **Then** each gets 202 with an empty body, and the three responses are equal in status, header names and values (except `Date`, the trace headers, and the session cookie's value and expiry) and body (OWASP Forgot Password Cheat Sheet: the same response whether or not the account exists)
   **And** each admitted request does the same writes, whatever the details (only the parallel loser below writes nothing): in one `SaveChanges` it sets `SupersededAtUtc` on the login's current challenge, inserts one blank challenge, and records `LinkingStart` `Succeeded`; then the request handler calls `TryWrite` on the bounded channel (Microsoft Learn, *Background tasks with hosted services*, Queued background tasks; *System.Threading.Channels*). The restart test (criterion 6) runs with a current challenge present, so it fails if the insert is sent before the update
   **And** the request makes no call to `ICoreBankingClient`, `ISimSwapCheck` or `ISmsSender`: with the core-banking fake blocked, the test still receives the 202 (FR-4: no SMS call inside the request)
   **And** two parallel starts by one login leave exactly one current challenge, because the unique index `UQ_LinkingChallenges_LoginSub` on `LoginSub`, filtered on `"SupersededAtUtc" IS NULL AND "ConsumedAtUtc" IS NULL` and named with `HasDatabaseName` (AD-15; Microsoft Learn, EF Core *Indexes*, Index filter), refuses the second insert; `UniqueViolations` maps that index to a new linking `Error`, so the loser's `UniqueViolationException` is caught by the handler, which answers the same 202 and queues nothing (never a 500)
   **And** when `TryWrite` returns false the request still answers 202 and writes one warning log line holding only the challenge ID, marked `// shortcut: a full queue answers 202, until story 6.5 adds the audited 429`
   **And** `disputedesk.linking.events` counts each accepted start with `event` `started`, created through `IMeterFactory` on the `DisputeDesk` meter (Microsoft Learn, *Creating metrics*), as the raise metrics are

2. **Format errors name the field and never echo it** (R4; FR-4)
   **Given** the request fields and their bounds: `idNumber`, required, a JSON string of exactly 13 ASCII digits whose last digit is the Luhn check digit of the first 12; `accountNumber`, required, a JSON string of 6 to 16 ASCII digits; neither is trimmed or normalised; any other field is ignored
   **When** an unlinked login posts each field missing, null, empty, with a letter, with a space, one digit short, one digit long, 10,000 characters long, as a JSON number, an ID number with a wrong check digit, and a body that is not valid JSON
   **Then** each gets 400 `validation` (naming only the failing field where one field fails), the body never contains the submitted value, and no challenge, audit row or job is written, through the built-in minimal API validation (`AddValidation`, DataAnnotations, and `IValidatableObject` for the check digit, as `RaiseDisputeRequest` does; Microsoft Learn, *Minimal APIs validation*; AD-3: errors never echo a value); a JSON number and invalid JSON get the same `validation` body the raise endpoint gives them

3. **Linking-route states and check order** (R9; FR-9, NFR-SEC-3)
   **Given** `POST /api/v1/linking/start`
   **When** it is called anonymously, with only `Authorization: Bearer <token>` and no cookie, by a linked customer, by a linked customer with a malformed body, by an unlinked login without the antiforgery token, and by an unlinked login with a valid body
   **Then** anonymous and bearer-only get 401 `unauthorized` with no redirect; the linked customer gets 403 `already-linked`, with or without a malformed body, because checks run 401, 403, then 400 (AD-8, AD-10); the missing antiforgery token gets 400, as on the customer routes (AD-7); the unlinked login gets 202
   **And** the route requires a new `Unlinked` policy (the session cookie only, a signed-in login, no `CustomerLinks` row for the login, resolved from the table on every request), and the authorization result handler writes `already-linked` as it writes `linking-required` (Microsoft Learn, *Policy-based authorization*; *Customize the behavior of AuthorizationMiddleware*); the endpoint-enumeration test of story 2.8 names `Unlinked` on the route
   **And** none of the refused calls writes a challenge

4. **SIM-swap check before any OTP, failing closed** (R5; FR-4)
   **Given** customer C, for whom the stub answers a recent SIM swap, and customer D with a check that returns an error and one that times out
   **When** each starts linking and the job runs
   **Then** the job runs in this order: core-banking match, link check, SIM-swap check on the cellphone from the core-banking match, then the claim of criterion 5; for these three no OTP is sent, the challenge keeps `OtpHmac` and `OtpSentAtUtc` null, the 202 was unchanged, and `LinkingSimSwapFailed` `Rejected` is audited (NIST SP 800-63B §3.1.3.3; AD-12, Order)
   **And** a request for customer D carrying an extra `cellphone` field with another number gets the same 202, and the SIM-swap fake and the SMS fake both receive only the cellphone on the bank's record

5. **The OTP is claimed, sent, and released on failure** (R7; FR-4, FR-5)
   **Given** customer D, whose check passes
   **When** the job sends the OTP
   **Then** one conditional `ExecuteUpdateAsync` claims the send: it sets `CustomerId`, `OtpHmac`, `OtpSentAtUtc` (the `TimeProvider` instant) and `ExpiresAtUtc` = `OtpSentAtUtc` plus 5 minutes, only where the row is this challenge and `SupersededAtUtc`, `ConsumedAtUtc` and `OtpSentAtUtc` are null, and `ISmsSender` runs only when it reports exactly 1 row (Microsoft Learn, EF Core *ExecuteUpdate and ExecuteDelete*, Concurrency control and rows affected; decided 2026-10-30)
   **And** the OTP is `RandomNumberGenerator.GetInt32(0, 1_000_000)` written as six digits with leading zeros (NIST SP 800-63B §3.1.3.2); only its HMAC-SHA256 under `Linking:OtpHmacKey`, over the challenge ID and the code, is stored (`HMACSHA256.HashData`; RFC 2104; Microsoft Learn); the message reaches customer D's cellphone on record, and `LinkingOtpSent` `Succeeded` is audited
   **And** `Linking:OtpHmacKey` is bound to `LinkingOptions` and checked at start-up (`ValidateOnStart`): base64 decoding to at least 32 bytes (RFC 2104 §3); a missing, non-base64 or shorter key stops the Api from starting with an error that does not contain the value (Microsoft Learn, *Options pattern*, Options validation)
   **And** when `ISmsSender` throws, or the send is cancelled, a second conditional update clears `OtpSentAtUtc`, `OtpHmac` and `ExpiresAtUtc` only where the row is this challenge and `OtpSentAtUtc` equals the claimed instant, so a failed send counts nothing toward the hourly cap and starts no lifetime; `LinkingOtpSent` `Failed` is audited, the job's span gets a fixed error status with no exception message or event, and the send is not retried
   **And** the 10-second budget covers only the steps before the claim; the send is bounded by SMTP's own timeout, and the release and the audit saves after the claim run on `CancellationToken.None` (as AD-16 deletes a key), so the claim is never left stamped by a cancelled release

6. **Restarting supersedes, before and during a send** (R4; FR-4)
   **Given** one job held before its claim, and another job held inside `ISmsSender` after its claim
   **When** the login starts again in each case and the held job is released
   **Then** the first job's claim updates 0 rows, so it sends nothing and audits `LinkingOtpSent` `Rejected`
   **And** the second job's send completes and audits `LinkingOtpSent` `Succeeded`; the earlier challenge is superseded and the new one is current and blank until its own job claims it, so the late OTP can never verify (story 6.2 accepts only the current challenge's OTP; decided 2026-10-30)

7. **Every stop is audited with the client IP** (R10; FR-10)
   **Given** each stop: start accepted (`LinkingStart` `Succeeded`, in the request); no match (`LinkingNoMatch` `Rejected`); already linked (`LinkingAlreadyLinked` `Rejected`); core banking unavailable, or the job's budget passed before the claim, including during the SIM-swap call (`LinkingStart` `Failed`); SIM swap (`LinkingSimSwapFailed` `Rejected`); superseded before the claim (`LinkingOtpSent` `Rejected`); sent (`LinkingOtpSent` `Succeeded`); send failed (`LinkingOtpSent` `Failed`)
   **When** each entry is written
   **Then** it is written through `AuditLog.Record` (story 2.8; AD-14a) with `OccurredAtUtc`, actor type `Customer`, `ActorId` the login's `sub`, `CustomerId` only once the details matched, and the client IP in a new nullable `inet` column `AuditLogEntries.ClientIp`; the job's entries carry the trace ID of the job's span, which starts on `Telemetry.ActivitySourceName` and carries the request's `ActivityContext` as a span link (OpenTelemetry, *Links between spans*; AD-12)
   **And** the client IP is only `HttpContext.Connection.RemoteIpAddress`, stored as `MapToIPv4()` when it is an IPv4-mapped IPv6 address: a request from the test's fixed address with a forged `X-Forwarded-For` stores the fixed address (trusted forwarded headers are story 6.6); sign-in and sign-out entries keep `ClientIp` null
   **And** when saving a job's audit entry fails, the job logs an error holding only the challenge ID and the action, sets its span status to error, and the next job still runs

8. **Nothing personal is stored, logged or traced** (R31; NFR-SEC-5)
   **Given** a seeded ID number, full account number and cellphone, and the plaintext OTP the SMS fake received
   **When** customer D links through criteria 1, 4 and 5, including a failed send
   **Then** a test reads every row of every table as JSON, the captured logs and the span attributes and events, and finds none of the four values
   **And** the job's in-memory record and `CustomerMatch` (which holds the cellphone) seal `ToString` to print no field (3.8 Known pitfall; OWASP Logging Cheat Sheet, data to exclude), and the SMTP exception of a failed send is neither logged nor recorded on the span, because MailKit's message can name the recipient

## Boundaries

- The uniform 202 covers well-formed, admitted starts across the three match outcomes; 401, 403, 400 and (story 6.5) 429 differ, and none of them depends on the details matching.
- The claim and the send are two steps, never described as atomic. A late send for a superseded challenge is harmless; a crash between claim and send loses that OTP and the person starts again; an Api restart drops queued jobs (AD-12, Restart). Each is a `// shortcut:` comment where it happens, with `docs/production-path.md` as the upgrade.
- The send cap under the advisory lock, the `capped` metric and the full-queue 429 are story 6.5. Verification is story 6.2. The per-login and per-IP limits are story 6.3 and the trusted forwarded headers story 6.6 (split from 6.3, 2026-10-30); until 6.3 merges, start is limited only by the gateway's `limit_req` (AD-23), and epic 6 is not done before 6.3, 6.5 and 6.6 (epic Done when).
- No new repository, queue abstraction, identity abstraction or Worker hand-off: the existing core-banking, SIM-swap and SMS ports, `AuditLog.Record`, `AntiforgeryFilter` and one challenge table (AD-12, Rejected). `IAppDbContext` does not change (AD-2).
- `AuditLogEntries` gains only `ClientIp`. `ActorId` already carries the login (AD-14a); the `LoginSub` and `Details` columns of the starting schema are added by the first story that needs them.
- The `AuditAction` and `AuditResult` sets and the AD-3 slug catalogue do not change.
- `Telemetry.cs` does not change: 4.1's `ActivitySourceName` and `AddSource` are used as merged.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Start linking with customer D's details through the gateway: the OTP lands in Mailpit, and `LinkingChallenges` holds only its HMAC.
- In a throwaway copy, move the SIM-swap call after the claim: criterion 4's test fails.
- In a throwaway copy, remove the release after an SMTP failure: criterion 5's failed-send test fails.
- In a throwaway copy, drop `"SupersededAtUtc" IS NULL` from the claim's condition: criterion 6's held-before-claim test fails.
- In a throwaway copy, remove the `UQ_LinkingChallenges_LoginSub` entry from `UniqueViolations`: criterion 1's parallel test gets a 500 and fails.

## Build facts

- The api service in `docker-compose.yml` gets `stop_grace_period: 35s` (longer than the host's 30-second `ShutdownTimeout`, NFR-RES-5), since 6.1 is the next story to edit that service.

- Owner: backend (backend-builder).
- Flags: high-stakes, migration (one migration: `LinkingChallenges` and `AuditLogEntries.ClientIp`).
- Lane: M2.
- Layer placement (AD-1, AD-2):
  - Application `Linking/`: the `LinkingChallenge` entity (as `AuditLogEntry` lives in Application), the job record (challenge ID, login `sub`, client IP, the request's `ActivityContext`, the ID and account numbers in memory only; sealed `ToString`), and `LinkingErrors` with the error `UniqueViolations` maps.
  - Application `Linking/StartLinking/`: the request handler, a plain class: supersede, insert, `LinkingStart` audit, one `SaveChanges`, catch the mapped `UniqueViolationException`, then `TryWrite` on the `ChannelWriter<LinkingJob>` it takes from DI (a BCL type, so Application needs no Infrastructure reference).
  - Infrastructure `Linking/`: `AddLinking()` (the bounded channel registered as one instance with its writer and reader, `LinkingOptions`, the metrics, the `BackgroundService`), the `BackgroundService` and its job steps, including the link check against `CustomerLinks` (an Infrastructure entity), and the claim and release in their own file that names no audit entry type (`AuditLogRulesTests` flags a file naming the audit entry type beside `ExecuteUpdate`).
  - Api `Linking/`: the endpoint, which registers the handler and counts `started`, as `DisputeEndpoints.cs` and `RaiseMetrics` do.
  - No type is named `Linking` or `StartLinking` (`LayerTests`: no type shares a namespace segment's name).
- Migration columns (AD-22, PascalCase, no time defaults):
  - `LinkingChallenges`: `Id` uuid PK; `LoginSub` text not null; `CustomerId` text null; `OtpHmac` bytea null; `Attempts` integer not null (set to 0 by the handler; story 6.2 counts it); `ExpiresAtUtc`, `OtpSentAtUtc`, `SupersededAtUtc`, `ConsumedAtUtc` timestamptz null (6.2 sets `ConsumedAtUtc`); `CreatedAtUtc` timestamptz not null; index `UQ_LinkingChallenges_LoginSub` as criterion 1.
  - `AuditLogEntries.ClientIp`: `IPAddress?` mapped to `inet` null.
- Job shape (AD-12): channel capacity 100 and the budget (10 seconds) in `LinkingOptions`, so tests can shorten the budget; `Parallel.ForEachAsync` over `ReadAllAsync` with `MaxDegreeOfParallelism = 4`, a new DI scope per job; the budget is a `CancellationTokenSource` on real time (`CancelAfter`), not the test clock, which never advances on its own. Request-body logging stays off on linking routes.
- Configuration: `Linking:OtpHmacKey` is never in `appsettings.json` and never logged; Compose sets `Linking__OtpHmacKey: ${LINKING_OTP_HMAC_KEY:-dev-only-…}` with a base64 dev value of at least 32 bytes (AD-26); `ApiFactory` sets a test key.
- Test setup:
  - Port fakes in one new file under `Linking/`: a core-banking fake that can block before answering, an `ISimSwapCheck` fake that can hold until released (the job held before its claim) or answer fail, error or time out, and an `ISmsSender` fake that records the message, can hold inside the send (the job held after its claim), or throws. `HoldInterceptor` holds only reader commands, so it does not hold the job.
  - `ApiFactory` sets a fixed client address on every request (for example a startup filter setting `Connection.RemoteIpAddress`), since the test server may leave it null; the builder confirms what the installed test server sets and uses the documented way.
  - `OtpSentAtUtc` and `ExpiresAtUtc` tests use a fixed clock not aligned to a microsecond and expect the stored microsecond value (review-story Known pitfall 2).
  - The failed audit save uses `FailAuditInsertInterceptor`. Tests insert their own logins and challenges and assert only on those rows.
- Verify against the installed versions, and report the result:
  - EF Core 10 sends the supersede `UPDATE` before the `INSERT` in one `SaveChanges` under the filtered unique index; the restart test with a current challenge proves it. If it does not, return `blocked` with the generated SQL: an explicit transaction would need `IAppDbContext` to change, which AD-2 forbids.
  - Whether the test server leaves `RemoteIpAddress` null.
  - MailKit's timeout and cancellation behaviour in `SmtpSmsSender` (a cancelled send throws, so the release runs), and whether MailKit's exception messages can carry the recipient.
- Touches:
  - `backend/src/DisputeDesk.Contracts/Linking/` (new: the start request)
  - `backend/src/DisputeDesk.Application/Linking/` (new: `LinkingChallenge`, the job record, `LinkingErrors`) and `Linking/StartLinking/` (new: the handler)
  - `backend/src/DisputeDesk.Application/Audit/` (`AuditLog.Record` takes the client IP; `AuditLogEntry.ClientIp`)
  - `backend/src/DisputeDesk.Application/Abstractions/CustomerMatch.cs` (sealed `ToString`)
  - `backend/src/DisputeDesk.Infrastructure/Linking/` (new: the job, the claim and release, the channel, `LinkingOptions`, the linking metrics, and `AddLinking()`, called from the Api only, as `AddCoreBanking()` and `AddSmsSender()` are: `AddInfrastructure` is shared with the Worker, and only the Api writes `LinkingChallenges`, AD-22)
  - `backend/src/DisputeDesk.Infrastructure/Persistence/` (new `LinkingChallenge` configuration, `UniqueViolations.cs`, `AuditLogEntryConfiguration.cs`, `Migrations/`)
  - `backend/src/DisputeDesk.Api/Linking/` (new: the endpoint)
  - `backend/src/DisputeDesk.Api/Authorization/` (the `Unlinked` policy and requirement, `LinkingRequiredResultHandler.cs`)
  - `backend/src/DisputeDesk.Api/Program.cs` (calls `AddLinking()`, maps the linking group)
  - `docker-compose.yml` (api service only: `Linking__OtpHmacKey`)
  - `backend/openapi.json`, `frontend/src/api/generated/` (regenerated; CI fails on drift)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Linking/` (new, the port fakes included), `Infrastructure/ApiFactory.cs` (the test key, the fixed client address, the fakes), `Authorization/EndpointAuthorizationTests.cs` (`Unlinked` on the start route)
- Not touched: `Infrastructure/DependencyInjection.cs`, `Infrastructure/Observability/Telemetry.cs` (used as 4.1 leaves it), `Infrastructure/Outbox/`, the Worker, `Application/Abstractions/IAppDbContext.cs`, `Application/Disputes/`, `Contracts/Disputes/`, `Api/Disputes/`, the CoreBanking stub, `Infrastructure/Sms/`.
- Overlap (2026-10-30): with 3.3 and 3.9, `backend/openapi.json` (each adds operations; whichever merges later regenerates it, and `frontend/src/api/generated/`); with 4.1, `docker-compose.yml` (4.1 edits the worker service, 6.1 the api service) and a read of `Telemetry.cs`, which 6.1 needs merged first. No other shared file; no migration in 3.3, 3.9 or 4.1.

## References

- parent — docs/tickets/epic-6-linking/epic-6-linking.md
- architecture — docs/architecture/architecture.md, AD-1, AD-2, AD-3, AD-7, AD-8, AD-10, AD-12, AD-14a, AD-15, AD-16, AD-20, AD-21, AD-22, AD-23, AD-26
- prd — docs/prd/prd.md, FR-4, FR-5, FR-9, FR-10, NFR-SEC-3, NFR-SEC-4, NFR-SEC-5, NFR-OBS-3
- challenge — docs/verification/6.1-challenge.md
- review — docs/verification/6.1-prebuild-review.md
- security — docs/verification/6.1-prebuild-security.md
- standard — <https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete>
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services>
- standard — <https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html>
- standard — <https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html>
- standard — <https://learn.microsoft.com/dotnet/core/extensions/channels>
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/validation>
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options>
- standard — <https://learn.microsoft.com/aspnet/core/security/authorization/policies>
- standard — <https://learn.microsoft.com/dotnet/core/diagnostics/metrics-instrumentation>
- standard — <https://learn.microsoft.com/ef/core/modeling/indexes>
- standard — <https://www.rfc-editor.org/rfc/rfc2104>
- standard — <https://pages.nist.gov/800-63-4/sp800-63b.html>, §3.1.3 (out-of-band authenticators)
- standard — <https://opentelemetry.io/docs/concepts/signals/traces/#span-links>

## Notes

- Risk high: enumeration and personal data; beyond the criteria, the security agent compares response sizes and timings across the three match outcomes.
- Decision: 2026-10-30 (human, 6.1 challenge) — the OTP send at the SMTP boundary is claim, send, release on failure: one conditional update claims the send (stamps `OtpSentAtUtc` only if the challenge is still current and unsent; EF Core affected-rows check), then SMTP through `ISmsSender`; if SMTP fails, a second conditional update clears the stamp, so a failed send counts nothing toward the three-per-hour cap and starts no lifetime. A late send for a superseded challenge is harmless because verification accepts only the current challenge's OTP. A crash between claim and send loses that OTP and the person restarts linking. The five-minute lifetime starts at the claim. The two operations are not described as atomic (criteria 5, 6).
- Decision: 2026-10-30 (human, 6.1 challenge) — AD-12 governs: every well-formed start writes a challenge; only a match with no linked login gets an OTP. Writes, timing and response are uniform whether or not the details match (OWASP Forgot Password Cheat Sheet). The supervisor amends FR-4's wording and AD-12 to match (criterion 1).
- Decision: 2026-10-30 (Sofia, 6.1 challenge) — 6.1 split into 6.1 (the start path to a sent OTP) and 6.5 (the send cap and the full-queue 429) to keep each within about 8 criteria (AGENTS.md, Definition of ready); `R6` moved to 6.5.
- Decision: 2026-10-30 (Sofia) — 6.1 owns the `AuditLogEntries.ClientIp` column and its migration (epic open question of 2026-10-28).
- Decision: 2026-10-30 (Sofia, 6.1 pre-build reviews) — folded the 18 review and 10 security points; 6.1 now waits on 4.1 for `Telemetry.ActivitySourceName`; the challenge index uses AD-15's filter and name; the old criterion 7 is split into the audit criterion (7) and the personal-data criterion (8), so no further split.
- Assumption: 2026-10-30 (Sofia) — the fixed 202 body is empty; nothing in it can differ between outcomes.
- Assumption: 2026-10-30 (Sofia) — an account number is 6 to 16 ASCII digits; the PRD says only "malformed" and the fixtures use 10 digits. Confirm at the human review of the diff.
- Assumption: 2026-10-30 (Sofia) — audit actions per stop use the closed AD-14a set as listed in criterion 7: a dependency failure before the claim is `LinkingStart` `Failed`, and a stale job's stop is `LinkingOtpSent` `Rejected`. `ISimSwapCheck` returns only pass or fail, so a recent swap, an error and a timeout all audit `LinkingSimSwapFailed` `Rejected`.
- Assumption: 2026-10-30 (Sofia) — `CustomerId` is set at the claim (AD-12), so a match that stops at the SIM-swap check (customer C) or, from 6.5, at the cap keeps it null on the challenge; its audit entry carries it. FR-4's "records the customer ID resolved at this step" is read that way; the supervisor may align the wording.
- Assumption: 2026-10-30 (Sofia) — the linking error `UniqueViolations` maps for `UQ_LinkingChallenges_LoginSub` is a `Conflict` with the existing slug `action-not-allowed`, as `DisputeErrors.VersionClash` is, because `ProblemCatalogueTests` requires every Application error code to be an AD-3 slug; it never reaches HTTP, since the handler answers 202.
- Assumption: 2026-10-30 (Sofia, 6.1 pre-build reviews) — AD-12's "each within a 10-second budget" is read as covering the steps before the claim; the send is then bounded by SMTP's own 10-second timeout, so a job can take up to about 20 seconds. Cancelling a claimed send would leave the claim stamped or the release cancelled. The supervisor may align AD-12's wording.
- Assumption: 2026-10-30 (Sofia) — the HMAC input is the challenge ID followed by the code, so the same code never hashes alike on two challenges; story 6.2 computes the same input (epic Notes).
