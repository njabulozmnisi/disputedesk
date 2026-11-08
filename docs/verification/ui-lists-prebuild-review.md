# ui-lists: pre-build review

- Date: 2026-11-08
- Story: `docs/backlog/story-ui-lists.md` (branch `feature/ux-refresh-docs`, worktree
  `/Users/njabulomnisi/Projects/Dojo/DisputeDesk-ux-docs`, commit eccb33d)
- Read against: `docs/ux/HANDOFF.md` (3.2, 3.3, 3.13, 5, 6), `docs/ux/DESIGN.md` (Components: List panel and rows,
  Transaction row; Touch targets), `docs/ux/EXPERIENCE.md` (Copy deck, Component Patterns, State Patterns, Touch,
  Accessibility Floor), `docs/ux/mockups/app.css` and the `transactions*.html` and `disputes*.html` mockups,
  `docs/prd/prd.md` (FR-11, FR-12, FR-16, NFR-CODE-6, Seed data), `docs/architecture/architecture.md`,
  `docs/conventions/frontend.md`, `docs/verification/decisions.md` (2026-11-08 rows), the base specs
  `ui-tokens-icons` and `ui-notices-states` and the sibling `ui-raise-review`, and the code under `frontend/src`
  (`features/transactions/**`, `features/disputes/history/**`, `DisputeStatusBadge.tsx`, `customerStatus.ts`,
  `components/ui/badge.tsx`, `skeleton.tsx`, `index.css`, `theme/tokens.css`) with its unit tests and the e2e specs
  that select these rows; Pass 1, Pass 2 and the security checklist.
- axe-core 4.14.0 (`frontend/node_modules/axe-core/axe.js`, `ariaProhibitedAttrEvaluate`) read for P2.
- Result: **changes**. One medium `[decision]` point, four medium `[story]` points, five low `[story]` points.

## Points

### P1 [decision] medium: the new lede says "from the last 90 days", but the list shows older transactions

Criterion 1 sets the lede to "Your card and account transactions from the last 90 days, newest first." (HANDOFF 3.2,
EXPERIENCE Copy deck line 105). The list is not limited to 90 days: FR-11 says "There are no filters", the seed gives
each customer "one older than 90 days" (PRD, Seed data, line 514), FR-12's `outsideWindow` rows are listed with the
hint "Older than 90 days. Contact the bank about this transaction." (`notDisputableHint.ts:24`), and
`TransactionsListTests.cs:204` lists `TXN-A-0001`, 95 days old. The mockup itself shows a 20 Jul row with that hint
under the new lede. The copy states a filter the product does not apply, on the same screen as a row that disproves it.

EXPERIENCE.md wins on copy (HANDOFF, Sources of truth), so the human decides: keep the lede as written, keep today's
"Your card and account transactions, newest first.", or soften it (for example "from about the last 90 days").
Criterion 1, EXPERIENCE line 105 and HANDOFF 3.2 then carry the answer.

### P2 [story] medium: the skeleton's `aria-busy` element is unnamed in the story, and the mockup's form is invalid ARIA

Criterion 6 adds the column header and day bands to the skeleton and keeps "`aria-busy` … as it is today". Today the
panel is the list: `<ul aria-busy="true" aria-label="Loading transactions">` (`TransactionsScreen.tsx:20`), and the
unit test finds it by `getByRole('list', { name: 'Loading transactions' })` with 5 list items
(`TransactionsScreen.test.tsx:509-533`). With a header inside the panel that structure cannot stay as is:

- the mockup moves `aria-busy` and `aria-label` to `<div class="panel">` (`transactions-loading.html`). ARIA 1.2
  prohibits naming the `generic` role, and axe 4.14's `aria-prohibited-attr` fails a named generic with no text;
- a `div` header placed directly in the `ul` fails axe's `list` rule (a list holds only `li`);
- a day band drawn as an `h2` holding only a skeleton bar is an empty heading (axe `empty-heading`).

