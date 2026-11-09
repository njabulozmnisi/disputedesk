# ui-lists: post-build review

- Date: 2026-11-08
- Story: `docs/backlog/story-ui-lists.md` (origin/main), pre-build review `docs/verification/ui-lists-prebuild-review.md`,
  `docs/verification/decisions.md` (ui-lists P1, the lede, 2026-11-08), `docs/ux/mockups/app.css` (`.panel`, `.day`,
  `.row`, `.dispute-link`, `.drow`).
- Change: uncommitted in `.claude/worktrees/agent-a72913a67169034e9` on 2f897c3, which also holds the two
  uncommitted bases (ui-tokens-icons, ui-notices-states). The story was separated by comparing `frontend/src` with
  `.claude/worktrees/agent-a1758a16625231b46` (bases only): exactly the ten `story_files` of
  `docs/verification/ui-lists-build.json` differ, nothing else.
- Results file: `TestResults/ui-lists-vitest.junit.xml`, 727 tests, 0 failures; every test the report maps is in it.
  `scripts/diff-hash.sh <worktree> 2f897c3` recomputed here: `3c8a8b8405b1`, the hash the report names.
- Tier: Feature (below High-stakes), so the security checklist ran here.
- Result: **approve**. No high finding. One medium finding owned by QA (e2e lines the rename leaves passing
  vacuously); it needs no builder change. Two low findings.

## Findings

### M1 medium (QA): e2e absence checks on the old link name would pass vacuously, and two of them are outside the spec's QA list

The Dispute link is now named `Dispute {merchant}`. Lookups of the old name `Dispute this transaction` that assert
presence fail loudly, but those that assert absence or branch on a count pass without proving anything:

- `e2e/tests/journeys/uj-2.spec.ts:118`, `toHaveCount(0)`: not in the spec's Tests and commits list;
- `e2e/tests/journeys/uj-1.spec.ts:103`, `toHaveCount(0)`: not in the list (lines 69 and 75 fail loudly);
- `e2e/tests/spa-transactions.spec.ts:330-332`: `disputeLinkExpected` becomes false and the `if` skips the
  assertion. This one is in the spec's list.

The builder named all of them in its assumptions (it may not edit e2e). If QA's brief carries only the spec's list,
the two journey checks stay green while checking nothing (AGENTS.md, "Never weaken a test").
Fix: QA's brief adds `uj-1.spec.ts:69, 75, 103` and `uj-2.spec.ts:118` to the spec's lines. Each lookup becomes
`Dispute ${description}` with `exact: true`. No builder fix round is needed.

### L1 low: the skeleton's day-band placeholders are `div` elements directly inside the `ul`

