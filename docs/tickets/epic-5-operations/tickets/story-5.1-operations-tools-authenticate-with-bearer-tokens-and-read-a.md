---
id: 1
type: story
title: "Operations tools authenticate with bearer tokens and read a dispute"
parent: epic-5-operations
covers: [R21, R10, R41]
after: [3.8, 3.3, 2.8, 2.5, 2.6]
hitl: false
risk: high
---

# Operations tools authenticate with bearer tokens and read a dispute

## Description

Adds JwtBearer on the operations group (AD-8, AD-9) with the `dispute.read` policy and the actor mapping, and `GET /api/v1/operations/disputes/<disputeId>`, which returns one dispute with every stored event type on its timeline and audits each read as `DisputeRead` through `AuditLog.Record`. It is the epic's tracer bullet: the first bearer credential, the first use of the ownership bypass, and the operations response shape that story 5.2 extends. Provisioning maps the machine user to the `operations` actor type, and a script gets an operations token through the client-credentials grant, so a real tool can read a dispute end to end. Scalar is story 5.5.

## Requirements

Verify: Integration tests (Testcontainers PostgreSQL) with tokens signed by the test key cover credential pinning on every endpoint, token validation, roles and the actor mapping, the read shape with every stored event type, privacy of the body and telemetry, and the `DisputeRead` audit entries; the architecture tests and the platform-script tests pass; `openapi.json` changes only by the read operation and its response records, and `frontend/src/api/generated/` matches it.

1. **Credential pinned per endpoint group** (R21; FR-25, FR-30, NFR-SEC-2)
   **Given** every mapped endpoint and the two credentials: the session cookie and a valid operations bearer token with `dispute.read`
   **When** the cookie calls `GET /api/v1/operations/disputes/<disputeId>`, and the bearer token alone calls a customer route (`GET /api/v1/disputes/<disputeId>`), a linking-group or default-policy endpoint (`GET /api/v1/bff/antiforgery`), and an unmatched path under `/api/v1/operations/`
   **Then** each gets 401 `unauthorized` and never a redirect; the bearer 401 on the operations route carries `WWW-Authenticate: Bearer`, and the cookie-scheme 401s carry none (AD-8)
   **And** `EndpointAuthorizationTests` combines each endpoint's `IAuthorizeData` through `IAuthorizationPolicyProvider` and asserts the schemes are exactly `JwtBearer` under `/api/v1/operations/` and never include `JwtBearer` elsewhere, and that the default authenticate scheme stays the cookie scheme (Microsoft Learn, *Authorize with a specific scheme*; ASVS 5.0 V8.3.1)
   **And** the operations group is mapped on the `apiV1` group, not under `MapCustomerRoutes()`, so it carries no cookie scheme and no antiforgery filter; there is no policy scheme and no `ForwardDefaultSelector` (AD-8 Rejected)

2. **Token validation** (R21; FR-25, NFR-SEC-2)
   **Given** the `Authorization` header, the one credential input, and JwtBearer configured from the `Zitadel` options: issuer `Zitadel:Issuer`, audience `Zitadel:ProjectId` (now `[Required]`, validated on start), signing keys from `Zitadel:MetadataAddress`, `RequireHttpsMetadata` from `Zitadel:RequireHttpsMetadata`, `MapInboundClaims = false`, `RoleClaimType = "role"`, `ValidAlgorithms = ["RS256"]`, `ClockSkew` at its documented default of 5 minutes, and `IncludeErrorDetails = false` (Microsoft Learn, *Configure JWT bearer authentication in ASP.NET Core*)
   **When** the read is called with no header, a non-Bearer scheme, a wrong issuer, a wrong audience, a wrong signature, an unknown `kid`, `alg: none`, HS256 signed with a symmetric key, no `exp`, `exp` more than `ClockSkew` in the past, or `nbf` more than `ClockSkew` in the future
   **Then** each gets 401 `unauthorized` with `WWW-Authenticate: Bearer`: no `error` parameter when the header is missing, and never an `error_description` (RFC 6750 §3, §3.1; RFC 7519 §4.1.4, §4.1.5; ASVS 5.0 V9.1.1, V9.1.2, V9.2.1, V16.5.1), and none of these calls writes an audit row
   **And** the test tokens are minted from the clock JwtBearer validates against: the builder checks whether JwtBearer 10.0.14 passes the injected `TimeProvider` to `TokenValidationParameters.TimeProvider`, and if it does not, mints from the system clock, and states which in the report
   **And** the builder reads the `typ` header of a real Zitadel access token on the running stack: if it is `at+jwt`, `TokenValidationParameters.ValidTypes = ["at+jwt"]` is set and an ID-token `typ` gets 401 (RFC 9068 §4); otherwise the report says the actor mapping is the backstop, which criterion 3's ID-token case proves

