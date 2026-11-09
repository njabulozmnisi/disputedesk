# ui-raise-review: post-build review

Reviewer, 2026-11-08. Feature tier, below High-stakes, so this review also runs the security checklist.

- Worktree: `.claude/worktrees/agent-a1bb517c7630285ef` (branch `feature/ui-raise-review` at `2f897c3`, every change
  uncommitted, with the two bases ui-tokens-icons and ui-notices-states).
- Diff reviewed: the story's eight `story_files` (from `docs/verification/ui-raise-review-build.json`). The story's
  own change was isolated by comparing `frontend/src` with the latest ui-notices-states worktree
  (`agent-a1758a16625231b46`, r1). They differ in exactly these eight files and nothing else.
- Spec: `docs/backlog/story-ui-raise-review.md` on `origin/main`; pre-build review
  `docs/verification/ui-raise-review-prebuild-review.md`; `docs/ux/` (HANDOFF 3.4 to 3.6, DESIGN, EXPERIENCE, mockups
  `dispute-new*.html`, `dispute-review.html`, `app.css`).
- Checks run (read-only): `npx vitest run src/features/disputes/raise src/lib/utils.test.ts` gave 7 files and 157
  tests, all passing. The builder's results file `TestResults/ui-raise-review-frontend.junit.xml` shows 738 tests,
  0 failures and 0 errors. `scripts/secret-scan.sh` in the worktree found no leaks. The story files contain no
  `style=`, raw hex, `px`, `dangerouslySetInnerHTML` or glyph. A Tailwind 4.3.3 compile of `src/index.css` through
  `@tailwindcss/node` showed how `text-heading` is built (point 1 below). A jsdom plus `dom-accessibility-api` check
  of both sr-only span forms is under point 2.

## Verdict

