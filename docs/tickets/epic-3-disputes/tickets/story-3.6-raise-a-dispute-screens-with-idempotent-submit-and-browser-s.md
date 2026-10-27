---
id: 6
type: story
title: "Raise-a-dispute screens with idempotent submit and browser spans"
parent: epic-3-disputes
covers: [R12, R13, R14, R15, R36, R40]
after: [2.4, 2.5, 2]
hitl: false
risk: medium
---

# Raise-a-dispute screens with idempotent submit and browser spans

## Description

Adds the raise and review screens (4a, 4b) with reason, note and expected amount, one `Idempotency-Key` per draft, error routing, and the SPA's OpenTelemetry spans for SM-1 and SM-2.

## Requirements

Verify: Vitest component tests pass, with API responses mocked from `openapi.json` shapes.

1. **Raise form rules** (R13, R14; FR-13, FR-14)
   **Given** a disputable transaction
   **When** the customer picks a reason and continues
   **Then** the six reasons from the copy deck show, the note counts to 1,000 characters, and the expected amount appears and is required only for wrong amount, below the transaction amount with at most 2 decimals

2. **Review and submit once** (R15; FR-15)
   **Given** the review screen
   **When** Submit dispute is chosen twice or retried after a network error
   **Then** every attempt for that draft sends the same `Idempotency-Key`, and a new draft gets a new key

3. **Outcomes routed by problem type** (R13; FR-13)
   **Given** responses 201, 409 `already-disputed`, 422 `not-disputable` and 503 `core-banking-unavailable`
   **When** they arrive
   **Then** 201 goes to the dispute detail and the transaction row shows "Submitted", 409 and 422 show their EXPERIENCE.md messages, and 503 offers Retry with the draft kept

4. **No Dispute control on non-disputable rows** (R12; FR-12)
   **Given** a pending row
   **When** the transactions screen renders
   **Then** it has no Dispute control and its route redirects to Transactions

5. **Browser trace and raise spans** (R36; NFR-OBS-1, NFR-OBS-3)
   **Given** the SPA's OpenTelemetry web SDK
   **When** the raise screen opens and the submit returns
   **Then** same-origin `/api` fetches carry `traceparent`, spans `dispute.raise.open` and `dispute.raise.submit` export to `/otlp/`, and no attribute holds personal data

6. **Accessible form errors** (R40; NFR-CODE-6)
   **Given** invalid input
   **When** the form is submitted
   **Then** each error is labelled and linked to its field, and focus moves to the first error

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- The Aspire Dashboard shows one trace from `dispute.raise.submit` to the Api raise span.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Touches: `frontend/src/features/disputes/raise/`; `frontend/src/telemetry/`; frontend/src/app/ (routes); `frontend/package.json`; `frontend/src/api/generated/`.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md
- design — docs/ux/mockups/dispute-new.html
- design — docs/ux/mockups/dispute-new-wrong-amount.html
- design — docs/ux/mockups/dispute-review.html
- design — docs/ux/mockups/dispute-review-retry.html
- design — docs/ux/mockups/transactions-after.html
- experience — docs/ux/EXPERIENCE.md, Copy deck
