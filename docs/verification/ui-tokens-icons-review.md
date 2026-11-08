# Review: ui-tokens-icons

Reviewer, post-build, 2026-11-08. Scope: the uncommitted change in the builder's worktree `.claude/worktrees/agent-a4d4a390a934b483b` (branch `feature/ui-tokens-icons`, base `origin/main` `2f897c3`), 13 modified and 3 new files. Read against the spec `docs/backlog/story-ui-tokens-icons.md` (ux-docs tree), `docs/ux/HANDOFF.md` 1, 2, 3.1 and 3.2, `docs/ux/DESIGN.md` (Layout & Spacing, Wordmark, Icons), `mockups/app.css`, the pre-build review and the builder record `docs/verification/ui-tokens-icons-build.json`. Tier: Feature (below High-stakes), so this review runs the security checklist too.

Verdict: **changes**. One medium finding (an absence assertion that cannot fail) and four lows.

## Checks run (read-only)

- Builder results `TestResults/frontend-junit.xml`: 676 test cases, `failures="0"`. Every new test name the record maps is in it.
- Glyph grep `grep -rnE '➚|◔|▲|✓|✕|↩|ⓘ|⚠|☰|⚑' frontend/src` printed nothing.
- No raw hex or px in the changed components. The only `px` hits are in comments, plus `lg:grid-cols-[minmax(0,1fr)_400px]` in `StartPage.tsx:86`, which is already on main and not in this diff.
- Lucide 1.70.0 installed source: `Icon.mjs` sets `hasAccessibleProp = Boolean(children) || hasA11yProp(rest)`, and `buildLucideIconNode.mjs:47` adds `aria-hidden="true"` when that is false. `hasA11yProp` returns true for any `aria-*`, `role` or `title` prop. The app has no `LucideProvider`, and no icon gets an `aria-*`, `role`, `title` or children.
- Registry: `lucide-react@1.70.0` is `latest`, repository `github.com/lucide-icons/lucide`, no install scripts. The integrity matches the lockfile entry. The lockfile adds only `node_modules/lucide-react`, which has no dependencies.
- `scripts/secret-scan.sh` in the worktree: no leaks. `npm audit --omit=dev --audit-level=high`: 0 vulnerabilities.
- A scratch check with the worktree's jsdom and `@testing-library/dom` 10.4.2 (aria-query 5.3.0) showed that `queryAllByRole(nav, 'img', { hidden: true })` returns 0 for a nav that holds a Lucide-style `<svg aria-hidden="true">`. That check is the basis of the medium finding.

## Pass 1: correctness

| AC | Proof | Judgement |
|---|---|---|
| 1 list columns | `theme.test.ts` "declares the list columns". The break removes `--amount-col`. | Proved. The two declarations sit in `:root` (`tokens.css:116-117`). No `@theme` mapping and no colour change. |
| 2 exact pin | `packagePins.test.ts`, plus reading the diff | Proved. `"lucide-react": "1.70.0"` appears in `dependencies` only, and no other package was added (lockfile checked). |
| 3 status icons | `customerStatus.test.ts` per row, plus the prioritised case | Proved for every row. `SearchPriority` is at module level, built with `createLucideIcon`, and each node has a unique key. The test checks the lens circle and the arrow, but not the handle (low 3). |
| 4 decorative icons | `expectDecorativeIcon` on the badge (line 409 test), the tab bar, the hint (`size-3.5`) and the new `StartPage.test.tsx` | Proved per surface: `aria-hidden`, `stroke`, `stroke-width` and the size class. The line 398 and 724-725 changes are exactly as the spec says. The "top navigation with text only" half of the tab bar test proves nothing (medium 1). |
| 5 wordmark and 44px | `routing.test.tsx` "the header wordmark" (2 cases) | Proved. The dot is the last child, after the text node, `aria-hidden` and `size-2`. The home link and the `TopNav` links carry `min-h-(--tap-target)`. The accessible name "DisputeDesk" is still asserted by the existing tests. |
| 6 no glyph | `theme.test.ts` "has no text-glyph icon", plus the grep | Proved. The escapes decode to exactly the ten glyphs. `appSourceFiles` skips tests, and the grep covers them. |

