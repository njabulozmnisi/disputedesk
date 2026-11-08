---
id: 5
type: story
title: "Transactions and disputes line up in columns on one panel"
sources: [docs/ux/HANDOFF.md, docs/ux/DESIGN.md, docs/ux/EXPERIENCE.md, docs/ux/mockups/transactions.html, docs/ux/mockups/transactions-loading.html, docs/ux/mockups/disputes.html, docs/ux/mockups/app.css, docs/conventions/frontend.md, docs/prd/prd.md]
companions: [docs/verification/ui-lists-prebuild-review.md, docs/verification/decisions.md]
after: [story-ui-notices-states]
refined: true
hitl: false
risk: low
decisions: settled
---

# Transactions and disputes line up in columns on one panel

## Description

The third area of the UI refresh (HANDOFF section 6, step 3). In `features/transactions/TransactionsScreen.tsx` the
lede does not say which transactions can be disputed, day headings sit above the rows instead of as bands, and the loading skeleton does not match
the columns. `TransactionRow.tsx` has no column grid, and its Dispute link is named "Dispute this transaction", not
after the merchant. `features/disputes/history/DisputeRow.tsx` has no chevron column. This spec lays both lists out
in the theme's columns. The detail skeleton is `ui-detail-start`'s, so this spec never touches
`DisputeDetailScreen.tsx`.

- Tier: Feature. Owner: frontend. Stacking: on `feature/ui-notices-states`; builds beside `ui-linking`,
  `ui-raise-review` and `ui-detail-start`. No shared file with `ui-linking` or `ui-detail-start`.
- Shared files with `ui-raise-review` (AGENTS.md, Dispatch rules, collision check): both stories are stacked
  siblings on `feature/ui-notices-states` and both edit `frontend/src/features/disputes/raise/raiseTelemetry.test.tsx`
  and `e2e/tests/dispute-raise.spec.ts`, in different lines. This spec owns only the Dispute link lookups: in
  `raiseTelemetry.test.tsx` lines 267, 406 and 428 (the test "is recorded a second time when the customer leaves to
  /transactions and enters again" and the two "hold no seeded value" tests); in `dispute-raise.spec.ts` lines 98,
  331, 628 and 632. `ui-raise-review` owns the "Change" lookups (`raiseTelemetry.test.tsx` line 256;
  `dispute-raise.spec.ts` lines 311, 386, 450, 456 and 735). Neither spec changes the raise screen's `h1` assertions
  ("Dispute this transaction"). Whichever of the two merges second rebases onto main and resolves any conflict,
  keeping both renames.
- Presentation only: no API, route, auth, session, idempotency or retry change.
- Covers: HANDOFF 3.2, 3.3, 3.13 (transactions; the disputes skeleton stays as it is, see Boundaries); DESIGN Components (List panel and rows, Transaction row);
  EXPERIENCE Component Patterns (Transaction row, Dispute row), State Patterns (Loading); PRD FR-11, FR-12, FR-16,
  NFR-CODE-6.

## Inputs

No new request inputs: this spec is presentation only, and no request body, query, header or route parameter
changes. No new UI input. Load more, keyset paging and focus after loading are unchanged.

## Criteria

1. **The lede and the column header** (HANDOFF 3.2; EXPERIENCE Transaction row)
   **Given** `/transactions` with rows
   **When** it renders
   **Then** the lede reads exactly "Your card and account transactions, newest first. You can dispute those from the
   last 90 days." (decided 2026-11-08, `docs/verification/decisions.md`; the list still shows rows older than 90
   days, as FR-11 has no filters); the rows sit on one panel; from 768px the panel opens with a header row
   (Transaction, Amount, Dispute) that has `aria-hidden="true"` and is absent from the accessibility tree
   **And** a new unit test in `TransactionsScreen.test.tsx`, "shows the lede and lists a row older than 90 days",
   asserts the lede with `getByText(…, { exact: true })` and that an `outsideWindow` row is still listed; a second
   asserts that no `columnheader` and none of the header's labels is reachable by `getByRole` queries.
   Mechanism: `aria-hidden`. Source: MDN, `aria-hidden`
   (<https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA/Reference/Attributes/aria-hidden>).

