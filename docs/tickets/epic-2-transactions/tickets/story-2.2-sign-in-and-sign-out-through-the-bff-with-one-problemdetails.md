---
id: 2
type: story
title: "Sign in and sign out through the BFF, with one ProblemDetails contract"
parent: epic-2-transactions
covers: [R1, R2, R3, R21, R26, R27, R29, R30]
after: [1.2, 1.5]
hitl: false
risk: high
---

# Sign in and sign out through the BFF, with one ProblemDetails contract

## Description

The Api becomes the confidential client with a server-side session (AD-7), and every API error follows the rest of AD-3; this is the first endpoint story and the least certain one on the tracer path.

## Requirements

Verify: BFF and ProblemDetails integration tests pass.

1. **Code flow with PKCE and a server-side ticket** (R2, R26; FR-2, NFR-SEC-1, NFR-ARC-3)
   **Given** a verified login
   **When** it signs in through `GET /api/v1/bff/login`
   **Then** the browser receives only an HttpOnly, SameSite=Strict session cookie holding a random key, the ticket is stored in the PostgreSQL `Sessions` table by key hash so a second Api instance would accept it, and no token reaches the browser
   **And** `returnUrl` other than a local path falls back to `/`

2. **Unverified login cannot sign in** (R1; FR-1)
   **Given** an ID token without `email_verified: true`
   **When** the callback validates it
   **Then** sign-in is refused and no session row is written

3. **Idle and absolute expiry** (R2; FR-2)
   **Given** a session
   **When** 15 minutes pass without an authenticated request, or 12 hours pass in total (fake `TimeProvider`)
   **Then** the next request gets 401 and the expired row is deleted
   **And** an authenticated request inside the window renews the idle limit

4. **Session state endpoint** (R2; FR-2)
   **Given** an anonymous caller and a signed-in login
   **When** each calls `GET /api/v1/bff/session`
   **Then** the body is `{ "state": "anonymous" }` and `{ "state": "unlinked" }` or `linked` respectively

5. **Antiforgery on unsafe methods** (R2; NFR-SEC-1)
   **Given** a signed-in login
   **When** it posts to an unsafe BFF or customer route without `X-XSRF-TOKEN`
   **Then** the response is 400 with problem slug `validation`
   **And** the token from `GET /api/v1/bff/antiforgery` makes the same request pass

6. **Sign-out ends both sessions** (R3; FR-3)
   **Given** a signed-in login
   **When** it posts the logout form
   **Then** its session row is deleted, the cookie is cleared, the redirect goes to Zitadel end-session with `client_id`, `post_logout_redirect_uri` and the session's `id_token_hint`, so Zitadel ends its session without a prompt
   **And** replaying the old cookie gets 401

7. **One ProblemDetails contract** (R30; NFR-API-2, NFR-API-1)
   **Given** any error from Domain, Application, the auth handlers, antiforgery or status-code pages
   **When** it reaches the client
   **Then** the body has `type` = `tag:disputedesk,2026:problem:<slug>`, `traceId` and camelCase `errors` keys, and 401 and 403 carry `unauthorized` and `forbidden`
   **And** `Validation` maps to 400 with field errors, `ActionNotAllowed` to 409 with the current status extension, `Forbidden` to 403, `Conflict` to 409 and `Unavailable` to 503, and no error echoes an input value

8. **Slug catalogue is complete** (R30; NFR-API-2, NFR-TEST-1)
   **Given** the slug constants in Contracts
   **When** the catalogue test runs
   **Then** every Domain and Application error code maps to one catalogue slug, and the catalogue holds exactly the AD-3 list

9. **Shared camelCase JSON and built-in validation** (R30; NFR-API-6, NFR-CODE-2)
   **Given** the Api
   **When** a request or response carries an enum
   **Then** one `JsonSerializerOptions` with `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` writes camelCase values, and `AddValidation` rejects malformed input with 400

10. **Migrations on start-up and readiness** (R29, R27; NFR-DATA-6, NFR-RES-1)
   **Given** an empty database
   **When** the Api starts
   **Then** the `Sessions` and `DataProtectionKeys` migration applies under the EF migration lock before `/health/ready` reports healthy, and readiness also checks Zitadel discovery

11. **Role keys and configuration from provisioning** (R21, R2; FR-25)
   **Given** `infra/zitadel/provision/provision.sh`
   **When** it is read
   **Then** the project roles are `dispute.startReview`, `dispute.escalate`, `dispute.resolve` and `dispute.read`, and `zitadel-clients.json` holds the `Zitadel:*` keys named in AD-26
   **And** `developmentMode` and `ExternalSecure` come from configuration

12. **Headless sign-in script exists** (R2; NFR-TEST-1)
   **Given** `scripts/dev-login.sh`
   **When** it is read and run with `--help`
   **Then** it takes a fixture login, drives the hosted sign-in with plain HTTP requests (no browser) and writes a cookie jar path; it holds no secret beyond the fixture dev passwords

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- `scripts/dev-login.sh` for login A writes a cookie jar that `GET /api/v1/bff/session` answers `linked` for once story 3 is merged (`unlinked` before).
- Browser sign-in for login A on `http://localhost:8080`, then sign-out lands on `/` and Zitadel shows no session.
- `docker compose down -v && docker compose up` re-provisions with the camelCase role keys.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes, migration.
- Lane: A.
- Touches: `backend/src/DisputeDesk.Api/Bff/`; `backend/src/DisputeDesk.Api/Program.cs`; `backend/src/DisputeDesk.Api/appsettings.json`; `backend/src/DisputeDesk.Api/Errors/`; `backend/src/DisputeDesk.Api/Health/`; `backend/src/DisputeDesk.Contracts/Problems/`; `backend/src/DisputeDesk.Contracts/Json/`; `backend/src/DisputeDesk.Domain/ErrorKind.cs, Error.cs`; `backend/src/DisputeDesk.Infrastructure/Sessions/`; backend/src/DisputeDesk.Infrastructure/Persistence/ (AppDbContext, Migrations/); `backend/src/DisputeDesk.Infrastructure/DependencyInjection.cs`; `backend/Directory.Packages.props`; `backend/openapi.json`; `backend/tests/DisputeDesk.Api.IntegrationTests/Bff/, ProblemDetailsTests.cs`; `infra/zitadel/provision/provision.sh`; docker-compose.yml (api Zitadel settings); `scripts/dev-login.sh`.

## References

- parent — docs/tickets/epic-2-transactions/epic-2-transactions.md

## Notes

- Risk high: authentication and sessions; beyond the criteria, the security agent checks cookie flags and that no token is logged.
- Assumption: the AD-3 completion lands here because this is the first endpoint story; split it out first if the builder blocks on size.
