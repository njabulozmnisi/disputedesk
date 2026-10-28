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
   **Then** the form is built with shadcn/ui's Form component (React Hook Form with a zod schema), the six reasons from the copy deck show as a Radix `RadioGroup`, the note counts to 1,000 UTF-16 code units after trimming (AD-4), and the expected amount appears and is required only for wrong amount, below the transaction amount with at most 2 decimals

2. **Review and submit once** (R15; FR-15)
   **Given** the review screen
   **When** Submit dispute is chosen twice or retried after a network error
   **Then** every attempt for that draft sends the same `Idempotency-Key` as an RFC 9651 Structured Field String, the quoted value of `crypto.randomUUID()` (`Idempotency-Key: "<uuid>"`), Submit ignores presses while the mutation is pending, and a new draft gets a new key
   **And** a 409 `idempotency-in-progress` keeps the spinner and resends the same request after 1 second, up to 5 times, as EXPERIENCE.md sets

3. **Outcomes routed by problem type** (R13; FR-13)
   **Given** responses 201, 409 `already-disputed`, 422 `not-disputable` and 503 `core-banking-unavailable`
   **When** they arrive
   **Then** 201 invalidates the transactions and disputes queries (`invalidateQueries`), so the row shows "Submitted", and goes to the dispute detail; 409 and 422 show their EXPERIENCE.md messages with `role="alert"`; 422 `idempotency-mismatch` shows as an unexpected error; and 503 offers Retry with the draft kept

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
   **Then** each error shows under its field, tied with `aria-describedby` and `aria-invalid`, and an error summary at the top links to each field and takes focus (EXPERIENCE.md; WCAG 2.2 SC 3.3.1)
   **And** a 400 `validation` response's `errors` show on the same fields

7. **Links to screens not built yet** (R13; FR-13)
   **Given** story 3.7 is not merged
   **When** a 201 goes to the dispute detail, or "View dispute" is chosen after a 409
   **Then** the not-found screen renders until 3.7 adds the route

8. **Dispute badges on transaction rows** (R12; FR-12)
   **Given** rows whose `disputeId` and `customerStatus` are set (story 3.1), in every customer status
   **When** the transactions screen renders
   **Then** each shows its status badge as `docs/ux/mockups/transactions.html` and `transactions-after.html` show, with the EXPERIENCE.md label (Escalated shows as "Under review (prioritised)"), has no Dispute control, and shows the `alreadyDisputed` reason text; a row with null fields shows no badge
   **And** moved here from story 2.4 (decided 2026-10-28), because the row fields arrive with 3.1

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- The Aspire Dashboard shows one trace from `dispute.raise.submit` to the Api raise span.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Touches: `frontend/src/features/disputes/raise/`; `frontend/src/features/transactions/` (dispute badges); `frontend/src/telemetry/`; frontend/src/app/ (routes); frontend/src/components/ui/ (shadcn/ui Form); frontend/package.json (React Hook Form, zod and the resolver, each verified and pinned per AGENTS.md); `frontend/src/api/generated/`.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md
- design — docs/ux/mockups/dispute-new.html
- design — docs/ux/mockups/dispute-new-wrong-amount.html
- design — docs/ux/mockups/dispute-review.html
- design — docs/ux/mockups/dispute-review-retry.html
- design — docs/ux/mockups/transactions-after.html
- design — docs/ux/mockups/transactions.html
- experience — docs/ux/EXPERIENCE.md, Copy deck

## Notes

- Decision: 2026-10-27 — forms use shadcn/ui's documented Form component (React Hook Form with zod); the packages are verified (publisher, downloads, repository, latest stable) and pinned exactly when added (AGENTS.md, AD-31).
- Decision: 2026-10-27 — the `Idempotency-Key` value is an RFC 9651 Structured Field String, so the SPA sends it quoted (AD-16).
- Decision: 2026-10-27 — links to screens not built yet render the not-found screen until their stories land.
