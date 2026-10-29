---
id: 3
type: story
title: "SLA sweep escalates overdue disputes"
parent: epic-5-operations
covers: [R22, R26, R27, R18, R23, R29, R36]
after: [2, 4.1]
hitl: false
risk: high
---

# SLA sweep escalates overdue disputes

## Description

Adds the Worker's SLA sweep (AD-18): it loads each claimed dispute by replay and escalates it through `Dispute.Escalate` as `automation`, and before the SLA is due records `SlaWarningRaised` once per dispute through `Dispute.RecordSlaWarning`, with its heartbeat in the Worker health check.

## Requirements

Verify: Worker integration tests with a fake `TimeProvider` cover the 240-hour boundary, the SLA warning, batching, two sweeps and catch-up.

1. **240-hour boundary** (R22; FR-26, NFR-TEST-2)
   **Given** Submitted and Under review disputes
   **When** the sweep runs at submission plus 239:59:59 and at plus 240:00:00 (fake `TimeProvider`)
   **Then** only the second run escalates them, through `Dispute.Escalate` on the dispute loaded by replay, with actor `automation` and ID `worker:sla-sweep`

2. **Bounded claims, two sweeps** (R22, R26; FR-26, NFR-ARC-4)
   **Given** 120 overdue disputes and two sweeps
   **When** both run
   **Then** claims are batches of at most 50 with `FOR UPDATE SKIP LOCKED`, every dispute is escalated once, and no dispute has a duplicate stored event

3. **Catch-up** (R22; FR-26)
   **Given** the Worker was down past several due times
   **When** it starts
   **Then** it repeats batches until one comes back short and every overdue dispute is escalated

4. **Escalation as the customer sees it** (R18, R23; FR-17, FR-28)
   **Given** an SLA escalation
   **When** the customer detail and the outbox are read
   **Then** the customer sees `underReviewPrioritised` with actor `theBank`, and one `status-changed.v1` with `actorType` `automation` is written

5. **SLA warning once per dispute** (R22, R18; FR-26, NFR-API-5)
   **Given** Submitted and Under review disputes whose `SlaDueAtUtc` is within the lead time, after it, and beyond it
   **When** the sweep runs twice
   **Then** each dispute within the lead time gets exactly one `SlaWarningRaised` stored event (the aggregate refuses a second, and the claim skips disputes whose read model `SlaWarningRaisedAtUtc` is set, through its partial index), an overdue dispute is escalated without a warning, and no warning writes an outbox row (AD-14, AD-18)
   **And** customers never see the warning (story 3.8's allowlist) and the operations read shows it (story 5.1)
   **And** `RecordSlaWarning` and the `SlaWarningRaised` `Apply` case are added here, and replaying a stream that holds the warning reproduces its `Disputes` row

6. **Sweep heartbeat and metric** (R27, R36; NFR-RES-1, NFR-OBS-3)
   **Given** the sweep loop
   **When** it runs every 30 seconds on a `PeriodicTimer` built with the injected `TimeProvider`
   **Then** `/tmp/heartbeat-sweep` is touched each loop, the health check fails when it is older than 90 seconds (three missed 30-second loops; AD-25), and `disputedesk.sla.escalations` counts

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- About 5 minutes after a fresh `docker compose up`, the seeded demo dispute shows "Under review (prioritised)" for customer A (UJ-4).

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes, migration (the `SlaWarningRaisedAtUtc` read model column and its partial index, AD-18).
- Lane: M2.
- Touches: `backend/src/DisputeDesk.Domain/Disputes/Dispute.cs` (`RecordSlaWarning`, its `Apply` case); `backend/src/DisputeDesk.Worker/Sla/`; `backend/src/DisputeDesk.Application/Sla/`; backend/src/DisputeDesk.Infrastructure/Sla/ (claim queries); backend/src/DisputeDesk.Infrastructure/Persistence/ (`SlaWarningRaisedAtUtc`, Migrations/); docker-compose.yml (worker health check adds the sweep file); `backend/tests/DisputeDesk.Worker.IntegrationTests/Sla/`.

## References

- parent — docs/tickets/epic-5-operations/epic-5-operations.md

## Notes

- Decision: 2026-10-27 — the sweep heartbeat threshold is 90 seconds, three missed 30-second loops, so one slow batch does not mark the Worker unhealthy; the relay threshold stays 30 seconds (AD-25).
- Risk high: double escalation; beyond the criteria, QA watches the seeded demo dispute get exactly one escalation entry.
- Decision: 2026-10-27 — scope: if time runs short, this story goes to `docs/production-path.md` (initiative scope decision).
- Unknown: the SLA warning lead time (how long before `SlaDueAtUtc` the warning is recorded) is an open decision for the human, settled when this story is readied (AD-18, FR-26); the story is not ready to build until a dated `Decision:` line here gives it.
- Decision: 2026-10-29 — `SlaWarningRaised` is recorded once per dispute before the SLA is due, hidden from customers, never published, visible to operations (ad-009, AD-18).
