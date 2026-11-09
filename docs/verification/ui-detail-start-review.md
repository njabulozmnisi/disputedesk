# ui-detail-start: post-build review

- Date: 2026-11-08
- Story: `docs/backlog/story-ui-detail-start.md` (origin/main), with `docs/verification/ui-detail-start-prebuild-review.md`,
  `docs/verification/decisions.md` (2026-11-08: Disputed on from `createdAt`; every timeline entry keeps its status label)
  and `docs/ux/` (HANDOFF 3.1, 3.10-3.13; DESIGN Components; EXPERIENCE Withdraw, Timeline, Accessibility Floor; mockups
  `dispute-withdraw.html`, `app.css`).
- Diff: uncommitted in `.claude/worktrees/agent-af28b3a0f99cd8e64`, stacked on `feature/ui-detail-start` (2f897c3) with
  the uncommitted ui-tokens-icons and ui-notices-states bases. This story's change is separated by comparing its
  `story_files` (`ui-detail-start-build.json`) with the bases-only tree `.claude/worktrees/agent-a1758a16625231b46`.
- Checked by: reading every story file's diff against the bases-only tree. I re-ran the five touched test files
  (`detail/`, `routing.test.tsx`, `ReviewScreen.test.tsx`, `wordmark.test.ts`, `InactivityWarning`): 5 files, 236 tests
  passed. I listed the PNG chunks (both files: IHDR, IDAT x4, IEND; 11,526 and 10,529 bytes; `file`: 620 x 128, 8-bit
  RGBA). I viewed both PNGs, read the Tailwind 4.3.3 `text` utility in `node_modules/tailwindcss/dist/lib.js`, and
  compared the shared files with ui-raise-review's tree (`agent-a0a85c91ff0f853f5`) and with origin/main.
- Result: **approve**. No high or medium finding. Five lows.

## Findings

