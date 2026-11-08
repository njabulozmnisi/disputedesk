# ui-notices-states: post-build review

- Date: 2026-11-08
- Story: `docs/backlog/story-ui-notices-states.md` (on main), pre-build review `docs/verification/ui-notices-states-prebuild-review.md`
- Worktree: `.claude/worktrees/agent-a796a93e20cf3ab82`, uncommitted, over 2f897c3 with the ui-tokens-icons patch applied
  beneath it. Only the 25 `story_files` in `docs/verification/ui-notices-states-build.json` were reviewed. Each changed
  file was diffed against the base worktree `.claude/worktrees/agent-ad4bfc5f595f1e6a2`, and each new file was read in full.
- Tier: Feature (presentation only). Passes: correctness, simplicity, standards, and the security checklist.
- Result: **changes**. One medium finding and one low finding.

## Checks run

- `npx vitest run` on `src/components`, `routing.test.tsx`, `src/features/linking`, `TransactionsScreen.test.tsx`,
  `src/features/disputes/history` and `src/features/disputes/detail`: 15 files, 412 tests, all pass. The new tests and the
  linking tests print no `act` warnings.
- The builder's results file `TestResults/ui-notices-states-vitest-junit.xml` reports 713 tests with 0 failures and 0 errors.
- `scripts/secret-scan.sh` in the worktree found no leaks.
- `package.json` and `package-lock.json` are byte-identical to the base worktree, so this story leaves them unchanged.
  The difference from main comes from ui-tokens-icons. `alert.tsx` imports `cn` from `@/lib/utils`.

## Findings

### M1 medium: the problem notice's hairline spans only the reference row's content, not the notice

`frontend/src/components/ProblemNotice.tsx:48`, with `frontend/src/components/ui/alert.tsx:71`.

`AlertDescription` is a grid with `justify-items-start`. The reference row is a grid item of it, so the row resolves
to `justify-self: start` and is sized to fit its content (CSS Box Alignment: a grid item that is not stretched gets a
fit-content width). The row's `border-t` is the hairline, so the hairline ends where the Copy pill ends. It does not
run across the notice.

The mockup draws it full width: `.notice__body` is a flex column that stretches its children, and `.ref-row` is a
child of it (`docs/ux/mockups/app.css:215`, `:225`). DESIGN says "on its own row after a faint hairline". The Api's
`traceId` is the W3C `Activity.Id`, 55 characters, which puts the row at about 620px. In a 560px form-width notice the
row wraps and fills the width. In an 880px content-width notice it fits on one line and the hairline stops about
three-quarters of the way across. That affects the transactions and disputes load-failure notices at 1280px.

I reasoned this from the CSS and did not render it. The builder's smoke screenshot was taken at 360px, where the row
wraps and hides the problem. jsdom has no layout, so no unit test can catch it. QA's mockup comparison at 1280px would,
but only after this round.

Fix: add `justify-self-stretch` (or `w-full`) to the reference row's `div`. It is one class, and the Copy pill and the
trace ID keep their flex layout.

### L1 low: `border-[1.5px]` is a raw px value in a component

`frontend/src/components/ProblemNotice.tsx:53`.

The story's Boundaries say "no raw hex or px value in a component". The value comes from the old Copy pill line
unchanged. DESIGN gives 1.5px for this outline, the mockup's `.copy` uses it, and Button, Input, Textarea and
RadioGroup already carry the same `border-[1.5px]`. The theme has no border-width token. Changing only this line
would make it the one exception among them, so this story should not change it.

Fix, in the lows batch: give the theme one 1.5px outline width and apply it to all five components, or amend the
Boundaries line to allow DESIGN's 1.5px outline. Either way it is one decision for all five, not a fix round.

## Pass 1: correctness

