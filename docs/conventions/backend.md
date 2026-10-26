# Backend conventions

.NET 10, minimal APIs, Clean Architecture with feature folders. Projects: Domain, Application, Infrastructure, Contracts, Api, Worker. Architecture tests enforce the layers and fail the build on a violation.

## Design

- **EF Core directly,** no repository wrapper. `IAppDbContext` lives in Application and only Infrastructure implements it. Application may reference EF Core itself (for `DbSet<T>`) but never a database provider such as Npgsql.
- **ORM first.** Raw SQL only with evidence (a measured problem or a feature EF can't express, such as `FOR UPDATE SKIP LOCKED`), in one named method in Infrastructure with the justification in a comment. Use `FromSql` / `ExecuteSql` / `SqlQuery` (interpolated, auto-parameterised); never the `*Raw` variants with concatenated input. No repository layer for it.
- **Money is `decimal` with an ISO currency code,** never a float.
- **Enum casing.** C# enum members are PascalCase. API JSON and event payloads use camelCase values through `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`; URL segments and problem `type` slugs are kebab-case.
- **Results for expected failures, exceptions for the unexpected.** The hand-written `Result`/`Error` type in Domain. 400 for malformed input (built-in minimal API validation), 404 for not found or owned by another customer, 409 for an action not allowed in the current status, 422 for a well-formed request that breaks a business rule; a global handler returns a clean 500 without a stack trace.
- **Methods returning `Task`/`ValueTask` end in `Async`** (test methods exempt).
- **One top-level type per file.**
- **SOLID and patterns with judgement.** No factories DI already provides. (Framework-first and the allowed interfaces are in AGENTS.md.)
- **Actions are actor-agnostic.** A status change is a named action (`start-review`, `escalate`, `resolve`, `withdraw`) authorised by one policy per action. Who acted is recorded for audit, never used to decide.

## Data integrity

- The outbox row is written in the same transaction as the change it describes. A single relay runs under a Postgres advisory lock, retries with backoff, and sets a `Failed` status after 5 attempts; there is no dead-letter topic (architecture AD-17). `SKIP LOCKED` is used by the SLA sweep, not the relay. The Kafka key is the dispute ID.
- `Idempotency-Key` on POST dispute. Optimistic concurrency on the Dispute aggregate.
- Migrations follow expand, migrate, contract. Migrations are always high-stakes and never run in parallel stories.
- Everything is stored in UTC. Inject `TimeProvider`; never read the system clock (the guard hook blocks it).
- The customer is resolved from the identity link table, never from a request field or a client-controlled claim. Ownership is the EF Core 10 named query filter `"Ownership"`; bypasses use `IgnoreQueryFilters(["Ownership"])` in the two named places only (the `Operations/` feature folder and the Worker's sweep).

## Database naming

PascalCase throughout, which is what EF Core and Npgsql generate by default (decided 2026-10-26; architecture AD-22).

- **Tables** are plural PascalCase: `Disputes`, `TimelineEntries`, `CustomerLinks`, `LinkingChallenges`, `OutboxMessages`, `IdempotencyKeys`, `AuditLogEntries`, `Sessions`, `DataProtectionKeys`.
- **Columns** are PascalCase and match the C# entity property: `CustomerId`, `CreatedAtUtc`.
- **Keys.** The primary key is `Id`. A foreign key property and column is `<EntityName>Id` (`DisputeId`).
- **Instants.** A column holding a UTC instant ends in `Utc`: `CreatedAtUtc`, `SlaDueAtUtc`, `OccurredAtUtc`, `ExpiresAtUtc`.
- **Enums** are stored as `text` holding the C# member name (`HasConversion<string>()`).
- **Constraint names:**
  - primary key `PK_<TableName>`
  - foreign key `FK_<DependentTable>_<PrincipalTable>_<ForeignKeyColumn>`
  - index `IX_<TableName>_<ColumnNames>`
  - unique constraint or unique index `UQ_<TableName>_<ColumnNames>`. EF Core names unique indexes `IX_` by default, so every unique one sets `HasDatabaseName` explicitly
  - check constraint `CK_<TableName>_<Description>`
- **Generated SQL first.** Let EF Core and Npgsql generate and quote identifiers wherever possible. Don't add `EFCore.NamingConventions` or any automatic snake_case conversion.
- **Handwritten SQL double-quotes every PascalCase identifier**, or Postgres folds it to lower case: `SELECT ... FROM "Disputes" WHERE "Status" IN ('Submitted','UnderReview') AND "SlaDueAtUtc" <= @now FOR UPDATE SKIP LOCKED`.
- **Review every generated migration** for naming before committing it: table, column and constraint names follow these rules, and unique indexes carry their `UQ_` name.
- **Infrastructure names are not schema objects.** The database names (`disputedesk`, `zitadel`), Postgres role names (`qa_readonly`) and Compose or Terraform identifiers keep their own style and are out of scope for these rules.

## Security and privacy

- OWASP basics: validate input at the edge, encode output, least privilege, no secrets in code or logs.
- POPIA: never store or log ID numbers; no personal data in logs, traces or metrics labels. Audit link, unlink and failed attempts.

## Reliability and observability

- OpenTelemetry traces, metrics and logs. Trace context travels through the outbox row into Kafka headers to the Worker.
- `/health/live` and `/health/ready`. `traceId` in every ProblemDetails.
- Log through `[LoggerMessage]` source-generated methods with PascalCase placeholders; never string interpolation.

## API and contract

- `/api/v1`, keyset pagination, ProblemDetails for every error.
- Code-first OpenAPI; `openapi.json` is committed and CI fails on drift. Scalar is the viewer (dev and demo only).
- Every `Location` header points to a real GET endpoint, proved by an integration test.

## Testing

- **Never mock the database.** No EF InMemory, no SQLite. The guard hook blocks both.
- Tier 1: pure unit tests for decisions (state machine, SLA escalation, validation) with an injected clock.
- Tier 2: integration tests against real Postgres via Testcontainers and `WebApplicationFactory`, one container per run, reset with Respawn. Every endpoint is called in every authorisation state.
- Unit-test the decisions, integration-test the plumbing.
- Test method names follow Microsoft's `Method_Scenario_Expected` convention (`Withdraw_WhenResolved_Returns409`); CA1707 is relaxed for tests only.
- No exact-equality timestamp assertions: a fixed `TimeProvider` or a tolerance.
- No null-forgiving `!` in production code (analyser error; test projects exempt).

## Deliberate differences from Microsoft defaults

- `Result`/`Error` for expected failures, not exceptions.
- The type is named `Error`; CA1716 is relaxed for Domain type names only.
- 400 for malformed input and 422 for a broken business rule, not 400 for both.
- Keyset pagination, not offset.
- Action endpoints, not a `PATCH` that sets the status.
- Test method names use underscores (`Method_Scenario_Expected`), so CA1707 is relaxed for tests.
- Constants and static readonly fields are PascalCase at every accessibility; other private fields, static or not, are `_camelCase` (Microsoft uses `s_` for private statics).
- Migrations run at Api start-up; migration bundles are a production-path note.
- Problem `type` values are tag URIs, not resolvable URLs.
