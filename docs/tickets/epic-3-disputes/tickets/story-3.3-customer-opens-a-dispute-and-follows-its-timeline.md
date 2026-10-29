---
id: 3
type: story
title: "Customer opens a dispute and follows its timeline"
parent: epic-3-disputes
covers: [R16, R17, R18, R11, R30, R31]
after: [8]
hitl: false
risk: high
---

# Customer opens a dispute and follows its timeline

## Description

Adds `GET /api/v1/disputes/<disputeId>` on story 3.8's customer view mapping: the dispute fields read the `Disputes` read model, and the timeline is built from the dispute's stored events through the deny-by-default allowlist (AD-13). The detail read skips a stored event row whose `EventType` it does not know before mapping it, so an unknown row is hidden, never a 500; replay keeps refusing unknown rows (3.8). Proves ownership, the authorization states and privacy on the detail, and starts the independent customer route list. Story 3.9 adds the history list on the same mapping.

## Requirements

Verify: Integration tests for the detail, its timeline and `outcome`, privacy of every field, the identical 404, the authorization states, the independent route list, unknown and corrupt stored rows, the core-banking outage and snapshot precision pass; the architecture tests pass; `openapi.json` changes only by the detail operation, `outcome` on `DisputeResponse` and the `Outcome` enum component.

1. **Detail and timeline** (R16; FR-17)
   **Given** a dispute with several entries, one resolved dispute and one escalated dispute (test setup in Build facts)
   **When** `GET /api/v1/disputes/{disputeId}` is read
   **Then** the body is `DisputeResponse` with the dispute fields from the `Disputes` read model, `outcome` included (`upheld` or `rejected` once resolved, else null), and the timeline built from the dispute's stored events in `Version` order, oldest first, through `CustomerView.ToTimeline`, the same mapping as the raise 201 body (story 3.8)
   **And** the `Location` returned by a raise is fetched in the test and serves this detail: one Contracts class `DisputeRoutes` holds the group-relative pattern `/disputes/{disputeId}` that `MapGet` uses under the `/api/v1` group and the method that builds the `Location` path `/api/v1/disputes/<id>` that the raise handler uses (AD-5: every `Location` points to a real GET)
   **And** `disputeId` binds as a `Guid` with no route constraint, so a value that is not a GUID fails to bind and gets 400 `validation`, as a malformed `limit` does (a `{disputeId:guid}` constraint would make it 404 and is not used; Microsoft Learn, *Parameter binding in minimal APIs*)

2. **Customer never sees internals** (R18, R31; FR-17, NFR-API-5)
   **Given** a dispute acted on by operations and by automation, escalated, with `SlaWarningRaised` in its stream
   **When** its detail is serialised
   **Then** each JSON object has exactly the allowed property names: `DisputeResponse` `disputeId`, `customerStatus`, `outcome`, `reason`, `note`, `transaction`, `expectedAmount`, `disputedAmount`, `createdAt`, `timeline`; the snapshot `transactionId`, `accountRef`, `accountLast4`, `transactionDate`, `description`, `amount`, `maskedCard`; a timeline entry `action`, `customerStatus`, `actor`, `occurredAt`, `note`; money `amount`, `currency`
   **And** no string value in the body equals an actor type value (`customer`, `operations`, `automation`) or the internal status `escalated`, and none contains the dispute's `CustomerId`, its `SlaDueAtUtc` in the wire format, any stored event `ActorId` (the customer's login `sub`, an operations `sub`, `worker:sla-sweep`, `worker:seeder`), any stored `TraceId`, or the `SlaWarningRaised` type name or data; escalated shows as `customerStatus` `underReviewPrioritised`, and the timeline `actor` is `you` or `theBank` (AD-13, Customer wire view; OWASP Authorization Cheat Sheet, deny by default)
   **And** a new Application record that holds the snapshot or the customer ID seals `ToString` as `StoredEvent` does (3.8 Known pitfall)

3. **Identical 404 for another customer's resource** (R17; FR-17, NFR-SEC-6, NFR-TEST-3)
   **Given** customer A; customer B's dispute, whose stream holds every stored event type, and B's transaction; an unknown dispute ID and transaction ID
   **When** A reads the detail with B's dispute ID and with the unknown ID, and raises on B's transaction and on the unknown transaction
   **Then** every response is 404 `not-found`, and on each endpoint the response for B's ID equals the one for the unknown ID in status, `Content-Type`, header names and values (except `Date`, the trace headers, and the session cookie's value and expiry, which the cookie handler renews on every response; each `Set-Cookie`'s name and its `Path`, `HttpOnly`, `SameSite` and `Secure` attributes must match), and body byte for byte once only `traceId` is removed (RFC 9457; AD-11, Not found; PRD 2.4 Key journeys (a))
   **And** neither `detail` nor `instance` carries the requested ID (AD-3: errors never echo a value)

