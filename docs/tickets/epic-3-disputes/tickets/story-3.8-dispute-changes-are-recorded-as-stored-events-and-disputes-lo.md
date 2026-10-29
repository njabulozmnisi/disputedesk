---
id: 8
type: story
title: "Dispute changes are recorded as stored events, and disputes load by replay"
parent: epic-3-disputes
covers: [R10, R15, R16, R17, R18, R23, R28, R29, R31, R35]
after: [2]
hitl: false
risk: high
---

# Dispute changes are recorded as stored events, and disputes load by replay

## Description

Every dispute change is a stored event in the dispute's own append-only stream in `StoredEvents` (ad-009). The `Dispute` aggregate appends the six stored event types (AD-13, AD-14a), `Dispute.Replay` rebuilds a dispute from its stream, and the one commit path (AD-14) writes the stored event, the `Disputes` read model row and, for a published type, the outbox row in one transaction, with no audit row. One exhaustive mapping in Infrastructure decides what is published. The customer timeline is built from the stored events through a deny-by-default allowlist, and the API contract does not change. `StoredEvents` is created by the migration that creates `Disputes`. Story 3.4 is the first handler that loads a dispute by replay to change it.

## Requirements

Verify: Domain unit tests, Testcontainers integration tests and the architecture tests pass; `dotnet ef migrations has-pending-model-changes` passes; `openapi.json` has no diff; raising for customer A writes one row each in `Disputes`, `StoredEvents` and `OutboxMessages` and no `AuditLogEntries` row.

Request inputs: none new. `POST /api/v1/disputes` keeps the inputs and bounds in story 3.1's table and story 3.2's `Idempotency-Key` rules.

1. **Six stored event types, appended only by the aggregate** (R29, R10; NFR-DATA-1, NFR-SEC-10)
   **Given** the Domain
   **When** its stored event types are listed
   **Then** they are exactly `DisputeRaised`, `ReviewStarted`, `DisputeEscalated`, `DisputeResolved`, `DisputeWithdrawn` and `SlaWarningRaised`, each a sealed record deriving from one abstract `StoredEvent` record, carrying `OccurredAtUtc`, `ActorType`, `ActorId`, `TraceId`, its `Version` and its own data as AD-14a lists (from- and to-status, the note meant for the customer, `outcome` on `DisputeResolved`, the snapshot on `DisputeRaised`, no from-status on `DisputeRaised`)
   **And** `Dispute.Raise` appends exactly one pending `DisputeRaised` with `Version` 1 that holds everything needed to rebuild the dispute (customer ID, `AccountRef`, `TransactionId`, the snapshot, reason, note and amounts), status setters are private, and the aggregate holds no timeline of its own (Microsoft Learn, *Event Sourcing pattern*; AD-13)

2. **Replay with the same `Apply` cases** (R29; NFR-DATA-2, NFR-TEST-2)
   **Given** streams built in a unit test from each lifecycle type (`DisputeRaised`, then `ReviewStarted`, `DisputeEscalated`, `DisputeResolved` or `DisputeWithdrawn`)
   **When** `Dispute.Replay(storedEvents)` folds them in `Version` order
   **Then** each reaches the status and `Version` of its last stored event, through one `Apply` case per type that the aggregate's own methods also use, and replaying a raise stream gives the same state as the `Dispute.Raise` that produced it (AD-13)
   **And** `Replay` returns the dispute's state only; it builds no timeline (criterion 13 builds the customer view from the same stream)
   **And** `SlaWarningRaised` has no `Apply` case yet; story 5.3 adds it with `RecordSlaWarning` (AD-18)

