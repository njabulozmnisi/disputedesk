---
id: 3
type: story
title: "SLA sweep escalates overdue disputes"
parent: epic-5-operations
covers: [R22, R26, R27, R18, R23, R29, R36]
after: [2, 4, 4.1]
refined: false
hitl: false
risk: high
---

# SLA sweep escalates overdue disputes

## Description

Adds the Worker's SLA sweep (AD-18): a hosted service on a `PeriodicTimer` claims overdue disputes in batches of at most 50 with `FOR UPDATE SKIP LOCKED`, loads each by replay and escalates it through `Dispute.Escalate` as `automation`; then it claims disputes whose SLA is due within 48 hours and records `SlaWarningRaised` once per dispute through `Dispute.RecordSlaWarning`. Each batch is one transaction that holds its row locks from claim to commit. The sweep touches its heartbeat for the Worker health check.

## Requirements

Verify: Worker integration tests with a fake `TimeProvider`, each on its own migrated database, cover the 240-hour boundary, batching, `SKIP LOCKED` against a held lock, two concurrent sweeps, catch-up, a race with an operations escalate and a version clash, the 48-hour warning boundaries, the customer and outbox view, the heartbeat and metrics, and privacy; Domain tests cover `RecordSlaWarning` and replay; architecture tests cover the actor and the bypass.

1. **The 240-hour boundary, as `automation`** (R22; FR-26, NFR-TEST-2)
   **Given** Submitted and Under review disputes
   **When** the sweep runs at submission plus 239:59:59.999999 and at plus 240:00:00 (fake `TimeProvider`)
   **Then** only the second run escalates them, through `Dispute.Escalate` on the dispute loaded by replay, and each new `DisputeEscalated` stored event has `ActorType` `Automation` and `ActorId` `worker:sla-sweep`
   **And** Escalated, Resolved and Withdrawn disputes past the SLA are left unchanged (FR-26)

2. **Bounded claims, one transaction per batch, `SKIP LOCKED` proved** (R22, R26; FR-26, NFR-ARC-4)
   **Given** 120 overdue disputes
   **When** the sweep runs
   **Then** it runs 3 batches of 50, 50 and 20 (three `disputedesk.sla_sweep.batch` spans with their counts), each one transaction holding its row locks from claim to commit, with one `SaveChanges` per dispute inside it, so a dispute that fails rolls back to its savepoint and the rest of the batch commits (Microsoft Learn, EF Core *Transactions*, savepoints); every dispute is escalated once with one stored event and one outbox row
   **And** the claim is one named, parameterised raw-SQL method in Infrastructure returning IDs through `Database.SqlQuery<Guid>`, which no query filter applies to, using `FOR UPDATE SKIP LOCKED` (PostgreSQL, *SELECT*, The Locking Clause) on the partial index `IX_Disputes_SlaDueAtUtc`; the stream load uses `IgnoreQueryFilters(["Ownership"])`, naming that filter only, and only in `Worker/Sla/` (Microsoft Learn, EF Core *Global query filters*, multiple filters; AD-11)
   **And** with a test connection holding `FOR UPDATE` on 5 of the due rows in an open transaction, the sweep escalates the others and returns without waiting, and escalates the 5 on the first loop after that transaction ends
   **And** two sweeps run concurrently with overlapping transactions on the 120 disputes, and each dispute ends with exactly one `DisputeEscalated` and one outbox row

3. **Catch-up after downtime** (R22, R27; FR-26, NFR-RES-1)
   **Given** the Worker was down past the due times of 120 disputes
   **When** it starts
   **Then** it repeats batches until one comes back short, and every overdue dispute is escalated; it touches `/tmp/heartbeat-sweep` after each batch as well as each loop, so a long catch-up does not age the heartbeat
   **And** a short batch means only that no more rows were claimable then: rows another transaction held are taken by a later loop (criterion 2)

4. **Races end in one escalation** (R22, R26; FR-26, AD-15)
   **Given** a claimed dispute, and an operations escalate (story 5.2) on the same dispute at the same moment
   **When** both commit
   **Then** the dispute has exactly one `DisputeEscalated`, the operations call gets 409 `action-not-allowed` or succeeds while the sweep skips, and neither side gets a 500 or a deadlock error
   **And** when the sweep's commit loses a version race (`UQ_StoredEvents_DisputeId_Version`), it rolls back to that dispute's savepoint, loads the stream again by replay, and escalates only if the dispute is still Submitted or Under review and past the SLA; otherwise it skips, and the timeline never shows a duplicate (FR-26)

5. **The escalation as the customer and the outbox see it** (R18, R23, R36; FR-17, FR-28, NFR-OBS-3)
   **Given** an SLA escalation
   **When** the customer detail and the outbox are read
   **Then** the customer sees `underReviewPrioritised` with actor `theBank`, and one `status-changed.v1` with `actorType` `automation` is written whose `traceparent` is the `disputedesk.sla_sweep.batch` span (AD-18)
   **And** after the commit, `disputedesk.sla.escalations` counts 1 and `disputedesk.dispute_actions` counts `action` `escalate` with `actor_type` `automation` (AD-20); a dispute whose save rolled back counts nothing

