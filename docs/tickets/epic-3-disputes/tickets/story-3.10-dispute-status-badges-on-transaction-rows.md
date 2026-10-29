---
id: 10
type: story
title: "Dispute status badges on transaction rows"
parent: epic-3-disputes
covers: [R12, R18]
after: [6]
hitl: false
risk: low
---

# Dispute status badges on transaction rows

## Description

A transaction row with a dispute shows that dispute's customer-facing status as a badge that links to the dispute, in place of the Dispute control and of any hint. It uses the row fields `disputeId` and `customerStatus` that story 3.1 added, and needs no contract change. Moved here from story 2.4 (decided 2026-10-28) and split from story 3.6 (decided 2026-10-30).

## Requirements

Verify: Vitest component tests pass, with API responses mocked by MSW from `openapi.json` shapes; `npm run typecheck`, `npm run lint` and `npm test` pass in `frontend/`.

1. **A badge for every customer status** (R12, R18; FR-12)
   **Given** rows whose `disputeId` and `customerStatus` are set, one in each `CustomerStatus` value
   **When** the transactions screen renders
   **Then** each row shows a status badge with the text `submitted` "Submitted", `underReview` "Under review", `underReviewPrioritised` "Under review (prioritised)", `resolved` "Resolved", `withdrawn` "Withdrawn", and no row ever shows the word "Escalated"
   **And** the badge is a TanStack Router `Link` to `/disputes/<disputeId>` with the accessible name "Dispute status: {status}", styled as `docs/ux/mockups/transactions-after.html` shows; the row has no Dispute control and no hint text, because the badge is the treatment for an already-disputed row (EXPERIENCE.md, Copy deck: Not disputable)
   **And** following the badge renders the not-found screen until story 3.7 adds the detail route
   **And** the status-to-text mapping is an exhaustive `switch` over `CustomerStatus` whose default is the assignment `const unreachable: never = status;`, so a new value fails `tsc`

2. **Rows without a dispute are unchanged** (R12; FR-12)
   **Given** rows whose `disputeId` and `customerStatus` are null
   **When** the transactions screen renders
   **Then** they show no badge; a disputable row keeps its Dispute `Link` (story 3.6) and a not-disputable row keeps its hint text (story 2.4)
   **And** the 2.4 shortcut that returned no hint for `alreadyDisputed` is replaced: an `alreadyDisputed` row shows its badge, and its hint stays empty

## Boundaries

- Must not change: the transactions list contract (no `outcome` on `TransactionResponse`), the row's amount, masked number and date, Load more and the screen's states (story 2.4), and the Dispute `Link` (story 3.6).
- Not here: the outcome. A resolved row shows plain "Resolved"; "Upheld" or "Rejected" shows on the dispute detail (story 3.7). The mockup's and EXPERIENCE.md's "Resolved · Upheld/Rejected" row text is superseded for rows by the 2026-10-30 decision below.

## Supervisor checks

Deliberate-break proofs, run by the supervisor in a throwaway copy, not the verifier.

- Map `underReviewPrioritised` to "Escalated": criterion 1's test fails.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Mechanisms and sources: TanStack Router `Link`; TypeScript exhaustiveness checking with a `never` assignment (TypeScript Handbook, *Narrowing: exhaustiveness checking*); not the bare `satisfies never;` statement, which ESLint's `no-unused-expressions` does not see (2.4 review, carried in story 3.7's Notes).
- Packages: none.
- Touches:
  - `frontend/src/features/transactions/TransactionRow.tsx` (the badge)
  - `frontend/src/features/transactions/notDisputableHint.ts` (the `alreadyDisputed` shortcut removed)
  - `frontend/src/features/transactions/` (new: the status badge and its label mapping, and tests beside `TransactionsScreen.test.tsx`)
- Consumed, not edited: `frontend/src/api/generated/`.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md, section Requirements
- design — docs/ux/mockups/transactions-after.html
- design — docs/ux/mockups/transactions.html
- design — docs/ux/DESIGN.md, Components
- experience — docs/ux/EXPERIENCE.md, Copy deck, Component Patterns
- verification — docs/verification/3.6-prebuild-review.md

## Notes

- Risk low: a wrong badge text is visible and recoverable; R18's "Escalated" rule is the one to protect, and the supervisor break above proves it.
- Decision: 2026-10-30 (human, D1) — transaction rows show a plain "Resolved" badge; the outcome shows on the dispute detail; no contract change.
- Decision: 2026-10-30 (Sofia, S10) — the badge alone is the treatment for an already-disputed row; no `alreadyDisputed` hint text (EXPERIENCE.md, Copy deck).
