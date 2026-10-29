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

Extends `fixtures/local.json` and the seeder with disputes in every status and outcome, including the SLA demo dispute. The seeder calls the `Dispute` aggregate methods with back-dated instants resolved from the fixture, and every seeded dispute goes through the full commit path, so it gets its stored events and outbox rows like any other dispute and is published once per fresh database (AD-13, AD-14, AD-30, ad-009).

## Requirements

Verify: A seeder integration test on a fresh database finds every status and outcome for A, B's dispute, the SLA demo timing, one stored event per seeded action, one outbox row per published seeded change, replay reproducing each seeded row, and nothing written by a second run.

1. **Every status and outcome for A** (R24; FR-29)
   **Given** a fresh start with `Seed:Enabled`
   **When** the seeder runs
   **Then** customer A has disputes in Submitted, Under review, Escalated, Withdrawn and Resolved with Upheld and with Rejected, each `Version` equal to its number of stored events, and replaying each stream reproduces its `Disputes` row (AD-13, AD-30)
   **And** A's Submitted and Under review disputes are less than 10 days old

2. **SLA demo dispute** (R24; FR-29, FR-26)
   **Given** the seeding instant
   **When** the demo dispute is written
   **Then** it is Under review with `SlaDueAtUtc` 5 minutes after seeding and `CreatedAtUtc` 240 hours before that

3. **B owns the 404 dispute** (R24; FR-29)
   **Given** customer B's seeded dispute
   **When** customer A requests it
   **Then** A gets 404 `not-found`

4. **Seeded disputes take the full commit path, once** (R24; FR-29, NFR-DATA-2)
   **Given** the seeder
   **When** it runs twice on a fresh database
   **Then** the seeder runs from EF Core's `UseSeeding` and `UseAsyncSeeding` hooks (Microsoft Learn, *Data seeding*), each seeded action commits its stored event and, for a published type, its outbox row through the AD-14 commit, written with no query-filter bypass, and the second run writes nothing, so each seeded event is published once per fresh database

5. **Status changes go through the aggregate with back-dated instants** (R24; FR-29, FR-24)
   **Given** the seeder, which reads the clock once for its anchor (the start of the current South African day, as the stub uses)
   **When** a seeded dispute is written
   **Then** it is created with `Dispute.Raise` and moved only by `StartReview`, `Escalate`, `Resolve` or `Withdraw`, each given its own back-dated instant resolved from the fixture against the anchor, and no status is set directly (AD-4, AD-30)
   **And** customer actions use actor type `Customer` with the fixture login's `userId`, and start review, escalate and resolve use `Automation` / `worker:seeder` (AD-14a, AD-30)
   **And** open seeded disputes other than the SLA demo dispute have `SlaDueAtUtc` more than the SLA warning lead time after the anchor, so the first sweep leaves their streams as seeded

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- After `docker compose up`, the SLA demo dispute shows "Under review (prioritised)" about 5 minutes later once story 3 is merged.
- On a fresh stack, each seeded published event appears once on `disputedesk.disputes`; after `docker compose restart api` no seeded event appears a second time.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Touches: fixtures/local.json (disputes); `fixtures/README.md`; `backend/src/DisputeDesk.Infrastructure/Seeding/`; `backend/tests/DisputeDesk.Api.IntegrationTests/Seeding/`.

## References

- parent — docs/tickets/epic-5-operations/epic-5-operations.md

## Notes

- Decision: 2026-10-26 — Gate 2: AD-13 governs seeding. The seeder changes dispute status only through the `Dispute` aggregate methods, so this story is in epic-5-operations and waits on story 2.
- Decision: 2026-10-27 — scope: if time runs short, this story goes to `docs/production-path.md` (initiative scope decision) and 7.2 is replanned.
- Decision: 2026-10-29 — the seeder calls the domain methods with back-dated instants, and seeded disputes go through the full commit path, outbox rows included, so they are published once per fresh database (FR-29, R24, AD-30, ad-009).
- Assumption: the SLA warning lead time is settled with story 5.3, which is built after this story; until then open seeded disputes other than the SLA demo dispute are raised at most 2 days before the anchor, which keeps them beyond any lead time under 8 days. Confirm when the lead time is decided.
- Open question: the actor value `worker:seeder` is an architecture assumption (AD-14a); the human confirms it before this story starts.