| Criterion | Proof | Assessment |
|---|---|---|
| AC1 Alert tones, icon, role, registry edits | `alert.test.tsx`: an icon and role, or no role, per tone; icon first; no `line-clamp`; description not muted; status tokens, `rounded-md` and `p-4`, with no `border`, `text-sm` or `font-medium`; the caller's role wins | Proved. Break 1 (`role="alert"` on every tone) fails the info case. |
| AC2 Problem notice reference row | `ProblemNotice.test.tsx`: the row, the hairline class, the mono trace ID, the 44px class, no row without a `traceId`, the action's place both ways, Copy at 1999 and 2000 ms, and a failed write | Proved in jsdom. The hairline's width is M1. |
| AC3 Error summary and field error | `ErrorSummary.test.tsx`: the group found by name, focus, `tabindex`, `aria-labelledby`, the inner alert, the `h2`, the link, exactly one alert. `field.test.tsx`: a `size-3.5` icon placed first | Proved. The existing `getByRole('group', { name })` tests pass unchanged. |
| AC4 Rate-limited tone | DetailsScreen and OtpScreen 429 tests: `lucide-clock`, no `TriangleAlert`, the same `waitCopy` sentence, and on DetailsScreen no reference sentence | Proved. On OtpScreen the missing `TriangleAlert` already shows `ProblemNotice` is not used, since `ProblemNotice` always draws that icon. |
| AC5 StateSurface, shared h1 | `StateSurface.test.tsx`: heading level, the tile, a plain wrapper without an icon, and the same heading element when the icon appears and goes. Screen tests cover all five states. The new detail test checks that the h1 stays the same element from failure to not found. `ContactHelp` sits outside the surface as the surface's next sibling | Proved. `{Icon ? tile : null}` keeps the heading at child index 1, so React never remounts it. Break 7 (`key`) fails the identity test. |
| AC6 StepProgress | `StepProgress.test.tsx`: step 1 and step 2, the label classes, the hidden bar | Proved. Both sides of the step boundary are tested. |

- Backend items (`Location`, ProblemDetails mapping, ownership, stored events, timestamps): not applicable. The story
  is presentation only and touches no API call, route, query key or telemetry.
- No null-forgiving `!` appears in the story's production files.
- `role="alert"` is still exactly where it was before: problem notices, the rate-limit notice and the error summary's
  inner element. The success, info and neutral tones carry no role. Every `FieldError` caller renders it only when a
  message exists, so the new icon never shows alone.
- Every state on the detail screen gets the same section classes as before: the card class while loading and when
  loaded, `grid gap-4` on failure, and the surface classes only for not found.
- The ARIA name pitfall is covered: the `group` is asserted by its name.

## Pass 2: simplicity

The points the brief asked about:

- **Alert API (required `tone`, icon drawn inside).** This is simpler than the registry's caller-supplied icon. DESIGN
  fixes one icon per tone, so with the map in `alert.tsx` a caller cannot forget the icon or pick the wrong one. The
  prop is required because the icon map needs it. Naming it `tone` matches `Badge`, which already uses `tone` for the
  same status tokens. Comparing the two error tones directly (`tone === 'danger' || tone === 'rate-limited'`) would read
  a little plainer than the `announcedTones` array, but that is taste, not a finding.
