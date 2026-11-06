---
name: DisputeDesk
type: architecture-spine
purpose: build-substrate
altitude: initiative
paradigm: Clean Architecture (ports and adapters) with feature folders, plus server-side browser sessions, an event-sourced Dispute (stored events) and a transactional outbox
scope: The whole DisputeDesk initiative - Api host (browser sessions and REST), Worker, core-banking stub, SPA, Compose platform, CI and the AWS dev environment
status: final
created: '2026-10-26'
updated: '2026-11-04'
binds: [R1, R2, R3, R4, R5, R6, R7, R8, R9, R10, R11, R12, R13, R14, R15, R16, R17, R18, R19, R20, R21, R22, R23, R24, R25, R26, R27, R28, R29, R30, R31, R32, R33, R34, R35, R36, R37, R38, R39, R40, R41]
sources:
  - docs/initiative-disputedesk.md
  - docs/prd/prd.md
  - docs/ux/ux.md
  - docs/ux/DESIGN.md
  - docs/ux/EXPERIENCE.md
  - docs/research/dispute-lifecycle/dispute-lifecycle.md
  - docs/research/event-versioning/event-versioning.md
  - docs/conventions/backend.md
  - docs/conventions/frontend.md
  - docs/glossary.md
  - AGENTS.md
  - docs/verification/brief-es-decisions.md
  - docs/decisions/ad-009-stored-events.md
  - backend/ (P0-1, P0-3), docker-compose.yml and infra/ (P0-2), .github/workflows/ (P0-4)
---

# Architecture Spine - DisputeDesk

This spine is the consistency contract for every DisputeDesk story. Each `AD-n` fixes something that two separately built stories could otherwise get incompatibly wrong. `[ADOPTED]` means an earlier decision or merged code already settled it. `Accepted 2026-10-26` marks the five recommendations accepted at Gate 1. Any other `[ASSUMPTION]` is an inference made in this headless run. The PRD keeps the testable detail.

Each AD states its current rule only: **Rule** (two to five sentences), **Details** where the rule needs specifics, **Prevents** and **Rejected**. A change to a rule replaces its text; the date, the story and the reason for each change are in [the amendment history](spine-amendments.md) and the story records it cites.

## Design Paradigm

**Clean Architecture (ports and adapters) with feature folders.** Dependencies point inward. Domain holds the rules, Application holds the use cases, and Infrastructure is the only layer that knows about Postgres, Kafka, HTTP clients or SMTP. Two hosts compose them: the **Api** (browser sessions and REST) and the **Worker** (outbox relay and SLA sweep). Locally the Api also runs the relay and the sweep in its own process (`Worker:InProcess`), and in AWS the Worker host runs them; both hosts register them through one Infrastructure registration (AD-27). Two patterns sit on top: **server-side browser sessions**, so the Api holds the tokens and the browser holds only a session cookie, and a **transactional outbox**, so every published dispute event commits with its change. Each dispute change is a **stored event** in the dispute's own append-only stream (ad-009); a handler loads a dispute by replaying its stream, and the `Disputes` row is a read model of that stream.

| Layer | Project (`backend/src/`) | Holds |
| --- | --- | --- |
| Domain | `DisputeDesk.Domain` | `Dispute` aggregate and its stored event types, action rules, disputability, money, `Result`/`Error` |
| Contracts | `DisputeDesk.Contracts` | Wire shapes only: API request and response records, dispute event records, problem slugs, audit vocabulary. References only Domain, for the enums identical on the wire |
| Application | `DisputeDesk.Application` | Feature folders (`Linking/`, `Transactions/`, `Disputes/`, `Operations/`) with handlers; `Audit/` (`AuditLog.Record`); `Abstractions/` (the three ports, `IAppDbContext`, `CurrentCustomer`) |
| Infrastructure | `DisputeDesk.Infrastructure` | `AppDbContext`, migrations, port adapters, the linking job and OTP verification (`Linking/`), the outbox relay and Kafka producer (`Outbox/`), the SLA sweep (`Sla/`) and the one registration both hosts use for them, the `Sessions` entity, seeding, telemetry |
| Api | `DisputeDesk.Api` | Endpoint groups per feature (the customer and linking route groups in `Customers/` and `Linking/`), sign-in, sign-out and the session store (`Authentication/`), the antiforgery filter and forwarded-header trust (`Security/`), authorisation policies, ProblemDetails mapping |
| Worker | `DisputeDesk.Worker` | The host that runs the relay and the sweep in AWS |

```mermaid
flowchart LR
  Api --> Application
  Api --> Infrastructure
  Worker --> Application
  Worker --> Infrastructure
  Infrastructure --> Application
  Application --> Domain
  Application --> Contracts
  Contracts --> Domain
  Stub["CoreBankingStub: no project references"]
```

Only these arrows are allowed. Api and Worker reach Contracts transitively. The architecture tests enforce the graph: `LayerTests` checks that Domain and the stub reference no other project, that Contracts references Domain and no other project, that Application never references Infrastructure, the hosts or Npgsql, that only Infrastructure implements `IAppDbContext` and the three ports, and that Api and Worker are the only hosts; `OwnershipRulesTests` checks the `IgnoreQueryFilters(["Ownership"])` and `AddDbContextPool` rules (AD-11).

## Invariants & Rules

### AD-1 - Clean Architecture with feature folders [ADOPTED]

- **Binds:** all backend stories, R25
- **Prevents:** a handler reaching Npgsql or HTTP directly; features split by technical kind; wire shapes duplicated across layers
- **Rule:** Keep the six projects and the reference graph above; a new host or project needs an AD change. Code is grouped by feature folder (`Disputes/RaiseDispute/...`), and each feature registers itself through its own `Add<Feature>`/`Map<Feature>` extension, so `Program.cs` gains one line per feature. Each Api endpoint calls one Application handler, a plain class registered in DI with no mediator library, and handlers return Contracts response records. Contracts references Domain only so that its records use the Domain enums that are identical on the wire, each defined once with no copy and no mapping; an enum whose wire members differ from Domain's stays in Contracts.
- **Rejected:** vertical slices alone (no enforceable domain boundary); N-tier with repositories; MediatR CQRS (banned by AGENTS.md); Contracts records duplicated inside Application.

### AD-2 - EF Core directly behind `IAppDbContext` [ADOPTED]

- **Binds:** every story that persists data, R29
- **Prevents:** a repository layer growing beside EF; Application binding to a provider; every story editing the same interface
- **Rule:** Handlers use EF Core through `IAppDbContext`, which exposes `Entities<TEntity>()` (forwarded to `Set<TEntity>()`) and `SaveChangesAsync`; stories add an entity and its `IEntityTypeConfiguration` in their feature folder and never edit the interface. Application may reference `Microsoft.EntityFrameworkCore` but never `Npgsql.*`. Raw SQL is allowed only where EF can't express it (`FOR UPDATE SKIP LOCKED`, advisory locks, the conditional linking updates), in one named, parameterised Infrastructure method with a justification comment. Names are the PascalCase that EF Core and Npgsql generate by default, so no naming-convention package is added.
- **Details:**
  - The member is not named `Set` because CA1716 rejects a keyword as a member name.
  - No `EFCore.NamingConventions` and no snake_case conversion. The full naming rules are in *Database naming* (`docs/conventions/backend.md`) and AD-22. Handwritten SQL double-quotes every identifier.
  - Each story adds one migration, generated after rebasing onto main. On a snapshot conflict the builder deletes and regenerates the migration and never hand-merges. CI runs `dotnet ef migrations has-pending-model-changes`.
- **Rejected:** repository and unit-of-work wrappers; Dapper for reads; a `DbSet` property per entity on the interface.

### AD-3 - Errors: one kind set, one mapping, one slug catalogue [ADOPTED, extended]

