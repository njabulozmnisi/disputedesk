---
id: 13
type: story
title: "Backend hardening from the readiness review"
parent: epic-7-release
covers: [R2, R3, R5, R10, R11, R15, R26, R27, R36, R41]
after: [4]
hitl: false
risk: medium
refined: true
---

# Backend hardening from the readiness review

## Description

Fixes the seven backend findings of the production readiness review that the human disposed as "fix, story 7.13" on 2026-10-31 (`docs/verification/7.10-prr.md`, Disposition): A-F3, C-F8, E-F12, F-F5, F-F8, F-F9 and F-F11. The OIDC handler's correlation and nonce cookies take their names from configuration, `__Host-` in AWS (AD-24 as amended on 2026-10-31); the BFF accepts ID tokens signed only with RS256; the core-banking and SIM-swap clients stop following redirects; refusal and sign-out audit entries are saved with `CancellationToken.None`, as linking already saves its audit entries, so a client disconnect can no longer cancel them, and a failed sign-out audit never keeps a session alive; two named tests close evidence gaps (the Api finishing an in-flight request on shutdown, and two Api instances starting together on an empty key store); and the raise rejection metric also counts the idempotency answers. F-F9 adds a test only: if it fails, the builder stops and reports `blocked` with the evidence, and the fix is a human decision; no custom key-refresh mechanism is written.

## Requirements

Verify: the Api integration tests named below pass in `backend/` (Testcontainers, under `scripts/test-lock.sh run testcontainers`), with the full backend suite, `dotnet format --verify-no-changes` and the build's warnings-as-errors; no new suppression; `openapi.json` unchanged.

