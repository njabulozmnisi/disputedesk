---
id: 4
type: story
title: "Notices lead with a tone icon, and full-screen and empty states share one surface"
sources: [docs/ux/HANDOFF.md, docs/ux/DESIGN.md, docs/ux/EXPERIENCE.md, docs/ux/mockups/index.html, docs/ux/mockups/app.css, docs/conventions/frontend.md, docs/prd/prd.md]
companions: [docs/verification/ui-notices-states-prebuild-review.md, docs/verification/decisions.md]
after: [story-ui-tokens-icons]
refined: true
hitl: false
risk: medium
decisions: settled
---

# Notices lead with a tone icon, and full-screen and empty states share one surface

## Description

The second area of the UI refresh (HANDOFF section 6, step 2). Notices today are hand-drawn `div`s:
`components/ProblemNotice.tsx` has a Copy button about 28px tall and no tone icon, and the rate limit shows as a
plain danger notice. There is no shadcn `Alert` in `components/ui/`. Full-screen and empty states (signed out, not
found twice, locked, both empty lists) are each laid out by hand. This spec builds notices on shadcn's `Alert`
(decided 2026-11-08), adds `StateSurface` for those states, and builds `StepProgress`, which areas 4 and 5 then use,
so they can build in parallel.

- Tier: Feature. Owner: frontend. Stacking: on `feature/ui-tokens-icons`; `ui-lists`, `ui-linking`,
  `ui-raise-review` and `ui-detail-start` stack on it.
- Presentation only: no API, route, auth, session, idempotency or retry change.
- Covers: HANDOFF 3.4 (component only), 3.7, 3.8; DESIGN Components (Notices, Problem notice, State surface, Step
  indicator); EXPERIENCE Component Patterns (Problem notice, Step indicator), State Patterns, Accessibility Floor;
  PRD NFR-CODE-4, NFR-CODE-6.

## Inputs

No new request inputs: this spec is presentation only, and no request body, query, header or route parameter
changes. No new UI input. No package is added: `alert` uses `class-variance-authority`, already a dependency.

## Criteria

1. **Notices are shadcn `Alert`s with a tone icon** (HANDOFF 3.7; DESIGN Notices)
   **Given** `components/ui/alert.tsx`, added with the shadcn CLI (`npx shadcn@<exact version> add alert`)
   **When** a notice renders in each tone
   **Then** its colours are the theme's status tokens and it leads with its icon: danger `TriangleAlert`, info `Info`,
   success `CircleCheck`, neutral `Info`, rate limited `Clock` in the danger colours (`mockups/link-rate-limited.html`)
   **And** the CLI changes no colour value in `tokens.css` or `index.css`, and adds nothing to `package.json`.
   Mechanism: shadcn Alert. Source: <https://ui.shadcn.com/docs/components/alert>, <https://ui.shadcn.com/docs/cli>.

2. **The problem notice keeps its reference on its own row** (HANDOFF 3.7; EXPERIENCE Problem notice)
   **Given** `ProblemNotice` with a `traceId`
   **When** it renders
   **Then** it is a danger `Alert` with `role="alert"`: the sentence, the next step, "If this keeps happening,
   contact the bank and quote this reference.", then after a 20% `currentColor` hairline a row with "Reference",
   the `traceId` in mono and the Copy pill, at least 44px tall (`--tap-target`)
   **And** Copy still writes the `traceId`, says "Copied" for 2 seconds, and keeps "Copy" when the write fails.
   Mechanism: Clipboard API. Source: MDN, `Clipboard.writeText()`
   (<https://developer.mozilla.org/en-US/docs/Web/API/Clipboard/writeText>); WCAG 2.2 SC 2.5.5
   (<https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced>), DisputeDesk's 44px floor.

3. **Error summaries and field errors carry the danger icon** (HANDOFF 3.7; WCAG 2.2 SC 3.3.1)
   **Given** `components/ErrorSummary.tsx` and `FieldError` in `components/ui/field.tsx`
   **When** a form has errors
   **Then** the summary is a danger `Alert` titled "Check the details below" with a link to each field, and it takes
   focus as before; each field error leads with a 14px `TriangleAlert`; ids, `aria-describedby` and message text are
   unchanged. Mechanism: shadcn Alert and Field. Source: <https://ui.shadcn.com/docs/components/field>;
   <https://www.w3.org/WAI/WCAG22/Understanding/error-identification>.

4. **The rate limit is its own tone** (HANDOFF 2, 3.7; EXPERIENCE State Patterns, Rate limited)
   **Given** a 429 on `/link` or `/link/otp` (`DetailsScreen.tsx:103`, `OtpScreen.tsx`)
   **When** the notice shows
   **Then** it is the rate-limited `Alert` (`Clock`, danger colours) with the same wait text from `retryAfter.ts`,
   and the problem notice is not used for it. Disabling the form is `ui-linking`'s.
   Mechanism: shadcn Alert. Source: <https://ui.shadcn.com/docs/components/alert>.

5. **One `StateSurface` for full-screen and empty states** (HANDOFF 3.8; DESIGN State surface)
   **Given** the new `components/StateSurface.tsx`
   **When** it renders on signed out (`pages/SignedOutPage.tsx`, `Clock`), not found (`pages/NotFoundPage.tsx` and
   `NotFoundCard` in `DisputeDetailScreen.tsx`, `Search`), locked (`LockedState` in `OtpScreen.tsx:31-52`, `Lock`),
   transactions empty (`TransactionsScreen.tsx`, `List`, no button) and disputes empty (`DisputesScreen.tsx`,
   `Flag`, "Go to transactions")
   **Then** each is one flat surface (`card`, hairline, `rounded-lg`) with a 48px `muted` tile holding a 24px
   `heading` icon, the heading (`h1` full-screen, `h2` empty), the existing sentences, then the one action
   **And** copy, heading focus (`usePageHeading`) and every action's target are unchanged.
   Mechanism: headings and landmarks. Source: WCAG 2.2 SC 1.3.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/info-and-relationships>).

