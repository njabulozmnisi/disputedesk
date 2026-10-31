---
id: 13
type: story
title: "Backend hardening from the readiness review"
parent: epic-7-release
covers: [R2, R10, R11, R26, R27, R36]
after: [4]
hitl: false
risk: medium
refined: false
---

# Backend hardening from the readiness review

## Description

Fixes the seven backend findings of the production readiness review that the human disposed as "fix, story 7.13" on 2026-10-31 (`docs/verification/7.10-prr.md`, Disposition): A-F3, C-F8, E-F12, F-F5, F-F8, F-F9 and F-F11. The OIDC handler's correlation and nonce cookies get `__Host-` names in AWS; the BFF accepts ID tokens signed only with RS256; the core-banking and SIM-swap clients stop following redirects; refusal and sign-out audit entries are saved even when the client disconnects, within a bound, and a failed sign-out audit never keeps a session alive; two named tests close evidence gaps (the Api finishing an in-flight request on shutdown, and two Api instances starting together on an empty key store); and the raise rejection metric also counts the idempotency answers. F-F9 adds a test only: if it fails, the builder stops and reports `blocked` with the evidence, and the fix is a human decision; no custom key-refresh mechanism is written.

## Requirements

Verify: the Api integration tests named below pass in `backend/` (Testcontainers, under `scripts/test-lock.sh run testcontainers`), with the full backend suite, `dotnet format --verify-no-changes` and the build's warnings-as-errors; no new suppression; `openapi.json` unchanged.

Inputs (configuration, not request inputs):
- `Bff:CorrelationCookie:Name` and `Bff:NonceCookie:Name`: required, validated on start like the existing cookie names (`CookieNameOptions`); `appsettings.json` holds `__Host-disputedesk-correlation.` and `__Host-disputedesk-nonce.`; local Compose overrides them with the same names without the prefix, as it does the session cookie (AD-24).
- The audit save bound: 5 seconds, a constant, not configuration.

1. **The OIDC correlation and nonce cookies carry the `__Host-` prefix in AWS** (R2; ASVS 5.0.0 V3.3.1, V3.3.3)
   **Given** the AWS cookie settings (as `SignInTests.Callback_AwsCookieSettingsOverHttps_SetsASecureHostPrefixedCookie` sets them) and the BFF's `OpenIdConnectOptions.CorrelationCookie` and `NonceCookie` named from configuration with `Path` `/` (Microsoft Learn, `RemoteAuthenticationOptions.CorrelationCookie`, `OpenIdConnectOptions.NonceCookie`)
   **When** an anonymous browser calls the login endpoint over HTTPS and then completes the callback
   **Then** every `Set-Cookie` on the login response and the callback response, the transient cookies and their deletions included, starts with `__Host-`, carries `Secure` and `Path=/` and no `Domain` (RFC 6265bis, cookie name prefixes), and sign-in completes
   **And** the local settings keep sign-in working over `http://localhost` (the existing sign-in tests pass unchanged)

2. **The BFF accepts ID tokens signed only with RS256** (R2; ASVS 5.0.0 V9.1.2)
   **Given** `TokenValidationParameters.ValidAlgorithms` set to RS256 on the OIDC handler, as the operations bearer handler already sets it (Microsoft Learn, `TokenValidationParameters.ValidAlgorithms`; OpenID Connect Core 1.0 §3.1.3.7, RS256 the default ID-token algorithm), matching Zitadel's discovery `id_token_signing_alg_values_supported`
   **When** the fake identity provider returns an ID token that is unsigned (`alg` `none`), signed HS256 with the client secret, or signed RS384 or PS256 with the provider's own published key
   **Then** each callback redirects to `/?signIn=failed` with no session row, no session cookie and no `SignIn` audit entry, and an RS256 token still signs in

3. **The banking clients never follow a redirect** (R11, R5; ASVS 5.0.0 V15.3.2)
   **Given** the `ICoreBankingClient` and `ISimSwapCheck` typed clients with a primary handler whose `AllowAutoRedirect` is false (Microsoft Learn, `IHttpClientFactory` `ConfigurePrimaryHttpMessageHandler`; `SocketsHttpHandler.AllowAutoRedirect`)
   **When** the fake core-banking server answers a transaction read, a customer match (POST with a body) and a SIM-swap check with 302 and with 307, each `Location` pointing at a second recording server, one on another origin
   **Then** the second server records no request, so no customer ID header, account reference or request body reaches it; the transaction read and the match return core banking unavailable, and the SIM-swap check fails closed, each as for any other non-success answer today

