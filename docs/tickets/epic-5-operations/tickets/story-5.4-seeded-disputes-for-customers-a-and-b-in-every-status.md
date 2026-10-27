---
id: 4
type: story
title: "Seeded disputes for customers A and B in every status"
parent: epic-5-operations
covers: [R24]
after: [2]
hitl: false
risk: medium
---

# Seeded disputes for customers A and B in every status

## Description

Extends `fixtures/local.json` and the seeder with disputes in every status and outcome, including the SLA demo dispute, changing each status only through the `Dispute` aggregate methods under a fixed seeding clock and writing no outbox rows (AD-13, AD-30).

## Requirements

Verify: A seeder integration test on a fresh database finds every status and outcome for A, B's dispute, the SLA demo timing and zero outbox rows.

1. **Every status and outcome for A** (R24; FR-29)
   **Given** a fresh start with `Seed:Enabled`
   **When** the seeder runs
   **Then** customer A has disputes in Submitted, Under review, Escalated, Withdrawn and Resolved with Upheld and with Rejected, each `Version` equal to its timeline count
   **And** A's Submitted and Under review disputes are less than 10 days old

2. **SLA demo dispute** (R24; FR-29, FR-26)
   **Given** the seeding instant
   **When** the demo dispute is written
   **Then** it is Under review with `SlaDueAtUtc` 5 minutes after seeding and `CreatedAtUtc` 240 hours before that

3. **B owns the 404 dispute** (R24; FR-29)
   **Given** customer B's seeded dispute
   **When** customer A requests it
   **Then** A gets 404 `not-found`

4. **Seeding publishes nothing and is idempotent** (R24; FR-29)
   **Given** the seeder
   **When** it runs twice
   **Then** the seeder runs from EF Core's `UseSeeding` and `UseAsyncSeeding` hooks, the second run writes nothing, and `OutboxMessages` stays empty for seeded disputes, written with no query-filter bypass

5. **Status changes go through the aggregate** (R24; FR-29, FR-24)
   **Given** the seeder and a fixed seeding clock
   **When** a seeded dispute is written
   **Then** it is created with `Dispute.Raise` and moved only by `StartReview`, `Escalate`, `Resolve` or `Withdraw`, each with its `Actor` and an instant from that clock, and no status is set directly

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- After `docker compose up`, the SLA demo dispute shows "Under review (prioritised)" about 5 minutes later once story 3 is merged.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: B.
- Touches: fixtures/local.json (disputes); `fixtures/README.md`; `backend/src/DisputeDesk.Infrastructure/Seeding/`; `backend/tests/DisputeDesk.Api.IntegrationTests/Seeding/`.

## References

- parent — docs/tickets/epic-5-operations/epic-5-operations.md

## Notes

- Decision: 2026-10-26 — Gate 2: AD-13 governs seeding. The seeder changes dispute status only through the `Dispute` aggregate methods under a fixed seeding clock, so this story moved from epic-3-disputes (was 3.5) to epic-5-operations (now 5.4) and waits on story 2.
- Decision: 2026-10-27 — scope: if time runs short, this story goes to `docs/production-path.md` (initiative scope decision) and 7.2 and 7.8a are replanned.
