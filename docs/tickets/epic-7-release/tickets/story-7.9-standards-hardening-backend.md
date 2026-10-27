---
id: 9
type: story
title: "Standards hardening (backend)"
parent: epic-7-release
covers: [R9, R3, R11, R30, R32, R35]
after: [2.1, 2.2, 2.3, 3.4, 5.2, 6.3]
hitl: false
risk: high
---

# Standards hardening (backend)

## Description

Replaces the remaining hand-made or missing backend mechanisms with their documented standard forms: deny by default, host filtering, sign-out through the OIDC handler, the standard resilience handler's total timeout, and security tests in the Production environment. Built before the closing sweep, which then changes no behaviour.

## Requirements

Verify: Integration tests for the fallback policy, sign-out and the core-banking outage pass, and the security and error-shape tests pass in both the Development and Production environments.

1. **Deny by default** (R9; NFR-SEC-3)
   **Given** `AuthorizationOptions.FallbackPolicy` requiring an authenticated user
   **When** every mapped endpoint is enumerated by a test
   **Then** only the BFF, health, OpenAPI document and Scalar endpoints carry `AllowAnonymous`, every other endpoint names its policy, and an endpoint mapped without one answers 401 to an anonymous caller (OWASP Authorization Cheat Sheet, deny by default; Microsoft Learn, *Authorization policies*, default and fallback policies)

2. **Sign-out through the OIDC handler** (R3; FR-3)
   **Given** a signed-in person
   **When** they sign out through `POST /api/v1/bff/logout`
   **Then** the endpoint calls the OIDC handler's built-in `SignOutAsync`, so the end-session request carries `state`, `id_token_hint`, `client_id` and `post_logout_redirect_uri` (OpenID Connect RP-Initiated Logout 1.0), and the hand-built redirect is removed
   **And** the existing sign-out tests still pass: the session row is deleted, both cookies are cleared, and a replayed old cookie gets 401

3. **Whole-call deadline from the standard handler** (R11; NFR-RES-2)
   **Given** the core-banking `HttpClient`
   **When** a call runs longer than 5 seconds in total
   **Then** the standard resilience handler's `TotalRequestTimeout` ends it (`HttpStandardResilienceOptions`; Microsoft Learn, *Build resilient HTTP apps*), the hand-made linked token source is removed, and the outage still gives 503 `core-banking-unavailable` within about 5 seconds

4. **Security tests in the Production environment** (R35, R30; NFR-TEST-3, NFR-API-2)
   **Given** the security and error-shape integration tests
   **When** they run
   **Then** they run once more with the `WebApplicationFactory` environment set to Production (Microsoft Learn, *Integration tests in ASP.NET Core*), so no Development-only behaviour hides a failure

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Browser sign-out for login A lands on `/`, Zitadel shows no session, and the end-session request carries `state`.
- Remove `AllowAnonymous` from `/health/live` in a throwaway copy: the endpoint enumeration test fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: A.
- Touches: backend/src/DisputeDesk.Api/Program.cs (fallback policy, AllowAnonymous on OpenAPI and Scalar); backend/src/DisputeDesk.Api/Bff/ (AllowAnonymous, sign-out through SignOutAsync); backend/src/DisputeDesk.Api/Health/ (AllowAnonymous); backend/src/DisputeDesk.Infrastructure/CoreBanking/ (TotalRequestTimeout); backend/tests/DisputeDesk.Api.IntegrationTests/ (endpoint enumeration, sign-out, Production-environment runs).

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md
- architecture — docs/architecture/architecture.md, AD-7, AD-10, AD-21

## Notes

- Decision: 2026-10-27 — the behaviour-changing items of the standards audit and the 2.3 security re-review move here from 7.8a, so the sweep stays "no behaviour change". Data Protection key encryption at rest moved to story 7.4.
- Runs in lane A after the stories whose code it hardens. `AllowedHosts` is owned by story 6.3 (forwarded headers), and the testing convention was corrected on 2026-10-27, so neither is repeated here.
- Risk high: authorisation, sign-out and host handling; beyond the criteria, the security agent re-checks every endpoint's authorisation state.
- Decision: 2026-10-27 — 2.7 review: each problem's `title` is the same for every occurrence of its type and matches its page (RFC 9457 §3.1.3); fold this into this story with a criterion before it starts.