2. **Day bands inside the panel** (HANDOFF 3.2; DESIGN List panel and rows)
   **Given** rows on more than one day
   **When** the list renders
   **Then** each day heading is a band inside the panel: `background` fill, 14/700 `muted-foreground`, hairlines
   above and below; it stays an `h2` with the same text ("Mon 26 Oct"). Mechanism: headings. Source: WCAG 2.2
   SC 1.3.1 (<https://www.w3.org/WAI/WCAG22/Understanding/info-and-relationships>).

3. **Three columns from 768px** (HANDOFF 3.2; DESIGN Transaction row)
   **Given** a viewport of 768px or more
   **When** a transaction row renders
   **Then** it is a grid of `minmax(0,1fr) var(--amount-col) var(--action-col)` with a `--space-6` gap: text, the
   amount right-aligned and tabular, then the action left-aligned; below 768px the structure is unchanged (amount
   top right, Dispute under it, any badge on its own right-aligned line)
   **And** on a disputed row the status-badge link is at least `--tap-target` (44px) tall (`inline-flex
   min-h-(--tap-target) items-center` on the link; the badge itself unchanged), left-aligned in the action column
   from 768px and on its own right-aligned line below
   **And** a resolved dispute's badge is plain Resolved with `Flag`, never the outcome. This clause holds today and
   needs no new code: the existing `TransactionsScreen.test.tsx` case for `resolved` ("Resolved", in "dispute status
   badge on a row") proves it, and the report maps it there. Mechanism: CSS grid with
   responsive variants. Source: Tailwind v4, grid-template-columns
   (<https://tailwindcss.com/docs/grid-template-columns>) and Responsive design
   (<https://tailwindcss.com/docs/responsive-design>); WCAG 2.2 SC 2.5.5
   (<https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced>) and DisputeDesk's 44px floor (DESIGN Touch
   targets).

4. **The Dispute link is a 44px target named after the merchant** (HANDOFF 3.2; EXPERIENCE Transaction row)
   **Given** a disputable row
   **When** it renders
   **Then** its one link reads "Dispute" plus `ChevronRight`, is at least 44px tall (`--tap-target`), is underlined
   on hover only, and its accessible name is "Dispute {merchant}" (for example "Dispute ZXP*GLOBALSVCS 0800"), with
   "Dispute" first (a visually hidden merchant after the visible "Dispute"). Tests select it as
   `Dispute ${description}` with `exact: true`: the badge links ("Dispute status: …") share the prefix "Dispute ", so a
   name without `exact: true`, or a regex such as `/Dispute/`, matches both. Mechanism: visible label inside the
   accessible name. Source: WCAG 2.2 SC 2.5.3
   (<https://www.w3.org/WAI/WCAG22/Understanding/label-in-name>) and SC 2.5.5
   (<https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced>).

5. **A disputes row is one link with a chevron** (HANDOFF 3.3; EXPERIENCE Dispute row)
   **Given** `/disputes` with open and closed disputes
   **When** a row renders
   **Then** it is one `<a>` (one tab stop) whose name reads merchant, reason, date, amount and status; the merchant
   is `primary` 700; hover tints the row with `background` and underlines the merchant only, not the badge label
   (the row link carries the named group `group/row` instead of today's plain `group`, with
   `group-hover/row:underline` on the merchant, so `DisputeStatusBadge`'s plain `group-hover:underline` fires only
   on the transaction row's badge link); from 768px it uses the transaction columns plus a `ChevronRight` column of
   spacing `5` (`--spacing(5)`, 1.25rem; the icon 16px with a 2px stroke); below 768px the badge drops to its own
   line and the chevron is hidden. Mechanism: one link per row. Source: WCAG 2.2 SC 2.4.4
   (<https://www.w3.org/WAI/WCAG22/Understanding/link-purpose-in-context>); Tailwind v4, Differentiating nested
   groups (<https://tailwindcss.com/docs/hover-focus-and-other-states#differentiating-nested-groups>).

6. **The transactions skeleton matches the columns** (HANDOFF 3.13; EXPERIENCE State Patterns, Loading)
   **Given** the first load of `/transactions`
   **When** the skeleton shows
   **Then** the panel is a plain `div` with no ARIA attribute, holding the column header (`aria-hidden="true"`, from
   768px) and then `<ul aria-busy="true" aria-label="Loading transactions">`; inside the list, 2 day-band placeholders
   (`aria-hidden="true"`, not headings, one skeleton bar each) and 5 skeleton rows, each an `li` with four bars:
   title and caption in the text column, one in the amount column, one in the action column; so the list exposes
   exactly the 5 rows
   **And** the existing unit test (`TransactionsScreen.test.tsx`, the list found by
   `getByRole('list', { name: 'Loading transactions' })`) asserts `aria-busy="true"`, exactly 5 list items and the
   header hidden; the existing axe test on the skeleton (`TransactionsScreen.test.tsx`, from line 535) passes.
   Mechanism: shadcn Skeleton; `aria-busy` on the list; `aria-hidden` on decoration. Source:
   <https://ui.shadcn.com/docs/components/skeleton>; MDN, `aria-busy`
   (<https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA/Reference/Attributes/aria-busy>); WAI-ARIA 1.2,
   generic role, naming prohibited (<https://www.w3.org/TR/wai-aria-1.2/#generic>), which is why the name stays on the
   `ul` and not on the panel `div` as the mockup draws it.

## Boundaries

- Presentation only: no change to API calls, the generated client, routes, auth, session, idempotency, retry, query
  keys or telemetry; Load more and its focus move are unchanged.
- Theme tokens only: no raw hex or px value in a component (the column widths come from `--amount-col` and
  `--action-col`). Icons: Lucide, 16px unless HANDOFF names another size, 2px stroke, `currentColor`,
  `aria-hidden="true"`.
- Touches (under `frontend/src/`): `features/transactions/TransactionsScreen.tsx`, `TransactionRow.tsx`,
  `features/disputes/history/DisputeRow.tsx`, `DisputesScreen.tsx` (not the empty state, which area 2 owns), and
  their tests; `features/disputes/raise/raiseTelemetry.test.tsx`, only the three Dispute link lookups (lines 267, 406
  and 428), in the test commit. Not `DisputeDetailScreen.tsx`.
- `DisputeStatusBadge` is unchanged: the disputes mockup's "Status: " prefix is not built, so a disputes row's name
  ends "… Dispute status: Submitted", as the transaction rows and detail rely on.
- The disputes skeleton stays as it is (no disputes-loading mockup; HANDOFF 3.13 names transactions and detail only).
- "Dispute this transaction" stays the raise screen's `h1` (`RaiseScreen.tsx`); its heading assertions in unit and
  e2e tests are unchanged.

## Tests and commits

- QA updates `e2e/tests/spa-transactions.spec.ts` (lines 225 to 232 and 328 to 332), `spa-disputes-history.spec.ts`,
  `spa-dispute-status-badges.spec.ts` (lines 100 and 201; line 228 selects `span.tabular-nums`) and
  `dispute-raise.spec.ts` (the Dispute link lookups only: lines 98, 331, 628 and 632, shared with `ui-raise-review`,
  see Description), each selecting `Dispute ${description}` with `exact: true`.
- QA adds layout checks on each side of the bounds, measured with bounding boxes as
  `spa-dispute-status-badges.spec.ts` already does, for `/transactions` and `/disputes`: at 767px the column header
  and the disputes chevron are hidden and the badge sits on its own line under the text; at 768px and 1280px the
  header shows, every amount's right edge and every action's left edge match down the list, and the disputes chevron
  shows; the Dispute link and the badge link are at least 44px tall; no horizontal scroll at 320px.
- The pull request holds two commits: the presentation change, then `test(ui-lists): update assertions for the UI
  refresh` with every unit-test and e2e assertion update. The squash merge joins them on main.

## Verify

- Builder, in `frontend/`: `npm run typecheck`, `lint`, `format`, `test` and `build` pass; the glyph grep
  (`grep -rn '[➚◔▲✓✕↩ⓘ⚠☰⚑]' frontend/src`) prints nothing; the diff has no raw hex or px in a component.
- QA, holding the `stack` lock: `e2e/run-regression.sh` passes with axe on every spec that runs it; with the
  Playwright MCP each changed screen matches its mockup at 360px and 1280px in light and dark, and at 1280px every
  amount, Dispute link and badge lines up in its column; no horizontal scroll at 320px or 200% zoom; every
  interactive element is at least 44px tall; one tab stop per disputes row, and the column header is not announced.

## Sofia's refine

Sofia, 2026-11-08, on `docs/verification/ui-lists-prebuild-review.md`. P1 `[decision]` answered by the human
(`docs/verification/decisions.md`, 2026-11-08); every `[story]` point fixed with the reviewer's recommended fix:

- P1 medium, lede: criterion 1 uses "Your card and account transactions, newest first. You can dispute those from
  the last 90 days." and names its new unit test, which also lists a row older than 90 days.
- P2 medium, skeleton ARIA: criterion 6 names the structure (plain panel `div`, hidden header, the named
  `aria-busy` list, hidden band placeholders that are not headings, exactly 5 rows) and its unit and axe tests;
  WAI-ARIA 1.2 generic role cited.
- P3 medium, badge link 44px: criterion 3 makes the badge link at least `--tap-target` tall and places it.
- P4 medium, rename reach: Touches adds the three `raiseTelemetry.test.tsx` lookups; Tests and commits names the
  `dispute-raise.spec.ts` lines; the Description records the shared files with `ui-raise-review` and which lines each
  owns; the `h1` stays; tests select the new name exactly.
- P5 medium, bounds untested: Tests and commits names QA's e2e checks at 767px, 768px, 1280px, 44px and 320px.
- P6 low, hover underline: criterion 5 underlines the merchant only, through the named group `group/row`.
- P7 low, raw px: the chevron column is spacing `5` (1.25rem).
- P8 low, skeleton shape: four bars per row, 5 rows, 2 bands.
- P9 low, outside Touches: `DisputeStatusBadge` and the disputes skeleton are unchanged (Boundaries).
- P10 low, already true: criterion 3 maps the Resolved clause to the existing test; no new code.