3. **A malformed stream never loads** (R29; NFR-DATA-2)
   **Given** unit-test streams that are empty, start with a type other than `DisputeRaised`, skip a `Version` (1, 3), repeat a `Version` (1, 1), or hold a test-only `StoredEvent` record that has no `Apply` case
   **When** `Dispute.Replay` is called on each
   **Then** it throws, through one guard at the start of `Replay` (non-empty, first type `DisputeRaised`, versions exactly 1 to n in order) and the `switch` over `Apply` cases throwing on a type it does not name, so no partial dispute is ever returned (Microsoft Learn, *Event Sourcing pattern*: the state is rebuilt from the complete stream)
   **And** an integration test that inserts, for customer A's dispute, a `StoredEvents` row whose `EventType` names no stored event record gets loading to fail: the load method throws rather than returning a dispute or not-found, so a handler answers the unexpected-error 500 ProblemDetails, never 404 (AD-3)
   **And** this is separate from the customer view: criterion 13's allowlist skips a type it does not admit for display, and that never makes a stream valid for replay

4. **Loading a dispute by replay** (R29, R17; NFR-DATA-2, NFR-SEC-6)
   **Given** one Application method that reads a dispute's stored events through the `Ownership` filter in `Version` order and passes them to `Dispute.Replay`
   **When** customer A loads their own dispute, customer B's dispute and an unknown ID
   **Then** A's dispute comes back with the state of its stream, and the other two are not found alike (AD-11, AD-13)

5. **The commit writes the stored event, the read model and the outbox row, and no audit row** (R28, R10; NFR-DATA-2, NFR-SEC-10)
   **Given** a successful raise
   **When** it commits
   **Then** one `SaveChangesAsync` writes one `StoredEvents` row, the `Disputes` read model row from the aggregate's new state, and one `OutboxMessages` row, in one transaction (EF Core *Transactions*: one `SaveChanges` is one transaction), and no `AuditLogEntries` row (ad-009, AD-14)
   **And** the stored event ID equals the outbox row ID, both UUIDv7 from the change's instant, and `StoredEvents.OccurredAtUtc` equals `OutboxMessages.CreatedAtUtc` and the CloudEvent `time` (AD-4; CloudEvents 1.0 `id`)
   **And** two parallel raises of one transaction leave exactly one row each in `Disputes`, `StoredEvents` and `OutboxMessages` and none in `AuditLogEntries`

6. **All or nothing** (R28; NFR-DATA-2, NFR-TEST-3)
   **Given** a raise whose save fails in the database after an earlier insert of the same save (the test-only interceptor that fails the `OutboxMessages` insert)
   **When** the transaction rolls back
   **Then** no dispute, stored event or outbox row remains, and the response is a 500 ProblemDetails

