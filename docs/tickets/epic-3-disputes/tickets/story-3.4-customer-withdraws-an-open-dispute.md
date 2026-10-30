---
id: 4
type: story
title: "Customer withdraws an open dispute"
parent: epic-3-disputes
covers: [R19, R17, R29, R23, R10, R41, R36]
after: [9, 2.8]
hitl: false
risk: high
---

# Customer withdraws an open dispute

## Description

Adds `Dispute.Withdraw` and `POST /api/v1/disputes/<disputeId>/withdraw`. The withdraw handler is the first to load a dispute by replaying its stored events, decide from that state and write the `Disputes` read model back (story 3.8, AD-13); on a version clash it reloads and decides once more (AD-15). A withdraw appends `DisputeWithdrawn`, published as `status-changed.v1` through story 3.8's mapping. Raise and withdraw refusals are audited as `Rejected` through `AuditLog.Record` (story 2.8, AD-14a).

## Requirements

Verify: Domain unit tests for the withdraw cells; integration tests (Testcontainers PostgreSQL) for withdraw, its inputs and check order, closed disputes, the credential and ownership checks, the version-clash reload, the event, the metrics and privacy, and refusal audit entries pass; the architecture tests pass; `openapi.json` changes only by the withdraw operation and its request, and `frontend/src/api/generated/` matches it.

1. **Withdraw an open dispute** (R19, R29; FR-18, NFR-DATA-5)
   **Given** the owner and a Submitted, Under review or Escalated dispute
   **When** they withdraw with a note
   **Then** the handler loads the dispute by replay (`DisputeReplay.LoadDisputeAsync`), `Dispute.Withdraw(note, actor, instant)` appends one `DisputeWithdrawn` stored event with actor type `Customer` and the login's `sub` as actor ID, and the handler marks the replayed dispute for writing with `db.Entities<Dispute>().Update(dispute)` before its one save (EF Core *Disconnected entities*: `Update` marks every property modified; Microsoft Learn *Event Sourcing pattern*: the read model is written with the event)
   **And** the `Disputes` row then shows Withdrawn at `Version` 2 (for a dispute withdrawn straight after its raise); the commit override throws for a tracked dispute with pending stored events that is neither Added nor Modified, so an `Attach` instead of `Update` fails the save (proved in `DisputeCommitTests`); and the replay test (`StoredEventReplayTests`) also replays a withdrawn dispute's stream and reproduces its row and timeline
   **And** success is 200 with the customer view (`CustomerView.ToResponse`), and the 200 body equals the body of a following `GET /api/v1/disputes/<disputeId>`; the new last timeline item has action `withdraw`, `customerStatus` `withdrawn`, actor `you` and the trimmed note; the top-level `note` stays the raise's note
   **And** the handler reads the clock once and cuts it to whole microseconds, as the raise does (AD-4); with a test clock that is not microsecond-aligned, the timeline item's `occurredAt` in the 200, in the following GET and the outbox payload's `occurredAt` hold the same stored value
   **And** no audit entry is written (ad-009), and withdraw takes no `Idempotency-Key` (AD-16 covers only the raise; RFC 9110 §9.2.2)

2. **Withdraw inputs and check order** (R19; FR-18, NFR-API-2)
   **Given** the inputs: `disputeId`, a route GUID with no route constraint (as the detail); the body, required, a JSON object (`WithdrawDisputeRequest`) whose unknown properties are ignored; and `note`, an optional string of at most 500 UTF-16 code units after trimming (AD-4), where absent, `null`, empty or whitespace-only means no note
   **When** the owner withdraws with notes of 500 and of 501 code units after trimming, a 500-unit note padded with spaces, a whitespace-only note, `{}` and `{"note": null}`; and with a `disputeId` that is not a GUID, no body, the JSON `null`, `[]`, malformed JSON, `"note": 5` and a `text/plain` body
   **Then** 500, the padded note, whitespace-only, `{}` and `null` succeed (the last three with no note on the timeline item), and 501 gets 400 `validation` naming `note` with its own `DisputeErrors` error saying 500 (not `NoteTooLong`, which says 1000 for the raise)
   **And** every binding failure answers before the lookup, as for the raise (Microsoft Learn, *Parameter binding in minimal APIs*): a non-GUID ID, no body, `null`, `[]`, malformed JSON and a wrong JSON type get 400 `validation` with no field named, and the `text/plain` body gets 415 `validation` (`RaiseDisputeInputTests` pattern)
   **And** the endpoint calls `.DisableValidation()` and the domain checks the note after loading and before the status (AD-8: 401, 403, 404, 400, 409): a well-formed body with a 501-unit note gets 404 `not-found` on an unknown ID and 400 `validation` on the owner's Resolved dispute

