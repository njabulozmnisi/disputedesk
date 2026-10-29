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

Adds the failure paths to story 4.1's relay: a broker outage or other transient error pauses the loop without counting an attempt, a permanent record rejection counts toward Failed after 5 (AD-17, Failures), a fatal producer error is classified as fatal and the producer is recreated (the classification is tested, the recreate is read in review), a leader handover during an in-flight send loses nothing (AD-17, Delivery guarantee), and SIGTERM finishes the one record in flight (AD-25, NFR-RES-5).

## Requirements

Verify: Worker integration tests on Testcontainers Postgres and Redpanda pass for the broker outage, the error classification, Failed rows, leader handover during an in-flight send, graceful shutdown and the privacy of the failure surfaces.

1. **Kafka outage does not count as an attempt** (R28; NFR-RES-3)
   **Given** the Redpanda container paused with Testcontainers' `PauseAsync` (present in the installed 4.16.0) while disputes are raised, so the broker keeps its mapped port
   **When** the relay tries to send and the broker is later unpaused with `UnpauseAsync`
   **Then** each failed send leaves its row Pending with `Attempts` 0 and `LastError` null, and the loop pauses with backoff capped at 10 seconds, while the heartbeat stays under the 30-second threshold (AD-17, Failures; AD-25; the heartbeat before each send is 4.1's)
   **And** after the broker returns every waiting row publishes in per-dispute order with `Attempts` 0 on each
   **And** the builder records in the report which `Error.Code` the paused broker produced against the pinned Redpanda image; the test asserts the row outcome, not the code

2. **Each delivery error is classified once** (R28; NFR-RES-3, NFR-RES-4)
   **Given** the relay's error classification, one private static method called directly by the test with `Error(ErrorCode, string, bool isFatal)` values (that constructor is in the installed Confluent.Kafka 2.16.0)
   **When** it is given every code in the lists below, an unlisted code, and an `Error` whose `IsFatal` is true
   **Then** an `Error` with `IsFatal` true is fatal, whatever its code (librdkafka, *INTRODUCTION.md*, idempotent producer fatal errors: dispose and recreate the producer)
   **And** a permanent record rejection is only one of: `Local_MsgSizeTooLarge`, `MsgSizeTooLarge`, `InvalidMsg`, `InvalidMsgSize`, `InvalidRecord`, `Local_BadMsg`; the builder checks each name exists in the installed `ErrorCode` enum, drops any that does not, and lists the final set in the report
   **And** every other error is transient: the broker-unavailable and timeout class of AD-17 (`Local_AllBrokersDown`, `Local_Transport`, `Local_MsgTimedOut`, `Local_TimedOut`), leadership errors such as `NotLeaderForPartition` and `LeaderNotAvailable`, a missing topic (`Local_UnknownTopic`, `UnknownTopicOrPart`), and any unlisted code; transient and fatal errors count no attempt (librdkafka retries retriable errors itself until `message.timeout.ms`, *INTRODUCTION.md*; Confluent.Kafka, *Error* class)
   **And** a fatal error leaves its row Pending with `Attempts` unchanged, and an unlisted code is logged once per occurrence as a warning with the code only

3. **Failed after 5 permanent rejections** (R28, R31; NFR-RES-4)
   **Given** a hand-built outbox row for 4.1's marker dispute (version 2, after the raised version 1), whose `Payload` carries the markers and is padded past the producer's `message.max.bytes` (librdkafka default 1,000,000 bytes), so the client refuses it locally with no broker round trip; the builder verifies that 2.16.0 raises `Local_MsgSizeTooLarge` for it; and a fake `TimeProvider` advanced past each `NextAttemptAtUtc`
   **When** it is refused 5 times
   **Then** each of the first four refusals increments `Attempts`, sets `NextAttemptAtUtc` to the batch instant plus 1 second doubled per earlier attempt (1, 2, 4 and 8 seconds) times a random factor from 0.8 to 1.2, which the test asserts as a range that tolerates microsecond truncation (Azure Architecture Center, *Best practices for transient fault handling*: exponential back-off with jitter; AD-17, Failures), and sets `LastError` to the `ErrorCode` name only, never `Error.Reason`
   **And** the 5th sets the row Failed, increments `Attempts` to 5, leaves `NextAttemptAtUtc` as the 4th set it, increments `disputedesk.outbox.failed` by 1 with no tags (a counter created through `IMeterFactory` on the `DisputeDesk` Meter; Microsoft Learn, *Creating metrics*; AD-20), and writes one `[LoggerMessage]` error with the event ID and dispute ID only, never the `DeliveryResult`, the `Message` or its value
   **And** the dispute's later rows stay Pending and unsent (AD-17: later events wait for an operator), and the refused send's producer span sets only `error.type`, to the Kafka error code, beside 4.1's attribute set

4. **No event lost across a leader handover** (R26, R23; NFR-ARC-4, FR-28)
   **Given** two relays with different fake clocks, and a dispute with versions 1 to 3 pending, with the broker paused so the leader's send of version 1 is in flight
   **When** the test terminates the leader's lock connection (`pg_terminate_backend` on the advisory-lock holder found in `pg_locks`) while the send is in flight, then unpauses the broker, so the old leader's delivery report arrives after its connection is gone: the window between Kafka accepting a record and its Sent update
   **Then** the other relay takes the lock over, no event is lost (every row ends Sent and every version is on the topic), and every row's `SentAtUtc` comes from the new leader's clock, which proves the old leader committed no Sent update, because its updates go only through the terminated connection (PostgreSQL, *Advisory Locks*: a session lock lasts as long as its connection)
   **And** every record that appears more than once carries the same CloudEvent `id` as its first copy; the test asserts at least one copy per version and the same `id` on duplicates, not a duplicate count
   **And** the guarantee is the one AD-17 (Delivery guarantee) states: per-dispute order holds except across a leader handover, where an earlier event can arrive late or twice; consumers apply an event only if its `disputeVersion` is newer than the last one they applied for that dispute and treat a repeated CloudEvent `id` as a duplicate (Enterprise Integration Patterns, *Idempotent Receiver*). The test asserts no loss and same-`id` duplicates, not strict order across the handover

5. **Graceful shutdown finishes the record in flight** (R27; NFR-RES-5)
   **Given** a relay with a send in flight (the broker paused) and further Pending rows
   **When** SIGTERM arrives (the test stops the host and measures the host's `StopAsync` time)
   **Then** the relay sends no further row; the in-flight `ProduceAsync` and its Sent update run with `CancellationToken.None`, bounded by 4.1's 10-second `MessageTimeoutMs` and the 5-second command timeout, and the loop checks `stoppingToken` before each next send (Microsoft Learn, *Background tasks with hosted services*, `BackgroundService`; Confluent.Kafka 2.16.0: the `ProduceAsync` token only stops the wait, not the delivery)
   **And** the in-flight row ends either Sent with its record on the topic (the broker unpaused before the timeout), or Pending with `Attempts` unchanged when the delivery timeout expires first; it is never Failed and never Sent without its record; the test covers both outcomes
   **And** the host stops within the default 30-second `HostOptions.ShutdownTimeout`, which the relay host keeps (Microsoft Learn, *.NET Generic Host*, shutdown timeout; AD-25); Compose sets `stop_grace_period: 35s` on the worker

6. **No personal data on the failure surfaces** (R31; NFR-SEC-5)
   **Given** 4.1's marker dispute (markers in every private field of `DisputeRaised`, `AccountLast4` and the transaction date included, and in the actor ID)
   **When** its send fails during a broker pause, its hand-built row is refused until Failed (criterion 3), and a fatal error recreates the producer
   **Then** no captured log line's attributes or message, `LogRecord.Exception`, span attribute, span `StatusDescription`, span event, metric tag, Kafka header or `LastError` holds any marker (OWASP Logging Cheat Sheet, data to exclude; ASVS 5.0 V16.2; AD-20)
   **And** every failure-path log (transient, fatal, Failed, producer recreate) is a `[LoggerMessage]` with no exception argument, and a failed span sets Error status with no description and records no exception event

## Boundaries

- 4.1's publish, order, leader, trace, options, heartbeat and privacy criteria stay green; their tests are extended, not loosened.
- No dead-letter topic; later events of a Failed row's dispute wait for an operator (AD-17).
- The Api host, the api service in Compose, `backend/openapi.json` and the outbox schema do not change. No migration.
- No test forces "delivered, then timed out" at the client; criterion 4 and 4.1's `id` test prove a resend keeps the CloudEvent `id`.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- `docker compose stop redpanda`, raise twice, start it: both events arrive in order.
- In a throwaway copy, write the Sent update on the scoped `AppDbContext` instead of the lock connection: criterion 4's test fails.
- In a throwaway copy, count a broker-unavailable error as an attempt: criterion 1's test fails.
- In a throwaway copy, pass `stoppingToken` to `ProduceAsync`: criterion 5's test fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes (events leaving the system; R31).
- Lane: M1, after 4.1.
- Already in 4.1 (its fix round, `docs/verification/4.1-fixes.md`), not repeated here: librdkafka's native log lines go through `ProducerBuilder.SetLogHandler` and errors through `SetErrorHandler` to `[LoggerMessage]` warnings with no record values; the heartbeat is touched before and after each send; test connections that hold the advisory lock open with `Pooling=false`, as the relay does. Criterion 4's lock holders reuse that setup. After this story, 4.1's Sent update takes `CancellationToken.None` instead of the stopping token (criterion 5).
- Test seams: the Redpanda container is paused and unpaused with `PauseAsync`/`UnpauseAsync`; `OutboxFixture` exposes them. 4.1's test setup applies (own rows, one collection with parallelisation off). `RelayHost` keeps the default 30-second shutdown timeout instead of its 10-second stop.
- Unverified, for the builder against the installed versions, stated in the report: which code a paused broker gives (criterion 1); the permanent-rejection names in `ErrorCode` (criterion 2); `Local_MsgSizeTooLarge` for an oversized payload (criterion 3); whether `Error.IsRetriable` is set on delivery-report errors (not relied on).
- Touches:
  - `backend/src/DisputeDesk.Infrastructure/Outbox/OutboxRelay.cs` (the classification, `Attempts`, backoff, `LastError`, Failed, the metric, the log lines, the producer recreate, shutdown)
  - `docker-compose.yml` (worker service only: `stop_grace_period: 35s`)
  - `backend/tests/DisputeDesk.Worker.IntegrationTests/Outbox/` (new failure, classification, handover and shutdown test files; `OutboxFixture.cs` pause and unpause; `RelayHost.cs` shutdown timeout; `OutboxRelayPrivacyTests.cs` extended)
- Not touched: `Infrastructure/Persistence/`, `Infrastructure/Observability/Telemetry.cs`, anything under `DisputeDesk.Api`, the api service in `docker-compose.yml`, `backend/openapi.json`, `frontend/`. No migration.

## References

- parent — docs/tickets/epic-4-events/epic-4-events.md
- architecture — docs/architecture/architecture.md, AD-17 (Delivery guarantee, Failures), AD-20, AD-25
- prd — docs/prd/prd.md, NFR-RES-3, NFR-RES-4, NFR-RES-5
- standard — <https://learn.microsoft.com/azure/architecture/best-practices/transient-faults>, exponential back-off with jitter
- standard — <https://docs.confluent.io/platform/current/clients/confluent-kafka-dotnet/_site/api/Confluent.Kafka.Error.html>, `Error`
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0#backgroundservice-base-class>, `BackgroundService`
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-10.0#shutdown-timeout>, host shutdown timeout
- standard — <https://github.com/confluentinc/librdkafka/blob/master/INTRODUCTION.md>, idempotent producer, fatal errors, message timeout
- standard — <https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS>, Advisory Locks
- standard — <https://learn.microsoft.com/dotnet/core/diagnostics/metrics-instrumentation>, creating metrics
- standard — <https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html>, data to exclude
- standard — <https://github.com/OWASP/ASVS>, ASVS 5.0 V16.2
- standard — <https://www.enterpriseintegrationpatterns.com/patterns/messaging/IdempotentReceiver.html>, Idempotent Receiver
- challenge — docs/verification/4.1-challenge.md (this story's criteria come from 4.1's challenged set), docs/verification/4.2-challenge.md
- review — docs/verification/4.1-prebuild-review.md, docs/verification/4.1-prebuild-security.md, docs/verification/4.2-prebuild-review.md, docs/verification/4.2-prebuild-security.md, docs/verification/4.1-fixes.md

## Notes

- Risk high: lost events or rows wrongly Failed; beyond the criteria, QA stops Redpanda on the stack and reads the topic after it returns.
- Decision: 2026-10-29 (Sofia, pre-build review) — split from 4.1 to keep each story within about 8 criteria (AGENTS.md, Definition of ready); its criteria are 4.1's challenged criteria 3, 4, 5 and 8 plus the pre-build review's points.
- Decision: 2026-10-30 (4.2 challenge `[decision]` point, settled by AD-25 and NFR-RES-5) — the shutdown unit is the record in flight, not the batch (criterion 5).
- Decision: 2026-10-30 (Sofia, 4.2 challenge and pre-build review) — Failed means a permanent record rejection from a named list; every other non-fatal error is transient and counts no attempt, so a recoverable row is never Failed (NFR-RES-4). `LastError` holds the code only, because `Error.Reason` is broker free text the repo cannot bound.
- Assumption: 2026-10-30 (Sofia) — an unlisted error code is transient, so an unknown permanent error retries forever without Failed; the per-occurrence warning makes it visible. Confirm at the human review of the diff.
- Assumption: 2026-10-30 (Sofia) — the permanent rejection is induced client-side by an oversized payload instead of a broker `max.message.bytes`, because with idempotence a broker rejection may bump the epoch or raise a fatal error (4.2 pre-build review). Confirm at the human review of the diff.
- Decision: 2026-10-30 (Sofia, 4.2 refine) — `stop_grace_period: 35s` on the api service (AD-25) is not in this story, to keep `docker-compose.yml` edits off 6.1's api service block; it goes with the next story that edits the api service (epic file, R27).
- Reading of AD-17 (Failures): its "per-record error" is NFR-RES-4's permanent rejection ("not a transient failure"); leadership and missing-topic errors are transient. The supervisor may align AD-17's wording; no criterion contradicts its rule.
- Not provable by a test without a fake producer: that a fatal error disposes and recreates the producer. Criterion 2 proves the classification; the reviewer reads the recreate.
- Overlap (2026-10-30): `docker-compose.yml` with 4.1 (worker service, sequential) and 6.1 (api service, a different block). No other shared file with 6.1 or 3.9. No migration.
