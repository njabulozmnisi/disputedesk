---
id: 4
type: story
title: "Seeded disputes for customers A and B in every status"
parent: epic-5-operations
covers: [R24, R29]
after: [2]
hitl: false
risk: medium
---

# Seeded disputes for customers A and B in every status

## Description

Extends `fixtures/local.json` and the seeder with disputes in every status and outcome, including the SLA demo dispute. The seeder calls the `Dispute` aggregate methods with back-dated instants resolved from the fixture, and commits each seeded dispute's whole stream through the full commit path in one save, so each seeded action has one stored event and one outbox row like any other dispute (AD-13, AD-14, AD-30, ad-009). It is idempotent: per customer it reads each fixture dispute by `accountRef` + `transactionId` through the ownership filter and raises only the missing ones, so a restart adds no rows. The relay publishes each row at least once; consumers deduplicate by CloudEvent `id` (AD-17).

## Requirements

Verify: Seeder integration tests (Testcontainers PostgreSQL, each on its own empty database) on a fixed clock cover every status and outcome for A, B's dispute, the SLA demo timing, one stored event and one outbox row per seeded action, replay reproducing each seeded row, a second run, two instances, the synchronous path, seeding switched off, a downgrade, invalid fixture entries and privacy; the fixture test passes.

1. **Every status and outcome for A** (R24, R29; FR-29, NFR-DATA-2)
   **Given** an empty database, `Seed:Enabled` on, and a fixed clock
   **When** the Api migrates and the seeder runs
   **Then** customer A has disputes in Submitted, Under review, Escalated, Withdrawn, Resolved Upheld and Resolved Rejected, plus the SLA demo dispute (criterion 2); each `Version` equals its number of stored events, and replaying each stream reproduces its `Disputes` row and timeline (`StoredEventReplayTests` pattern; AD-13)
   **And** A's Submitted and Under review disputes are less than 10 days old at the seeding instant (FR-29)

2. **SLA demo dispute** (R24; FR-26, FR-29)
   **Given** a fixed clock that is not microsecond-aligned, read once at the start of seeding and cut to whole microseconds (AD-4)
   **When** the SLA demo dispute is written
   **Then** it is Under review with `SlaDueAtUtc` exactly the seeding instant plus 5 minutes and `CreatedAtUtc` exactly `SlaDueAtUtc` minus 240 hours, as stored values (AD-30), on a transaction at least 11 days old so the raise passes the 90-day rule

3. **B owns the dispute A gets 404 for** (R24; FR-29)
   **Given** customer B's one seeded dispute
   **When** customer A requests it through the customer detail route
   **Then** A gets 404 `not-found`, identical to an unknown ID apart from `traceId` (AD-11; EF Core *Global query filters*)
   **And** `fixtures/README.md` says how to find B's dispute ID, which is a new UUIDv7 on each fresh stack (B's history list)

4. **Seeded disputes take the full commit path, once** (R24, R29; FR-29, NFR-DATA-2)
   **Given** the seeder running from EF Core's `UseAsyncSeeding` and `UseSeeding` (Microsoft Learn, *Data seeding*), inside the migration transaction when no migration is pending and otherwise after EF Core has released the migration lock, so two instances may seed at once and the seed stays idempotent through the unique `(AccountRef, TransactionId)` index, one `SaveChanges` per dispute and the migrator's retry (AD-30), after the links are saved, and only when `20261028084134_AddDisputes` is applied
   **When** it seeds each customer's disputes
   **Then** per customer it builds a second `AppDbContext` over the migrating context's connection (`Database.GetDbConnection()`, as `OutboxRelay` builds its context; EF Core *Transactions*, sharing a connection) with a `CurrentCustomer` set to that customer and its fixture login's `userId`, and reads `Disputes` by `AccountRef` + `TransactionId` through the ownership filter with no bypass (AD-11, AD-30)
   **And** a fixture dispute it finds is skipped whole; one it does not find is raised (`everDisputed` false, from that read) and taken through its actions, all pending together, and committed in one `SaveChanges` through the AD-14 override, so its stream is all or nothing (EF Core *Transactions*: one `SaveChanges` is one transaction)
   **And** after the run there is exactly one stored event and one outbox row per seeded action, each outbox row's ID equal to its stored event ID (AD-14, AD-17)