- **ErrorSummary as `<Alert tone="danger" role="group">`, border dropped.** This is sound. The caller's role overrides
  the Alert's, the inner `role="alert"` stays the only announcement (tested), and the `h2` stays. Dropping the 1.5px
  destructive border follows DESIGN (notices are `{rounded.md}` with no border, `app.css:213`) and criterion 3 ("the
  danger `Alert`'s look"). The GOV.UK pattern's structure (group, name, focus, links) is kept, and only its visual
  styling follows the project's design system.
- **StateSurface's optional icon and the shared h1.** The criterion asks for exactly this ("the tile slot and surface
  classes sit on the same elements in each state and are null when not needed"), and it can be explained in one
  sentence: no icon means a plain section. Two signals on the detail screen come from the same state, `icon` and
  `sectionClass`, and both are commented where they are set. I found no smaller design that keeps the h1 mounted and
  still uses `StateSurface` for not found.
- **`border-[1.5px]` on the Copy pill.** See L1.
- **The repeated `mt-3` wrapper (5 places).** I accept it. It is one utility class that reproduces the mockup's
  `.state > .btn { margin-top: space-3 }`, not duplicated logic. An `action` slot would not fit the detail screen,
  whose state cards arrive as children. The builder recorded the slot as the upgrade path.
- **`StepProgress`.** It uses fixed two segments and `step: 1 | 2` (P9), with no speculative option.
- The change adds no wrapper, single-implementation interface or extension point. Every new file is one that the
  criteria name.

## Standards check

| Mechanism | Standard | Assessment |
|---|---|---|
| Alert | shadcn/ui Alert, Manual installation (<https://ui.shadcn.com/docs/components/alert>) | Follows the documented fallback. The CLI ran in a scratch copy and added the `cn` package, so its output was discarded, as the spec requires. |
| Alert roles | WAI-ARIA APG Alert pattern (<https://www.w3.org/WAI/ARIA/apg/patterns/alert/>) | Errors only. Fits. |
| Error summary | GOV.UK Error summary (<https://design-system.service.gov.uk/components/error-summary/>) | Structure kept. Fits. |
| Field error icon | WCAG 2.2 SC 3.3.1 (<https://www.w3.org/WAI/WCAG22/Understanding/error-identification>) | The text is kept and the icon is decorative. Fits. |
| Copy | Clipboard API `writeText` (MDN) | Behaviour unchanged. Fits. |
| Copy target size | WCAG 2.2 SC 2.5.5 and `--tap-target` | `min-h-(--tap-target)`. Fits. |
| State surface headings | WCAG 2.2 SC 1.3.1 and 2.4.3 | The caller's heading, one h1, a decorative tile. Fits. A `<section>` without a name stays generic, not a region landmark. |
| Step indicator | WCAG 2.2 SC 1.1.1 | A text label and an `aria-hidden` bar. Fits. |
| Hairline | Tailwind v4 opacity modifier on `currentColor` (<https://tailwindcss.com/docs/border-color>) | `border-current/20` matches the mockup's 20% `color-mix` with no inline style. Fits. Its width is M1. |

I found no home-grown mechanism.

## Security checklist

- AuthN/Z, browser session, linking, data and audit: not touched. The change is presentation only, and the diff changes
  no API call, route, cookie or query.
- Supply chain: no package added. `package.json` and `package-lock.json` are unchanged by this story. The shadcn CLI
  (`shadcn@4.23.0`, pinned and recorded) ran only in a scratch copy, and its `cn` dependency was not taken.
  `scripts/secret-scan.sh` found no leaks.
- Headers: no inline `style` attribute, no `dangerouslySetInnerHTML`, and no new external resource, so CSP
  `style-src 'self'` holds.
- The trace ID shown and copied is the existing reference, not personal data.

## Escapes

To be added at capture.

## Fix round 1 (2026-11-08, delta review)

- Previous tree: `.claude/worktrees/agent-a796a93e20cf3ab82`. New tree: `.claude/worktrees/agent-ad206ad8091f391c0`.
  Fix list: `docs/verification/ui-notices-states-fixes.md`.
- Delta: `diff -rq` over `frontend/src` finds four changed files. Two are the base's own fix round 2 (`RootLayout.tsx`,
  `routing.test.tsx`, the skip link) and were not reviewed. The story delta is `ProblemNotice.tsx` and
  `ProblemNotice.test.tsx`, plus the rewritten `docs/verification/ui-notices-states-build.json`.
- Result: **approve**. Both medium findings are closed, the low is fixed, and the delta adds no new finding.

### Fix round 1 checks

- `npx vitest run src/components` in the new tree: 6 files, 37 tests, all pass.
- The builder's `TestResults/ui-notices-states-vitest-junit.xml` reports 715 tests with 0 failures and 0 errors. That
  is two more than round 0, one for each new assertion block.
- `grep -nE '[0-9.]+px|#[0-9a-fA-F]{3,6}'` over the 13 production files of the story finds only three comments. No
  class carries a raw px or hex value.
- tailwind-merge 3.7.0, with the `extendTailwindMerge` config from `src/lib/utils.ts`, merges the Copy pill's classes to
  `... border-primary bg-card text-primary min-h-(--tap-target) px-4 text-label`. The caller's `text-label` replaces
  `text-body`, and `text-primary` survives, because the config declares `label` and `body` as font sizes.
- Contrast (WCAG relative luminance), from `tokens.css`. The cobalt outline against the notice's danger background is
  5.55:1 in light mode (#2B57C9 on #FDECEA) and 6.35:1 in dark mode (#8CAAF7 on #4A1A16). The label on the pill's
  card fill is 6.35:1 light and 6.47:1 dark. That passes SC 1.4.11 (3:1) and SC 1.4.3 (4.5:1).

### Earlier findings

| Finding | Status | Evidence |
|---|---|---|
| M1 medium: the hairline stops at the Copy pill | **Closed** | `ProblemNotice.tsx:49` adds `justify-self-stretch` to the row, which overrides `AlertDescription`'s `justify-items-start` for this one item (CSS Box Alignment, `justify-self`). `ProblemNotice.test.tsx:31-32` asserts the class. The last break in `build.json` removes the class and makes the named row test fail. In the builder's headless Chromium run at 1280px with a 55-character `traceId`, the row is 772px wide (x 268 to 1040), the full description width. Without the class it was 614px. jsdom has no layout, so the class assertion is the strongest unit proof possible. QA's mockup comparison at 1280px remains the visual check. |
| L1 low (the supervisor ranked it medium): `border-[1.5px]` in `ProblemNotice` | **Closed for this story** | The hand-styled `<button>` is now the shared `Button` with `variant="secondary"` and `size="compact"` (`ProblemNotice.tsx:52-60`). The 1.5px outline comes from `button.tsx`, which already existed, so the story's diff has no raw px. `size="compact"` gives `min-h-(--tap-target)` (44px), and the existing tap-target test still asserts it. The new test asserts `data-slot="button"` and `rounded-full`. Its break restores the old `<button>` and fails that test. The "Copied" for 2 s test and the keeps "Copy" on failure test are unchanged and pass. The question of a theme-wide 1.5px border token for Button, Input, Textarea and RadioGroup existed before this story and is not a finding here. |
| Verify low: the breaks used `test` as the key | **Fixed** | All 11 breaks use `test_method` with a runnable command. |

### The Copy pill's look: DESIGN versus the mockup

The mockup's `.copy` (`docs/ux/mockups/app.css:229`) is a transparent pill with a `currentColor` outline and an
inherited colour, so on the danger notice it is dark red on pink. The new pill is the standard secondary button: a
cobalt outline and label on the card fill.

The story says DESIGN and EXPERIENCE win where the mockups drift (Boundaries, from HANDOFF "Sources of truth"). DESIGN
supports the secondary button and says nothing for a `currentColor` outline:

- DESIGN line 323: "Pills (`{rounded.full}`) for every button ... the Copy button". Line 341: "Reference ... plus a
  pill Copy button". Neither line gives the Copy button its own colour.
- DESIGN line 327: "shadcn components used as-is with the tokens above: `Button`, ...". Line 329, Secondary: "white
  with a 1.5px cobalt outline and cobalt label, tint fill on hover". The `components.button-secondary` token
  (lines 157-163) says the same.
- DESIGN line 235: cobalt "means 'an action is available here': filled primary buttons, secondary-button outlines and
  labels, links ...". "It is never used for status or decoration." Copy is an action, so cobalt is the DESIGN-correct
  colour. A red outline would put an action in the status colour.
- `components.problem-notice` (lines 213-216) sets only the notice's background, foreground and radius. Nothing in
  DESIGN or EXPERIENCE (line 139: "Copy (44px)") asks for the button to take the notice's colour.

So the builder's choice follows DESIGN. A `className` override back to `currentColor` would go against DESIGN line
235 and add colour overrides for each hover and press state on one Button. It is not needed, and I rank no finding.

Note for the supervisor (not a finding): the story's Boundaries line lists the mockup drifts QA must not report
(16px icons, `waitCopy`'s single sentence). This drift is not on that list. The QA brief should say that the Copy
pill is the DESIGN secondary button (cobalt outline on card), not the mockup's `currentColor` outline, so QA does not
report it as a mismatch.

### Delta: Pass 1, Pass 2, standards, security

- Pass 1: criteria AC1 to AC6 are unchanged apart from AC2, and AC2's proof is stronger as described above. The delta
  has no `!`, no new state and no behaviour change in `copyReference`.
- Pass 2: the delta removes a second, hand-styled button and reuses the design system's component. The design is
  smaller. The only override is `className="text-label"`, which sets DESIGN's label size and is merged correctly.
- Standards: shadcn/ui Button (<https://ui.shadcn.com/docs/components/button>) and CSS `justify-self`
  (<https://developer.mozilla.org/en-US/docs/Web/CSS/justify-self>). Both are documented mechanisms, and the delta
  adds no home-grown mechanism.
- Security checklist: the delta is presentation only. It adds no package, no inline style and no new external
  resource, and the trace ID handling is unchanged.

### Findings this round

None.

## Capture (supervisor, 2026-11-08)

### Escapes

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
|---|---|---|---|---|
| The Reference row's hairline stopped at the Copy pill (grid item not stretched) | reviewer | builder smoke (360px only, where the row wraps) | missing test | the builder's browser pass covers 1280px for full-width rows |
| The Copy pill kept a raw `border-[1.5px]` against the Verify line | verifier | builder self-check | non-standard mechanism | build the control from the shared Button |
| Build record keyed breaks `test` instead of `test_method` | verifier | builder | script error | breaks keyed `test_method` in every brief since |

### Metrics

| Fix rounds | Full-suite runs | Stages | Notes |
|---|---|---|---|
| 1 (review and verify) | 3 (build, fix round, rebase onto ui-tokens-icons round 3) | pre-build review; builder x3; reviewer x2; verifier x3; QA x1 | merged as `517a11e` (#26) |
