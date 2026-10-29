---
id: 1
type: story
title: "Outbox relay publishes dispute events to Kafka with a Worker heartbeat"
parent: epic-4-events
covers: [R23, R26, R27, R28, R31, R36]
after: [3.8]
hitl: false
risk: high
---

# Outbox relay publishes dispute events to Kafka with a Worker heartbeat

## Description

Adds the single-leader relay (AD-17) on its own lock connection, with trace propagation (AD-20), its Kafka options (AD-26) and the relay heartbeat health check (AD-25). Story 4.2 adds the failure paths on top: broker outages, per-record errors and Failed rows, the fatal-error recreate, leader handover during an in-flight send, and graceful shutdown.

## Requirements

Verify: Worker integration tests on Testcontainers Postgres and Redpanda pass for publish, per-dispute order, one leader, trace context, the Kafka options and unreachable dependencies, the heartbeat and the privacy of the Kafka boundary; the existing `WorkerHostTests` pass unedited.

1. **Publish byte for byte** (R23, R28; FR-27, FR-28)
   **Given** a pending outbox row whose `NextAttemptAtUtc` is due
   **When** the relay runs a batch
   **Then** the record goes to `disputedesk.disputes` keyed by dispute ID, its value equal to `Payload`, and its headers are exactly `traceparent`, `content-type: application/cloudevents+json` and, only when the extracted context carries one, `tracestate`; no `baggage` header and no other header is written (W3C Trace Context; CloudEvents Kafka protocol binding, structured mode)
   **And** the row is marked Sent, with `SentAtUtc` set, only after its delivery report succeeds; each batch reads `TimeProvider.GetUtcNow()` once at its start, selects due rows with `"NextAttemptAtUtc" <= @now`, and stamps `SentAtUtc` with that instant (AD-4)
   **And** the producer runs with `EnableIdempotence = true` and `Acks.All` (AD-17, Producer), which the test reads back from the producer configuration
   **And** the published CloudEvent `id` equals the outbox row's ID, and `Payload`, `Id`, `EventType`, `DisputeId`, `DisputeVersion`, `TraceParent` and `TraceState` are unchanged after the row is Sent; the relay never mints an ID (CloudEvents 1.0 `id`; AD-17, AD-19, ad-009)

2. **Per-dispute order** (R23, R28; FR-28, NFR-RES-3)
   **Given** versions 1 to 3 of one dispute pending, and pending rows of a second dispute
   **When** the relay runs
   **Then** for each dispute only its head row (the lowest version not Sent) is produced, one send at a time, and the next version only after the previous delivery report, so versions 1 to 3 reach the topic in order (AD-17, Order)
   **And** a Failed or not-yet-due head row, set directly by the test, holds back the later rows of that dispute only, while the second dispute publishes

3. **One leader, on its own lock connection** (R26; NFR-ARC-4)
   **Given** two Worker hosts against one database, each with its own in-memory span exporter
   **When** both run over the same pending rows
   **Then** only the host holding `pg_try_advisory_lock` on the fixed key produces: the other host's exporter records no producer span, and each row is published once (PostgreSQL, *Advisory Locks*)
   **And** the relay holds the lock on one dedicated `NpgsqlConnection` and does every outbox read and update on that same connection, through a short-lived `AppDbContext` built on it (`UseNpgsql(connection)`, the context not owning the connection; EF Core, *DbContext configuration*, using an existing connection); before each batch it checks the connection with a cheap query, and on an error it disposes the connection and produces nothing until it holds the lock again (AD-17, Leadership)
   **And** the only raw SQL is the lock call and the check query: parameterised (`NpgsqlParameter`, no interpolation), identifiers double-quoted (AD-22), no `now()` or `CURRENT_TIMESTAMP` (AD-4; a reviewer check), no table other than `"OutboxMessages"`, on the existing `ConnectionStrings:DisputeDesk` with no new credential; updates set only `Status`, `Attempts`, `NextAttemptAtUtc`, `SentAtUtc` and `LastError`; the AD-14 source-rules test stays green unedited; the report cites AD-17's same-connection rule as the evidence for the raw SQL

4. **Trace continues** (R36; NFR-OBS-1)
   **Given** outbox rows with a valid stored `TraceParent` with and without `TraceState`, with none, with a malformed `traceparent`, and with a valid `traceparent` beside a `tracestate` that breaks a W3C list rule the propagator enforces
   **When** each is produced
   **Then** the stored pair is read through `Propagators.DefaultTextMapPropagator.Extract` over a carrier of the two columns, never parsed by hand, and a producer span starts on the ActivitySource `DisputeDesk` (`Telemetry.ActivitySourceName`, beside `MeterName`, added with `AddSource` in `WithTracing`; AD-20): kind Producer, named `send disputedesk.disputes`, with exactly the attributes `messaging.system` `kafka`, `messaging.operation.name` and `messaging.operation.type` `send`, `messaging.destination.name`, `messaging.kafka.message.key` and `messaging.message.id`, and no message body attribute (OpenTelemetry Kafka semantic conventions, the version the pinned OpenTelemetry .NET release follows; the report cites it)
   **And** its parent is the extracted context when it is valid, and it is a root span when the context is absent or malformed, with no error and the row still published
   **And** the headers are injected through `Propagators.DefaultTextMapPropagator.Inject` with a `PropagationContext` of the span's context and `default(Baggage)`, so the relay formats no trace header itself, writes no `baggage` header, and forwards no `tracestate` the propagator dropped; the record with the rule-breaking `tracestate` publishes without it