Backend items (Location, `Result`/ProblemDetails, the ownership filter, stored events, version clash) do not apply. No `!` in production code.

Wordmark geometry: the dot is an empty flex item in an `items-baseline` container. Flexbox synthesises its baseline from the bottom edge, so it sits on the text baseline like a full stop, as DESIGN Wordmark asks (CSS Flexbox Level 1, 8.5 / 9.6). QA's browser comparison confirms the drawn result.

## The four points in the brief

1. **`frontend/src/packagePins.test.ts` (not named in the spec).** It earns its place. It turns NFR-CODE-5 and AGENTS.md "pinned exactly" into a deterministic check over every dependency. That is the repo's preferred form for a rule ("a repeated mistake becomes a check, not a sentence"). It gives criterion 2 a break that can fail, and it is short and plain. Nothing else in the frontend enforces the pin today (`.npmrc` has no `save-exact`, and CI has no such check). Its second case ("lists lucide-react once, as a runtime dependency") is specific to this story inside a test that covers the whole repo. It cannot prove "once", because `JSON.parse` keeps the last duplicate key. It only proves "in `dependencies`, not in `devDependencies`". That is tolerable, so no finding.
2. **`frontend/src/test/icons.ts` (`expectDecorativeIcon`).** It earns its place. Three test files (routing, TransactionsScreen, StartPage) use it at five call sites. That meets AGENTS.md's "abstract on the third real case". It lives in `src/test/` beside `server.ts`, `support.tsx` and `sourceFiles.ts`, which is the repo's home for test helpers. The one parameter, `sizeClass`, is used (`size-3.5` for the hint), so it is not speculative. The builder record says "four test files". The count is three.
3. **Relying on Lucide's default `aria-hidden`.** Accepted. It is the documented default (<https://lucide.dev/guide/react/advanced/accessibility>), and criterion 4 names it as the mechanism. The installed 1.70.0 source behaves as documented (see Checks). Every surface's test asserts the attribute, so an upgrade that changes the default fails the suite. Adding an explicit `aria-hidden` would duplicate the framework default.
4. **The hint's `gap-1.5` / `mt-0.5`.** Low finding 2. The builder's record says "DESIGN.md gives 6px gap and 1px offset". DESIGN does not say that: the values are the mockup's `.hint` (`mockups/app.css:143-145`). DESIGN Layout & Spacing says "the Tailwind subset 1, 2, 3, 4, 6, 8, 12, 16 … Nothing outside it", and its only named exception is the wordmark's 2px. The spec already settled the same choice for the badge in DESIGN's favour: it keeps `gap-1`, not the mockup's 6px. So the hint gap should be `gap-1`. The `mt-0.5` offset is geometrically right: a 13px caption at 1.4 line height gives an 18.2px line, and centring a 14px icon in it needs 2.1px. No step in the subset fits, so mark it the way the wordmark's exception is marked. Neither value is raw px. Both are theme scale steps, so the "no raw hex or px" boundary holds.

## Findings

