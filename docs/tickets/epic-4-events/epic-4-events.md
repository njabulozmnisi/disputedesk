---
id: 4
type: epic
title: "Dispute events reach Kafka in per-dispute order"
parent: initiative-disputedesk
covers: [R23, R26, R27, R28, R31, R36]
sources: []
companions: []
after: []
risk: high
---

# Dispute events reach Kafka in per-dispute order

## Description

The Worker's outbox relay publishes every outbox row to `disputedesk.disputes` as a CloudEvent, keyed by dispute ID with trace headers, in per-dispute order, through Kafka outages, with failed rows parked after 5 attempts and a heartbeat health check.

## Outcome

The rest of the bank learns of every dispute change; signal: events on the topic in order through an outage.

## Requirements

R23 (parent R23): Raising publishes `disputedesk.dispute.raised.v1` and every successful action, including withdrawal and SLA escalation, publishes `disputedesk.dispute.status-changed.v1` with the fields listed in FR-28, as CloudEvents 1.0 structured JSON on one topic keyed by dispute ID with `traceparent` in the headers; events carry no personal data, are delivered at least once with a stable event ID in per-dispute commit order, and each type has a committed JSON Schema checked by a test. This epic delivers: publishing (at least once, stable ID, per-dispute order, headers); across a leader handover an earlier event can arrive late or twice, and consumers apply the `disputeVersion` check and duplicate-`id` rule of AD-17 (decided 2026-10-29). Story 1 publishes in order; story 2 proves the handover.
R26 (parent R26): The runtime units are exactly those in NFR-ARC-2, with one Api and one Worker; sessions, idempotency keys and the audit log live in PostgreSQL so the Api could scale out without session loss (rate-limit state is the stated exception); a second Worker would be harmless. This epic delivers: a second relay is harmless (advisory lock; story 1), including across a handover (story 2).
R27 (parent R27): The Api host exposes `/health/live` and `/health/ready` and the Worker's relay and sweep touch heartbeat files, which Compose health checks use; both shut down on SIGTERM within 30 seconds after finishing in-flight work. This epic delivers: the Worker relay heartbeat (story 1) and the Worker's graceful shutdown with the record in flight (story 2). The Api already keeps the default 30-second `HostOptions.ShutdownTimeout`; its Compose `stop_grace_period: 35s` goes with the next story that edits the api service after 6.1 (decided 2026-10-30, Sofia, 4.2 refine, to keep 4.2's Compose edit off 6.1's api block).
R28 (parent R28): A dispute change commits with its stored event and, when the change is published, its outbox row, or none of them commit; the stored event ID, the outbox ID and the CloudEvent `id` are the same value; raising and actions keep working while Kafka is down and events publish in per-dispute order when it returns; a row the broker rejects permanently is marked failed after 5 attempts, counted and logged, and later events for that dispute wait for an operator per the README runbook. This epic delivers: stable ID and per-dispute order (story 1); Kafka outage tolerance and failed rows (story 2).
R31 (parent R31): ID numbers are never stored or logged, card numbers are kept and shown only as first 6 and last 4 digits, account numbers only as last 4, both masks checked where data enters from core banking; logs are structured, carry the trace ID, and logs, traces, metrics and published events carry no personal data; stored events hold `CustomerId`, `AccountRef`, `TransactionId` and the snapshot as private records with the same access as the `Disputes` table, are never logged or traced, and are kept under NFR-DATA-7. This epic delivers: no personal data in the relay's Kafka headers, producer spans, logs, metric tags or `LastError` (stories 1 and 2).
R36 (parent R36): The SPA, Api host and Worker emit OpenTelemetry traces, metrics and logs, and one trace runs browser to API to outbox to Kafka to Worker with trace context injected by hand; the business metrics in NFR-OBS-3 are emitted; the Aspire Dashboard (locally) and CloudWatch (AWS) show them with no manual setup. This epic delivers: the producer span continuing the browser trace.

## Done when

- A raised dispute appears on `disputedesk.disputes` with `traceparent`, and one trace runs browser to Worker in the Aspire Dashboard.
- Events raised while Redpanda is stopped publish in order when it returns.
- The worker container reports healthy from its heartbeat; integrated verification passed.

## Boundaries

- No consumer and no dead-letter topic (prd §6.2).
- The SLA sweep's heartbeat belongs to epic-5-operations.

## References

- parent — docs/initiative-disputedesk.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-17, AD-19, AD-20, AD-25
- experience — docs/ux/EXPERIENCE.md
- design — docs/ux/DESIGN.md
- prd — docs/prd/prd.md
- verification — docs/verification/

## Notes

- Decision: 2026-10-26 — epic Requirements are the parent's lines only, ids kept (`R<n> (parent R<n>)`); each line names the part this epic delivers where the parent line is shared.
- Decision: 2026-10-26 — tracer bullet: story 1, Outbox relay publishes dispute events to Kafka with a Worker heartbeat; it is the thinnest path through every layer this epic touches.
- Decision: 2026-10-29 (Sofia, 4.1 pre-build review) — story 1 is split to stay within about 8 criteria (AGENTS.md, Definition of ready): story 1 publishes in per-dispute order under one leader with trace context, the Kafka options, the heartbeat and the privacy test; story 2 adds broker outages, per-record errors and Failed rows, the fatal-error recreate, leader handover during an in-flight send and graceful shutdown. Story 1 stays the tracer bullet.
- Decision: 2026-10-29 (human, 4.1 pre-build security S4) — plaintext Kafka on the private Compose network is accepted for v1 as a `// shortcut:`; broker SASL/SCRAM or mTLS goes in `docs/production-path.md` through story 7.6.
- Waits on 3.8 because: 1 needs what those stories build (outbox ID = stored event ID and the publish mapping, ad-009) (see its References and Description).
- Waits on: epic 3 (outbox rows written by the one commit path (AD-14), with outbox ID = stored event ID).
- Decision: 2026-10-26 — Gate 2: code in every builder-owned story of this epic is stupid simple and logical, per AGENTS.md (smallest change that meets the criteria; every abstraction earns its place).