`frontend/src/features/transactions/TransactionsScreen.tsx:56-58`. The HTML content model of `ul` allows only `li`
and script-supporting elements (<https://html.spec.whatwg.org/multipage/grouping-content.html#the-ul-element>). The
axe pass does not show the structure is valid: axe 4.14's `only-listitems` check skips children that are hidden from
screen readers (`node_modules/axe-core/axe.js:30502`), so an `aria-hidden` `div` is never tested. There is no effect on
assistive technology, because the band is hidden and the list still exposes its 5 rows. WCAG 2.2 dropped SC 4.1.1, so
this does not fail WCAG. Criterion 6 asks for "2 day-band placeholders (`aria-hidden="true"`, not headings)" inside the
list and does not require a `div`. The report's "(the spec)" overstates this.
Fix: `<li aria-hidden="true" className={bandClass}>`. The markup is the same size and valid. The list still exposes 5
`listitem`s, the test's `:scope > [aria-hidden="true"]` still counts 2, and `first-of-type` still picks the first band.

### L2 low: on a phone, the bottom of the Dispute link's focus ring is clipped on the panel's last row

`frontend/src/features/transactions/TransactionRow.tsx:97` with `TransactionsScreen.tsx:20`. `-my-3` cancels the
row's `py-3`, as the mockup's `.dispute-link` does. For a one-line description, row 2's height is set by the link, so
the link's border box ends exactly at the `li`'s bottom edge. Arithmetic: amount 20.8px, gap 4px, link margin box
20px, against a text column of 24px + 18.2px. The global `:focus-visible` ring sits 2px outside the box and is 3px
thick (`index.css:43-46`). On the last row of the panel, the new `overflow-hidden` therefore clips the ring's bottom
side. The other three sides stay visible, so SC 2.4.7 still holds. The builder's smoke checked ring placement for the
disputes rows only. This finding comes from the CSS; I did not check it in a browser.
Fix: add `focus-visible:-outline-offset-3` to the Dispute link, the same remedy the diff already uses for the rows.

## Points judged, not findings

- **Files outside Touches** (`src/test/raise.tsx`, `ReviewScreen.test.tsx`, `signOut.test.tsx`): these edits are
  forced and minimal. The new link name breaks the first two, and the visually hidden merchant, which criterion 4
  requires, repeats the description, so a bare `getByText(description)` matches two elements. Leaving the tree green
  requires these edits, and the builder declared them under `conflicts`. The pre-build review (P4) missed them; see
  Escapes.
- **35 `getByText(…, { selector: 'p' })` changes** (31 in `TransactionsScreen.test.tsx`, 4 in `signOut.test.tsx`):
  `selector` is a documented ByText option (<https://testing-library.com/docs/queries/bytext/>). Each query still finds
  the visible description, and none was an absence check, so no assertion is weakened. The one changed absence check,
  `TransactionsScreen.test.tsx:554`, replaces a text regex on the old name with a role query on the new name, beside
  the existing `links.toHaveLength(1)`. It is equivalent.
- **`overflow-hidden` on the panel with `focus-visible:-outline-offset-3`**: this matches the mockup's `.panel { overflow:
  hidden }`. Without the inset offset, the transaction `li` (focused after Load more), the disputes `li` and the
  full-width disputes row link would lose the left and right sides of their 2px-offset ring. The offset is the right
  remedy, and `-outline-offset-3` is a standard Tailwind v4 utility. The one place it does not reach is L2.
- **`-my-3 -mr-1` on the phone Dispute link**: this reproduces `app.css` `.dispute-link` exactly (margins of minus
  `--space-3`, minus `--space-1` on the right, padding `--space-1`), and `md:m-0` matches the 768px reset. The 44px box
  stays inside the `li`, overlaps only the non-interactive amount by about 8px, and overlaps no other target. The
  coupling to `ledgerRow`'s `py-3` is commented at line 92. Its only side effect is L2.
- **Chevron stroke 2 against the mockup's 2.5**: the spec's Boundaries set "2px stroke" for icons, and pre-build P7
  recorded that the boundary wins over the mockup. This is correct as built.
- **The disputes amount as a `p` and the badge wrapper as a `div`**: the test at `DisputesScreen.test.tsx:131-147`
  proves the row name has a space before the status. The `p` and `div` are block content inside an `<a>`, which HTML
  allows.
- **`exact: true` in the spec for Testing Library**: `ByRoleOptions` has no `exact`, and a string `name` already
  matches the whole name. The builder's conflict is correct. The test at `TransactionsScreen.test.tsx:254` shows
  `/^Dispute /` matching both links and the full name matching one.

## Lines shared with the sibling ui-raise-review

The sibling's current tree (`.claude/worktrees/agent-a0a85c91ff0f853f5`) was compared with the same base. No line is
touched by both stories.

- `raiseTelemetry.test.tsx`: the sibling changes 256 ("Change your dispute"). This story changes 267, 406 and 428.
  Lines 257 to 266 stay unchanged between the two hunks, so a three-way merge is clean.
- `ReviewScreen.test.tsx`: the sibling changes 65-79, 128, 138, 209, 219-220 and appends from 637. This story changes
  355 to 357 (the `openRaiseFor` lookup). The spec's Description names only `raiseTelemetry.test.tsx` and
  `dispute-raise.spec.ts` as shared, but this file is shared too.
- The sibling's tree still holds the old link name at `src/test/raise.tsx:52`, `ReviewScreen.test.tsx:358` and
  `raiseTelemetry.test.tsx:267, 406, 428`. These are this story's lines, so whichever story merges second keeps this
  story's version of them. The sibling's new tests reach the raise screen through `openRaiseScreen` and
  `openReviewScreen`, which this story updated generically (`Dispute ${first.description}`). None of them looks up a
  merchant on `/transactions` by text. Neither story touches `DisputesScreen.test.tsx`, `TransactionsScreen.test.tsx`
  or `signOut.test.tsx` on the other's lines.

## Pass 1: correctness

| Criterion | Proof | Holds |
|---|---|---|
| 1 lede, header `aria-hidden` | "shows the lede and lists a row older than 90 days" (exact lede per decisions.md, `outsideWindow` row and hint listed); "hides the column header…" (`aria-hidden`, `isInaccessible` on each label, no `columnheader`) | yes |
| 2 day bands | "draws each day heading as a band…" (both `h2`s in the one panel with the header, band classes, the first row's `border-t` as the hairline below) | yes |
| 3 columns, badge link 44px | three "the row columns" tests (classes on each side of `md:`); the existing `resolved` case proves the Resolved clause | yes, in jsdom by class. The 767/768/1280 bounds are QA's e2e by the spec (pre-build P5) |
| 4 Dispute link | name `Dispute ZXP*GLOBALSVCS 0800`, `sr-only` merchant after the visible "Dispute", `min-h-(--tap-target)`, `no-underline hover:underline`, decorative chevron; the prefix test | yes |
| 5 disputes row | one link and its full name; chevron `hidden md:block md:col-start-4`, the four-column grid with `--spacing(5)`; `group/row`, `hover:bg-background`, `text-primary group-hover/row:underline`, not `group` | yes |
| 6 skeleton | plain panel with no role or `aria-*`, hidden header first, 2 hidden bands of 1 bar, no heading, 5 `li`s of 4 bars; the existing `aria-busy` and axe tests | yes (see L1 on the band element) |

- Every new test fails without its change. The builder's nine breaks each target one clause. The verifier runs them.
- No API, route, query key, telemetry or Load more change. The focus move to the first new `li` and its announcement
  are untouched. The `section` wrapper per day was dropped, but an unnamed `section` has the generic role, so the
  accessibility tree does not change, and no e2e selects it.
- Theme tokens only: no raw hex or px in a class (`px` appears in comments only). The glyph grep prints nothing.
- The backend items in Pass 1 (Location, ProblemDetails, ownership, events, `!`) do not apply: the change is
  presentation only.

## Pass 2: simplicity

- `ledgerColumns` and `ledgerRow` are shared class strings used by the header, the rows and the skeleton rows, which
  is three real uses. The disputes row writes its four-column grid inline, which is plain duplication of one string and
  fine.
- No new component file, option, wrapper or abstraction. `ColumnHeader` is a local function used twice.
- `src/test/raise.tsx:52`'s `first?.description ?? ''` exists only for `noUncheckedIndexedAccess`. A missing row would
  still fail loudly at `findByRole`. Not a finding.
- Nothing belongs in `docs/production-path.md`.

## Standards check

| Mechanism | Standard | Home-grown? |
|---|---|---|
| Decorative header and band placeholders | `aria-hidden` (MDN) | no |
| Day bands as headings | WCAG 2.2 SC 1.3.1 | no |
| Column grid with responsive variants, `--spacing(5)` | Tailwind v4 grid-template-columns, responsive design | no |
| Merchant in the link name after "Dispute" | WCAG 2.2 SC 2.5.3 | no |
| 44px targets | WCAG 2.2 SC 2.5.5, DESIGN Touch targets | no |
| One link per disputes row | WCAG 2.2 SC 2.4.4 | no |
| `group/row` | Tailwind v4, differentiating nested groups | no |
| Skeleton and `aria-busy` on the named list | shadcn Skeleton, MDN `aria-busy`, WAI-ARIA 1.2 generic role | no |
| Ring inside a clipped panel | CSS `outline-offset` (WCAG 2.2 SC 2.4.7) | no |

Every mechanism fits in one sentence.

## Security checklist

Presentation only: no endpoint, session, cookie, antiforgery, header, CSP or package change from this story (the
`package.json` changes belong to the bases). The visually hidden merchant is the description the row already shows,
rendered as React text, so it is escaped. The browser telemetry records no link names. `scripts/secret-scan.sh` run in
the worktree over the whole uncommitted change: no leaks found. No findings.

## Escapes (for the capture)

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
|---|---|---|---|---|
| The sr-only merchant duplicates the description, breaking bare `getByText(description)` in `TransactionsScreen.test.tsx` and `signOut.test.tsx`; the new link name also breaks `ReviewScreen.test.tsx:355` and `src/test/raise.tsx:52` | builder | pre-build review (P4) | spec gap | the pre-build review greps the whole of `frontend/src` and `e2e/` for every renamed name and for every text a change duplicates |
| `uj-1.spec.ts:69, 75, 103` and `uj-2.spec.ts:118` missing from the QA list (M1) | builder, reviewer | pre-build review (P4) | spec gap | same as above |
| The spec's `exact: true` for Testing Library `getByRole` is not an option | builder | pre-build review | spec gap | a story names a library option only after checking the installed types |

```json
{"status": "approve", "record": "docs/verification/ui-lists-review.md", "findings": [{"severity": "medium", "file": "e2e/tests/journeys/uj-2.spec.ts", "line": 118, "issue": "After the rename to 'Dispute {merchant}', absence checks on 'Dispute this transaction' pass vacuously: uj-2.spec.ts:118 and uj-1.spec.ts:103 (toHaveCount(0)), and spa-transactions.spec.ts:330-332 silently skips its assertion. The uj-1 and uj-2 lines are missing from the spec's QA list.", "fix": "QA's brief adds uj-1.spec.ts:69, 75, 103 and uj-2.spec.ts:118 to the spec's e2e lines, selecting `Dispute ${description}` with exact: true. Owner QA; no builder fix round."}, {"severity": "low", "file": "frontend/src/features/transactions/TransactionsScreen.tsx", "line": 56, "issue": "Skeleton day-band placeholders are div elements directly inside the ul, which the HTML content model forbids. axe 4.14 skips aria-hidden children, so its pass does not cover this. The spec does not require a div.", "fix": "Use <li aria-hidden=\"true\" className={bandClass}>; the list still exposes 5 listitems."}, {"severity": "low", "file": "frontend/src/features/transactions/TransactionRow.tsx", "line": 97, "issue": "On a phone, -my-3 makes the Dispute link's border box end at the li's bottom edge, so on the panel's last row the overflow-hidden panel clips the bottom side of its 2px-offset focus ring (from the CSS, not checked in a browser).", "fix": "Add focus-visible:-outline-offset-3 to the Dispute link."}], "assumptions": ["The tree at .claude/worktrees/agent-a1758a16625231b46 holds exactly the two bases; only the ten story_files differ from it.", "The sibling ui-raise-review's current tree is .claude/worktrees/agent-a0a85c91ff0f853f5 (newest build report, 17:03)."], "open_questions": [], "conflicts": []}
```

## Fix round 1 (2026-11-08, delta only)

- Previous tree: `.claude/worktrees/agent-a72913a67169034e9`. New tree: `.claude/worktrees/agent-a1c721639cdb71441`.
  Fix list: `docs/verification/ui-lists-fixes.md`. `e2e/` was not reviewed (QA's edits); a grep was run only to close M1.
- Delta, from `diff -r` of the two trees without `e2e/`, `node_modules` and build output: four source files
  (`DisputeStatusBadge.tsx`, `TransactionRow.tsx`, `TransactionsScreen.tsx`, `TransactionsScreen.test.tsx`), the
  build report and the results file. Nothing else changed.
- Results file: `TestResults/ui-lists-vitest.junit.xml`, 730 tests, 0 failures. The three new tests are in it.
  `scripts/diff-hash.sh <new tree> 2f897c3` recomputed here: `2611a228a19e`, the hash the report names.
- Result: **approve**. No high or medium finding. One new low finding (L3), the builder's open item.

### Earlier findings

- **M1 (QA): closed.** In the new tree's `e2e/tests`, no lookup of a link named `Dispute this transaction` is
  left. The matches that remain are the raise screen's heading (`dispute-raise.spec.ts`, `uj-1.spec.ts:76`), which is
  still named that.
- **L1: closed.** `TransactionsScreen.tsx:56-58`: the skeleton bands are `<li aria-hidden="true">`. Every child of
  the `ul` is now an `li`, and the list still exposes 5 items, because aria-hidden items are excluded.
  `first-of-type:max-md:border-t-0` still selects the first band, since it is the first `li` in the list.
- **L2: closed.** `TransactionRow.tsx:97`: the Dispute link gets `focus-visible:-outline-offset-3`, the remedy this
  record proposed and the one the rows already use.

### The medium fix: the status badge wraps (QA finding 1)

- **Plain and standard: yes.** There are two classes on the Badge (`whitespace-normal wrap-anywhere`) and
  `shrink-0` on the icon.
  - `cn` (tailwind-merge 3.7.0) replaces the primitive's `whitespace-nowrap` with `whitespace-normal`. This was
    checked by running `twMerge` on the real class strings.
  - `overflow-wrap: anywhere` is the right value over `break-word`. Only `anywhere` lowers the min-content width
    (<https://developer.mozilla.org/en-US/docs/Web/CSS/overflow-wrap>), and the badge sits in fit-content contexts:
    a grid item with `justify-self-end`, or an inline-level box. With `break-word` the badge would still be at least
    as wide as its longest word ("(prioritised)") and would still overflow.
  - Line breaking still prefers spaces. A word breaks inside itself only when it does not fit on a line alone.
  - `shrink-0` stops the flex icon from being squeezed below `size-4`.
  - The utility is already used in four places for headings and the trace ID.
- **Scope: right, and harmless on the detail screen.** `DisputeStatusBadge` has three callers: `TransactionRow`,
  `DisputeRow` (both named by the fix list) and `DisputeDetailScreen.tsx:126`.
  - Fixing it once in the shared component is the smallest change that covers both lists. Fixing each call site
    would mean writing it twice.
  - On the detail screen the badge is an inline-flex box in a plain block `div`. Its shrink-to-fit width stays at
    max-content whenever the line is at least that wide, so nothing changes at ordinary sizes. When the line is
    narrower, the badge now wraps instead of overflowing, which is the same improvement under SC 1.4.10.
  - The shared `Badge` primitive is unchanged, and `DisputeStatusBadge` is its only user.
  - Not a finding.
- **Tests and breaks: sound.**
  - All three tests are class assertions, which is what the fix list asked for, because jsdom does no layout. The
    builder's browser smoke and QA's new 360px/200% e2e case carry the layout proof.
  - Each break removes exactly the classes or element that its test asserts, so each test would fail. I read the
    breaks and did not re-run them: the verifier runs them.
  - FIX3's test checks both sides: every child is an `LI`, and exactly 5 children are exposed as list items. Its
    break (back to `div`) fails the tag check.
  - The FIX1 test runs on a transactions row only. `DisputeRow` renders the same component, so one test covers it.

### L3 low (new): the md column grid is wider than the panel when only the root font size grows

`TransactionRow.tsx:19-20` (`ledgerColumns`), `DisputeRow.tsx:42`, the panel's `overflow-hidden`
(`TransactionsScreen.tsx:20` and the disputes panel), and the tokens `--amount-col: 9rem` and `--action-col: 14rem`
(`theme/tokens.css:116-117`).

- **When it happens.** Tailwind's `md` breakpoint is `48rem` in a media query, so it follows the browser's initial
  font size, not the page's `html` font size. When only the root font size is raised, `md` still applies at 768px
  while the rem tracks grow.
  - At 200% at 768px, the fixed part of the row is 288 + 448 + 96 (gaps) + 96 (padding) = 928px. That is wider than
    the panel, so the badge column is clipped. The builder measured panel scroll of 26 to 290px at 768px/150-200%
    and 1280px/200%.
  - "Only the root font size raised" covers author or extension stylesheets, QA's method, and the builder's claim
    about Firefox text-only zoom, which I have not verified.
- **Whose it is: this story's.** The overflow-hidden panel and the rem-track md grid are both new here. Only the
  token values come from the ui-tokens-icons base. Before this story, the same width would have overflowed visibly.
  Now it is hidden.
- **Severity under WCAG 2.2 SC 1.4.4: low, not a failure.** The Understanding document lets content meet SC 1.4.4
  through the user agent's zoom (sufficient technique G142,
  <https://www.w3.org/WAI/WCAG22/Techniques/general/G142>; <https://www.w3.org/WAI/WCAG22/Understanding/resize-text>).
  - Full-page zoom to 200% makes the CSS viewport half as wide, so the phone layout applies. The fix-round smoke
    shows the phone layout clean at 320 and 360px at 200% in both modes.
  - The browser's font-size setting scales the media query too, and passes all 30 runs.
  - The remaining loss is on an extra path, so it is a robustness gap, not a criterion failure. Keyboard reach is
    kept: an `overflow: hidden` box is still a scroll container, so focusing a clipped badge link scrolls it into
    view. Pointer users lose sight of the column.
- **Fix, for the lows batch (choose one there; not checked in a browser):**
  - let the panel scroll instead of clip (`overflow-x-auto` in place of `overflow-hidden`). It still clips the band
    and hover fills to the rounded corners, and at normal sizes nothing overflows, so no scroll bar appears;
  - or cap the md tracks so they cannot outgrow the panel. This keeps the columns aligned only if every row uses the
    same cap.

### Pass 2 and standards (delta)

- There is no new abstraction, option or file, and every change fits in a sentence.
- The new mechanisms are `overflow-wrap: anywhere` (MDN, CSS Text 3), the `ul` content model (HTML Standard) and
  `outline-offset` for a ring inside a clipping box (SC 2.4.7). None is home-grown.
- The build report's two `standards` entries for the focus ring repeat each other. That is not a finding.

### Security checklist (delta)

This is a presentation-only change, with no endpoint, header, package or data change. No findings.

```json
{"status": "approve", "record": "docs/verification/ui-lists-review.md", "findings": [{"severity": "low", "file": "frontend/src/features/transactions/TransactionRow.tsx", "line": 20, "issue": "The md grid's rem tracks (--amount-col 9rem, --action-col 14rem, plus gaps and padding) are wider than the new overflow-hidden panel when only the root font size is raised (768px at 150-200%, 1280px at 200%), so the badge column is clipped; the same in DisputeRow.tsx:42. Not a WCAG 1.4.4 failure: page zoom (G142) and the browser font-size setting both get the phone layout and pass.", "fix": "Lows batch: let the panel scroll (overflow-x-auto in place of overflow-hidden), or cap the md tracks the same way on every row."}], "assumptions": ["The builder's mode A and mode B browser measurements are taken as reported; this review did not run a browser.", "The claim that Firefox text-only zoom behaves like a raised CSS root font size is not verified."], "open_questions": [], "conflicts": []}
```

## Capture (supervisor, 2026-11-09)

### Escapes

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
|---|---|---|---|---|
| Absence checks on the old link name in uj-1 and uj-2 would pass vacuously after the rename | reviewer | pre-build review (the spec's e2e list missed them) | spec gap | QA briefs name every lookup of a renamed accessible name, presence and absence |
| Status badges overflowed and were clipped at 200% text on phones | QA | builder smoke (widths, not text scaling) | input boundary | postmortem proposal: the builder's headless pass sets the root font to 200% at 320px; applied in later briefs |
| Breaks 6 and 9 stopped applying after the fix round edited their lines | verifier | fix-round builder | script error | a fix builder re-runs every break, not only the new ones |

### Metrics

| Fix rounds | Full-suite runs | Stages | Notes |
|---|---|---|---|
| 1 (QA), plus a record-only break regeneration | 2 | pre-build review; builder x3; reviewer x2; verifier x3; QA x3 (two interrupted by the stack lock or the session) | merged as `5bdc25a` (#30) |