7. **A failed save loses nothing silently** (R28; NFR-DATA-2)
   **Given** the commit override in both `SaveChanges` and `SaveChangesAsync`, which takes the pending stored events before it saves
   **When** the save throws any exception (tested on each path with a unique violation, the test-only interceptor's failed insert, and a cancelled token on the async path)
   **Then** the override clears the change tracker (`ChangeTracker.Clear()`, EF Core *Change tracking*) and rethrows, so a second save on the same context writes no dispute, stored event or outbox row, and no dispute is ever saved without its stored event (AD-14, Failed save)
   **And** a unique violation is still rethrown as the mapped `UniqueViolationException` (AD-15) and every other exception is rethrown unchanged

8. **One exhaustive publish mapping** (R23, R31; FR-27, FR-28, NFR-SEC-5, NFR-TEST-4)
   **Given** one `switch` in Infrastructure over every stored event type
   **When** mapping tests pass the `switch` one stored event of each type, built in the test
   **Then** `DisputeRaised` maps to `disputedesk.dispute.raised.v1` exactly as AD-19 and AD-19a define it; `ReviewStarted`, `DisputeEscalated`, `DisputeResolved` and `DisputeWithdrawn` each map to `disputedesk.dispute.status-changed.v1` with the AD-19 data (`disputeId`, `fromStatus`, `toStatus`, `action`, `actorType`, `outcome` on resolve only, `occurredAt`, `disputeVersion`), valid against `contracts/events/disputedesk.dispute.status-changed.v1.schema.json` and its committed sample; and `SlaWarningRaised` maps to nothing (Microsoft Learn, *Domain events versus integration events*)
   **And** a commit test covers only `DisputeRaised`, the one type this story's aggregate can append: a raise writes one outbox row holding that `raised.v1` payload; stories 3.4, 5.2 and 5.3 add the commit test of each type with the method that appends it
   **And** a test enumerates every `StoredEvent` record in the Domain assembly and fails when one is missing from the mapping; a test-only type the mapping does not name throws, so a commit holding it writes nothing and no outbox row
   **And** no outbox payload holds a customer ID, `AccountRef`, `TransactionId`, amount, note or actor identity: a stored event is never published as is

9. **Insert only, one guard for both records** (R10, R29; NFR-SEC-10, NFR-DATA-7)
   **Given** a tracked `StoredEvent` or `AuditLogEntry` that is Modified or Deleted
   **When** the context saves
   **Then** the commit override refuses it before saving and nothing is written (OWASP Logging Cheat Sheet: protect the integrity of event data; AD-14)
   **And** the existing architecture source-rules test covers the stored events as it covers the audit entries: a production file naming `StoredEvent`, its configuration or the `StoredEvents` table that calls `ExecuteUpdate`, `ExecuteDelete`, `ExecuteSql`, `SqlQuery` or any `*SqlRaw` fails it, with sample lines for both names

10. **The unique version is the one concurrency mechanism** (R29; NFR-DATA-5, NFR-TEST-3)
   **Given** two saves that each append a stored event with the same `(DisputeId, Version)`
   **When** both commit
   **Then** `UQ_StoredEvents_DisputeId_Version` (named with `HasDatabaseName`) rejects the second, which `UniqueViolations` maps to the one `Conflict` error, and nothing from that save commits, its `Disputes` row and outbox row included (AD-15; EF Core *Handling concurrency conflicts*)
   **And** the EF Core model of `Disputes` has no concurrency token (no row-version property), and `StoredEvents` has no index besides its key and `UQ_StoredEvents_DisputeId_Version` (no `GlobalPosition` column, no `EventType` index; AD-22)

11. **Stored events carry the ownership filter** (R17; NFR-SEC-6)
    **Given** customer A's context and stored events of customers A and B
    **When** `StoredEvents` is queried directly
    **Then** only A's are returned, because `StoredEvent` carries the `Ownership` named filter through its dispute (EF Core *Global query filters*: filter a required dependent through its navigation), and with no current customer none are returned; the bypass rule of AD-11 is unchanged

12. **Replay reproduces the read model and the timeline** (R29, R16; NFR-DATA-2, NFR-TEST-3)
    **Given** raises through the raise endpoint covering a note, the wrong-amount reason, no note, and a second customer
    **When** a test replays each dispute's stream
    **Then** the state `Replay` returns equals its `Disputes` row column by column
    **And** the customer view built from that same ordered stored-event stream, passed separately through the criterion 13 allowlist, gives a `timeline` equal to the one the API returned for it, item for item and in order (ad-009, AD-13)

13. **The customer timeline comes from stored events through a deny-by-default allowlist** (R16, R18; FR-17, NFR-API-5)
    **Given** a dispute's stored events in `Version` order, the same stream replay reads
    **When** the customer view is built from them (the raise 201 body today)
    **Then** one allowlist in Application admits only the five lifecycle types, checked with a type test over the admitted record types, so `SlaWarningRaised` and every type not on it give no `timeline` item (OWASP Authorization Cheat Sheet: deny by default; AD-13)
    **And** the API contract is unchanged: `openapi.json` has no diff, `DisputeResponse` keeps its `timeline` array of `TimelineEntryResponse`, and every existing raise and idempotency test passes unchanged, the stored 201 body bytes included

14. **One shared JSON options instance** (R23, R15; FR-15, FR-27)
    **Given** the outbox payload, the stored event `Data` and the stored 201 body of story 3.2
    **When** each is serialised
    **Then** all three use one shared, read-only `JsonSerializerOptions` instance defined once in Contracts with the AD-5 settings (Microsoft Learn, *Configure JsonSerializerOptions*: reuse one instance), and the Api's `ConfigureHttpJsonOptions` keeps applying the same settings to the framework's instance

15. **Stored events are never logged or traced** (R31; NFR-SEC-5, NFR-DATA-7)
    **Given** a raise for a seeded customer with the test log and trace capture on
    **When** the commit runs
    **Then** no captured log line, span attribute or metric label holds that customer's ID or `AccountRef`, and `EnableSensitiveDataLogging` is off outside tests (AD-22)

16. **The migrations show only forward development** (R29; NFR-DATA-6, NFR-DATA-4)
    **Given** the migration history
    **When** it is read and applied to an empty database
    **Then** the migration that creates `Disputes` also creates `StoredEvents` with the AD-22 columns (`Id`, `DisputeId` as a foreign key to `Disputes` with restricted delete, `Version`, `EventType` as text, `OccurredAtUtc` as `timestamptz`, `ActorType`, `ActorId`, `TraceId`, `Data` as `jsonb`) and `UQ_StoredEvents_DisputeId_Version`, and no migration creates or drops a timeline table, so no migration creates a table a later one drops (EF Core *Migrations overview*; AD-22)
    **And** every migration's designer file and the model snapshot match the model, and `dotnet ef migrations has-pending-model-changes` passes, as CI runs it (EF Core *Managing migrations*: checking for pending model changes)
    **And** the migration that creates `Disputes` keeps its migration ID, under AD-22's pre-release rule: before the first release no environment holds the schema except disposable local and CI databases, so the history may be reshaped in place and every database is recreated; from the first release migrations are forward-only (EF Core *Managing migrations*: resetting all migrations). CI needs nothing: its integration tests start a fresh Testcontainers Postgres on every run (`.github/workflows/ci.yml`)

## Boundaries

- No new endpoint, request input or response field; `openapi.json` does not change.
- Raise refusals are not audited here and this story writes no audit row at all; story 3.4 owns raise and withdraw refusal auditing through `Audit.Record` (story 2.8; AD-14a).
- No `Withdraw`, `StartReview`, `Escalate`, `Resolve` or `RecordSlaWarning` method: stories 3.4, 5.2 and 5.3 add them, each using the `Apply` case and publish mapping of its type.
- `Program.cs`, `DependencyInjection.cs`, the Bff, Health and Authorization folders, `AuditAction.cs` and `ApiFactory.cs` are not touched (collision check with 2.8).
- `DisputesApi.cs` keeps its existing helper signatures stable (names, parameters and return types), because 2.8 runs `RaiseDisputeAuthorizationTests.cs`, which uses them, in the Production environment; 2.8 does not touch `DisputesApi.cs`.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- On a fresh local database (`docker compose down -v`, then `docker compose up --wait`), `dev-login.sh` as A and raise through the gateway; `qa_readonly` shows one row each in `Disputes`, `StoredEvents` and `OutboxMessages` with one instant, the stored event ID equal to the outbox ID, no `AuditLogEntries` row for the raise, and no timeline table in `\dt`.
- In a throwaway copy, drop `StoredEvent` from the insert-only guard: criterion 9's test fails.
- In a throwaway copy, map a row-version concurrency token on `Disputes`: criterion 10's model test fails.
- In a throwaway copy, add `SlaWarningRaised` to the customer allowlist: criterion 13's test fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes, migration.
- Lane: M2 (parallel with 2.8 on M1; collision check below).
- Touches:
  - `backend/src/DisputeDesk.Domain/Disputes/` (`Dispute.cs`; the stored event records; an `Outcome` enum for `DisputeResolved`; the files deleted under Code to change)
  - `backend/src/DisputeDesk.Application/Disputes/CustomerView.cs`, `DisputeWire.cs`, the stream loading method, `RaiseDispute/RaiseDisputeHandler.cs` (shared JSON options)
  - `backend/src/DisputeDesk.Contracts/Events/` (`DisputeEvents.cs`, the `status-changed.v1` record); `backend/src/DisputeDesk.Contracts/Json/DisputeDeskJson.cs` (the shared instance)
  - `backend/src/DisputeDesk.Infrastructure/Persistence/` (`AppDbContext.cs`, `CommitRows.cs` or the mapping file that holds its publish `switch`, `DisputeConfiguration.cs`, a `StoredEvents` configuration, `UniqueViolations.cs`, `Migrations/20261028084134_AddDisputes.cs` and `.Designer.cs`, `Migrations/20261028150231_AddIdempotencyKeys.Designer.cs`, `Migrations/AppDbContextModelSnapshot.cs`)
  - `contracts/events/disputedesk.dispute.status-changed.v1.schema.json` and its sample
  - `backend/tests/DisputeDesk.Domain.Tests/Disputes/` (`DisputeRaiseTests.cs`, new replay tests)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Disputes/` (`DisputeCommitTests.cs`, `OwnershipFilterTests.cs`, `RaiseDisputeTests.cs`, `RaiseDisputeConcurrencyTests.cs`, `RaisedEventTests.cs`, `DisputeSchemaTests.cs`, `DisputesApi.cs`, new stored-event replay, publish mapping, privacy and concurrency test files); `Infrastructure/FailOutboxInsertInterceptor.cs` only if its insert target changes
  - `backend/tests/DisputeDesk.ArchitectureTests/AuditLogRulesTests.cs`
- Not touched: `backend/openapi.json` (no diff).

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md
- decision — docs/decisions/ad-009-stored-events.md
- architecture — docs/architecture/architecture.md, AD-3, AD-4, AD-11, AD-13, AD-14, AD-14a, AD-15, AD-18, AD-19, AD-19a, AD-22
- conventions — docs/conventions/backend.md, Stored events
- Microsoft Learn — https://learn.microsoft.com/azure/architecture/patterns/event-sourcing
- Microsoft Learn — https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation#domain-events-versus-integration-events
- Microsoft Learn — https://learn.microsoft.com/ef/core/saving/transactions
- Microsoft Learn — https://learn.microsoft.com/ef/core/saving/concurrency
- Microsoft Learn — https://learn.microsoft.com/ef/core/change-tracking/
- Microsoft Learn — https://learn.microsoft.com/ef/core/querying/filters
- Microsoft Learn — https://learn.microsoft.com/ef/core/modeling/indexes
- Microsoft Learn — https://learn.microsoft.com/ef/core/managing-schemas/migrations/
- Microsoft Learn — https://learn.microsoft.com/ef/core/managing-schemas/migrations/managing
- Microsoft Learn — https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/configure-options
- OWASP — https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html
- OWASP — https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html
- CloudEvents — https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md#id

## Notes

- Risk high: the dispute record, customer data and a migration; beyond the criteria, the security agent checks that no query reads `StoredEvents` without the ownership filter outside the AD-11 bypasses and that no log or span carries stored event data.
- Decision: 2026-10-29 — built after 3.2 and before 3.3, on M2, in parallel with 2.8 on M1 (ad-009; lane plan).
- Decision: 2026-10-29 — before the first release the migration that creates `Disputes` is reshaped in place under its migration ID and every database is recreated; from the first release migrations are forward-only (AD-22; challenger `[decision]` settled by the human, `docs/verification/3.8-challenge.md`).
- Decision: 2026-10-29 — raise refusal auditing is story 3.4's, not this story's (AD-14a; challenger `[decision]` settled by the human).
- Decision: 2026-10-29 — the unique `(DisputeId, Version)` index is the one concurrency mechanism, and `Disputes` maps no concurrency token (AD-15, ad-009).
- Assumption: the `status-changed.v1` record, schema and sample are written here, because the exhaustive mapping (criterion 8) maps four types to it; story 3.4 then proves a withdraw writes it.
- Assumption: the `Apply` cases for all five lifecycle types are written here, so the replay tests cover every lifecycle type; the methods that append `ReviewStarted`, `DisputeEscalated`, `DisputeResolved` and `DisputeWithdrawn` arrive in 3.4 and 5.2 and reuse them.
- Input (from 3.2 review R2, 2026-10-28): one shared JSON options instance (criterion 14).