3. **Closed disputes refuse, and Withdrawn is final** (R19; FR-19, FR-20, FR-24)
   **Given** a Resolved dispute and a Withdrawn dispute of the caller
   **When** the owner withdraws each, withdraws one open dispute twice in a row, and raises the withdrawn dispute's transaction again
   **Then** each withdraw of a closed dispute gets 409 `action-not-allowed` with `customerStatus` (`resolved` or `withdrawn`), decided by the `Dispute` allowed-actions table in Domain (FR-24, AD-13; `Error.ActionNotAllowed`)
   **And** of the two withdraws in a row the second gets 409 `action-not-allowed` with `customerStatus` `withdrawn`, and the stream holds exactly one `DisputeWithdrawn` and the outbox exactly one row for it (the `UQ_StoredEvents_DisputeId_Version` index blocks a second event)
   **And** the raise gets 409 `already-disputed`, and the transaction row stays not disputable

4. **Only the owner, only through the cookie** (R19, R17; NFR-SEC-1, NFR-SEC-2, NFR-SEC-6, NFR-TEST-3)
   **Given** a bearer token, an unlinked login, a cookie without the antiforgery header, customer A, B's open (Submitted) dispute and an unknown ID
   **When** each calls withdraw
   **Then** the bearer token gets 401 `unauthorized`, the unlinked login 403 `linking-required` and the cookie without the header 400 `validation` before any lookup (the `Linked` policy and the customer group's `AntiforgeryFilter`, AD-8, AD-10)
   **And** A's withdraw of B's dispute and of the unknown ID both get 404 `not-found` with bodies identical apart from `traceId`, also when each carries a 501-unit note, because replay reads stored events through the named ownership filter (AD-11; EF Core *Global query filters*; ASVS 5.0 V8.2.2)
   **And** after A's attempt B's stream keeps its latest `Version` with no `DisputeWithdrawn`, no outbox row is added, and B's `Disputes` row stays Submitted
   **And** withdraw joins the independent customer route list in `Ownership/CustomerRoutesTests.cs` and the identical-404 test in `Ownership/NotFoundResponsesTests.cs` (stories 3.3 and 3.9)

5. **Version clash: reload and decide once more** (R29, R19; NFR-DATA-5, NFR-TEST-3)
   **Given** two withdraws of one dispute that the existing `RendezvousInterceptor` holds until both have read the stream, so both replay before either saves; and a withdraw whose save a test-only interceptor (`AppendNextVersionInterceptor`, an EF Core `DbCommandInterceptor`) precedes by committing the next version's stored event and `Disputes` row in its own transaction, armed once or twice
   **When** each commits
   **Then** the clash is the save's `DbUpdateException` on `UQ_StoredEvents_DisputeId_Version`, which the commit override maps through `UniqueViolations` to `DisputeErrors.VersionClash`, clears the change tracker and rethrows as `UniqueViolationException`; the handler catches only that error (no EF Core row token, no `DbUpdateConcurrencyException`; AD-15; Microsoft Learn *Event Sourcing pattern*: optimistic concurrency on append; EF Core *Handling concurrency conflicts*: retry on the new data)
   **And** of the two withdraws exactly one succeeds; the other replays the stream once more, finds Withdrawn and gets 409 `action-not-allowed` with `customerStatus` `withdrawn`
   **And** armed once with a `ReviewStarted`, the withdraw succeeds on its one retry from Under review (`fromStatus` `underReview`), with the instant the later of its use-case instant and the stream's latest `OccurredAtUtc` (AD-4)
   **And** armed twice (a `ReviewStarted`, then a `DisputeEscalated`), the second clash reads the `Disputes` row once and answers 409 `action-not-allowed` with `customerStatus` as last read (`underReviewPrioritised`) and makes no further decision attempt (AD-15, decided 2026-10-30); there is never a second retry

6. **Status-changed event** (R23; FR-28, NFR-TEST-4)
   **Given** a successful withdraw
   **When** the outbox row is read
   **Then** it is a CloudEvents 1.0 structured `disputedesk.dispute.status-changed.v1` with the AD-19 attributes, `id` equal to the `DisputeWithdrawn` stored event ID, and data `disputeId`, `fromStatus`, `toStatus` `withdrawn`, `action` `withdraw`, `actorType` `customer`, `occurredAt` and `disputeVersion`, valid against the schema committed by story 3.8, and with no note

7. **Withdraw metrics, with no personal data** (R36; NFR-OBS-3, NFR-SEC-10)
   **Given** a withdraw of a dispute raised 30 minutes earlier, one that succeeds on the retry, and refused withdraws (404 and 409)
   **When** metrics, logs and spans are collected
   **Then** `disputedesk.dispute_actions` rises by exactly 1 per successful withdraw, the retried one included, with exactly the labels `action` `withdraw` and `actor_type` `customer` (AD-20), and refusals add nothing
   **And** `disputedesk.disputes.withdraw_age`, a histogram with unit `s` and no labels, records the withdraw instant minus `CreatedAtUtc` in seconds (1800 here), with explicit bucket boundaries 60, 300, 900, 1800, 3600, 7200, 21600, 86400, 604800 and 2592000 set through `InstrumentAdvice<double>.HistogramBucketBoundaries`, so SM-C3 (within 1 hour) is readable (Microsoft Learn, *Creating metrics*: histogram advice; OpenTelemetry semantic conventions, units)
   **And** `StoredEventPrivacyTests` extended to a successful withdraw and to the 404 and 409 refusals finds the note, the login `sub` and the customer ID in no log line, exported span attribute or metric label (OWASP Logging Cheat Sheet, data to exclude; ASVS 5.0 V16.2.5)

8. **Refusals audited as `Rejected`, only through `AuditLog.Record`** (R10, R41; FR-10, NFR-SEC-10)
   **Given** customer A's raises that get 404 `not-found`, 409 `already-disputed` (also after losing the unique-index race) or 422 `not-disputable`, and A's withdraws that get 404 `not-found` (B's dispute and an unknown ID) or 409 `action-not-allowed` (a closed dispute and each clash outcome)
   **When** each answers
   **Then** each writes one `AuditLogEntries` row through `AuditLog.Record` (AD-14a; OWASP Logging Cheat Sheet), and the test checks every column: `Action` `Raise` or `Withdraw`, `Result` `Rejected`, `ActorType` `Customer`, `ActorId` A's `sub`, `CustomerId` A's; `DisputeId` A's dispute for the two 409s and null for every 404 and the 422; no column holds B's dispute ID or customer ID; the rows for B's dispute and for the unknown ID are equal apart from `Id`, `OccurredAtUtc` and `TraceId`
   **And** none is written for a success, a replayed stored 4xx under the same `Idempotency-Key`, 401, 403, the antiforgery 400, a binding 400 or 415, the post-load 400s (withdraw note over 500; raise expected amount not below the transaction amount), the filter's 409 `idempotency-in-progress` and 422 `idempotency-mismatch`, and raise 503 `core-banking-unavailable`: these are validation or dependency errors, not a refusal on rule or status (AD-3, AD-14a)
   **And** a failed audit save (`FailAuditInsertInterceptor`) answers 500, as sign-in does (story 2.8), and for the raise releases the idempotency key
   **And** a source-rules test, following `AuditLogRulesTests`, fails when any production file other than `AuditLog.cs` (configuration, `AppDbContext` and migrations excluded) names `AuditLogEntry` and constructs or adds one, with sample-line theories for both `new AuditLogEntry` and the target-typed `Entities<AuditLogEntry>().Add(new() { ... })`

## Boundaries

- The raise's behaviour is unchanged apart from its refusal audit rows: its checks, 1000-unit note limit (`Dispute.NoteMaxLength`, `DisputeErrors.NoteTooLong`), idempotency and responses stay as they are.
- A raise refusal commits in two steps: the handler's save writes the audit row, then the `IdempotencyFilter` stores the 4xx. A request that dies between them leaves the key `InProgress`, and a take-over after 60 seconds may write a second `Rejected` row. This over-recording is accepted; the two commits are not merged (AD-16). After a lost race the audit save runs on the cleared change tracker, as the handler already does.
- After a second version clash the reported status is as last read and may already be stale when the response arrives (AD-15, decided 2026-10-30).
- EF Core keeps logging the expected 23505 failures on `UQ_Disputes_AccountRef_TransactionId` and `UQ_StoredEvents_DisputeId_Version` at Error: no log filter is added (`ConfigureWarnings` on `CoreEventId.SaveChangesFailed` or `RelationalEventId.CommandError`, or a category level, would hide every failed save). Npgsql's `Include Error Detail` stays off (its default), so the logged 23505 holds no key values. No test.
- Problem `title`s do not change in this story (moved to its own Small-tier change, epic Notes).
- Customer routes get no rate limiter in this story (a production-path item, through story 7.6).
- Story 3.3's detail and 3.9's list, their route lists, the identical 404 and the allowlist stay green; this story only adds the withdraw route to the lists.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- In a throwaway copy, remove the retry (answer the first clash with 409): criterion 5's armed-once test fails.
- In a throwaway copy, use `Attach` instead of `Update` for the replayed dispute: the save throws and criterion 1's row test fails; with the override's new check also removed, the replay-and-row test fails because the row keeps its old status.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2, after 3.9 (both edit the endpoint, contract and route-list files).
- Names: the handler is `WithdrawDisputeHandler` in `Application/Disputes/WithdrawDispute/`; the request is `WithdrawDisputeRequest` and the route `DisputeRoutes.Withdraw` (`/disputes/{disputeId}/withdraw`) in Contracts; the metrics are `DisputeActionMetrics` in `Api/Disputes/`, beside `RaiseMetrics`.
- Touches:
  - `backend/src/DisputeDesk.Domain/Disputes/Dispute.cs` (`Withdraw`, the allowed-actions table, the 500-unit withdraw note limit), `DisputeErrors.cs` (the withdraw note error, the action-not-allowed error)
  - `backend/src/DisputeDesk.Application/Disputes/WithdrawDispute/` (new); `Application/Disputes/RaiseDispute/RaiseDisputeHandler.cs` (refusal audit)
  - `backend/src/DisputeDesk.Contracts/Disputes/` (`WithdrawDisputeRequest.cs` new; `DisputeRoutes.cs` gains the withdraw pattern)
  - `backend/src/DisputeDesk.Infrastructure/Persistence/AppDbContext.cs` (the override refuses a dispute with pending stored events that is neither Added nor Modified)
  - `backend/src/DisputeDesk.Api/Disputes/` (`DisputeEndpoints.cs`, `DisputeActionMetrics.cs` new)
  - `backend/openapi.json`, `frontend/src/api/generated/` (regenerated with `npm run generate`; CI's Orval drift step fails otherwise)
  - `backend/tests/DisputeDesk.Domain.Tests/Disputes/` (withdraw cells)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Disputes/Withdraw/` (new); `Disputes/` (`RaiseDisputeRefusalTests.cs`, `StoredEventReplayTests.cs`, `StoredEventPrivacyTests.cs`, `DisputeCommitTests.cs`, `DisputesApi.cs` helpers)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Infrastructure/` (`AppendNextVersionInterceptor.cs` new)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Ownership/` (`CustomerRoutesTests.cs`, `NotFoundResponsesTests.cs`)
  - `backend/tests/DisputeDesk.ArchitectureTests/AuditLogRulesTests.cs`
- Used unchanged: `Application/Audit/AuditLog.cs`, `Application/Disputes/DisputeReplay.cs`, `Application/Disputes/CustomerView.cs`, `Infrastructure/Persistence/UniqueViolations.cs`, `Api/Disputes/Idempotency/`, and in tests `Api.IntegrationTests/Infrastructure/RendezvousInterceptor.cs` and `FailAuditInsertInterceptor.cs`.
- Not touched: `Infrastructure/DependencyInjection.cs` (no log filter), `Api/Errors/ProblemDetailsSetup.cs`, `Api/Bff/`, `Bff/` tests, migrations.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md
- architecture — docs/architecture/architecture.md, AD-3, AD-4, AD-8, AD-10, AD-11, AD-13, AD-14, AD-14a, AD-15, AD-16, AD-19, AD-20
- decision — docs/decisions/ad-009-stored-events.md
- prd — docs/prd/prd.md, FR-18, FR-19, FR-20, FR-24, FR-28, SM-C3, NFR-OBS-3, NFR-SEC-10
- standard — <https://learn.microsoft.com/ef/core/saving/disconnected-entities>, `Update` on a disconnected entity
- standard — <https://learn.microsoft.com/azure/architecture/patterns/event-sourcing>, Event Sourcing pattern
- standard — <https://learn.microsoft.com/ef/core/saving/concurrency>, handling concurrency conflicts
- standard — <https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors>, EF Core interceptors
- standard — <https://learn.microsoft.com/ef/core/querying/filters>, global query filters
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding>, parameter binding in minimal APIs
- standard — <https://learn.microsoft.com/dotnet/core/diagnostics/metrics-instrumentation>, creating metrics, histogram advice
- standard — <https://opentelemetry.io/docs/specs/semconv/general/metrics/>, metric units
- standard — <https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html>, OWASP Logging Cheat Sheet
- standard — <https://www.rfc-editor.org/rfc/rfc9110#section-9.2.2>, idempotent methods
- standard — <https://github.com/OWASP/ASVS>, ASVS 5.0 V8.2.2, V16.2.5
- challenge — docs/verification/3.4-challenge.md
- review — docs/verification/3.4-prebuild-review.md, docs/verification/3.4-prebuild-security.md

## Notes

- Decision: 2026-10-27 — withdraw success is 200 with the updated customer view of the dispute, so the SPA needs no second read.
- Risk high: a final status set wrongly; beyond the criteria, QA checks the transaction row stays not disputable after withdraw.
- Decision: 2026-10-29 — raise and withdraw refusals are audited here, the first story built after both `AuditLog.Record` (2.8) and the stored-event commit (3.8); a version clash reloads and decides once more (AD-14a, AD-15, ad-009).
- Decision: 2026-10-30 (human, 3.4 challenge `[decision]` point) — after a second version clash the response reports the status as last read: one read of the `Disputes` row, no further decision attempt (AD-15 reworded; criterion 5).
- Decision: 2026-10-30 (Sofia, 3.4 pre-build review) — the expected 23505 logs (from 3.2 QA finding 1) stay as EF Core writes them, with no filter and no test; story 7.6 records the alert exclusion in `docs/production-path.md` (Boundaries).
- Decision: 2026-10-30 (Sofia, 3.4 pre-build review) — the problem-title test (from 2.7 review finding 3) moves out to its own Small-tier change (epic Notes), and the carried `SignOutTests` rename moves to `docs/verification/flaky-fixes.md` as a later test-only item; neither belongs to withdraw.
- Decision: 2026-10-30 (Sofia, 3.4 pre-build review) — criteria merged to 8 (AGENTS.md, Definition of ready): "Withdrawn is final" joins "Closed disputes refuse", the credential checks join the identical 404, and the audit source rule joins the refusal audit.
