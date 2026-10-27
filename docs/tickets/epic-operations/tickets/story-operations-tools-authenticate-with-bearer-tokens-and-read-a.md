---
id: 1
type: story
title: "Operations tools authenticate with bearer tokens and read a dispute"
parent: epic-operations
covers: [R21, R39]
after: [3.1, 2.5, 2.6]
hitl: false
risk: high
---

# Operations tools authenticate with bearer tokens and read a dispute

## Description

Adds JwtBearer on the operations group (AD-8, AD-9), role policies, the actor mapping, `GET /api/v1/operations/disputes/<disputeId>` and Scalar behind `Scalar:Enabled`.

## Requirements

Verify: Integration tests with tokens signed by a test key cover credential pinning, roles, actor mapping and the read shape.

1. **Credential pinned per group** (R21; FR-25, NFR-SEC-2)
   **Given** a session cookie and a bearer token
   **When** the cookie calls an operations route and the token calls a customer route
   **Then** both get 401 `unauthorized`, never a redirect

2. **Token validation** (R21; FR-25, NFR-SEC-2)
   **Given** tokens with a wrong issuer, audience, signature or an expired lifetime
   **When** they call an operations route
   **Then** each gets 401

3. **Role and actor mapping** (R21; FR-25, FR-30, NFR-SEC-3)
   **Given** a valid token without the route's role, and one whose `sub` maps to no actor type
   **When** they call
   **Then** both get 403 `forbidden`

4. **Read one dispute** (R21; FR-30)
   **Given** a token with `dispute.read`
   **When** it reads a dispute by ID
   **Then** the body has the snapshot, reason, amount, internal `status`, `outcome` and timeline with `actorType`, and no personal details beyond the snapshot; an unknown ID gets 404

5. **Provisioned actor mapping** (R21; FR-25)
   **Given** `provision.sh`
   **When** it is read
   **Then** the operations machine user holds all four camelCase roles and `Operations:Actors:<sub>` maps it to `operations`

6. **Scalar behind a switch** (R39; NFR-DOC-4)
   **Given** `Scalar:Enabled` true and false
   **When** `/scalar/` is requested
   **Then** it is served only when true, with its own CSP allowing the client-credentials flow to the auth origin

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- From `/scalar/`, obtain a client-credentials token and read a seeded dispute.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: B.
- Touches: `backend/src/DisputeDesk.Api/Operations/`; `backend/src/DisputeDesk.Application/Operations/ReadDispute/`; `backend/src/DisputeDesk.Api/Program.cs`; `backend/src/DisputeDesk.Api/appsettings.json`; infra/zitadel/provision/provision.sh (Operations:Actors, dispute.read grant); infra/nginx/templates/ (Scalar CSP); `backend/openapi.json`; `backend/tests/DisputeDesk.Api.IntegrationTests/Operations/`.

## References

- parent — docs/tickets/epic-operations/epic-operations.md

## Notes

- Risk high: a second credential type; beyond the criteria, the security agent checks no customer route accepts bearer.