- **Binds:** every endpoint, R20, R30
- **Prevents:** stories inventing status codes or problem types; framework responses without a body; the SPA routing on message text
- **Rule:** Domain and Application return `Result` with an `Error` for expected failures and throw only for the unexpected, and `ErrorResults.ToProblem` is the only place an `Error` becomes HTTP. Every error response, from a handler or from the framework, is an RFC 9457 problem body whose `type` is a resolvable URL for one slug in the catalogue below, with `traceId` and camelCase `errors` keys. Errors never echo a value. The SPA identifies a problem by its full `type` URI.
- **Details:**
  - **Kinds.** `ErrorKind` is exactly: `NotFound` (404), `Validation` (400, with field errors), `Forbidden` (403), `ActionNotAllowed` (409, adds the caller's view of the current status), `Conflict` (409), `BusinessRule` (422), `Unavailable` (503) and `RateLimited` (429 `rate-limited`, used by the full linking queue).
  - **Validation.** Malformed input gets 400 from the built-in minimal API validation (`AddValidation`). Domain field rules (expected amount, trimmed note length) also return `Validation` and so 400, with the same shape.
  - **Body.** `type` = `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/<slug>.md`, a resolvable URL whose page gives the title, the status, when it happens and what the client should do (RFC 9457 §3.1.1 encourages resolvable type URIs). `traceId` is already global. `type`, `traceId` and the `errors` keys are set in `CustomizeProblemDetails`.
  - **Framework responses.** The cookie and JwtBearer challenge and forbid events only set 401 and 403. The rate limiter's `OnRejected` writes `rate-limited` with `Retry-After` through `ErrorKind.RateLimited` and `ErrorResults.ToProblem`, sharing the full queue's problem type. An antiforgery failure is `validation`. `UseStatusCodePages` writes every other bodiless error with the slug its status implies (401 `unauthorized`, 403 `forbidden`).
  - **Slugs.** Constants in Contracts, one class per feature, gathered by a test: `validation`, `not-found`, `action-not-allowed`, `already-disputed`, `not-disputable` (adds `reason`), `idempotency-in-progress`, `idempotency-mismatch`, `linking-required`, `already-linked`, `otp-incorrect` (adds `attemptsRemaining`), `linking-locked`, `rate-limited`, `core-banking-unavailable`, `unauthorized`, `forbidden`, `internal`. A test asserts every Domain and Application error code is in the catalogue.
  - **SPA.** RFC 9457 §3.1.1 makes the `type` URI the identifier: one function builds `<base>/<slug>.md` from a single base constant, and the SPA compares with `===`.
  - **One exception to the body shape.** The gateway's own 429 (AD-23) carries no `traceId`, because nginx has no trace context.
- **Rejected:** exceptions for control flow; FluentResults or ErrorOr; tag URIs (`tag:disputedesk,2026:problem:<slug>`: nothing resolves, and `disputedesk` is not the fully qualified authority RFC 4151 requires); `invalid-transition` (the glossary rules out "transition").

### AD-4 - Time, money and identifiers [ADOPTED]

- **Binds:** R12, R14, R22, R29, R30
- **Prevents:** clock reads tests can't control; one change stamped with several instants; floating-point money
- **Rule:** Each use case reads `TimeProvider.GetUtcNow()` once, at the start of the handler or of each sweep or relay batch, and passes that instant into the aggregate, so one change carries one instant everywhere. Application code and its SQL never read the database clock: no `now()` or `CURRENT_TIMESTAMP`, no column time default, and raw SQL takes `@now` as a parameter. Every timestamp is UTC `timestamptz`; money is `decimal` in C# and `numeric(19,2)` plus an ISO 4217 code in Postgres; dispute IDs and event IDs are UUIDv7.
- **Details:**
  - The aggregate and the AD-14 commit copy the instant to `StoredEvents.OccurredAtUtc`, `OutboxMessages.CreatedAtUtc`, the CloudEvent `time` and `occurredAt`; `AuditLog.Record` stamps an audit row with its caller's instant.
  - The one exception is the seeder (AD-30): it reads the clock once for its anchor and gives each seeded action its own back-dated instant resolved from the fixture. An AD-15 retry takes the later of the use-case instant and the stream's latest instant, without reading the clock again.
  - Raise sets `CreatedAtUtc` to that instant and `SlaDueAtUtc` to it plus 240 hours.
  - A source-rules test greps application code for `now()` and `CURRENT_TIMESTAMP`. The one handwritten statement that uses `now()` is AD-22's operator release of a `Failed` outbox row, which a person runs by hand outside the application.
  - South African time is a fixed UTC+02:00 offset, used only by the Domain's 90-day rule and the SPA's display.
  - In JSON, responses carry `{ "amount": "849.00", "currency": "ZAR" }` with exactly two decimals, and requests send a bare decimal string with ZAR implied.
  - IDs follow RFC 9562 (`Guid.CreateVersion7`, from the use-case instant) and are sent as strings.
  - Note length is counted in UTF-16 code units after trimming, on both sides.
- **Rejected:** `DateTime.UtcNow`; NodaTime; `double`; prefixed string IDs.

### AD-5 - HTTP API shape [ADOPTED]

- **Binds:** every endpoint, the SPA, R30
- **Prevents:** a generic status endpoint; offset paging; casing drift; a stale frontend contract
- **Rule:** .NET 10 minimal APIs, with every resource route under `/api/v1` except the OIDC callbacks (AD-7) and `/health/*`; each action has its own endpoint, named after it, and every `Location` header points to a real GET. Lists use keyset pagination with an opaque base64url cursor. JSON property names and enum values are camelCase (`underReview`, `startReview`) through one shared `JsonSerializerOptions`, used by both API and events. Enums go through one `JsonConverterFactory` that creates `CamelCaseNameOnlyEnumConverter<TEnum>` for every enum type: it writes the camelCase name, and reads an enum in a JSON request body only from exactly that name, refusing a number, a list and any other casing. The built-in `JsonStringEnumConverter` reads names ignoring case with no option to stop it, so one strict custom converter gives the whole wire contract one enum rule (Microsoft Learn, *How to write custom converters*, factory pattern). The built-in OpenAPI document is generated at build time to `backend/openapi.json`, committed and drift-checked (`ci.yml`).
- **Details:**
  - Pages: default 20, maximum 50, and an undecodable cursor gets 400. A page is `{ "items": [...], "nextCursor": "..." }`, with `nextCursor` null on the last page.
  - Transactions are ordered by transaction date then transaction ID, disputes by `CreatedAtUtc` then `Id`, both newest first. The disputes list has no filter in v1, and the SPA groups the pages it has loaded into open and closed.
  - C# enum members are PascalCase (`UnderReview`, `StartReview`).
  - Scalar is behind `Scalar:Enabled` (AD-26).
- **Rejected:** controllers; Swashbuckle; offset paging; a `PATCH` that sets the status; a hand-written OpenAPI file.

### AD-6 - Zitadel stands in for the bank's identity platform [ADOPTED]

- **Binds:** R1, R2, R3, R21, R37
- **Prevents:** DisputeDesk building sign-up, password or verification logic; each of two stories assuming the other enforces email verification
- **Rule:** Zitadel v4 (self-hosted, with its built-in login pages at `/ui/login`) owns logins, sign-up, email verification and password reset, and DisputeDesk never calls Zitadel management APIs at runtime. It is configured zero-click: FirstInstance and DefaultInstance environment variables, plus the idempotent `provision.sh`, which writes client settings to `zitadel-clients.json` under the configuration keys in AD-26. Unverified logins are blocked twice: Zitadel's login asks for email verification before it completes the sign-in of a login whose email is not verified, and the Api also rejects an ID token without `email_verified: true` in `OnTokenValidated`.
- **Details:**
  - The built-in login is selected with `ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_REQUIRED: "false"`, read when the instance is created.
  - `provision.sh` runs inside the `zitadel` container, from the `disputedesk-zitadel` image's Compose `post_start` hook, once `zitadel ready` succeeds; the container reports healthy only after the script has written its provisioned marker, and the api waits for that (AD-27).
  - The hosted pages are themed through the label policy, generated from the theme tokens at provisioning time.
- **Rejected:** Keycloak (the fallback, unused); Duende IdentityServer (banned); a mock OIDC server.

### AD-7 - Browser session model [ADOPTED]

- **Binds:** R2, R3, R26, the SPA
- **Prevents:** a token reaching the browser; lost sessions; session fixation; idle sign-out earlier than 15 minutes; a CSRF-able logout
- **Rule:** The Api is the confidential client and keeps every token on the server, giving the browser only a session cookie (RFC 10017 §6.1, OAuth 2.0 for Browser-Based Applications), through the built-in OpenID Connect and cookie handlers, so no token reaches the browser. The cookie holds only a session key; an `ITicketStore` keeps the ticket server-side in `Sessions` under the key's SHA-256 and enforces a 15-minute idle limit and a 12-hour absolute limit, so the server's session is the authority. Every unsafe customer and linking endpoint checks an antiforgery token (OWASP synchronizer token pattern), and logout is a form POST that also ends the Zitadel session.
- **Details:**
  - **Sign-in.** The OpenID Connect handler uses the code flow, PKCE and `client_secret_post`, matching the provisioned Zitadel app. The web app has only the authorization-code grant.
  - **Server-side session.** The `ITicketStore` lives in Api, because the Worker image lacks the ASP.NET Core shared framework that defines `ITicketStore`. `StoreAsync` always mints a new 256-bit random key, and the row stores its SHA-256 in `KeyHash`. `RetrieveAsync` checks `ExpiresAtUtc` and `AbsoluteExpiresAtUtc` against `TimeProvider`, and deletes and rejects an expired row. Otherwise it moves `ExpiresAtUtc` forward on each request that carries the cookie; the absolute limit never moves. An hourly timer in the Api deletes expired rows.
  - **Linking keeps the key.** A successful link keeps the session and its key: linked state is read from `CustomerLinks` on every request. The key is not renewed after linking; session fixation is already blocked, because the key is minted server-side at sign-in and the store rejects any key it did not mint. Renewal at a privilege change is production-path entry P32.
  - **Keys and tokens.** Data Protection keys persist to Postgres (`DataProtectionKeys`) locally. In AWS the key ring is stored with AWS's documented `Amazon.AspNetCore.DataProtection.SSM` package (`PersistKeysToAWSSystemsManager`) as SSM Parameter Store SecureStrings, encrypted at rest with KMS. The two key-ring mechanisms are kept: the human kept the 2026-10-27 decision of SSM Parameter Store with KMS for AWS (human decision 2026-11-03). Only the ID token is kept, inside the protected server-side ticket, for the end-session `id_token_hint`; access and refresh tokens are never stored.
  - **Expiry.** The ticket store renews the idle limit on every authenticated API request; the cookie does not. The cookie has no expiry date and holds only the session key; it is written at sign-in and cleared at sign-out, never renewed, so a request in flight during logout cannot set it again (`ExpireTimeSpan` is the 12-hour absolute limit). The SPA never polls. Two minutes before the idle limit it shows a warning dialog whose "Stay signed in" calls `GET /api/v1/authentication/session`, an authenticated request that renews the idle limit (WCAG 2.2 SC 2.2.1 Timing Adjustable). Each tab also ends the session in the browser 15 minutes after the last API response in that tab (a per-tab wall-clock deadline, checked when its timer fires and when the page becomes visible or gains focus); no deadline or message is shared between tabs. The session query alone refetches on window focus (TanStack Query `refetchOnWindowFocus`), so a tab brought to the front learns of a session ended elsewhere; a live answer renews the session. Every end (a 401, an `anonymous` session answer, the tab's deadline) is a `location.replace` to the session-ended screen, a document navigation that drops the old document's memory, and that screen makes no API request. The deadline is a privacy cutoff; the server's session stays the authority.
  - **Endpoints.** `GET /api/v1/authentication/login?returnUrl=` accepts only local paths (`IsLocalUrl`) and otherwise uses `/`. `GET /api/v1/authentication/session` returns `{ "state": "anonymous" | "unlinked" | "linked" }`. `GET /api/v1/antiforgery` returns the request token. Callbacks stay at `/api/signin-oidc` and `/api/signout-callback-oidc`.
  - **Logout.** `POST /api/v1/authentication/logout` is a top-level form POST carrying `__RequestVerificationToken`. It validates antiforgery only for an authenticated caller, deletes any session row the cookie names, clears the session and antiforgery cookies, and redirects to Zitadel end-session with `client_id`, `post_logout_redirect_uri` = the app origin's `/api/signout-callback-oidc` (the handler's `SignedOutCallbackPath`, which then lands the browser on `/`), and the session's `id_token_hint`, so Zitadel ends its session without a prompt (an anonymous logout sends no hint). The ID token crosses the browser only in that one redirect, already spent and short-lived; without it Zitadel asks for a click and its session outlives sign-out. The redirect is made by the OIDC handler's built-in `SignOutAsync`, which also sends `state` (OpenID Connect RP-Initiated Logout 1.0), and logout records `SignOut` (AD-14a). A failed `SignIn` audit save stops the sign-in; a failed `SignOut` audit save is logged as an error and does not stop the sign-out, because logout must end the session (OWASP Session Management Cheat Sheet).
  - **CSRF.** `AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN")` sets the header, and every unsafe customer and linking endpoint runs an `IAntiforgery` endpoint filter that reads it. The built-in antiforgery middleware validates only form-bound endpoints and never short-circuits, so Microsoft Learn documents calling `IAntiforgery` directly for these. No CORS policy is registered.
- **Rejected:** tokens in the SPA; a static `X-CSRF: 1` header alone; Duende's session library (banned); Redis or a cookie-held ticket.

### AD-8 - Two credential types, pinned per endpoint group; one check order [ADOPTED]

- **Binds:** R9, R19, R20, R21
- **Prevents:** an endpoint that takes both credentials; a sign-in redirect on an API call; probing which dispute IDs exist, stopped by 403 before 404
- **Rule:** **Customer** routes (`/api/v1/transactions`, `/api/v1/disputes...`) and **linking** routes (`/api/v1/linking/...`) accept only the cookie scheme, **operations** routes (`/api/v1/operations/disputes/...`) accept only JwtBearer, and every policy pins its scheme, so the wrong or a missing credential gets 401 or 403, never a 302. Every route checks in the framework's order (FR-24): 401, 403, 400, 404, 409, 422, then 400 for a rule that needs the loaded resource. Authentication and authorisation come first, then the built-in minimal API validation of the request, then the lookup, the status and the business rules. A cookie-scheme 401 carries no `WWW-Authenticate` header, a deliberate deviation from RFC 9110 §15.5.2 because no challenge scheme for cookies is standardised; JwtBearer 401s carry `WWW-Authenticate: Bearer` (RFC 6750 §3).
- **Details:**
  - **Schemes.** The cookie scheme's `OnRedirectToLogin` and `OnRedirectToAccessDenied` events return 401 and 403. JwtBearer validates issuer, audience (the Zitadel project ID), lifetime and signature, with `MapInboundClaims = false` and `RoleClaimType = "role"`.
  - **Endpoints with a body** (raise, withdraw, resolve) keep the built-in validation. A request the framework cannot bind (a route ID that is not a GUID, no body, malformed JSON, a wrong JSON type, an enum value that is a number, a list, an unknown name or any casing but the exact camelCase name) gets the framework's 400 `validation`, and a non-JSON body on a cookie route gets 415 `validation`; there is no custom body reading (Microsoft Learn, *Parameter binding in minimal APIs*). A bound body is checked by its data annotations and `IValidatableObject` with the same 400 shape, before the handler and so before the lookup (Microsoft Learn, *Validation support in minimal APIs*). The built-in validation still checks the bound body when the route ID is not a GUID, so a non-GUID route ID with a bad body answers 400 naming the body field. On a cookie route the body validation runs before the group's `AntiforgeryFilter`; both answer 400 `validation`, and no handler runs.
  - **Raise.** The `Idempotency-Key` filter's own answers before the lookup (AD-16) belong to the key, not to this order. The comparison of the expected amount with the loaded transaction's amount is the trailing 400, because the amount is known only once the transaction is loaded.
  - **Resolve with a non-JSON body.** Resolve, the one operations endpoint with a JSON body, declares only JSON, so routing hands a `text/plain` body to the framework's 415 endpoint, which has no bearer policy and is answered by the cookie fallback policy: 401 with no `WWW-Authenticate: Bearer`, for a missing, refused or valid token alike, writing no dispute, stored event or outbox row. A customer cookie on the same request satisfies the fallback policy and gets the 415 `validation`, writing no dispute, stored event, outbox or audit row. A refused token there writes no `Denied` entry (AD-14a). Human decision, 2026-11-04.
- **Rejected:** a fallback policy that also accepts JwtBearer (any validly signed project token would then satisfy deny-by-default everywhere); one policy scheme that picks cookie or bearer per request; antiforgery on the operations group; 404 before 400, which needed `.DisableValidation()`, a body check after the lookup and a second order for raise, when 403 before 404 alone stops ID probing.

### AD-9 - Operations roles and actor mapping [ADOPTED]

- **Binds:** R20, R21, FR-30
- **Prevents:** roles that encode who the caller is; tokens without roles; actor identity used to decide
- **Rule:** There is one Zitadel project role per action (`dispute.startReview`, `dispute.escalate`, `dispute.resolve`) plus `dispute.read` (FR-30), each with one authorisation policy named after it; role keys are camelCase after the `dispute.` prefix. `OnTokenValidated` flattens only this project's role claim, `urn:zitadel:iam:org:project:<projectId>:roles`, into one `role` claim per key. `Operations:AllowedClients` is a plain allowlist, an array of service accounts' `sub` values compared by exact (ordinal) equality: every allowed caller is actor type `operations`, a `sub` not in the list gets 403, and an empty or missing list refuses every caller. Actor type and identity are recorded, never passed into a decision.
- **Details:**
  - Operations clients request the scope `openid urn:zitadel:iam:org:project:id:<projectId>:aud urn:zitadel:iam:org:projects:roles`.
  - The generic `urn:zitadel:iam:org:project:roles` claim is not read, because it is not bound to this project and could carry another project's roles (ASVS 5.0 V10.3.2).
  - The SLA sweep acts as `automation` with identity `worker:sla-sweep`, in-process and without a token. Withdraw needs no role.
- **Rejected:** coarse roles; OAuth scopes as permissions; Zitadel Actions.

### AD-10 - Authorisation states and customer resolution [ADOPTED]

- **Binds:** R9, R17
- **Prevents:** the customer taken from a request field or the token subject; a single-implementation interface for the current customer
- **Rule:** Two policies decide the authorisation state, `Unlinked` on linking routes and `Linked` on customer routes, and a deny-by-default fallback policy requires an authenticated user on every endpoint without its own policy; the authentication and health endpoints are marked `AllowAnonymous()` (Microsoft Learn, *Policy-based authorization*: require global user authentication). The `Linked` policy's authorization handler fills the concrete scoped class `CurrentCustomer` on every customer request by looking up the login (`sub` from the session) in `CustomerLinks`. Handlers and `AppDbContext` read only that class, never `HttpContext.User`, and the Worker never sets it. Because authorisation runs as policies, 401 and 403 always come before binding and validation (400).
- **Details:**
  - `CurrentCustomer` is in `Application/Abstractions`. Each policy names the cookie scheme (AD-8), and the documented `IAuthorizationMiddlewareResultHandler` writes the 403 problem.
  - An unlinked login on a customer route gets 403 `linking-required`; a linked login on a linking route gets 403 `already-linked`.
- **Rejected:** a customer-ID claim stamped at sign-in (stale after linking); an `ICurrentCustomer` interface (AGENTS.md); Zitadel metadata as the link store; an endpoint filter (it runs after built-in validation, so 400 could beat 403); claims transformation (runs on every authenticate call, more than once per request).

### AD-11 - Ownership by global query filter, three named bypasses [ADOPTED]

- **Binds:** R17, every query over disputes, their stored events and their timelines
- **Prevents:** a forgotten `Where` leaking another customer's dispute, stored events or timeline; a filter that bakes in the first customer's ID; a customer path reusing an operations loader
- **Rule:** `AppDbContext` adds the EF Core 10 named query filter `"Ownership"` to `Dispute`, `d => d.CustomerId == CurrentCustomerId` over an instance member set per context from `CurrentCustomer`, and `StoredEvent` carries the same filter through its dispute; with no current customer it matches nothing. `IgnoreQueryFilters(["Ownership"])` appears only in the `Operations/` feature folder, the SLA sweep (`Infrastructure/Sla/`) and the seeder (`Infrastructure/Seeding/Seeder.cs`, AD-30), and an architecture test fails on any other use. Another customer's dispute and an unknown ID return identical 404 bodies apart from `traceId`.
- **Details:**
  - The filter never uses a captured local or a static. `AddDbContextPool` is forbidden.
  - `StoredEvent` (ad-009) is reachable only through its dispute's stream (EF Core *Global query filters*: filter the required dependent through its navigation). Loading by replay and the timeline both read the stored events (AD-13), so no query reads `StoredEvents` without that filter outside the three bypasses.
  - The architecture test also fails on `Disputes/` or `Transactions/` referencing `Operations/`, `Sla/` or `Seeding/`.
  - Transaction ownership is checked by `ICoreBankingClient`, which only answers for the given customer (AD-21).
- **Rejected:** a check in each handler; Postgres row-level security.

### AD-12 - Linking: uniform request, checks and send off the request path

- **Binds:** R4 to R8, R10
- **Prevents:** response, timing or tries-left revealing a match; an OTP before the SIM-swap check; the ID number persisted; a stale job sending a code; the cap overrun
- **Rule:** Every well-formed `POST /api/v1/linking/start` returns 202 with one fixed body and queues an in-memory job, so the response and its timing never reveal a match. The job matches the details in core banking and checks the link, then, under a per-customer advisory lock, checks the three-per-hour send cap and the SIM swap (failing closed) before it claims the challenge and sends the OTP, so no OTP is sent before the SIM-swap check [ADOPTED]. `POST /api/v1/linking/verify` counts every attempt atomically before it compares the code; a correct code within three attempts links, and three wrong codes lock the challenge. The built-in rate limiter limits linking per client IP and per login.
- **Details:**
  - **Start.** It validates format (400 names the field and never echoes it), sets `SupersededAtUtc` on the login's earlier challenge, inserts a blank challenge row and calls `TryWrite` on a bounded in-memory `Channel` (capacity 100). The job holds `challengeId`, `loginSub`, `clientIp`, the parent `ActivityContext`, and the ID and account numbers in memory only. A full channel returns 429 `rate-limited` (`ErrorKind.RateLimited`, no `Retry-After`) to everyone and is audited.
  - **Background job.** A hosted service runs up to 4 jobs at once, each within a 10-second budget. In order: core-banking match, link check, then one explicit EF Core transaction whose first statement is the two-key `pg_advisory_xact_lock(<namespace>, <hash of the customer ID>)` (the two-key space never meets the relay's one-key lock), holding the 3-per-hour cap (challenges for the customer with `OtpSentAtUtc` in the last hour), then `ISimSwapCheck`, which fails closed, then the claim; it commits before the send.
  - **Claim and send.** The claim is one conditional fill that sets `CustomerId`, `OtpHmac` (HMAC-SHA256 with `Linking:OtpHmacKey`), `OtpSentAtUtc` and `ExpiresAtUtc` = that instant plus 5 minutes, only where the row isn't superseded, consumed or already sent. `ISmsSender` runs only if exactly one row was updated. If `ISmsSender` fails, a second conditional update on that row clears `OtpHmac` and `ExpiresAtUtc` and keeps `OtpSentAtUtc`, so a failed send starts no lifetime but still counts toward the hourly cap: every claim counts, delivered or not, so at most three SMS reach a customer per hour. The claim and the send are two steps, not one atomic operation (EF Core, rows affected). A send already in flight when a newer start supersedes the challenge is harmless: verification accepts only the current challenge's OTP. A crash between claim and send loses that OTP, and the person restarts linking. Every stop is audited with the job's login, IP and a span linked to the request's trace; an unexpected exception before the claim rolls the transaction back and records `LinkingStart` `Failed`.
  - **Verify.** One atomic conditional update on the login's current challenge (`SupersededAtUtc` and `ConsumedAtUtc` null): `SET "Attempts" = "Attempts" + 1 ... WHERE "Attempts" < 3 RETURNING ...`, parameterised raw SQL through `Database.SqlQuery<T>`. This counts every attempt the same way, whether the row was blank, released, expired or active. The handler then always computes the HMAC and compares with `FixedTimeEquals`, against a fixed dummy when the stored value is null. A counted attempt that does not link returns 422 `otp-incorrect` (`BusinessRule`) with `attemptsRemaining` = 3 minus `Attempts` while `Attempts` is under 3, and 409 `linking-locked` (`Conflict`) when it reaches 3 (FR-7 governs). An uncounted attempt on a locked challenge returns 409. A login with no challenge row gets 422 `otp-incorrect` with `attemptsRemaining` 2, exactly like a first wrong code, and is not counted: it holds no OTP and can never succeed (FR-6; OWASP Authentication Cheat Sheet, generic responses).
  - **Link.** On success, a conditional consume of the challenge, the `CustomerLinks` insert and the `Linked` audit entry commit in one EF Core transaction, and the session keeps its key: the next request reads the link and sees the linked customer (AD-7, AD-10). A unique-constraint loss rolls back the consume and answers as a counted wrong attempt.
  - **Rate limits.** The built-in limiter chains a per-client-IP limiter (AD-23) and then a per-login limiter with `PartitionedRateLimiter.CreateChained` as the `GlobalLimiter`, returning `RateLimitPartition.GetNoLimiter` outside the linking routes, since the two partition on different keys (Microsoft Learn: *Rate limiting middleware*, *Create chained limiters*). Both use sliding windows held in memory, whose permits come back segment by segment (15 one-minute segments by default, `Linking:RateLimits`), with `QueueLimit` 0. Chained limiters run in order, and a time-based limiter does not return a permit it already granted when a later limiter in the chain rejects the request (Microsoft Learn, *Rate limiting middleware*, *Chain limiters in a named policy*; the global `PartitionedRateLimiter.CreateChained` section documents only that the limiters run in sequence, and the permit that is not returned is a property of the time-based limiter types), so a request rejected by the login limiter has already taken IP permits. How many is not documented, so the spine and FR-8 do not state it; a test pins today's count as a regression check, not a requirement. `UseRateLimiter` runs after authentication and authorization, so 401 and 403 take no permit. The limiter sets `RejectionStatusCode = 429`, and its `OnRejected` writes the `rate-limited` ProblemDetails with `Retry-After` = the window length in seconds, since a sliding-window lease carries no `RetryAfter` metadata (AD-3). Only the first rejection per partition per window is audited, recorded in a size-limited `IMemoryCache` entry that expires one window later; every rejection is counted by the built-in `aspnetcore.rate_limiting.requests` metric.
  - **Cap window.** `OtpSentAtUtc` is the claiming job's start instant (AD-4's one reading), at most one `Linking:Budget` before the claim, so the cap's hour can be short by up to one budget; accepted (ASVS 5.0 V2.4.1, rated low). `Linking:Budget` is 1 to 30 seconds, never longer than Npgsql's default 30-second command timeout, so the budget always ends a lock wait by its own path.
  - **Logging.** Request-body logging is off on linking routes.
  - **Restart.** [ASSUMPTION] An Api restart drops queued jobs, so the person sees wrong codes and starts again.
- **Rejected:** the lookup inside the request (timing); handing jobs to the relay's host through a table (it would persist data derived from the ID number); sending before the SIM-swap check.

### AD-13 - The `Dispute` aggregate owns every rule; customers see a mapped view

- **Binds:** R12 to R14, R16, R18 to R20, R22, R29
- **Prevents:** the FR-24 table or disputability copied and drifting; `escalated`, `automation`, an actor identity or an internal stored event reaching a customer; 409 and 422 decided differently by two stories; one story loading by replay and another from the row
- **Rule:** `Dispute.Raise(...)`, `StartReview`, `Escalate`, `Resolve(outcome, note)`, `Withdraw(note)` are the only ways to create or change a dispute; each takes an `Actor` and the use-case instant, returns `Result`, and appends exactly one pending stored event carrying the next `Version` (ad-009). A handler that changes a dispute loads it by replaying its stream and decides from that state; the `Disputes` row is a read model of the stream, and no handler decides from it. Domain owns the allowed-actions table, disputability, the amount rules and note trimming. Customers see a view mapped once in Application, and their timeline passes the stored events through a deny-by-default allowlist of the five lifecycle types.
- **Details:**
  - **Stored events.** The methods append `DisputeRaised`, `ReviewStarted`, `DisputeEscalated`, `DisputeResolved` or `DisputeWithdrawn`. `DisputeRaised` holds everything needed to rebuild the dispute (customer ID, `AccountRef`, `TransactionId`, the transaction snapshot, reason, note and amounts). Status setters are private. The operations escalate and the SLA sweep both call the same `Escalate`.
  - **Loading by replay.** The stored events are the dispute's record. Withdraw, the operations actions and the SLA sweep replay the stream in `Version` order through `Dispute.Replay(storedEvents)` in Domain (Microsoft Learn, *Event Sourcing pattern*: the current state is rebuilt by replaying the events). Each method decides, creates its stored event, then changes state only by applying that event through one `Apply` case per type, and `Replay` uses the same `Apply` cases, so loading and deciding share one fold. A stream with no row visible through the ownership filter (AD-11) is 404.
  - **Read model.** The `Disputes` row is the stream at its latest `Version`, written by the AD-14 commit in the same transaction as the stored event and the outbox row (Microsoft Learn, *Event Sourcing pattern*: materialized views). Lists, the dispute fields of the detail reads, the transaction rows, the unique transaction index and the SLA sweep's claim read it. A test replays every stream, over every type, and asserts it reproduces its `Disputes` row and its timeline.
  - **Timeline.** The timeline is built from the dispute's stored events (glossary: Timeline); the aggregate holds no timeline of its own. The customer view's allowlist in Application admits only the five lifecycle types and hides every other type, including any type added later; tests cover every type and a test-only unknown type. One mapping serves the raise 201 body, the detail read and the history list. Operations (FR-30) see every type. In C#, each stored event type is a sealed record deriving from one abstract `StoredEvent` record in Domain, and `EventType` is the record's type name; the allowlist is a set of the five admitted record types checked with a type test, so a test-only record declared in a test project maps to nothing (OWASP Authorization Cheat Sheet: deny by default). A row whose `EventType` the reader does not know is skipped for customers, never an error.
  - **Disputability and raise order.** Disputability returns a reason: `AlreadyDisputed`, `OutsideWindow` or `NotEligible` (on the wire `alreadyDisputed`, `outsideWindow`, `notEligible`). Raise checks run in this order: transaction found (404), earlier dispute (409 `already-disputed`), then window and eligibility (422 `not-disputable` with `reason`). The unique index stays as the race backstop.
  - **Customer wire view.** `customerStatus` is one of `submitted`, `underReview`, `underReviewPrioritised`, `resolved`, `withdrawn`, plus `outcome`. The timeline `actor` is `you` or `theBank`. No actor type, actor identity, SLA or due date appears. A customer 409 carries `customerStatus`. Each transaction row carries `disputable`, `notDisputableReason`, and the dispute's `disputeId` and `customerStatus` when one exists. Operations responses use the internal `status` and `actorType`.
- **Rejected:** rules in handlers; a state-machine library; one response shape for both audiences; deciding from the `Disputes` row (a copy of the record, not the record; ad-009); a customer timeline that shows any type not on the allowlist.

### AD-14 - One commit path for dispute changes

- **Binds:** R10, R23, R28, R41
- **Prevents:** a change committed without its stored event or its outbox row; a private fact published by accident; two outbox payload shapes; two records of one successful change; a failed save silently dropping a pending event
- **Rule:** One `AppDbContext.SaveChangesAsync` override in Infrastructure turns each pending stored event into one `StoredEvents` row and one `OutboxMessages` row, and writes the `Disputes` read-model row from the aggregate's new state, all in one transaction (EF Core *Transactions*: one `SaveChanges` is one transaction). Every stored event is published; one exhaustive mapping decides which integration event each type becomes, and a stored event is never published as is. `StoredEvents` and `AuditLogEntries` are insert only, and handlers never write stored events, outbox rows or dispute audit rows by hand.
- **Details:**
  - **Identity.** The stored event ID is a UUIDv7 from the change's instant (AD-4) and is also the outbox row ID and the CloudEvent `id` (ad-009). No `AuditLogEntries` row is written for a successful dispute change: the stream is that record (ad-009).
  - **One publish mapping.** One exhaustive `switch` in Infrastructure maps every stored event type to its integration event, with no case that publishes nothing: `DisputeRaised` to `raised.v1`; `ReviewStarted`, `DisputeEscalated`, `DisputeResolved` and `DisputeWithdrawn` to `status-changed.v1`. An unmapped type throws and stops the commit, and a test enumerates every type (Microsoft Learn, *Domain events versus integration events*).
  - **Payload.** The mapping serialises the complete structured CloudEvent once, with the shared JSON options, into `OutboxMessages.Payload` (`text`, so the bytes are exactly what was written). `EventType`, `DisputeId` and `DisputeVersion` are copied out for querying. `TraceParent` and `TraceState` come from `Activity.Current`.
  - **Insert-only guard.** The override rejects any `AuditLogEntry` or `StoredEvent` that is Modified or Deleted, before saving (OWASP Logging Cheat Sheet: protect the integrity of event data). The guard sees only tracked entities, so a source-rules test, like AD-4's `now()` grep, fails on `ExecuteUpdate`, `ExecuteDelete` or raw SQL against either table outside migrations.
  - **Failed save.** The override takes the pending events before saving, so on any exception from the save it clears the change tracker (`ChangeTracker.Clear()`, EF Core *Change tracking*) and rethrows. Nothing can later save the dispute without its events; the handler reloads (AD-15).
  - **Audit rows.** Linking handlers, handlers recording refused or forbidden dispute actions, operations reads, and sign-in and sign-out (AD-14a) add audit rows through one Application method, `AuditLog.Record(...)`.
- **Rejected:** per-handler writes; MediatR notifications; a separate interceptor; `jsonb` payload (it re-orders keys); an audit row beside each stored event; one outbox row per stored event (ad-009).

### AD-14a - Audit and timeline vocabulary

- **Binds:** R10, R21, R41, every story writing `AuditLogEntries` or `StoredEvents`
- **Prevents:** five stories each inventing action names, stored event types, actor IDs and result values; a successful dispute change recorded twice
- **Rule:** Actions, results, actor values and stored event types are closed sets owned once: Domain owns `ActorType`, `DisputeAction`, `DisputeStatus` and the stored event records, and every enum identical on the wire (those three, `Outcome`, `Reason`, `TransactionKind`, `NotDisputableReason`, `TransactionDirection`, `TransactionState`), which Contracts uses directly (AD-1); Contracts owns `AuditAction`, `AuditResult`, `StoredEventType`, `SessionState` and the customer-view enums `CustomerStatus` and `TimelineActor`, whose members differ from Domain's, and Application maps each customer-view enum from Domain once. Values are stored as their PascalCase C# names (AD-22), and stored events and audit rows use the same actor values. The audit log records linking, refused or forbidden actions, operations reads, and sign-in and sign-out, never a successful dispute change, which its stored event already records (ad-009).
- **Details:**
  - **Actor IDs.** `ActorId` is the `LoginSub` for actor type `Customer`, the token `sub` for `Operations`, and for `Automation` either `worker:sla-sweep` (the SLA sweep) or `worker:seeder` (the seeder's start review, escalate and resolve, AD-30). `ActorType` has one meaning, the route's credential type: a `Denied` entry on an operations route records actor type `Operations` and the bearer token's `sub` as actor ID, whether or not that `sub` is in `Operations:AllowedClients` (AD-9).
  - **Stored events.** The type is a closed set: `DisputeRaised`, `ReviewStarted`, `DisputeEscalated`, `DisputeResolved`, `DisputeWithdrawn` (ad-009). Each carries `OccurredAtUtc`, `ActorType`, `ActorId` and `TraceId`, plus its own data: from- and to-status, the note meant for the customer, `outcome` on `DisputeResolved`, and the transaction snapshot on `DisputeRaised`. `DisputeRaised` has no from-status. Changes within a type are additive; a new shape is a new type name.
  - **What is audited.** Customer reads, refused ones included (a 404 on another customer's dispute), write no audit row; the identical 404 and the trace ID in the logs are their record. Refused or forbidden dispute actions are audited, from customers and operations alike (ad-009; FR-25). On an operations route (403, 404 or 409 after authentication), the `IAuthorizationMiddlewareResultHandler` (AD-10) records `Denied` through `AuditLog.Record` for an authenticated caller, and the read and action handlers record `Rejected` for 404 and 409. The customer raise and withdraw handlers record `Rejected` for their 404, 409 and 422 refusals through `AuditLog.Record`.
  - **Unaudited case.** A `text/plain` resolve is answered by the cookie fallback policy before any operations policy runs (AD-8), so a refused bearer token there writes no `Denied` entry. The request reaches no handler and changes nothing; its record is the 401 and the trace ID in the logs. Human decision, 2026-11-04.
  - **Columns.** The customer ID goes only in `AuditLogEntries.CustomerId`. `AuditLogEntries.Result` is `Succeeded`, `Rejected` (rule or status), `Denied` (authorisation) or `Failed` (dependency). A dispute's Outcome never appears in an audit entry, and never in `Result`: a refused resolve records its actor, action and result, as FR-25 asks, with no `Details` for it.
  - **Actions.** `AuditLogEntries.Action` is a closed set: `Raise`, `StartReview`, `Escalate`, `Resolve`, `Withdraw` (each only with `Rejected` or `Denied`), `DisputeRead` (an operations read, FR-30), `SignIn`, `SignOut` (R41), `LinkingStart`, `LinkingNoMatch`, `LinkingAlreadyLinked`, `LinkingSimSwapFailed`, `LinkingSendCapped`, `LinkingOtpSent`, `LinkingOtpIncorrect`, `LinkingOtpExpired`, `LinkingLocked`, `LinkingRateLimited`, `Linked`.
- **Rejected:** free-text action names; a dispute outcome stored in the audit result; an audit row with `Succeeded` for a dispute change beside its stored event.

### AD-15 - Concurrency and uniqueness [ADOPTED]

- **Binds:** R7, R13, R20, R22, R29
- **Prevents:** lost updates; two stored events with one version; two disputes on one transaction; two logins on one customer; a clash answered 409 when the action is still allowed; two concurrency checks for one conflict
- **Rule:** `UQ_StoredEvents_DisputeId_Version` is the one concurrency check: two handlers that replayed the same stream both append the next version, and the database rejects the second, which rolls back its whole `SaveChanges` transaction, `Disputes` row and outbox row included. On the first clash a withdraw or operations handler replays the stream again and decides once more with the same request; a second clash, or a reloaded status that forbids the action, returns 409 `action-not-allowed`. There is exactly one reload, never a loop. Every uniqueness rule is a named unique index, mapped to its `Error` in one place in Infrastructure.
- **Details:**
  - **Why one check.** Microsoft Learn, *Event Sourcing pattern*: optimistic concurrency on append; EF Core *Handling concurrency conflicts*: optimistic concurrency fails the save, and the application retries the operation on the new data. `Disputes` has no concurrency token and no `xmin` mapping: every write to it commits with a stored event in the same transaction (AD-14), so the unique index already catches every concurrent change, and a row token would detect the same conflict a second way. The aggregate's `Version` integer is incremented by each stored event, and published events carry it as `disputeVersion`.
  - **Version clash** (ad-009): a violation of `UQ_StoredEvents_DisputeId_Version`, which `UniqueViolations` maps to one `Conflict` error. A `DbUpdateConcurrencyException` is never a clash: in v1 it comes only from the `IdempotencyKeys` lease (AD-16), which is never retried; raise has no version clash.
  - **The retry.** The handler clears the change tracker before it replays. On the retry the instant is the later of the use-case instant (AD-4) and the reloaded dispute's latest `OccurredAtUtc`, so time never runs backwards within a stream. If the reloaded status forbids the action, the 409 carries that reloaded status as the caller is allowed to see it. If a second clash follows, the handler reads the `Disputes` row once and returns the 409 with the status as last read, as the caller is allowed to see it, and makes no further decision attempt; a change committed after that read is not reflected. Both use the existing slug; no new one. The sweep re-evaluates.
  - **Unique indexes.** `UQ_StoredEvents_DisputeId_Version`; `UQ_Disputes_AccountRef_TransactionId` → 409 `already-disputed`; `UQ_CustomerLinks_LoginSub` and `UQ_CustomerLinks_CustomerId`; and the partial `UQ_LinkingChallenges_LoginSub` (filtered on `"SupersededAtUtc" IS NULL AND "ConsumedAtUtc" IS NULL`) allowing one unsuperseded, unconsumed challenge per login. Each sets `HasDatabaseName` explicitly, since EF Core would name them `IX_`.
- **Rejected:** pessimistic locks for customer and operations actions; check-then-insert without the index; answering a clash with 409 without reloading; an `xmin` concurrency token on `Disputes` beside the stream index (a second check of the same conflict); retrying until success.

### AD-16 - Idempotent raise [ADOPTED]

- **Binds:** R15
- **Prevents:** a double submit creating two disputes; a takeover that overwrites the winner's stored response
- **Rule:** Raise follows the IETF Idempotency-Key header draft (draft-ietf-httpapi-idempotency-key-header) through one endpoint filter on `POST /api/v1/disputes` only: 400 for a missing or malformed key, 409 while the first request is in progress, 422 for a reused key with a different body, and a completed key replays the stored status, body and `Location`. Keys are stored per customer for 24 hours. The key row completes in the raise's own `SaveChangesAsync` under a lease, so a takeover never overwrites the winner's stored response.
- **Details:**
  - **Key format.** The key is an RFC 9651 Structured Field String, as the draft defines it, so the value is quoted (`Idempotency-Key: "<uuid>"`). The filter parses that form and rejects any other with 400 `validation`. The key's string value must be 1 to 255 printable characters, or the request gets 400 `validation` before any insert.
  - **Insert.** In its own short transaction the filter inserts an `IdempotencyKeys` row (`CustomerId`, `Key`, unique as `UQ_IdempotencyKeys_CustomerId_Key`) with `InProgress`, a fresh `LeaseId`, and the SHA-256 of the canonical validated request. An expired row is replaced in the same statement.
  - **Duplicates.** A different hash gets 422 `idempotency-mismatch`. An in-progress row older than 60 seconds with the same hash is taken over with a new `LeaseId`; a younger one gets 409 `idempotency-in-progress`. The raise endpoint's request timeout is 30 seconds, so a live request is never taken over.
  - **Writes conditional on the lease.** The filter attaches the tracked key row to the scoped context, and the handler's one `SaveChangesAsync` marks it completed with the 201 response. `LeaseId` is an EF Core concurrency token, so zero rows rolls back the dispute. Every 4xx the handler returns is stored afterwards with a lease-conditional `ExecuteUpdateAsync`. Any 5xx, including a request-timeout 504, deletes the key with a lease-conditional `ExecuteDeleteAsync` that matches only `InProgress`, with `CancellationToken.None`. After a lost unique-index save the context is cleared with `ChangeTracker.Clear()`.
  - **Stored body.** `ResponseBody` is `text`, not `jsonb`, so a replay is byte for byte the original (jsonb reorders keys, as AD-14 notes for the outbox payload).
- **Rejected:** an in-memory cache; keys on operations actions.

### AD-17 - Transactional outbox with a single relay [ADOPTED]

- **Binds:** R23, R26, R28
- **Prevents:** an event for a rolled-back change; events out of order per dispute; two relays after a lost lock; Kafka downtime marking rows failed; a consumer seeing a different `id` from the stored event
- **Rule:** One relay, leader by a session-level `pg_try_advisory_lock`, publishes the outbox rows AD-14 writes to `disputedesk.disputes` at least once, keyed by dispute ID, and never re-mints a row's ID, which is its stored event's ID and the CloudEvent `id` (ad-009). Order is defined only within one dispute, by `DisputeVersion`: per dispute the relay sends the lowest-version row that isn't `Sent` and awaits its delivery report before the dispute's next row. Consumers apply an event only if its `disputeVersion` is newer than the last one they applied for that dispute, and treat a repeated CloudEvent `id` as a duplicate (Enterprise Integration Patterns, *Idempotent Receiver*).
- **Details:**
  - **Rows.** Tests check that each outbox row's ID is its stored event's ID, and that the published CloudEvent `id` is the outbox row's ID.
  - **Leadership.** The relay is a hosted service in Infrastructure, run by the Api locally and by the Worker in AWS (AD-27). It holds `pg_try_advisory_lock` on one fixed key and does every outbox read and update on that same connection. A session-level advisory lock lasts as long as its connection, so before each batch the relay checks that connection with a cheap query. On error it disposes the connection and stops producing. A test terminates the lock connection and checks for handover.
  - **Order.** There is no global position and no order across disputes (ad-009; cross-dispute options are deferred to `docs/production-path.md`). A `Failed` or not-yet-due head row means that dispute is skipped. Every stored event has an outbox row, so a dispute's outbox rows have no version gaps.
  - **Delivery guarantee.** The advisory lock cannot fence a Kafka send already in flight when a leader loses its connection, so across a leader handover an earlier event of a dispute can arrive late or twice; consumer deduplication handles it. Fencing with Kafka transactions (KIP-98, a fixed `transactional.id`) is the stronger option, in `docs/production-path.md`.
  - **Seeded rows.** Seeded disputes have outbox rows like any other (AD-30), so relay tests set up their own rows and never assume an empty outbox.
  - **Producer.** `Confluent.Kafka` with `EnableIdempotence = true` and `Acks.All`. The value is `Payload` byte for byte, and the headers are `traceparent` and `tracestate` (W3C Trace Context) and `content-type: application/cloudevents+json` (CloudEvents Kafka protocol binding, structured mode).
  - **Failures.** A broker-unavailable or timeout error pauses the loop with backoff and doesn't count as an attempt. A permanent rejection of the record (a named list of Kafka error codes, such as a message over `message.max.bytes`) increments `Attempts`, sets `NextAttemptAtUtc` with exponential backoff and jitter, and records the error code alone in `LastError`; every other non-fatal error is transient and counts no attempt, because librdkafka already retries it until `message.timeout.ms`. The 5th such error sets `Failed`, increments the failed-rows metric and logs the event ID. Later events for that dispute wait for an operator. There is no dead-letter topic.
- **Rejected:** `SKIP LOCKED` on the relay; a dead-letter topic; CDC; publishing inside the request; a `GlobalPosition` sequence as a publish checkpoint (PostgreSQL sequences are not rolled back and commit out of allocation order).

### AD-18 - SLA sweep [ADOPTED]

- **Binds:** R22
- **Prevents:** double escalation; a sweep with its own rules; untraced sweep events
- **Rule:** A hosted service in Infrastructure, run in the same host as the relay, runs every 30 seconds [ASSUMPTION: within "at least once a minute"], claims due disputes in batches of up to 50 with `FOR UPDATE SKIP LOCKED`, loads each by replay (AD-13), calls `Dispute.Escalate` with `Automation` / `worker:sla-sweep` and commits through AD-14, repeating until a batch comes back short, which catches up after downtime. The sweep only escalates: it has one claim, and records nothing for a dispute before its SLA is due.
- **Details:**
  - **Trace.** For each batch the sweep starts an Activity `disputedesk.sla_sweep.batch`.
  - **Escalation claim.** One named raw-SQL method: `SELECT ... FROM "Disputes" WHERE "Status" IN ('Submitted','UnderReview') AND "SlaDueAtUtc" <= @now ORDER BY "SlaDueAtUtc" LIMIT 50 FOR UPDATE SKIP LOCKED`, through the partial index `IX_Disputes_SlaDueAtUtc` (filtered on those two statuses).
  - **Transactions.** Each batch is one transaction holding its row locks from claim to commit, with a savepoint per dispute. The claim returns IDs, and only `Infrastructure/Sla/` loads streams through the named bypass (AD-11).
- **Rejected:** a cron container; Quartz or Hangfire; escalating without the aggregate; a stored warning before the SLA is due (no journey, metric or consumer reads it).

### AD-19 - Dispute event contract [ADOPTED]

- **Binds:** R23, R31, R35
- **Prevents:** personal data in events; two event shapes; consumers unable to dedupe; a stored event published as is
- **Rule:** Two types, `disputedesk.dispute.raised.v1` and `disputedesk.dispute.status-changed.v1`, as CloudEvents 1.0 structured JSON with no SDK, built from stored events by the one AD-14 mapping; stored events are private domain events and are never published (ad-009; Microsoft Learn, *Domain events versus integration events*). Events carry no customer ID, account or card number, amount, note or actor identity; NFR-SEC-5 and R31 apply to these published events. Changes within v1 are additive, and a breaking change is a new type published side by side. The committed JSON Schemas are under `contracts/events/`, and consumers dedupe on `source` + `id`.
- **Details:**
  - **Attributes.**
    - `id`: the outbox row ID, which is also the stored event ID (CloudEvents 1.0 §`id`: unique within the source)
    - `source`: `/disputedesk/api`
    - `subject`: the dispute ID
    - `time`
    - `datacontenttype`: `application/json`
    - `dataschema`: `https://github.com/njabulozmnisi/disputedesk/blob/main/contracts/events/<type>.schema.json`, the committed schema's resolvable URL; the contract test checks the file exists
  - **Data.** `status-changed.v1` data is `disputeId`, `fromStatus`, `toStatus`, `action`, `actorType`, `outcome` (resolve only), `occurredAt` and `disputeVersion`, published for `ReviewStarted`, `DisputeEscalated`, `DisputeResolved` and `DisputeWithdrawn`. `raised.v1` data is in AD-19a.
  - **`disputeVersion`** is the stored event's `Version`. Every stored event is published, so the versions written for a dispute have no gaps; delivery stays at least once, so consumers order by it and deduplicate (AD-17).
  - **Code and tests.** The records live in Contracts. The contract test validates samples and checks that the committed v1 samples still deserialise. There is no schema registry.
  - **Seeded disputes** have one stored event and one outbox row per seeded action, and are published at least once like any other event; consumers deduplicate by CloudEvent `id` (AD-17, AD-30).
  - **Naming.** The `disputedesk.` type prefix deviates from the CloudEvents reverse-DNS SHOULD, because the project owns no domain. Where the event-versioning research differs, this spine governs: actor types are `customer`, `operations` and `automation` (the glossary's, not the research example's `Agent` and `System`), and enum values are camelCase (the research example, written before the casing decision, used PascalCase).
- **Rejected:** a schema registry; Avro or Protobuf; a custom envelope; a topic per type; a relative `dataschema`; publishing stored events directly (ad-009).

### AD-19a - `raised.v1` data fields [Accepted 2026-10-26]

- **Binds:** R23, FR-27
- **Prevents:** the event and the operations read becoming two sources of dispute detail
- **Rule:** `raised.v1` data is exactly `disputeId`, `reason`, `status` (`submitted`), `occurredAt` and `disputeVersion` (1). It carries no customer ID, amount or note. Consumers call the bearer-only `GET /api/v1/operations/disputes/<disputeId>` (`dispute.read`).
- **Rejected:** a fat event with the snapshot; a `channel` field.

### AD-20 - One trace from browser to the relay; named signals [ADOPTED]

- **Binds:** R36, the SM-1 to SM-4 signals
- **Prevents:** a broken trace at the outbox; a prerelease Kafka package; metrics that can't produce the success metrics
- **Rule:** One W3C trace runs from the browser through the Api and the outbox row to the relay: the SPA's OpenTelemetry web SDK propagates `traceparent` on same-origin `/api` fetches, AD-14 stores the trace context, and the relay starts a producer span whose parent is the stored context, injecting the headers by hand through `Propagators.DefaultTextMapPropagator`. The ActivitySource and the Meter are both named `DisputeDesk`, with the instruments below, and no label carries personal data.
- **Details:**
  - The SPA emits the spans `dispute.raise.open` and `dispute.raise.submit` (SM-1, SM-2). The relay starts a root span when the row has no stored context.
  - The instruments are `disputedesk.disputes.raised` (`reason`), `disputedesk.dispute_actions` (`action`, `actor_type`), `disputedesk.sla.escalations`, `disputedesk.disputes.resolved` (`outcome`, `sla_escalated`), `disputedesk.disputes.withdraw_age` (histogram, SM-C3), `disputedesk.linking.events` (`event`: started, completed, locked, capped), `disputedesk.raise.rejections` (`status`) and `disputedesk.outbox.failed`.
  - Logs use message templates. HttpClient instrumentation for the stub never records customer identifiers, because they travel only in headers and bodies (AD-21). A test asserts that no exported span attribute contains a seeded customer ID.
- **Rejected:** `OpenTelemetry.Instrumentation.ConfluentKafka` (prerelease); span links without a parent.

### AD-21 - Ports and the core-banking stub [Accepted 2026-10-26]

- **Binds:** R4, R5, R11 to R13, R25, R26
- **Prevents:** resilience only on paper; three stories growing the port and the stub at once; customer identifiers in URLs
- **Rule:** Only `ICoreBankingClient`, `ISimSwapCheck` and `ISmsSender` cross to external systems; they are declared in Application and implemented in Infrastructure. The core-banking stub is a separate .NET minimal API, `backend/stubs/DisputeDesk.CoreBankingStub`, with its own Dockerfile target and the Compose service `core-banking`; it references no DisputeDesk project and reads `backend/fixtures/local.json`. Customer and account identifiers travel in headers or bodies, never in the path or query. The typed `HttpClient` uses `AddStandardResilienceHandler`, tuned so its behaviour can be demonstrated, and exhausted policies give 503 `core-banking-unavailable` with nothing written.
- **Details:**
  - **The port and stub, shipped whole:**
    - `MatchCustomerAsync(idNumber, accountNumber)` → customer ID and the cellphone on record. This is a POST body; the ID number is never in a URL.
    - `GetTransactionsAsync(customerId, cursor, limit)`, merged across the customer's accounts, keyset ordered
    - `GetTransactionAsync(customerId, accountRef, transactionId)`
    - `ISimSwapCheck` against the stub
    - `ISmsSender`, SMTP to Mailpit only
  - **Ownership and shape.** A call for an account or transaction that isn't that customer's returns 404, which the Api maps to `not-found`. The transaction shape is `transactionId`, `accountRef` (opaque, stable), `accountLast4`, `transactionDate`, `description`, `amount`, `currency`, `direction` (`debit`/`credit`), `kind` (`card`, `ownAccountTransfer`, `transfer`, `other`), `state` (`settled`/`pending`) and `maskedCard` (first 6 and last 4, or null). Full account and card numbers never reach the Api. Every account and transaction in the fixtures carries an explicit `accountRef` and `transactionId`.
  - **Resilience.** Attempt timeout 2 s, 2 retries from 200 ms with jitter, total 5 s (`TotalRequestTimeout`, the whole-call deadline), circuit breaker opening when every call in a 30 s window fails (`FailureRatio` 1.0, minimum throughput 5) and breaking for 15 s. Retries cover every method on purpose, since every stub call is read-only. To show it, stop the stub: raising gets 503 within about 5 s and history still works.
  - The stub adds a runtime unit, allowed by NFR-ARC-2.
- **Rejected:** an in-process fake; WireMock (a JVM container); the SPA calling the stub.

### AD-22 - Data model and table ownership [Accepted 2026-10-26]

- **Binds:** R7, R10, R15, R26, R28, R29, R31
- **Prevents:** the same table added twice; two writers for one table; a Worker running on an old schema; stored events read, logged or traced more widely than disputes
- **Rule:** The `disputedesk` database uses plural PascalCase table names and PascalCase column names that match the C# entity properties, as EF Core and Npgsql generate them by default [ADOPTED], with the constraint names below, and each table has only the writers listed below. The Api applies EF Core migrations at start-up, under EF Core's migration lock, before it reports ready, and in AWS the Worker starts after the Api is healthy. Locally the in-process relay and sweep start only after the Api's migrations: the Api applies them before its host starts its hosted services (EF Core, *Applying Migrations*: apply migrations at runtime; ASP.NET Core, *App startup*: hosted services start when `Run` executes), so neither runs on a missing schema. Stored events are private records with the same access as `Disputes`, never logged or traced. Nothing is deleted from `Disputes`, `StoredEvents` or `AuditLogEntries` in v1.
- **Details:**
  - **Names.** Every table's primary key is `Id`; a foreign key column is `<EntityName>Id`. Constraints are `PK_<TableName>`, `FK_<DependentTable>_<PrincipalTable>_<ForeignKeyColumn>`, `IX_<TableName>_<ColumnNames>`, `UQ_<TableName>_<ColumnNames>` (set with `HasDatabaseName`, since EF Core names unique indexes `IX_`) and `CK_<TableName>_<Description>`. Columns holding a UTC instant end in `Utc`. Enum values are stored as `text` holding the C# member name (PascalCase, `HasConversion<string>()`), instants as `timestamptz` and money as `numeric(19,2)` plus a currency code. Handwritten SQL double-quotes every identifier. `DataProtectionKeys` is the shape `DataProtection.EntityFrameworkCore` defines, unchanged. The ER diagram below is the starting schema; the reviewer checks each generated migration against these names.
  - **Writers.**

    | Table | Written by |
    | --- | --- |
    | `CustomerLinks`, `LinkingChallenges`, `IdempotencyKeys`, `Sessions`, `DataProtectionKeys` | the Api only |
    | `Disputes` (the read model of each stream) | the Api, the SLA sweep and the seeder, only through AD-13 and AD-14 |
    | `StoredEvents` | inserted only by AD-14; never updated or deleted (the AD-14 guard) |
    | `OutboxMessages` | inserted only by AD-14, updated only by the relay, with one operator exception: releasing a `Failed` row (below) |
    | `AuditLogEntries` | insert only |

  - **`StoredEvents`** (ad-009) is part of the dispute data model from the migration that creates `Disputes`, and has `Id` (the stored event ID, = outbox ID = CloudEvent `id`), `DisputeId` (FK to `Disputes`), `Version`, `EventType` (text, the AD-14a type), `OccurredAtUtc`, `ActorType`, `ActorId`, `TraceId` and `Data` (`jsonb`: the stored event record's own fields, serialised with the shared JSON options of AD-5, so camelCase keys and enum values; `jsonb` key order is harmless because `Data` is never re-sent byte for byte, unlike AD-14's `Payload`). Its only index besides the key is `UQ_StoredEvents_DisputeId_Version`, which also serves reading a stream in order; there is no `GlobalPosition` column and no `EventType` index. The timeline is built from a dispute's stored events; there is no separate timeline table.
  - **Personal data** (ad-009). Stored events hold `CustomerId`, `AccountRef`, `TransactionId` and the snapshot. They have the same access as `Disputes` (AD-11), are never logged or traced (no log template, span attribute or metric label carries their data; AD-20) and are kept under NFR-DATA-7. `EnableSensitiveDataLogging` stays off outside tests, and AD-20's telemetry test also asserts that no captured log line holds a seeded customer ID or account reference. Transactions are never copied apart from the snapshot.
  - **Migrations.** From the first release, migrations are forward-only and stay compatible with the previous release's code (EF Core, *Managing migrations*). Before it, no environment holds the schema except disposable local and CI databases, so the history may be reshaped in place, and every database is recreated when it is. The migration history shows only forward development of the data model, so no migration creates a table a later one drops. No two stories touch migrations at once.
  - **Releasing a `Failed` outbox row** is the one operator exception to "updated only by the relay". After the cause is fixed outside the row, an operator runs, as the `disputedesk` role in one PostgreSQL transaction, a targeted `UPDATE "OutboxMessages" SET "Status" = 'Pending', "Attempts" = 0, "NextAttemptAtUtc" = now() WHERE "Id" = <event ID> AND "Status" = 'Failed'`, and commits only when it updated one row. `Payload` and every other column stay unchanged, a row is never set `Sent` or deleted by hand, and the release is proved by the event reaching the topic before its dispute's successor (AD-17). `infra/deploy/runbook.md` gives the exact steps; there is no release endpoint or command.
- **Rejected:** a schema per feature; the Worker applying migrations; storing full account or card numbers; a release endpoint for `Failed` outbox rows.

### AD-23 - Client IP, forwarded headers and the gateway edge

- **Binds:** R8, R10, R32
- **Prevents:** per-IP limits keyed to a proxy; a spoofed `X-Forwarded-For`; a wrong OIDC redirect host in AWS; nginx's 503 being read as a core-banking outage
- **Rule:** nginx is the edge: its config is templated from environment values, it sets the security headers and a coarse per-IP limit, and it sends the Api single-value forwarded headers. The Api trusts forwarded headers only from the configured network `ForwardedHeaders:TrustedNetwork`, with `ForwardLimit = 1` and only the public app host allowed, so a spoofed header from anywhere else is ignored. Each security header appears once per response.
- **Details:**
  - **Templated config.** The official image's `templates/`: app host, auth host, auth origin for the CSP `form-action`, and HSTS (AWS only).
  - **Forwarded headers.** The gateway sends `Host` = the public app host, plus single-value `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host`. Locally the client is `$remote_addr`. In AWS, `real_ip` trusts only the CloudFront origin-facing ranges and reads `CloudFront-Viewer-Address` with its port stripped. `X-Forwarded-Proto` comes from `CloudFront-Forwarded-Proto`. All three distributions (AD-28) have IPv6 disabled.
  - **Api trust.** `ForwardedHeaders` processes For, Proto and Host. The loopback defaults are cleared, `KnownProxies` stays empty, `KnownIPNetworks` holds only `ForwardedHeaders:TrustedNetwork` (the Compose default network's subnet, pinned in `docker-compose.yml`; no name lookup at start-up; `KnownNetworks` is obsolete in .NET 10), and `ForwardedHeadersOptions.AllowedHosts` holds the public app host. Every container on that network is trusted. A test proves a spoofed header from anywhere else is ignored.
  - **Coarse limit.** `limit_req` allows 10 requests per second with a burst of 20, keyed on the real client address, on `/api/` and, locally, `/otlp/` (AD-29). It rejects with 429 and a `rate-limited` ProblemDetails body without `traceId` (AD-3). IPv6 partitions in the Api use the /64 prefix.
  - **Security headers.** nginx sets the CSP, `X-Content-Type-Options`, `Referrer-Policy` and `frame-ancestors` with `add_header ... always`; the Api sets only `Cache-Control: no-store`, on every `/api` response it produces (OWASP HTTP Headers Cheat Sheet). nginx adds the same `Cache-Control: no-store` only to the `/api` responses it generates itself (its 429 and its own error pages), so each response carries it once.
- **Rejected:** trusting any `X-Forwarded-For`; reading the IP in handlers; CloudFront response-header policies for the security headers (nginx owns them in both environments).

### AD-24 - Cookie and transport security per environment [Accepted 2026-10-26]

- **Binds:** R2, R32, R37, R38
- **Prevents:** a host step for local HTTPS; insecure cookies in AWS; environment branches in code
- **Rule:** Local runs keep the zero-click `http://localhost:8080` and `http://auth.localhost:8080`. The session and antiforgery cookies are HttpOnly and SameSite=Strict everywhere; in AWS, behind HTTPS through CloudFront, they use `SecurePolicy = Always` and the `__Host-` prefix, and in Development only they use `SameAsRequest` and unprefixed names (R2 exempts local `http://localhost`). Every cookie name and the secure policy come from configuration, not code.
- **Details:**
  - Keys: `Authentication:SessionCookie:Name`, `Authentication:AntiforgeryCookie:Name`, `Authentication:Cookie:SecurePolicy`, `Authentication:CorrelationCookie:Name`, `Authentication:NonceCookie:Name`.
  - The OIDC correlation and nonce cookies use `__Host-` names with `Path=/` in AWS over HTTPS, and unprefixed names on local http. They stay `Secure` and `SameSite=None` in every environment, because the `form_post` callback is cross-site.
  - In AWS, Zitadel runs with `ExternalSecure=true`, `ExternalPort=443` and TLS mode external, and the web app has `developmentMode` off.
- **Rejected:** mkcert (a host step, which breaks "Docker only"); a self-signed gateway certificate (browser warnings).

### AD-25 - Health, shutdown and relay liveness [Accepted 2026-10-26]

- **Binds:** R27
- **Prevents:** a hung relay or sweep that still reports running; a readiness check that fails on every Kafka blip
- **Rule:** The Api keeps `/health/live` (no checks) and `/health/ready` (Postgres and Zitadel discovery, not core banking). The relay and the sweep expose no HTTP: in whichever host runs them (the Api locally, the Worker in AWS) each touches its own heartbeat file every loop, and that host's Compose health check reads them. Both hosts keep the default 30-second `HostOptions.ShutdownTimeout` and finish the in-flight request or, for the relay, the one record in flight; Compose sets `stop_grace_period: 35s`.
- **Details:**
  - The files are `/tmp/heartbeat-relay` and `/tmp/heartbeat-sweep`. A relay that isn't leader touches its file on each lock attempt.
  - The local Dockerfile targets add a static busybox for the Compose health checks, which fail if the relay file is older than 30 seconds or the sweep file is older than 90 seconds (three missed 30-second sweep loops).
  - At shutdown the relay stops taking new rows, waits for the in-flight send's delivery report (bounded by the delivery timeout) and marks it Sent, or leaves it Pending for the next start (at-least-once, AD-17).
- **Rejected:** Kestrel in the Worker just for probes; Kafka in any readiness check.

### AD-26 - Configuration and secrets per environment [ADOPTED]

- **Binds:** R32, R37, R38, R39
- **Prevents:** secrets in the repo; dev defaults in AWS; colliding configuration keys; environment checks in feature code
- **Rule:** One options class per configuration section, bound with `ValidateOnStart`, under exactly the keys below, which `provision.sh` also uses for the `Zitadel:*` and `Operations:*` values it writes. Secrets come only from the environment, never from the repo or an image. Feature switches are configuration keys, never `IsDevelopment()` in feature code. Scalar is served at `/scalar/` and holds no credential, locally or in AWS.
- **Details:**
  - **Keys.** `Kafka:BootstrapServers` (`localhost:19092` for a host-run process, `redpanda:9092` in Compose; plaintext on the private network in v1, a `// shortcut:`, with broker SASL/SCRAM or mTLS in `docs/production-path.md`), `Zitadel:Issuer`, `Zitadel:MetadataAddress`, `Zitadel:ProjectId`, `Zitadel:WebApp:ClientId`, `Zitadel:WebApp:ClientSecret`, `Authentication:SessionCookie:Name`, `Authentication:AntiforgeryCookie:Name`, `Authentication:CorrelationCookie:Name`, `Authentication:NonceCookie:Name`, `Authentication:Cookie:SecurePolicy`, `Operations:AllowedClients` (an array of subs, written only by `provision.sh`; no `appsettings*.json` holds an `Operations` section, because the binder merges arrays by index across providers), `Linking:OtpHmacKey`, `Linking:QueueCapacity`, `Linking:Budget`, `Linking:RateLimits:*`, `ForwardedHeaders:TrustedNetwork`, `ForwardedHeaders:AllowedHosts`, `CoreBanking:BaseUrl`, `Seed:Enabled`, `Scalar:Enabled`, `DataProtection:SsmParameterPath` (unset: the key ring in Postgres; set: in SSM Parameter Store, AD-7), `DataProtection:KmsKeyId` (the KMS key that encrypts the key ring, required when the path is set).
  - **Files.** `docker-compose.yml` keeps its dev-only defaults. `infra/deploy/aws/compose.yml` (ad-011) uses `${VAR:?}` with no inline defaults, and `.env` is written from SSM SecureStrings at deploy.
  - **Secrets.** The OTP HMAC key, database passwords, the Zitadel master key, the Mailpit basic-auth credentials and the origin-verify header value.
  - **Feature switches.** Scalar and the seeder are on in the AWS demo.
  - **Scalar.** It never holds or pre-fills a client ID, client secret or token. A script (`scripts/operations-token.sh`) runs the client-credentials grant as a confidential client (RFC 6749 §4.4) and a person pastes the bearer token into Scalar. The Api holds no operations client secret. Scalar's options are identical in every environment, so the page at `/scalar/` has one inline script with one fixed hash: its own nginx CSP allows only that hash in `script-src` (never `'unsafe-inline'`) and `connect-src 'self'`, nginx keeps owning the headers (AD-23), and a test hashes the served inline script against the nginx template. Its `style-src` is `'self' 'unsafe-inline'`, on the Scalar locations only, because Scalar 2.18.3 adds its stylesheet at runtime; it never also holds a style hash, which would make browsers ignore `'unsafe-inline'`. `openapi.json` declares only a `bearer` scheme, on the operations routes only; it declares no `clientCredentials` scheme and no `tokenUrl`, and real tools find the token endpoint through OIDC discovery and the README.
- **Rejected:** secrets in images; AWS Secrets Manager (cost).

### AD-27 - Compose platform and runtime units [ADOPTED]

- **Binds:** R26, R37
- **Prevents:** stories adding containers; a second public entry point; local and AWS drifting apart
- **Rule:** The runtime units are fixed. Locally: gateway (nginx, the only public entry), api (which also runs the relay and the sweep, `Worker:InProcess=true`), postgres, redpanda, zitadel, mailpit, aspire-dashboard and `core-banking` (AD-21). In AWS the same units, except that RDS replaces postgres, the otel collector replaces aspire-dashboard (AD-29), and a separate worker runs the relay and the sweep; a test pins that the two environments differ only in these units. The SPA is built by `frontend/Dockerfile` as a build-only `spa` service under the Compose profile `build`, never started, and copied into the gateway image through a named build context (ad-011). Images are multi-stage, non-root, and pinned by tag and digest.
- **Details:**
  - postgres holds the databases `disputedesk` and `zitadel` and the role `qa_readonly`.
  - The relay's host creates `disputedesk.disputes` with 3 partitions at start-up through the Kafka AdminClient (`CreateTopicsAsync`; an existing topic counts as success), registered with the relay by `AddOutboxRelayAndSlaSweep` (`Infrastructure/DependencyInjection.cs`, the one registration both hosts call), so the topic exists before the first event. The broker sets no topic auto-creation flag.
  - zitadel runs the `disputedesk-zitadel` image: Zitadel's own binary plus the provisioning scripts and their tools, run as Zitadel's non-root user. Its `post_start` hook runs `provision.sh` (AD-6); the script removes the provisioned marker first and writes it only on success, the health check requires the marker, and the api has `depends_on: zitadel (healthy)`.
  - aspire-dashboard is the plain pinned image; the services that use it depend on it starting, not on a health check.
  - In AWS the worker has the heartbeat health check (AD-25) and `depends_on: api (healthy)`.
  - One opt-in exception: `redpanda-console` under the Compose profile `console`, local only, off by default, never started in AWS, published only on `127.0.0.1` (ad-010).
- **Rejected:** a separate SPA container; Kubernetes locally; a one-shot provisioning container (a second container holding Zitadel's administrator token); a one-shot topic-creation container (the relay's host already connects to the broker).

### AD-28 - AWS dev environment [ADOPTED]

- **Binds:** R38
- **Prevents:** a deployment shape that differs from local; long-lived keys; a stack that doesn't fit the instance; a wrong public host
- **Rule:** One EC2 instance runs the same Compose stack plus `infra/deploy/aws/compose.yml`, with RDS PostgreSQL in place of the postgres container, the worker as its own unit (AD-27) and three CloudFront distributions in front, all defined in Terraform under `infra/terraform/`. After `publish` on `main`, CI's `deploy` job calls the reusable `deploy.yml` with the five published digests when the repository variable `DEPLOY_ENABLED` is `true`; `deploy.yml` is also dispatched by hand for rollback. Its job runs in the GitHub environment `dev`, which only `main` may use, assumes a GitHub OIDC deploy role that trusts the subject `environment:dev`, and runs the instance's deploy script through SSM Run Command, so there are no long-lived keys. Until the new free-plan account exists, the code is checked only with `terraform fmt -check`, `terraform validate` and `terraform test` plans against mocked providers, and no AWS command is run. AWS-only mechanisms stay as built until an apply is possible: no new ones are added before then, except the deploy after `publish` and the `environment:dev` trust (ad-012), whose live role assumption is proved only by the first deploy; the mocked Terraform tests pin the new subject.
- **Details:**
  - **Terraform.** Modules (`network`, `compute`, `database`, `edge`, `deploy-role`, `observability`, `budget`) and one root, `envs/dev`. Each module is called once today; the seven stay so a second environment can reuse them (decided 2026-11-03). Each IAM policy document is a `<name>.json.tftpl` file beside its module's `main.tf`, rendered with `templatefile()`, and the infra test project reads every `Action` and condition key from those files with `System.Text.Json` and checks them against AWS's service reference (human decision 2026-11-04). The deploy role's OIDC subject is pinned by `tests/deploy_role.tftest.hcl` and confirmed by a one-time check recorded in `infra/terraform/README.md`, not by a CI job. The S3 backend uses `use_lockfile = true`; a separate `bootstrap` root creates the state bucket.
  - **Compute.** One EC2 c7i-flex.large (AL2023, Elastic IP, IMDSv2 hop limit 2, SSM Session Manager, no SSH, 2 GiB swap). Only the `api` and `otel-collector` containers may reach the instance metadata service: the override pins their addresses on a fixed Compose subnet, and the deploy script inserts a `DOCKER-USER` iptables rule that drops traffic to 169.254.169.254 from every other container, the Amazon ECS Developer Guide's method for blocking container access to instance metadata; a test asserts the rule. The override's memory limits total about 3.4 GiB, with Redpanda at `--memory=512M` [ASSUMPTION: the AWS story measures it].
  - **Database.** RDS PostgreSQL db.t4g.micro, single AZ and private, holds both databases. Roles come from a one-shot init that applies `infra/images/postgres` against RDS [ASSUMPTION]. Npgsql and Zitadel connect with `sslmode=verify-full` and the RDS CA bundle. Backups are kept 1 day, and `skip_final_snapshot = true`.
  - **Edge.** Three CloudFront distributions, app, auth and mail (Mailpit), use `CachingDisabled` and forward Host, cookies and query strings to nginx on port 80. Terraform outputs their domains to SSM, and from there into `.env` (gateway hosts, Zitadel `ExternalDomain`, `APP_ORIGINS`). The security group admits only the CloudFront origin-facing prefix list, and nginx rejects requests without the secret origin header. CloudFront reaches the origin over plain HTTP for the free-plan demo, which holds only synthetic data; it is a `# shortcut:` in Terraform, and an HTTPS or VPC origin, a domain, an ACM certificate with a TLS 1.2 minimum viewer protocol, and WAF are a `docs/production-path.md` item, as are the default CloudFront certificate's TLS 1.0 minimum and the absence of WAF. In AWS, containers reach the identity origin at its public HTTPS URL through CloudFront, not through a network alias, and the CloudFront origin-facing ranges reach nginx's `set_real_ip_from` as a file the deploy script writes from AWS's `ip-ranges.json`.
  - **Images and Mailpit.** Images come from GHCR, tagged by git SHA (AD-32). Mailpit lives on its own origin: the third CloudFront distribution and its own nginx server block, which checks the origin-verify header, with Mailpit's UI and API behind basic auth (`MP_UI_AUTH`); the app host answers 404 on `/mailpit/`, so no third-party UI shares the SPA's origin.
  - **Deploy.** The script writes `.env` from SSM atomically, initializes the RDS roles once, runs `docker compose up -d --wait` and fails the command on a non-zero exit; the workflow waits for the invocation's terminal status and fails unless it is `Success`. Rollback means setting `DEPLOY_ENABLED` to `false`, dispatching `deploy.yml` with the previous SHA, and setting it back once the fix is on `main`, so the next `main` run does not redeploy forward. Terraform plans and applies from the maintainer's machine, never from CI; CI runs only `terraform fmt -check`, `terraform validate` and `terraform test` against mocked providers, with no AWS credentials.
  - **Cost and teardown.** A budget alarm excludes credits. `terraform destroy` removes everything except the state bucket.
- **Rejected:** ECS or Fargate; an ALB; the SPA on S3 with OAC and Caddy at the origin (from the AWS research, rejected to keep one Compose stack); ECR; DynamoDB locking.

### AD-29 - Telemetry export per environment

- **Binds:** R36
- **Prevents:** code that knows where telemetry goes
- **Rule:** The hosts export OTLP only, switched on by `OTEL_EXPORTER_OTLP_ENDPOINT` (P0-3): locally to the Aspire Dashboard, and in AWS to an `otel/opentelemetry-collector-contrib` container, pinned by digest, that sends traces to the X-Ray OTLP endpoint, metrics to CloudWatch and logs to a log group. Locally the SPA exports OTLP/HTTP to `/otlp/` on the same origin, and the gateway proxies it to the Aspire Dashboard. In AWS there is no browser telemetry intake, so browser spans are a local-stack feature.
- **Details:**
  - The collector uses `sigv4auth` and the instance role (`CloudWatchAgentServerPolicy`). Terraform enables Transaction Search and creates the log group with 7-day retention.
  - The local `/otlp/` proxy has a body-size cap and the coarse limit, so the CSP keeps `connect-src 'self'`.
  - In AWS the gateway routes no `/otlp/`, and the SPA built for AWS exports nothing (a build-time switch); the Api and Worker still export to X-Ray and CloudWatch through the collector. An authenticated or sampled browser intake is a `docs/production-path.md` item.
- **Rejected:** a CloudWatch exporter in the hosts; the browser posting to a second origin; an anonymous `/otlp/` intake in AWS (unbounded X-Ray and CloudWatch ingestion).

### AD-30 - Seed data path

- **Binds:** R24
- **Prevents:** seeded disputes that bypass the commit path or cannot be replayed; seed and stub disagreeing; the seeder's bypass matching another customer's dispute; relay tests that pass only on an empty outbox
- **Rule:** `backend/fixtures/local.json` is the one source of seed data for the stub and the seeder, which resolve its relative `daysAgo` from the same anchor, the start of the current South African day. The seeder runs through EF Core's `UseAsyncSeeding` and `UseSeeding` (the documented seeding hooks, run by `MigrateAsync`) behind `Seed:Enabled`, and is idempotent. AD-13 governs: each seeded dispute is created with `Dispute.Raise` and reaches its status only through `StartReview`, `Escalate`, `Resolve` and `Withdraw`, never by setting a status (ad-009), and goes through the full AD-14 commit, so it gets stored events and outbox rows like any other dispute. Each QA run starts on a fresh stack.
- **Details:**
  - **One source.** The fixture holds customers, logins, links, accounts and transactions (explicit `accountRef` and `transactionId`, relative `daysAgo`) and the seeded disputes.
  - **Named bypass.** The seeder writes `CustomerLinks` (`LoginSub` = the fixture login's `userId`) and reads each fixture dispute on the migrating context with `IgnoreQueryFilters(["Ownership"])`, AD-11's third named bypass, matching the dispute's customer ID as well as its `accountRef` + `transactionId`, so the bypass never widens the match; it raises only the ones it does not find. It builds no second `AppDbContext` and no `CurrentCustomer`.
  - **Idempotency.** EF Core runs `UseAsyncSeeding` and `UseSeeding` under its migration lock on every migrate, whether or not a migration was pending (EF Core *Data Seeding*), so the seeder must be idempotent: it raises only the fixture disputes it does not find, saves each stream with one `SaveChanges`, and the unique `(AccountRef, TransactionId)` index refuses a duplicate.
  - **Commit.** Each seeded action has its `Actor` and a back-dated instant resolved from the fixture against the anchor, and gets one stored event and one outbox row; each seeded dispute's whole stream is written in one `SaveChanges`. A restart adds no rows, so nothing new is published; the relay publishes each row at least once and consumers deduplicate by CloudEvent `id` (AD-17). Relay tests must not depend on an empty outbox (AD-17).
  - **Seeded values.** A seeded dispute's `Version` equals its number of stored events, and replaying its stream reproduces its row (AD-13). Customer actions use the fixture login's `userId` as `Customer`; start review, escalate and resolve use `Automation` / `worker:seeder` (AD-14a). Open seeded disputes (Submitted or Under review) other than the SLA demo dispute are not yet due when the first sweep runs (`SlaDueAtUtc` days after the seeding instant), so the first sweep leaves their streams as seeded. The SLA demo dispute gets `SlaDueAtUtc` = seeding instant plus 5 minutes and `CreatedAtUtc` = `SlaDueAtUtc` minus 240 hours.
  - **Customer D** starts unlinked in the seed; it is the customer the E2E journey links.
  - **QA reset.** A QA run starts with `docker compose down -v`, then `docker compose up --wait`, so customer D starts unlinked from the seed; there is no test-only database role and no reset endpoint (ad-005).
- **Rejected:** SQL seed scripts; seeding through the API; seeding without stored events or outbox rows.

### AD-31 - Frontend integration contract [ADOPTED]

- **Binds:** R34, R40, every frontend story
- **Prevents:** hand-written fetches; guessed auth state; routing on message text
- **Rule:** React and TypeScript in `frontend/`, built with Vite, with routes defined with TanStack Router and feature folders `features/{linking,transactions,disputes}`. Orval generates the hooks from `backend/openapi.json`, nobody edits them, and one mutator adds same-origin credentials and `X-XSRF-TOKEN`. The authorisation state comes from `/api/v1/authentication/session`, and the SPA routes on the problem `type`, compared as the full URI (AD-3), as EXPERIENCE.md's table says.
- **Details:**
  - **Forms.** shadcn/ui's current documented form pattern: `Field` with React Hook Form's `Controller` and a zod schema; each field wires `aria-invalid` and `aria-describedby` itself, because `Field` does not (shadcn/ui, *Forms*).
  - **After a 401** the SPA sends the user to sign-in with a `returnUrl` that carries no identifier from the current route: from a raise screen it returns to `/transactions`.
  - Each of these packages is verified and pinned when first added (AGENTS.md).
  - The SPA reads `attemptsRemaining`, `reason` and `customerStatus` from the problem bodies. Sign-out is a form POST (AD-7). The theme lives in `frontend/src/theme/tokens.css`. The review screen creates one `Idempotency-Key` per draft.
- **Rejected:** a hand-written client; reading auth state in JavaScript.

### AD-32 - CI and release pipeline

- **Binds:** R33, R34, R35
- **Prevents:** each story bolting its own job onto CI; unclear images in GHCR
- **Rule:** `ci.yml` jobs: `backend` (P0-4, plus the migrations drift check), `frontend` (`tsc --noEmit`, ESLint, Prettier, Vitest, Orval regeneration drift), `platform` (Compose validation), and `e2e` (Compose up, Playwright journeys with an axe scan, ZAP baseline). `security.yml` (P0-4) stays. Every check job runs on every pull request and every `main` push, with no docs-only skip. Two gate jobs report the result: `ci-passed` in `ci.yml` and `security-passed` in `security.yml` each need every check job of their workflow, run with `if: always()` and fail unless each succeeded, so a later job left out of a gate's `needs` fails a `floor-guard` step. The pinned Compose and Trivy installs are the local composite actions under `.github/actions/` (ad-012). On main a `publish` job, the only one with `packages: write`, pushes `api`, `worker`, `gateway` (with the SPA), `core-banking` and `zitadel` to GHCR, tagged `sha-` plus the first 12 characters of the commit SHA, and the deploy references digests. `deploy.yml` only consumes the published images by digest: CI's `deploy` job calls it with `publish`'s five digests, and it is dispatched by hand for rollback. A ruleset on `main` (`.github/rulesets/main.json`) requires a pull request, the `ci-passed` and `security-passed` checks on an up-to-date branch and CodeQL's code-scanning results, and blocks force pushes and deletion; the repository allows squash merging only, with auto-merge and branch deletion on merge (ad-012 clause 2). A pull request's checks test GitHub's merge of the up-to-date branch, and `main`'s own run builds, scans and publishes the squash commit (ad-012 clause 6).
- **Rejected:** a separate CI system; `latest` tags.

## Consistency Conventions

| Concern | Convention |
| --- | --- |
| Domain words | `docs/glossary.md` terms verbatim in code, routes, JSON, events and copy: `Login`, `LinkedCustomer`, `Linking`, `Dispute`, `Reason`, `Status`, `Outcome`, `Action`, `Actor`, `ActorType`, `Timeline`, `StoredEvent` (glossary: Stored event; "dispute event" means only the published fact). No synonyms such as enrolment, case, claim, user or transition |
| C# naming | `Disputes/RaiseDispute/` holds `RaiseDisputeRequest`/`Response` (Contracts), `RaiseDisputeHandler` (Application) and `RaiseDisputeEndpoint` (Api); configurations are named `<Entity>Configuration` |
| Routes | Lowercase, with kebab-case action segments: `/api/v1/disputes/<disputeId>/withdraw`, `/api/v1/operations/disputes/<disputeId>/start-review` |
| Casing | C# enum members PascalCase (`UnderReview`, `StartReview`). Wire values in API JSON and event payloads, including ProblemDetails extension fields, camelCase through one converter factory creating `CamelCaseNameOnlyEnumConverter<TEnum>` (AD-5), the same style as Microsoft Graph enums; a JSON request body's enum is read only from its exact camelCase name, and a number, a list or any other casing is refused. Enum values in Postgres `text` columns are the C# member names (`HasConversion<string>()`). Zitadel role keys use the same camelCase after the `dispute.` prefix (`dispute.startReview`, AD-9). Table, column and constraint names PascalCase, the EF Core default (plural tables, `Id` keys, `<EntityName>Id` foreign keys, `...Utc` instants). No DisputeDesk-defined name is snake_case; the only snake_case left belongs to protocols, tools and infrastructure (OIDC parameters, OpenTelemetry attributes, Postgres functions, the `qa_readonly` role). Route segments and problem `type` slugs kebab-case. Topic `disputedesk.disputes`; CloudEvents `type` dotted lowercase (`disputedesk.dispute.status-changed.v1`). Metric names OpenTelemetry style (`disputedesk.disputes.raised`). Test methods `Method_Scenario_Expected` (`Withdraw_WhenResolved_Returns409`) |
| Internal enum values (wire) | statuses `submitted`, `underReview`, `escalated`, `resolved`, `withdrawn`; actions `raise`, `startReview`, `escalate`, `resolve`, `withdraw`; outcomes `upheld`, `rejected`; actor types `customer`, `operations`, `automation`; reasons `unrecognised`, `duplicateCharge`, `wrongAmount`, `notReceived`, `cancelledSubscription`, `refundNotProcessed`. Stored in Postgres as `Submitted`, `UnderReview`, `StartReview`, `DuplicateCharge` and so on |
| Customer enum values (wire) | `customerStatus`: `submitted`, `underReview`, `underReviewPrioritised`, `resolved`, `withdrawn`; `actor`: `you`, `theBank`; `notDisputableReason`: `alreadyDisputed`, `outsideWindow`, `notEligible`; session `state`: `anonymous`, `unlinked`, `linked` |
| Database | Plural PascalCase tables, PascalCase columns matching the C# properties; `Id` primary keys, `<EntityName>Id` foreign keys; constraints `PK_<Table>`, `FK_<Dependent>_<Principal>_<Column>`, `IX_<Table>_<Columns>`, `UQ_<Table>_<Columns>` (via `HasDatabaseName`), `CK_<Table>_<Description>`; UTC instants are `timestamptz` columns ending in `Utc`; `numeric(19,2)` plus `Currency char(3)`; enums stored as `text` C# member names (PascalCase); no time defaults; handwritten SQL double-quotes identifiers; no naming-convention package. Database, role and Compose names (`disputedesk`, `qa_readonly`) are infrastructure names and keep their own style |
| Timestamps in JSON | RFC 3339 UTC as System.Text.Json writes a UTC `DateTimeOffset`: offset `+00:00`, fractional seconds as written. `transactionDate` is the instant, which the SPA renders in South African time |
| Errors | ProblemDetails only (AD-3) |
| Logging | Message templates. Never log ID numbers, OTPs, full account or card numbers, notes, tokens, or request bodies on linking routes |
| Personal data | ID number: never stored. Account: `accountRef` plus `last4`. Card: first 6 and last 4 digits |
| Clock | `TimeProvider`, read once per use case (AD-4); tests use a fake |
| Tests | Unit tests on Domain decisions; integration tests on Testcontainers Postgres with `WebApplicationFactory`, every endpoint in every authorisation state; no EF InMemory or SQLite |

## Stack

| Name | Version |
| --- | --- |
| .NET SDK / runtime | 10.0.403 / 10.0.14 (pinned exactly in `global.json`, the Dockerfile and `Directory.Packages.props`; bump the SDK, runtime and every `Microsoft.*` 10.0.x package together; stay on 10.0.x when .NET 11 ships) |
| EF Core / Npgsql EF provider | 10.0.14 / 10.0.3 |
| OpenIdConnect, JwtBearer, DataProtection.EntityFrameworkCore | same patch as the runtime (10.0.14) |
| Microsoft.Extensions.Http.Resilience | 10.11.0 |
| Confluent.Kafka | 2.16.0 |
| OpenTelemetry .NET | 1.20.1 |
| Scalar.AspNetCore | 2.18.3 |
| xunit.v3 / Testcontainers.PostgreSql | 4.0.2 / 4.16.0 |
| PostgreSQL (container) | 18.6 |
| Redpanda / Redpanda Console | v26.2.6 / v3.13.1 (Console under the opt-in `console` profile, AD-27) |
| Zitadel | v4.20.0 |
| nginx (`nginxinc/nginx-unprivileged`) | 1.31.7 (1.31.8 has no unprivileged build yet and no security fixes) |
| Mailpit | v1.32.0 |
| Node | 24.22.1 (`.nvmrc`) |
| React / Vite | 19.3.1 / 8.3.5 |
| TypeScript | 6.0.3 (typescript-eslint 8.72.0 needs a version below 6.1.0; TypeScript 7.0.2 is out) |
| Tailwind CSS / Orval / TanStack Query | 4.3.3 / 8.45.0 / 5.104.5 |
| Playwright | 1.64.1 |
| Terraform / hashicorp/aws | 1.17.0 / 6.70.0 |

Versions were checked against nuget.org, npm and the HashiCorp registry on 2026-10-26, or read from merged code. Each story re-verifies its versions and pins them exactly on first use (AGENTS.md). Pins not listed here (collector image, ESLint, Prettier, TanStack Router, React Hook Form, zod, `@axe-core/playwright`, the OpenTelemetry web SDK, the RDS minor version) are set by their owning stories.

## Structural Seed

### Component view: local

```mermaid
flowchart LR
  B[Browser SPA] -->|localhost:8080 and auth.localhost:8080| GW[gateway nginx]
  GW -->|static SPA| SPA[SPA files]
  GW -->|/api/*| API[api: browser sessions, REST, relay and SLA sweep]
  GW -->|/otlp/*| ASP[aspire-dashboard]
  GW -->|auth host| Z[zitadel, provisioned by its post_start hook]
  API -->|OIDC back channel via gateway alias| GW
  API --> PG[(postgres)]
  API -->|core banking and SIM-swap| CB[core-banking stub]
  API -->|SMTP| MP[mailpit]
  API -->|disputedesk.disputes| RP[redpanda]
  Z --> PG
  Z -->|SMTP| MP
  API -->|OTLP| ASP
```

### Deployment view: AWS dev

```mermaid
flowchart LR
  subgraph EC2[EC2 c7i-flex.large: Compose plus aws override]
    GW[nginx] --> API[api]
    GW --> Z[zitadel]
    GW -->|/otlp/| COL[otel collector contrib]
    WK[worker] --> RP[redpanda]
    API --> CB[core-banking stub]
    API --> MP[mailpit, UI behind basic auth]
    API --> COL
    WK --> COL
  end
  U[Browser] -->|HTTPS| CFA[CloudFront app]
  U -->|HTTPS| CFZ[CloudFront auth]
  CFA -->|HTTP 80 with origin secret| GW
  CFZ -->|HTTP 80 with origin secret| GW
  API --> RDS[(RDS Postgres db.t4g.micro)]
  WK --> RDS
  Z --> RDS
  COL --> CW[CloudWatch and X-Ray]
  SSM[SSM Parameter Store] -.->|.env at deploy| EC2
  GH[GitHub Actions deploy job, environment dev, via OIDC role] -.->|SSM Run Command| EC2
  GHCR[GHCR images] -.-> EC2
```

### Request path: raise a dispute, with trace propagation

```mermaid
sequenceDiagram
  participant B as Browser SPA
  participant G as gateway nginx
  participant A as Api
  participant C as core-banking stub
  participant P as Postgres
  participant W as relay (Api locally, Worker in AWS)
  participant K as Redpanda
  B->>G: POST /api/v1/disputes with cookie, X-XSRF-TOKEN, Idempotency-Key, traceparent
  G->>A: same headers plus X-Forwarded-For, -Proto, -Host
  Note over A: server span continues the browser trace
  A->>A: session, antiforgery, Linked policy, CurrentCustomer from CustomerLinks
  A->>P: insert IdempotencyKeys row InProgress with lease (own transaction)
  A->>C: get transaction (resilience handler, traceparent propagated)
  A->>A: Dispute.Raise (disputability, amount rule, one instant)
  A->>P: one transaction: StoredEvents, Disputes read model, OutboxMessages with TraceParent, key completed
  A-->>B: 201 with Location /api/v1/disputes/{id}
  W->>P: on the advisory-lock connection, read head outbox row per dispute
  Note over W: producer span parented by the stored traceparent
  W->>K: produce key disputeId, headers traceparent, tracestate, content-type
  W->>P: mark sent
```

### Data model (starting schema)

```mermaid
erDiagram
  CustomerLinks {
    uuid Id PK
    text LoginSub UK "UQ_CustomerLinks_LoginSub"
    text CustomerId UK "UQ_CustomerLinks_CustomerId"
    timestamptz LinkedAtUtc
  }
  LinkingChallenges {
    uuid Id PK
    text LoginSub "UQ_LinkingChallenges_LoginSub, partial"
    text CustomerId "null until matched"
    bytea OtpHmac "null until sent"
    int Attempts
    timestamptz ExpiresAtUtc
    timestamptz OtpSentAtUtc
    timestamptz SupersededAtUtc
    timestamptz ConsumedAtUtc
    timestamptz CreatedAtUtc
  }
  Disputes {
    uuid Id PK
    text CustomerId
    text AccountRef "UQ_Disputes_AccountRef_TransactionId"
    text TransactionId
    timestamptz TransactionDateUtc
    text Description
    numeric TransactionAmount "numeric(19,2)"
    text MaskedCard "first 6 and last 4"
    text AccountLast4
    text Reason
    text Note
    numeric ExpectedAmount "numeric(19,2), wrong amount only"
    numeric DisputedAmount "numeric(19,2)"
    text Currency "ZAR"
    text Status
    text Outcome
    int Version "latest stored event Version; disputeVersion"
    timestamptz CreatedAtUtc
    timestamptz SlaDueAtUtc "IX_Disputes_SlaDueAtUtc, partial"
  }
  StoredEvents {
    uuid Id PK "stored event ID = outbox ID = CloudEvent id"
    uuid DisputeId FK "FK_StoredEvents_Disputes_DisputeId"
    int Version "UQ_StoredEvents_DisputeId_Version"
    text EventType "no index"
    timestamptz OccurredAtUtc
    text ActorType
    text ActorId "never shown to customers"
    text TraceId
    jsonb Data "private: holds the snapshot on DisputeRaised"
  }
  OutboxMessages {
    uuid Id PK "CloudEvents id"
    text EventType
    uuid DisputeId FK "FK_OutboxMessages_Disputes_DisputeId"
    int DisputeVersion
    text Payload "full structured CloudEvent"
    text TraceParent
    text TraceState
    text Status "Pending, Sent, Failed"
    int Attempts
    timestamptz NextAttemptAtUtc
    timestamptz SentAtUtc
    text LastError
    timestamptz CreatedAtUtc
  }
  IdempotencyKeys {
    uuid Id PK
    text CustomerId "UQ_IdempotencyKeys_CustomerId_Key"
    text Key
    uuid LeaseId
    bytea RequestHash
    text Status "InProgress, Completed"
    int ResponseStatus
    text ResponseLocation
    text ResponseBody
    timestamptz CreatedAtUtc
    timestamptz ExpiresAtUtc
  }
  AuditLogEntries {
    uuid Id PK
    timestamptz OccurredAtUtc
    text Action
    text Result
    text ActorType
    text ActorId
    text LoginSub
    text CustomerId
    uuid DisputeId "no FK: linking entries have none"
    inet ClientIp
    text TraceId
    jsonb Details "never an ID number"
  }
  Sessions {
    uuid Id PK
    bytea KeyHash UK "UQ_Sessions_KeyHash"
    text LoginSub
    bytea Ticket "data-protected"
    timestamptz ExpiresAtUtc
    timestamptz AbsoluteExpiresAtUtc
  }
  DataProtectionKeys {
    int Id PK
    text FriendlyName
    text Xml
  }
  CustomerLinks ||--o{ Disputes : "CustomerId"
  Disputes ||--|{ StoredEvents : "stream"
  Disputes ||--o{ OutboxMessages : "dispute events"
  Disputes ||--o{ AuditLogEntries : "refused actions and reads"
```

### Endpoint map

| Group | Scheme and policy | Routes |
| --- | --- | --- |
| Authentication | cookie; anonymous allowed | `GET /api/v1/authentication/login`, `POST /api/v1/authentication/logout`, `GET /api/v1/authentication/session`, `GET /api/v1/antiforgery`; callbacks `/api/signin-oidc`, `/api/signout-callback-oidc` |
| Linking | cookie, `Unlinked`, rate limited | `POST /api/v1/linking/start` (202), `POST /api/v1/linking/verify` |
| Customer | cookie, `Linked` | `GET /api/v1/transactions`, `POST /api/v1/disputes` (Idempotency-Key, 201), `GET /api/v1/disputes`, `GET /api/v1/disputes/<disputeId>`, `POST /api/v1/disputes/<disputeId>/withdraw` |
| Operations | bearer, one role each | `GET /api/v1/operations/disputes/<disputeId>` (`dispute.read`), `POST .../start-review`, `.../escalate`, `.../resolve` |
| Health | none | `/health/live`, `/health/ready` (gateway: `/api/health/*`) |

### Source tree additions

```text
backend/
  src/DisputeDesk.*/<Feature>/...        # AD-1 feature folders
  stubs/DisputeDesk.CoreBankingStub/     # AD-21, no project references
  fixtures/local.json                    # AD-30 single seed source (ad-011)
frontend/                                # AD-31; Dockerfile builds the SPA the gateway image copies (ad-011)
contracts/events/                        # AD-19 JSON Schemas and v1 samples
infra/terraform/modules/, envs/dev/      # AD-28
infra/tests/DisputeDesk.PlatformTests/  # checks of infra files, no reference to backend/ (ad-011)
infra/deploy/aws/compose.yml                    # AD-26 production overrides (ad-011)
```

The repository uses one top-level folder per stack (`backend/`, `frontend/`, `e2e/`, `infra/`), with Microsoft's `src/` and `tests/` layout inside `backend/`; a root `src/` and `tests/` layout was considered and rejected because each folder is one build context and one agent lane.

## Capability → Architecture Map

| Quality area | Where it lives | Governed by |
| --- | --- | --- |
| Architecture and scalability | Six projects and two hosts (one process locally, two in AWS); a stateless Api (sessions and keys in Postgres); a second relay or sweep would be harmless | AD-1, AD-2, AD-7, AD-17, AD-18, AD-27 |
| Resilience and operational readiness | Tuned resilience handler on the stub, the outbox through Kafka downtime, heartbeats, graceful shutdown, deploy rollback | AD-17, AD-21, AD-25, AD-28 |
| Data and domain modelling | `Dispute` aggregate, stored events loaded by replay with a read model, one commit path, uniqueness and concurrency, one clock read, schema | AD-4, AD-13, AD-14, AD-15, AD-22, ad-009 |
| API design | `/api/v1`, action endpoints, keyset paging, ProblemDetails, committed OpenAPI, idempotent raise | AD-3, AD-5, AD-8, AD-16 |
| Security (primary) | Browser sessions, scheme pinning, roles, authorisation states, ownership filter, linking without disclosure, trusted proxies, cookies, audit | AD-6 to AD-12, AD-14a, AD-23, AD-24, AD-26 |
| Code quality | Layer tests, analysers, glossary terms, no banned libraries | AD-1, AD-3, AD-32, conventions |
| Testing (important) | Domain unit tests, Testcontainers integration tests, OpenAPI and event contract tests, Playwright and ZAP on the stack | AD-13, AD-19, AD-32, conventions |
| Observability | One trace from browser to the relay, named business metrics, Aspire and CloudWatch | AD-20, AD-29 |
| Infrastructure and DevOps | Zero-click Compose, CI and GHCR, the Terraform dev environment, OIDC deploy | AD-26, AD-27, AD-28, AD-32 |
| Documentation | This spine and its [amendment history](spine-amendments.md), plus the decision records in `docs/decisions/`: ad-001 to ad-003 and ad-005 to ad-011 (Zitadel, `IAppDbContext`, Result types, QA data integrity, actor-agnostic actions, browser session, linking, stored events, Redpanda, team folders); the delivery-process record ad-004 (model roles) is in `docs/process/` | all |

| Requirement | Governed by |
| --- | --- |
| R1 to R3 sign-up, sign-in, sign-out | AD-6, AD-7, AD-24 |
| R41 sign-in and sign-out audit | AD-14, AD-14a |
| R4 to R8, R10 linking | AD-12, AD-14a, AD-15, AD-23 |
| R9, R17, R18 authorisation and scoping | AD-10, AD-11, AD-13 |
| R11, R12 transactions | AD-5, AD-13, AD-21 |
| R13 to R16, R19 raise, history, withdraw | AD-13 to AD-16, ad-009 |
| R20, R21 operations | AD-8, AD-9, AD-13, AD-14a |
| R22 SLA | AD-18, AD-13 |
| R23, R28 events and atomicity | AD-14, AD-17, AD-19, AD-19a |
| R24 seed | AD-30, AD-14 |
| R25, R26 architecture and units | AD-1, AD-21, AD-27 |
| R27 health | AD-25 |
| R29 data | AD-4, AD-15, AD-22 |
| R30 API | AD-3, AD-5 |
| R31 personal data | AD-19, AD-20, AD-21, AD-22, conventions |
| R32 gateway and secrets | AD-23, AD-26 |
| R33, R34 CI and code quality | AD-32, AD-1, AD-31 |
| R35 tests | AD-19, AD-32, conventions |
| R36 observability | AD-20, AD-29 |
| R37, R38 platform and AWS | AD-27, AD-28 |
| R39 docs | AD-26 (Scalar), Deferred |
| R40 accessibility | AD-31, AD-32 (axe scan in E2E) |

## Deferred

| Item | Why it can wait |
| --- | --- |
| Copy for each linking state | Copy only; AD-3 and AD-12 fix the wire contract |
| Exact relay batch size and backoff curve | Tuning; AD-17 fixes the behaviour |
| CloudFront caching for static assets | Performance only; caching stays disabled until then |
| Database-level append-only `AuditLogEntries` and `StoredEvents` (a separate migration role) | Production-path note; AD-14 enforces insert only in the application |
| Order across disputes: Kafka offsets, a high-water mark with `pg_snapshot_xmin`, or change data capture | `docs/production-path.md`. Nothing reads across streams in v1; order is per dispute by `Version` (AD-17, ad-009). The production path is written for a large retail bank with tens of millions of clients, and its scale figures are labelled estimates |
| Crypto-shredding of personal data in stored events (a per-customer key, destroyed on erasure) | `docs/production-path.md` (ad-009). In v1 stored events are kept under NFR-DATA-7 with the same access as `Disputes` (AD-22) |
| Stored event schema evolution beyond additive changes (upcasting) | Production-path note; AD-14a allows only additive changes within a type, and a new shape is a new type |
| Snapshots at intervals to shorten replay | Not needed: streams hold a handful of stored events in v1 (an estimate: 15 to 100 once internal work is recorded) |
| Operations tool credentials | `docs/production-path.md`; in v1 a script gets the token and a person pastes it into Scalar, which holds no credential; real tools keep their own credentials in a secret store and authenticate with `private_key_jwt` (RFC 7523) |
| Shared rate-limit state, several Api and Worker instances | Out of scope (prd §6.2) |
| An event consumer and its inbox table | Out of scope; the contract requires dedupe |
| Fencing a relay leader with Kafka transactions (a fixed `transactional.id`, KIP-98) | `docs/production-path.md`; v1 is at-least-once and consumers keep per-dispute order by `disputeVersion` (AD-17) |
| Kafka broker authentication and TLS (SASL/SCRAM or mTLS) | `docs/production-path.md`; v1 runs plaintext on the private Compose network as a `// shortcut:` (AD-26) |
| Outbox cleanup, topic retention, retention and de-identification jobs | Production-path notes |
| Seeded snapshots after a day boundary without `down -v` | Demo limitation; the README says to reset with `docker compose down -v` |
| CloudFront prefix-list weight against security-group quotas | Checked by the AWS story once the account exists |
| Node 26 LTS move | Revisit after 2026-10-28; `.nvmrc` stays on 24 until a story bumps it |
| README runbook for failed outbox rows; the decision records | Settled in v1: stories 7.7 (`infra/deploy/runbook.md`, the release of a `Failed` row, AD-22's operator exception) and 7.6 (the records in `docs/decisions/`) |
| Browser telemetry intake in AWS | `docs/production-path.md`; in v1 browser spans are local only (AD-29) |

## Open questions

- The new AWS free-plan account doesn't exist yet, so the RDS version, CloudFront verification, instance eligibility and the memory budget are unchecked.
- AD-15 gives `Disputes` no concurrency token, because the stream's unique `(DisputeId, Version)` catches every concurrent change and every `Disputes` write commits with a stored event. The `xmin` token was settled on 2026-10-26; a human confirms that ad-009's concurrency rule governs.