| Sev | File:line | Finding | Fix |
|---|---|---|---|
| medium | `frontend/src/app/routing.test.tsx:374-378` | The test "draws the phone tab bar … and the top navigation with text only" asserts `within(Main).queryByRole('img', { hidden: true })` is absent. An `<svg>` has no implicit role in `@testing-library/dom` 10.4.2 (aria-query 5.3.0 maps no role to `svg`), and Lucide adds no `role`. So the query returns nothing even when the top nav holds Lucide icons (scratch check above). The absence assertion cannot fail, and criterion 4's boundary "`TopNav` keeps text only" is unproved, although the test name claims it. This is the same kind of gap as the known pitfall on absence assertions. | Assert on the element that would appear: `expect(screen.getByRole('navigation', { name: 'Main' }).querySelector('svg')).toBeNull()`. Add a break that puts `<List className="size-4" />` in a `TopNav` link and expects this test to fail. |
| low | `frontend/src/features/transactions/TransactionRow.tsx:65-66` | `gap-1.5` (6px) is outside DESIGN Layout's spacing subset ("Nothing outside it"). The value comes from the mockup, while the spec keeps DESIGN's `gap-1` for the badge over the mockup's 6px. `mt-0.5` (2px) is also outside the subset but has no in-subset equivalent. The builder record cites the wrong source (DESIGN.md) for both. | `gap-1`. Keep `mt-0.5` with a one-line comment that names it as an optical offset for the 14px icon on the caption line, as the wordmark's `ml-0.5` comment does. Alternatively, have DESIGN name the exception. |
| low | `frontend/src/features/disputes/customerStatus.test.ts:33-35` | The `SearchPriority` test checks Lucide's lens circle and the arrow but not the handle `m21 21-4.34-4.34`. Criterion 3 names both of Search's nodes, and a changed handle would pass. | Add `expect(container.querySelector('path[d="m21 21-4.34-4.34"]')).toBeInTheDocument()`. |
| low | `frontend/src/features/disputes/customerStatus.test.ts:23-25` | Simplicity: `description?.icon ?? 'span'`, followed by `expect(Icon).not.toBe('span')`, is a sentinel trick to avoid a non-null assertion. It takes a second read. | `const description = describeCustomerStatus('underReviewPrioritised'); if (!description) throw new Error('no description');`, then use `description.icon`. Or `expect(description?.icon).toBeDefined()` before rendering. |
| low | `docs/verification/ui-tokens-icons-build.json` (assumptions) | The record says `expectDecorativeIcon` is used by "four test files" (it is three) and that DESIGN.md gives the hint's 6px/1px (the source is `mockups/app.css`). This is the record's accuracy only. Nothing in the code changes. | Correct the two lines in the fix round's record. |

## Pass 2: simplicity

- `StatusDescription.icon: LucideIcon` with one render site (`const Icon = statusDescription.icon; <Icon className="size-4" />`) is the smallest change. It adds no wrapper component and no icon registry.
- `SearchPriority` is one `createLucideIcon` call at module level with three keyed nodes, as the spec says.
- The wordmark is one inner span plus the dot. Removing the old `gap-2` is correct now that the link has one child.
- The two new files earn their place (see points 1 and 2 above). `StartPage.test.tsx` is named by the spec.
- No speculative options, flags or extension points. Nothing belongs in `docs/production-path.md`.

## Standards check

| Mechanism | Standard | Home-grown? |
|---|---|---|
| List-column tokens as `:root` custom properties | Tailwind v4 theme variables <https://tailwindcss.com/docs/theme>; CSS Custom Properties | No |
| Exact pin and the pin test | npm `--save-exact` <https://docs.npmjs.com/cli/commands/npm-install>; AGENTS.md Dependency verification | No (the test is a plain check of the written rule) |
| Icons as Lucide React components | <https://lucide.dev/guide/react> | No |
| Custom icon via `createLucideIcon` | <https://lucide.dev/guide/react/advanced/typescript>; the `createLucideIcon` export in 1.70.0 | No |
| Decorative icons hidden, text carries meaning | Lucide accessibility <https://lucide.dev/guide/react/advanced/accessibility>; WCAG 2.2 SC 1.1.1, 1.4.1 | No |
| Wordmark dot on the baseline | Tailwind `items-baseline` <https://tailwindcss.com/docs/align-items>; CSS Flexbox baseline alignment | No |
| 44px targets | the existing `--tap-target` token; WCAG 2.2 SC 2.5.5 | No |

Each mechanism can be explained in one sentence. None is home-grown.

## Security checklist (below High-stakes)

- AuthN/Z, browser session, linking, audit: not touched. No endpoint, cookie, route guard, session or linking code changed.
- Data: no API call, query key, log, trace or telemetry change. No personal data added to the DOM. The badge's sr-only text and the dispute-id absence test are unchanged.
- Supply chain: one new package. It is verified in the registry (latest tag, repository, integrity matches the lockfile, no install scripts) and pinned exactly. `npm audit --omit=dev --audit-level=high` reports 0. `scripts/secret-scan.sh` over the whole uncommitted change (untracked files included) reports no leaks.
- Headers and CSP: unchanged. Lucide renders inline SVG with presentation attributes only (no `style` attribute, no fetch), so the nginx CSP needs no change.
- No security findings.

