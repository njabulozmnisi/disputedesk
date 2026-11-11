# Review: lows-2026-11-10, lane 2 (frontend)

- Worktree: `.claude/worktrees/agent-afaea93af032c63a3`, base `f6193f5`, uncommitted diff (34 tracked files, 1 new test: `frontend/src/components/ui/alert-dialog.test.tsx`).
- Builder record: `docs/verification/lows-2026-11-10-frontend-build.json`, diff hash `606931f67f59`. Recomputed: `scripts/diff-hash.sh <worktree> HEAD` now prints `6cc97b1487e2`, because the verifier's `lows-2026-11-10-frontend-verify.md` sits untracked in the worktree (the known `verify-with-copilot.sh` row in `lows.md`). With that one file left out, the hash is `606931f67f59` again, so the tree under review is the tree that was tested.
- Test results: `TestResults/frontend-junit.xml`, 866 tests, 0 failures, 0 errors (read, not re-run).
- Tier: lows batch below High-stakes, so this review also ran the security checklist.
- Read: the whole diff; `OtpScreen.tsx`, `RaiseScreen.tsx`, `InactivityWarning.tsx`, `alert-dialog.tsx`, `Header.tsx`, `Navigation.tsx`, `RootLayout.tsx` (tab-bar reserve, footer), `TransactionRow.tsx`, `DisputeRow.tsx`, `Page.tsx`; input-otp 1.5.0's `onComplete` (it runs in an effect when the value goes from shorter than `maxLength` to `maxLength`); the gateway's 429 (`infra/images/gateway/templates/nginx.conf.template:81-92`); the e2e specs that touch the changed code (`linking-screens.spec.ts`, `dispute-raise.spec.ts`, `spa-transactions.spec.ts`); EXPERIENCE.md's raise rows.
- Ran (read-only, headless Chromium from `e2e/node_modules`, with the worktree's built CSS and fonts from `frontend/dist`, the classes copied from the diff):
  - List row amounts at 768, 1024 and 1280px with 100, 150 and 200% root font, for R849.00 up to R123 456.78. The amount overflows its capped 25% track only into the 48px gap. The worst case is R123 456.78 at 768px and 200%, which touches the text column by 1px. The cap holds.
  - The phone tab bar at 320, 360 and 600px with 100, 150 and 200% root font, scrolled to the page end: see M2.
- Not run: the Vitest suite (the builder's run is the result of record), the breaks (the verifier's), the e2e suite or the Compose stack.

## Rows

| # | Row | Outcome | Assessment |
|---|---|---|---|
| 1 | ui-tokens-icons hint `gap-1.5`, `mt-0.5` | `gap-1`, comment on `mt-0.5` | Correct; test fails without it (break 1). Close. |
| 2 | SearchPriority handle path | Test asserts `m21 21-4.34-4.34` | Correct (break 2). Close. |
| 3 | `?? 'span'` placeholder | Plain `if (!description) throw` guard | Correct, simpler, no `!`. Close. |
| 4 | Skip link covers 24.6px of Transactions | Accepted in the Header comment under SC 2.5.8 | Justified: the row offered this outcome, and 80% of the link stays visible. Close. |
| 5 | Header comment padding vs content edge | Comment says content edge, cites CSS Flexbox 4.1 | Correct. Close. |
| 6 | ErrorSummary links 22px | `inline-flex min-h-(--tap-target) items-center`; `ul` gap dropped | Correct (break 3); a long message still wraps inside the link. Close. |
| 7 | One outline width token | `--outline-width: 1.5px`, read with `border-(length:--outline-width)` in Button, Input, Textarea, RadioGroup | Correct, the documented Tailwind v4 form. The built CSS has `border-width:var(--outline-width)` and the token. The theme test refuses any raw `border-[1.5px]` (break 4). Close. |
| 8 | input-otp `-m-px` | 1px border plus `inset-ring` (Tailwind box-shadow) | Correct and standard, and the builder measured equal cells. It breaks an e2e check: see M1. Close once M1 is fixed. |
| 9 | "field holding 481" paste | `it.each(['4815167', '4815 1678'])` | Correct (break 6). Close. |
| 10 | Inline "Start again" link | Kept inline, comment cites the SC 2.5.8 inline exception | Code side justified. The row asks to reconcile DESIGN.md, which is not in the diff: see L1. |
| 11 | Disabled field label | `has-disabled:[&_label]:text-muted-foreground` on `Field` | Correct. It builds to `:has(:disabled) label`, and every `Field` holds one control (6 uses, none holds a button). Break 7. Close. |
| 12 | Field error `role="alert"` after a wrong code | `role` only while `codeError === undefined && wrongCode` | Correct for the path the row names: a refused paste after a wrong code is announced once, by the live region. The test asserts both sides: the wrong-code alert first, then no alert and the status text. Break 8. One path left: see L7. Close. |
| 13 | Stale error beside six digits | `onComplete={() => clearErrors('code')}` | Correct. `onComplete` runs only when the value reaches six digits, so it clears neither the wrong-code copy (state, not a form error) nor anything while the code is short. It also removes the summary, which shows only while `codeError` is set. Tests cover the refused-paste and invalid-submit paths (break 9). The adjusted test ("typing breaks it again") now types 5 of 6 digits, so it still proves typing does not move focus to the summary. That is a fair change, not a weakened test. Close. |
| 14 | 429 on `/api/v1/antiforgery` | Already handled; two tests pin it | Credible: nginx answers with the `rate-limited` problem body, and `isProblem` matches it. The tests use `Retry-After: 120`, while the gateway sends `1`: see L6. Close. |
| 15 | List rem tracks wider than the panel | `min(var(--amount-col),25%)`, `min(var(--action-col),35%)` on transactions and disputes | Correct and standard (CSS `min()` in a track size). My measurement confirms nothing overlaps, and 100% is unchanged (at 768px, 25% and 35% are wider than 9rem and 14rem). The comment overstates what is left for the text column: see L4. Close. |
| 16 | 320px, 200%: text column 17px | Not fixed, "reason in the report" | The build record gives no reason: see L3. |
| 17 | Header scrolls sideways at 200% | Header row `flex-wrap`, tab bar `flex-wrap` plus `min-w-fit`, Button label may wrap | The header wrap is right: one line at 100%, and the DOM order stays the visual order. The Button change is safe: it is `shrink-0` and the screens put buttons in `flex-col` below md, so no 100% layout changes. The tab bar wrap brings a new defect: see M2. The detail page still scrolls 10px at 320px and 200% (the builder's own residual): see L2. |
| 18 | Review head row `items-start`, Transaction not bold | `items-end`; `<span className="font-strong">` | Correct (break 16). Close. |
| 19 | `label-col` token | "Already in the theme" | `--label-col: 9rem` is in `tokens.css:117`, but the row asks to amend criterion 5 of `story-ui-raise-review.md` and DESIGN.md's spacing tokens. Neither is in the diff: see L1. |
| 20 | Continue needs a second click | `mode: 'onTouched'` | The documented React Hook Form mode, and it fixes the path QA named (break 17). It changes the documented timing: see L5. |
| 21 | `aria-disabled:opacity-70` | Not changed, "DESIGN text for the supervisor" | Code side justified: this is the pending look, separate from disabled. The DESIGN.md text is not in the diff: see L1. |
| 22 | Hero amount widens the review card | `wrap-anywhere` on the hero amounts; detail grid `grid-cols-1` | Correct and standard (`grid-cols-1` is `minmax(0,1fr)`, so the track can shrink). At 320px and 200% a hero amount can break between digits; that is the reflow trade-off, not a finding. Close. |
| 23 | Alert-dialog footer `sm` vs `md` | `md:flex-row` | Correct (break 19). Close. |
| 24 | Wordmark test, one pixel and descender box | Four corners; the baseline from the "D" column box, dot within 2px | Correct; the test now sits on both sides of the bound. Close. |
| 25 | Session warning initial focus | Stay signed in is `AlertDialogCancel variant="default"` | Correct and the Radix-documented way: Radix AlertDialog focuses the Cancel part on open, and with no Cancel it focused nothing. The behaviour is unchanged otherwise. Cancel's click calls `onOpenChange(false)`, which runs `staySignedIn()` once, like Escape, and the controlled dialog closes only when the renewal succeeds. Before, an Action's `preventDefault` did the same, so `spa-transactions.spec.ts:464` and `:493` still hold. The test asserts the initial focus and Tab and Shift+Tab inside the dialog (break 20). Close. |
| 26 | CSP violation from the scroll lock | Accepted; shortcut comment extended with the nonce upgrade | Justified: the CSP stays strict, and the upgrade (a per-response nonce, plus `get-nonce`'s `setNonce` for react-style-singleton) is the documented route. It needs a gateway change, which is outside this lane. Close. |

Scope: every hunk maps to a row above. The detail-page hunks (`grid-cols-1` and the hero `wrap-anywhere`) go beyond row 22's review card, but they serve carried row 17 ("every page scrolls"), so they are in scope. The remaining frontend-adjacent rows (`linking-start.spec.ts` flake, the `story-ui-linking.md` Inputs bullet, the `run-regression.sh` port check) are not in this lane.

## Pass 1: correctness

- Each code row has a test that fails without the change (20 breaks). Rows 4, 5, 10, 21 and 26 change only comments. jsdom has no layout, so the class-level tests for rows 1, 6, 7, 8, 11, 15, 17, 18, 22 and 23 are backed by the builder's Chromium pass, and by mine for rows 15 and 17.
- `onComplete` and the `role` condition were checked on every path: wrong code then refused paste; refused paste then six digits; invalid submit then six digits; a 400 `validation` then six digits (the summary goes, as with an invalid submit); wrong code then six new digits (the wrong-code alert stays, as before).
- `onTouched` was checked on blur then correct, on Continue without a blur (unchanged), on reason change (it still clears `expectedAmount`), and on the e2e fill-then-blur loop at `dispute-raise.spec.ts:276-299` (same results).
- No `!` in production code. No API, data or event change.

## Pass 2: simplicity and standards

Every mechanism has a documented source: Radix AlertDialog Cancel, WAI-ARIA APG alertdialog, React Hook Form `mode`, input-otp `onComplete`, Tailwind `inset-ring` and `border-(length:…)`, CSS `:has()`, CSS `min()` in grid tracks, WCAG 2.2 SC 1.4.10 and 2.5.8. Nothing is home-grown, and each change is the smallest in its row. The `firstLetterBox` helper in `wordmark.test.ts` is test code and plain enough.

## Security checklist

Frontend only: no new dependency (`package.json` and the lock file are unchanged), no secret, no CSP or header change, no new network call. The session warning still renews with one `GET /session`, and Escape and the button share one path, so the idle timeout (OWASP Session Management) is unchanged. A full code still never submits by itself (`onComplete` only clears errors). No finding.

## Findings

### M1 (medium): an e2e check fails with row 8

`e2e/tests/linking-screens.spec.ts:220` treats the code input's focus indicator as visible only when the active cell has `borderTopWidth >= 2`. The border is now 1px, and the second pixel is the inset ring (box-shadow). Every `checkState` that tabs onto the code input then reports the OTP input as "a focused control with no visible focus indicator". The regression job runs this spec, so the pull request's checks go red. The builder flagged it as a conflict, outside its paths.
Fix (in the same change): test the active cell's `boxShadow` for the inset ring, for example `parseFloat(target.borderTopWidth) >= 1 && target.boxShadow.includes('inset')`, and update the comment on lines 216-217.

### M2 (medium): the wrapped phone tab bar hides the page end

`frontend/src/app/Navigation.tsx:37` (`flex-wrap`), against `frontend/src/app/RootLayout.tsx:74` (`pb-24`). The bar is `fixed` at the foot of the screen, and the layout keeps `pb-24` (6rem) free for it. Measured in headless Chromium with the built CSS, at 320px:

| Root font | Bar height | Reserve (`pb-24`) | Page end hidden under the bar |
|---|---|---|---|
| 100% | 58px, one row | 96px | none |
| 150% | 164px, two rows | 144px | 44px |
| 200% | 218px, two rows | 192px | 58px |

At 150% and 200% the bar wraps onto two rows and grows taller than the reserve, so the last lines of the page (today the footer's data and privacy notice) cannot be scrolled out from under it. That is lost content under text resize and reflow (WCAG 2.2 SC 1.4.4, 1.4.10). The change brings this in: before it, the bar stayed one row (114px at 200%, inside the reserve).
Fix: use the standard way, so the space follows the bar's real height. Make the bar `sticky bottom-4`, in the layout's flex column, instead of `fixed` with a fixed reserve (CSS Positioned Layout, sticky), and drop `pb-24`. Or keep one row and let each segment shrink instead of wrapping the bar. Measure at 320px and 360px with 150% and 200% text.

### Lows

- **L1** Rows 10, 19 and 21 are closed only on the code side. Their text changes are not in the diff: DESIGN.md's 44px rule versus the inline-link exception (row 10); criterion 5 of `docs/backlog/story-ui-raise-review.md` and `label-col: 9rem` in DESIGN.md's spacing tokens (row 19); DESIGN.md's Button for the `aria-disabled` pending look (row 21). The batch must carry those edits (supervisor or a docs lane), or the rows stay open.
- **L2** Row 17 is not fully closed: the dispute detail page still scrolls 10px sideways at 320px and 200% (the builder's residual). Keep a row for it.
- **L3** Row 16 says "not fixed: reason in the report", but `lows-2026-11-10-frontend-build.json` gives no reason. Put the reason in the row's closure (the phone row's second column holds the no-wrap amount; nothing is clipped).
- **L4** `frontend/src/features/transactions/TransactionRow.tsx:17-18`: "leaves the text column at least 40%" leaves out that the gaps (and, on disputes, the chevron column) come out of that 40%. At 768px and 200% the disputes text column is about 46px (574 − 143.5 − 201 − 40 − 3 × 48). Correct the comment. If the narrow column matters, add it to row 16's reason.
- **L5** `frontend/src/features/disputes/raise/RaiseScreen.tsx:79` (`onTouched`) against `docs/ux/EXPERIENCE.md:133`, "Validates on blur and Continue". After the first blur the field now also checks on every change, so clearing it shows "Enter the amount you expected to pay." at once. Update EXPERIENCE.md's timing to match.
- **L6** `frontend/src/features/linking/OtpScreen.test.tsx` (the two antiforgery 429 tests) stub `Retry-After: 120`. The gateway sends `Retry-After: 1` (`nginx.conf.template:88`), so a real user sees the notice for about a second, which is likely what QA took for "no visible notice". A test stand-in sends the upstream's real headers (Known pitfalls). Add a case with `'1'` that asserts the notice shows, then focus moves to Verify.
- **L7** `frontend/src/features/linking/OtpScreen.tsx:249`. After wrong code, then refused paste, then six digits, `clearErrors` brings back the wrong-code copy and `role="alert"` on the same element. Some screen readers will announce "That code didn't work. You have N tries left." again, about the earlier attempt. Not covered by a test. Acceptable as is, or show the wrong-code alert only until the next input.
- **L8** `frontend/src/app/Navigation.tsx:17`: with `min-w-fit`, the "Transactions" segment is about 13px wider than the bar's inner edge at 320px and 200% (measured 41-301px against the bar's 32-288px). Its active fill then runs past the pill. This goes once M2's fix keeps the segments inside the bar.

Process note (not a finding against the diff): the verifier's record in the story worktree changes `diff-hash.sh`'s output, as the open `verify-with-copilot.sh` row in `lows.md` says.

## Status

`changes`: M1 and M2 open one fix round. M1 touches `e2e/`, so the fix builder's paths must include it.

## Fix round 1 (2026-11-11)

Delta only: round 0's worktree `agent-afaea93af032c63a3` against round 1's `agent-a9958e0c9b5706eb6` (`diff -ru` of `frontend/` and `e2e/tests`). Fix list: `lows-2026-11-10-frontend-fixes.md`.

- What changed: `Navigation.tsx` (the bar is `sticky bottom-4 z-10 mx-4 mb-4`, no longer `fixed inset-x-4`; segments `px-2`), `RootLayout.tsx` (`pb-24 md:pb-0` removed), `OtpScreen.tsx` (`onComplete` replaced by an `onChange` that calls `field.onChange` and clears the `code` error at six characters), and the tests for those. Nothing else in `frontend/` or `e2e/tests` changed; `cn` is still used in `RootLayout.tsx` (by the Footer).
- Diff hash: `scripts/diff-hash.sh` prints `ade7d85ef9ea`, the builder's. Results file: 869 tests, 0 failures (read, not re-run).
- Ran (read-only, headless Chromium with round 1's built CSS from `frontend/dist`, a 30-link list page using the diff's bar and layout classes): Tab walks at 320px with 100, 150 and 200% root font, measuring whether each focused link sits entirely under the bar. I compared round 1's sticky bar, round 0's fixed bar, the bar as it was before this lane (one row, `fixed`, `pb-24`), and round 1's bar with `scroll-padding-bottom` set to its height. I also read axe-core 4.14.0's `target-size` check (`e2e/node_modules/axe-core/axe.js`), and input-otp 1.5.0's paste handler.

### Earlier findings

- **M1 (e2e focus check, `linking-screens.spec.ts:220`)**: open. Not in this round; the fix list hands it to QA in the same batch. The pull request stays red until it lands.
- **M2 (page end hidden under the wrapped bar)**: closed. The bar is now in the layout's flex column, so it takes its own height in the flow, and at the page end it sits after the footer. Short pages still put it at the bottom (`main` is `flex-1`). This is the CSS sticky positioning the finding named. The builder measured 0px under the bar in all 12 cases (320 and 360px, 100, 150 and 200%, on `/transactions` and `/disputes`).
- **L8 (`min-w-fit` segment past the bar)**: closed by `px-2`. The builder measured no segment past the bar in all 12 cases.
- **Verifier's M1 (stale error after a full-length edit)**: closed, and correct. input-otp's own paste handler runs before the page's `onPaste`. It cuts the new value to six characters, and if that value passes the pattern it calls `onChange`. So a refused over-long paste into a full field first calls `onChange` with the same six digits (clearing the error), and then the page's `onPaste` sets the error again: the refusal still shows. Every other way to six digits (typing, a paste over a selection, autofill) clears the error. A full code still never submits by itself. The new `it.each` covers the path the fix list named (a full field, a refused paste, an edit that keeps six digits). Its expected `481514` follows input-otp's `slice(0, selectionStart) + pasted + slice(selectionEnd)`, cut to six.
- Lows L1 to L7: unchanged, for `lows.md` as the fix list says.

### The axe target-size result: regression or finding?

It is not a user-facing regression. The bar and the links sit in the same places as with round 0's fixed bar; only axe's reading changed. axe-core 4.14.0 only pairs elements that are both fixed or both not fixed when it looks for overlapping targets (`selfIsFixed === _isFixedPosition(vNeighbor)`, `axe.js:19881`). A `fixed` bar was therefore never compared with the rows under it, while a `sticky` one is. When a row's link is mostly covered, and all of the obscuring targets are in the tab order, `targetSizeEvaluate` returns `false` (`partiallyObscured`), and that counts as a violation.

It is a finding all the same, because the e2e suite runs axe with `wcag22aa` (which includes `target-size`) at phone widths with the bar on screen: `spa-disputes-history.spec.ts:214` ("axe at 320"), `spa-dispute-status-badges.spec.ts:233` ("axe at 390"), and the journeys' `screens.ts`. Whether a seeded page puts a link's last pixels under the bar at scroll position 0 depends on its rows. The builder's own run hit it on `/transactions`. See R1-M2.

### Empty Disputes state, 90px sideways at 320px and 200%

Pre-existing, and not made worse by this lane. The cause is the layout, not this round. `Page` is `grid` with no template, so its one implicit column is never narrower than its widest item's min-content, and `StateSurface` is a `grid justify-items-start` section holding the Button, with `px-6` (48px a side at 200%). On main the Button was `whitespace-nowrap`, so "Go to transactions" made the column even wider than now. That comes from reading the code: I did not measure main. It is the same cause as the detail page's 10px residual (L2), which round 0 fixed on that screen with `grid-cols-1`. See R1-L1.

### New findings

#### R1-M1 (medium): a focused link can sit entirely under the two-row tab bar

`frontend/src/app/Navigation.tsx:37`. At 150% and 200% text the bar wraps to two rows, 164px and 218px tall plus 16px (`bottom-4`) at 200%. When Tab moves focus to a link in the lower part of the screen, Chrome scrolls only as far as needed to bring it into the viewport, not clear of the bar, so the link ends up under the bar. Tab walk over 30 links at 320px:

| Bar | 100% | 150% | 200% |
|---|---|---|---|
| Before this lane (one row, `fixed`, `pb-24`) | 0 hidden | 0 hidden | 0 hidden |
| Round 0 (`fixed`, wraps) | 0 | 6 hidden, 1 partly | 9 hidden |
| Round 1 (`sticky`, wraps) | 0 | 6 hidden, 1 partly | 9 hidden |
| Round 1 with `scroll-padding-bottom` set to the bar's height | 0 | 0 | 0 |

A focused control that is entirely hidden fails WCAG 2.2 SC 2.4.11 Focus Not Obscured (Minimum), level AA. The builder's walk saw the same thing ("rows scroll under the bar at 150%, as before"); "before" there meant round 0. Round 0's wrap brought it in, and I missed it in the first review (see the escape below). The page is a synthetic list using the diff's classes, so the exact counts depend on the rows. The mechanism does not.
Fix: the documented one, CSS `scroll-padding` on the root (WCAG technique C43). Below md, set `scroll-padding-bottom` on `html` to the two-row bar's height plus its offset. That is about `8rem`, all in rem so it scales with the text: 2 × `min-h-12`, `p-1` and `gap-1`, a 2px border, and `bottom-4`. Assert the class in a test, and measure the Tab walk at 320px with 150% and 200% text.

#### R1-M2 (medium): the sticky bar now trips axe target-size in the e2e phone-width checks

`frontend/src/app/Navigation.tsx:37`, against the `wcag22aa` axe checks at `e2e/tests/spa-disputes-history.spec.ts:214`, `e2e/tests/spa-dispute-status-badges.spec.ts:233` and `e2e/tests/journeys/screens.ts`. Explained above: the axe reading changes with `sticky`, not the layout. The builder's run already showed one result on `/transactions` at 100%. If a seeded e2e page does the same, the pull request's checks go red. This is likely but unverified: I did not run the e2e suite.
Fix: run those three specs against the round-1 build before merging. If any shows `target-size`, choose one of two layouts.

- Go back to a `fixed` bar, and reserve a fixed rem space sized for two rows (`pb-32`, 8rem, below md) together with R1-M1's `scroll-padding-bottom`. That keeps M2 closed at every text size and gives axe nothing to pair. The cost is about 54px of extra blank space under the footer at 100%.
- Or keep `sticky` and name the axe result for the human as a tool artefact (`[decision]`). Excluding the rule in the tests is not an option: that would weaken them.

#### R1-L1 (low): the empty Disputes state scrolls 90px sideways at 320px and 200%

`frontend/src/components/Page.tsx:15` (`grid` with no template) and `frontend/src/components/StateSurface.tsx:29`. Pre-existing (above). The likely fix is `grid-cols-1` on `Page`, which may also clear L2's 10px on the detail page. Add a row to `lows.md`; it is unverified until measured.

### Escape

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
|---|---|---|---|---|
| R1-M1, focus hidden under the wrapped bar | reviewer, fix round 1 | reviewer, first review (and the builder's keyboard walk) | missing test | For a fixed or sticky bar, measure the Tab walk at 150% and 200% text for a focused control entirely under it, as well as the page end (one line in the review skill's Known pitfalls). |

### Status (fix round 1)

`changes`: R1-M1 (and R1-M2, if the e2e axe checks show `target-size`) open the second and last fix round. M1 (the e2e focus check) stays with QA in this batch.

## Fix round 2 (2026-11-11)

Delta only: round 1's worktree `agent-a9958e0c9b5706eb6` against round 2's `agent-a25dc814bf4b1d086` (`diff -ru` of `frontend/` and `e2e/tests`). The supervisor's decision is in `lows-2026-11-10-frontend-fixes.md`, "Fix round 2".

- What changed, and nothing else:
  - `Navigation.tsx` and `RootLayout.tsx` are byte-identical to `f6193f5`: `git diff f6193f5` on both is empty. The bar is back to `fixed inset-x-4 bottom-4`, one row, segments `px-3`, no `min-w-fit`, and the layout's `pb-24 md:pb-0` is back.
  - The two tab-bar tests are removed from `routing.test.tsx`. "lets the header row wrap" stays (line 725), and `within` is still used in the file.
  - The verifier's M1 fix (`OtpScreen.tsx:221`, the `onChange` clearing the code error at six characters) and its tests are unchanged.
  - No other file in `frontend/` or `e2e/tests` differs from round 1.
- Diff hash: `scripts/diff-hash.sh` prints `c3301d13d4a7`, the builder's. Results file: 867 tests, 0 failures (869 less the two removed tests; read, not re-run). The builder's record lists 20 breaks. The four round-1 tab-bar breaks are gone, and none of the 20 targets the tab bar.

### Earlier findings

- **R1-M1 (focus wholly under the bar)**: no longer applies. The bar is main's one-row bar again. My own round-1 Tab walk measured that exact bar (`fixed`, one row, `pb-24`) at 320px with 100, 150 and 200% text, and found 0 focused links wholly under it. The builder measured the same on the real `/transactions` and `/disputes`, in 12 cases, in both this tree and a build of `f6193f5`. Its only partial overlaps (2 stops on `/disputes` at 320px and 200%) are identical on main.
- **R1-M2 (axe target-size with a sticky bar)**: no longer applies. axe-core 4.14.0 never compares a `fixed` element with non-fixed ones (`axe.js:19881`), which is how main's bar has always passed the e2e axe checks.
- **M2 (end of the page under the bar)**: stays closed. The one-row bar plus its offset (58 + 16, 86 + 24, 114 + 32 px at 100, 150 and 200%) fits inside the `pb-24` space (96, 144, 192 px). The builder measured 0px under the bar in all 12 cases, in both trees.
- **L8 (`min-w-fit`)**: no longer applies; the class is gone.
- **Carried row 17**: the header-row wrap stays. The builder measured 0 horizontal overflow on the lists at 320 and 360px with 150 and 200% text, where main overflows 19 to 132px. The tab-bar part goes back to `lows.md`, as decided: at 320px and 200% text the one-row bar leaves "Disputes" partly off-screen, as on main. The write-up there names the later fix: a `fixed` bar, a two-row rem space (`pb-32` below md), and `scroll-padding-bottom` on `html` (WCAG technique C43). That matches my round-1 suggestion.
- **M1 (e2e focus check at `linking-screens.spec.ts:220`)**: still open, assigned to QA in this batch. The pull request stays red until it lands.
- Lows L1 to L7 and R1-L1: unchanged, for `lows.md`.

### Assessment

The delta matches the decision exactly, and it adds nothing new: the tab bar, its layout space and its tests are main's again. Every round-0 and round-1 change outside the tab bar is kept. No new findings.

### Status (fix round 2)

`approve` for this lane's code. Before merge: QA's update of `e2e/tests/linking-screens.spec.ts:220` (M1), and the lows and docs rows (L1 to L7, R1-L1, row 17's tab bar) going to `lows.md` or the supervisor's docs edits.
