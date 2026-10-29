# Decisions: event sourcing and audit scope (brief-es)

Decided by the human on 2026-10-29, after the supervisor's evaluation and the decision-time challenge (`brief-es-challenge.md`, verdict RETHINK). These decisions drive the PRD, initiative, architecture, glossary and story changes listed under "Changes".

## Decisions

| # | Topic | Decision |
|---|---|---|
| 1 | Audit of successful dispute changes | The append-only dispute stream is the record of successful dispute changes; the duplicate `AuditLogEntries` row is removed. `DisputeEvents` gets the same insert-only guard as the audit log. The audit log keeps linking, refused or forbidden actions, operations reads, and sign-in and sign-out. |
| 2 | Event types | Domain events are stored (`DisputeRaised`, `ReviewStarted`, `DisputeEscalated`, `DisputeResolved`, `DisputeWithdrawn`, `SlaWarningRaised`); integration events stay `disputedesk.dispute.raised.v1` and `disputedesk.dispute.status-changed.v1`. One exhaustive mapping in Infrastructure decides what is published (domain versus integration events, Microsoft Learn: *.NET Microservices Architecture*, "Domain events versus integration events"). Stored event ID = outbox ID = CloudEvent `id`. |
| 3 | `GlobalPosition` and the `EventType` index | Not built. Order within a dispute comes from `Version` with unique `(DisputeId, Version)`; publish order from the relay's per-dispute order (AD-17). Cross-dispute options (Kafka offsets, a high-water mark with `pg_snapshot_xmin`, change data capture) go to the production path. |
| 4a | Internal event type | `SlaWarningRaised`, recorded once per dispute by the SLA sweep before the SLA is due. Hidden from customers by the allowlist, never published (no outbox row), visible to operations. The type and the deny-by-default test arrive with 3.8 and 3.3; the sweep produces it in 5.3. |
| 4b | Version clash | On a version clash a customer or operations handler reloads the stream and decides once more; only a second clash, or a status that forbids the action, returns 409. AD-15 amended. |
| 5 | Story 7.9 | Replaced by new story 2.8: sign-in and sign-out audited (`SignIn`, `SignOut`, one `Audit.Record` method), sign-out through the OIDC handler, a deny-by-default fallback policy, and the security and error-shape tests run again under the Production environment. 7.9's whole-call deadline item is dropped (2.1 added that deadline on purpose). |
| 6 | Release stories | 7.1, 7.2, 7.3, 7.6, 7.7 and 7.10 stay, before the AWS stories 7.4 and 7.5. |
| 7 | Story number | The event-sourcing story is 3.8, built before 3.3. |
| 8 | Production path | `docs/production-path.md`; the context is written as "a large retail bank with tens of millions of clients", with scale figures labelled as estimates. |
| 9 | Story 4.1 | Runs on the M1 after 2.8, in parallel with 3.8 and 3.3 on the M2, after a collision check. New criterion: the published CloudEvent `id` equals the stored event ID. |
| 10 | Story 1.6 | Dropped. The session-long stack-lock fix (routed to 1.6 by `cec72b6`) is recorded as tooling debt. Stories 7.8a and 7.8b are dropped as well. |
| 11 | Personal data in events | NFR-SEC-5 and R31 apply to published events. Stored events are private records (they hold `CustomerId`, `AccountRef`, `TransactionId` and the snapshot) with the same access as the `Disputes` table, never logged or traced, kept under NFR-DATA-7. Crypto-shredding goes to the production path. |
| 12 | Seeded disputes | The seeder calls the domain methods with back-dated instants, and seeded disputes go through the full commit path, including outbox rows, so they are published once per fresh database. FR-29, R24 and AD-30 are rewritten; relay tests must not depend on an empty outbox. |

## Story-level points from the challenge (no human gate)

- 3.8 backfills from both `Disputes` and `TimelineEntries`, and a test shows that replaying each stream reproduces the current row and timeline.
- 3.8 covers a failed save: `AppDbContext.SaveChangesAsync` takes pending changes before saving, so a failed save must not lose them silently.
- 3.3 proves deny-by-default with a test-only unknown event type as well as the every-type test.

## Changes

- **PRD** (`bmad-prd`): FR-10, FR-13, FR-17, FR-18, FR-24, FR-25, FR-26, FR-29, NFR-DATA-1, NFR-DATA-2, NFR-DATA-5, NFR-SEC-5, NFR-SEC-10, and the §6.2 deferrals.
- **Initiative** (Sofia): R10, R16, R18, R21, R22, R24, R28, R29, R31, plus a new requirement for sign-in and sign-out audit.
- **Architecture** (`bmad-architecture`): AD-11, AD-13, AD-14, AD-14a, AD-15, AD-17, AD-18, AD-19, AD-22, AD-30, the Deferred table; a new decision record for event sourcing.
- **Glossary** (`grill`): "Timeline" is built from the dispute's events; a new term for a stored event, distinct from "Dispute event" (published).
- **Stories** (Sofia): new 3.8 and 2.8; amend 3.3, 3.4, 4.1, 5.1, 5.2, 5.3, 5.4, 6.1, 7.1, 7.6; remove 7.9, 7.8a, 7.8b, 1.6; update the index and `tickets.toml`.
- **Supervisor**: the review-story, qa-story and security-story skills; `docs/conventions/backend.md`.
- **Lanes**: M1 runs 2.8 (the worker-tmux trial), then 4.1. M2 runs 3.8, 3.3, 3.4, 5.1, 5.2, 5.4, 6.1, 6.2, 6.3, 5.3, 2.6. Frontend: 3.6 now, 3.7 after 3.3, 6.4 after 6.2. Then 7.10, 7.1 to 7.3, 7.6, 7.7, and last 7.4 and 7.5.
