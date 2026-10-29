---
id: 1
type: story
title: "QA journey UJ-1 on the real stack with axe and ZAP"
parent: epic-7-release
covers: [R35, R40, R33]
after: [6.4, 3.6, 3.10, 3.11, 3.7, 2.5, 2.6, 4.1, 10]
hitl: false
risk: high
---

# QA journey UJ-1 on the real stack with axe and ZAP

## Description

Playwright runs UJ-1 end to end on a fresh stack per QA run and traces it through browser, network, database and events, with accessibility and ZAP baseline scans.

## Requirements

Verify: `npx playwright test` in `e2e/` passes UJ-1 against a fresh stack (`docker compose down -v`, then `docker compose up --wait`); axe and ZAP report no untriaged high finding.

1. **UJ-1 passes** (R35; NFR-TEST-5, FR-1, FR-2, FR-4, FR-6, FR-11, FR-13, FR-16, FR-17, FR-3)
   **Given** a fresh stack
   **When** a new login signs up, verifies in Mailpit, links customer D with the OTP from Mailpit, raises an unrecognised dispute and signs out
   **Then** the row shows "Submitted", the history lists the dispute, the timeline has one entry with actor "You", and a replayed cookie gets 401 after sign-out

2. **Four hops traced** (R35, R23; NFR-TEST-5, FR-27)
   **Given** the raise in UJ-1
   **When** the test inspects the network, the database through `qa_readonly` and the topic
   **Then** the request carried the `Idempotency-Key`, one row exists in each of `Disputes`, `StoredEvents` and `OutboxMessages` (Sent) with the stored event ID equal to the outbox ID and the CloudEvent `id`, no `AuditLogEntries` row records the raise, the login's `SignIn` and `SignOut` audit entries exist, and one `raised.v1` is on `disputedesk.disputes`

3. **Accessibility** (R40; NFR-CODE-6)
   **Given** every screen visited
   **When** `@axe-core/playwright` runs at 1280px and 320px with the tags `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa` and `wcag22aa`
   **Then** no WCAG 2.2 AA violation is reported
   **And** a manual keyboard pass over the same screens (Tab order, visible focus, no keyboard trap; WCAG 2.2 SC 2.1.1, 2.1.2, 2.4.7) is recorded in `docs/verification/7.1-qa.md`, because axe checks only part of WCAG 2.2 AA

4. **Customer D reset by a fresh stack** (R35; FR-29)
   **Given** repeated QA runs
   **When** each run starts
   **Then** the stack is recreated with `docker compose down -v` then `docker compose up --wait`, so customer D starts unlinked from the seed, with no test-only database role and no application endpoint used for the reset

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- ZAP baseline (`zap-baseline.py`) against the running stack: no untriaged high finding.

## Build facts

- Owner: qa (qa agent).
- Flags: none.
- Lane: QA.
- Touches: e2e/ (new: package.json, playwright.config.ts, fixtures, journeys/uj-1.spec.ts); `docs/verification/`.

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md

## Notes

- Decision: 2026-10-26 — Gate 2: QA resets customer D by starting a fresh stack per QA run (`docker compose down -v`, then `up --wait`), not through a test-only database role; `qa_readonly` stays read-only for inspection.
- Decision: 2026-10-27 — WCAG 2.2 AA is checked by axe plus a recorded manual keyboard pass, not proved by axe alone (R40).
- Risk high: the main demo path; beyond the criteria, the run is repeated twice, each on a fresh stack, to prove the D reset.
