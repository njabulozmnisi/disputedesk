---
id: 3
type: epic
title: "Customers raise, follow and withdraw disputes"
parent: initiative-disputedesk
covers: [R10, R11, R12, R13, R14, R15, R16, R17, R18, R19, R23, R28, R29, R30, R31, R35, R36, R40, R41]
sources: []
companions: []
after: []
risk: high
---

# Customers raise, follow and withdraw disputes

## Description

A linked customer raises a dispute on a disputable transaction, sees it on the transaction row, in the dispute history and on its timeline, and withdraws it while it is open; every change is a stored event committed with the `Disputes` read model and, when published, its outbox row, and a refused raise or withdraw is audited.

## Outcome

Customers raise and follow disputes online; signal: SM-1 and SM-2 from the raise metrics and spans.

## Requirements

R10 (parent R10): Every link and every linking failure, every refused or forbidden dispute action, every operations read, and every sign-in and sign-out (R41) is written to an audit log the application only appends to; linking entries carry time, login, outcome and client IP, never the ID number. A successful dispute change (raise, withdraw, operations actions, SLA escalation) is recorded as a stored event in `StoredEvents`, which has the same insert-only guard as the audit log, and writes no audit entry. v1 has no unlink endpoint. This epic delivers: stored events for raise and withdraw with no audit entry (stories 1 and 8), and `Rejected` audit entries for raise and withdraw refusals (story 4).
R11 (parent R11): A linked customer lists their own transactions newest first with keyset pagination (`limit` an integer from 1 to 50, default 20; a bad `limit` or cursor gets 400), each row showing the fields in FR-11; when the core-banking source is still down after the resilience policies, the list returns 503 and the SPA offers a retry while dispute history stays available. This epic delivers: dispute history staying available while core banking is down.
R12 (parent R12): Each transaction row shows either that it is disputable or its dispute's customer-facing status; pending transactions, credits, transfers between own accounts, transactions dated more than 90 South African calendar days ago (boundary at the end of D + 90), and any transaction ever disputed are not disputable. This epic delivers: the "ever disputed" rule and the dispute status on each row.
R13 (parent R13): A linked customer raises a dispute on their own disputable transaction with a reason and an optional note of up to 1,000 characters; the server re-reads the transaction, decides disputability, stores the snapshot and ignores any amount, date or description sent; success is 201 with status Submitted and one timeline entry; an ever-disputed transaction gets 409 (enforced by a unique index under concurrency), any other non-disputable one 422, another customer's or a missing one 404, and a core-banking outage 503 with nothing written.
R14 (parent R14): For the reason wrong amount the customer must give an expected amount (rejected for every other reason) of at least R0.00, below the transaction amount, with at most 2 decimals; the dispute is for the difference, in the ZAR billed amount, with no floating-point arithmetic.
R15 (parent R15): Raising requires an `Idempotency-Key`; a replay of the same validated request returns the original response with no second dispute or event, a different request under the same key gets 422, a key still in progress gets 409, a 5xx releases the key, and keys are scoped to the customer for 24 hours.
R16 (parent R16): A linked customer lists their disputes newest first and opens one to see its details and timeline oldest first, built from the dispute's stored events, each entry showing the status as the customer sees it, the time in South African time, the actor label ("You" or "The bank") and any note meant for the customer; the history works while the core-banking source is down, and a Rejected outcome shows how to complain to the bank and then the National Financial Ombud Scheme. This epic delivers: the detail and timeline (story 3), the history list (story 9), the screens (story 7).
R17 (parent R17): Disputes are scoped to the customer resolved from the link table, so another customer's dispute and an unknown ID return 404 bodies identical apart from `traceId`; integration tests prove this on every customer endpoint, and only the operations endpoints and the Worker bypass the scope, each in one named place. This epic delivers: the ownership filter and bypass rule (story 1) and the identical-404 tests on every customer endpoint (stories 3 and 4), and list ownership across pages and cursors (story 9).
R18 (parent R18): No customer response or screen contains an actor's identity, an SLA date, a due date or a promised resolution time, and Escalated always shows as "Under review (prioritised)"; a customer timeline shows only stored event types on an allowlist, so `SlaWarningRaised` and any type not on the allowlist stay hidden. This epic delivers: the allowlist (story 8), the hidden internals on the detail (story 3) and the list (story 9).
R19 (parent R19): The owning customer withdraws a Submitted, Under review or Escalated dispute with an optional note of up to 500 characters, through a customer endpoint only (a bearer token cannot); Withdrawn is final and its transaction is never disputable again; withdrawing a Resolved or Withdrawn dispute returns 409 and the SPA does not offer it.
R23 (parent R23): Raising publishes `disputedesk.dispute.raised.v1` and every successful action, including withdrawal and SLA escalation, publishes `disputedesk.dispute.status-changed.v1` with the fields listed in FR-28, as CloudEvents 1.0 structured JSON on one topic keyed by dispute ID with `traceparent` in the headers; events carry no personal data, are delivered at least once with a stable event ID in per-dispute commit order, and each type has a committed JSON Schema checked by a test. This epic delivers: the `raised.v1` and `status-changed.v1` CloudEvents written to the outbox with their committed schemas (publishing is epic-4-events).
R28 (parent R28): A dispute change commits with its stored event and, when the change is published, its outbox row, or none of them commit; the stored event ID, the outbox ID and the CloudEvent `id` are the same value; raising and actions keep working while Kafka is down and events publish in per-dispute order when it returns; a row the broker rejects permanently is marked failed after 5 attempts, counted and logged, and later events for that dispute wait for an operator per the README runbook. This epic delivers: the atomic commit and stored event ID = outbox ID (NFR-DATA-2; story 8).
R29 (parent R29): PostgreSQL holds every record NFR-DATA-1 lists, the stored events among them, and transactions are not copied apart from the snapshot; a handler loads a dispute by replaying its stored events and decides from that state, the `Disputes` row is a read model written in the same transaction as the stored event and the outbox row, and a test proves replaying each stream reproduces its row and its timeline; the allowed actions, disputability and amount rules live in the domain model; money is `numeric(19,2)` with ISO code ZAR and every timestamp is stored in UTC; a dispute's stored events are ordered by `Version`, unique on (`DisputeId`, `Version`), and the other uniqueness rules are enforced by the indexes in NFR-DATA-5; on a version clash a customer or operations handler reloads the stream and decides once more, and only a second clash, or a status that forbids the action, returns 409; schema changes are EF Core migrations applied on start-up, `StoredEvents` is part of the dispute data model from the migration that creates `Disputes`, and no migration creates a table a later one drops; disputes, their stored events and their audit entries are never deleted in v1. This epic delivers: the dispute tables with `StoredEvents` from the migration that creates `Disputes`, loading by replay and the replay test (story 8), money, UTC, the version and unique indexes, and the reload on a version clash (story 4).
R30 (parent R30): Every route is under `/api/v1`; the built-in OpenAPI document is committed as `openapi.json` and checked for drift in CI; every error is RFC 9457 ProblemDetails with a stable `type` and the `traceId`; lists use opaque keyset cursors; money travels as decimal strings and enum values as camelCase (C# members PascalCase, stored in Postgres as the member names). This epic delivers: one test of the wire timestamps (`+00:00`, System.Text.Json's default) and enum reading (the framework's `JsonStringEnumConverter`, case-insensitive) on every customer route (story 9; decided 2026-10-29).
R31 (parent R31): ID numbers are never stored or logged, card numbers are kept and shown only as first 6 and last 4 digits, account numbers only as last 4, both masks checked where data enters from core banking; logs are structured, carry the trace ID, and logs, traces, metrics and published events carry no personal data; stored events hold `CustomerId`, `AccountRef`, `TransactionId` and the snapshot as private records with the same access as the `Disputes` table, are never logged or traced, and are kept under NFR-DATA-7. This epic delivers: stored events never logged or traced (story 8), and no private field in the detail and list responses or their logs (stories 3 and 9).
R35 (parent R35): Every FR consequence not tagged `[manual check]` maps to an automated test listed in its story's report: unit tests for every FR-24 cell, the D + 90 boundary, amounts and SLA timing; integration tests on real PostgreSQL in Testcontainers with no in-memory fakes; contract tests for `openapi.json` and each event schema; Playwright E2E for UJ-1, UJ-2, UJ-5a, UJ-5b and UJ-5d on the real stack. This epic delivers: domain unit, integration and event contract tests.
R36 (parent R36): The SPA, Api host and Worker emit OpenTelemetry traces, metrics and logs, and one trace runs browser to API to outbox to Kafka to Worker with trace context injected by hand; the business metrics in NFR-OBS-3 are emitted; the Aspire Dashboard (locally) and CloudWatch (AWS) show them with no manual setup. This epic delivers: the raise metrics and the SPA raise spans.
R40 (parent R40): The customer portal meets WCAG 2.2 AA (contrast, keyboard access, visible focus, labelled errors, reflow at 320px), checked by axe in the E2E tests, plus a recorded manual keyboard pass. This epic delivers: the raise, history, detail and withdraw screens.
R41 (parent R41): Every sign-in and every sign-out writes an audit entry, with the actions `SignIn` and `SignOut`, and every audit entry is written through one `AuditLog.Record` method. This epic delivers: raise and withdraw refusals through `AuditLog.Record` and the source-rules test that only `AuditLog.Record` writes audit entries (story 4).

## Done when

- Customer A raises, follows and withdraws a dispute in the SPA on the running stack.
- Each change leaves one stored event and, when its type is published, one outbox row, or none, and no audit row; replaying each stream reproduces its `Disputes` row and its timeline.
- Another customer's dispute and an unknown ID give identical 404 bodies on every customer endpoint.
- Event schemas are committed and checked by a contract test; integrated verification passed per story.

## Boundaries

- Operations actions and the SLA sweep belong to epic-5-operations; publishing to Kafka belongs to epic-4-events.
- Must not change: the transactions list contract beyond adding `disputeId` and `customerStatus` on disputed rows.

## References

- parent — docs/initiative-disputedesk.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-4, AD-11, AD-13, AD-14, AD-14a, AD-15, AD-16, AD-19, AD-19a, AD-20, AD-22, AD-30, AD-31
- experience — docs/ux/EXPERIENCE.md
- design — docs/ux/DESIGN.md
- prd — docs/prd/prd.md
- verification — docs/verification/

## Notes

- Decision: 2026-10-26 — epic Requirements are the parent's lines only, ids kept (`R<n> (parent R<n>)`); each line names the part this epic delivers where the parent line is shared.
- Decision: 2026-10-26 — tracer bullet: story 1, Linked customer raises a dispute that commits with its stored event and outbox row; it is the thinnest path through every layer this epic touches.
- Decision: 2026-10-29 — story 8 (stored events and loading by replay) is built after story 2 and before story 3, on M2 in parallel with 2.8 (ad-009); stories 3 and 4 build on it, and story 4 also waits on 2.8 for `AuditLog.Record`.
- Decision: 2026-10-29 (Sofia, 3.3 pre-build review) — story 3 is split to stay within about 8 criteria (AGENTS.md, Definition of ready): story 3 is the dispute detail and timeline, story 9 the history list, wire timestamps and enums, and the alias rule; built 3 then 9 then 4 on M2 (3.4 and 3.9 edit the same endpoint, contract and route-list files), and story 7 also waits on 9.
- Waits on 2.3 because: 1 needs what those stories build (see its References and Description).
- Waits on 2.4, 2.5 because: 6 needs what those stories build (see its References and Description).
- Waits on: epic 2 (CurrentCustomer, the Linked policy, the transactions handler and the SPA shell).
- Decision: 2026-10-26 — Gate 2: AD-13 governs seeding, so the seeded-disputes story (was 3.5) moved to epic-5-operations as story 5.4, after the operations actions (5.2); R24 moved with it.
- Decision: 2026-10-26 — Gate 2: code in every builder-owned story of this epic is stupid simple and logical, per AGENTS.md (smallest change that meets the criteria; every abstraction earns its place).
- Decision: 2026-10-30 (Sofia, 3.6 pre-build review) — story 6 is split to stay within about 8 criteria (AGENTS.md, Definition of ready): story 6 is the raise and review screens with the idempotent submit (after 3.3, for the regenerated client), story 10 the dispute badges on rows (a plain "Resolved", human D1), story 11 the browser spans for SM-1 and SM-2 (R36); 10 and 11 wait only on 6 and share no file, and story 7 also waits on 11 because both edit `router.tsx`.