4. **A refusal's audit entry is saved even if the client disconnects** (R10; NFR-SEC-10, P38)
   **Given** the saves that write a `Denied` or `Rejected` audit entry (the operations authorisation refusal, the raise, withdraw and operations-action refusals, the operations read refusal), each saved with its own token bounded at 5 seconds and independent of `HttpContext.RequestAborted` (Microsoft Learn, *Cancellation in managed threads*, `CancellationTokenSource` with a delay; OWASP Logging Cheat Sheet)
   **When** a test cancels the request's token at the moment the entry is saved (an EF Core `SaveChangesInterceptor` registered by the test; Microsoft Learn, *EF Core interceptors*), for an operations call without the role and for a raise on an already-disputed transaction
   **Then** each audit entry is committed with its usual fields, and a save that takes longer than 5 seconds is cancelled by the bound
   **And** a successful dispute change still writes no audit entry, and its save still uses the request's token (unchanged)

5. **A sign-out is audited even if the client disconnects, and never kept alive by a failed audit** (R10, R3; NFR-SEC-10, R41)
   **Given** the sign-out endpoint, with the `SignOut` entry saved under the same 5-second bound, independent of the request's token
   **When** the request's token is cancelled at the save, and, separately, the save fails or exceeds the bound
   **Then** in the first case the session row is gone, the browser gets the sign-out redirect and the `SignOut` entry is committed
   **And** in the second case the session row is still gone, the browser still gets the sign-out redirect, and the existing error log line is written with its trace ID and no login sub, email or token (OWASP Session Management Cheat Sheet: logout invalidates the session; NFR-OBS-2)

