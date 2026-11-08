---
id: 8
type: story
title: "Dispute detail shows when it was disputed, and the start page reassures"
sources: [docs/ux/HANDOFF.md, docs/ux/DESIGN.md, docs/ux/EXPERIENCE.md, docs/ux/mockups/dispute-submitted.html, docs/ux/mockups/dispute-under-review.html, docs/ux/mockups/dispute-rejected.html, docs/ux/mockups/dispute-withdraw.html, docs/ux/mockups/dispute-loading.html, docs/ux/mockups/start.html, docs/ux/mockups/app.css, docs/conventions/frontend.md, docs/prd/prd.md]
companions: [docs/verification/ui-detail-start-prebuild-review.md, docs/verification/decisions.md]
after: [story-ui-notices-states]
refined: true
hitl: false
risk: low
decisions: settled
---

# Dispute detail shows when it was disputed, and the start page reassures

## Description

The sixth area of the UI refresh (HANDOFF section 6, step 6). In `features/disputes/detail/DisputeDetailScreen.tsx`
the summary has no "Disputed on" row, Withdraw sits mid-card with no consequence sentence, the "Dispute submitted"
banner has no icon, the rejected guidance is one paragraph (line 29), and the loading skeleton is two generic bars.
`Timeline.tsx` repeats the badge text in each entry and joins actor and time with " · ". The withdraw dialog has no
hairline and no phone layout, and the start page has no assurance line. "Disputed on" comes from the detail
response's `createdAt` (decided 2026-11-08, HANDOFF 3.10): the contract guarantees it, while the timeline's order
is a mapping convention.

- Tier: Feature. Owner: frontend. Stacking: on `feature/ui-notices-states`; builds beside `ui-lists`, `ui-linking`
  and `ui-raise-review` (no shared file). The detail skeleton is here, not in `ui-lists`.
- Presentation only: no API, route, auth, session, idempotency or retry change; `createdAt` is already in
  `DisputeResponse`, so no API field is added.
- Covers: HANDOFF 3.10, 3.11, 3.12, 3.13 (detail); DESIGN Components (Dispute summary card, Timeline, Rejected
  guidance, Start page); EXPERIENCE Component Patterns (Timeline, Withdraw), State Patterns (Just submitted, Loading),
  Responsive & Platform; PRD FR-17, FR-18, NFR-CODE-6.

## Inputs

No new request inputs: this spec is presentation only, and no request body, query, header or route parameter
changes. UI inputs are unchanged: the withdraw note stays optional, at most 500 characters after trimming.

## Criteria

1. **"Disputed on" comes from `createdAt`** (HANDOFF 3.10; FR-17)
   **Given** a dispute whose `createdAt` is `2026-10-25T22:30:00Z` (00:30 SAST on 26 Oct)
   **When** the summary renders
   **Then** a row "Disputed on" after "Transaction date" reads "26 Oct 2026", formatted in South African time by
   `formatDayWithYear` in `features/transactions/format.ts`; it never reads `timeline[0]`
   **And** a unit test proves it with a timeline whose first entry has a different time. Mechanism: the existing
   formatter over `Intl.DateTimeFormat`. Source: MDN, `Intl.DateTimeFormat`
   (<https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat>).