6. **`StepProgress` is ready for linking and raising** (HANDOFF 3.4; DESIGN Step indicator)
   **Given** the new `components/StepProgress.tsx` with a label, the current step and the step count (2)
   **When** it renders step 1 or step 2
   **Then** the label is 14/700 `muted-foreground` text and the bar is two 4px pill segments, `primary` up to and
   including the current step and `border` after it, with `aria-hidden="true"` on the bar
   **And** a unit test proves both steps and the hidden bar; no screen uses it yet (areas 4 and 5 do).
   Mechanism: text label with a decorative bar. Source: WCAG 2.2 SC 1.1.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/non-text-content>).

## Boundaries

- Presentation only: no change to API calls, the generated client, routes, auth, session, idempotency, retry, query
  keys or telemetry; `role="alert"` and the live region are unchanged.
- Theme tokens only: no raw hex or px value in a component. Icons: Lucide, 16px unless HANDOFF names another size,
  2px stroke, `currentColor`, `aria-hidden="true"`.
- Touches (under `frontend/src/`): `components/ui/alert.tsx` (new), `components/ProblemNotice.tsx`,
  `components/ErrorSummary.tsx`, `components/ui/field.tsx`, `components/StateSurface.tsx` (new),
  `components/StepProgress.tsx` (new), `pages/SignedOutPage.tsx`, `pages/NotFoundPage.tsx`, `OtpScreen.tsx` (locked
  state and rate-limit notice only), `DetailsScreen.tsx` (rate-limit notice only), `NotFoundCard` in
  `DisputeDetailScreen.tsx`, the empty states in `TransactionsScreen.tsx` and `DisputesScreen.tsx`, and their tests.

## Tests and commits

- QA updates the e2e specs that reach these states or assert their copy: `linking-screens.spec.ts`,
  `linking-rate-limits.spec.ts`, `spa-session-end.spec.ts`, `dispute-detail.spec.ts`, `spa-transactions.spec.ts`,
  `spa-disputes-history.spec.ts`, and any other its grep for "quote this reference" finds.
- The pull request holds two commits: the presentation change, then `test(ui-notices-states): update assertions for
  the UI refresh` with every unit-test and e2e assertion update. The squash merge joins them on main.

## Verify

- Builder, in `frontend/`: `npm run typecheck`, `lint`, `format`, `test` and `build` pass; the glyph grep
  (`grep -rn '[➚◔▲✓✕↩ⓘ⚠☰⚑]' frontend/src`) prints nothing; the diff has no raw hex or px in a component.
- QA, holding the `stack` lock: `e2e/run-regression.sh` passes with axe on every spec that runs it; with the
  Playwright MCP each changed screen matches its mockup at 360px and 1280px in light and dark; no horizontal scroll
  at 320px or 200% zoom; every interactive element, the Copy pill included, is at least 44px tall; keyboard order is
  unchanged.