Fix: criterion 6 names the structure, for example the panel `div` (no ARIA) holding the `aria-hidden` header and
the `<ul aria-busy="true" aria-label="Loading transactions">`, whose band placeholders are `aria-hidden` (not
headings) so the list still exposes exactly the 5 skeleton rows; and it names the unit test (the list by name,
`aria-busy`, 5 items, the header hidden) plus the existing axe test at `TransactionsScreen.test.tsx:535`. Sources: MDN
`aria-busy`; WAI-ARIA 1.2, generic role, prohibited `aria-label` (<https://www.w3.org/TR/wai-aria-1.2/#generic>).

### P3 [story] medium: the status-badge link on a transaction row is below the 44px floor and no criterion covers it

DESIGN.md (Touch targets, line 317) and HANDOFF 5 require every interactive element to be at least 44px tall, and
this story's own QA check says so. On a disputed row the action is the badge link
(`TransactionRow.tsx:85-91`): `py-2` around a badge of `py-0.5` and 14px label type, about 40px today; the mockup's
`a.badge` (`app.css`, `.badge` padding 3px) is about 26px. Criterion 4 makes only the Dispute link 44px, so QA's check
would fail on a row the story moves into the action column.

Fix: criterion 3 (or 4) adds that the badge link is at least `--tap-target` tall (for example
`inline-flex min-h-(--tap-target) items-center`), the badge itself unchanged, left-aligned in the action column from
768px and on its own right-aligned line below. Source: WCAG 2.2 SC 2.5.5
(<https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced>), DisputeDesk's 44px floor.

### P4 [story] medium: the Dispute link's rename reaches files outside Touches, one shared with `ui-raise-review`

Criterion 4 renames the link from "Dispute this transaction" to "Dispute {merchant}". Testing Library and Playwright
`exact` matches on the old name fail in:

- `frontend/src/features/disputes/raise/raiseTelemetry.test.tsx:267`, `:406`, `:428` (unit test, not in Touches);
- `e2e/tests/dispute-raise.spec.ts:98`, `:331`, `:628`, `:632` (covered only by "any other spec");
- the listed `spa-transactions.spec.ts:225-232`, `:328-332` and `spa-dispute-status-badges.spec.ts:100`, `:201`.

`ui-raise-review` renames "Change" to "Change your dispute", which breaks `raiseTelemetry.test.tsx:256`, in the test
next to line 267, though that spec says the file stays unchanged. So the two stories edit neighbouring tests of one
file, and the claim "no shared file" with `ui-raise-review` does not hold (AGENTS.md, Dispatch rules, collision check).

Fix: Touches lists `raiseTelemetry.test.tsx` (the three link lookups only); Tests and commits names
`dispute-raise.spec.ts`; the Description records the shared file with `ui-raise-review` (the one that merges second
rebases). Two cautions for the builder and QA: "Dispute this transaction" is also the raise screen's `h1`
(`RaiseScreen.tsx:59`, `:146`), and those heading assertions stay; and the new Dispute link and the badge links
("Dispute status: …") share the prefix "Dispute ", so a Playwright `name` without `exact: true`, or a regex such as
`/Dispute/`, matches both. Select `Dispute ${description}` exactly.

### P5 [story] medium: the 768px and 44px bounds have no named test on either side

Criteria 1, 3 and 5 turn on a 768px bound (header shown, three columns, chevron shown and badge in its column), and
criterion 4 on a 44px height. jsdom has no layout, so unit tests can only assert class strings; the story leaves the
proof to QA's manual Playwright MCP pass. The known pitfall applies: a test on each side of every bound a criterion
names. The e2e suite already measures this way (`spa-dispute-status-badges.spec.ts:172-243`, bounding boxes at 1280
and 390, scroll width at 320; `linking-screens.spec.ts:226`).

Fix: name the e2e checks QA adds, for `/transactions` and `/disputes`: at 767px the header and chevron are hidden
and the badge sits on its own line under the text; at 768px and 1280px the header shows, every amount's right edge and
every action's left edge match down the list, and the disputes chevron shows; the Dispute link and the badge link are
at least 44px tall; no horizontal scroll at 320px.

### P6 [story] low: hovering a disputes row underlines the badge label as well as the merchant

`DisputeStatusBadge` underlines its label with `group-hover:underline`, and the disputes row link already carries
`group` (`DisputeRow.tsx:34`). Criterion 5 adds a merchant underline on row hover, so both would underline; the mockup
underlines only the merchant (`app.css`, `.drow:hover .drow__title`). Fix: criterion 5 says whether the badge label
underlines on a disputes row; if not, a named group on the row (`group/row`, `group-hover/row:underline` on the
merchant) leaves the transaction row's badge link behaviour alone. Source: Tailwind v4, Differentiating nested groups
(<https://tailwindcss.com/docs/hover-focus-and-other-states#differentiating-nested-groups>).

### P7 [story] low: the 20px chevron column is a raw px value

Criterion 5's 20px column breaks the "no raw hex or px value in a component" boundary unless it is written in the
spacing scale. Fix: name the value (Tailwind `5`, that is `--spacing(5)`, 1.25rem). The icon in it stays 16px with a
2px stroke (the mockup draws 2.5; the boundary wins).

### P8 [story] low: the skeleton's shape is loose

"Three skeleton bars in the real columns" against the mockup's four (title and caption in the text column, amount,
action); the row count (EXPERIENCE State Patterns: 5) and the number of bands (mockup: 2) are not stated. Fix: state
the bars per row, 5 rows and 2 bands.

### P9 [story] low: the mockup's badge prefix and the disputes skeleton could tempt changes outside Touches

The disputes mockup prefixes the badge with "Status: "; `DisputeStatusBadge` says "Dispute status:" and is shared with
the transaction rows and detail, whose e2e names rely on it. EXPERIENCE State Patterns (Loading) asks for skeleton rows
matching the layout on Disputes too, which this story covers by its "Covers" line, but HANDOFF 3.13 names only
transactions and detail and there is no disputes-loading mockup. Fix: state that `DisputeStatusBadge` is unchanged
(so the disputes row name reads "… Dispute status: Submitted") and that the disputes skeleton stays as it is.

### P10 [story] low: criterion 3's "And" is already true, so it needs no new code

`TransactionResponse` has no outcome, `TransactionRow.tsx:50-52` calls `describeCustomerStatus` without one, and
`TransactionsScreen.test.tsx:383-400` proves "Resolved" for `resolved`; `Flag` arrives with `ui-tokens-icons`. Fix: map
the clause to that test in the report rather than adding code.

## Pass 1 notes

- Criteria are testable and tagged; each names its mechanism and source. P5 is the gap in proof.
- Load more, its focus move to the `li` (`tabIndex={-1}`) and the announcement are unchanged by the design; the
  disputes row stays one `<a>` in a focusable `li`, so one tab stop per row holds.
- The disputes row's DOM order (merchant, caption, amount, badge, chevron) already gives the accessible name order
  criterion 5 asks for; the chevron is `aria-hidden`.
- At 768px the content is about 672px wide; three columns (144 + 224 + 2 x 24) leave about 256px for the text, and
  about 212px with the chevron column. The widest badge, "Under review (prioritised)", fits 14rem with `whitespace-nowrap`.

## Pass 2 and standards

- No new abstraction, component, package or option: two components gain a grid, one skeleton is redrawn. Nothing
  belongs in `docs/production-path.md`.
- Mechanisms and sources: `aria-hidden` (MDN), headings (WCAG 1.3.1), CSS grid with responsive variants (Tailwind v4),
  label in name (WCAG 2.5.3), 44px target (WCAG 2.5.5 and the DisputeDesk floor), one link per row (WCAG 2.4.4), shadcn
  Skeleton and `aria-busy` (MDN). All documented; none home-grown. P2 and P6 add WAI-ARIA 1.2 and Tailwind named groups.

## Security checklist

Presentation only: no endpoint, session, cookie, header, CSP or package change. The merchant in the Dispute link's
visually hidden text is the description already shown in the row, rendered as React text (escaped). The browser
telemetry (`frontend/src/telemetry/`) has no user-interaction instrumentation, so link names reach no span. No
findings.
