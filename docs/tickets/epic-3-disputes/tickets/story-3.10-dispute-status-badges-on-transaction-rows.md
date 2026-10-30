---
id: 10
type: story
title: "Dispute status badges on transaction rows"
parent: epic-3-disputes
covers: [R12, R18, R40]
after: [6]
hitl: false
risk: low
---

# Dispute status badges on transaction rows

## Description

A transaction row with a dispute shows that dispute's customer-facing status as a badge that links to the dispute, in place of the Dispute control and of any hint. It uses the row fields `disputeId` and `customerStatus` that story 3.1 added, and needs no contract change. The status label mapping and the badge live in `frontend/src/features/disputes/`, so story 3.7 reuses them on history rows and the detail header instead of writing a second status switch. Until 3.7 adds the dispute route, the badge is a plain anchor, as 3.6's `SubmitFailure.tsx` does. Moved here from story 2.4 (decided 2026-10-28) and split from story 3.6 (decided 2026-10-30).

## Requirements

Verify: Vitest tests pass, with API responses mocked by MSW from `openapi.json` shapes; `npm run typecheck`, `npm run lint` and `npm test` pass in `frontend/` with no new suppression.

Inputs: no request input. The two response fields read are `disputeId` (`format: uuid`, a .NET `Guid`) and `customerStatus` (the closed `CustomerStatus` enum: `submitted`, `underReview`, `underReviewPrioritised`, `resolved`, `withdrawn`). The contract sends both or neither; the SPA treats any other combination, and any value outside the enum, as no dispute to show.

1. **One status mapping, shared, never "Escalated"** (R18, R12; FR-12, FR-17)
   **Given** `describeCustomerStatus(status)` in `frontend/src/features/disputes/customerStatus.ts`
   **When** it is called with each `CustomerStatus` value
   **Then** it returns the label, tone and icon from DESIGN.md's status tone table: `submitted` "Submitted" (info, ➚), `underReview` "Under review" (info, ◔), `underReviewPrioritised` "Under review (prioritised)" (priority, ▲), `resolved` "Resolved" (neutral, ⚑), `withdrawn` "Withdrawn" (neutral, ↩); no value maps to "Escalated"
   **And** it is an exhaustive `switch` whose `default` assigns the value to a `never` variable and reads it in a way that `tsc` (with `noUnusedLocals`) and ESLint (`strictTypeChecked`) both accept with no suppression, so a new enum value fails `tsc` (TypeScript Handbook, *Narrowing: exhaustiveness checking*)
   **And** at runtime the `default` returns `undefined` (no badge), never the raw value: neither `return unreachable;` nor `String(unreachable)` in any text, title or log (2.4's `notDisputableHint` precedent)

2. **A disputed row shows its badge as a link** (R12, R40; FR-12)
   **Given** rows whose `disputeId` and `customerStatus` are both set, one in each `CustomerStatus` value
   **When** the transactions screen renders
   **Then** each row's only trailing control is a plain `<a href={`/disputes/${encodeURIComponent(disputeId)}`}>` around `DisputeStatusBadge` (`frontend/src/features/disputes/DisputeStatusBadge.tsx`, shadcn/ui `Badge` with the tone's `status-*` tokens), marked `// shortcut: a plain link (a full page load) until story 3.7 adds the dispute route`
   **And** the link's accessible name is exactly "Dispute status: {label}": the prefix is visually hidden text and the icon is `aria-hidden`, so the visible label is part of the name (WCAG 2.2 SC 2.5.3 Label in Name; WCAG technique C7)
   **And** the row has no Dispute control and no hint text, because the badge is the treatment for an already-disputed row (EXPERIENCE.md, Copy deck: Not disputable)
   **And** a test asserts the exact href for one fixture ID, and a fixture with `disputeId: '../x?y#z'` gives `/disputes/..%2Fx%3Fy%23z` (RFC 3986 section 2.1; OWASP ASVS 5.0 V1.2.2)

3. **An unknown status or a half-set pair shows no badge** (R18, R12; FR-12)
   **Given** an MSW response with one row whose `customerStatus` is the out-of-contract `'escalated'` and one row with only one of the two fields set, both `disputable: false` and `notDisputableReason: 'alreadyDisputed'`
   **When** the transactions screen renders
   **Then** neither row shows a badge, a Dispute control or hint text, and `queryByText(/escalated/i)` finds nothing on the whole screen; the server's mapping (AD-13) stays the control and this is defence in depth

4. **Rows without a dispute are unchanged** (R12; FR-12)
   **Given** rows whose `disputeId` and `customerStatus` are null
   **When** the transactions screen renders
   **Then** they show no badge; a disputable row keeps its Dispute `Link` (story 3.6) and a not-disputable row keeps its hint text (story 2.4), through the existing `TransactionRow` branches with no new mechanism
   **And** `notDisputableHint` keeps returning no hint for `alreadyDisputed`; only its shortcut comment, which pointed at this story, is removed
   **And** the existing `TransactionsScreen.test.tsx` case that expects no link on an already-disputed row is replaced by criterion 2's test, because the 2026-10-30 decision (S10) changed the expected result; no other assertion changes

