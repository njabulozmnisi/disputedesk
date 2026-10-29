---
id: 8
type: story
title: "Sign-in and sign-out are audited, and every endpoint is denied by default"
parent: epic-2-transactions
covers: [R10, R41, R3, R9, R30, R31, R35]
after: [2, 3]
hitl: false
risk: high
---

# Sign-in and sign-out are audited, and every endpoint is denied by default

## Description

Every sign-in and sign-out writes an audit entry (`SignIn`, `SignOut`) through one new Application method, `AuditLog.Record`, using the existing audit fields only (AD-14a, R41, NFR-SEC-10). Sign-out goes through the OpenID Connect handler's built-in `SignOutAsync` (AD-7). A deny-by-default fallback authorization policy requires an authenticated user on every endpoint that names no policy (AD-10). The security and error-shape tests also run under the Production environment. The whole-call deadline on core-banking calls is story 2.1's `TotalRequestTimeout` (AD-21), not this story's. No migration.

## Requirements

Verify: Integration tests for the sign-in and sign-out audit entries and their failure paths, the sign-in log capture, the end-session redirect and signed-out callback, the endpoint enumeration in each environment, the isolated fallback probe and the unmatched-route cases pass; the security and error-shape tests pass in both the Development and Production environments; `openapi.json` has no diff.

Request inputs: none new. `GET /api/v1/bff/login` keeps `returnUrl` (at most 2,048 characters, local paths only) and `POST /api/v1/bff/logout` keeps the `__RequestVerificationToken` form field (story 2.2).

1. **One method writes every audit entry** (R41, R10; NFR-SEC-10)
   **Given** `AuditLog.Record(...)` in `DisputeDesk.Application/Audit/`
   **When** it is called with an action, a result, an actor, the caller's instant and optional customer ID and dispute ID
   **Then** it adds one `AuditLogEntries` row with exactly the existing fields (`Id` as a UUIDv7 from the instant, `OccurredAtUtc`, `Action`, `Result`, `ActorType`, `ActorId`, `CustomerId`, `DisputeId`, `TraceId` from the current activity), never reads the clock itself (AD-4), and needs no migration (AD-14, AD-14a)
   **And** `AuditAction` gains `SignIn` and `SignOut`, stored as their member names (AD-22)

2. **Sign-in is audited** (R41, R10; NFR-SEC-10, FR-2)
   **Given** a login completing the code flow at `/api/signin-oidc`
   **When** the OpenID Connect handler receives the ticket (`OnTicketReceived`, which `OpenIdConnectEvents` inherits from `RemoteAuthenticationEvents`, Microsoft Learn), after `OnTokenValidated` has accepted it and before the cookie handler writes the session
   **Then** one `SignIn` entry with result `Succeeded` is written through `AuditLog.Record` and saved, where `Succeeded` means the code flow completed and the handler accepted the ticket (the session is written after it)
   **And** the actor type is `Customer` and the actor ID is the login's `sub` for every login; `CustomerId` is the customer ID that `CustomerLinks` holds for that `sub`, found by one query at sign-in, and null when the login is unlinked (the ticket holds no customer ID, AD-10); a test signs in one linked and one unlinked login and asserts all three fields of each entry
   **And** a callback that fails (`OnRemoteFailure`: the unverified-email and wrong-client-secret callbacks) writes no `SignIn` entry, and a session renewed by an authenticated request writes no further one

3. **A failed sign-in audit stops the sign-in** (R41; NFR-SEC-10)
   **Given** a test-only interceptor that fails the `AuditLogEntries` insert
   **When** a login completes the code flow
   **Then** the callback answers the 500 ProblemDetails, no `Sessions` row is written and no session cookie is set, so no session exists without its `SignIn` entry (OWASP Logging Cheat Sheet: record authentication events)
   **And** the reverse is not promised: if the session write fails after the entry is saved, the entry stands without a session (stated under Boundaries)

4. **No token or email in the sign-in logs** (R31; NFR-SEC-5)
   **Given** the test factory's log capture (`TelemetryCapture`, the OpenTelemetry in-memory log exporter, every category at the factory's levels) over exactly three callback requests: a verified login, an unverified-email login and a wrong-client-secret login (the last two reach `OnRemoteFailure`)
   **When** each callback completes
   **Then** no captured record's formatted message or attribute value holds that login's ID token, access token or email address, and neither does any `AuditLogEntries` row (OWASP Logging Cheat Sheet: data to exclude)
   **And** the `OnRemoteFailure` warning passes this assertion; where `Failure.Message` would carry any of them, the warning logs only what the test proves safe