Inputs (configuration, not request inputs):
- `Bff:CorrelationCookie:Name` and `Bff:NonceCookie:Name`: required, non-empty, validated on start by the existing `CookieNameOptions` (the same `[Required]` check as `Bff:SessionCookie:Name`); `appsettings.json` holds `__Host-disputedesk-correlation.` and `__Host-disputedesk-nonce.`; local Compose and the test defaults (`ApiFactory.DefaultSettings`) hold the same names without the prefix, as they do the session cookie (AD-24, amended).
- No audit save bound is added: the saves use `CancellationToken.None`, and the database command timeout (Npgsql's default, 30 seconds) bounds them, as it bounds linking's audit saves today (Decision below).

1. **The OIDC correlation and nonce cookies carry the `__Host-` prefix in AWS and keep Secure and SameSite=None everywhere** (R2; ASVS 5.0.0 V3.3.1, V3.3.3)
   **Given** the BFF's `OpenIdConnectOptions.CorrelationCookie` and `NonceCookie` named from `Bff:CorrelationCookie:Name` and `Bff:NonceCookie:Name`, with `Path` `/`, `SecurePolicy` `Always` and `SameSite` `None` in every environment, not `Bff:Cookie:SecurePolicy` (Microsoft Learn, `RemoteAuthenticationOptions.CorrelationCookie`, `OpenIdConnectOptions.NonceCookie`, `CookieBuilder`; the `form_post` callback is cross-site, so SameSite=None is required)
   **When** an anonymous browser, with the AWS cookie settings (as `SignInTests.Callback_AwsCookieSettingsOverHttps_SetsASecureHostPrefixedCookie` sets them, plus the two new names), calls the login endpoint over HTTPS and then completes the callback
   **Then** every transient-cookie `Set-Cookie` on the login and callback responses, the deletions included, has a name starting `__Host-`, the `secure` attribute, `samesite=none`, no `Domain`, and a `Path` attribute parsed to exactly `/` (not a substring match: the handler's default is `/api/signin-oidc`) (RFC 6265bis, cookie name prefixes), and sign-in completes
   **And** with the test defaults the same cookies carry the unprefixed names with `secure`, `samesite=none` and `Path=/`, the existing sign-in tests pass unchanged, and an Api started without either name refuses to start, as for a missing session cookie name; the browser accepting these Secure cookies over `http://localhost` is proved by the e2e sign-in journeys on `http://localhost:8080`, as it is for the handler defaults today

2. **The BFF accepts ID tokens signed only with RS256** (R2; ASVS 5.0.0 V9.1.2)
   **Given** `TokenValidationParameters.ValidAlgorithms` set to RS256 on the OIDC handler, as the operations bearer handler already sets it (Microsoft Learn, `TokenValidationParameters.ValidAlgorithms`; OpenID Connect Core 1.0 §3.1.3.7, RS256 the default ID-token algorithm)
   **When** the fake identity provider returns an ID token that is unsigned (`alg` `none`), signed HS256 with the client secret, or signed RS384 or PS256 with the provider's own published key
   **Then** each callback redirects to `/?signIn=failed` with no session row, no session cookie and no `SignIn` audit entry, and an RS256 token still signs in
   **And** the builder's report quotes `id_token_signing_alg_values_supported` from the pinned Zitadel's discovery document on the Compose stack, and it includes `RS256`

3. **The banking clients never follow a redirect** (R11, R5; ASVS 5.0.0 V15.3.2)
   **Given** the `ICoreBankingClient` and `ISimSwapCheck` typed clients registered with `UseSocketsHttpHandler((handler, _) => handler.AllowAutoRedirect = false)` (Microsoft Learn, `HttpClientBuilderExtensions.UseSocketsHttpHandler`, *IHttpClientFactory with .NET*; `SocketsHttpHandler.AllowAutoRedirect`)
   **When** the fake core-banking server answers a transaction read, a customer match (POST with a body) and a SIM-swap check with 302 and with 307, each `Location` pointing at a second recording server on another origin
   **Then** the second server records no request, so no customer ID header, account reference or request body reaches it; the transaction read and the match return core banking unavailable, and the SIM-swap check fails closed, each as for any other non-success answer today (a same-origin `Location` is not a separate case: with the redirect off no `Location` is followed, whatever its origin)

4. **A refusal's audit entry is saved even if the client disconnects** (R10; NFR-SEC-10, P38)
   **Given** the five saves that write a `Denied` or `Rejected` audit entry (the operations authorisation refusal, and the raise, withdraw, operations-action and operations-read refusals), each called with `CancellationToken.None` instead of the request's token, with a comment, as linking saves its audit entries (Microsoft Learn, *Cancellation in managed threads*; OWASP Logging Cheat Sheet)
   **When** a test `SaveChangesInterceptor` (Microsoft Learn, *EF Core interceptors*) holds the save in `SavingChangesAsync`, before the INSERT, until the client has cancelled the request and `HttpContext.RequestAborted` has fired, once for each of the five refusals: an operations call without the role, a raise on an already-disputed transaction, a withdraw of a dispute that cannot be withdrawn, an operations action not allowed in the dispute's status, and an operations read of an unknown dispute
   **Then** each audit entry is committed with its usual fields (AD-14a)
   **And** a refusal whose save fails still answers 500 as today, and a successful dispute change still writes no audit entry and still saves with the request's token

5. **A sign-out is audited even if the client disconnects, and never kept alive by a failed audit** (R10, R3, R41; NFR-SEC-10)
   **Given** the sign-out endpoint, with the `SignOut` entry saved with `CancellationToken.None`, as in criterion 4, and the existing catch in `RecordSignOutAsync` unchanged (with no token able to cancel the save, the only exceptions left are save failures, which it logs; Npgsql's command timeout is an `NpgsqlException`, not an `OperationCanceledException`)
   **When** the request's token is cancelled while the save is held (the criterion 4 interceptor), and, separately, the save throws (the interceptor throws a `DbException`, as a database error or Npgsql's command timeout does)
   **Then** in the first case the session row is gone and the `SignOut` entry is committed (the client has gone, so no response is asserted)
   **And** in the second case the session row is still gone, the response is the sign-out redirect to the identity provider's end-session endpoint with the session and antiforgery cookie deletions, and the existing error log line is written with its trace ID and no login sub, email or token (OWASP Session Management Cheat Sheet: logout invalidates the session; NFR-OBS-2)

6. **The Api finishes an in-flight request when it shuts down** (R27; NFR-RES-5, 7.10 criterion 4 shutdown row)
   **Given** a named test hosting the Api on Kestrel with `WebApplicationFactory<TEntryPoint>.UseKestrel()` (Microsoft Learn, `WebApplicationFactory<TEntryPoint>.UseKestrel`, .NET 10; not the in-memory test server, so the host's graceful shutdown applies; Microsoft Learn, *.NET Generic Host*, host shutdown and `HostOptions.ShutdownTimeout`, default 30 seconds), on the same database and key store as an in-memory `ApiFactory` instance through which a seeded linked customer signs in over `https://localhost`; the test sends that browser's session and antiforgery cookies and antiforgery header to the Kestrel Api over plain http; the fake core-banking server holds the raise's transaction read for 1.5 seconds (longer than the 1-second break below, shorter than the core-banking client's 2-second attempt timeout, AD-21, which would otherwise retry the read)
   **When** the Kestrel host is stopped (`IHost.StopAsync` or disposing the factory) while that raise is in flight
   **Then** the raise answers 201, its `Disputes` row, stored event and outbox row are committed, and the stop completes within 30 seconds

7. **Two Api instances starting together on an empty key store accept each other's cookies** (R26; 7.10 criterion 4 key-ring row)
   **Given** a named test, `SignInTests.Session_TwoInstancesStartingTogetherOnAnEmptyKeyStore_AcceptEachOthersCookie`, that creates its own database, asserts its `DataProtectionKeys` table is empty (never truncating the shared one), and starts two Api instances on it concurrently, so both hosts' Data Protection startup reaches the empty key ring at the same time
   **When** each instance signs a browser in and each browser then reads its session through the other instance; the arrangement repeats 5 times, each on a new database, and the test writes the key count after start for each run to the test output
   **Then** every read answers its state (`unlinked`) through the other instance, checked against Microsoft Learn's Data Protection key management (key creation with immediate activation on an empty ring, the shared repository and the shared application name)
   **And** `Session_CookieFromOneApiInstance_IsAcceptedByAnother` is unchanged; if the new test fails, the builder reports `blocked` with the failing runs, and no key-refresh code is written. This proves the Postgres key repository; AWS runs one instance with its key ring in SSM (story 7.4), so the race does not arise there

8. **The raise rejection metric counts each answer a customer receives, and a stored replay once** (R36, R15; NFR-OBS-3)
   **Given** `disputedesk.raise.rejections` by `status`
   **When** a raise is refused by the handler (409 already disputed, 422 not disputable), answered 422 `idempotency-mismatch`, answered 409 `idempotency-in-progress`, and when a stored 409 or 422 is replayed under its key
   **Then** each handler refusal, each mismatch and each in-progress answer adds exactly one count with its status, every time it is answered (a mismatch or in-progress answer is not stored, so each one is a new answer), and a replay adds none, because its rejection was counted when first answered (`RaiseMetricsTests` extended with the mismatch, in-progress and replay cases, the mismatch and in-progress cases sent twice to assert two counts)

## Boundaries

- Must not change: the session and antiforgery cookies (names, flags, AD-24); the PKCE code flow; the operations bearer validation; the resilience policies (AD-21); the audit entry fields (AD-14a); what the sign-out endpoint returns; the Data Protection registration, key storage and application name (story 7.4); the existing `Session_CookieFromOneApiInstance_IsAcceptedByAnother` test.
- The successful-path saves keep the request's token: a successful dispute change, a successful operations read (a cancelled read discloses nothing; the client gets no response), and the sign-in save in `OnTicketReceived` (a cancelled sign-in creates no session).
- No custom key-refresh or key-ring code (F-F9 decision); no audit outbox, queue or retry loop; no timeout constant or wrapper method around the audit saves; no new metric tag; no new package; no migration.
- Out of scope (production path or accepted): actor-bound limits (A-F2), the GET login ending a session (A-F7, accepted), token-type checks on bearer tokens (C-F9), the banking stub's own authentication (C-F7), a sign-out audit lost to a database failure (P38, accepted), and the OIDC and JWT bearer backchannels' redirects (Decision below).
- `architecture.md` (AD-24, AD-26) and `production-path.md` are amended in a separate docs change, not by the builder.

## Supervisor checks

Deliberate-break proofs, run by the supervisor in a throwaway copy, not by the verifier.

- Remove the correlation cookie's `Path` setting: criterion 1's test fails (exact `Path` parse).
- Set the transient cookies' `SameSite` to `Lax`: criterion 1's test fails.
- Remove `ValidAlgorithms` from the OIDC handler: criterion 2's RS384 and PS256 cases fail (unsigned and HS256 are refused already, so they do not prove the change).
- Remove `AllowAutoRedirect = false` from the SIM-swap client: criterion 3's test fails.
- Pass `RequestAborted` to the operations authorisation refusal save again: criterion 4's test fails.
- Pass `RequestAborted` to the `SignOut` save again: criterion 5's disconnect case fails.
- Set `HostOptions.ShutdownTimeout` to 1 second in the Kestrel test: criterion 6's held 1.5-second raise fails.
- Give the second instance a different Data Protection application name: criterion 7's test fails.
- Count rejections in the handler path only, as before: criterion 8's mismatch and in-progress cases fail.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes (authentication cookies, ID-token validation, the audit log).
- Lane: B.
- Mechanisms and sources: OIDC handler cookies (<https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.remoteauthenticationoptions.correlationcookie>, <https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.openidconnect.openidconnectoptions.noncecookie>); cookie name prefixes (RFC 6265bis, <https://datatracker.ietf.org/doc/draft-ietf-httpbis-rfc6265bis/>); `TokenValidationParameters.ValidAlgorithms` (<https://learn.microsoft.com/dotnet/api/microsoft.identitymodel.tokens.tokenvalidationparameters.validalgorithms>); OpenID Connect Core 1.0 §3.1.3.7 (<https://openid.net/specs/openid-connect-core-1_0.html>); `UseSocketsHttpHandler` (<https://learn.microsoft.com/dotnet/api/microsoft.extensions.dependencyinjection.httpclientbuilderextensions.usesocketshttphandler>, <https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory>), `SocketsHttpHandler.AllowAutoRedirect` (<https://learn.microsoft.com/dotnet/api/system.net.http.socketshttphandler.allowautoredirect>); cancellation (<https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads>); Npgsql command timeout (<https://www.npgsql.org/doc/connection-string-parameters.html>); EF Core interceptors (<https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors>); `WebApplicationFactory<TEntryPoint>.UseKestrel` (<https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1.usekestrel?view=aspnetcore-10.0>); Generic Host shutdown (<https://learn.microsoft.com/dotnet/core/extensions/generic-host#host-shutdown>); Data Protection key management (<https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-management?view=aspnetcore-10.0>) and configuration (<https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0>); OWASP Logging and Session Management Cheat Sheets; OWASP ASVS 5.0.0 V3.3, V9.1, V15.3 (<https://github.com/OWASP/ASVS>).
- Packages: none.
- Touches:
  - `backend/src/DisputeDesk.Api/Bff/BffAuthentication.cs` (the cookie builders, `ValidAlgorithms`, and the comment that cites AD-24's old rule), `BffOptions.cs` (criteria 1, 2), `backend/src/DisputeDesk.Api/appsettings.json` (criterion 1), `docker-compose.yml` (the two local cookie-name overrides only)
  - `backend/src/DisputeDesk.Infrastructure/CoreBanking/CoreBankingRegistration.cs` (criterion 3)
  - `backend/src/DisputeDesk.Api/Authorization/AuthorizationResultHandler.cs`, the refusal saves in `backend/src/DisputeDesk.Application/` (`RaiseDisputeHandler`, `WithdrawDisputeHandler`, `OperationsActionHandler`, `ReadDisputeHandler`) (criterion 4), `backend/src/DisputeDesk.Api/Bff/BffEndpoints.cs` (criterion 5)
  - `backend/src/DisputeDesk.Api/Disputes/Idempotency/IdempotencyFilter.cs`, `backend/src/DisputeDesk.Api/Disputes/DisputeEndpoints.cs`, `RaiseMetrics.cs` (criterion 8)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/` (`Infrastructure/ApiFactory.cs` for the two unprefixed default names, `Bff/SignInTests.cs`, `Bff/SignOutTests.cs`, `CoreBanking/`, `Authorization/`, `Disputes/RaiseMetricsTests.cs`, `Infrastructure/FakeZitadel.cs`, and a new shutdown test)

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- verification — docs/verification/7.10-prr.md, Findings A-F3, C-F8, E-F12, F-F5, F-F8, F-F9, F-F11, the Evidence stack section, and Disposition
- verification — docs/verification/7.13-challenge.md, 7.13-prebuild-review.md, 7.13-prebuild-security.md, and their human decisions
- architecture — docs/architecture/architecture.md, AD-7, AD-14a, AD-21, AD-24, AD-26
- prd — docs/prd/prd.md, NFR-SEC-1, NFR-SEC-10, NFR-RES-5, NFR-OBS-2, NFR-OBS-3
- production path — docs/production-path.md, P38
- story — docs/tickets/epic-7-release/tickets/story-7.4-aws-compose-override-and-opentelemetry-collector.md, criterion 8

## Notes

- Risk medium (proposed): a wrong cookie or algorithm setting stops every sign-in, and a wrong audit change loses refusal records; nothing here serves real customers, and the CI journeys sign in end to end.
- Decision: 2026-10-31 (human, 7.10 disposition) — A-F3, C-F8, E-F12, F-F5, F-F8, F-F9 and F-F11 are fixed in this story; F-F9 is a named test of two Api instances starting together on an empty key store exchanging a cookie, checked against Microsoft Learn's Data Protection key management, with the existing test never loosened and no custom key-refresh mechanism.
- Decision: 2026-10-31 (human, 7.13 pre-build) — AD-24 is amended: the OIDC correlation and nonce cookie names come from configuration like the session and antiforgery cookies, `__Host-` in AWS over HTTPS and unprefixed locally; they stay Secure always with SameSite=None. AD-26's key list gains `Bff:CorrelationCookie:Name` and `Bff:NonceCookie:Name`; both architecture edits are made in the separate docs change.
- Decision: 2026-10-31 (human, 7.13 pre-build) — F-F5: the narrower fix is enough; a sign-out audit lost to a database failure stays in P38, accepted for the demo.
- Decision: 2026-10-31 (Sofia, 7.13 refine) — the audit saves use `CancellationToken.None`, linking's existing pattern (`VerifyOtpHandler`, `LinkingRateLimits`, `StartLinkingHandler`, the idempotency filter's own writes), not a new 5-second bound: it is one argument per call site with no constant, no `CancellationTokenSource` and no timeout path to handle, and the database command timeout (Npgsql default, 30 seconds) already bounds a stuck save. Nothing forbids it: the request does not wait longer than any other database call today, and with no token able to cancel the save, the sign-out's `OperationCanceledException` gap (challenger, security 4) cannot arise.
- Decision: 2026-10-31 (Sofia, 7.13 refine) — the OIDC handler's token backchannel and the JWT bearer handler's metadata backchannel (security point 3, ASVS V15.3.2) go to the production path, not this story: their only peer is the configured Zitadel issuer over HTTPS, and the integration tests replace the OIDC backchannel with the fake identity provider (`FakeZitadel.AddTo`), so a production `BackchannelHttpHandler` setting cannot be proved by a behaviour test without a new test seam. The production-path entry is added in the separate docs change.
- Decision: 2026-10-31 (Sofia, 7.13 refine) — `Path=/` sends the transient cookies on every request for their lifetime (the handler's 15-minute remote authentication timeout); many abandoned sign-ins in that window could grow the `Cookie` header past nginx's 8 KB header buffer, which answers 400 until they expire. Accepted as an edge for the demo; no check is added.
- Decision: 2026-10-31 (Sofia, 7.13 write) — the rejection metric counts responses a customer receives; a replayed stored 409 or 422 is the same rejection and is not counted again (NFR-OBS-3 counts rejections on raising, not retries). A mismatch or in-progress answer is not stored, so each is counted every time it is answered.
- Decision: 2026-10-31 (Sofia, 7.13 write) — a failed sign-out audit keeps today's strategy (the session ends, the failure is logged with its trace ID and exported through the log pipeline); the fix is that a disconnect can no longer cancel the save.
- Collision: touches `docker-compose.yml` (two local cookie-name lines) and backend code only; it does not touch `PlatformScriptTests.cs`, `docker-compose.aws.yml` or anything under `infra/`, so it may build beside 7.12 and 7.14.
- Production path: the docs-only production-path change drops P38's audit-cancellation sentence once this story merges.
- Decision: 2026-10-31 (supervisor ruling on the verifier's criterion 6 failure) — the hold is 1.5 seconds, not 2: a 2-second hold equals the core-banking resilience attempt timeout (AD-21), so the read would time out and retry instead of being answered; 1.5 seconds still exceeds the 1-second `ShutdownTimeout` break, which fails the test (builder-verified).
