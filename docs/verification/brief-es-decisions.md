# Decisions: stored events and audit scope (brief-es)

Decided by the human on 2026-10-29, after the supervisor's evaluation and the decision-time challenge (`brief-es-challenge.md`). These decisions are the design of dispute records, publishing and the audit log; every document and story states them as the design.

## Decisions

| # | Topic | Decision |
|---|---|---|
| 1 | Record of dispute changes | Each successful dispute change is a stored event in the append-only `StoredEvents` stream, and that stream is the record of dispute changes. `StoredEvents` and `AuditLogEntries` share the same insert-only guard. The audit log holds linking, refused or forbidden actions, operations reads, and sign-in and sign-out. |
| 2 | Event types | Stored event types are `DisputeRaised`, `ReviewStarted`, `DisputeEscalated`, `DisputeResolved`, `DisputeWithdrawn`, `SlaWarningRaised`; integration events are `disputedesk.dispute.raised.v1` and `disputedesk.dispute.status-changed.v1`. One exhaustive mapping in Infrastructure decides what is published (domain versus integration events, Microsoft Learn: *.NET Microservices Architecture*, "Domain events versus integration events"). Stored event ID = outbox ID = CloudEvent `id`. |
| 3 | Ordering | Order within a dispute comes from `Version` with unique `(DisputeId, Version)`; publish order from the relay's per-dispute order (AD-17). There is no global position and no `EventType` index. Cross-dispute options (Kafka offsets, a high-water mark with `pg_snapshot_xmin`, change data capture) are in the production path. |
| 4a | Internal event type | `SlaWarningRaised` is recorded once per dispute by the SLA sweep before the SLA is due (the lead time is settled in story 5.3). Hidden from customers by the allowlist, never published (no outbox row), visible to operations. The type and the deny-by-default test arrive with 3.8 and 3.3; the sweep produces it in 5.3. |
| 4b | Version clash | On a version clash a customer or operations handler reloads the stream and decides once more; only a second clash, or a status that forbids the action, returns 409 (AD-15). |
| 5 | Story 2.8 | Sign-in and sign-out are audited (`SignIn`, `SignOut`, one `Audit.Record` method), sign-out goes through the OIDC handler, a deny-by-default fallback policy applies, and the security and error-shape tests also run under the Production environment. The whole-call deadline on core-banking calls is 2.1's `TotalRequestTimeout` (AD-21). |
| 6 | Release stories | 7.1, 7.2, 7.3, 7.6, 7.7 and 7.10 come before the AWS stories 7.4 and 7.5. |
| 7 | Story number | The stored-events story is 3.8, built before 3.3. |
| 8 | Production path | `docs/production-path.md`; the context is "a large retail bank with tens of millions of clients", with scale figures labelled as estimates. |
| 9 | Story 4.1 | Runs on the M1 after 2.8, in parallel with 3.8 and 3.3 on the M2, after a collision check. 3.8 proves stored event ID = outbox ID; 4.1 proves the published CloudEvent `id` = outbox ID. |
| 10 | Tooling and follow-ups | The EF-only idempotency claim (`docs/research/ef-core-upsert/ef-core-upsert.md`) is a production-path item. A session-long `stack` lock is tooling debt; until it exists, a brief runs a stack session as one locked script. |
| 11 | Personal data in events | NFR-SEC-5 and R31 apply to published events. Stored events are private records (they hold `CustomerId`, `AccountRef`, `TransactionId` and the snapshot) with the same access as the `Disputes` table, never logged or traced, kept under NFR-DATA-7. Crypto-shredding is in the production path. |
| 12 | Seeded disputes | The seeder calls the domain methods with back-dated instants, and seeded disputes go through the full commit path, including outbox rows, so they are published once per fresh database (FR-29, R24, AD-30). Relay tests do not depend on an empty outbox. |
| 13 | Loading a dispute | A handler loads a dispute by replaying its stored events and decides from that state. The `Disputes` row is a read model written in the same transaction as the stored event and the outbox row; a test proves replaying each stream reproduces its row and its timeline. |
| 14 | Data model and migrations | `StoredEvents` is part of the dispute data model from the migration that creates `Disputes`. The migration history shows only forward development of the data model: no migration creates a table a later one drops, and every document describes this design as the design from the start. Story 3.8 brings the existing migrations into that shape. |
| 15 | Concurrency | The unique `(DisputeId, Version)` index on `StoredEvents` is the one concurrency mechanism for dispute changes; `Disputes` maps no concurrency token (AD-15, EF Core *Handling concurrency conflicts*). |

## Naming (grill, 2026-10-29)

- The glossary term for a fact in a dispute's append-only history is **stored event**; "dispute event" means only the published fact.
- The table is `StoredEvents`, PascalCase like every other table (AD-22).
- The decision record is `docs/decisions/ad-009-stored-events.md` (ad-001 to ad-008 are planned in story 7.6).

## Story-level points

- 3.8 covers a failed save: `AppDbContext.SaveChangesAsync` takes pending stored events before saving, so a failed save must not lose them silently.
- 3.3 proves deny-by-default with a test-only unknown event type as well as the every-type test.

## Where each decision lands

- **PRD:** FR-10, FR-13, FR-17, FR-18, FR-24, FR-25, FR-26, FR-29, NFR-DATA-1, NFR-DATA-2, NFR-DATA-5, NFR-DATA-7, NFR-SEC-5, NFR-SEC-10, §6.2.
- **Initiative:** R10, R16, R18, R20, R21, R22, R24, R28, R29, R31, R41 (sign-in and sign-out audit).
- **Architecture:** AD-4, AD-7, AD-10, AD-11, AD-13, AD-14, AD-14a, AD-15, AD-17, AD-18, AD-19, AD-21, AD-22, AD-30, the Deferred table; decision record ad-009.
- **Glossary:** "Stored event"; "Timeline" is built from a dispute's stored events.
- **Stories:** 3.8 and 2.8; 3.1, 3.2, 3.3, 3.4, 4.1, 5.1, 5.2, 5.3, 5.4, 6.1, 7.1, 7.6; the index and `tickets.toml` list exactly the planned stories.
- **Skills and conventions:** the review-story, qa-story and security-story skills; `docs/conventions/backend.md`.
- **Lanes:** M1 runs 2.8 (the worker-tmux trial), then 4.1. M2 runs 3.8, 3.3, 3.4, 5.1, 5.2, 5.4, 6.1, 6.2, 6.3, 5.3, 2.6. Frontend: 3.6 now, 3.7 after 3.3, 6.4 after 6.2. Then 7.10, 7.1 to 7.3, 7.6, 7.7, and last 7.4 and 7.5.
