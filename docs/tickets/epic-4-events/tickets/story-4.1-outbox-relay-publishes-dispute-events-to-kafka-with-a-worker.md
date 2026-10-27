---
id: 1
type: story
title: "Outbox relay publishes dispute events to Kafka with a Worker heartbeat"
parent: epic-4-events
covers: [R23, R26, R27, R28, R36]
after: [3.1]
hitl: false
risk: high
---

# Outbox relay publishes dispute events to Kafka with a Worker heartbeat

## Description

Adds the single-leader relay (AD-17) with trace propagation (AD-20), the relay heartbeat health check and graceful shutdown on both hosts (AD-25).

## Requirements

Verify: Worker integration tests on Testcontainers Postgres and Redpanda pass for publish, order, outage, failed rows and lock handover.

1. **Publish byte for byte** (R23; FR-27, FR-28)
   **Given** a pending outbox row
   **When** the relay runs
   **Then** the record goes to `disputedesk.disputes` keyed by dispute ID, its value equal to `Payload`, with headers `traceparent`, `tracestate` and `content-type: application/cloudevents+json`, and the row is marked Sent

2. **Per-dispute order** (R23, R28; FR-28, NFR-RES-3)
   **Given** versions 1 to 3 of one dispute pending
   **When** the relay runs
   **Then** they publish in version order, each after the previous delivery report
   **And** a Failed or not-yet-due head row holds back later rows of that dispute only

3. **Kafka outage** (R28; NFR-RES-3)
   **Given** the broker stopped while disputes are raised
   **When** it returns
   **Then** every waiting row publishes in per-dispute order and no attempt was counted during the outage

4. **Failed after 5** (R28; NFR-RES-4)
   **Given** a record the broker rejects permanently
   **When** it fails 5 times
   **Then** the row is Failed with `LastError`, `disputedesk.outbox.failed` increments and the event ID is logged

5. **Single leader** (R26; NFR-ARC-4)
   **Given** two relays
   **When** both start and the leader's lock connection is terminated
   **Then** only one publishes at a time and the other takes over without duplicating order

6. **Trace continues** (R36; NFR-OBS-1)
   **Given** an outbox row with a stored `TraceParent`
   **When** it is produced
   **Then** the producer span's parent is the stored context, or a root span when none, and the headers are injected through the default propagator

7. **Heartbeat and shutdown** (R27; NFR-RES-1, NFR-RES-5)
   **Given** the Worker
   **When** the relay loops and then SIGTERM arrives
   **Then** `/tmp/heartbeat-relay` is touched every loop including non-leader lock attempts, and both hosts finish in-flight work within the 30-second `ShutdownTimeout`

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Re-run the telemetry smoke check: one trace from the browser raise to the Worker producer span in the Aspire Dashboard.
- `docker compose stop redpanda`, raise twice, start it: both events arrive in order.
- `docker compose ps` shows the worker healthy and starting after the api.

## Build facts

- Owner: backend (backend-builder).
- Flags: none.
- Lane: A.
- Touches: `backend/src/DisputeDesk.Worker/`; `backend/src/DisputeDesk.Infrastructure/Outbox/`; backend/src/DisputeDesk.Api/Program.cs (ShutdownTimeout); backend/Directory.Packages.props (Confluent.Kafka); backend/Dockerfile (worker-local target); docker-compose.yml (worker health check, depends_on api healthy, stop_grace_period); `backend/tests/DisputeDesk.Worker.IntegrationTests/`.

## References

- parent — docs/tickets/epic-4-events/epic-4-events.md

## Notes

- Risk high: lost or reordered events; beyond the criteria, QA reads the topic for one raise.
