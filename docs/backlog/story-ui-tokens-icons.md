---
id: 3
type: story
title: "Icons are Lucide components and the theme file holds the list columns"
sources: [docs/ux/HANDOFF.md, docs/ux/DESIGN.md, docs/ux/EXPERIENCE.md, docs/ux/mockups/index.html, docs/ux/mockups/app.css, docs/ux/mockups/tokens.css, docs/conventions/frontend.md, docs/prd/prd.md]
companions: [docs/verification/ui-tokens-icons-prebuild-review.md, docs/verification/decisions.md]
after: []
refined: true
hitl: false
risk: low
decisions: settled
---

# Icons are Lucide components and the theme file holds the list columns

## Description

The first area of the UI refresh (HANDOFF section 6, step 1). Every customer screen shows its status and navigation
icons as text glyphs (➚ ◔ ▲ ✓ ✕ ↩ ⓘ ☰ ⚑) in `customerStatus.ts`, `Navigation.tsx`, `TransactionRow.tsx` and
`StartPage.tsx`. The glyphs fall outside the Nunito Sans latin subset, so each device draws them differently. This
spec replaces them with `lucide-react` icons, adds the two list-column tokens the theme file lacks, and moves the
wordmark dot after the name. Every later area builds on it.

- Tier: Feature. Owner: frontend. Stacking: on main; `ui-notices-states` stacks on it.
- Presentation only: no API, route, auth, session, idempotency or retry change.
- Covers: HANDOFF 1, 2, 3.1 (the SPA header only); DESIGN Components (Status badge, Wordmark), Theme file; PRD
  NFR-CODE-4, NFR-CODE-5, NFR-CODE-6.

## Inputs

No new request inputs: this spec is presentation only, and no request body, query, header or route parameter
changes. No new UI input.

