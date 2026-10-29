---
id: 2
type: story
title: "Relay rides out broker outages, failed records, leader handover and shutdown"
parent: epic-4-events
covers: [R23, R26, R27, R28, R31]
after: [1]
hitl: false
risk: high
---

# Relay rides out broker outages, failed records, leader handover and shutdown

## Description

Adds the failure paths to story 4.1's relay: a broker outage pauses the loop without counting an attempt, a per-record error counts toward Failed after 5 (AD-17, Failures), a fatal producer error recreates the producer, a leader handover during an in-flight send loses nothing (AD-17, Delivery guarantee), and SIGTERM finishes the in-flight send (AD-25).

## Requirements

Verify: Worker integration tests on Testcontainers Postgres and Redpanda pass for the broker outage, the error classification, Failed rows, leader handover during an in-flight send, graceful shutdown and the privacy of the failure surfaces.

1. **Kafka outage does not count as an attempt** (R28; NFR-RES-3)
   **Given** the Redpanda container paused while disputes are raised, so the broker keeps its mapped port
   **When** the relay tries to send and the broker is later unpaused
   **Then** a send that ends in a broker-unavailable error (`Error.Code` `Local_AllBrokersDown`, `Local_Transport`, `Local_MsgTimedOut` or `Local_TimedOut`) leaves the row Pending with `Attempts` unchanged and pauses the loop with backoff capped at 10 seconds, while the heartbeat stays under the 30-second threshold (AD-17, Failures; AD-25)
   **And** after the broker returns every waiting row publishes in per-dispute order, `Attempts` is 0 on each, and a row whose timed-out send had in fact been delivered is sent again with the same CloudEvent `id` (at-least-once, criterion 4)

2. **Each delivery error is classified once** (R28; NFR-RES-3, NFR-RES-4)
   **Given** the relay's error classification, called directly by the test
   **When** it is given each code of criterion 1, `MsgSizeTooLarge`, another broker error code, and an `Error` whose `IsFatal` is true
   **Then** the four codes of criterion 1 are broker-unavailable; a fatal error disposes and recreates the producer and counts no attempt (librdkafka, *Fatal errors* for the idempotent producer); every other error is a per-record error
   **And** a fatal error leaves its row Pending with `Attempts` unchanged

3. **Failed after 5 per-record errors** (R28, R31; NFR-RES-4)
   **Given** a record the broker rejects permanently, induced in the test with its own Redpanda container whose `disputedesk.disputes` is created with a topic `max.message.bytes` smaller than the row's `Payload`, so the broker answers `MsgSizeTooLarge`, and a fake `TimeProvider` advanced past each `NextAttemptAtUtc`
   **When** it fails 5 times
   **Then** each failure increments `Attempts`, sets `NextAttemptAtUtc` to the batch instant plus 1 second doubled per earlier attempt (1, 2, 4 and 8 seconds) with ±20% jitter (exponential backoff with jitter; AD-17, Failures), and sets `LastError` to `Error.Code` and `Error.Reason` only, cut to at most 500 characters
   **And** the 5th sets the row Failed, increments `disputedesk.outbox.failed` by 1 with no tags (a counter created through `IMeterFactory` on the `DisputeDesk` Meter; Microsoft Learn, *Creating metrics*; AD-20), and writes one `[LoggerMessage]` error with the event ID and dispute ID only, never the `DeliveryResult`, the `Message` or its value
   **And** the failed send's producer span sets only `error.type`, to the Kafka error code, beside 4.1's attribute set

4. **No event lost across a leader handover** (R26, R23; NFR-ARC-4, FR-28)
   **Given** two relays and a dispute with versions 1 to 3 pending, with the broker paused so the leader's send is in flight
   **When** the test terminates the leader's lock connection (`pg_terminate_backend` on the advisory-lock holder found in `pg_locks`) and unpauses the broker
   **Then** the other relay takes the lock over, no event is lost (every row ends Sent and every version is on the topic), every record that appears more than once carries the same CloudEvent `id` as its first copy, and the old leader marks no further row Sent, because its updates go only through the terminated connection (PostgreSQL, *Advisory Locks*: a session lock lasts as long as its connection)
   **And** the guarantee is the one AD-17 (Delivery guarantee) states: per-dispute order holds except across a leader handover, where an earlier event can arrive late or twice; consumers apply an event only if its `disputeVersion` is newer than the last one they applied for that dispute and treat a repeated CloudEvent `id` as a duplicate (Enterprise Integration Patterns, *Idempotent Receiver*). The test asserts no loss and same-`id` duplicates, not strict order across the handover