4. **Authorization states and check order on the detail** (R17; NFR-SEC-6, FR-24)
   **Given** `GET /api/v1/disputes/{disputeId}`
   **When** it is called anonymously, by an unlinked login, by a linked customer, by an unlinked login with a `disputeId` that is not a GUID, and with only `Authorization: Bearer <token>` and no cookie
   **Then** anonymous gets 401 `unauthorized` with no redirect and no `WWW-Authenticate` (AD-8); unlinked gets 403 `linking-required`; linked gets 200; unlinked with a bad `disputeId` still gets 403, because checks run 401, 403, then 400 (AD-8, AD-10); bearer-only gets 401, because customer routes accept only the cookie scheme (AD-8) (Microsoft Learn, *Policy-based authorization*)
   **And** none of these calls writes an `AuditLogEntries` row: customer reads, refused ones included, write no audit row (AD-14a, decided 2026-10-29)

5. **Every customer route names `Linked`, checked against an independent list** (R17; NFR-SEC-6, NFR-TEST-3)
   **Given** a test-owned list of the customer routes by method and route pattern: `GET /api/v1/transactions`, `POST /api/v1/disputes`, `GET /api/v1/disputes/{disputeId}`
   **When** the test reads the app's `EndpointDataSource`
   **Then** each listed route exists and requires the `Linked` policy, and the set of endpoints requiring `Linked` equals the list, so a new customer route missing from the list, or a listed route without `Linked`, fails (Microsoft Learn, *Routing in ASP.NET Core*, `EndpointDataSource`; story 3.9 adds `GET /api/v1/disputes`, story 3.4 adds withdraw)
   **And** the identical-404 test of criterion 3 runs on every listed route that takes a resource ID (detail and raise); the transactions list takes none

6. **Unknown and corrupt stored rows** (R18, R31; FR-17, NFR-API-5)
   **Given** a dispute whose `StoredEvents` hold a test-inserted row with an `EventType` no stored event type has, and another dispute with a known-type row whose `Data` cannot be deserialised, each with a marker value in `Data`
   **When** each detail is read
   **Then** the first answers 200 with that row left out: the detail read skips it by `EventType` before deserialising it and logs nothing, while loading by replay keeps refusing such a row (3.8; AD-13)
   **And** the second answers 500 `internal` with only `traceId`, and for both no captured log line, span attribute or response body holds either marker (AD-22: stored events are never logged or traced; OWASP Logging Cheat Sheet, data to exclude)

7. **The detail works while core banking is down** (R11, R16; NFR-RES-2, FR-17)
   **Given** the core-banking stub is unreachable
   **When** the customer opens a dispute
   **Then** the detail answers 200 from the stored snapshot, because the detail read never calls the core-banking port (AD-21: the snapshot is stored at raise, so reads need no core banking)

8. **Snapshot instants keep the precision Postgres stores** (R16; FR-16)
   **Given** a core-banking transaction whose date has 7 fractional digits and a non-zero UTC offset
   **When** the customer raises on it and then reads the detail
   **Then** `transaction.transactionDate` is the same string in the 201 and the detail, because the raise converts the snapshot's date with `ToUniversalTime()` and truncates it to whole microseconds once, where it takes the snapshot (PostgreSQL, *Date/Time Types*: `timestamptz` has microsecond resolution; AD-4: every timestamp is UTC)

## Boundaries

