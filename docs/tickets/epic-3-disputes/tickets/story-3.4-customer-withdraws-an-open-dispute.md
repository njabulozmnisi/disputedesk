---
id: 4
type: story
title: "Customer withdraws an open dispute"
parent: epic-3-disputes
covers: [R19, R17, R29, R23, R10, R41, R36]
after: [3, 2.8]
hitl: false
risk: high
---

# Customer withdraws an open dispute

## Description

Adds `Dispute.Withdraw` and `POST /api/v1/disputes/<disputeId>/withdraw`. The withdraw handler is the first to load a dispute by replaying its stored events and decide from that state (story 3.8, AD-13); on a version clash it reloads and decides once more (AD-15). A withdraw appends `DisputeWithdrawn`, published as `status-changed.v1` through story 3.8's mapping. Raise and withdraw refusals are audited as `Rejected` through `Audit.Record` (story 2.8, AD-14a).

## Requirements

Verify: Domain unit tests for the withdraw cells and integration tests for withdraw, closed disputes, the version-clash reload, refusal audit entries and the event pass.

1. **Withdraw an open dispute** (R19; FR-18)
   **Given** the owner and a Submitted, Under review or Escalated dispute
   **When** they withdraw with an optional note of up to 500 characters
   **Then** the handler loads the dispute by replay, `Dispute.Withdraw` appends one `DisputeWithdrawn` stored event, the status is Withdrawn, the `timeline` gains one item with actor `you`, and no audit entry is written (ad-009)
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

6. **Version clash: reload and decide once more** (R29, R19; NFR-DATA-5, NFR-TEST-3)
   **Given** two parallel withdraws of one dispute, and a withdraw whose first save loses `UQ_StoredEvents_DisputeId_Version` to a test-only append
   **When** each commits
   **Then** of the two withdraws exactly one succeeds; the other clears the change tracker, replays the stream once more, finds Withdrawn and gets 409 `action-not-allowed` with `customerStatus` (AD-15; EF Core *Handling concurrency conflicts*: retry on the new data)
   **And** a withdraw whose reloaded status still allows it succeeds on the one retry, with the instant the later of its use-case instant and the stream's latest `OccurredAtUtc` (AD-4), and a second clash returns 409 `action-not-allowed`; there is never a second retry

7. **Status-changed event** (R23; FR-28, NFR-TEST-4)
   **Given** a successful withdraw
   **When** the outbox row is read
   **Then** it is a CloudEvents 1.0 structured `disputedesk.dispute.status-changed.v1` with the AD-19 attributes, `id` equal to the `DisputeWithdrawn` stored event ID, and data `disputeId`, `fromStatus`, `toStatus` `withdrawn`, `action` `withdraw`, `actorType` `customer`, `occurredAt` and `disputeVersion`, valid against the schema committed by story 3.8

8. **Withdraw metrics** (R36; NFR-OBS-3)
   **Given** a withdraw
   **When** metrics are collected
   **Then** `disputedesk.dispute_actions` counts it by action and actor type and `disputedesk.disputes.withdraw_age` records its age in seconds (unit `s`, OpenTelemetry semantic conventions)

9. **Refusals audited as `Rejected`** (R10, R41; FR-10, NFR-SEC-10)
   **Given** a raise that gets 404 `not-found`, 409 `already-disputed` or 422 `not-disputable`, and a withdraw that gets 404 `not-found` or 409 `action-not-allowed`
   **When** each answers
   **Then** each writes one audit entry through `Audit.Record` with action `Raise` or `Withdraw`, result `Rejected`, actor type `Customer`, actor ID the login's `sub`, `CustomerId` the caller's customer, and `DisputeId` only when the dispute is the caller's (AD-14a)
   **And** a successful raise or withdraw writes no audit entry, a replayed stored 4xx under the same `Idempotency-Key` writes no second entry, and body-shape 400s and 401 and 403 write none

10. **Only `Audit.Record` writes audit entries** (R41; NFR-SEC-10)
    **Given** the production source
    **When** the architecture source-rules tests run
    **Then** a test fails when any file other than the one holding `Audit.Record` creates an `AuditLogEntry`

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- None beyond the story flow.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Touches: `backend/src/DisputeDesk.Domain/Disputes/Dispute.cs`; `backend/src/DisputeDesk.Application/Disputes/WithdrawDispute/`; `backend/src/DisputeDesk.Application/Disputes/RaiseDispute/` (refusal audit); `backend/src/DisputeDesk.Api/Disputes/`; `backend/openapi.json`; backend/tests/ (Domain.Tests/Disputes/, Api.IntegrationTests/Disputes/Withdraw/, ArchitectureTests/).

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md

## Notes

- Decision: 2026-10-27 — withdraw success is 200 with the updated customer view of the dispute, so the SPA needs no second read.
- Risk high: a final status set wrongly; beyond the criteria, QA checks the transaction row stays not disputable after withdraw.
- Decision: 2026-10-29 — raise and withdraw refusals are audited here, the first story built after both `Audit.Record` (2.8) and the stored-event commit (3.8); a version clash reloads and decides once more (AD-14a, AD-15, ad-009).
- Input (from 3.2 QA finding 1, 2026-10-28): a raise that loses the unique-index race answers 409 correctly, but EF Core logs two `fail:` errors (23505 on `UQ_Disputes_AccountRef_TransactionId`), which would trip an error-rate alert in production; a version clash on `UQ_StoredEvents_DisputeId_Version` logs the same way. Decide whether to filter those log events (EF Core logging configuration) or document them, and add a test for the choice.
- Input (from 2.7 review finding 3, 2026-10-27): each problem's `title` is the same for every occurrence of its type and matches its documentation page (RFC 9457 §3.1.3); `action-not-allowed` first appears here, so add one test over the problem catalogue that every response of a type carries that type's title.
