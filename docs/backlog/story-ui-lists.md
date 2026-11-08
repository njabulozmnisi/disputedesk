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
lede omits the 90 days, day headings sit above the rows instead of as bands, and the loading skeleton does not match
the columns. `TransactionRow.tsx` has no column grid, and its Dispute link is named "Dispute this transaction", not
after the merchant. `features/disputes/history/DisputeRow.tsx` has no chevron column. This spec lays both lists out
in the theme's columns. The detail skeleton is `ui-detail-start`'s, so this spec never touches
`DisputeDetailScreen.tsx`.

- Tier: Feature. Owner: frontend. Stacking: on `feature/ui-notices-states`; builds beside `ui-linking`,
  `ui-raise-review` and `ui-detail-start` (no shared file).
- Presentation only: no API, route, auth, session, idempotency or retry change.
- Covers: HANDOFF 3.2, 3.3, 3.13 (transactions); DESIGN Components (List panel and rows, Transaction row);
  EXPERIENCE Component Patterns (Transaction row, Dispute row), State Patterns (Loading); PRD FR-11, FR-12, FR-16,
  NFR-CODE-6.

## Inputs

No new request inputs: this spec is presentation only, and no request body, query, header or route parameter
changes. No new UI input. Load more, keyset paging and focus after loading are unchanged.

## Criteria

1. **The lede and the column header** (HANDOFF 3.2; EXPERIENCE Transaction row)
   **Given** `/transactions` with rows
   **When** it renders
   **Then** the lede reads "Your card and account transactions from the last 90 days, newest first."; the rows sit
   on one panel; from 768px the panel opens with a header row (Transaction, Amount, Dispute) that has
   `aria-hidden="true"` and is absent from the accessibility tree (unit test with `getByRole` queries).
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
   **And** a resolved dispute's badge is plain Resolved with `Flag`, never the outcome. Mechanism: CSS grid with
   responsive variants. Source: Tailwind v4, grid-template-columns
   (<https://tailwindcss.com/docs/grid-template-columns>) and Responsive design
   (<https://tailwindcss.com/docs/responsive-design>).

4. **The Dispute link is a 44px target named after the merchant** (HANDOFF 3.2; EXPERIENCE Transaction row)
   **Given** a disputable row
   **When** it renders
   **Then** its one link reads "Dispute" plus `ChevronRight`, is at least 44px tall (`--tap-target`), is underlined
   on hover only, and its accessible name is "Dispute {merchant}" (for example "Dispute ZXP*GLOBALSVCS 0800"), with
   "Dispute" first. Mechanism: visible label inside the accessible name. Source: WCAG 2.2 SC 2.5.3
   (<https://www.w3.org/WAI/WCAG22/Understanding/label-in-name>) and SC 2.5.5
   (<https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced>).

5. **A disputes row is one link with a chevron** (HANDOFF 3.3; EXPERIENCE Dispute row)
   **Given** `/disputes` with open and closed disputes
   **When** a row renders
   **Then** it is one `<a>` (one tab stop) whose name reads merchant, reason, date, amount and status; the merchant
   is `primary` 700; hover tints the row with `background` and underlines the merchant; from 768px it uses the
   transaction columns plus a 20px `ChevronRight` column; below 768px the badge drops to its own line and the
   chevron is hidden. Mechanism: one link per row. Source: WCAG 2.2 SC 2.4.4
   (<https://www.w3.org/WAI/WCAG22/Understanding/link-purpose-in-context>).

6. **The transactions skeleton matches the columns** (HANDOFF 3.13; EXPERIENCE State Patterns, Loading)
   **Given** the first load of `/transactions`
   **When** the skeleton shows
   **Then** it has the column header, day bands and rows with three skeleton bars in the real columns, and
   `aria-busy` is set as it is today. Mechanism: shadcn Skeleton. Source:
   <https://ui.shadcn.com/docs/components/skeleton>; MDN, `aria-busy`
   (<https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA/Reference/Attributes/aria-busy>).

## Boundaries

- Presentation only: no change to API calls, the generated client, routes, auth, session, idempotency, retry, query
  keys or telemetry; Load more and its focus move are unchanged.
- Theme tokens only: no raw hex or px value in a component (the column widths come from `--amount-col` and
  `--action-col`). Icons: Lucide, 16px unless HANDOFF names another size, 2px stroke, `currentColor`,
  `aria-hidden="true"`.
- Touches (under `frontend/src/`): `features/transactions/TransactionsScreen.tsx`, `TransactionRow.tsx`,
  `features/disputes/history/DisputeRow.tsx`, `DisputesScreen.tsx` (not the empty state, which area 2 owns), and
  their tests. Not `DisputeDetailScreen.tsx`.

## Tests and commits

- QA updates `e2e/tests/spa-transactions.spec.ts`, `spa-disputes-history.spec.ts` and
  `spa-dispute-status-badges.spec.ts` (line 228 selects `span.tabular-nums`), and any other spec that names the
  Dispute link "Dispute this transaction".
- The pull request holds two commits: the presentation change, then `test(ui-lists): update assertions for the UI
  refresh` with every unit-test and e2e assertion update. The squash merge joins them on main.

## Verify

- Builder, in `frontend/`: `npm run typecheck`, `lint`, `format`, `test` and `build` pass; the glyph grep
  (`grep -rn '[➚◔▲✓✕↩ⓘ⚠☰⚑]' frontend/src`) prints nothing; the diff has no raw hex or px in a component.
- QA, holding the `stack` lock: `e2e/run-regression.sh` passes with axe on every spec that runs it; with the
  Playwright MCP each changed screen matches its mockup at 360px and 1280px in light and dark, and at 1280px every
  amount, Dispute link and badge lines up in its column; no horizontal scroll at 320px or 200% zoom; every
  interactive element is at least 44px tall; one tab stop per disputes row, and the column header is not announced.
