---
id: 7
type: story
title: "Disputes history, dispute detail and withdraw screens"
parent: epic-3-disputes
covers: [R16, R18, R19, R40]
after: [4, 6]
hitl: false
risk: medium
---

# Disputes history, dispute detail and withdraw screens

## Description

Adds the Disputes screen grouped open and closed, the dispute detail with timeline, Withdraw where allowed, and the guidance after a Rejected outcome.

## Requirements

Verify: Vitest component tests pass, with API responses mocked from `openapi.json` shapes.

1. **History grouped open and closed** (R16; FR-16)
   **Given** loaded pages of disputes
   **When** the Disputes screen renders
   **Then** open and closed groups show newest first with reason, amount, status badge and outcome, with Load more on `useInfiniteQuery`, focus moved to the first new row and the new rows announced politely (WCAG 2.2 SC 2.4.3, 4.1.3)

2. **Timeline as the customer sees it** (R16, R18; FR-17, NFR-API-5)
   **Given** a dispute detail
   **When** it renders
   **Then** entries show oldest first in an ordered list, each with the status label, South African time in a `<time datetime>` holding the UTC instant, "You" or "The bank" and any note, and `underReviewPrioritised` reads "Under review (prioritised)"

3. **Withdraw offered only while open** (R19; FR-18, FR-20)
   **Given** disputes in each customer status
   **When** the detail renders
   **Then** Withdraw appears for Submitted, Under review and Under review (prioritised) only, and confirming sends an optional note of up to 500 characters
   **And** success invalidates the dispute and list queries and announces "Dispute withdrawn"; a 409 `action-not-allowed` with `customerStatus` `withdrawn` counts as success (FR-18), and any other 409 shows the status from `customerStatus`

4. **Rejected guidance** (R16; FR-17)
   **Given** a dispute resolved Rejected
   **When** the detail renders
   **Then** it shows how to complain to the bank and then the National Financial Ombud Scheme, as EXPERIENCE.md words it

5. **Not found is one screen** (R17; FR-17)
   **Given** a 404 on a dispute or an unknown route
   **When** it renders
   **Then** the same not-found screen shows for both

6. **Accessible dialog** (R40; NFR-CODE-6)
   **Given** the withdraw dialog, a shadcn/ui `AlertDialog` (WAI-ARIA APG alert dialog pattern)
   **When** it opens, and closes by Escape or "Keep dispute"
   **Then** focus moves into it, stays inside, and returns to Withdraw on close

7. **Skip link** (R40; NFR-CODE-6)
   **Given** any screen of the shell, these screens included
   **When** the keyboard user presses Tab first after a route change
   **Then** the first focusable element is a visible-on-focus "Skip to main content" link that moves focus to the main region, so the header can be bypassed (WCAG 2.2 SC 2.4.1 Bypass Blocks)

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- None beyond the story flow.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Touches: `frontend/src/features/disputes/history/, detail/, withdraw/`; frontend/src/app/ (routes, the shell's skip link).

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md
- design — docs/ux/mockups/disputes.html
- design — docs/ux/mockups/dispute-under-review.html
- design — docs/ux/mockups/dispute-withdrawn.html
- design — docs/ux/mockups/dispute-loading.html
- design — docs/ux/mockups/not-found.html
- experience — docs/ux/EXPERIENCE.md, Copy deck

## Notes

- Input (from 2.4 QA, 2026-10-28): each route change focuses the `<h1>`, so the first Tab skips the header and there is no skip link; criterion 7 adds one. Also from 2.4's review: the bare `reason satisfies never;` statement passes ESLint only because `no-unused-expressions` does not unwrap `satisfies`; write the exhaustiveness check as an assignment (`const unreachable: never = reason;`) where this story touches it.
