---
id: 5
type: story
title: "Gateway serves the SPA with the edge rules, and CI checks the frontend"
parent: epic-2-transactions
covers: [R32, R34, R36, R37]
after: [4]
hitl: false
risk: medium
---

# Gateway serves the SPA with the edge rules, and CI checks the frontend

## Description

Builds the SPA into the gateway image in a Node stage, templates nginx from the environment with the AD-23 edge rules and the `/otlp/` proxy, and adds the CI `frontend` job; completes the tracer bullet.

## Requirements

Verify: Reading the gateway Dockerfile, nginx templates and `ci.yml` shows the criteria below; the Api security-header integration tests pass; `docker compose config` is a supervisor check.

1. **SPA built into a non-root gateway image** (R37; NFR-INFRA-1)
   **Given** the gateway Dockerfile
   **When** it builds
   **Then** a Node stage builds `frontend/` with `npm ci` and a pinned `nginxinc/nginx-unprivileged` stage serves it as non-root, with unknown SPA routes falling back to `index.html` and a missing `/assets/` file getting 404
   **And** the image builds from the repository root as its build context, with a root `.dockerignore`, so the Node stage reads `frontend/` and the nginx stage reads `infra/nginx/`

2. **Security headers and coarse limit** (R32; NFR-SEC-7)
   **Given** the nginx template
   **When** it is rendered for local and for AWS
   **Then** it sets a CSP with no inline script, `X-Content-Type-Options`, `Referrer-Policy` and `frame-ancestors 'none'`, HSTS only when the AWS variable is set
   **And** `limit_req` allows 10 requests per second with burst 20 on `/api/` and `/otlp/`, with `limit_req_status 429` and an `error_page` that returns `application/problem+json` with `type` `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/rate-limited.md` and no `traceId`, because nginx has no trace context (RFC 6585, RFC 9457; AD-3)
   **And** each security header appears once per response: nginx sets the CSP, `X-Content-Type-Options`, `Referrer-Policy` and `frame-ancestors` with `add_header ... always`, the Api drops its duplicate `X-Content-Type-Options` and `Referrer-Policy`, and the Api keeps `Cache-Control: no-store` on `/api` (OWASP HTTP Headers Cheat Sheet; AD-23)
   **And** any location with its own `add_header` repeats the security headers, because nginx inherits `add_header` only when a level defines none

3. **Templated hosts and forwarded headers** (R32; NFR-SEC-7)
   **Given** the template variables
   **When** the gateway proxies to the Api
   **Then** app host, auth host and the CSP `form-action` origin come from the environment through the official image's `/etc/nginx/templates/*.template` (with `NGINX_ENVSUBST_OUTPUT_DIR` set when the template is the whole `nginx.conf`), and the Api receives single-value `X-Forwarded-For` (`$remote_addr`, never `$proxy_add_x_forwarded_for`), `-Proto` and `-Host`

4. **Same-origin telemetry proxy** (R36; NFR-OBS-1)
   **Given** the gateway
   **When** the SPA posts to `/otlp/`
   **Then** nginx forwards it to the OTLP endpoint with a body-size cap, so the CSP keeps `connect-src 'self'`

5. **CI frontend job** (R34; NFR-CODE-1, NFR-CODE-4, NFR-TEST-6)
   **Given** a pull request
   **When** CI runs
   **Then** the `frontend` job runs `tsc --noEmit`, ESLint, Prettier, Vitest and an Orval regeneration drift check, and fails on any of them

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- `docker compose config` validates.
- `curl -I` on `http://localhost:8080/` and on `/api/health/live` shows each security header exactly once; 40 rapid requests to `/api/health/live` get some 429s.
- Customer A signs in through the gateway and sees transactions: the tracer bullet demo.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes (security headers owned by the gateway).
- Lane: B.
- Touches: infra/nginx/ (Dockerfile with Node stage, templates/, html/ placeholder removed); `.dockerignore`; docker-compose.yml (gateway build context and template variables); backend/src/DisputeDesk.Api/Security/SecurityHeaders.cs (drop the duplicated headers); `backend/tests/DisputeDesk.Api.IntegrationTests/SecurityHeadersTests.cs`; .github/workflows/ci.yml (frontend job); `.nvmrc`.

## References

- parent — docs/tickets/epic-2-transactions/epic-2-transactions.md

## Notes

- Decision: 2026-10-27 — standards audit: one owner per security header (OWASP HTTP Headers cheat sheet). Today `/api` responses carry `X-Content-Type-Options` twice and two different `Referrer-Policy` values (gateway and Api); settle each header in one place.
- Decision: 2026-10-27 — the gateway owns the security headers: nginx sets each one once with `add_header ... always`; the Api keeps only `Cache-Control: no-store` on `/api` (AD-23).
- Decision: 2026-10-27 — the gateway's own 429 carries no `traceId` (AD-3 exception) and uses the resolvable problem `type` (2.7).
- Decision: 2026-10-27 — the gateway image builds from the repository root with a root `.dockerignore`, so one build reads both `frontend/` and `infra/nginx/`.