5. **Idempotent across restarts and instances** (R24; FR-29, NFR-DATA-2)
   **Given** `SeededLinksTests`' four cases extended to disputes, stored events and outbox rows
   **When** the seeder runs twice on one database; two Api instances start together on an empty database; the synchronous path runs (`new AppDbContext(options)` with no customer); seeding is switched off; and the database is downgraded below `AddDisputes`
   **Then** the second run and the second instance add no `Disputes`, `StoredEvents` or `OutboxMessages` row (so a restart publishes nothing new; the relay's at-least-once delivery is AD-17's), the synchronous path seeds the same rows once, seeding switched off adds no `Disputes`, `StoredEvents` or `OutboxMessages` row, and the downgrade does not fail
   **And** seeding writes no `AuditLogEntries` row (ad-009) and adds nothing to `disputedesk.dispute_actions` or `disputedesk.disputes.resolved`

6. **Status changes only through the aggregate, with back-dated instants** (R24, R29; FR-24, FR-29)
   **Given** the anchor (the start of the current South African day, as the stub uses) and the seeding instant
   **When** a seeded dispute is written
   **Then** it is created with `Dispute.Raise` and moved only by `StartReview`, `Escalate`, `Resolve` or `Withdraw`, each with its own instant resolved from the fixture against the anchor (or, for the SLA demo dispute, against the seeding instant), and no status is set directly (AD-4, AD-13, AD-30)
   **And** the raise and withdraw use actor type `Customer` with the fixture login's `userId` (resolved from `fixtures.logins` by key with `Single`, as the link seeder does); start review, escalate and resolve use `Automation` with actor ID `worker:seeder` (AD-14a)
   **And** each seeded snapshot equals the transaction the core-banking stub returns for the same fixture row (date = anchor minus `daysAgo` at `time` in South African time, `accountLast4`, `maskedCard`), proved by a test
   **And** every open seeded dispute (Submitted or Under review, A's and B's) other than the SLA demo dispute has `SlaDueAtUtc` more than 7 days after the seeding instant (raised at most 2 days before the anchor), so the first sweep warns or escalates none of them while the warning lead time is under 7 days (story 5.3)

7. **Fixture disputes are bounded, synthetic and fail loudly** (R24; FR-29, NFR-SEC-6)
   **Given** the fixture inputs, per dispute: `customer`, a fixture customer key that is linked; `accountRef` and `transactionId`, which exist in `transactions`, belong to that customer's account and are unique across disputes; `reason`, one of the six reason names, and `expectedAmount`, a decimal string only for `wrongAmount`; `note`, optional, at most 1000 UTF-16 code units after trimming; `actions`, starting with `raise`, each with `daysAgo` + `time`, or `hoursAfterRaise` for the SLA demo dispute's later actions; `outcome` on resolve; a resolve note of 1 to 500 and a withdraw note of at most 500 code units after trimming; instants strictly increasing, after the transaction's instant and not after the seeding instant
   **When** a test seeds a fixture with an unknown `transactionId`, another customer's transaction, out-of-order instants, an instant after the seeding instant, and an action the aggregate refuses (resolve from Submitted)
   **Then** each throws with a message naming the fixture entry's position (never a customer ID or note), the migrator retries and readiness stays unhealthy; nothing is skipped silently
   **And** a fixture test checks that no seeded note contains any customer's `idNumber`, `cellphone`, `accountNumber` or `displayName`, or a run of 6 or more digits, and that no seeded transaction is one a test or E2E spec names (today `TXN-A-0001`, `-0002`, `-0024`, `-0028`, `-0029` and `TXN-B-0027`)
   **And** the seeder's log line holds counts only: no customer ID, account reference, dispute ID or note (AD-20; ASVS 5.0 V16.2.5)

## Boundaries