`changes`: one medium finding (the review's masked number lost its screen-reader label) and three lows. Every
criterion is proved by a test that fails without the change. The change is presentation only.

## Findings

| # | Severity | File:line | Finding | Fix |
|---|---|---|---|---|
| 1 | medium | `frontend/src/features/disputes/raise/ReviewScreen.tsx:70` | `NumberRow` puts the visible form `492345•• •••• 3456` (or `•••• 4821`) in the `dd` with no screen-reader form. Before this story, review rendered `MaskedNumber`, which hides the printed form (`aria-hidden`) and gives screen readers "card ending 3456" or "account ending 4821". EXPERIENCE.md, Accessibility (line 215), says "masked numbers are labelled 'card ending 3456'". The change regresses a written rule: a screen reader now reads six digits and a run of bullets. Criterion 5 names the `dd`'s visible text only, so a fix still meets it. No test covers the spoken form. | In `NumberRow`, render the visible text in `<span aria-hidden="true">` and add `<span className="sr-only">card ending {lastFour}</span>` (account: `account ending {accountLast4}`), as `MaskedNumber` does. Assert in `ReviewScreen.test.tsx` that the `dd` holds the sr-only "card ending 3456" or "account ending 4821" and that the printed form is `aria-hidden`. The existing visible-text assertions stay. |
| 2 | low | `frontend/src/features/disputes/raise/ReviewScreen.tsx:203`, `:221` | Review differs from `mockups/dispute-review.html` in two ways that Boundaries does not list (Boundaries lists the differences "so builder and QA share one target"). First, the head row uses `items-start` where the mockup's `.readback-head` uses `align-items: flex-end`. Criterion 6 and HANDOFF 3.6 say only "top right beside the hero amount", which both alignments meet. Second, the Transaction `dd` is not bold (the mockup has `<dd><strong>`). QA's mockup pass may flag either. | `items-end` matches the mockup and still meets "top right", or list both as accepted differences. Lows batch. |
| 3 | low | `frontend/src/theme/tokens.css:79`, `:215`; `frontend/src/pages/StartPage.tsx:119`; `frontend/src/components/ui/alert-dialog.tsx:74` | Existing problem (on main), found while checking point 1. The theme has a font size and a colour both named `heading`. Tailwind 4.3.3 builds `.text-heading` as `color: var(--heading)` only (compile checked), so the 24px h2 size can't be used. The start page's "How it works" `h2` and every `AlertDialogTitle` ask for `text-heading` as a size and render at the inherited size. Not caused by this story. | Rename one of the pair (for example the size to `--text-h2`, used on those two elements) in the lows batch. Then put the name back in `lib/utils.ts`'s font-size list if it is the size. |
| 4 | low | `frontend/src/features/disputes/raise/ReviewScreen.tsx:70` | `NumberRow` repeats `MaskedNumber`'s masking (`slice(0, 6)`, `slice(-4)`, the bullet layout). This is two uses, under the repo's "abstract on the third real case" rule, so it is accepted. It is listed only so the fix for finding 1 keeps both spoken forms identical. | None now. Extract one helper when a third use appears. |

## The builder's flagged deviations

1. **`lib/utils.ts`, `heading` removed from tailwind-merge's font-size list.**
   - *Correct.* `tokens.css` declares both `--color-heading` (line 190) and `--text-heading` (line 215). Compiling
     the real `src/index.css` with Tailwind 4.3.3 emits exactly one rule, `.text-heading { color: var(--heading); }`;
     `.text-label` and `.text-subheading` emit font sizes. tailwind-merge had been told `text-heading` is a size, so
     `FieldDescription`'s `cn('text-label text-muted-foreground', 'text-label font-strong text-heading')` dropped
     both `text-label` classes and kept the muted colour, which defeats criterion 3. With the entry removed,
     tailwind-merge's default treats `text-heading` as a colour, which matches what Tailwind builds. The new unit
     test proves it. The criterion 3 test fails without the change.
   - *Every `text-heading` use in `frontend/src`:* 18 sites. 16 are static `className` strings, which no `cn` call
     merges, so their output is unchanged. `alert-dialog.tsx:74` calls `cn('text-heading font-strong', className)`,
     and neither caller (`InactivityWarning.tsx:64`, `WithdrawDialog.tsx:164`) passes a `className`, so its output is
     unchanged too. The last site is this story's `FieldDescription`.
   - *Plainest fix:* yes. It is one list entry, in the shared helper that holds the root cause (the user standard:
     fix the root cause once in shared code). The alternatives are worse: renaming a token touches `tokens.css` and
     a dozen components outside every story's scope, and working around it inside `RaiseScreen` would contradict
     the class string criterion 3 names. The new comment explains the omission in one sentence.
   - *Keep it in this story:* yes. Criterion 3 cannot hold without it, the edit is declared, and it collides with no
     sibling story (`ui-lists` leaves `lib/utils.ts` and its test unchanged). It falls outside the Touches list, but
     it was needed and declared, so it is not ranked. The underlying name clash is finding 3. Capture note: the
     pre-build review missed that the class string criterion 3 names collides in tailwind-merge (spec gap).
2. **The sr-only span with the space outside it (`Change <span className="sr-only">your dispute</span>`).**
   *Correct and preferable.* In jsdom with `dom-accessibility-api`, the spec's literal form
   `Change<span class="sr-only"> your dispute</span>` is named `"Changeyour dispute"` and the builder's form
   `"Change your dispute"`. Browsers name both "Change your dispute". The trailing space of the text node collapses
   at the end of the inline-flex line, so the visible "Change" is unchanged. The accessible name comes from the
   content, starts with the visible label (WCAG 2.2 SC 2.5.3), and there is no `aria-label`. The test asserts all
   three. Not a finding. The spec and mockup markup is the form a unit test cannot name.
3. **`NumberRow` duplicating `MaskedNumber`.** Accepted on simplicity grounds (two uses, finding 4). It also dropped
   `MaskedNumber`'s screen-reader form, which is finding 1.
4. **`items-start` vs the mockup's `flex-end`.** Meets criterion 6's "top right". It is a mockup difference that
   Boundaries doesn't list, so it is finding 2 (low).

## Presentation only

Confirmed:

- `ReviewScreen.tsx` changes only JSX, `Row`, and the new `NumberRow`. The `useRaiseDispute` mutation, `mutationFn`,
  the `Idempotency-Key: draft.key` header, `retry`/`retryDelay`, `onSuccess`/`onError`/`onSettled`, `send()`,
  `refused` and the heading focus effect are all byte-identical.
- `StepProgress` is gated on the same `refused` (`endsTheDraft`) as the card. So "exactly while the review card shows"
  holds by construction, and tests cover each side (404, 409, 422 x2; network, 503, 504).
- `RaiseScreen.tsx` changes only JSX: the step, the form classes, `TransactionSummary` moved inside the form, the
  difference classes, and the action buttons. The form handlers, the schema and the draft are untouched.
- `raiseTelemetry.test.tsx` changes only the one "Change" locator (line 256). `raiseSpans.ts` is untouched.
- Review's Cancel keeps `disabled={pending}` through `Button asChild`, and the test that asserts `aria-disabled`
  still passes.

## Pass 1: correctness

| Criterion | Proved by | Verdict |
|---|---|---|
| 1 Step exactly while it can go on | `ReviewScreen.test.tsx` refusals x4 and retryables x3, step above the h1; `RaiseScreen.test.tsx` step above the h1, summary after the h1 | Proved; both sides of the bound tested; break 1 targets it |
| 2 One surface, quiet block | `RaiseScreen.test.tsx` form surface holds block, radios, Cancel; quiet block classes | Proved; break 2 |
| 3 Difference 14/700 heading | `RaiseScreen.test.tsx` classes, no muted colour, described-by; `utils.test.ts` | Proved; fails without the utils change |
| 4 Hairline, 44px pills, full width | `RaiseScreen.test.tsx` and `ReviewScreen.test.tsx` action tests; pending `aria-disabled` test | Proved; break 4 |
| 5 One surface, 9rem column, Card/Account row | region by name, row classes and order, account variant | Proved; break 5. Finding 1 is about the spoken form, which the criterion doesn't name |
| 6 Change named from content, 44px | name, sr-only span, no `aria-label`, `min-h-(--tap-target)`; telemetry 4a/4b/4a | Proved; break 6 |
| 7 Static neutral notice | no role, no `aria-live` on or around it; exactly one alert after a 503 | Proved; break 7 |

The backend items in the checklist (Location, ProblemDetails, ownership, events, `TimeProvider`, `!`) do not apply.
The story has no backend change and adds no `!` in production code. Every role that needs a name is asserted by name
(`region` "Your dispute"; the error summary `group` by name).

## Pass 2: simplicity

The design is plain: Tailwind utilities on existing elements, existing `StepProgress`, `Alert` and `Button asChild`
components, one tiny `NumberRow`, and no new abstraction or option. `Row` lost `min-w-0` (not needed under
`minmax(0,1fr)`) and `first:border-t-0` (the mockup's first row has a hairline). Each method reads on first pass.

## Standards check

| Mechanism | Standard | Verdict |
|---|---|---|
| Step label over a decorative bar | WCAG 2.2 SC 1.1.1 | Follows |
| Theme tokens via Tailwind v4 utilities, `9rem` grid column, `wrap-anywhere` | Tailwind v4 theme, grid-template-columns, overflow-wrap docs | Follows |
| Field description via `aria-describedby` | shadcn Field; WCAG SC 1.3.1 | Follows |
| `Button asChild` around the router `Link` | shadcn Button | Follows |
| Named `section` as a region | WAI-ARIA 1.2 region | Follows |
| Name from content, visible label first, 44px target | WCAG SC 2.5.3, 2.5.5; WAI-ARIA name from content | Follows |
| Static Alert, `role="alert"` for errors only | WAI-ARIA 1.2 alert; APG Alert | Follows |
| tailwind-merge `extendTailwindMerge` class groups | tailwind-merge configuration docs | Follows (point 1) |
| Masked number for screen readers | EXPERIENCE.md Accessibility; existing `MaskedNumber` pattern | Not followed: finding 1 |

## Security checklist (below High-stakes)

- AuthN/Z, browser session, linking, audit: none touched (no endpoint, cookie, session or link change).
- Data: no personal data added to the DOM beyond what review already showed. The masked card and account stay
  masked (first six and last four only, as before). No logging or telemetry change.
- Supply chain: no package added or changed by this story. The secret scan of the whole uncommitted change is clean.
- Headers and CSP: no `style` attribute, no inline script, no `dangerouslySetInnerHTML`. The CSP test in
  `RaiseScreen.test.tsx` still passes.
- No security finding.

## Notes for QA

The builder's `e2e_lines_for_qa` are accurate: the `dispute-raise.spec.ts` "Change" lookups at 311, 386, 450, 456 and
735 become `{ name: 'Change your dispute', exact: true }`. The six Radix reason radios are 24px (they predate this
story and the reason cards are out of scope), and QA's 44px sweep will report them.

## Fix round 1 (delta review, 2026-11-08)

- Previous tree: `.claude/worktrees/agent-a1bb517c7630285ef`. New tree: `.claude/worktrees/agent-a0a85c91ff0f853f5`.
  Fix list: `docs/verification/ui-raise-review-fixes.md` (review finding 1 only).
- Scope of the delta: a recursive compare of the two trees (without `node_modules`, `bin`, `obj`, `dist`,
  `TestResults` and `.git`) differs in exactly three files: `frontend/src/features/disputes/raise/ReviewScreen.tsx`,
  `frontend/src/features/disputes/raise/ReviewScreen.test.tsx` and `docs/verification/ui-raise-review-build.json`.
  Nothing else changed.
- Checks run (read-only): `npx vitest run src/features/disputes/raise` in the new tree gave 6 files and 156 tests, all
  passing. The builder's results file `frontend/TestResults/ui-raise-review-frontend.junit.xml` shows 740 tests, 0
  failures and 0 errors (738 before, plus the two new tests). I checked the breaks in a throwaway copy in the scratchpad
  (since deleted), using the new tree's `frontend` with `node_modules` linked. Results:
  - The builder's new break (card `NumberRow` reverted to plain text) fails "reads the masked number to a screen reader
    as its short form, hiding the printed one".
  - My own revert of the account branch fails "reads an account number as "account ending", hiding the printed one".
  - Dropping only `aria-hidden` from the card's printed span fails the card test.
  - Each time, the other 43 tests in the file pass.

### Earlier findings

| # | Severity | Status | Note |
|---|---|---|---|
| 1 | medium | Closed | `NumberRow` (`ReviewScreen.tsx:73` to `:90`) now copies `MaskedNumber` (`features/transactions/TransactionRow.tsx:14` to `:35`): the printed form sits in `<span aria-hidden="true">` and an sr-only span reads `card ending {lastFour}` or `account ending {accountLast4}`, word for word the same as the list. The visible text in the `dd` is unchanged (`492345•• •••• 3456`, `•••• 4821`), so criterion 5 and the "reads the draft back" assertions still hold. The `getAllByText(/Card/)` length-1 check (test line 72) still passes because the sr-only text starts with a lowercase "card". Both variants have a test that fails without the fix: the card through the builder's break, the account through my revert. |
| 2 | low | Open | Unchanged. Goes to `lows.md` at merge. |
| 3 | low | Open | Unchanged. Goes to `lows.md` at merge. |
| 4 | low | Open | Unchanged. The two copies' spoken forms are now identical, as the finding asked. Goes to `lows.md` at merge. |

### The changed account-row assertion

`getByText('•••• 4821').tagName).toBe('DD')` became `getByText('•••• 4821').closest('dd')).not.toBeNull()`. The old
check needed the printed text to be the `dd`'s own text node. After the fix that text sits in a span inside the `dd`,
so the old form could not hold. The new check proves the same thing: the printed account number is inside a `dd`
inside the "Your dispute" region. Neither form ties the value to the Account row in particular. The `dt` order
assertion just above it (`Transaction, Date, Account, Reason`) and the absence of "Card" are unchanged, and the new
account test pins the span itself. So the assertion is no weaker in what it proves. Not a finding.

### Delta checks

- Pass 1: the fix adds two spans per branch and no logic. It adds no `!` and does not touch the mutation, the focus
  effect or `refused`. The axe check in "reads the draft back" still passes.
- Pass 2: this is the plainest form, the same markup as `MaskedNumber`. The doc comment names its source. No new
  abstraction (finding 4 still applies: two uses).
- Standards: the masked number for screen readers now follows EXPERIENCE.md, Accessibility, and the existing
  `MaskedNumber` pattern (`aria-hidden` printed form plus visually hidden text, WAI-ARIA 1.2 `aria-hidden`). The
  standards-check row "Masked number for screen readers" now reads "Follows".
- Security checklist: no new data in the DOM. The last four was already shown, and the sr-only text repeats it. No
  logging, header or dependency change.

### Verdict

`approve`. Finding 1 is closed, the delta adds no new finding, and lows 2 to 4 carry to `lows.md` at merge.

## Fix round 2 (delta review, 2026-11-08)

- Previous tree: `.claude/worktrees/agent-a0a85c91ff0f853f5`. New tree: `.claude/worktrees/agent-ae154877f3a7a40c6`.
  Fix list: `docs/verification/ui-raise-review-fixes.md`, round 2 (QA's medium: the 9rem label column collapsed the
  value column under text-only scaling).
- Scope of the delta: a recursive compare of the two trees (without `node_modules`, `bin`, `obj`, `dist`,
  `TestResults`, `test-results` and `.git`) differs in exactly these files: `ReviewScreen.tsx` (the `Row` grid class
  and `wrap-anywhere` on the `dt`), `ReviewScreen.test.tsx` (one test renamed, its class assertion updated, a `dt`
  assertion added), `theme/tokens.css` (one line, `--label-col: 9rem`), `theme/theme.test.ts` (one assertion),
  `docs/verification/ui-raise-review-build.json` and the gitignored results file. QA's e2e edits are the same in both
  trees and were skipped. Nothing else changed.
- Checks run (read-only):
  - `npx vitest run src/features/disputes/raise/ReviewScreen.test.tsx src/theme/theme.test.ts` in the new tree: 2 files,
    56 tests, all passing. The builder's results file shows 740 tests, 0 failures, 0 errors.
  - Breaks 4 and 8, applied in a throwaway copy of `frontend` (since deleted): each fails exactly the renamed test
    ("reads the dl in a label column of 9rem, capped at 40% ...") with the other 43 passing. My own two reverts in the
    same copy: deleting the `--label-col` line fails "declares the list columns"; dropping `wrap-anywhere` from the
    `dt` fails the renamed test.
  - `frontend/dist` (the builder's `vite build`) holds the compiled rule
    `grid-template-columns: minmax(0,min(var(--label-col), 40%)) minmax(0,1fr)`, so Tailwind 4 emits the arbitrary
    class.
  - Layout in headless Chromium (Playwright 1.64.1 from the main checkout's `e2e`): a static page with the built CSS and
    the review card's markup (the `Page` form column, the section's `px-4 py-6 md:p-6`, four rows), root font size set
    to each percentage. jsdom has no layout, so this is the only check here of the widths themselves. Results:

| Viewport | Text | Label column | Smallest value column | Page scroll width |
|---|---|---|---|---|
| 320px | 100% | 102px (40%) | 136px | 320 |
| 320px | 150% | 89px (40%) | 109px | 320 |
| 320px | 200% | 92px (40%) | 105px | 327 (the hero amount, residual 2 below) |
| 360px | 100% | 118px (40%) | 160px | 360 |
| 360px | 150% | 105px (40%) | 133px | 360 |
| 360px | 200% | 92px (40%) | 106px | 360 |
| 1280px | 100% | 144px (9rem) | 302px | 1280 |

### Earlier findings

| # | Severity | Status | Note |
|---|---|---|---|
| Fix list 2 (QA) | medium | Closed | The value column now keeps at least 60% of the row less the gap at every width and text size measured (105px at its smallest, where QA measured 0px), and the dl adds no horizontal scroll. 9rem holds at 1280px. A test and two breaks pin the class string and the token. QA's own text-scaling check in `spa-raise-review-axe.spec.ts`, which the fix list asks for, is not in the tree yet. That is QA's step, not the builder's. |
| 1 | medium | Closed (round 1) | Unchanged by this round. |
| 2 to 4 | low | Open | Unchanged. They go to `lows.md` at merge. |

### The questions in the brief

- **Standard and plain.** Yes. `minmax(0, X)` is the CSS Grid track function: the track may shrink to 0, so it never
  forces the row wider than its container, and may grow to X (CSS Grid 1, minmax():
  <https://www.w3.org/TR/css-grid-1/#valdef-grid-template-columns-minmax>). `min(var(--label-col), 40%)` is the CSS
  Values 4 comparison function. In a track size, the percentage resolves against the grid container's inline size
  (CSS Values 4, comparison functions: <https://www.w3.org/TR/css-values-4/#comp-func>; CSS Grid 1, track sizing:
  <https://www.w3.org/TR/css-grid-1/#track-sizes>). The `minmax(0, …)` wrapper earns its place: the builder found
  that a bare `min()` track counted as 9rem when the card's min-content width was worked out, and widened the card.
  CSS Grid 1, 7.2.1, treats a percentage as `auto` for intrinsic contributions. `overflow-wrap: anywhere` on the
  `dt` (CSS Text 3: <https://www.w3.org/TR/css-text-3/#overflow-wrap-property>) lets a long label such as
  "Transaction" break inside the narrower column at 200%. In one sentence: the label column is 9rem but never more
  than 40% of the row, and labels and values wrap instead of overflowing. Nothing here is home-grown.
- **The arbitrary class is allowed by "theme tokens only".** Yes. The rule (`docs/conventions/frontend.md:10`;
  Boundaries "no raw hex or px value in a component") forbids raw colours and pixel values. The class holds a theme
  variable, a percentage, `0` and `1fr`, and no px or hex value. Criterion 5 itself named an arbitrary `grid-cols-[…]`
  class. Tailwind v4 documents arbitrary values, with underscores for spaces
  (<https://tailwindcss.com/docs/adding-custom-styles#using-arbitrary-values>).
- **The new token earns its place.** Yes, narrowly. It has one use. But Boundaries require the 9rem to be "its own
  value, not `--amount-col`", the fix list asked for "theme-token terms", and the token sits beside the two other
  layout-column tokens (`--amount-col`, `--action-col`). It is a plain `:root` variable, so it is always emitted
  (computed `9rem` in the browser). `theme.test.ts` pins it. The cost is the DESIGN.md drift in low 6.
- **The tests and breaks prove it.** For what a unit test can prove, yes: the class string, the `dt` wrap and the
  token, each with a test that fails without it (breaks 4 and 8, and my two reverts). The widths are proved only by
  the builder's browser pass and the table above. QA's e2e text-scaling check is the lasting proof.
- **Nothing else changed.** Confirmed (scope above).

### New findings

| # | Severity | File:line | Finding | Fix |
|---|---|---|---|---|
| 5 | low | `frontend/src/features/disputes/raise/ReviewScreen.tsx:62` | The 40% cap binds at the default text size on every phone width, not only under text scaling. 9rem needs a row of at least 360px, so a viewport of about 426px. At 360px and 100% the label column is 118px, not the mockup's 144px (`screenshots/dispute-review-360.jpg`). The fix list said "keep 9rem where it fits", and 9rem fits at 360px and 100% (the value column had 134px before). The builder's assumption "9rem holds where it fits" was checked at 1280px only. This is no accessibility or correctness defect: the labels fit and the value column gets wider. But QA's AC5 reading of "144px 1fr at 360" will now read about 118px. | Plainest: accept it, and write "9rem, capped at 40% of the row" into criterion 5 (low 6). Alternative: a 50% cap keeps 144px at 360px and 100% (measured), at the price of the value column at 320px and 200% (82px instead of 105px). This is a design choice for the supervisor and Sofia, not a fix round. |
| 6 | low | `docs/backlog/story-ui-raise-review.md` criterion 5; `docs/ux/DESIGN.md:147`; `docs/verification/ui-raise-review-build.json` | Records drift from the code. (a) Criterion 5 still names `grid-cols-[9rem_minmax(0,1fr)]` "at every width". The fix list overrode it for WCAG 2.2 SC 1.4.4 and 1.4.10, but the story does not yet say so. (b) DESIGN.md's spacing tokens (the stated single source of the theme) list `amount-col` and `action-col` but not `label-col`. (c) `story_files` omits `frontend/src/theme/tokens.css` and `frontend/src/theme/theme.test.ts`, which this round changed. The AC5 mapping still names the old test title first. The `standards` list has no entry for `minmax()` or `min()`. If the `tokens.css` hunk were left out of the commit together with its test, nothing would fail: `var(--label-col)` would be invalid at computed-value time and the rows would fall back to one column. | (a) and (b): a docs-only amendment by Sofia (criterion 5: "a label column of 9rem capped at 40% of the row"; DESIGN.md: `label-col: 9rem`). (c): the supervisor commits both theme files with the story. The JSON is a pipeline record and needs no fix round. |

### The builder's two residual overflows at 200% text

Neither is this story's, and neither comes from the review `dl`.

- **R1, the signed-in header at 320px and 360px** (`frontend/src/app/Header.tsx`, a base file from ui-tokens-icons,
  outside this story's Touches list). Not measured here. The builder measured a 452px scroll width. From the code,
  main's header already had the wordmark with a 24px dot and `gap-2`, and the base made the wordmark narrower, so the
  overflow was most likely there on main already (inferred, not run on main). It affects every signed-in screen, not
  review. **Low, already there before this story.** It goes to `lows.md` as its own row against `Header.tsx`, for the
  lows batch or a Small fix (let the header row wrap, or let its controls shrink).
- **R2, the 2rem hero amount at 320px** (`ReviewScreen.tsx`, the "You're disputing" amount). Reproduced: with the
  built CSS at 320px and 200%, the hero amount alone sets the card's min-content width and the page scrolls by 7px
  for "R1 234.56". The builder saw 35px with a longer amount. On main the same `text-amount-hero` amount sat in a
  `p-6` box, which is wider padding than this story's `px-4` below 768px. So the story made it slightly better, and
  the overflow was already there. **Low, already there before this story.** It goes to `lows.md` as its own row,
  for the lows batch (for example `min-w-0` on the card and a hero size that steps down below `md`, or letting the
  amount wrap).
- **Why low.** Both appear only when two conditions are combined: a 320px or 360px viewport and 200% text-only
  scaling. That is past the story's Verify bar ("no horizontal scroll at 320px or 200% zoom", each on its own). WCAG
  2.2 SC 1.4.10 is defined at 320 CSS px without text scaling. SC 1.4.4 allows scrolling as long as no content is
  lost, and nothing is lost here (the overflow is 7 to 35px and all content can be scrolled to). The round 2 finding
  differed: the value column there went to 0px and content was lost.

### Delta checks

- Pass 1: no logic change. The mutation, the focus effect, `refused` and `NumberRow` are untouched. No `!`.
- Pass 2: one class and one token. This is the smallest form that meets the fix list in theme-token terms. Nothing is
  speculative.
- Standards: the standards table above now adds CSS Grid 1 `minmax()` and CSS Values 4 `min()` for the label column.
  Both are followed.
- Security checklist: presentation only. No data, header, CSP, logging or dependency change. No `style` attribute.

### Verdict

`approve`. The round 2 medium is closed. The delta adds two lows (5 and 6), and residuals R1 and R2 are lows that
were already there before this story. All four go to `lows.md` at merge, with lows 2 to 4 still open.

## Capture (supervisor, 2026-11-09)

### Escapes

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
|---|---|---|---|---|
| The review's masked number lost its screen-reader form ("card ending 3456") | reviewer | builder self-check | missing test | a replaced component's spoken form is asserted before it is swapped out |
| The 9rem label column collapsed the value column under text-only scaling (WCAG 2.2 SC 1.4.4, 1.4.10) | QA | pre-build review (criterion 5 said "9rem at every width") | spec gap | a rem-sized column in a criterion states its cap; text scaling at 320 and 360px is part of the builder's browser smoke (`ui-raise-review-postmortem.md`) |
| The 40% cap binds at the default text size below about 426px | reviewer (delta) | builder (checked at 1280px only) | missing test | accepted; criterion 5 and DESIGN `label-col` are amended in the lows batch |
| `RaiseScreen.test.tsx` conflicted with main after ui-lists and ui-linking merged | supervisor at update-branch | n/a | environment or tooling | the guard denies a builder `git merge`, so the supervisor starts it and a builder resolves it |

### Metrics

| Fix rounds | Full-suite runs | Stages | Notes |
|---|---|---|---|
| 2 (review r1, QA r2) | 3 (builder, fix round 1, fix round 2) | pre-build review; frontend builder x3 plus one conflict resolution; reviewer x3; verifier x2; QA x2 | merged as `415ec80` (#28); postmortem `ui-raise-review-postmortem.md` |
