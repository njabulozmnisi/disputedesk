---
id: 3
type: story
title: "SLA sweep escalates overdue disputes"
parent: epic-5-operations
covers: [R22, R26, R27, R18, R23, R36]
after: [2, 4.1]
hitl: false
risk: high
---

# SLA sweep escalates overdue disputes

## Description

Adds the Worker's SLA sweep (AD-18) through `Dispute.Escalate` as `automation`, with its heartbeat in the Worker health check.

## Requirements

Verify: Worker integration tests with a fake `TimeProvider` cover the 240-hour boundary, batching, two sweeps and catch-up.

1. **240-hour boundary** (R22; FR-26, NFR-TEST-2)
   **Given** Submitted and Under review disputes
   **When** the sweep runs at submission plus 239:59:59 and at plus 240:00:00 (fake `TimeProvider`)
   **Then** only the second run escalates them, through `Dispute.Escalate` with actor `automation` and ID `worker:sla-sweep`

2. **Bounded claims, two sweeps** (R22, R26; FR-26, NFR-ARC-4)
   **Given** 120 overdue disputes and two sweeps
   **When** both run
   **Then** claims are batches of at most 50 with `FOR UPDATE SKIP LOCKED`, every dispute is escalated once, and no duplicate timeline entry exists

3. **Catch-up** (R22; FR-26)
   **Given** the Worker was down past several due times
   **When** it starts
   **Then** it repeats batches until one comes back short and every overdue dispute is escalated

4. **Escalation as the customer sees it** (R18, R23; FR-17, FR-28)
   **Given** an SLA escalation
   **When** the customer detail and the outbox are read
   **Then** the customer sees `underReviewPrioritised` with actor `theBank`, and one `status-changed.v1` with `actorType` `automation` is written

5. **Sweep heartbeat and metric** (R27, R36; NFR-RES-1, NFR-OBS-3)
   **Given** the sweep loop
   **When** it runs every 30 seconds
   **Then** `/tmp/heartbeat-sweep` is touched each loop, the health check fails when it is older than 30 seconds, and `disputedesk.sla.escalations` counts

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- About 5 minutes after a fresh `docker compose up`, the seeded demo dispute shows "Under review (prioritised)" for customer A (UJ-4).

## Build facts

- Owner: backend (backend-builder).
- Flags: none.
- Lane: A.
- Touches: `backend/src/DisputeDesk.Worker/Sla/`; `backend/src/DisputeDesk.Application/Sla/`; backend/src/DisputeDesk.Infrastructure/Sla/ (claim query); docker-compose.yml (worker health check adds the sweep file); `backend/tests/DisputeDesk.Worker.IntegrationTests/Sla/`.

## References

- parent — docs/tickets/epic-5-operations/epic-5-operations.md

## Notes

- Risk high: double escalation; beyond the criteria, QA watches the seeded demo dispute get exactly one escalation entry.
