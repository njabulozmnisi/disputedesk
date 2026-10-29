---
id: 1
type: story
title: "Operations tools authenticate with bearer tokens and read a dispute"
parent: epic-5-operations
covers: [R21, R10, R41, R39]
after: [3.8, 2.8, 2.5, 2.6]
hitl: false
risk: high
---

# Operations tools authenticate with bearer tokens and read a dispute

## Description

Adds JwtBearer on the operations group (AD-8, AD-9), role policies, the actor mapping, `GET /api/v1/operations/disputes/<disputeId>` with every stored event type on its timeline and each read audited as `DisputeRead` through `AuditLog.Record`, and Scalar behind `Scalar:Enabled`.

## Requirements

Verify: Integration tests with tokens signed by a test key cover credential pinning, roles, actor mapping, the read shape with every stored event type, and the `DisputeRead` audit entry.

1. **Credential pinned per group** (R21; FR-25, NFR-SEC-2)
   **Given** a session cookie and a bearer token
   **When** the cookie calls an operations route and the token calls a customer route
   **Then** both get 401 `unauthorized`, never a redirect

2. **Token validation** (R21; FR-25, NFR-SEC-2)
   **Given** tokens with a wrong issuer, audience or signature, or expired by more than the JwtBearer `ClockSkew`
   **When** they call an operations route
   **Then** each gets 401 `unauthorized` with a `WWW-Authenticate: Bearer` header (RFC 6750 §3)

3. **Role and actor mapping** (R21; FR-25, FR-30, NFR-SEC-3)
   **Given** a valid token without the route's role, and one whose `sub` maps to no actor type
   **When** they call
   **Then** both get 403 `forbidden`

4. **Read one dispute** (R21; FR-30)
   **Given** a token with `dispute.read`
   **When** it reads a dispute by ID
   **Then** the body has the snapshot, reason, amount, internal `status`, `outcome` and the timeline built from every stored event of the dispute, `SlaWarningRaised` included, each with its stored event type and `actorType`, and no personal details beyond the snapshot; an unknown ID gets 404 (FR-30; AD-13: operations see every type)
   **And** each successful read writes one `DisputeRead` audit entry with result `Succeeded`, actor type `Operations`, actor ID the token's `sub` and the dispute ID, through `AuditLog.Record` (story 2.8; AD-14a, FR-25), and `AuditAction` gains `DisputeRead`

5. **Provisioned actor mapping** (R21; FR-25)
   **Given** `provision.sh`
   **When** it is read
   **Then** the operations machine user issues JWT access tokens (`ACCESS_TOKEN_TYPE_JWT`), holds all four camelCase roles, and `Operations:Actors:<sub>` maps it to `operations`

6. **Scalar behind a switch** (R39; NFR-DOC-4)
   **Given** `Scalar:Enabled` true and false
   **When** `/scalar/` is requested
   **Then** it is served only when true, with its own CSP allowing the client-credentials flow to the auth origin

7. **Client-credentials scheme in the contract** (R39, R21; NFR-DOC-4, NFR-API-1)
   **Given** `openapi.json`
   **When** it is read
   **Then** it declares an OAuth 2.0 security scheme with the `clientCredentials` flow and Zitadel's token URL (OpenAPI Security Scheme Object, RFC 6749 §4.4), applied to the operations routes only, so Scalar can obtain a token

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- From `/scalar/`, obtain a client-credentials token and read a dispute customer A raised through the customer API.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Touches: `backend/src/DisputeDesk.Contracts/Audit/AuditAction.cs` (`DisputeRead`); `backend/src/DisputeDesk.Api/Operations/`; `backend/src/DisputeDesk.Application/Operations/ReadDispute/`; `backend/src/DisputeDesk.Api/Program.cs`; `backend/src/DisputeDesk.Api/appsettings.json`; infra/zitadel/provision/provision.sh (Operations:Actors, dispute.read grant); infra/nginx/templates/ (Scalar CSP); `backend/openapi.json`; `backend/tests/DisputeDesk.Api.IntegrationTests/Operations/`.

## References

- parent — docs/tickets/epic-5-operations/epic-5-operations.md

## Notes

- Decision: 2026-10-27 — `openapi.json` declares the `clientCredentials` scheme so Scalar can obtain a token. Scalar holding a client secret is a local stand-in for an operations tool; the code that configures it carries a `// shortcut: Scalar holds an operations client secret, local stand-in for an operations tool; remove when a real tool holds its own credentials` comment.
- Production path: real operations tools keep their own client credentials in a secret store, and Scalar holds none outside local runs (architecture Deferred, so story 7.6 records it in `docs/production-path.md`).
- Risk high: a second credential type; beyond the criteria, the security agent checks no customer route accepts bearer.
- Input (from 3.1 security, 2026-10-28): AD-11 also asks for an architecture test that fails when `Disputes/` or `Transactions/` reference `Operations/` or `Sla/`. 3.1 pinned the `IgnoreQueryFilters` bypass to `DisputeDesk.Application/Operations/` and `DisputeDesk.Worker/Sla/`; add the reference rule here, where `Operations/` first exists.