## For QA (not findings)

- e2e updates the builder listed: `e2e/tests/spa-disputes-history.spec.ts:282-283` (glyph filters become `svg.lucide-check` / `svg.lucide-x`) and `e2e/tests/spa-transactions.spec.ts:331` (`'Transactions'`, `'Disputes'`).
- In a real browser: the wordmark dot's baseline position, the hint icon's alignment on wrapped lines, and the header height now that the home link and `TopNav` links are 44px.

## Fix round 1 (2026-11-08)

Delta review only. Scope: the fix builder's uncommitted change in `.claude/worktrees/agent-ad4bfc5f595f1e6a2`, compared with the reviewed change in `.claude/worktrees/agent-a4d4a390a934b483b`. Both are on `HEAD` `2f897c3` with the same 13 modified and 4 untracked paths.

Verdict: **approve**. The medium finding is closed. No new findings.

### What changed

`diff -rq` over both trees (excluding `node_modules`, `.git`, `bin`, `obj`, `dist`) lists three files only:

- `frontend/src/app/routing.test.tsx`: one hunk at line 374. The `within(Main).queryByRole('img', { hidden: true })` absence check is replaced by `expect(screen.getByRole('navigation', { name: 'Main' }).querySelector('svg')).toBeNull()`, which is the fix the first review asked for.
- `docs/verification/ui-tokens-icons-build.json`: the record (break 9 added, each `test_method` is now a runnable command, new diff hash and assumptions).
- `TestResults/frontend-junit.xml`: the new results file. It has 676 tests and `failures="0"`, and it includes the changed test.

No production file changed, and no other test or e2e, backend or infra file changed.

### Checks run (read-only, in a scratchpad clone of the fixed `frontend/`, since deleted)

- The fixed test with no break passes (1 passed, 87 skipped).
- With break 9 applied (`<List className="size-4" />` in the `TopNav` Transactions link; the patch applies cleanly at `Navigation.tsx:18-20`), the test fails: `expected SVGSVGElement … to be null`. Break 9 is real.
- For contrast, the earlier version of `routing.test.tsx` with break 9 still applied passes. This confirms that the old assertion could not fail and the new one closes the gap.
- The `-t` filter matches exactly one test, so the break's `test_method` runs the intended test.

### Earlier findings

| Sev | Finding | Status |
|---|---|---|
| medium | `routing.test.tsx:374-378`: the absence assertion could not fail | **Closed.** It now asserts on the `svg` itself, and break 9 proves it fails when the top nav gets an icon. |
| low | `TransactionRow.tsx:65-66`: `gap-1.5` / `mt-0.5` fall outside DESIGN's spacing subset | Open, so it goes to `lows.md`. The code is unchanged. The record now cites the right source (`mockups/app.css`), but the DESIGN subset conflict remains. |
| low | `customerStatus.test.ts:33-35`: the Search handle path is not asserted | Open, so it goes to `lows.md`. |
| low | `customerStatus.test.ts:23-25`: the `?? 'span'` sentinel | Open, so it goes to `lows.md`. |
| low | `ui-tokens-icons-build.json`: "four test files" and the DESIGN.md source | **Closed.** The record now says three test files and names the files, and it cites `mockups/app.css` for the hint's 6px and 1px. |

### Pass 1, Pass 2, standards and security on the delta

- Correctness: criterion 4's "`TopNav` keeps text only" boundary is now proved by a test that fails without it. The other criteria are unchanged from the first review.
- Simplicity: the new assertion is one line and plainer than the one it replaces.
- Standards: no new mechanism. `querySelector` is the DOM standard, and `toBeNull` is a Vitest built-in.
- Security: the delta touches only a test and the build record. No security impact.

## Fix round 2 (2026-11-08)

Delta review only. Scope: the fix builder's uncommitted change in `.claude/worktrees/agent-ad782c578e37f341c`, compared with the round 1 change plus QA's two e2e edits in `.claude/worktrees/agent-ad4bfc5f595f1e6a2`. Fix list: `ui-tokens-icons-fixes.md`, round 2, finding 2 (QA, medium: at 320px the focused skip link overlapped the 44px home link, and axe failed SC 2.5.8).

