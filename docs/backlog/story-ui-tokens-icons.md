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
   **Then** `:root` declares `--amount-col: 9rem` and `--action-col: 14rem`, `@theme inline` maps them as
   `--spacing-amount-col` and `--spacing-action-col`, and no colour value is changed or added
   **And** `theme.test.ts` asserts both declarations and both mappings.
   Mechanism: CSS custom properties mapped to Tailwind utilities. Source: Tailwind v4, Theme variables
   (<https://tailwindcss.com/docs/theme>).

2. **`lucide-react` is added, verified and pinned** (NFR-CODE-5)
   **Given** `frontend/package.json` and `package-lock.json`
   **When** the diff is read
   **Then** `lucide-react` appears once in `dependencies` with an exact version (no `^` or `~`) and no other package
   is added. Mechanism: npm exact pin. Source: AGENTS.md, Dependency verification; lucide-react
   (<https://lucide.dev/guide/packages/lucide-react>).

3. **Each status maps to its Lucide icon** (HANDOFF 2 table; FR-12, FR-16, FR-17)
   **Given** `describeCustomerStatus` in `features/disputes/customerStatus.ts`
   **When** it describes each status and outcome
   **Then** `icon` is a Lucide component, not a string: Submitted `Send`, Under review `Search`, Under review
   (prioritised) `SearchPriority`, Resolved `Flag`, Resolved · Upheld `Check`, Resolved · Rejected `X`, Withdrawn
   `Undo2`; labels, tones and groups are unchanged
   **And** `SearchPriority` is built with `createLucideIcon` from the paths in `mockups/dispute-prioritised.html`
   (`circle cx=11 cy=11 r=7`, `m20 20-3.5-3.5`, `M11 14V8M8.5 10.5 11 8l2.5 2.5`); `customerStatus.test.ts`
   asserts the component per row. Mechanism: Lucide React components and custom icons. Source:
   <https://lucide.dev/guide/packages/lucide-react>.

4. **Icons are decorative and drawn the same everywhere** (HANDOFF 2; WCAG 2.2 SC 1.1.1, SC 1.4.1)
   **Given** `DisputeStatusBadge.tsx`, `app/Navigation.tsx` (Transactions `List`, Disputes `Flag`),
   `TransactionRow.tsx` (the not-disputable hint, `Info`) and `pages/StartPage.tsx` (the sample badge, `Search`)
   **When** each renders
   **Then** each icon is an `svg` with `aria-hidden="true"`, stroke `currentColor`, stroke width 2, and 16px
   (`size-4`; the hint's `Info` is 14px, HANDOFF 3.2); the badge, tab and hint text is unchanged, so every
   accessible name is unchanged
   **And** `TransactionsScreen.test.tsx` (lines 398, 409, 724-725 assert glyphs today) asserts the `svg` instead.
   Mechanism: `aria-hidden` on decorative images; text carries the meaning. Source: WCAG 2.2 Understanding SC 1.1.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/non-text-content>) and SC 1.4.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/use-of-color>).

5. **The wordmark dot follows the name** (HANDOFF 3.1; DESIGN Wordmark; `mockups/app.css` `.brand__dot`)
   **Given** `app/Header.tsx`
   **When** any screen renders
   **Then** the 24px leading dot is gone, and an 8px `primary` dot with `aria-hidden="true"` sits after
   "DisputeDesk", baseline-aligned, 2px gap, in theme spacing (`size-2`, `--space-2`)
   **And** the home link's accessible name stays exactly "DisputeDesk". Mechanism: inline flex, baseline alignment.
   Source: Tailwind v4, align-items (<https://tailwindcss.com/docs/align-items>); WCAG 2.2 SC 2.5.3 Label in Name
   (<https://www.w3.org/WAI/WCAG22/Understanding/label-in-name>).

6. **No text glyph is an icon** (HANDOFF 5)
   **Given** the built tree
   **When** `grep -rn '[➚◔▲✓✕↩ⓘ⚠☰⚑]' frontend/src` runs
   **Then** it prints nothing (source and unit tests). Mechanism: grep over the source. Source: HANDOFF 5.

## Boundaries

- Presentation only: no change to API calls, the generated client, routes, auth, session, idempotency, retry, query
  keys or telemetry.
- Theme tokens only: no raw hex or px value in a component (`theme.test.ts` fails on hex; the builder and reviewer
  grep the diff for px). Icons: Lucide, 16px unless HANDOFF names another size, 2px stroke, `currentColor`,
  `aria-hidden="true"`.
- The wordmark PNGs and the identity provider's label policy are not changed here: they are regenerated after area 6
  merges (HANDOFF 3.1, the plan's reminders).
- Touches: `theme/tokens.css`, `theme/theme.test.ts`, `package.json`, `package-lock.json`,
  `features/disputes/customerStatus.ts` and its test, `DisputeStatusBadge.tsx`, `app/Navigation.tsx`,
  `app/Header.tsx`, `features/transactions/TransactionRow.tsx`, `TransactionsScreen.test.tsx`, `pages/StartPage.tsx`
  (all under `frontend/src/` except the package files).

## Tests and commits

- QA updates `e2e/tests/spa-disputes-history.spec.ts` (lines 282-283 filter on the ✓ and ✕ glyphs) and any other spec
  its grep for the glyphs finds.
- The pull request holds two commits: the presentation change, then `test(ui-tokens-icons): update assertions for the
  UI refresh` with every unit-test and e2e assertion update. The squash merge joins them on main.

## Verify

- Builder, in `frontend/`: `npm run typecheck`, `lint`, `format`, `test` and `build` pass; the glyph grep prints
  nothing; the diff has no raw hex or px in a component.
- QA, holding the `stack` lock: `e2e/run-regression.sh` passes with axe on every spec that runs it; with the
  Playwright MCP each changed screen matches its mockup at 360px and 1280px in light and dark; no horizontal scroll
  at 320px or 200% zoom; every interactive element is at least 44px tall; keyboard order is unchanged.
