---
id: 2
type: story
title: "QA journeys UJ-2, UJ-5a, UJ-5b and UJ-5d"
parent: epic-release
covers: [R35, R40]
after: [1, "5.4"]
hitl: false
risk: medium
---

# QA journeys UJ-2, UJ-5a, UJ-5b and UJ-5d

## Description

Adds the withdraw, ownership, closed-dispute and unlinked-login journeys on seeded data to the suite; each QA run starts on a fresh stack, so seeded data is in its initial state.

## Requirements

Verify: `npx playwright test` passes all five journeys with axe clean on a fresh stack per QA run (`docker compose down -v`, then `docker compose up --wait`).

1. **UJ-2 withdraw** (R35; NFR-TEST-5, FR-18, FR-19)
   **Given** customer A's Under review dispute
   **When** A withdraws with the note "Parcel arrived"
   **Then** the timeline shows Withdrawn with actor "You" and its transaction is not disputable

2. **UJ-5a another customer's dispute** (R35; NFR-TEST-5, FR-17, NFR-SEC-6)
   **Given** B's dispute ID
   **When** A opens it
   **Then** the not-found screen shows and the API body matches an unknown ID's apart from `traceId`

3. **UJ-5b resolved cannot be withdrawn** (R35; NFR-TEST-5, FR-20)
   **Given** A's resolved dispute
   **When** the detail opens and the API is called directly
   **Then** no Withdraw control shows and the API returns 409

4. **UJ-5d unlinked login** (R35; NFR-TEST-5, FR-9)
   **Given** an unlinked login
   **When** it opens `/transactions`
   **Then** `GET /api/v1/transactions` returns 403 `linking-required` and the SPA shows `/link`

5. **Accessibility** (R40; NFR-CODE-6)
   **Given** every screen visited
   **When** axe runs
   **Then** no WCAG 2.2 AA violation is reported

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- None beyond the story flow.

## Build facts

- Owner: qa (qa agent).
- Flags: none.
- Lane: QA.
- Touches: `e2e/journeys/`.

## References

- parent — docs/tickets/epic-release/epic-release.md

## Notes

- Decision: 2026-10-26 — Gate 2: each QA run starts on a fresh stack (`docker compose down -v`, then `up --wait`); UJ-2's withdrawal of A's Under review dispute is reset the same way. The seeded disputes now come from story 5.4 (was 3.5).