| # | Severity | File:line | Finding | Fix / route |
|---|---|---|---|---|
| L1 | low | `frontend/src/components/ui/alert-dialog.tsx:63` | The sheet switches to the centred dialog at `md` (768px), but the footer still goes to a row at `sm` (640px). Between 640 and 767px the bottom sheet shows the two buttons side by side. The mockup keeps them stacked until 768px (`docs/ux/mockups/app.css:312`, `.actions` turns into a row inside `@media (min-width: 768px)`). This story edited that line to add the hairline. | `sm:flex-row` becomes `md:flex-row`, so the footer switches at the same breakpoint as the sheet. Lows batch. QA's 767px check will show the row layout. |
| L2 | low | `frontend/src/components/ui/alert-dialog.tsx:40`, `:63`; reaches `frontend/src/app/InactivityWarning.tsx:62` | The shared primitive change also turns the session-timeout warning into a bottom sheet below 768px, with a hairline above its buttons. No criterion names that dialog. The pre-build review said `alert-dialog.tsx` had one consumer, which was wrong (that is my escape, not the builder's). **Acceptable, no code change:** criterion 7 names `alert-dialog.tsx` as the place for the change. DESIGN styles dialogs as one component (`rounded.lg`, overlay shadow). The height cap also fixes SC 1.4.10 for the warning. Behaviour is unchanged: no focus, Esc or controlled-open change, and the InactivityWarning tests pass. | QA: one screenshot of the session warning at 360px, light and dark, to record the new look. |
| L3 | low | `frontend/src/components/ErrorSummary.tsx:48` (ui-notices-states; existed before this story) | The builder measured the error summary's field links at 22px tall at 767px and wider. EXPERIENCE line 190 sets "44px minimum targets". They likely meet WCAG 2.2 SC 2.5.8 through its spacing exception (24px), but not the project's 44px floor. They show inside the withdraw dialog, so this story's QA ("every interactive element is at least 44px tall") will meet them. This story does not own the component. | Route to ui-notices-states' owner (still unmerged, so it can go in its fix round) or to the lows batch. This story's QA records it as known and does not fail the story for it. |
| L4 | low | `frontend/src/theme/tokens.css:190`, `:215`; `frontend/src/components/ui/alert-dialog.tsx:76`; `frontend/src/pages/StartPage.tsx` ("How it works" h2). Already on main. | Confirmed: Tailwind 4.3.3's `text` utility resolves `--text-color` and `--color` before `--text`, so `.text-heading` is `color: var(--heading)` only. The withdraw dialog title and the start page h2 render at 16px, not DESIGN's 24px. The Timeline and guidance h2s in this story use `text-subheading` (20px) for that reason, and the theme test bans `text-[`. This is the same finding as `ui-raise-review-review.md` finding 3 and `3.12-qa.md` O6. | One lows row only (a duplicate of ui-raise-review's): rename the size token (for example `--text-h2`). Note for that fix: this story uses `text-heading` deliberately as the navy colour (`Timeline.tsx:104`, `DisputeDetailScreen.tsx:94`), so keep the colour name and rename the size. |
| L5 | low | `frontend/src/theme/wordmark.test.ts:131-133`, `:146-148` | "Is transparent around the drawing" checks the alpha of one pixel, (0, 0). "Dot on the baseline" is asserted only as "at or above the name's foot", and the name's box includes the descender of "p", so a dot sitting high would still pass. The images I viewed are right (dot after the name, on the baseline, transparent), and criterion 9's proof is the report plus QA's screenshots, so this is only test strength. | Optional, lows batch: assert alpha 0 on the four corners or the edge rows, and compare the dot's foot with the foot of a letter without a descender (for example "D"). |

## Points the brief asked about

- **`surfaceClass.ts` earns its place.** It is one string with three uses in two files: the Timeline, the loading
  timeline and the Rejected guidance. It keeps the three flat surfaces identical. There is no `react-refresh` lint rule,
  so exporting it from `Timeline.tsx` was possible, but a `.tsx` file exporting a class string is less plain than a
  one-line module. Not a finding. For the refactor sweep: ui-raise-review's `ReviewScreen.tsx:214` uses the same string,
  and on a later real case a shared flat-surface constant under `components/` could serve both.
- **`alert-dialog.tsx` reaching InactivityWarning.** Acceptable (L2).
- **The note's `wrap-anywhere` (`Timeline.tsx:115`) is correct and better than the old `wrap-break-word`.** The note is
  a grid item, and grid items take a `min-width: auto` of min-content. `overflow-wrap: anywhere` counts the break
  chances in min-content and `break-word` does not, so a long unbroken note now shrinks instead of pushing the timeline
  wider than 320px (CSS Text 3, `overflow-wrap`). The note still renders as plain text, through a JSX template string.
- **"Back to disputes" at 44px (`DisputeDetailScreen.tsx:295`).** In scope. EXPERIENCE line 190 and this story's Verify
  require 44px on every interactive element. It is a one-class change in a file the story owns, and
  `DisputeDetailScreen.test.tsx` asserts `min-h-(--tap-target)`.
- **Regenerated wordmark PNGs and `wordmark.test.ts`.** Criterion 9 holds:
  - The PNGs are 620 x 128, 8-bit RGBA, about 11 KiB each.
  - They have no `tEXt`, `zTXt`, `iTXt` or `eXIf` chunk.
  - The dot comes after the name, in the right colours for both themes.
  - Nothing under `e2e/` changed, no script was committed, and no package was added.

  The test reads the PNG with `node:zlib` and the W3C PNG filter rules. I checked the Paeth and Average predictors
  against PNG-3 section 9. That is about 40 lines of decoding, which the dot-position assertion needs. It also gives
  story 2.6's size and metadata rules a permanent check, which the pre-build review (P5) found no CI job had. It adds
  no dependency. Not a finding beyond L5.
- **`text-heading` is colour-only.** Pre-existing on main. Ranked low and routed as one lows row (L4).
- **The ErrorSummary link at 22px.** ui-notices-states' component. Ranked low and routed (L3).

## Lines shared with sibling trees

- **ui-raise-review** (`agent-a0a85c91ff0f853f5`) changes `ReviewScreen.test.tsx` at base lines 62-80, 125-141 and
  206-224, and appends from 637. This story changes only base line 272 (the banner selector, `strong` to
  `[data-slot="alert-title"]`). ui-raise-review leaves that line as `selector: 'strong'` (its line 275), and its
  appended block makes no banner assertion. **No shared line.** The hunks are about 45 lines apart, so either merge
  order applies cleanly. If ui-raise-review merges first, this story's rebase keeps the new selector. ui-raise-review
  does not touch the other story files: `DisputeDetailScreen.tsx` and its test, `Timeline.tsx`, `alert-dialog.tsx`,
  `StartPage.tsx` and `routing.test.tsx` match the base.
- **ui-tokens-icons** (merged, #24): origin/main's `StartPage.tsx` is identical to the base, and main's
  `routing.test.tsx` differs from it only by ui-notices-states' hunks. This story's hunks (the caption at
  `StartPage.tsx:109`, the new test at `routing.test.tsx:75`) sit apart from them, so the rebase onto main is clean.

## Pass 1: correctness

| Criterion | Proof | Assessment |
|---|---|---|
| AC1 Disputed on | `DisputeDetailScreen.test.tsx` "shows Disputed on from createdAt…": `createdAt` 2026-10-25T22:30Z reads "26 Oct 2026" in a `<time datetime>`, and the summary `dl` has no "27 Oct 2026" while the timeline has it. "runs the summary rows…" asserts the full `dt` order on a wrong-amount dispute. | Proved. Breaks 1 and 2 (`timeline[0]`, old row order) fail these. |
| AC2 Withdraw foot | The `it.each` asserts the button and the sentence both present or both absent for six statuses. "puts the sentence and the button at the card foot…" checks the same parent, the hairline class and that the sentence comes first. `withdrawButtonRef` stays on the button (`:198`). | Proved. The dialog description's text differs, so existing lookups stay unambiguous. |
| AC3 Success banner | `Alert tone="success"` (CircleCheck, no role). The banner test still finds no live region and now finds a decorative `svg.lucide-circle-check`. `ReviewScreen.test.tsx` keeps the single `status` announcement. | Proved. |
| AC4 Timeline | `textsOf` asserts label, sentence, actor, time and note in order per item, scoped to the Timeline region's `listitem`s, with no " · " in the actor and time paragraph. The dot and rail test, the flat surface test and the promise line test also cover it. The unknown-value test still finds no raw value, and every entry keeps a `<time>`. | Proved. The rail, dots and halo match DESIGN (dot and rail centres all at 8px). The 600 and 800 sentence weights are implemented (`font-label`, `font-title text-heading`) but not asserted; the criterion's test list does not ask for them. The e2e status assertions are untouched. QA's e2e updates are listed in `ui-detail-start-build.json` `e2e_for_qa`, including `:413` (`ol li` becomes 7) and `:424` (guidance steps), both caused by criterion 5. |
| AC5 Rejected guidance | An `h2` by name inside a named region, two `li` with the exact steps, after the Timeline region. "shows no Rejected guidance for a %s dispute" covers the other outcomes. | Proved. Stacking below 1024px comes from the existing column; QA checks the 1023 and 1024px bounds. |
| AC6 Skeleton | The `h1` sits inside `aria-busy` and outside any `aria-hidden`. There are three `li`, each with an `aria-hidden` dot, and no `[style]`. The same `h1` element is kept after load (existing `:156`), and the loading axe test passes. | Proved. |
| AC7 Dialog | Classes asserted: hairline, `max-h-dvh overflow-y-auto`, `inset-x-0 bottom-0 rounded-t-lg`, `md:` centring. The existing dialog tests pass unchanged. | Proved within jsdom's reach. The 767 and 768px bounds, 320px and 200% zoom are QA's, as the criterion says. L1 is the footer breakpoint. |
| AC8 Assurance line | `routing.test.tsx`: the caption text, `text-caption`, a decorative `svg.lucide-lock`, after "Create a login". | Proved. |
| AC9 Wordmark | `wordmark.test.ts`: dimensions, depth and colour type, 512 KiB, no metadata chunks, transparency, dot after the name, 28-32px core. | Proved. L5 is test strength. QA owns the hosted-page screenshots and the provisioning log. |

The backend items (Location, Result to ProblemDetails, ownership, stored events, replay, `TimeProvider`) do not apply:
the story is presentation only. There is no null-forgiving `!` in the story's production code. The `as HTMLElement`
casts are in tests only, on elements the test has just found.

## Pass 2: simplicity

The story adds no wrapper, interface, option or layer:

- The loading skeleton and the Rejected guidance are small local components in the screen file, following the existing
  `Row`, `FailureCard` and `NotFoundCard` pattern.
- The timeline keeps the existing describe functions and adds two dot class constants.
- `surfaceClass.ts` is discussed above.
- The sheet is responsive classes on the one existing `AlertDialogContent`, which is simpler than adding a Sheet or
  Drawer component.
- The wordmark test's decoder is the only sizeable new code. It is plain loops with a named Paeth branch, documented to
  the PNG spec.

Each part can be explained in a sentence.

## Standards check

| Mechanism | Standard | Assessment |
|---|---|---|
| Disputed on | `Intl.DateTimeFormat` through the existing `formatDayWithYear` (MDN) | Fits |
| Success banner | shadcn Alert; WCAG 2.2 SC 4.1.3 | Fits; no second announcement |
| Timeline, guidance | Ordered lists; WCAG 2.2 SC 1.3.1 | Fits (Preflight `role="list"` is the spec's logged low) |
| Skeleton | shadcn Skeleton; MDN `aria-busy` | Fits |
| Sheet and height cap | Radix Alert Dialog with Tailwind v4 responsive variants; WCAG 2.2 SC 1.4.10 | Fits; L1 |
| Back link and target size | EXPERIENCE 44px floor; WCAG 2.2 SC 2.5.8 (2.5.5 for 44px) | Fits |
| Note wrapping | CSS Text 3 `overflow-wrap: anywhere` | Fits |
| Assurance line | Lucide icon beside text, decorative | Fits |
| Wordmark | Playwright `page.screenshot({ omitBackground, clip })`; W3C PNG-3; `node:zlib` inflate | Fits |

## Security checklist (below High-stakes)

- AuthN/Z, browser session, linking, audit: not touched. No API call, route, query key or session change.
- Data: the note renders as text through a JSX template string. There is no `dangerouslySetInnerHTML` in the story
  files, and no new personal data is displayed: `createdAt` was already in `DisputeResponse`.
- Headers: no CSP change. There is no inline `style` (the loading test asserts none), and the bar widths are classes.
- Supply chain: no package added. `node:fs`, `node:path` and `node:zlib` are built-ins. The PNGs carry no text or EXIF
  chunks (verified above). The builder reports `scripts/secret-scan.sh` with no leaks over the whole uncommitted change.
- Hosted logos: same upload path and origin as story 2.6. The AWS refresh is a human deploy step, outside the pipeline.

## Notes for the supervisor

- The spec's commit split puts the `ReviewScreen.test.tsx` selector change in the `test(ui-detail-start): …` commit,
  with the other assertion updates.
- Lows to add at merge: L1, L2 (record only), L3 (unless ui-notices-states fixes it first), L4 (dedupe with
  ui-raise-review finding 3), L5.

## Capture (supervisor, 2026-11-09)

### Escapes

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
|---|---|---|---|---|
| `alert-dialog.tsx` has two consumers, so the session warning also became a bottom sheet | reviewer | pre-build review (said one consumer) | spec gap | a criterion that edits a shared primitive lists its consumers from a grep |
| The session warning does not move focus into the dialog on open | QA | the story that built the warning (pre-existing) | missing test | logged as a medium low: initial focus on Stay signed in, with a keyboard test |
| The verifier's model could not run `scripts/diff-hash.sh` in its sandbox and returned FAIL | verifier (own check) | n/a | environment or tooling | the verifier confirms the hash itself, as it did here |

### Metrics

| Fix rounds | Full-suite runs | Stages | Notes |
|---|---|---|---|
| 0 | 1 (builder) | pre-build review; frontend builder x1; reviewer x1; verifier x1; QA x1 | merged as `1e55a9d` (#27); wordmark PNGs regenerated in this story |
