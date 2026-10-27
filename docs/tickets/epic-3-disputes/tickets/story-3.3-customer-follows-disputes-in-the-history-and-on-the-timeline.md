---
id: 3
type: story
title: "Customer follows disputes in the history and on the timeline"
parent: epic-3-disputes
covers: [R16, R17, R18, R11]
after: [2]
hitl: false
risk: high
---

# Customer follows disputes in the history and on the timeline

## Description

Adds `GET /api/v1/disputes` and `GET /api/v1/disputes/<disputeId>` with the customer view mapping, and proves ownership on every customer endpoint.

## Requirements

Verify: Integration tests for list, detail, ownership and the core-banking outage pass.

1. **History list** (R16; FR-16, NFR-API-3)
   **Given** a customer with disputes
   **When** they list them
   **Then** rows come newest first by `CreatedAtUtc` then `Id` with keyset paging, each with the snapshot summary, reason, disputed amount, `customerStatus` and `outcome` when resolved

2. **Detail and timeline** (R16; FR-17)
   **Given** a dispute with several entries
   **When** its detail is read
   **Then** the timeline is oldest first, each entry with `customerStatus`, the UTC instant, actor `you` or `theBank` and any customer-visible note

3. **Customer never sees internals** (R18; FR-17, NFR-API-5)
   **Given** disputes acted on by operations and automation, and an escalated one
   **When** customer responses are serialised
   **Then** no actor type, actor identity, SLA or due date appears, and escalated shows as `underReviewPrioritised`

4. **Identical 404 on every customer endpoint** (R17; FR-17, NFR-SEC-6, NFR-TEST-3)
   **Given** customer A and a dispute of customer B
   **When** A calls the detail route with B's ID and with an unknown ID, and raises on B's transaction
   **Then** every response is 404 `not-found` with bodies identical apart from `traceId`
   **And** an integration test enumerates every customer endpoint that exists (transactions, raise, list, detail) and fails when a new one is not listed

5. **History works while core banking is down** (R11, R16; NFR-RES-2, FR-16)
   **Given** the stub is unreachable
   **When** the customer lists and opens disputes
   **Then** both succeed from the stored snapshots

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- None beyond the story flow.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: A.
- Touches: `backend/src/DisputeDesk.Application/Disputes/ListDisputes/, GetDispute/, CustomerView/`; `backend/src/DisputeDesk.Contracts/Disputes/`; `backend/src/DisputeDesk.Api/Disputes/`; `backend/openapi.json`; `backend/tests/DisputeDesk.Api.IntegrationTests/Disputes/, Ownership/`.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md

## Notes

- Risk high: cross-customer leakage; beyond the criteria, the security agent diffs the 404 bodies byte for byte.