5. **Kafka options and unreachable dependencies** (R27, R28; NFR-RES-1, NFR-RES-3)
   **Given** one options class bound from `Kafka:BootstrapServers` with `[Required]`, `ValidateDataAnnotations` and `ValidateOnStart` (Microsoft Learn, *Options pattern*; AD-26), `localhost:19092` in the Worker `appsettings.json`, `redpanda:9092` in the Compose worker environment, and no credential anywhere
   **When** the key is empty, and separately when Postgres or the broker cannot be reached (no connection string, or nothing listening on the bootstrap address)
   **Then** an empty key stops the host at start with an options validation error; an unreachable dependency is logged with one `[LoggerMessage]` warning that carries no connection string or record value, the relay retries with backoff capped at 10 seconds, keeps touching its heartbeat, leaves every row Pending with `Attempts` unchanged, and stops when the host stops
   **And** the existing `WorkerHostTests` (start then stop within 10 seconds with no database or broker) pass unedited

6. **Heartbeat** (R27; NFR-RES-1)
   **Given** the Worker
   **When** the relay loops as leader, as non-leader on each lock attempt, while idle and while backing off
   **Then** `/tmp/heartbeat-relay` is touched at least every 10 seconds: after each send, on each lock attempt, and after each batch; an idle relay waits at most 10 seconds between batches, one send waits at most 10 seconds for its delivery report (`MessageTimeoutMs` 10,000), heads are sent one at a time, and backoff is capped at 10 seconds, so no loop leaves the file older than the 30-second health-check threshold (AD-25)
   **And** the Compose worker health check fails when the relay file is older than 30 seconds, the worker `depends_on` the api being healthy, and story 5.3 adds the sweep file