- The allowlist applies to the customer view only. Operations see every stored event type, `SlaWarningRaised` included (FR-30; story 5.1). `CustomerTimelineAllowlistTests` (3.8) already prove the allowlist over every stored event type and a test-only type, and stay green.
- The API contract shapes of 3.8 (`DisputeResponse`, `TimelineEntryResponse`, the `timeline` array) change only by one field: `DisputeResponse` gains `outcome` (nullable, null until resolved), so the raise 201 carries `"outcome": null`.
- The JSON options of 3.8 (`DisputeDeskJson`, `SingleNameEnumConverter`) do not change. Stored event `Data`, outbox `Payload` and stored idempotent responses are not rewritten.
- Customer reads, refused ones included, write no audit row (AD-14a).
- The history list, its paging, wire-format checks over every customer route and the alias rule are story 3.9.
- Replay still throws on an unknown `EventType` (3.8).

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- In a throwaway copy, map a new customer GET without adding it to criterion 5's list: the route-list test fails.
- In a throwaway copy, remove the unknown-row skip from the detail read: criterion 6's unknown-row test fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Names: the handler is `GetDisputeHandler` in `Application/Disputes/GetDispute/`, so the class is not named like its namespace; files that import Domain qualify `Contracts.Disputes.Outcome`.
- Reads: the detail reads the `Disputes` row and the stream in two statements under READ COMMITTED; a change committing between them can show a timeline one entry ahead of the status until the next read, and a `// shortcut:` comment says so.
- Test setup: only `Raise` exists, so tests write later stored events as hand-built `StoredEventRow` rows (the insert-only guard allows adds) and update the `Disputes` row for that one dispute ID, through a helper in `DisputesApi.cs`; `DisputeStatusSetter`, which updates every dispute, is not used for these tests.
- Touches:
  - `backend/src/DisputeDesk.Application/Disputes/GetDispute/GetDisputeHandler.cs` (new)
  - `backend/src/DisputeDesk.Application/Disputes/CustomerView.cs` (`Timeline` renamed `ToTimeline`, `outcome`), `StoredEventRow.cs` (the known-type check), `RaiseDispute/RaiseDisputeHandler.cs` (the `Location` from `DisputeRoutes`, the UTC microsecond snapshot date, the `ToTimeline` call)
  - `backend/src/DisputeDesk.Contracts/Disputes/` (`DisputeResponse.Outcome`, new `DisputeRoutes.cs`)
  - `backend/src/DisputeDesk.Api/Disputes/DisputeEndpoints.cs` (the detail `MapGet`, the handler registration)
  - `backend/openapi.json`
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Disputes/` (new `GetDisputeTests.cs`, `GetDisputeAuthorizationTests.cs`, `CustomerPrivacyTests.cs`, `StoredRowSkipTests.cs`, `SnapshotPrecisionTests.cs`; `CustomerTimelineAllowlistTests.cs` and `StoredEventReplayTests.cs` for the rename; `DisputesApi.cs` helpers; existing raise tests only where they assert the 201's exact fields), never `RaiseDisputeAuthorizationTests.cs`
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Ownership/` (new: `CustomerRoutesTests.cs` for criterion 5, `NotFoundResponsesTests.cs` for criterion 3)
- Used unchanged: `Transactions/TransactionsApi.cs`, `Infrastructure/ApiFactory.cs`, `Infrastructure/TelemetryCapture.cs`, `Contracts/Json/`, `ApiV1.cs`.
- Not touched: `Infrastructure/Persistence/` (no migration), `Program.cs` (`MapDisputes` already maps the group), `Authorization/` tests (2.8's; they stay green because the new route names `Linked`), `ApiReferenceTests.cs`, `Application/Audit/`, CoreBanking stub files, the Worker.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md
- architecture — docs/architecture/architecture.md, AD-3, AD-4, AD-5, AD-8, AD-10, AD-11, AD-13, AD-14a, AD-22
- challenge — docs/verification/3.3-challenge.md
- review — docs/verification/3.3-prebuild-review.md, docs/verification/3.3-prebuild-security.md

## Notes

- Risk high: cross-customer leakage; beyond the criteria, the security agent diffs the 404 responses byte for byte.
- Decision: 2026-10-29 (human, 3.3 challenge) — enum values are read by the framework default, `JsonStringEnumConverter` with camelCase writes and case-insensitive reads (AD-5); a value that names no member still gives 400 (story 3.9, criterion 7).
- Decision: 2026-10-29 (human) — response timestamps keep System.Text.Json's default `+00:00` form; the architecture's Consistency Conventions now say `+00:00` (story 3.9, criterion 7).
- Decision: 2026-10-29 (human, 3.3 pre-build security C7) — customer reads, refused ones included, write no audit row; AD-14a says so (criterion 4).
- Decision: 2026-10-29 (Sofia, pre-build review) — 3.3 split into 3.3 (the detail, buildable now) and 3.9 (the history list, wire format and the alias rule) to keep each within about 8 criteria (AGENTS.md, Definition of ready); the title changed with it, the number kept.
- Settled (3.8 QA, `docs/verification/3.8-qa.md`): `createdAt` and `occurredAt` already carry the six digits Postgres stores; criterion 8 closes the same gap for the snapshot's `transactionDate`.
- Assumption: 2026-10-29 (Sofia, 3.3 challenge) — the customer timeline keeps `action` `escalate` beside `customerStatus` `underReviewPrioritised`. R18 and FR-17 bind the status ("Escalated appears as Under review (prioritised)"), AD-13 keeps the `escalated` status value from customers, and the `TimelineEntryResponse` contract is 3.8's and stays as it is. Confirm at the human review of the diff.
- Carried from 3.2 review R5: the raise `Location` and the detail route share `DisputeRoutes` (criterion 1).
- Carried from 3.8 (naming research, `docs/research/projection-naming/projection-naming.md`): `CustomerView` keeps its name; its `Timeline` method becomes `ToTimeline`, matching `ToResponse` (.NET guidelines: methods are verbs).
- Collision check (2026-10-29): 3.3 runs on M2 while 2.8 finishes on M1. No file in 3.3's Touches is in 2.8's (Bff/, Authorization tests, `ApiReferenceTests.cs`, `TransactionsApi.cs`, `Application/Audit/`, CoreBanking stub files, `RaiseDisputeAuthorizationTests.cs`); 3.3 reads `TransactionsApi.cs`, `ApiFactory.cs` and `TelemetryCapture.cs` unchanged and keeps its route-list test in a new `Ownership/` file. No file overlap with 4.1 or 4.2 (Worker, `Infrastructure/Outbox/`, `Infrastructure/Observability/`, `docker-compose.yml`, `Dockerfile`, `Directory.Packages.props`). No migration.