3. **Roles and the actor mapping** (R21; FR-25, NFR-SEC-3)
   **Given** `OnTokenValidated` flattens only the claim `urn:zitadel:iam:org:project:<Zitadel:ProjectId>:roles` into one `role` claim per key (AD-9, as amended 2026-10-30), the `dispute.read` policy pins the JwtBearer scheme and requires the `dispute.read` role plus an actor-mapping requirement (`IAuthorizationRequirement` and `AuthorizationHandler`; Microsoft Learn, *Policy-based authorization*), and that handler maps the token's `sub` through `Operations:Actors:<sub>` into a concrete scoped `CurrentOperationsActor` in `Application/Abstractions` holding the `Actor`, as `LinkedCustomerHandler` fills `CurrentCustomer` (AD-10)
   **When** the read is called, on both a real dispute ID and an unknown one, with a valid token holding no roles; a token holding `dispute.read` only under the generic claim `urn:zitadel:iam:org:project:roles`; a token with `dispute.read` whose `sub` has no `Operations:Actors` entry; and a token shaped like a customer's ID token (same issuer and audience, a customer login as `sub`, no roles, and in a second case `dispute.read`)
   **Then** each gets 403 `forbidden`, with the same body for the real and the unknown ID apart from `traceId`, so a caller cannot probe which IDs exist (FR-24 check order: 403 before 404)
   **And** start-up fails when an `Operations:Actors` value is anything but `operations` (the options class validates it with `ValidateOnStart`, AD-26); an empty map is valid and every token then gets 403 (AD-9: exactly one actor type; ASVS 5.0 V10.3.3)
   **And** handlers read only `CurrentOperationsActor`, never `HttpContext.User` (AD-10); story 5.1 adds only the `dispute.read` policy, and story 5.2 adds the other three

