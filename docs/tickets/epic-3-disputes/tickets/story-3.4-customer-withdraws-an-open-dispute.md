---
id: 4
type: story
title: "Customer withdraws an open dispute"
parent: epic-3-disputes
covers: [R19, R17, R23, R10, R36]
after: [3]
hitl: false
risk: high
---

# Customer withdraws an open dispute

## Description

Adds `Dispute.Withdraw` and `POST /api/v1/disputes/<disputeId>/withdraw`, the first status change, with the `status-changed.v1` event and its schema.

## Requirements

Verify: Domain unit tests for the withdraw cells and integration tests for withdraw, closed disputes, races and the event pass.

1. **Withdraw an open dispute** (R19; FR-18)
   **Given** the owner and a Submitted, Under review or Escalated dispute
   **When** they withdraw with an optional note of up to 500 characters
   **Then** the status is Withdrawn, one timeline entry with actor `you` is added, and one audit entry is written
   **And** success is 200 with the updated customer view of the dispute, the same shape as `GET /api/v1/disputes/<disputeId>`
   **And** a note over 500 characters after trimming gets 400 `validation`, and an unknown ID with such a note gets 404 first, because the endpoint calls `.DisableValidation()` and validates after loading (AD-8)

2. **Withdrawn is final** (R19; FR-19)
   **Given** a Withdrawn dispute
   **When** its owner withdraws again or its transaction is raised again
   **Then** the action gets 409 and the raise gets 409 `already-disputed`

3. **Closed disputes refuse** (R19; FR-20, NFR-API-2)
   **Given** a Resolved or Withdrawn dispute
   **When** its owner withdraws
   **Then** the response is 409 `action-not-allowed` with `customerStatus`

4. **Customer credential only** (R19; NFR-SEC-2, NFR-SEC-1)
   **Given** a bearer token, an unlinked login, and a cookie without the antiforgery header
   **When** each calls withdraw
   **Then** they get 401 `unauthorized`, 403 `linking-required` and 400 `validation` respectively

5. **Identical 404 on withdraw** (R17; NFR-SEC-6, NFR-TEST-3)
   **Given** customer A, a dispute of customer B and an unknown ID
   **When** A withdraws each
   **Then** both get 404 `not-found` with bodies identical apart from `traceId`
   **And** the endpoint enumeration test includes withdraw

6. **Race on withdraw** (R19; NFR-TEST-3)
   **Given** two parallel withdraws of one dispute
   **When** both commit
   **Then** exactly one succeeds and the other gets 409 `action-not-allowed`

7. **Status-changed event** (R23; FR-28, NFR-TEST-4)
   **Given** a successful withdraw
   **When** the outbox row is read
   **Then** it is a CloudEvents 1.0 structured `disputedesk.dispute.status-changed.v1` with the AD-19 attributes and data `disputeId`, `fromStatus`, `toStatus` `withdrawn`, `action` `withdraw`, `actorType` `customer`, `occurredAt` and `disputeVersion`, valid against the committed schema

8. **Withdraw metrics** (R36; NFR-OBS-3)
   **Given** a withdraw
   **When** metrics are collected
   **Then** `disputedesk.dispute_actions` counts it by action and actor type and `disputedesk.disputes.withdraw_age` records its age in seconds (unit `s`, OpenTelemetry semantic conventions)

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- None beyond the story flow.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: A.
- Touches: `backend/src/DisputeDesk.Domain/Disputes/Dispute.cs`; `backend/src/DisputeDesk.Application/Disputes/WithdrawDispute/`; `backend/src/DisputeDesk.Contracts/Events/`; `backend/src/DisputeDesk.Api/Disputes/`; `contracts/events/disputedesk.dispute.status-changed.v1.schema.json and sample`; `backend/openapi.json`; backend/tests/ (Domain.Tests/Disputes/, Api.IntegrationTests/Disputes/Withdraw/).

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md

## Notes

- Decision: 2026-10-27 — withdraw success is 200 with the updated customer view of the dispute, so the SPA needs no second read.
- Risk high: a final status set wrongly; beyond the criteria, QA checks the transaction row stays not disputable after withdraw.