- The link seeding is unchanged: its rows, its log line and the four existing `SeededLinksTests` expectations for links stay as they are.
- No new seeding mechanism, API path, SQL script or ownership bypass (AD-30 Rejected; `OwnershipRulesTests` keeps refusing a bypass under `Infrastructure/Seeding/`).
- Kafka delivery stays at least once (AD-17); this story asserts rows, not arrivals.
- `Seed:Enabled` stays off by default and on only in Compose; refusing it in a real deployment is a production-path item (story 7.6's Notes).
- E2E specs that pick a disputable transaction by position see 7 fewer of A's on `ACC-A-1`; QA re-runs them (13 stay disputable).

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- On a fresh stack, every outbox row of a seeded dispute is `Sent`, and the topic `disputedesk.disputes` holds each of their `id`s; after `docker compose restart api`, `OutboxMessages` has no new row and any repeated message on the topic carries an `id` already seen.
- After story 5.3 is merged, about 5 minutes after `docker compose up` the SLA demo dispute shows "Under review (prioritised)" for customer A.
- In a throwaway copy, seed without setting `CurrentCustomer` on the second context: criterion 5's second-run case fails on `UQ_Disputes_AccountRef_TransactionId`.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes. No migration.
- Lane: M2, after 5.2 (it calls `StartReview`, `Escalate` and `Resolve`).
- Touches:
  - `fixtures/local.json` (a `disputes` array), `fixtures/README.md`
  - `backend/src/DisputeDesk.Infrastructure/Seeding/` (`Seeder.cs`; the fixture records)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Seeding/` (new), `Transactions/SeededLinksTests.cs` (the four cases extended)
  - `docker-compose.yml` (the `Seed__Enabled` comment now says links and disputes)
- Used unchanged: `AppDbContext` and its commit override, `Dispute`, `DatabaseMigrator`, `SeedRegistration`, `SeedOptions`.

## References

- parent — docs/tickets/epic-5-operations/epic-5-operations.md
- architecture — docs/architecture/architecture.md, AD-4, AD-11, AD-13, AD-14, AD-14a, AD-17, AD-19, AD-20, AD-30
- decision — docs/decisions/ad-009-stored-events.md
- prd — docs/prd/prd.md, FR-26, FR-29, NFR-DATA-2
- standard — <https://learn.microsoft.com/ef/core/modeling/data-seeding>, `UseSeeding` and `UseAsyncSeeding`
- standard — <https://learn.microsoft.com/ef/core/saving/transactions>, one `SaveChanges` is one transaction; sharing a connection between contexts
- standard — <https://learn.microsoft.com/ef/core/querying/filters>, global query filters
- standard — <https://www.enterpriseintegrationpatterns.com/patterns/messaging/IdempotentReceiver.html>, Idempotent Receiver
- standard — <https://github.com/OWASP/ASVS>, ASVS 5.0 V16.2.5
- challenge — docs/verification/5.4-challenge.md
- review — docs/verification/5.4-prebuild-review.md, docs/verification/5.4-prebuild-security.md

## Notes

- Risk medium: demo data only, but a wrong seed breaks every demo journey; beyond the criteria, QA reads A's history on a fresh stack and checks one dispute per status.
- Decision: 2026-10-26 — Gate 2: AD-13 governs seeding. The seeder changes dispute status only through the `Dispute` aggregate methods, so this story is in epic-5-operations and waits on story 2.
- Decision: 2026-10-27 — scope: if time runs short, this story goes to `docs/production-path.md` (initiative scope decision) and 7.2 is replanned.
- Decision: 2026-10-29 — the seeder calls the domain methods with back-dated instants, and seeded disputes go through the full commit path, outbox rows included (FR-29, R24, AD-30, ad-009).
- Decision: 2026-10-30 (human, 5.4 challenge `[decision]` point 1) — the seeder writes start review, escalate and resolve as actor type `Automation` with actor ID `worker:seeder`; AD-14a records it and the assumption is dropped (criterion 6).
- Decision: 2026-10-30 (human, 5.4 challenge `[decision]` point 2) — at-least-once wording everywhere (PRD FR-29, R24, AD-19, AD-30, ad-009): one stored event and one outbox row per seeded action, no new rows on a restart, published at least once, consumers deduplicate by CloudEvent `id`. AD-17 is unchanged (criteria 4 and 5, supervisor checks).
- Decision: 2026-10-30 (human, 5.4 pre-build review) — idempotency: per customer, a second `AppDbContext` over the migrating connection with `CurrentCustomer` set, reading by `accountRef` + `transactionId` before raising, as AD-30 says (option (a); criterion 4).
- Decision: 2026-10-30 (Sofia, 5.4 pre-build review and security S2) — each seeded dispute's whole stream commits in one `SaveChanges`, so a failure part-way never leaves a dispute at an earlier status that a re-run would skip (criterion 4).
- Decision: 2026-10-30 (Sofia, 5.4 pre-build review) — the warning lead time is measured from the seeding instant, which can be up to 24 hours after the anchor; open seeded disputes other than the SLA demo dispute are raised at most 2 days before the anchor, so they stay clear of any lead time under 7 days. This replaces the earlier assumption; story 5.3 carries the bound as an input (criterion 6; AD-30 reworded).
- Production path: lines for `docs/production-path.md` are in story 7.6's Notes (Input from 5.4, 2026-10-30).
- Resolution (2026-10-30, human decision): criterion 4 follows AD-30 as corrected from the EF Core 10 and Npgsql source (`docs/verification/5.4-fixes.md` item 4, `5.4-verify.md` round 2): seeding after pending migrations runs outside the migration lock; idempotency, not a lock, prevents duplicates.