5. **The badge reads only the list row** (R12; FR-12)
   **Given** a screen of rows with every status
   **When** it renders
   **Then** the only request made is the transactions list (recorded in the test with MSW's `server.events.on('request:start')`): the plain anchor does no preloading, and the story adds no query key, no request and no storage (MSW, Life-cycle events)
   **And** `disputeId` appears only in the href, never in visible text or the accessible name

## Boundaries

- Must not change: the transactions list contract (no `outcome` on `TransactionResponse`), the row's amount, masked number and date, Load more and the screen's states (story 2.4), and the Dispute `Link` (story 3.6).
- No route is added (no placeholder `/disputes` route): `router.tsx` is story 3.11's and 3.7's. Following the badge before 3.7 lands loads the SPA's not-found screen; that is a QA observation, not a Vitest check, because jsdom does not follow a plain link.
- Not here: the outcome. A resolved row shows plain "Resolved" in the neutral tone with a flag, so it implies no outcome; "Upheld" or "Rejected" shows on the dispute detail (story 3.7). The "Resolved · Upheld" and "Resolved · Rejected" row badges in `docs/ux/mockups/transactions-after.html` and `transactions.html` are superseded for rows by the 2026-10-30 decision below; they still apply on the detail.
- Styling and placement are checked in review against the mockup, not by Vitest: on a phone the badge sits on its own right-aligned line under the row text, so "Under review (prioritised)" never squeezes the merchant name or the amount; from 768px it sits in the action column (DESIGN.md, Transaction row). Reflow at 320 CSS px stays QA's axe and Playwright check (R40).
- No new package: shadcn/ui `Badge` is copied source on the installed `radix-ui` 1.8.1 and `class-variance-authority` 0.7.1; the shadcn CLI adds no dependency. The icon is a text glyph, as the mockups use; no icon library, no remote asset, no CSP change.

## Supervisor checks

Deliberate-break proofs, run by the supervisor in a throwaway copy, not the verifier.

- Map `underReviewPrioritised` to "Escalated": criterion 1's test fails.
- Delete the `resolved` case from `describeCustomerStatus`: `tsc` fails.
- Make the `default` return the raw value: criterion 3's test fails.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Start: from 3.6's merged `HEAD`, never from a 3.6 patch and never dispatched beside 3.6; 3.6 edits all three existing files below.
- Mechanisms and sources: a plain anchor with `encodeURIComponent` (3.6's `SubmitFailure.tsx` precedent; RFC 3986 section 2.1); TypeScript exhaustiveness checking with a `never` assignment (TypeScript Handbook); shadcn/ui `Badge`; visually hidden prefix text (WCAG technique C7); MSW life-cycle events for the request record.
- Packages: none.
- Touches:
  - `frontend/src/features/disputes/customerStatus.ts` (new: `describeCustomerStatus`, with its test)
  - `frontend/src/features/disputes/DisputeStatusBadge.tsx` (new)
  - `frontend/src/components/ui/badge.tsx` (new: shadcn/ui `Badge` source)
  - `frontend/src/features/transactions/TransactionRow.tsx` (the badge link)
  - `frontend/src/features/transactions/notDisputableHint.ts` (the shortcut comment removed)
  - `frontend/src/features/transactions/TransactionsScreen.test.tsx` (the already-disputed case replaced; criteria 2 to 5)
- QA stage, not the builder (QA owns `e2e/`): `e2e/tests/spa-transactions.spec.ts` changes its row rule to: a "Dispute this transaction" link exactly when `disputable`; a "Dispute status: …" link exactly when `disputeId` is set; no hint text on `alreadyDisputed` rows, matching the hint phrases only, not `/dispute/i`.
- Consumed, not edited: `frontend/src/api/generated/`.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-13
- design — docs/ux/DESIGN.md, Colour (status tone table); Components, Status badge and Transaction row
- design — docs/ux/mockups/transactions-after.html
- design — docs/ux/mockups/transactions.html
- experience — docs/ux/EXPERIENCE.md, Copy deck, Component Patterns
- TypeScript — https://www.typescriptlang.org/docs/handbook/2/narrowing.html#exhaustiveness-checking
- shadcn/ui — https://ui.shadcn.com/docs/components/badge
- W3C — https://www.w3.org/TR/WCAG22/#label-in-name
- W3C — https://www.w3.org/WAI/WCAG22/Techniques/css/C7
- IETF — https://www.rfc-editor.org/rfc/rfc3986#section-2.1
- MSW — https://mswjs.io/docs/api/life-cycle-events
- verification — docs/verification/3.10-challenge.md, 3.10-prebuild-review.md, 3.10-prebuild-security.md

## Notes

- Risk low: a wrong badge text is visible and recoverable; R18's "Escalated" rule is the one to protect, and the supervisor breaks above prove it.
- Decision: 2026-10-30 (human, D1) — transaction rows show a plain "Resolved" badge; the outcome shows on the dispute detail; no contract change.
- Decision: 2026-10-30 (Sofia, S10) — the badge alone is the treatment for an already-disputed row; no `alreadyDisputed` hint text (EXPERIENCE.md, Copy deck).
- Decision: 2026-10-30 — the badge is a plain anchor until 3.7, as 3.6's `SubmitFailure.tsx` is, rather than building 3.10 after 3.7; 3.7's Touches carry the badge link for the swap to a typed `Link` (pre-build review).
- Decision: 2026-10-30 — plain "Resolved" on a row is the neutral tone with a flag icon, added to DESIGN.md's status tone table; it implies no outcome.
- Decision: 2026-10-30 — an unknown status renders no badge, never the raw value (pre-build security T2, narrowing the review's two options to the one that returns nothing).