7. **No personal data at the Kafka boundary** (R31; NFR-SEC-5)
   **Given** a dispute raised with marker values in every private field of `DisputeRaised` (customer ID, account reference, transaction ID, the snapshot's description and masked card, note, expected and disputed amounts) and in the actor ID
   **When** the relay publishes its row
   **Then** no captured log line, span attribute, metric tag or Kafka header holds any marker, and the record value is `Payload`, whose content 3.8's tests already guard (`PublishMappingTests`, `RaisedEventTests`) (OWASP Logging Cheat Sheet, data to exclude; AD-20; 3.8 Known pitfall: check every private field)
   **And** story 4.2 extends this test to the failure surfaces (`LastError`, the Failed log line, `disputedesk.outbox.failed`, `error.type`)

## Boundaries

- Every failed send in this story leaves its row Pending with `Attempts` unchanged and backs the loop off (criterion 5). Telling broker outages from per-record errors, `Attempts`, `LastError`, Failed rows, `disputedesk.outbox.failed` and the fatal-error recreate are story 4.2; so are leader handover during an in-flight send, graceful shutdown and `stop_grace_period`.
- Plaintext Kafka with no client authentication on the private Compose network is accepted for v1 (human, 2026-10-29). The producer configuration carries `// shortcut: plaintext, no client auth on the private Compose network; upgrade to TLS plus SASL/SCRAM or mTLS with topic ACLs (only the relay principal writes) before a shared network`, and story 7.6 writes the matching `docs/production-path.md` entry (its Notes, Input from 4.1; AD-26).
- The AWS override (`docker-compose.aws.yml`) is story 7.4; if it needs the key, it sets it with `${VAR:?}` there.
- The Api host, `backend/openapi.json` and the outbox schema do not change. No migration.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Re-run the telemetry smoke check: one trace from the browser raise to the Worker producer span in the Aspire Dashboard.
- `docker compose ps` shows the worker healthy and starting after the api.
- Trivy on the `worker-local` image; `docker compose exec worker id -u` is not 0.
- In a throwaway copy, drop the lock check so both hosts produce: criterion 3's test fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes (events leaving the system; R31).
- Lane: M1, after 2.8, in parallel with 3.3 on M2 (collision check in Notes).
- Shape: one `BackgroundService` (`OutboxRelay`) plus one options class (`KafkaOptions`); the error handling and the heartbeat are private methods, not types. No index is added for head-row selection; a `// shortcut:` comment names the index to add when the outbox grows.
- Packages: `Confluent.Kafka` 2.16.0 (architecture, Stack) and `Testcontainers.Redpanda` 4.16.0, each verified and pinned per AGENTS.md (Dependency verification); the report includes `dotnet list package --vulnerable --include-transitive` (Confluent.Kafka brings in `librdkafka.redist`).
- Test setup:
  - Every test inserts its own rows for fresh dispute IDs and asserts only on Kafka records with those keys and on those rows; none depends on an empty outbox or topic (AD-17, Seeded rows; AD-30). Version 1 is raised through the commit (the outbox foreign key needs a `Disputes` row, and only `Raise` exists); later versions are hand-built outbox rows.
  - The relay tests share one xUnit collection with parallelisation off, because the advisory-lock key is fixed and the database is shared (xUnit, *Running tests in parallel*).
  - A Worker Postgres fixture (Testcontainers.PostgreSql) applies the migrations with `MigrateAsync`. Redpanda runs the Compose image pinned by tag and digest (`redpanda:v26.2.2@sha256:468bd13a…`), not the module's default tag. Tests create `disputedesk.disputes` with 3 partitions through the admin client, as Compose does.
- Touches:
  - `backend/src/DisputeDesk.Worker/` (`WorkerHost.cs` registers the relay and the options, `appsettings.json` adds `Kafka:BootstrapServers`, `DisputeDesk.Worker.csproj`)
  - `backend/src/DisputeDesk.Infrastructure/Outbox/` (new: `OutboxRelay.cs`, `KafkaOptions.cs`)
  - `backend/src/DisputeDesk.Infrastructure/Observability/Telemetry.cs` (`ActivitySourceName`, `AddSource`)
  - `backend/src/DisputeDesk.Infrastructure/DisputeDesk.Infrastructure.csproj` (the Confluent.Kafka reference)
  - `backend/Directory.Packages.props` (Confluent.Kafka, Testcontainers.Redpanda)
  - `backend/Dockerfile` (`worker-local` target: from `worker`, keeps `USER $APP_UID`, adds only busybox from the pinned `probe` image, as `api-local` and `core-banking` do; AD-27)
  - `docker-compose.yml` (worker service only: `Kafka__BootstrapServers`, the relay-file health check, `depends_on` api healthy, the `worker-local` target)
  - `backend/tests/DisputeDesk.Worker.IntegrationTests/` (new relay test files and fixtures, its `.csproj`); `WorkerHostTests.cs` and `WorkerTelemetryTests.cs` unedited
- Not touched: `Infrastructure/Persistence/` (the relay reads `OutboxMessages` as AD-22 defines it), `Infrastructure/DependencyInjection.cs`, anything under `DisputeDesk.Api`, `backend/openapi.json`, `docker-compose.aws.yml`. No migration.

## References

- parent — docs/tickets/epic-4-events/epic-4-events.md
- architecture — docs/architecture/architecture.md, AD-4, AD-17 (Leadership, Order, Producer), AD-20, AD-22, AD-25, AD-26, AD-27
- challenge — docs/verification/4.1-challenge.md
- review — docs/verification/4.1-prebuild-review.md, docs/verification/4.1-prebuild-security.md

## Notes

- Risk high: lost or reordered events, or personal data leaving the system; beyond the criteria, QA reads the topic for one raise.
- Decision: 2026-10-29 — 3.8 proves stored event ID = outbox ID; this story proves the published CloudEvent `id` = outbox ID (ad-009). It runs on M1 after 2.8 (lane plan).
- Decision: 2026-10-29 (human, 4.1 challenge) — delivery is at-least-once with a version check, as AD-17 (Delivery guarantee) states; story 4.2 holds the handover test. Fencing with Kafka transactions is the stronger option, recorded by AD-17 for the production path.
- Decision: 2026-10-29 (human, 4.1 pre-build security S4) — plaintext Kafka on the private Compose network is accepted for v1 as a `// shortcut:`; broker SASL/SCRAM or mTLS is a production-path item, written by story 7.6 (Boundaries).
- Decision: 2026-10-29 (human) — the configuration key is `Kafka:BootstrapServers`: `localhost:19092` in Worker `appsettings.json`, `redpanda:9092` in the Compose worker environment (AD-26). A broker address is not a secret, so it may sit in `appsettings.json`; no credential does.
- Decision: 2026-10-29 (Sofia, pre-build review) — 4.1 split into 4.1 (publish, order, leader, trace, options, heartbeat, privacy) and 4.2 (failure paths, handover, shutdown) to keep each within about 8 criteria (AGENTS.md, Definition of ready).
- Assumption: 2026-10-29 (Sofia, 4.1 challenge) — the 10-second send timeout and backoff cap (criteria 5 and 6) are this story's bounds within AD-17 (Failures) and AD-25; confirm at the human review of the diff.
- Builder verifies against the installed versions and cites in the report: which `tracestate` rules the pinned OpenTelemetry .NET `TraceContextPropagator` enforces on `Extract` (its source), and picks criterion 4's rule-breaking `tracestate` from a rule it enforces (W3C allows at most 32 list members and 512 characters).
- Collision check (2026-10-29): 3.8 is merged. No file overlap with 2.8 (Bff/, Authorization tests, ApiReferenceTests, `TransactionsApi.cs`, `Application/Audit/`, CoreBanking stub files) or with 3.3 and 3.9 (Application/Disputes, Contracts/Disputes, Api/Disputes, Api integration tests, ArchitectureTests, `openapi.json`); only `Directory.Packages.props` is shared, and neither 2.8 nor 3.3 edits it. No migration, so 4.1 and 3.3 may run at the same time on two machines.