5. **Sign-out through the OIDC handler, audited** (R3, R41; FR-3, NFR-SEC-10)
   **Given** a signed-in login
   **When** it posts `POST /api/v1/bff/logout`
   **Then** the endpoint, in this order: signs out of the cookie scheme, which deletes the session row, and deletes the antiforgery cookie; records one `SignOut` entry with result `Succeeded`, actor type `Customer`, actor ID the login's `sub` and `CustomerId` as in criterion 2, through `AuditLog.Record` (a failed save is logged and does not stop the sign-out, criterion 6); then calls `SignOutAsync` on the OIDC scheme, whose handler adds `id_token_hint` from the session's saved ID token, `client_id`, `state` and `post_logout_redirect_uri` itself (Microsoft Learn, *Configure OpenID Connect web authentication*; AD-7)
   **And** the hand-built end-session query and the `ConfigurationManager` read in the logout endpoint are deleted; the handler builds the end-session request from discovery
   **And** a test follows the actual redirect: it goes to the fake's `end_session_endpoint` with `id_token_hint` equal to the ID token the fake issued at sign-in, `client_id`, a `state`, and `post_logout_redirect_uri` = the app origin's `/api/signout-callback-oidc` (the handler's `SignedOutCallbackPath`); a request to that callback with the returned `state` lands the browser on `/` (OpenID Connect RP-Initiated Logout 1.0, sections 2 and 3; Microsoft Learn, *Configure OpenID Connect web authentication in ASP.NET Core*, signed-out callback path)
   **And** the 2.2 sign-out behaviour holds: the session row is deleted, both cookies are cleared, a replayed old cookie gets 401, and an anonymous caller is redirected to end-session with a `state` and no `id_token_hint` and writes no audit entry

6. **Sign-out failures** (R3, R41; FR-3)
   **Given** the three failures below, each made by a test
   **When** a signed-in login posts `POST /api/v1/bff/logout`
   **Then** a failed `SignOut` audit save (the test-only interceptor, failing only the `AuditLogEntries` insert) does not stop the sign-out: the session row is deleted, both cookies are cleared and the end-session redirect follows, and the failure is logged at Error with no token, access token or email (AD-7; OWASP Session Management Cheat Sheet: logout ends the session)
   **And** a failed session-row deletion answers the 500 ProblemDetails, deletes no cookie and writes no `SignOut` entry, so the login stays signed in and can retry (the session row is deleted before the `SignOut` entry is recorded)
   **And** failed discovery at the OIDC sign-out (`FakeZitadel.DiscoveryFails`), after the local session has ended, answers the 500 ProblemDetails; the session row is deleted, so the old cookie gets 401, and the Zitadel session outlives it
   **And** nothing is promised atomic across the database and Zitadel (stated under Boundaries)

7. **Deny by default, explicit metadata on every endpoint** (R9; NFR-SEC-3)
   **Given** `AuthorizationOptions.FallbackPolicy` set to a policy requiring an authenticated user (Microsoft Learn, *Policy-based authorization*: default and fallback policies; *Configure OpenID Connect web authentication*: force authorization and opt out at public endpoints)
   **When** a test reads the app's `EndpointDataSource` and enumerates every mapped endpoint, once in Development and once in Production, with no test-only endpoint mapped
   **Then** in both environments only `login`, `session` and `logout` under `/api/v1/bff`, `/health/live` and `/health/ready` carry `AllowAnonymous()`, and in Development also the OpenAPI document and Scalar, which `Program.cs` maps only there; the Production list holds neither (asserted by name in each environment)
   **And** every other endpoint names its policy (`Linked`, or the default policy on `antiforgery`), and the test fails on an endpoint with neither (OWASP Authorization Cheat Sheet: deny by default). The enumeration is a test; the fallback policy is the enforcement

8. **The fallback policy answers an endpoint that names none** (R9; NFR-SEC-3)
   **Given** a separate test whose own factory alone maps one test-only routed endpoint with no authorization metadata
   **When** an anonymous caller requests it
   **Then** it answers 401 `unauthorized` ProblemDetails, and a signed-in caller gets its 200; criterion 7's enumeration never sees this endpoint

9. **Unmatched routes** (R9, R30; NFR-SEC-3, NFR-API-2)
   **Given** `GET /api/v1/no-such-route`
   **When** an anonymous caller requests it
   **Then** it answers 401 `unauthorized` ProblemDetails with a `traceId`, `Cache-Control: no-store` and no gateway headers, because the authorization middleware applies the fallback policy to a request with no endpoint (Microsoft Learn, *Policy-based authorization*: default and fallback policies), which also keeps route existence hidden (OWASP Authorization Cheat Sheet: deny by default)
   **And** a signed-in caller gets 404 `not-found` ProblemDetails with a `traceId`, `Cache-Control: no-store` and no gateway headers
   **And** only these existing tests change, each by signing its caller in and keeping every assertion, plus one new anonymous 401 case beside each: `ProblemDetailsTests.UnknownRoute_Get_Returns404ProblemDetailsWithTraceId`, `ProblemDetailsTests.UnknownRoute_Get_TraceIdMatchesServerSpan`, the `/api/v1/no-such-route` and `ForbidProbe` rows of `ProblemDetailsTests.FrameworkResponse_EachStatus_CarriesItsSlug`, and the `/api/v1/no-such-route` and `ThrowProbe` rows of `SecurityHeadersTests.UseSecurityHeaders_AnyResponse_LeavesGatewayHeadersToTheGatewayAndKeepsApiNoStore`. The probes are middleware after authorization on paths with no endpoint, so an anonymous caller now gets 401 before reaching them. No other assertion changes

10. **Security and error-shape tests in the Production environment** (R35, R30; NFR-TEST-3, NFR-API-2)
    **Given** the security-headers, ProblemDetails, transactions-authorization, raise-authorization and endpoint-enumeration integration tests
    **When** they run
    **Then** each runs once with the `WebApplicationFactory` environment set to Development and once set to Production (Microsoft Learn, *Integration tests in ASP.NET Core*), and passes in both, so no Development-only behaviour hides a failure

## Boundaries

- No migration, no new audit column, no new endpoint, no response shape change; `openapi.json` does not change.
- Linking, dispute refusals and operations reads are audited by their own stories (6.1 to 6.3, 3.4, 5.1, 5.2); each uses `AuditLog.Record`. The source-rules test that only `AuditLog.Record` creates an audit entry is in story 3.4, the first story built after both 2.8 and 3.8.
- The core-banking whole-call deadline is 2.1's `TotalRequestTimeout` (AD-21).
- `AllowedHosts` is story 6.3's.
- Failed sign-in callbacks are logged (criterion 4) but not audited; only a completed code flow writes `SignIn`.
- Nothing is atomic across the database and Zitadel: a `SignIn` entry can stand if the session write after it fails, a sign-out whose audit save fails completes without its `SignOut` entry (logged as an error), and the Zitadel session can outlive a local sign-out whose discovery fails (criteria 3 and 6).
- `DisputesApi.cs` and its helper signatures are not touched; 3.8 keeps them stable for `RaiseDisputeAuthorizationTests.cs`.
- Files of story 3.8 are not touched (collision check): `DisputeDesk.Domain/`, `DisputeDesk.Infrastructure/Persistence/`, `Application/Disputes/`, `Contracts/Events/`, `Contracts/Json/`, `contracts/events/`, and the dispute test files other than `RaiseDisputeAuthorizationTests.cs`.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Browser sign-in then sign-out for login A through the gateway: the browser lands on `/`, Zitadel shows no session, the end-session request carries `state`, and `qa_readonly` shows one `SignIn` and one `SignOut` row for A's `sub`.
- In a throwaway copy, remove `AllowAnonymous()` from `/health/live`: the enumeration test fails.
- In a throwaway copy, remove the fallback policy: the criterion 8 probe test and the anonymous unmatched-route 401 cases fail.
- In a throwaway copy, drop the `OnRedirectToIdentityProviderForSignOut` hint: the end-session test fails on `id_token_hint`.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes (auth).
- Lane: M1 (parallel with 3.8 on M2; collision check below). First story of the worker-tmux trial; 4.1 follows it on M1.
- Touches:
  - `backend/src/DisputeDesk.Application/Audit/AuditLog.cs` (new)
  - `backend/src/DisputeDesk.Contracts/Audit/AuditAction.cs`
  - `backend/src/DisputeDesk.Api/Bff/BffAuthentication.cs` (fallback policy, `OnTicketReceived`, `OnRedirectToIdentityProviderForSignOut`, the `OnRemoteFailure` log if criterion 4 needs it)
  - `backend/src/DisputeDesk.Api/Bff/BffEndpoints.cs` (`SignOutAsync` on the OIDC scheme, the hand-built end-session query deleted, `AllowAnonymous()`)
  - `backend/src/DisputeDesk.Api/Health/HealthEndpoints.cs` (`AllowAnonymous()`)
  - `backend/src/DisputeDesk.Api/Program.cs` (`AllowAnonymous()` on the OpenAPI document and Scalar)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Bff/SignInTests.cs`, `Bff/SignOutTests.cs`, `Infrastructure/FakeZitadel.cs` (end-session `state`, if needed), `Infrastructure/ApiFactory.cs` (only if the Production runs need a setting), `Infrastructure/Browser.cs` (only if a signed-in helper is needed), a new `Infrastructure/FailAuditInsertInterceptor.cs`, a new `Infrastructure/FallbackProbe.cs` (criterion 8), `Infrastructure/TelemetryCapture.cs` used unchanged, a new `Authorization/` endpoint-enumeration test file, `SecurityHeadersTests.cs`, `ProblemDetailsTests.cs`, `Transactions/TransactionsAuthorizationTests.cs`, `Disputes/RaiseDisputeAuthorizationTests.cs`
  - `backend/tests/DisputeDesk.Api.IntegrationTests/ApiReferenceTests.cs` (Production rows signed in, plus the anonymous 401 case), `CoreBanking/CoreBankingStubFactory.cs` and a new `CoreBanking/CoreBankingStubStartTests.cs` (the test host sets its port in Kestrel's options; fix round 1)
- Not touched: `backend/openapi.json` (no diff).

## References

- parent — docs/tickets/epic-2-transactions/epic-2-transactions.md
- architecture — docs/architecture/architecture.md, AD-4, AD-7, AD-10, AD-14, AD-14a, AD-21, AD-22
- Microsoft Learn — https://learn.microsoft.com/aspnet/core/security/authorization/policies
- Microsoft Learn — https://learn.microsoft.com/aspnet/core/security/authentication/configure-oidc-web-authentication
- Microsoft Learn — https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.remoteauthenticationevents.onticketreceived
- Microsoft Learn — https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.authenticationproperties.parameters
- Microsoft Learn — https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.openidconnect.openidconnectevents.onredirecttoidentityproviderforsignout
- Microsoft Learn — https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.openidconnect.openidconnectoptions.signedoutcallbackpath
- Microsoft Learn — https://learn.microsoft.com/aspnet/core/test/integration-tests
- OWASP — https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html
- OpenID — https://openid.net/specs/openid-connect-rpinitiated-1_0.html
- OWASP — https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html

## Notes

- Risk high: authorisation and sign-out; beyond the criteria, the security agent re-checks every endpoint's authorisation state in both environments.
- Decision: 2026-10-29 — sign-in and sign-out audit, sign-out through the OIDC handler, the fallback policy and the Production-environment tests are this story's (initiative Notes; AD-7, AD-10, AD-14a).
- Assumption: `SignIn` is recorded in `OnTicketReceived`, which runs only when the code flow completes, so a session renewal or the session re-issue after linking (6.2) is not a sign-in; `Succeeded` means the ticket was accepted, not that the session was written (criterion 2).
- Assumption: the `SignIn` entry's `CustomerId` is the linked customer's ID when `CustomerLinks` holds the login, else null; the actor type is `Customer` for every login (AD-14a).
- Decision: 2026-10-29 — an anonymous request that matches no endpoint gets 401 under the fallback policy and a signed-in one gets 404; the affected tests sign their caller in and keep their assertions (criterion 9; challenger `[story]` point, `docs/verification/2.8-challenge.md`).
- AD-7's end-session flow is the handler's: `post_logout_redirect_uri` = `/api/signout-callback-oidc`, which lands the browser on `/` (Microsoft Learn, `SignedOutCallbackPath`); Zitadel already registers both URIs (`infra/zitadel/provision/provision.sh`). The e2e expectation in `e2e/tests/bff-sign-in-and-out.spec.ts` follows it; QA owns `e2e/` and updates it in this story's QA stage.
- Input (from 2.5 review N2, 2026-10-28): in `SecurityHeadersTests.cs`, which this story edits, rename `isApiResponse` to `isUnderApiPath`.
