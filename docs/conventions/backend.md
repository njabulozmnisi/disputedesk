# Backend conventions

.NET 10, minimal APIs, Clean Architecture with feature folders. Projects: Domain, Application, Infrastructure, Contracts, Api, Worker. Architecture tests enforce the layers and fail the build on a violation.

## Design

- **EF Core directly,** no repository wrapper. `IAppDbContext` lives in Application and only Infrastructure implements it. Application references the EF Core abstractions package only.
- **ORM first.** Raw SQL only with evidence (a measured problem or a feature EF can't express, such as `FOR UPDATE SKIP LOCKED`), parameterised, in one named method in Infrastructure with the justification in a comment. No repository layer for it.
- **Money is `decimal` with an ISO currency code,** never a float.
- **Results for expected failures, exceptions for the unexpected.** The hand-written `Result`/`Error` type in Domain. 404 for not found or not yours, 409 for an invalid transition, 422 for validation or a business rule. A global handler returns a clean 500 without a stack trace.
- **SOLID and patterns with judgement.** No factories DI already provides. (Framework-first and the allowed interfaces are in AGENTS.md.)
- **Actions are actor-agnostic.** A status change is a named action (`start-review`, `escalate`, `resolve`, `withdraw`) authorised by one policy per action. Who acted is recorded for audit, never used to decide.

## Data integrity

- The outbox row is written in the same transaction as the change it describes. The relay uses `FOR UPDATE SKIP LOCKED`, retries with backoff, and a dead-letter status. The Kafka key is the dispute ID.
- `Idempotency-Key` on POST dispute. Optimistic concurrency on the Dispute aggregate.
- Migrations follow expand, migrate, contract. Migrations are always high-stakes and never run in parallel stories.
- Everything is stored in UTC. Inject `TimeProvider`; never read the system clock (the guard hook blocks it).
- The customer is resolved from the identity link table, never from a request field or a client-controlled claim. Ownership is a global query filter.

## Security and privacy

- OWASP basics: validate input at the edge, encode output, least privilege, no secrets in code or logs.
- POPIA: never store or log ID numbers; no personal data in logs, traces or metrics labels. Audit link, unlink and failed attempts.

## Reliability and observability

- OpenTelemetry traces, metrics and logs. Trace context travels through the outbox row into Kafka headers to the Worker.
- `/health/live` and `/health/ready`. `traceId` in every ProblemDetails.
- Structured logs with message templates, never string interpolation.

## API and contract

- `/api/v1`, keyset pagination, ProblemDetails for every error.
- Code-first OpenAPI; `openapi.json` is committed and CI fails on drift. Scalar is the viewer (dev and demo only).
- Every `Location` header points to a real GET endpoint, proved by an integration test.

## Testing

- **Never mock the database.** No EF InMemory, no SQLite. The guard hook blocks both.
- Tier 1: pure unit tests for decisions (state machine, SLA escalation, validation) with an injected clock.
- Tier 2: integration tests against real Postgres via Testcontainers and `WebApplicationFactory`, one container per run, reset with Respawn. Every endpoint is called in every authorisation state.
- Unit-test the decisions, integration-test the plumbing.
- No exact-equality timestamp assertions: a fixed `TimeProvider` or a tolerance.
- No null-forgiving `!` in production code (analyser error; test projects exempt).