Verdict: **approve**. The QA medium finding is closed in code. The e2e confirmation waits on QA's re-run of the two linking projects. No new findings.

### What changed

`diff -ruN` over `frontend/src` and `e2e` (excluding `node_modules`), plus `diff -rq` over the whole tree (excluding `node_modules`, `dist`, `.git`, `docs`, `TestResults`), list two files only:

- `frontend/src/app/RootLayout.tsx:75-80`: the skip link's focus classes. Before, `focus:absolute focus:top-2 focus:left-2 focus:z-20 … focus:py-2` floated the link over the header. Now it uses `focus:mx-4 focus:my-2 focus:inline-flex focus:min-h-(--tap-target) focus:items-center focus:self-start …`. Once `focus:not-sr-only` resets the position to static, the link is a normal flex item in the shell's `flex-col` column, above `<Header>`, so it pushes the header down while it has focus. The comment now gives the reason (SC 2.5.8). The `href`, the `onClick` that moves focus by script and the link's place as the first focusable element are unchanged.
- `frontend/src/app/routing.test.tsx:712-723`: one new test. On `/transactions`, signed in as linked, it asserts `focus:min-h-(--tap-target)` is present and that `focus:absolute` and `focus:fixed` are absent.

`e2e/` is identical in both trees, so QA's edits (`spa-disputes-history.spec.ts:282-283`, `spa-transactions.spec.ts:331`) are kept unchanged. `Header.tsx` and `Navigation.tsx` have newer modification times but the same content. That fits the builder applying and reverting the breaks with `git apply` / `git apply -R`.

### Judgement

