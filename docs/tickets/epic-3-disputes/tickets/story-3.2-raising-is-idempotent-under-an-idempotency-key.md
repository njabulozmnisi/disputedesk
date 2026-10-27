---
id: 2
type: story
title: "Raising is idempotent under an Idempotency-Key"
parent: epic-3-disputes
covers: [R15]
after: [1]
hitl: false
risk: high
---

# Raising is idempotent under an Idempotency-Key

## Description

Adds the `IdempotencyKeys` table and the lease-based endpoint filter on `POST /api/v1/disputes` (AD-16).

## Requirements

Verify: Integration tests for replay, mismatch, in-progress, takeover, 5xx release and expiry pass on Testcontainers Postgres.

1. **Key required and well formed** (R15; FR-15)
   **Given** a raise
   **When** it has no `Idempotency-Key`, a value that is not an RFC 9651 Structured Field String (an unquoted `Idempotency-Key: abc`), or a string outside 1 to 255 printable characters
   **Then** it gets 400 `validation` before any insert
   **And** a quoted key such as `Idempotency-Key: "<uuid>"` is accepted, as draft-ietf-httpapi-idempotency-key-header defines the header
   **And** `openapi.json` declares `Idempotency-Key` as a required header parameter of the raise operation, so the generated hook takes it

2. **Replay returns the original** (R15; FR-15, NFR-TEST-3)
   **Given** a completed raise
   **When** the same validated request is sent again with the same key
   **Then** the stored status, body and `Location` return and no second dispute or outbox row exists

3. **Mismatch and in progress** (R15; FR-15)
   **Given** a key in use
   **When** a different request uses it, or the same request arrives while the first is in progress
   **Then** they get 422 `idempotency-mismatch` and 409 `idempotency-in-progress`, the codes draft-ietf-httpapi-idempotency-key-header recommends
   **And** an in-progress row older than 60 seconds with the same hash is taken over under a new lease; the raise endpoint's 30-second limit is the built-in request timeout (`WithRequestTimeout`), and the handler honours its cancellation

4. **Deterministic refusals stored, 5xx released** (R15; FR-15)
   **Given** a raise that returns 404, 409 or 422, and one that returns 503
   **When** each is replayed
   **Then** the 4xx replays its stored response and the 503 key is gone so a retry runs fresh

5. **Scoped and expiring** (R15; FR-15)
   **Given** two customers using the same key, and a key older than 24 hours
   **When** they raise
   **Then** keys are scoped per customer and an expired row is replaced
   **And** the completion write is conditional on the lease, and a lost lease rolls the dispute back

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- None beyond the story flow.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes, migration.
- Lane: A.
- Touches: `backend/src/DisputeDesk.Api/Disputes/Idempotency/`; backend/src/DisputeDesk.Infrastructure/Persistence/ (IdempotencyKey configuration, Migrations/); `backend/openapi.json`; `backend/tests/DisputeDesk.Api.IntegrationTests/Disputes/Idempotency/`.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md

## Notes

- Decision: 2026-10-27 — the key follows draft-ietf-httpapi-idempotency-key-header exactly: an RFC 9651 Structured Field String, parsed in that form, any other form 400 `validation` (AD-16, prd FR-15).
- Risk high: a double submit creates two disputes; beyond the criteria, QA double-clicks Submit on a throttled network and counts rows.
