---
id: 5
type: epic
title: "Operations tools and the SLA sweep act on disputes"
parent: initiative-disputedesk
covers: [R10, R18, R20, R21, R22, R23, R24, R26, R27, R36, R39]
sources: []
companions: []
after: []
risk: high
---

# Operations tools and the SLA sweep act on disputes

## Description

An operations tool holding a client-credentials token with the right role reads a dispute and starts review, escalates or resolves it under the allowed-actions table, and the Worker escalates disputes at submission plus 240 hours through the same domain operation, and seeded disputes in every status reach their statuses through the same aggregate methods.

## Outcome

Disputes move to an outcome under one set of rules; signal: SM-4 from the resolution and escalation metrics.

## Requirements

R10 (parent R10): Every link and every linking failure, and every dispute change (raise, withdraw, operations actions, SLA escalation), is written to an audit log the application only appends to; linking entries carry time, login, outcome and client IP, never the ID number. v1 has no unlink endpoint. This epic delivers: audit entries for operations actions and SLA escalation.
R18 (parent R18): No customer response or screen contains an actor's identity, an SLA date, a due date or a promised resolution time, and Escalated always shows as "Under review (prioritised)". This epic delivers: the sweep's escalation shown as "Under review (prioritised)".
R20 (parent R20): An actor holding the action's role performs start review, escalate or resolve, each on its own endpoint, only as the allowed-actions table in FR-24 permits, and anything else returns 409 with the current status; resolve requires an outcome and a customer-visible note of 1 to 500 characters; of two racing actions exactly one succeeds; checks run 401, 403, 404, 400, 409.
R21 (parent R21): Operations endpoints accept only client-credentials bearer JWTs validated for issuer, audience, lifetime and signature, and customer and linking endpoints accept only the cookie, so the wrong credential gets 401; a token without the role, or a credential not mapping to exactly one actor type, gets 403; `dispute.read` returns one dispute with no personal details beyond the snapshot; each action's audit entry carries the fields listed in FR-25.
R22 (parent R22): The Worker escalates every dispute still Submitted or Under review at or after submission plus 240 hours, through the same domain operation as escalate (actor type `automation`), at least once a minute, in bounded batches that two Workers cannot double-claim, catching up after downtime and never writing a duplicate timeline entry; SLA time comes from `TimeProvider`.
R23 (parent R23): Raising publishes `disputedesk.dispute.raised.v1` and every successful action, including withdrawal and SLA escalation, publishes `disputedesk.dispute.status-changed.v1` with the fields listed in FR-28, as CloudEvents 1.0 structured JSON on one topic keyed by dispute ID with `traceparent` in the headers; events carry no personal data, are delivered at least once with a stable event ID in per-dispute commit order, and each type has a committed JSON Schema checked by a test. This epic delivers: status-changed events from operations actions and the sweep.
R24 (parent R24): A fresh `docker compose up` seeds customers A to D with about 90 days of start-up-relative transactions as FR-29 describes: A linked with a dispute in every status and outcome (one passing the SLA minutes after start-up), B linked and owning the dispute A gets 404 for, C unlinked and failing the SIM-swap check, D unlinked and able to complete linking; seeded disputes publish no events. This epic delivers: the seeded disputes for A and B, written through the `Dispute` aggregate methods.
R26 (parent R26): The runtime units are exactly those in NFR-ARC-2, with one Api and one Worker; sessions, idempotency keys and the audit log live in PostgreSQL so the Api could scale out without session loss (rate-limit state is the stated exception); a second Worker would be harmless. This epic delivers: a second sweep is harmless (SKIP LOCKED).
R27 (parent R27): The Api host exposes `/health/live` and `/health/ready` and the Worker's relay and sweep touch heartbeat files, which Compose health checks use; both shut down on SIGTERM within 30 seconds after finishing in-flight work. This epic delivers: the sweep heartbeat.
R36 (parent R36): The SPA, Api host and Worker emit OpenTelemetry traces, metrics and logs, and one trace runs browser to API to outbox to Kafka to Worker with trace context injected by hand; the business metrics in NFR-OBS-3 are emitted; the Aspire Dashboard (locally) and CloudWatch (AWS) show them with no manual setup. This epic delivers: action, resolution and escalation metrics.
R39 (parent R39): The README gives build, run and test steps, the seed identities, how to drive the operations endpoints from Scalar (the dev API viewer and stand-in operations tool), the research simplifications, and ends with the ten-area map; `docs/production-path.md` records every §6.2 deferral with a reason; `docs/decisions/` holds the decision records NFR-DOC-3 names. This epic delivers: Scalar behind `Scalar:Enabled` as the stand-in operations tool (NFR-DOC-4).

## Done when

- From Scalar, a token with the roles reads, starts review and resolves a seeded dispute; the customer timeline shows the bank as actor.
- The seeded SLA demo dispute shows "Under review (prioritised)" minutes after start-up.
- Every FR-24 cell has a unit test; integrated verification passed.

## Boundaries

- No operations list or search (prd §6.2).
- Actor type and identity are recorded, never used to decide (prd §4.7).

## References

- parent — docs/initiative-disputedesk.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-8, AD-9, AD-13, AD-14a, AD-15, AD-18, AD-25, AD-26, AD-30
- experience — docs/ux/EXPERIENCE.md
- design — docs/ux/DESIGN.md
- prd — docs/prd/prd.md
- verification — docs/verification/

## Notes

- Decision: 2026-10-26 — epic Requirements are the parent's lines only, ids kept (`R<n> (parent R<n>)`); each line names the part this epic delivers where the parent line is shared.
- Decision: 2026-10-26 — tracer bullet: story 1, Operations tools authenticate with bearer tokens and read a dispute; it is the thinnest path through every layer this epic touches.
- Waits on 3.1, 2.5, 2.6 because: 1 needs what those stories build (see its References and Description).
- Waits on 3.4 because: 2 needs what those stories build (see its References and Description).
- Waits on 4.1 because: 3 needs what those stories build (see its References and Description).
- Decision: 2026-10-26 — Gate 2: AD-13 governs seeding; the seeded-disputes story moved here from epic-disputes (was 3.5, now 5.4), after story 2 and before the SLA sweep, whose supervisor check uses the seeded SLA demo dispute.
- Waits on 2 because: 4 changes seeded statuses only through `StartReview`, `Escalate` and `Resolve` (AD-13).
- Waits on: epic 2 (the gateway templates and provisioning from stories 2.5 and 2.6), epic 3 (the Dispute aggregate and the one commit path), epic 4 (the Worker heartbeat health check).
- Decision: 2026-10-26 — Gate 2: code in every builder-owned story of this epic is stupid simple and logical, per AGENTS.md (smallest change that meets the criteria; every abstraction earns its place).