5. **Graceful shutdown** (R27; NFR-RES-5)
   **Given** a relay with a send in flight (the broker paused)
   **When** SIGTERM arrives (the test stops the host)
   **Then** the relay produces no new row, awaits the in-flight delivery report, and stops within the default 30-second `HostOptions.ShutdownTimeout` (Microsoft Learn, *.NET Generic Host*, host shutdown; AD-25); the in-flight row ends either Sent with its record on the topic, or Pending with `Attempts` unchanged, to be sent again by the next leader; it is never Failed and never Sent without its record
   **And** both hosts keep the default `HostOptions.ShutdownTimeout`; Compose sets `stop_grace_period: 35s` on the api and the worker

6. **No personal data on the failure surfaces** (R31; NFR-SEC-5)
   **Given** 4.1's marker dispute (markers in every private field of `DisputeRaised` and in the actor ID)
   **When** its send fails during a broker pause, and, in the `MsgSizeTooLarge` container, per record until Failed
   **Then** no captured log line, span attribute, metric tag, Kafka header or `LastError` holds any marker (OWASP Logging Cheat Sheet, data to exclude; AD-20)

## Boundaries

- 4.1's publish, order, leader, trace, options, heartbeat and privacy criteria stay green; their tests are extended, not loosened.
- No dead-letter topic; later events of a Failed row's dispute wait for an operator (AD-17).
- The Api host, `backend/openapi.json` and the outbox schema do not change. No migration.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- `docker compose stop redpanda`, raise twice, start it: both events arrive in order.
- In a throwaway copy, write the Sent update on the scoped `AppDbContext` instead of the lock connection: criterion 4's test fails.
- In a throwaway copy, count a broker-unavailable error as an attempt: criterion 1's test fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes (events leaving the system; R31).
- Lane: M1, after 4.1.
- Test seams: the Redpanda container is paused and unpaused so its mapped port stays the same. The builder verifies the pause API in the installed Testcontainers version; if it has none, the Redpanda container gets a fixed host port binding and is stopped and started instead. The criterion-3 container is its own, so the small `max.message.bytes` never reaches the shared topic. 4.1's test setup applies (own rows, one collection with parallelisation off).
- Touches:
  - `backend/src/DisputeDesk.Infrastructure/Outbox/OutboxRelay.cs` (the classification, `Attempts`, backoff, `LastError`, Failed, the metric, the log line, the producer recreate, shutdown)
  - `docker-compose.yml` (`stop_grace_period: 35s` on the api and worker services only)
  - `backend/tests/DisputeDesk.Worker.IntegrationTests/` (new failure, handover and shutdown test files; 4.1's privacy test extended)
- Not touched: `Infrastructure/Persistence/`, anything under `DisputeDesk.Api`, `backend/openapi.json`. No migration.

## References

- parent — docs/tickets/epic-4-events/epic-4-events.md
- architecture — docs/architecture/architecture.md, AD-17 (Delivery guarantee, Failures), AD-20, AD-25
- challenge — docs/verification/4.1-challenge.md (this story's criteria come from 4.1's challenged set)
- review — docs/verification/4.1-prebuild-review.md, docs/verification/4.1-prebuild-security.md

## Notes

- Risk high: lost events or rows wrongly Failed; beyond the criteria, QA stops Redpanda on the stack and reads the topic after it returns.
- Decision: 2026-10-29 (Sofia, pre-build review) — split from 4.1 to keep each story within about 8 criteria (AGENTS.md, Definition of ready); its criteria are 4.1's challenged criteria 3, 4, 5 and 8 plus the pre-build review's points.
- Assumption: 2026-10-29 (Sofia, 4.1 challenge) — the broker-unavailable error codes (criterion 1) are this story's bounds within AD-17 (Failures); confirm at the human review of the diff.
- Builder verifies against the installed versions and cites in the report: librdkafka's guidance for fatal idempotent-producer errors in the pinned Confluent.Kafka 2.16.0 (dispose and recreate), the `Error` constructor that sets `IsFatal` for criterion 2's test, and the Testcontainers pause API.