6. **The Api finishes an in-flight request when it shuts down** (R27; NFR-RES-5, 7.10 criterion 4 shutdown row)
   **Given** the Api hosted on Kestrel in a named test (not the in-memory test server, so the host's graceful shutdown applies; Microsoft Learn, *.NET Generic Host*, host shutdown and `HostOptions.ShutdownTimeout`, default 30 seconds), with the fake core-banking server holding a raise's transaction read for 2 seconds
   **When** the host is asked to stop while that raise is in flight
   **Then** the raise answers 201, its `Disputes` row, stored event and outbox row are committed, and the host's stop completes within 30 seconds

7. **Two Api instances starting together on an empty key store accept each other's cookies** (R26; 7.10 criterion 4 key-ring row)
   **Given** a named test, `SignInTests.Session_TwoInstancesStartingTogetherOnAnEmptyKeyStore_AcceptEachOthersCookie`, that creates its own database with an empty `DataProtectionKeys` table (never truncating the shared one) and starts two Api instances on it at once
   **When** both sign a browser in concurrently, so both create the key ring at the same time, and each browser then reads its session through the other instance; the arrangement repeats 5 times, each on a new database
   **Then** every read answers its state (`unlinked`) through the other instance, checked against Microsoft Learn's Data Protection key management (key creation with immediate activation on an empty ring, the shared repository and the shared application name)
   **And** `Session_CookieFromOneApiInstance_IsAcceptedByAnother` is unchanged; if the new test fails, the builder reports `blocked` with the failing runs, and no key-refresh code is written

8. **The raise rejection metric counts the idempotency answers once** (R36, R15; NFR-OBS-3)
   **Given** `disputedesk.raise.rejections` by `status`
   **When** a raise is refused by the handler (409 already disputed, 422 not disputable), answered 422 `idempotency-mismatch`, answered 409 `idempotency-in-progress`, and when a stored 409 or 422 is replayed under its key
   **Then** each first answer adds exactly one count with its status, and a replay adds none, because its rejection was counted when first answered (`RaiseMetricsTests` extended with the mismatch, in-progress and replay cases)

## Boundaries

- Must not change: the session and antiforgery cookies (names, flags, AD-24); the PKCE code flow; the operations bearer validation; the resilience policies (AD-21); the audit entry fields (AD-14a); what the sign-out endpoint returns; the Data Protection registration, key storage and application name (story 7.4); the existing `Session_CookieFromOneApiInstance_IsAcceptedByAnother` test.
- No custom key-refresh or key-ring code (F-F9 decision); no audit outbox, queue or retry loop; no new metric tag; no new package; no migration.
- Out of scope (production path or accepted): actor-bound limits (A-F2), the GET login ending a session (A-F7, accepted), token-type checks on bearer tokens (C-F9), the banking stub's own authentication (C-F7).

## Supervisor checks

Deliberate-break proofs, run by the supervisor in a throwaway copy, not by the verifier.

- Remove the correlation cookie's `Path` setting: criterion 1's test fails.
- Remove `ValidAlgorithms` from the OIDC handler: criterion 2's HS256 or RS384 case fails.
- Remove `AllowAutoRedirect = false` from the SIM-swap client: criterion 3's test fails.
- Pass `RequestAborted` to the `Denied` save again: criterion 4's test fails.
- Count rejections in the handler path only, as before: criterion 8's mismatch and in-progress cases fail.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes (authentication cookies, ID-token validation, the audit log).
- Lane: B.
- Mechanisms and sources: OIDC handler cookies (<https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.remoteauthenticationoptions.correlationcookie>, <https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.openidconnect.openidconnectoptions.noncecookie>); cookie name prefixes (RFC 6265bis, <https://datatracker.ietf.org/doc/draft-ietf-httpbis-rfc6265bis/>); `TokenValidationParameters.ValidAlgorithms` (<https://learn.microsoft.com/dotnet/api/microsoft.identitymodel.tokens.tokenvalidationparameters.validalgorithms>); OpenID Connect Core 1.0 §3.1.3.7 (<https://openid.net/specs/openid-connect-core-1_0.html>); `IHttpClientFactory` primary handler (<https://learn.microsoft.com/aspnet/core/fundamentals/http-requests>), `SocketsHttpHandler.AllowAutoRedirect` (<https://learn.microsoft.com/dotnet/api/system.net.http.socketshttphandler.allowautoredirect>); cancellation (<https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads>); EF Core interceptors (<https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors>); Generic Host shutdown (<https://learn.microsoft.com/dotnet/core/extensions/generic-host#host-shutdown>); Data Protection key management (<https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-management?view=aspnetcore-10.0>) and configuration (<https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0>); OWASP Logging and Session Management Cheat Sheets; OWASP ASVS 5.0.0 V3.3, V9.1, V15.3 (<https://github.com/OWASP/ASVS>).
- Packages: none.
- Touches:
  - `backend/src/DisputeDesk.Api/Bff/BffAuthentication.cs`, `BffOptions.cs` (criteria 1, 2), `backend/src/DisputeDesk.Api/appsettings.json` (criterion 1), `docker-compose.yml` (the two local cookie-name overrides only)
  - `backend/src/DisputeDesk.Infrastructure/CoreBanking/CoreBankingRegistration.cs` (criterion 3)
  - `backend/src/DisputeDesk.Api/Authorization/AuthorizationResultHandler.cs`, `backend/src/DisputeDesk.Api/Bff/BffEndpoints.cs`, and the refusal saves in `backend/src/DisputeDesk.Application/` (raise, withdraw, operations action, operations read) (criteria 4, 5)
  - `backend/src/DisputeDesk.Api/Disputes/Idempotency/IdempotencyFilter.cs`, `backend/src/DisputeDesk.Api/Disputes/DisputeEndpoints.cs`, `RaiseMetrics.cs` (criterion 8)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/` (`Bff/SignInTests.cs`, `Bff/SignOutTests.cs`, `CoreBanking/`, `Authorization/`, `Disputes/RaiseMetricsTests.cs`, `Infrastructure/FakeZitadel.cs`, and a new shutdown test)

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- verification — docs/verification/7.10-prr.md, Findings A-F3, C-F8, E-F12, F-F5, F-F8, F-F9, F-F11, the Evidence stack section, and Disposition
- architecture — docs/architecture/architecture.md, AD-7, AD-14a, AD-21, AD-24
- prd — docs/prd/prd.md, NFR-SEC-1, NFR-SEC-10, NFR-RES-5, NFR-OBS-2, NFR-OBS-3
- production path — docs/production-path.md, P38
- story — docs/tickets/epic-7-release/tickets/story-7.4-aws-compose-override-and-opentelemetry-collector.md, criterion 8

## Notes

- Risk medium (proposed): a wrong cookie or algorithm setting stops every sign-in, and a wrong audit change loses refusal records; nothing here serves real customers, and the CI journeys sign in end to end.
- Decision: 2026-10-31 (human, 7.10 disposition) — A-F3, C-F8, E-F12, F-F5, F-F8, F-F9 and F-F11 are fixed in this story; F-F9 is a named test of two Api instances starting together on an empty key store exchanging a cookie, checked against Microsoft Learn's Data Protection key management, with the existing test never loosened and no custom key-refresh mechanism.
- Decision: 2026-10-31 (Sofia, 7.13 write) — the transient cookie names come from configuration, beside the session and antiforgery names (AD-24's pattern: `__Host-` in `appsettings.json`, unprefixed locally), so local sign-in over `http://localhost` does not depend on how a browser treats prefixed cookies there.
- Decision: 2026-10-31 (Sofia, 7.13 write) — the rejection metric counts responses a customer receives for the first time; a replayed stored 409 or 422 is the same rejection and is not counted again (NFR-OBS-3 counts rejections on raising, not retries).
- Decision: 2026-10-31 (Sofia, 7.13 write) — a failed sign-out audit keeps today's strategy (the session ends, the failure is logged with its trace ID and exported through the log pipeline); the fix is that a disconnect can no longer cancel the save, and the save is bounded at 5 seconds.
- Collision: touches `docker-compose.yml` (two local cookie-name lines) and backend code only; it does not touch `PlatformScriptTests.cs`, `docker-compose.aws.yml` or anything under `infra/`, so it may build beside 7.12 and 7.14.
- Production path: the docs-only production-path change drops P38's audit-cancellation sentence once this story merges.