6. **The SLA warning, 48 hours before the SLA is due, once per dispute** (R22, R18, R29; FR-26, FR-17, FR-30, NFR-API-5)
   **Given** Submitted and Under review disputes whose `SlaDueAtUtc` is exactly now plus 48 hours, now plus 48 hours and one microsecond, now plus one microsecond, and exactly now
   **When** the sweep runs twice
   **Then** the disputes due in exactly 48 hours and in one microsecond each get one `SlaWarningRaised`, the one due in 48 hours and one microsecond gets none, and the one due now is escalated without a warning; no dispute gets a second warning, because the aggregate refuses it and the claim skips disputes whose `SlaWarningRaisedAtUtc` is set, through the partial index `IX_Disputes_SlaWarning` on `SlaDueAtUtc` filtered on `"SlaWarningRaisedAtUtc" IS NULL AND "Status" IN ('Submitted','UnderReview')`; no warning writes an outbox row (AD-14, AD-18)
   **And** the warning claim runs after the escalation claims, the same way, with `@now` and the lead time as parameters; the lead time is one constant, `TimeSpan.FromHours(48)`, beside `RecordSlaWarning` in Domain
   **And** `RecordSlaWarning` and the `SlaWarningRaised` `Apply` case are added here; replaying a stream that holds the warning reproduces its `Disputes` row, `SlaWarningRaisedAtUtc` included (`DisputeReplayTests`' case that expected such a stream to throw becomes a valid stream)
   **And** customers never see the warning (`CustomerTimelineAllowlistTests`, story 3.8) and the operations read shows it (`ReadDisputeTests`, story 5.1)

7. **Sweep heartbeat on the injected clock** (R27; NFR-RES-1, AD-25)
   **Given** the sweep loop
   **When** it runs every 30 seconds on a `PeriodicTimer` built with the injected `TimeProvider` (Microsoft Learn, `PeriodicTimer(TimeSpan, TimeProvider)`)
   **Then** `/tmp/heartbeat-sweep` is touched each loop, and the Worker's Compose health check fails when that file is older than 90 seconds (three missed 30-second loops; AD-25), which an architecture test reads from `docker-compose.yml`

8. **Nothing personal in the sweep's telemetry; the actor stays in the sweep** (R29, R36; NFR-SEC-5, AD-14a, AD-20)
   **Given** a sweep run over a seeded customer's disputes
   **When** its logs, spans and metrics are captured
   **Then** they carry counts and at most dispute IDs, never a `CustomerId`, `AccountRef`, `TransactionId` or the snapshot, and an exception is logged by type only (OWASP Logging Cheat Sheet)
   **And** the automation actor is one constant in `Worker/Sla/` (`ActorType.Automation`, `worker:sla-sweep`), and an architecture test fails on `ActorType.Automation` outside `Worker/Sla/` and the seeder, so no HTTP path can record an automation actor

## Boundaries

- No second scheduler, leader election, repository layer, publish path or concurrency token: one hosted service, the existing commit path (AD-14), the stream's unique `(DisputeId, Version)` (AD-15).
- The sweep never uses the relay's advisory-lock connection; exclusion between Workers is the row locks alone (NFR-ARC-4).
- No audit entry for an SLA escalation or warning (NFR-SEC-10). The warning is never published and never shown to customers (ad-009).
- `Escalate` and its FR-24 cells are story 5.2's and do not change.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- About 5 minutes after a fresh `docker compose up`, the seeded SLA demo dispute shows "Under review (prioritised)" for customer A, with exactly one escalation entry (UJ-4).
- On the running stack, stop the sweep's loop (for example by pausing the Worker container for 100 seconds): the Worker turns unhealthy; resumed, it turns healthy again.
- In a throwaway copy, drop `SKIP LOCKED` from the claim: criterion 2's held-lock test waits and fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes, migration (`Disputes.SlaWarningRaisedAtUtc` timestamptz null and the partial index `IX_Disputes_SlaWarning`, AD-18, AD-22).
- Lane: M2.
- Build order: after 5.2 and 5.4 merge, and after 6.1 merges: 5.3's migration and 6.1's both change `AppDbContextModelSnapshot`, so they never build at once (AD-22). Once 6.1 has merged, it may build beside 6.5, 6.2, 6.6 and 6.3, none of which has a migration: it shares no file with 6.5, 6.2 or 6.3, and shares `docker-compose.yml` (the worker's health check, not the network or the api) and `PlatformScriptTests.cs` (its own test) with 6.6, in different sections; the later merge rebases.
- Layer placement (AD-1, AD-11): the claim queries in `Infrastructure/Sla/` (IDs only, no filter bypass, so `OwnershipRulesTests` keeps `Infrastructure/Sla/SlaQueries.cs` disallowed); the hosted service, the replay with the named bypass, the actor constant and the metrics in `Worker/Sla/`; no `Application/Sla/`.
- Test setup: each Sla test runs on its own migrated database, as the Api integration tests' `CreateMigratedDatabaseAsync` does, because the sweep escalates every overdue dispute it can see.
- Verify against the installed versions, and report the result: the order EF Core sends the `StoredEvents` insert and the `Disputes` update in one `SaveChanges`; criterion 4's race test shows a deadlock if the order is wrong.
- Touches:
  - `backend/src/DisputeDesk.Domain/Disputes/Dispute.cs` (`RecordSlaWarning`, its `Apply` case, the lead-time constant)
  - `backend/src/DisputeDesk.Infrastructure/Sla/` (new: the claim queries)
  - `backend/src/DisputeDesk.Infrastructure/Persistence/` (`SlaWarningRaisedAtUtc`, the partial index, `Migrations/`)
  - `backend/src/DisputeDesk.Worker/Sla/` (new), `Worker/WorkerHost.cs` (the hosted service, the metrics)
  - `docker-compose.yml` (worker health check adds the sweep file)
  - `backend/tests/DisputeDesk.Worker.IntegrationTests/Sla/` (new, with the per-test database)
  - `backend/tests/DisputeDesk.Domain.Tests/Disputes/` (`RecordSlaWarning` cells, `DisputeReplayTests.cs`)
  - `backend/tests/DisputeDesk.ArchitectureTests/` (`SourceRulesTests.cs` for the automation actor, `PlatformScriptTests.cs` for the 90-second threshold)

## References

- parent — docs/tickets/epic-5-operations/epic-5-operations.md
- architecture — docs/architecture/architecture.md, AD-11, AD-13, AD-14, AD-14a, AD-15, AD-18, AD-20, AD-22, AD-25
- prd — docs/prd/prd.md, FR-17, FR-26, FR-28, FR-30, NFR-ARC-4, NFR-RES-1, NFR-OBS-3
- challenge — docs/verification/5.3-challenge.md
- review — docs/verification/5.3-prebuild-review.md
- security — docs/verification/5.3-prebuild-security.md
- standard — <https://www.postgresql.org/docs/current/sql-select.html#SQL-FOR-UPDATE-SHARE>
- standard — <https://learn.microsoft.com/ef/core/saving/transactions>
- standard — <https://learn.microsoft.com/ef/core/querying/sql-queries>
- standard — <https://learn.microsoft.com/ef/core/querying/filters>
- standard — <https://learn.microsoft.com/dotnet/api/system.threading.periodictimer.-ctor>
- standard — <https://docs.docker.com/reference/compose-file/services/#healthcheck>
- standard — <https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html>

## Notes

- Decision: 2026-10-27 — the sweep heartbeat threshold is 90 seconds, three missed 30-second loops, so one slow batch does not mark the Worker unhealthy; the relay threshold stays 30 seconds (AD-25).
- Risk high: double escalation; beyond the criteria, QA watches the seeded demo dispute get exactly one escalation entry.
- Decision: 2026-10-27 — scope: if time runs short, this story goes to `docs/production-path.md` (initiative scope decision).
- Decision: 2026-10-29 — `SlaWarningRaised` is recorded once per dispute before the SLA is due, hidden from customers, never published, visible to operations (ad-009, AD-18).
- Input (from 5.4 refine, 2026-10-30): the seeded open disputes other than the SLA demo dispute have `SlaDueAtUtc` more than 7 days after the seeding instant, so the lead time decided here must be under 7 days, or story 5.4's fixture moves those disputes closer to the anchor in the same change.
- Decision: 2026-10-30 — the SLA warning lead time is 48 hours: the sweep records `SlaWarningRaised` when `SlaDueAtUtc` is 48 hours or less away (day 8 of 10), under 5.4's 7-day limit (human decision; AD-18, FR-26; docs/verification/5.3-challenge.md, Human decision). It settles the earlier Unknown on the lead time.
- Decision: 2026-10-30 (Sofia, 5.3 pre-build reviews) — folded the challenger's, the reviewer's and security's points: the claim returns IDs and the bypass lives only in `Worker/Sla/`; one transaction per batch with a savepoint per dispute; a held-lock test proves `SKIP LOCKED`; the race and version-clash cases; the heartbeat touched after each batch; per-test databases; 5.3 now waits on 5.4.
- Resolution: 2026-10-30 (Sofia, parallel re-slice) — the wait on 6.1 stays: it is the one-migration-at-a-time rule, and 6.1's migration is built and not yet merged. Nothing else in epic 6 blocks this story: 6.5, 6.2, 6.3 and 6.6 add no migration and touch no file of this story's except the two shared with 6.6 in different sections, so it builds beside them once 6.1 merges. No criterion, PRD requirement or architecture decision changed.