4. **Read one dispute** (R21; FR-30)
   **Given** a raised dispute whose stream also holds one stored event of each other type (`ReviewStarted`, `DisputeEscalated`, `DisputeResolved`, `DisputeWithdrawn`, `SlaWarningRaised`), appended by the test as `StoredEventRow` rows in `Version` order (the insert-only guard allows it), plus one row of an unknown type; and the route input `disputeId`, typed `Guid` with no route constraint, as on the customer detail (story 3.3)
   **When** a mapped token with `dispute.read` reads it, reads an unknown ID, and reads `not-a-guid`
   **Then** the body is an `OperationsDisputeResponse`: `disputeId`, `status` (the internal status, `DisputeStatus` camelCase), `outcome` (`upheld` or `rejected` when resolved, else null), `reason`, `transaction` (the stored snapshot, story 3.3's `TransactionSnapshotResponse`), `expectedAmount`, `disputedAmount`, `createdAt` and `timeline`
   **And** `timeline` has one entry per stored event in `Version` order, each exactly `eventType`, `actorType`, `occurredAt`, `fromStatus`, `toStatus` and `note`; `eventType` is a Contracts enum of the six stored event types in camelCase (`disputeRaised` to `slaWarningRaised`, AD-5); `fromStatus` is null on `disputeRaised` and `slaWarningRaised`, and `toStatus` null on `slaWarningRaised`; `note` is set only on `disputeResolved`, the bank's note meant for the customer (see Notes); the unknown-type row is skipped before its data is read, as for customers (`HasKnownEventType`, AD-13)
   **And** the unknown ID gets 404 `not-found` with the same body as story 3.3's customer 404 apart from `traceId` (AD-11), and `not-a-guid` gets 400 `validation` naming `disputeId` without echoing it, after 401 and 403 (AD-3, AD-8)
   **And** the handler `ReadDisputeHandler` in `Application/Operations/ReadDispute/` reads the `Disputes` row and the stored events with the named bypass `IgnoreQueryFilters(["Ownership"])` on both queries, never the parameterless form (EF Core, *Global query filters*, named filters; AD-11); Application does not reference `AppDbContext.OwnershipFilter` (`LayerTests`), so the literal is used, with a comment naming that constant

5. **Nothing personal beyond the snapshot** (R21; FR-30, NFR-SEC-6)
   **Given** a dispute raised by a seeded customer with a free-text note and withdrawn with a note, read by a mapped token
   **When** the response is serialised and the telemetry of the read is captured
   **Then** the response and timeline records hold exactly the fields of criterion 4, built by an explicit mapping in Application (`OperationsView`), never by serialising a `StoredEvent` record or `StoredEvents.Data`
   **And** a value scan of the body finds no seeded `CustomerId`, no customer `LoginSub`, no operations `sub`, no `ActorId` value, no `TraceId` and neither note the customer wrote (ASVS 5.0 V14.2.6; the 3.8 Known pitfall); a new Application record that holds the snapshot seals `ToString`
   **And** no exported span attribute and no log line of the read holds the customer ID, the account reference or the `Authorization` value, as `TransactionsTelemetryTests` checks for the transactions list (AD-20); IdentityModel's `ShowPII` stays false

6. **Each read audited** (R10, R21, R41; FR-25, NFR-SEC-10)
   **Given** a mapped token with `dispute.read`
   **When** it reads a dispute, reads an unknown ID, and reads a dispute while `FailAuditInsertInterceptor` is armed
   **Then** the successful read writes exactly one `AuditLogEntries` row: action `DisputeRead`, result `Succeeded`, actor type `Operations`, actor ID the token's `sub`, the dispute ID and no customer ID; the unknown ID writes exactly one row with result `Rejected` and the requested dispute ID (the column has no foreign key); both go through `AuditLog.Record` (story 2.8; AD-14a), and `AuditAction` gains `DisputeRead`
   **And** the handler saves the audit row before it returns the body, so the armed interceptor gives 500 `internal` with no dispute data in the body and no row, failing closed as the sign-in does (AD-7; OWASP Logging Cheat Sheet; ASVS 5.0 V16.3.2)
   **And** a 401, a 403 or a 400 from this story writes no row here; the 403 is recorded as `Denied` by story 5.2's result handler (Boundaries)

7. **Provisioned actor mapping and an operations token** (R21; FR-25)
   **Given** `provision.sh` and the new `scripts/operations-token.sh`
   **When** the platform-script tests read them and the supervisor runs them on the stack
   **Then** `provision.sh` writes `Operations:Actors:<the machine user's ID>` = `operations` into `zitadel-clients.json` beside the `Zitadel:*` keys (the machine user's ID is its token `sub`), and its "is current; secrets not rotated" check also requires `.Operations.Actors[$OPS_ID] == "operations"`, so an existing volume gains the key on the next run (AD-26)
   **And** `scripts/operations-token.sh` runs the client-credentials grant (RFC 6749 §4.4) against `${AUTH_ORIGIN:-http://auth.localhost:8080}/oauth/v2/token` with the AD-9 scope, reading the machine user's credentials from the `zitadel-clients` volume through the provisioner image as `infra/smoke.sh` does, passing them to curl on stdin and never as an argument, and prints only the access token
   **And** `PlatformScriptTests` asserts the actor key, the extended current check, and that the script passes no secret as an argument

8. **Bypass and reference rules** (R21; NFR-SEC-6, AD-11)
   **Given** the architecture tests
   **When** they run
   **Then** `OwnershipRulesTests` fails on the parameterless `IgnoreQueryFilters()` anywhere, keeps the named form to `DisputeDesk.Application/Operations/` and `DisputeDesk.Worker/Sla/`, and fails when a file under `Disputes/` or `Transactions/` references the `Operations` or `Sla` namespaces (AD-11; sample-line cases for each, as the test has for the bypass)

## Boundaries

- Customer routes, the linking group and the fallback policy stay cookie-only; story 3.3's detail, its identical 404, the customer allowlist and `CustomerRoutesTests` stay green.
- A bearer request to an unmatched path under `/api/v1/operations/` gets the fallback policy's cookie 401 without `WWW-Authenticate`, because no endpoint matched (AD-8 rejects a per-request scheme choice).
- The 403 on the read route is recorded as `Denied` by story 5.2's `IAuthorizationMiddlewareResultHandler`, which covers every operations route; this story writes no `Denied` row.
- Customer reads, refused ones included, still write no audit row (AD-14a).
- No operations list or search (FR-30).
- Scalar stays as merged (Development only) until story 5.5; the OpenAPI document gains the read operation but no security scheme here.
- The Api has no option or key for the operations client secret (AD-26 decision, 2026-10-30).

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- On the running stack, with a token from `scripts/operations-token.sh`, `curl` reads a dispute customer A raised through the customer API (200 with every criterion 4 field), and `AuditLogEntries` holds one `DisputeRead` `Succeeded` row with the machine user's ID; the same call with A's session cookie only gets 401.
- In a throwaway copy, also accept the generic `urn:zitadel:iam:org:project:roles` claim: criterion 3's generic-claim case fails.
- In a throwaway copy, drop the actor-mapping requirement from the policy: criterion 3's unmapped and ID-token cases fail.
- In a throwaway copy, return the body before saving the audit row: criterion 6's armed-interceptor case fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Package: `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.14 (not in the shared framework), pinned like OpenIdConnect 10.0.14; the builder confirms publisher, downloads and version on nuget.org (AGENTS.md, Dependency verification).
- Names: `ReadDisputeHandler` (`Application/Operations/ReadDispute/`), `OperationsView` (the explicit mapping), `CurrentOperationsActor` (`Application/Abstractions`), `OperationsDisputeResponse`, `OperationsTimelineEntryResponse` and the stored event type enum in `Contracts/Operations/`.
- Tests: `FakeZitadel` signs access tokens with its RS256 key and serves it through the discovery document JwtBearer reads; `ApiFactory` sets `Zitadel:ProjectId` and `Operations:Actors`.
- Touches:
  - `backend/Directory.Packages.props`, `backend/src/DisputeDesk.Api/DisputeDesk.Api.csproj`
  - `backend/src/DisputeDesk.Contracts/Audit/AuditAction.cs` (`DisputeRead`), `backend/src/DisputeDesk.Contracts/Operations/` (new)
  - `backend/src/DisputeDesk.Api/Operations/` (new: endpoints, JwtBearer setup, the actor requirement and handler, the actors options), `backend/src/DisputeDesk.Api/Bff/ZitadelOptions.cs` (`ProjectId`), `backend/src/DisputeDesk.Api/Program.cs`
  - `backend/src/DisputeDesk.Application/Abstractions/CurrentOperationsActor.cs`, `backend/src/DisputeDesk.Application/Operations/ReadDispute/` (new)
  - `infra/zitadel/provision/provision.sh`, `scripts/operations-token.sh` (new)
  - `backend/openapi.json`, `frontend/src/api/generated/` (regenerated with `npm run generate`; CI's Orval drift step fails otherwise)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Operations/` (new), `Authorization/EndpointAuthorizationTests.cs`, `Infrastructure/FakeZitadel.cs`, `Infrastructure/ApiFactory.cs`
  - `backend/tests/DisputeDesk.ArchitectureTests/OwnershipRulesTests.cs`, `PlatformScriptTests.cs`
- Not touched: `ApiReferenceTests.cs` and the Scalar mapping (story 5.5), `infra/nginx/templates/`, `Infrastructure/Persistence/` (no migration), the Worker.

## References

- parent — docs/tickets/epic-5-operations/epic-5-operations.md
- architecture — docs/architecture/architecture.md, AD-3, AD-5, AD-7, AD-8, AD-9, AD-10, AD-11, AD-13, AD-14a, AD-20, AD-26
- prd — docs/prd/prd.md, FR-24, FR-25, FR-30, NFR-SEC-2, NFR-SEC-3, NFR-SEC-6, NFR-SEC-10
- standard — <https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0>, JWT bearer authentication
- standard — <https://learn.microsoft.com/aspnet/core/security/authorization/authorize-with-a-specific-scheme?view=aspnetcore-10.0>, authorize with a specific scheme
- standard — <https://learn.microsoft.com/aspnet/core/security/authorization/policies?view=aspnetcore-10.0>, policy-based authorization
- standard — <https://learn.microsoft.com/ef/core/querying/filters>, global query filters (named filters)
- standard — <https://www.rfc-editor.org/rfc/rfc6750#section-3>, the Bearer challenge
- standard — <https://www.rfc-editor.org/rfc/rfc7519#section-4.1>, registered JWT claims
- standard — <https://www.rfc-editor.org/rfc/rfc9068#section-4>, JWT access token `typ`
- standard — <https://www.rfc-editor.org/rfc/rfc6749#section-4.4>, client credentials grant
- standard — <https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html>, OWASP Logging Cheat Sheet
- standard — <https://github.com/OWASP/ASVS>, ASVS 5.0 V8.3.1, V9.1.1, V9.1.2, V9.2.1, V10.3.3, V14.2.6, V16.3.2, V16.5.1
- challenge — docs/verification/5.1-challenge.md
- review — docs/verification/5.1-prebuild-review.md, docs/verification/5.1-prebuild-security.md

## Notes

- Risk high: a second credential type on the customers' origin; beyond the criteria, the security agent sends a bearer token to every customer and linking route.
- Decision: 2026-10-30 (human, 5.1 `[decision]` points 1) — Scalar never holds or pre-fills a client secret, locally or in AWS. `scripts/operations-token.sh` runs the client-credentials grant (RFC 6749 §4.4, a confidential client) and a person pastes the bearer token into Scalar (story 5.5). The Api holds no operations client secret. This replaces the 2026-10-27 "local stand-in" decision (AD-26 amended).
- Decision: 2026-10-30 (human, 5.1 `[decision]` point 2) — one fixed CSP hash for Scalar, with identical options in every environment; nginx keeps owning the headers (AD-23 unchanged). Built in story 5.5.
- Decision: 2026-10-30 (human, 5.1 `[decision]` point 3) — AD-9 drops the generic `urn:zitadel:iam:org:project:roles` fallback; only `urn:zitadel:iam:org:project:<ProjectId>:roles` is read (criterion 3; ASVS 5.0 V10.3.2).
- Decision: 2026-10-30 (Sofia, 5.1 pre-build review) — split to stay within about 8 criteria (AGENTS.md, Definition of ready): Scalar, its switch, the security schemes and the gateway CSP move to story 5.5; this story keeps the API, the audit, provisioning and the token script.
- Decision: 2026-10-30 (Sofia, from FR-30) — the operations response leaves out every note the customer wrote: the raise note and the withdraw note. FR-30 lists the fields as "snapshot, reason, amount, status, outcome and timeline" and says "the customer's personal details are not returned beyond what the snapshot holds"; free text written by the customer is not in that list and can hold personal details. The resolve note stays on its timeline entry: the bank wrote it, and it is the note meant for the customer that the timeline carries (glossary: Timeline).
- Decision: 2026-10-30 (Sofia, 5.1 pre-build review) — the read's 404 is audited here as `DisputeRead` `Rejected` (FR-25: "a rejected operations attempt after authentication (403, 404 or 409)"; NFR-SEC-10: every operations read); the audit entry carries no customer ID, as FR-25 names only the actor and the dispute ID.
- Production path: lines for `docs/production-path.md` are in story 7.6's Notes (Input from 5.1, 2026-10-30).
