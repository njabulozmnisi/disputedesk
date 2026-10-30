---
id: 7
type: story
title: "Disputes history screen and the shell's skip link"
parent: epic-3-disputes
covers: [R16, R18, R40]
after: [9, 10, 11]
hitl: false
risk: low
refined: false
---

# Disputes history screen and the shell's skip link

## Description

Adds screen 5, Disputes (`/disputes`): the customer's disputes grouped Open and Closed, newest first, a page of 20 at a time, each row linking to its dispute; the outcome badges ("Resolved · Upheld", "Resolved · Rejected") that this screen and story 3.12's detail share; the shell's skip link; and typed router links in place of the plain `/disputes` anchors. Split from the earlier "history, detail and withdraw" story (decided 2026-10-30); story 3.12 adds the detail, timeline and withdraw.

## Requirements

Verify: Vitest component tests pass, with API responses mocked by MSW from `openapi.json` shapes (story 3.9's regenerated client); every new screen state passes the axe-core check the component tests already run; `npm run typecheck`, `npm run lint` and `npm test` pass in `frontend/` with no new suppression.

Inputs: the route `/disputes` takes no path or search parameter (no `validateSearch`). The SPA sends no `limit` (the API default, 20) and passes `nextCursor` back unchanged as `cursor`; the cursor stays in `useInfiniteQuery` state, never in the URL or storage. Response fields read: `items[]` with `disputeId` (GUID), `customerStatus` (closed enum), `outcome` (`upheld`, `rejected` or null), `reason` (closed enum), `transaction.description`, `disputedAmount`, `createdAt` (RFC 3339 UTC); `nextCursor` (string or null). An enum value outside the contract renders no label, never the raw value.

1. **History grouped Open and Closed** (R16, R18; FR-16, FR-17, NFR-API-5)
   **Given** a linked customer on `/disputes` (inside `SessionGate allow="linked"`) and pages from the generated disputes infinite query
   **When** the screen renders
   **Then** the `h1` is "Disputes" with "Every dispute you've raised, newest first." below it; an "Open" section (`h2`) holds `submitted`, `underReview` and `underReviewPrioritised` disputes and a "Closed" section (`h2`) holds `resolved` and `withdrawn`, each in API order (newest first); a section with no rows is hidden
   **And** each row shows the snapshot's description, the reason label and "raised {d Mon yyyy}" in South African time (the reason labels of story 3.6), the disputed amount through `formatMoney`, and the status badge; the row's link is a TanStack Router `Link` to `/disputes/$disputeId` whose accessible name includes the description and the status label (WCAG 2.2 SC 2.4.4, 2.5.3)
   **And** an item whose `customerStatus` is outside the enum is left out of both groups, and no text on the screen matches `/escalated/i` (AD-13; story 3.10's T2 rule)

2. **Outcome badges, shared with the detail** (R16, R18; FR-16, FR-17)
   **Given** `frontend/src/features/disputes/customerStatus.ts` and `DisputeStatusBadge.tsx` from story 3.10
   **When** a badge is described for a `customerStatus` and an `outcome`
   **Then** `resolved` with `upheld` is "Resolved · Upheld" (success tone, check icon), `resolved` with `rejected` is "Resolved · Rejected" (danger tone, cross icon), `resolved` with null stays "Resolved" (neutral, flag) as on transaction rows, and every other status keeps 3.10's label, tone and icon (DESIGN.md, status tone table; EXPERIENCE.md, Copy deck); the badge gains the `success` and `danger` tones on the existing `status-*` tokens
   **And** the mapping stays one exhaustive `switch` per enum, closed in its `default` with `x satisfies never`, as `customerStatus.ts` already does, with no suppression (TypeScript Handbook, *Narrowing: exhaustiveness checking*); an unknown `outcome` on a resolved dispute gives plain "Resolved"

3. **Load more, focus and announcement** (R16, R40; FR-16, NFR-CODE-6)
   **Given** a first page whose `nextCursor` is set
   **When** the customer presses Load more
   **Then** the generated infinite query fetches the next page; the button shows "Loading…", stays focusable with `aria-disabled` and sends no second request on a second press; after the page arrives focus moves to the first new dispute, found by `disputeId` (not by rendered position, because a new page can add rows to both sections), whose row element has `tabindex="-1"`; "{n} more disputes loaded" ("1 more dispute loaded" for one) is announced through `useAnnounce`; the button is gone when `nextCursor` is null (WCAG 2.2 SC 2.4.3, 4.1.3; EXPERIENCE.md, Load more)
   **And** one test loads a second page holding one open and one closed dispute older than the first page's rows, and asserts focus on the first new row in API order, wherever it renders
   **And** a failed Load more keeps the loaded rows and the button, and shows the problem notice "Something went wrong on our side. Try again." with the `traceId` reference

4. **Loading, empty and error states** (R16, R40; FR-16, NFR-CODE-6)
   **Given** the first page loading, answering with no items, or failing
   **When** the screen renders
   **Then** while loading the `h1` shows at once and the list area holds 5 `Skeleton` rows with `aria-busy="true"`; with no items the screen shows "You haven't disputed any transactions." then "If something on your account looks wrong, find it in your transactions and choose Dispute. It will show here with its status." and a secondary button-styled `Link` "Go to transactions"; on a failure (5xx, network) it shows the problem notice with the `traceId` and "Try again", which refetches (EXPERIENCE.md, State Patterns); the problem's `title` and `detail` are never shown (AD-3)
   **And** the list works while core banking is down (no 503 path: the list reads stored snapshots, story 3.9)

5. **Typed links to Disputes** (R16, R40; FR-16)
   **Given** the plain `<a href="/disputes">` shortcuts in `Navigation.tsx` (top bar and tab bar), `TransactionsScreen.tsx` (empty state), `TransactionsError.tsx` ("View disputes") and `NotFoundPage.tsx` ("Go to your disputes")
   **When** the route exists
   **Then** each becomes a TanStack Router `Link` to `/disputes`, its `// shortcut:` comment is removed, and following it makes no full page load (the test asserts the router navigated with no document reload, as the existing Link tests do)
   **And** the Disputes navigation item marks the current page with `aria-current="page"` on `/disputes`, as Transactions does on `/transactions` (TanStack Router, `Link` active state; WAI-ARIA `aria-current`)

6. **Skip link, first in the document** (R40; NFR-CODE-6)
   **Given** the shell (`RootLayout`) on any screen
   **When** the page first loads and the keyboard user presses Tab
   **Then** focus lands on "Skip to main content", the first focusable element in document order (before the header), hidden until focused and visible when focused; activating it (Enter or click) moves focus to `<main id="main" tabIndex={-1}>` by script, with `preventDefault` on the click so the router sees no hash navigation (WCAG technique G1; WCAG 2.2 SC 2.4.1, 2.4.7)
   **And** after a client-side route change focus is on the new screen's `h1`, as now (EXPERIENCE.md, Accessibility Floor), which is already past the header, so the bypass holds; a test checks the first Tab on initial load and the `h1` focus after a route change separately
   **And** EXPERIENCE.md's Accessibility Floor gains the skip-link line in the same change

## Boundaries

- Must not change: transaction rows and their badges apart from the shared badge mapping (story 3.10), the raise screens (story 3.6), the route-change `h1` focus rule.
- Not here: the dispute detail, timeline and withdraw, the real `/disputes/$disputeId` route (a placeholder route marked `// shortcut:` may exist so the typed links compile; story 3.12 replaces it), the remaining plain `/disputes/<id>` anchors and the `returnUrl` rule for the detail (story 3.12); browser spans for this screen (story 3.11 covers raise only).
- No dispute data goes into the URL, `localStorage` or `sessionStorage`, and no TanStack Query persister is added (pre-build security S4).

## Supervisor checks

Deliberate-break proofs, run by the supervisor, not the verifier.

- In a throwaway copy, focus the first new row by index in the Open section: criterion 3's two-section test fails.
- In a throwaway copy, move the skip link after the header: criterion 6's first-Tab test fails.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Mechanisms and sources: TanStack Query `useInfiniteQuery` through the Orval-generated hook (<https://tanstack.com/query/latest/docs/framework/react/guides/infinite-queries>); TanStack Router `createRoute` and `Link` (<https://tanstack.com/router/latest/docs/framework/react/guide/navigation>); the one polite live region `useAnnounce` (story 2.4); WCAG 2.2 SC 2.4.1 and technique G1 (<https://www.w3.org/WAI/WCAG22/Techniques/general/G1>); SC 2.4.3, 4.1.3; `Intl.DateTimeFormat` with `timeZone: 'Africa/Johannesburg'` as `format.ts` does.
- Packages: none.
- Touches:
  - `frontend/src/features/disputes/history/` (new: the screen, its row, its tests)
  - `frontend/src/features/disputes/customerStatus.ts`, `DisputeStatusBadge.tsx`, `frontend/src/components/ui/badge.tsx` (outcome badges and the two tones; story 3.10's files) and their tests
  - `frontend/src/app/router.tsx` (the `/disputes` route), `frontend/src/app/RootLayout.tsx` (skip link, `main` target), `frontend/src/app/Navigation.tsx`
  - `frontend/src/features/transactions/TransactionsScreen.tsx`, `TransactionsError.tsx`, `TransactionsScreen.test.tsx` (typed links)
  - `frontend/src/features/transactions/format.ts` and `format.test.ts` (the "d Mon yyyy" day, if no existing formatter gives it)
  - `frontend/src/pages/NotFoundPage.tsx`
  - `frontend/src/app/routing.test.tsx` (`/disputes` is no longer not-found for a linked customer; `/disputes/abc` stays not-found until 3.12)
  - `frontend/src/test/support.tsx` (a disputes-page MSW helper and a summary fixture)
  - `docs/ux/EXPERIENCE.md` (Accessibility Floor: the skip link)
  - `e2e/tests/spa-transactions.spec.ts` (QA stage: any expectation of the not-found screen at `/disputes` or of the old first Tab target)
- Reuses, not rewrites: `describeCustomerStatus` and `DisputeStatusBadge` (story 3.10), `ProblemNotice`, `Page`, `usePageHeading`, the Load more pattern of `TransactionsScreen.tsx`; no second status switch.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md, section Requirements
- design — docs/ux/mockups/disputes.html
- design — docs/ux/mockups/dispute-loading.html
- design — docs/ux/DESIGN.md, status tone table
- experience — docs/ux/EXPERIENCE.md, Copy deck, Component Patterns, State Patterns, Accessibility Floor
- architecture — docs/architecture/architecture.md, AD-13, AD-31
- verification — docs/verification/3.7-challenge.md, docs/verification/3.7-prebuild-review.md, docs/verification/3.7-prebuild-security.md

## Notes

- Risk low (proposed): a read-only screen whose data the API already scopes to the customer; a wrong grouping or badge misleads but changes nothing.
- Decision: 2026-10-30 (Sofia, refine) — the earlier story passed 8 criteria once the review and challenge points were folded in, so it splits into this history screen (3.7) and the detail, timeline and withdraw screen (3.12), built in that order.
- Decision: 2026-10-30 (Sofia, refine) — the skip link is first in document order and moves focus to `main` by script; route changes keep focusing the `h1` (challenger gap 4, pre-build review).
- Input (from 2.4 QA, 2026-10-28): each route change focuses the `<h1>` and there was no skip link; criterion 6 adds one.
- Waits on 3.9 (the list operation and regenerated client), 3.10 (`customerStatus.ts` and the badge) and 3.11 (both edit `frontend/src/test/support.tsx`; collision check 2026-10-30).
- Moved out (challenger, simplest design, 2026-10-30): the 3.6 round-2 cleanups (`FieldSet` and `FieldLegend` in `field.tsx`; `text-body` on the reason label in `RaiseScreen.tsx`) are tied to no criterion here; they are now in the epic's Notes as a Small-tier change.
- Resolution: 2026-10-30 (Sofia, after the 3.7 verify) — wording aligned with what was accepted, no behaviour change: criterion 2's exhaustiveness check is the repo convention, an exhaustive `switch` closed with `x satisfies never` (`customerStatus.ts`, accepted in 3.10; TypeScript Handbook, *Narrowing: exhaustiveness checking*), replacing "a `never` variable that is also read"; the Boundaries line now allows the placeholder `/disputes/$disputeId` route marked `// shortcut:` until 3.12 (docs/verification/3.7-verify.md, Supervisor disposition). No PRD requirement or architecture decision changed.