Package: `lucide-react`, exactly pinned. At writing (2026-11-08) the latest stable is `1.70.0` (repository
<https://github.com/lucide-icons/lucide>); the builder re-checks publisher, weekly downloads, repository and latest
stable version before adding it (AGENTS.md, Dependency verification; NFR-CODE-5) and records them in its report.

## Criteria

1. **The theme file holds the list columns** (HANDOFF 1; DESIGN Theme file; NFR-CODE-4)
   **Given** `frontend/src/theme/tokens.css`
   **When** it is read
   **Then** `:root` declares `--amount-col: 9rem` and `--action-col: 14rem`; no `--spacing-*` mapping is added for
   them (`ui-lists` reads them as `var(--amount-col)` in an arbitrary grid, the way the repo reads `--content-max`),
   and no colour value is changed or added
   **And** `theme.test.ts` asserts both declarations. Mechanism: CSS custom properties in the theme file. Source:
   Tailwind v4, Theme variables (<https://tailwindcss.com/docs/theme>).

2. **`lucide-react` is added, verified and pinned** (NFR-CODE-5)
   **Given** `frontend/package.json` and `package-lock.json`
   **When** the diff is read
   **Then** `lucide-react` appears once in `dependencies` with an exact version (no `^` or `~`) and no other package
   is added. Mechanism: npm exact pin. Source: npm, `npm install --save-exact`
   (<https://docs.npmjs.com/cli/commands/npm-install>); AGENTS.md, Dependency verification.

3. **Each status maps to its Lucide icon** (HANDOFF 2 table; FR-12, FR-16, FR-17)
   **Given** `describeCustomerStatus` in `features/disputes/customerStatus.ts`
   **When** it describes each status and outcome
   **Then** `icon` is a Lucide component, not a string: Submitted `Send`, Under review `Search`, Under review
   (prioritised) `SearchPriority`, Resolved `Flag`, Resolved · Upheld `Check`, Resolved · Rejected `X`, Withdrawn
   `Undo2`; labels, tones and groups are unchanged
   **And** `SearchPriority` is defined at module level in `customerStatus.ts` with
   `createLucideIcon('SearchPriority', …)` from Lucide's own `Search` nodes (`circle cx=11 cy=11 r=8`,
   `m21 21-4.34-4.34`) plus the arrow `M11 14V8M8.5 10.5 11 8l2.5 2.5`, each node with a unique `key`, so both Under
   review badges show the same lens; `customerStatus.test.ts` asserts the component per row. Mechanism: Lucide React
   components and one custom icon. Source: Lucide, TypeScript, custom icons
   (<https://lucide.dev/guide/react/advanced/typescript>), and the `createLucideIcon` declaration exported and typed
   in `lucide-react` 1.70.0.

4. **Icons are decorative and drawn the same everywhere** (HANDOFF 2; WCAG 2.2 SC 1.1.1, SC 1.4.1)
   **Given** `DisputeStatusBadge.tsx`, the phone `TabBar` in `app/Navigation.tsx` (Transactions `List`, Disputes
   `Flag`; `TopNav` keeps text only, as DESIGN Segmented nav and `mockups/transactions-after.html` draw it),
   `TransactionRow.tsx` (the not-disputable hint, `Info`) and `pages/StartPage.tsx` (the sample badge, `Search`)
   **When** each renders
   **Then** each icon is an `svg` with `aria-hidden="true"`, `stroke="currentColor"`, Lucide's default
   `stroke-width="2"` (no `absoluteStrokeWidth`, no `nonScalingStroke`) and the `size-4` class (the hint's `Info`
   takes `size-3.5`, 14px, HANDOFF 3.2); the badge, tab and hint text is unchanged, so every accessible name is
   unchanged
   **And** one unit test per surface finds `svg.lucide-<name>` and asserts those four attributes, reading the class
   and not `width`/`height`, which jsdom leaves at 24: the badge (one status is enough, criterion 3 proves the
   mapping; the line 409 test below carries it), the tab bar (`lucide-list`, `lucide-flag`), the hint (`lucide-info`), and a new
   `pages/StartPage.test.tsx` for the sample badge (`lucide-search`)
   **And** `TransactionsScreen.test.tsx` changes exactly so: line 398 drops the `ⓘ` line or asserts there is no
   `svg.lucide-info` (lines 395-397 already prove there is no hint text); line 409 asserts that
   `link.querySelector('svg.lucide-search-priority')` has the four attributes above; lines 724-725 expect
   `'a:Transactions'` and `'a:Disputes'`. Mechanism: `aria-hidden` on decorative images; text carries the meaning.
   Source: Lucide, Accessibility (icons are `aria-hidden` by default,
   <https://lucide.dev/guide/react/advanced/accessibility>); WCAG 2.2 Understanding SC 1.1.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/non-text-content>) and SC 1.4.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/use-of-color>).

5. **The wordmark dot follows the name, and the header's targets are 44px** (HANDOFF 3.1, 5; DESIGN Wordmark, Layout)
   **Given** `app/Header.tsx` and `TopNav` in `app/Navigation.tsx`
   **When** any screen renders
   **Then** the 24px leading dot is gone; the home link is `items-center` with `min-h-(--tap-target)` and holds an
   inner `inline-flex items-baseline` span with "DisputeDesk" and then the dot: `size-2 rounded-full bg-primary`,
   `ml-0.5` (0.125rem, 2px: DESIGN Wordmark's one exception to the 4-based spacing subset), `aria-hidden="true"`
   **And** the `TopNav` links take `min-h-(--tap-target)` in place of `min-h-10`
   **And** the home link's accessible name stays exactly "DisputeDesk" (already proved by `routing.test.tsx:407` and
   `sessionEnd.test.tsx:202`), and a routing or header test asserts that the link's last element is the
   `aria-hidden` `size-2` dot, after the text. Mechanism: inline flex, baseline alignment, the 44px target token.
   Source: Tailwind v4, align-items (<https://tailwindcss.com/docs/align-items>); WCAG 2.2 SC 2.5.3 Label in Name
   (<https://www.w3.org/WAI/WCAG22/Understanding/label-in-name>) and SC 2.5.5
   (<https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced>).

6. **No text glyph is an icon** (HANDOFF 5)
   **Given** the app source (`appSourceFiles`, `.ts` and `.tsx`)
   **When** `theme.test.ts` runs its new case "has no text-glyph icon"
   **Then** no file matches the pattern, which is written with Unicode escapes so that the test file holds no glyph
   itself: `/➚|◔|▲|✓|✕|↩|ⓘ|⚠|☰|⚑/u` (➚ ◔ ▲ ✓ ✕ ↩ ⓘ ⚠ ☰ ⚑)
   **And** the builder's grep over `frontend/src`, unit tests included, prints nothing:
   `grep -rnE '➚|◔|▲|✓|✕|↩|ⓘ|⚠|☰|⚑' frontend/src` (an alternation, not a bracket expression, which a C locale
   matches byte by byte). Mechanism: a source scan in the existing theme test. Source: HANDOFF 5.

## Boundaries

- Presentation only: no change to API calls, the generated client, routes, auth, session, idempotency, retry, query
  keys or telemetry.
- Theme tokens only: no raw hex or px value in a component (`theme.test.ts` fails on hex; the builder and reviewer
  grep the diff for px). Icons: Lucide, 16px unless HANDOFF names another size, Lucide's default `stroke-width` 2,
  `currentColor`, `aria-hidden="true"`.
- The wordmark PNGs and the identity provider's label policy are not changed here: `ui-detail-start` criterion 9
  regenerates the PNGs and re-provisions the hosted pages (HANDOFF 3.1).
- Touches: `theme/tokens.css`, `theme/theme.test.ts`, `package.json`, `package-lock.json`,
  `features/disputes/customerStatus.ts` and its test, `DisputeStatusBadge.tsx`, `app/Navigation.tsx`,
  `app/Header.tsx`, `app/routing.test.tsx`, `features/transactions/TransactionRow.tsx`,
  `TransactionsScreen.test.tsx`, `pages/StartPage.tsx`, `pages/StartPage.test.tsx` (new) (all under
  `frontend/src/` except the package files).

## Tests and commits

- QA updates `e2e/tests/spa-disputes-history.spec.ts` (lines 282-283 filter on the ✓ and ✕ glyphs),
  `e2e/tests/spa-transactions.spec.ts:331` (expects `'☰ Transactions'` and `'⚑ Disputes'` among the focused names;
  they become `'Transactions'` and `'Disputes'`) and any other spec the glyph grep finds.
- The pull request holds two commits: the presentation change, then `test(ui-tokens-icons): update assertions for the
  UI refresh` with every unit-test and e2e assertion update. The squash merge joins them on main.

## Verify

- Builder, in `frontend/`: `npm run typecheck`, `lint`, `format`, `test` and `build` pass; the glyph grep prints
  nothing; the diff has no raw hex or px in a component.
- QA, holding the `stack` lock: `e2e/run-regression.sh` passes with axe on every spec that runs it; with the
  Playwright MCP each changed screen matches its mockup at 360px and 1280px in light and dark; no horizontal scroll
  at 320px or 200% zoom; every interactive element is at least 44px tall, the header's home link and `TopNav` links
  included; keyboard order is unchanged.
- QA's mockup comparison covers icon choice, size and placement, not path geometry: the mockup icons are hand-drawn
  stand-ins whose paths differ from Lucide's. The badge keeps DESIGN `status-badge`'s padding (`2px 10px`, `gap-1`),
  not the mockup's `3px 10px 3px 8px`. Lucide's `stroke-width` 2 is in its 24-unit viewBox, about 1.33px at 16px, as
  the mockups draw it.

## Sofia's refine

Sofia, 2026-11-08, on `docs/verification/ui-tokens-icons-prebuild-review.md` (every point `[story]`, all fixed):

- medium, tests per surface: criterion 4 names one test per surface asserting `aria-hidden`, `stroke`,
  `stroke-width` and the size class, with a new `StartPage.test.tsx`; criterion 5 adds a dot-after-name test.
- medium, TransactionsScreen lines: criterion 4 spells out the change at lines 398, 409 and 724-725.
- medium, units: criterion 5 names `size-2`, `ml-0.5` and `items-baseline`, and the 2px exception.
- medium, 44px header: criterion 5 adds `min-h-(--tap-target)` to the home link and the `TopNav` links.
- medium, glyph check: criterion 6 is a `theme.test.ts` case (Unicode escapes, so the test holds no glyph) plus the
  `grep -E` alternation.
- medium, PNGs and label policy: moved to `ui-detail-start` criterion 9; Boundaries points there.
- low, spacing mappings: dropped; only the two `:root` declarations stay.
- low, `SearchPriority`: built from Lucide's `Search` nodes plus the arrow, keyed, at module level in
  `customerStatus.ts`.
- low, sources: the accessibility page, the TypeScript page plus `createLucideIcon`, and npm `--save-exact`.
- low, `TabBar` only: stated in criterion 4.
- low, `spa-transactions.spec.ts:331`: named in Tests and commits.
- low, mockup comparison: a Verify line tells QA what to compare.
