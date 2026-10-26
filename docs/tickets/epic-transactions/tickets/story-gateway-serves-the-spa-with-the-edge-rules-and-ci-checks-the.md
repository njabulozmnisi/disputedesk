---
id: 5
type: story
title: "Gateway serves the SPA with the edge rules, and CI checks the frontend"
parent: epic-transactions
covers: [R32, R34, R36, R37]
after: [4]
hitl: false
risk: medium
---

# Gateway serves the SPA with the edge rules, and CI checks the frontend

## Description

Builds the SPA into the gateway image in a Node stage, templates nginx from the environment with the AD-23 edge rules and the `/otlp/` proxy, and adds the CI `frontend` job; completes the tracer bullet.

## Requirements

Verify: Reading the gateway Dockerfile, nginx templates and `ci.yml` shows the criteria below; `docker compose config` is a supervisor check.

1. **SPA built into a non-root gateway image** (R37; NFR-INFRA-1)
   **Given** the gateway Dockerfile
   **When** it builds
   **Then** a Node stage builds `frontend/` and a pinned nginx stage serves it as non-root, with unknown SPA routes falling back to `index.html`

2. **Security headers and coarse limit** (R32; NFR-SEC-7)
   **Given** the nginx template
   **When** it is rendered for local and for AWS
   **Then** it sets a CSP with no inline script, `X-Content-Type-Options`, `Referrer-Policy` and `frame-ancestors 'none'`, HSTS only when the AWS variable is set
   **And** `limit_req` allows 10 requests per second with burst 20 on `/api/` and `/otlp/`, rejecting with 429 and a `rate-limited` ProblemDetails body

3. **Templated hosts and forwarded headers** (R32; NFR-SEC-7)
   **Given** the template variables
   **When** the gateway proxies to the Api
   **Then** app host, auth host and the CSP `form-action` origin come from the environment, and the Api receives single-value `X-Forwarded-For`, `-Proto` and `-Host`

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
- `curl -I http://localhost:8080/` shows the headers; 40 rapid requests to `/api/health/live` get some 429s.
- Customer A signs in through the gateway and sees transactions: the tracer bullet demo.

## Build facts

- Owner: backend (backend-builder).
- Flags: none.
- Lane: B.
- Touches: infra/nginx/ (Dockerfile with Node stage, templates/, html/ placeholder removed); docker-compose.yml (gateway build and template variables); .github/workflows/ci.yml (frontend job); `.nvmrc`.

## References

- parent — docs/tickets/epic-transactions/epic-transactions.md
