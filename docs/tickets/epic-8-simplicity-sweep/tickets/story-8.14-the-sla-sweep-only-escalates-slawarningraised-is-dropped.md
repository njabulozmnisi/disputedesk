---
id: 14
type: story
title: "The SLA sweep only escalates: SlaWarningRaised is dropped"
parent: epic-8-simplicity-sweep
covers: [R16, R22, R23, R29, R30]
after: [12]
hitl: false
risk: high
refined: true
---

# The SLA sweep only escalates: SlaWarningRaised is dropped

## Description

Slice S11 of the mitigation plan (wave 3), carrying the human's decision D3; the epic's one migration. `SlaWarningRaised` was added to exercise the stored-event pattern, and a requirement was then written around it: no journey, metric or consumer reads it, it is never published, and it costs a second claim query and partial index, a read-model column, a 48-hour lead time, a publish mapping to nothing and gaps in `disputeVersion` that every consumer must tolerate (DES-3, DES-16). This story removes it: the sweep claims and escalates overdue disputes only, one migration drops the column and its index, the event type leaves the domain, the stored-event mapping, the operations timeline and the wire enum, and every stored event of a dispute is published, so `disputeVersion` has no gaps. The deny-by-default timeline allowlist stays proven by the test-only unknown event type.

Human decision, 2026-11-03 (D3): yes, drop `SlaWarningRaised`, its sweep claim, its column and its partial index. FR-26 and R22 are removed or reworded; one migration drops `SlaWarningRaisedAtUtc` and `IX_Disputes_SlaWarning`; the sweep and seeder lose the warning path; `disputeVersion` has no gaps; the glossary's "Timeline" becomes true again.

## Requirements