- **Standard.** This is the plain in-flow skip link. Technique G1 (<https://www.w3.org/WAI/WCAG22/Techniques/general/G1>) puts the link first and makes it visible on focus. Because the link is in normal flow, it can obscure nothing, so SC 2.5.8 (<https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum>) holds by construction and does not depend on the header's height. The fix list also allowed making the link cover the home link fully. The builder rejected that because it ties the link to the header's height, and that reasoning is sound. The 44px comes from the existing `--tap-target` token (`tokens.css:111`) through Tailwind v4's `min-h-(<custom-property>)` form, with no raw px. The link also grows from 40px to 44px, which matches DESIGN.md's rule for every interactive element.
- **Plain.** It is one class list and one comment. There is no new component, state or script, and one sentence explains it.
- **Every route and session state** (Known pitfalls, 7.14). The link sits in `RootLayout` above every `<Outlet/>`, and the change is CSS only. The existing skip-link tests still pass for every route: I ran `npx vitest run src/app/routing.test.tsx -t skip` in the new worktree and got 17 passed. The builder's headless-Chromium smoke covered the anonymous, unlinked and linked states at 320, 360 and 1280px. The skip link was 44px tall at y8 and the home link at y70, with no overlap and no axe violation. With the old classes restored, the same smoke reproduced QA's overlap and the `target-size` violation.
- **Activation.** Enter or click moves focus to `<main>`, so the link loses focus and collapses, and the header moves back up. That is the expected behaviour of an in-flow skip link, and focus is already on the target.

### Does the test plus break prove it

- The unit test asserts classes, not geometry, because jsdom has no layout. That is the most a component test can prove here. It fails if `focus:min-h-(--tap-target)` is removed or `focus:absolute` comes back. Break 10 restores the exact pre-fix class list, which fails both conditions. The verifier runs the break.
- The geometry and the axe result rest on the builder's smoke and, authoritatively, on QA's re-run of `linking-screens` and `linking-queue`, which the fix list requires before merge.
- Results file `TestResults/frontend-junit.xml`: 677 tests, `failures="0"`, and it includes the new test. `scripts/diff-hash.sh <worktree> 2f897c3` gives `0a1bf425eec8`, which is the hash the build record names.

### Earlier findings

| Sev | Finding | Status |
|---|---|---|
| medium (QA F1, round 2) | At 320px the focused skip link overlapped the 44px home link (SC 2.5.8) | **Closed in code.** The link is in normal flow, 44px tall, above the header. The unit test, break 10 and the builder's smoke prove it. The e2e confirmation waits on QA's re-run of the two linking projects. |
| medium (round 1) | `routing.test.tsx:374-378`: the top-nav absence check could not fail | Closed in round 1. The code is unchanged. |
| low ×3 | `TransactionRow.tsx:65-66` (spacing subset); `customerStatus.test.ts:33-35` (Search handle); `customerStatus.test.ts:23-25` (`?? 'span'` sentinel) | Open. The code is unchanged, and they go to `lows.md` at merge. |

### Pass 1, Pass 2, standards and security on the delta

- Correctness: the fix list's requirements are met. The link sits clear of the home link, uses theme tokens only, stays first in tab order and is visible on focus. No acceptance criterion changes.
- Simplicity: the new class list is smaller than the old one (no positioning, no z-index).
- Standards: WCAG 2.2 technique G1 and SC 2.5.8, cited in the code comment and in the build record's `standards`. Nothing is home-grown.
- Security: the delta is CSS classes and one test, so there is no security impact.

## Fix round 3 (2026-11-08)

Delta review only. Scope: the fix builder's uncommitted change in `.claude/worktrees/agent-ae1ed41bc1abcb91e`, compared with round 2 in `.claude/worktrees/agent-ad782c578e37f341c`. The human authorised a third round. Fix list: `ui-tokens-icons-fixes.md`, round 3, finding 3. QA F4 was medium: focusing the round 2 in-flow skip link shifted the page down 60px, so a pointer click made while it had focus was lost. The human's decision is a floating skip link that fully covers the home link, with no layout shift.

Verdict: **approve**. F4 is closed in code. The e2e confirmation waits on QA's re-run of linking-screens, linking-queue and journeys. There are three new low findings and no high or medium.

Escape for the capture: the round 2 review approved the in-flow link and called the header moving back on blur "expected". It missed that the shift makes a pointer click fail (SC 2.5.2), which QA then caught as F4. Category: input boundary (the focus-then-pointer path). This matches the existing Known pitfall "for a focus or timing fix, every path through the changed code is checked".

### What changed

`diff -rq` over `frontend/src`, `docs` and `e2e` shows four changed files. `e2e/` differs only by a `test-results` folder in the old tree.

- `frontend/src/app/Header.tsx:15-16, 20, 31-46`: the skip link moves out of `RootLayout`. It is now the first child of the header's inner flex container (`flex min-h-16 items-center gap-6 px-4 py-2 md:px-6`), directly before the home link. It uses `focus:absolute focus:z-20 focus:inline-flex focus:min-h-(--tap-target) focus:items-center focus:rounded-full focus:bg-card focus:px-4 focus:shadow-overlay` and has no inset or margin utility. A new `onSkipToMain` prop carries the focus call.
- `frontend/src/app/RootLayout.tsx:75-80`: the `<a>` is removed, and `onSkipToMain={() => mainRef.current?.focus()}` is passed to `Header`. The `href="#main"`, the `preventDefault` and focusing by script are unchanged, and so is `<main id="main" tabIndex={-1}>`.
- `frontend/src/app/routing.test.tsx:655-665`: the first-Tab test now asserts that the skip link comes before the home link instead of before the banner.
- `frontend/src/app/routing.test.tsx:711-728`: the class test now asserts four things: the skip link and the home link share a parent; the skip link has `focus:absolute` and `focus:min-h-(--tap-target)`; the home link has `min-h-(--tap-target)`; and the skip link has no top/right/bottom/left/inset/start/end or margin utility.
- `docs/verification/ui-tokens-icons-build.json`: round 3 notes. Break 10 now restores the pre-story floating geometry (`focus:top-2 focus:left-2`, no tap-target height). The e2e glyph lines changed only in JSON escaping (`✓` and the like); the content is the same.

### Is the positioning standard and robust

- **Standard.** CSS Flexible Box Layout Level 1, section 4.1 "Absolutely-Positioned Flex Children" (<https://www.w3.org/TR/css-flexbox-1/#abspos-items>), says this: "The static position of an absolutely-positioned child of a flex container is determined such that the child is positioned as if it were the sole flex item in the flex container." When every inset is `auto`, the box sits at that static position (CSS Positioned Layout Level 3, section 4, <https://www.w3.org/TR/css-position-3/#abspos-insets>; CSS 2.1 sections 10.3.7 and 10.6.4). On the main axis the container's `justify-content` is `normal`, so the skip link starts at the content edge, where the home link (the first in-flow item) also starts. On the cross axis, `items-center` centres both boxes on the same single flex line. Both are `min-h-(--tap-target)` (44px) and one line tall, so they get the same top and the same height. The skip link is about 52px wider (181px against QA's measured 129px), so its box contains the home link's box. This is documented behaviour, supported in every current engine. It needs no offsets that could drift from the header's `px-4 md:px-6` or `min-h-16`. Unlike the option rejected in round 2, it does not depend on the header's height, because centring cancels it out. One sentence explains it.
- **Out of flow, so no shift.** Absolutely positioned children take no part in flex layout and no `gap`. The link does not move anything when focused or when it collapses back to `sr-only`. That meets SC 2.5.2 and SC 3.2.1 as the fix list asks.
- **Unchanged from main.** The `focus:not-sr-only focus:absolute` pair (which wins over `position`) is the same pair main ships, and QA measured main's link as floating.
- **Every route and session state** (Known pitfalls, 7.14). `Header` renders on every route, including the session-ended screen (state `undefined`). I ran `npx vitest run src/app/routing.test.tsx -t "skip link"` in the new worktree: 17 passed. Results file `TestResults/frontend-junit.xml`: 677 tests, `failures="0"`, and it includes the new test. `scripts/diff-hash.sh <worktree> 2f897c3` gives `d4ad4fb4eb25`, which matches the build record.

### Is a skip link inside the banner acceptable

Yes. Technique G1 (<https://www.w3.org/WAI/WCAG22/Techniques/general/G1>) asks for a link "at the top of the page" that goes to the main content. It names no landmark. SC 2.4.1 is met, because the link is still the first focusable element, as the first-Tab and StrictMode tests prove. The GOV.UK pattern (`govuk-skip-link` as the first child of `<body>`, before the header) is a convention, not a WCAG requirement. Being inside `banner` loses nothing for screen reader users: landmark navigation to `main` is a second bypass either way, and axe's `region` rule is satisfied. The only real gap is in the written records: story 3.7 criterion 6 says "(before the header)" (low finding 2). The living rule, `docs/ux/EXPERIENCE.md:192`, says "the first focusable element in the document" and still holds.

### Does the test plus break pin it

- The class test pins what the geometry rests on: the same container, `focus:absolute`, equal tap-target heights, and no offset or margin. jsdom has no layout, so it can do no more. Break 10 fails it on two counts (the offset regex and the missing `focus:min-h`). Moving the link back into `RootLayout` would fail the shared-parent assertion. I checked that the regex does not match `focus:min-h-…` or `focus:items-center`.
- Not pinned: a future `focus:self-*` (2px misalignment on the 64px header, leaving a partly covered strip), and the width relation (skip link wider than the wordmark). In a real browser, CI's e2e axe `target-size` at 320px (which caught F1) and linking-queue's click after the keyboard walk (which caught F4) guard these. Not a finding.
- The builder's headless-Chromium smoke (containment, hit test, no shift, pointer click, axe) has not been re-run here. QA's re-run is the authoritative check.

### Earlier findings

| Sev | Finding | Status |
|---|---|---|
| medium (QA F4, round 3) | The focused in-flow skip link shifted the page 60px, and a click made while it had focus was lost | **Closed in code.** The link floats out of flow at the home link's static position. The unit test and break 10 pin it. The e2e confirmation waits on QA's linking-queue re-run. |
| medium (QA F1, round 2) | The focused skip link partly covered the 44px home link (SC 2.5.8) | **Still closed.** It now fully covers the home link (same top, same height, wider) instead of sitting clear of it. The e2e confirmation waits on QA's linking-screens re-run at 320px. |
| medium (round 1) | `routing.test.tsx`: the top-nav absence check could not fail | Closed in round 1. The code is unchanged. |
| low ×3 | `TransactionRow.tsx:65-66`; `customerStatus.test.ts:33-35`; `customerStatus.test.ts:23-25` | Open. The code is unchanged, and they go to `lows.md` at merge. |

### New findings

| Sev | File | Finding | Fix |
|---|---|---|---|
| low | `frontend/src/app/Header.tsx:39`, `frontend/src/app/Navigation.tsx:19` | From 768px, in the linked state, the focused skip link (about 52.6px wider than the home link) reaches about 28.6px past the home link's right edge. Past the 24px `gap-6` and the nav's 4px padding, that covers roughly the first 24px of the 44px "Transactions" link while the skip link has focus. The visible part stays about 100x44, above SC 2.5.8's 24x24, so axe `target-size` passes and no criterion breaks. But the fix list's "no partial obscuring" rationale is not literally true here, and the builder's browser check tested only containment of the home link. These numbers are computed from QA's measured boxes and were not reproduced in a browser. At 320px no other target is near (TopNav is hidden, and Sign out/Sign in sit at the right). | QA confirms the overlap at 1280px, linked. The lows batch either notes it in the comment as accepted under SC 2.5.8 or narrows the overlap. |
| low | `frontend/src/app/routing.test.tsx:655-665` | Story 3.7 criterion 6 (done) says the skip link is first "(before the header)". The test now proves "before the home link", because the link is inside the banner. There is no accessibility gap (see above), but a done criterion's wording no longer matches the code. | The supervisor records it in `decisions.md`: round 3 places the skip link as the banner's first child, which replaces 3.7 criterion 6's parenthetical. |
| low | `frontend/src/app/Header.tsx:33-34` (and the build record note) | The comment says the link sits "at the container's padding edge". Under Flexbox section 4.1 it sits where the sole flex item would, at the content edge inside the padding, which is where the home link starts. The comment is wrong about the mechanism it explains. | Lows batch: "at the container's content edge, where the home link starts", and cite CSS Flexbox Level 1, section 4.1. |

### Pass 1, Pass 2, standards and security on the delta

- Correctness: the fix list's requirements are met in code. The link floats, covers the home link fully, causes no shift, uses theme tokens only (`--tap-target`, `bg-card`, `shadow-overlay`), and has a unit class test and a break. The real-browser check at 320 and 1280px is the builder's smoke and then QA's. It is still the first focusable element and still moves focus to `main` by script. AC7's proof list is updated.
- Simplicity: one prop with a doc comment and one class list, with no new component, state or effect. Passing a callback prop is the plainest way to keep the `main` ref in `RootLayout`. Nothing is speculative.
- Standards: CSS Flexbox Level 1 section 4.1 for the static position; WCAG 2.2 G1, SC 2.4.1, 2.4.7, 2.5.2, 2.5.8 and 3.2.1. Nothing is home-grown.
- Security: the delta is markup, CSS classes, one prop and tests, so there is no security impact.

## Capture (supervisor, 2026-11-08)

### Escapes

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
|---|---|---|---|---|
| The "top navigation has no icon" test queried `role=img`, which an `<svg>` never has, so it could not fail | reviewer | builder (no break targeted that boundary) | missing test | each boundary in a criterion gets a break |
| The 44px home link was partly hidden by the focused skip link at 320px (axe target-size) | QA | pre-build review (overlap in a focus state) | spec gap | postmortem proposal: a pre-build line on tap-target overlap |
| The in-flow skip link shifted the page and lost the next click | QA | reviewer (fix round 2 delta) and builder smoke | input boundary | postmortem proposal: frontend builders run a headless-browser pass with axe and a keyboard walk at 320px; applied through the briefs since |
| `run-breaks.sh` could not run Vitest breaks | verifier | builder (record format) | environment or tooling | breaks are written as runnable shell commands; lows row for a Vitest mode |

### Metrics

| Fix rounds | Full-suite runs | Stages | Notes |
|---|---|---|---|
| 3 (review 1, QA 2; the third authorised by the human) | 4 (one per build and fix round) | pre-build review; builder x4; reviewer x4; verifier x4; QA x3 | merged as `856d5ef` (#24); a postmortem was written at the second fix round |
