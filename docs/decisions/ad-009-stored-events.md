# ad-009: Stored events are the dispute's record

**Status:** accepted

**Decision.** Every change to a dispute is appended as a stored event to the dispute's own stream in the `StoredEvents` table (PascalCase like every table, AD-22), in the same transaction as the change ([Event Sourcing pattern](https://learn.microsoft.com/azure/architecture/patterns/event-sourcing), Microsoft Learn). The stream is the record of successful dispute changes. A handler loads a dispute by replaying its stored events and decides from that state; the `Disputes` row is a read model written in the same transaction as the stored event and the outbox row, and a test replays each stream and checks it reproduces the row and the timeline (AD-13). Stored events are private domain events; what leaves the bank is decided by one exhaustive mapping in Infrastructure that turns some of them into the two published integration events ([Domain events versus integration events](https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation#domain-events-versus-integration-events), Microsoft Learn). `StoredEvents` is part of the dispute data model from the migration that creates `Disputes`, and the timeline is built from the stored events; there is no separate timeline table.

| Rule | Value |
| --- | --- |
| Stored event types | `DisputeRaised`, `ReviewStarted`, `DisputeEscalated`, `DisputeResolved`, `DisputeWithdrawn`, `SlaWarningRaised` |
| Loading | Replay the stream in `Version` order through `Dispute.Replay`, which uses the same `Apply` cases as the aggregate's methods (AD-13) |
| Order and concurrency | `Version` within one dispute, unique `(DisputeId, Version)`; no order across disputes. The unique index is the only concurrency check: `Disputes` has no concurrency token, because every write to it commits with a stored event (AD-15; [EF Core: Handling concurrency conflicts](https://learn.microsoft.com/ef/core/saving/concurrency)) |
| Immutability | Insert only: the commit refuses a Modified or Deleted stored event, as it does for the audit log |
| Identity | Stored event ID = outbox row ID = CloudEvent `id` ([CloudEvents 1.0 `id`](https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md#id)); 3.8 tests stored event ID = outbox ID, 4.1 tests published `id` = outbox ID |
| Published | `DisputeRaised` as `disputedesk.dispute.raised.v1`; the four status changes as `disputedesk.dispute.status-changed.v1`; `SlaWarningRaised` never (no outbox row) |
| Privacy | `DisputeRaised` holds `CustomerId`, `AccountRef`, `TransactionId` and the transaction snapshot: same access as `Disputes`, never logged or traced, kept under NFR-DATA-7 |
| Audit log | Keeps linking, refused or forbidden actions, operations reads (`DisputeRead`), sign-in and sign-out; no row for a successful dispute change |

**Options rejected.**

- *Full event store with a `GlobalPosition` and an `EventType` index:* a PostgreSQL sequence is not a commit-order checkpoint, because [sequence values are not rolled back](https://www.postgresql.org/docs/current/functions-sequence.html) and transactions commit out of allocation order; nothing reads across streams today. Cross-dispute options (Kafka offsets, a high-water mark with `pg_snapshot_xmin`, change data capture) are in `docs/production-path.md`.
- *Keeping an `AuditLogEntries` row for every successful change beside the stored event:* two records of the same fact that can disagree.
- *Publishing stored events directly:* private facts and personal data would leave the bank, and a new internal type would become a public contract by accident.
- *Deciding from the `Disputes` row:* the decision would rest on a copy of the record rather than the record.
- *An `xmin` concurrency token on `Disputes` beside the unique index:* a second check of the same conflict.

**Consequences.** A lost update is caught by the unique `(DisputeId, Version)`, and a handler that loses replays the stream again and decides once more (AD-15). Published `disputeVersion` values can skip the versions of unpublished stored events. The customer timeline reads stored events through a deny-by-default allowlist. Seeded disputes go through the same commit, so each seeded action has one stored event and one outbox row, a restart adds none, and they are published at least once like any other event (consumers deduplicate by CloudEvent `id`; wording settled 2026-10-30, story 5.4). Crypto-shredding for erasure is a production-path item.