Verify: the full backend suite passes; `dotnet ef migrations has-pending-model-changes` reports none (CI's EF drift step, `ci.yml:62-65`); the OpenAPI and Orval drift checks pass after regenerating; on a fresh stack (`docker compose down -v`, then `up --wait`), the e2e regression suite and the journeys pass after QA's changes.

Inputs: none for requests. The operations dispute read's timeline response loses one possible `type` value (`slaWarningRaised`).
- Database history: a database that holds `SlaWarningRaised` stored events is unsupported and must be recreated (`docker compose down -v`) before the new code reads it. In such a database replay refuses the unknown type, so the sweep and every action skip or fail that dispute for good (Boundaries).

1. **One migration drops the column and the index** (D3; R29, NFR-DATA-1)
   **Given** the latest migration `Infrastructure/Persistence/Migrations/20261030131025_AddSlaWarning.cs`, which added `Disputes.SlaWarningRaisedAtUtc` and `IX_Disputes_SlaWarning`
   **When** a new migration, named `DropSlaWarning` and generated with `dotnet ef migrations add`, runs at Api start-up
   **Then** its `Up` drops the index and the column, and its `Down` is the one `migrations add` generates, restoring both as `AddSlaWarning` created them (the column nullable `timestamptz`, the partial index with its filter `"SlaWarningRaisedAtUtc" IS NULL AND "Status" IN (…)`), reviewed in the diff against `AddSlaWarning`'s `Up` and proved by the Supervisor round trip; the model snapshot matches; `IX_Disputes_SlaWarning`'s configuration (`DisputeConfiguration.cs:45-46`) and its DDL pin in `DisputeSchemaTests.cs:119-131` go

2. **The event type leaves the domain and the stored-event mapping** (D3; R22, R29, AD-13)
   **Given** `Domain/Disputes/SlaWarningRaised.cs`, `Dispute.SlaWarningLeadTime` (`Dispute.cs:25`), the `SlaWarningRaisedAtUtc` property (`:88`), `RecordSlaWarning` (`:260-273`) and its `Apply` case (`:351-352`), and `StoredEventRow.TypeNamed`'s entry (`Application/Disputes/StoredEventRow.cs:94`)
   **When** this story lands
   **Then** all are deleted, `StoredEventTypesTests`' closed list (`Domain.Tests/StoredEventTypesTests.cs:19`) no longer holds it, and `DisputeSlaWarningTests.cs` and the warning case of `DisputeReplayTests.cs:62-74` are deleted; a stored row with that type name is an unknown type, which replay refuses as it refuses any unknown type (AD-13, unchanged)

3. **The sweep claims and escalates overdue disputes only** (D3; R22, FR-26)
   **Given** `Worker/Sla/SlaSweep.cs` (the `Claim` enum at `:54`, the loop over `{ Claim.Overdue, Claim.Warning }` at `:96`, the `"warning"` span tag at `:118`, the claim choice at `:125-127`, `RecordSlaWarning` at `:202`) and `Infrastructure/Sla/SlaQueries.cs:36-54` (`ClaimWarningsAsync`)
   **When** the sweep runs
   **Then** it runs one claim, the overdue query (PostgreSQL `FOR UPDATE SKIP LOCKED`, unchanged), and escalates each claimed dispute through the same domain operation as today, with the same span, heartbeat, metric and locking behaviour; `ClaimWarningsAsync` and the `Claim` enum are deleted, and `SlaWarningTests.cs` is deleted while `SlaBoundaryTests`, `SlaHeartbeatAndPrivacyTests` and `SlaTelemetryPrivacyTests` pass with their warning lines removed
   **And** with one claim left, the span tag `disputedesk.sla_sweep.claim` (`SlaSweep.cs:118`) is dropped and `LogBatch`'s message loses its `{Claim}` placeholder (`:247-248`); the tests that read the tag (`SweepRig.cs:99`, `SlaBatchTests.cs:30,50,200,205`, `SlaHeartbeatAndPrivacyTests.cs:73`) stop reading it, and the `disputedesk.sla_sweep.claimed` count stays

4. **Every stored event is published, so versions have no gaps** (D3; R23, R28, AD-19)
   **Given** the publish mapping in `Infrastructure/Persistence/CommitRows.cs:33-34` (`SlaWarningRaised => null`, "never published")
   **When** this story lands
   **Then** the mapping has no case that publishes nothing and returns a non-nullable message; its exhaustive test (`PublishMappingTests.cs:35,189`) asserts, for each of the five remaining types (`DisputeRaised`, `ReviewStarted`, `DisputeEscalated`, `DisputeResolved`, `DisputeWithdrawn`), one outbox row whose ID and `DisputeVersion` are the stored event's, with a non-nullable expected type
   **And** a new integration test raises and reviews a dispute, runs the sweep once inside the old 48-hour lead window (nothing is written) and once after the dispute is due (it is escalated), and finds stored events and outbox rows with versions 1, 2, 3 and no gap; without this story the first sweep would write a warning at version 3 and the test fails
   **And** "no gap" describes the versions written to the stored stream and the outbox; delivery stays at least once, with failed or delayed rows possible (AD-17)

5. **The operations timeline and the wire enum lose the type** (D3; R16, R30)
   **Given** `Contracts/Operations/StoredEventType.cs:11` and `Application/Operations/OperationsView.cs:46`
   **When** this story lands
   **Then** the enum member and the view's case are deleted; `backend/openapi.json` (the `StoredEventType` and operations timeline schemas, `:1559`, `:1741`) and `frontend/src/api/generated/` (`model/storedEventType.ts`, `operationsTimelineEntryResponse.ts`) are regenerated, and the diff is reviewed as the contract change D3 makes
   **And** `OperationsViewTests.cs:24` and `Operations/ReadDisputeTests.cs:31` pass with the warning removed

6. **The customer timeline allowlist is still proven** (D3; R18, AD-13)
   **Given** `CustomerTimelineAllowlistTests` (`:21`, `:51-53`) and `CustomerPrivacyTests` (`:68`, `:113`, `:129`)
   **When** this story lands
   **Then** the warning cases are removed and the test-only unknown event type still proves that a stored event outside the allowlist never reaches a customer response (OWASP, deny by default); `NotFoundResponsesTests.cs:74`'s every-type stream drops the warning

7. **The stack and its specs run without the warning** (D3; R22, R24, R35)
   **Given** a fresh stack, whose seeded SLA demo dispute (`backend/fixtures/local.json:282`, `"slaDemo": true`) is due 5 minutes after seeding
   **When** the sweep reaches it
   **Then** it is escalated: its stored events are the seeded ones followed by one `DisputeEscalated`, with no `SlaWarningRaised`
   **And** in the QA stage, `e2e/tests/sla-sweep.spec.ts` (`:9`, `:71`, `:80`, `:88`, `:93`, `:169`, `:202`), `seeded-disputes.spec.ts:24-27` (`sweepEventTypes`) and `dispute-list.spec.ts:125` drop the warning

## Boundaries

- Must not change: the SLA itself (escalation at submission plus 240 hours, FR-26); the escalate operation, its stored event, its outbox row and its event; the sweep's schedule, lock and telemetry apart from the warning tag; the customer views.
- No data migration of existing `StoredEvents` rows: before the first release every database is recreated (AD-22: "the history may be reshaped in place, and every database is recreated when it is"); the merge report tells developers to run `docker compose down -v` once.
- Accepted pre-release incompatibilities (AD-22): an old Worker against the migrated database fails its warning claim on the dropped column; a database that is not recreated keeps its `SlaWarningRaised` rows, which replay refuses, so the sweep skips that dispute forever and its actions fail. Both end with `docker compose down -v`.
- The seeder needs no change: `SeedDispute` has no warning path (D3's "the seeder loses the warning path" is met by AD-30's wording, docs-first).
- `OperationsTimelineEntryResponse.ToStatus` stays nullable (only its doc comment changes), so the timeline schema changes only by the lost enum value.
- No other migration may run in parallel with this story (AGENTS.md, Dispatch rules).

## Supervisor checks

- Throwaway copy: keep `SlaWarningRaised => null` in the publish mapping with the type restored; criterion 4's no-gap test fails.
- Throwaway copy, round trip on a scratch database: apply every migration, then `dotnet ef database update AddSlaWarning` (runs `Down`) and compare `pg_indexes` (`IX_Disputes_SlaWarning` and its predicate) and `information_schema.columns` (`SlaWarningRaisedAtUtc`, nullable `timestamptz`) with the schema before this story; `database update` again shows both absent. Then make `Down` restore the index without its filter; the `pg_indexes` comparison shows the difference.
- Throwaway copy: remove the change but keep the new test; criterion 4's lead-window sweep writes a warning at version 3 and the test fails.
- Docker check (supervisor, throwaway stack): `docker compose down -v`, `up --wait`, wait 6 minutes; the SLA demo dispute's stored events end with one `DisputeEscalated` and hold no `SlaWarningRaised` (criterion 7).

## Build facts

- Owner: backend (backend-builder); QA changes the e2e specs in its stage.
- Tier: High-stakes (migration): the security agent reviews the diff and the human reviews it in a review block.
- Flags: high-stakes, migration, contract.
- Lane: A, after 8.12; no other migration in flight.
- Before the build (supervisor, docs-only change recording D3): PRD FR-26 (`docs/prd/prd.md:483`), the customer timeline allowlist line (`:377`) and NFR-DATA-2's stored event list and publish mapping; the initiative's R18 (`docs/initiative-disputedesk.md:48`) and R22 (`:52`); the architecture's AD-13 method list and closed set, the one publish mapping, AD-14a's closed set, AD-17's sentence that the relay ignores version gaps, AD-18's warning claim and its *Prevents*, AD-19's version-gap sentence, AD-30's lead-time constraint and its *warning lead time* wording, and the open question on "Timeline"; `docs/decisions/ad-006-actor-agnostic-dispute-actions.md` line 9 (`RecordSlaWarning`); and in `docs/decisions/ad-009-stored-events.md` a dated note superseding lines 9 and 14 (the warning type and "`SlaWarningRaised` never"), not a rewrite of the record. `docs/glossary.md`'s "Timeline" stays as it is (it is true again). The builder does not edit them.
- Mechanisms and sources:
  - EF Core migrations (`migrations add`, `Up` and `Down`): <https://learn.microsoft.com/ef/core/managing-schemas/migrations/>.
  - The overdue claim's row locks, unchanged: PostgreSQL `SELECT … FOR UPDATE SKIP LOCKED`, <https://www.postgresql.org/docs/current/sql-select.html#SQL-FOR-UPDATE-SHARE>.
  - Deny by default for the timeline allowlist: <https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html>.
- Packages: none.
- Touches: `backend/src/DisputeDesk.Domain/Disputes/SlaWarningRaised.cs` (deleted), `Dispute.cs`, `Application/Disputes/StoredEventRow.cs`, `CustomerView.cs` (doc comment), `Application/Operations/OperationsView.cs`, `Contracts/Operations/StoredEventType.cs`, `Contracts/Operations/OperationsTimelineEntryResponse.cs:7-8` (doc comment, the source of the `openapi.json` description), `contracts/events/disputedesk.dispute.status-changed.v1.schema.json` (the description's sentence that `disputeVersion` can skip versions goes), `Infrastructure/Persistence/CommitRows.cs`, `DisputeConfiguration.cs`, `Infrastructure/Sla/SlaQueries.cs`, `Infrastructure/Persistence/Migrations/` (new `DropSlaWarning` and snapshot), `Worker/Sla/SlaSweep.cs`, the tests named above, `backend/openapi.json`, `frontend/src/api/generated/` (regenerated; CI fails on drift); QA stage: the three e2e specs in criterion 7.

## Pre-build

- Challenger: `docs/verification/8.14-challenge.md` (simplify).
- Review and security: `docs/verification/epic-8-prebuild-wave3.md`, sections Review 8.14 (migration safe under AD-22's pre-release rule) and Security read 8.14.
- Decisions: none raised.
- Sofia, 2026-11-04: every `[story]` point folded. Review: the no-gap test crosses the old lead window (criterion 4); R18, AD-14a's closed set, AD-18's *Prevents*, ad-006 and a superseding note in ad-009 (docs-first); the span tag dropped and its tests named (criterion 3); `OperationsTimelineEntryResponse.cs` in Touches; the generated `Down` reviewed with a concrete round trip (criterion 1, Supervisor checks); the pre-release incompatibilities (Boundaries). Security: a leftover warning row's effect stated and `docker compose down -v` relied on (Inputs, Boundaries). Challenger: schema-only compatibility (Notes); the event schema description and AD-17's gap sentence (Touches, docs-first); no seeder change (Boundaries); fresh database required (Inputs); "no gap" scoped to written versions (criterion 4); the PostgreSQL source (criterion 3, mechanisms); per-type outbox assertions (criterion 4); NFR-DATA-2, R18 and AD-30 (docs-first).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- plan — docs/verification/simplicity-audit/mitigation-plan.md, section 1 (D3), wave 3, S11
- audit — docs/verification/simplicity-audit/design.md, DES-3, DES-16
- prd — docs/prd/prd.md, FR-26
- architecture — docs/architecture/architecture.md, AD-13, AD-18, AD-19, AD-22, AD-30
- story — docs/tickets/epic-5-operations/tickets/story-5.3-sla-sweep-escalates-overdue-disputes.md

## Notes

- Risk high (proposed): a schema change and a contract change; criterion 1's `Down` and the fresh-stack check prove both directions.
- Human decision, 2026-11-03 (D3), as quoted in the Description; not reopened here.
- Decision: 2026-11-04 (Sofia) — a new forward migration rather than deleting `AddSlaWarning` from the history: AD-22 would allow reshaping, but D3 names "one migration". The forward migration applies to a database that already ran `AddSlaWarning` as schema only; such a database is still unsupported if it holds warning rows (Inputs, Boundaries), so the promise is AD-22's: recreate the database.
- Decision: 2026-11-04 (Sofia) — the span tag `disputedesk.sla_sweep.claim` is dropped, not fixed at `overdue`: with one claim it carries no information.