2. **Withdraw sits at the card's foot with its consequence** (HANDOFF 3.10; EXPERIENCE Withdraw; FR-18)
   **Given** an open dispute
   **When** the summary renders
   **Then** after a hairline at the card's foot comes "Changed your mind? You can withdraw while the dispute is open.
   You can't dispute this transaction again after you withdraw." then the Withdraw button; closed disputes show
   neither; focus return after the dialog is unchanged. Mechanism: theme tokens. Source: WCAG 2.2 SC 2.4.3
   (<https://www.w3.org/WAI/WCAG22/Understanding/focus-order>).

3. **The just-submitted banner is a success notice** (HANDOFF 3.10; EXPERIENCE Just submitted)
   **Given** the detail opened straight after a submit
   **When** it renders
   **Then** "Dispute submitted" is a success `Alert` with `CircleCheck`, static, with no second announcement.
   Mechanism: shadcn Alert (area 2). Source: <https://ui.shadcn.com/docs/components/alert>; WCAG 2.2 SC 4.1.3
   (<https://www.w3.org/WAI/WCAG22/Understanding/status-messages>).

4. **The timeline is a flat surface of copy-deck lines** (HANDOFF 3.10; DESIGN Timeline; EXPERIENCE Timeline)
   **Given** `Timeline.tsx`
   **When** it renders
   **Then** it is a flat surface (`card`, hairline, `rounded-lg`) with the `h2` "Timeline" and an ordered list; each
   entry shows the copy-deck line only, no badge text ("The bank agreed with you.", "The bank didn't uphold your
   dispute."); actor and time sit with no separator character; the latest entry has the 16px `primary` dot with a 4px
   `primary-subtle` halo; open disputes end with "We'll update the timeline as your dispute moves." below a hairline
   inside the surface. `<time datetime>` stays. Mechanism: ordered list semantics. Source: WCAG 2.2 SC 1.3.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/info-and-relationships>).

5. **Rejected guidance is its own surface** (HANDOFF 3.10; DESIGN Rejected guidance)
   **Given** a rejected dispute
   **When** the detail renders
   **Then** below the timeline a flat surface has the `h2` "If you disagree with this outcome" and an ordered list of
   two steps: "You can complain to the bank first." and "If you're still not satisfied after the bank has answered
   your complaint, you can take it to the National Financial Ombud Scheme South Africa, an independent service that
   is free for customers."; below 1024px card, timeline and guidance stack. Mechanism: responsive variants. Source:
   Tailwind v4, Responsive design (<https://tailwindcss.com/docs/responsive-design>).

6. **The detail skeleton matches the layout** (HANDOFF 3.13; EXPERIENCE State Patterns, Loading)
   **Given** the first load of `/disputes/:id`
   **When** the skeleton shows
   **Then** it is a skeleton summary card plus a timeline surface with three entries (a dot and two bars each), and
   `aria-busy` is set as today. Mechanism: shadcn Skeleton. Source: <https://ui.shadcn.com/docs/components/skeleton>.

7. **The withdraw dialog has a hairline and a phone sheet** (HANDOFF 3.11; EXPERIENCE Withdraw)
   **Given** `WithdrawDialog.tsx` and `components/ui/alert-dialog.tsx`
   **When** the dialog opens
   **Then** the buttons sit below a hairline; below 768px the content is a bottom sheet through responsive classes;
   initial focus is still "Keep dispute", Esc still keeps the dispute, and success still announces "Dispute
   withdrawn" and moves focus to the `h1`. Mechanism: Radix Alert Dialog. Source:
   <https://www.radix-ui.com/primitives/docs/components/alert-dialog>;
   <https://ui.shadcn.com/docs/components/alert-dialog>.

8. **The start page carries the assurance line** (HANDOFF 3.12; DESIGN Start page)
   **Given** `pages/StartPage.tsx`
   **When** it renders
   **Then** under the Sign in and Create a login buttons a 13px caption with `Lock` reads "You'll confirm it's you
   with your ID number, account number and a one-time code." Mechanism: Lucide icon beside text. Source:
   <https://lucide.dev/guide/packages/lucide-react>.

## Boundaries

- Presentation only: no change to API calls, the generated client, routes, auth, session, idempotency, retry, query
  keys or telemetry; the withdraw request, its 409-as-success handling and the dialog's focus rules are unchanged.
- Theme tokens only: no raw hex or px value in a component. Icons: Lucide, 16px unless HANDOFF names another size,
  2px stroke, `currentColor`, `aria-hidden="true"`.
- Touches (under `frontend/src/`): `features/disputes/detail/DisputeDetailScreen.tsx` (not `NotFoundCard`, which area
  2 owns), `Timeline.tsx`, `features/disputes/withdraw/WithdrawDialog.tsx`, `components/ui/alert-dialog.tsx`,
  `pages/StartPage.tsx` (the assurance line only), and their tests.

## Tests and commits

- QA updates `e2e/tests/dispute-detail.spec.ts`, `seeded-disputes.spec.ts`, `operations-actions.spec.ts` (the history
  badge test at line 366), `journeys/uj-1.spec.ts` and `journeys/uj-5b.spec.ts`, and any other spec that asserts a
  timeline entry's badge text or the " · " separator.
- The pull request holds two commits: the presentation change, then `test(ui-detail-start): update assertions for the
  UI refresh` with every unit-test and e2e assertion update. The squash merge joins them on main.

## Verify

- Builder, in `frontend/`: `npm run typecheck`, `lint`, `format`, `test` and `build` pass; the glyph grep
  (`grep -rn '[➚◔▲✓✕↩ⓘ⚠☰⚑]' frontend/src`) prints nothing; the diff has no raw hex or px in a component.
- QA, holding the `stack` lock: `e2e/run-regression.sh` passes with axe on every spec that runs it; with the
  Playwright MCP each detail state, the withdraw dialog and the start page match their mockups at 360px and 1280px in
  light and dark; no horizontal scroll at 320px or 200% zoom; every interactive element is at least 44px tall;
  keyboard order is unchanged.
