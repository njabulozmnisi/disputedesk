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
  indicator), Shapes; EXPERIENCE Component Patterns (Problem notice, Step indicator), State Patterns, Accessibility
  Floor; PRD NFR-CODE-4, NFR-CODE-6.

## Inputs

No new request inputs: this spec is presentation only, and no request body, query, header or route parameter
changes. No new UI input. No package is added: `alert` uses `class-variance-authority`, already a dependency, and
`cn` from `@/lib/utils`.

Tool: the shadcn CLI, run with `npx` and pinned to `shadcn@4.23.0` (the version the pre-build review inspected on
2026-11-08); the builder re-checks that it is the latest stable before running it and records the version in its
report (AGENTS.md, Dependency verification).

## Criteria

1. **Notices are shadcn `Alert`s with a tone icon, and only errors announce** (HANDOFF 3.7; DESIGN Notices, Shapes;
   EXPERIENCE Accessibility Floor)
   **Given** `components/ui/alert.tsx`, added with `npx shadcn@4.23.0 add alert`; if the CLI adds or changes any
   package or writes an import other than `cn` from `@/lib/utils`, the builder discards its output and uses the shadcn
   docs' Manual installation for Alert instead (the component copied, `cn` imported from `@/lib/utils`)
   **When** a notice renders in each tone
   **Then** its colours are the theme's status tokens and it leads with its 16px icon: danger `TriangleAlert`, info
   `Info`, success `CircleCheck`, neutral `Info`, rate limited `Clock` in the danger colours
   (`mockups/link-rate-limited.html`)
   **And** the root carries `role="alert"` for the danger and rate-limited tones only; success, info and neutral
   render with no `role`
   **And** the generated file is edited so: `AlertTitle` has no `line-clamp` (the title wraps); `AlertDescription`
   takes the tone's foreground, not `text-muted-foreground`; each tone is `rounded-md`, has no border, `p-4`, and uses
   the theme's type tokens (`text-body`, `text-label`, `font-strong`), not `text-sm` or `font-medium`; the CLI changes
   no colour value in `tokens.css` or `index.css`
   **And** `alert.test.tsx` renders each tone and asserts its icon (`svg.lucide-<name>`) and its role or the absence
   of one, and renders a long title and asserts no `line-clamp` class. Mechanism: shadcn Alert (you own the code);
   the WAI-ARIA alert role for errors only. Source: <https://ui.shadcn.com/docs/components/alert> (CLI and Manual),
   <https://ui.shadcn.com/docs/cli>; WAI-ARIA APG, Alert pattern (<https://www.w3.org/WAI/ARIA/apg/patterns/alert/>);
   WCAG 2.2 SC 1.4.10 (<https://www.w3.org/WAI/WCAG22/Understanding/reflow>).

2. **The problem notice keeps its reference on its own row** (HANDOFF 3.7; EXPERIENCE Problem notice)
   **Given** `ProblemNotice`
   **When** it renders with a `traceId`
   **Then** it is a danger `Alert` with `role="alert"`: the sentence, the next step, "If this keeps happening,
   contact the bank and quote this reference.", then after a 20% `currentColor` hairline a row with "Reference",
   the `traceId` in mono and the Copy pill, at least 44px tall (`--tap-target`)
   **And** with no `traceId` there is no reference sentence, no hairline and no row; an `action`, when given, renders
   inside the `Alert` after the reference row (or after the next step when there is no row), as today
   **And** Copy still writes the `traceId`, says "Copied" for 2 seconds, and keeps "Copy" when the write fails
   **And** a new `ProblemNotice.test.tsx` proves the row, its absence without a `traceId`, the action's place, and the three
   Copy outcomes. Mechanism: Clipboard API. Source: MDN, `Clipboard.writeText()`
   (<https://developer.mozilla.org/en-US/docs/Web/API/Clipboard/writeText>); WCAG 2.2 SC 2.5.5
   (<https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced>), DisputeDesk's 44px floor.

3. **Error summaries and field errors carry the danger icon** (HANDOFF 3.7; WCAG 2.2 SC 3.3.1)
   **Given** `components/ErrorSummary.tsx` and `FieldError` in `components/ui/field.tsx`
   **When** a form has errors
   **Then** the summary is drawn in the danger `Alert`'s look with `TriangleAlert`, titled "Check the details below",
   with a link to each field, and it takes focus as before
   **And** its root keeps `role="group"`, `aria-labelledby="error-summary-title"` and `tabIndex={-1}`, the inner
   `role="alert"` stays, and the title stays an `h2` (not `AlertTitle`), so the existing
   `getByRole('group', { name: 'Check the details below' })` tests pass unchanged
   **And** each field error leads with a 14px (`size-3.5`) `TriangleAlert`; ids, `aria-describedby` and message text
   are unchanged; a new `field.test.tsx` asserts the icon. Mechanism: GOV.UK error summary; shadcn Field. Source: GOV.UK
   Design System, Error summary (<https://design-system.service.gov.uk/components/error-summary/>);
   <https://ui.shadcn.com/docs/components/field>;
   <https://www.w3.org/WAI/WCAG22/Understanding/error-identification>.

4. **The rate limit is its own tone** (HANDOFF 2, 3.7; EXPERIENCE State Patterns, Rate limited)
   **Given** a 429 on `/link` or `/link/otp` (`DetailsScreen.tsx:103`, `OtpScreen.tsx`)
   **When** the notice shows
   **Then** it is the rate-limited `Alert` (`Clock`, danger colours, `role="alert"`) with the same single sentence
   from `waitCopy` in `retryAfter.ts`, and the problem notice is not used for it; the existing rate-limit tests in
   `DetailsScreen.test.tsx` and `OtpScreen.test.tsx` assert the `lucide-clock` icon. Disabling the form is
   `ui-linking`'s. Mechanism: shadcn Alert. Source: <https://ui.shadcn.com/docs/components/alert>.

5. **One `StateSurface` for full-screen and empty states** (HANDOFF 3.8; DESIGN State surface)
   **Given** the new `components/StateSurface.tsx`
   **When** it renders on signed out (`pages/SignedOutPage.tsx`, `Clock`), not found (`pages/NotFoundPage.tsx` and
   `NotFoundCard` in `DisputeDetailScreen.tsx`, `Search`), locked (`LockedState` in `OtpScreen.tsx:31-52`, `Lock`),
   transactions empty (`TransactionsScreen.tsx`, `List`, no button) and disputes empty (`DisputesScreen.tsx`,
   `Flag`, "Go to transactions")
   **Then** each is one flat surface (`card`, hairline, `rounded-lg`) with a 48px `muted` tile holding a 24px
   `heading` icon, the heading (`h1` full-screen, `h2` empty), the existing sentences, then the one action;
   `ContactHelp` in the locked state stays after the surface, outside it, unchanged
   **And** `StateSurface` takes the caller's heading element rather than rendering its own, so on the detail screen
   the shared `h1` stays one element at one place in the tree in every state (loading, failure, not found, loaded):
   the tile slot and surface classes sit on the same elements in each state and are null when not needed
   **And** copy, heading focus (`usePageHeading`) and every action's target are unchanged; the detail focus tests
   (`DisputeDetailScreen.test.tsx:238`, "moves focus to the heading when Try again is answered 404", and the
   not-found cases at `:1218-1222`) pass unchanged; `StateSurface.test.tsx` proves the heading level and the
   `aria-hidden` tile icon. Mechanism: headings and landmarks. Source: WCAG 2.2 SC 1.3.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/info-and-relationships>), SC 2.4.3
   (<https://www.w3.org/WAI/WCAG22/Understanding/focus-order>).

6. **`StepProgress` is ready for linking and raising** (HANDOFF 3.4; DESIGN Step indicator)
   **Given** the new `components/StepProgress.tsx` with a label and `step: 1 | 2` (every use in HANDOFF 3.4 has two
   steps, so the count is fixed, not a prop)
   **When** it renders step 1 or step 2
   **Then** the label is 14/700 `muted-foreground` text and the bar is two 4px pill segments, `primary` up to and
   including the current step and `border` after it, with `aria-hidden="true"` on the bar
   **And** `StepProgress.test.tsx` proves step 1 (one `primary`, one `border`), step 2 (two `primary`) and the hidden
   bar; no screen uses it yet (areas 4 and 5 do). Mechanism: text label with a decorative bar. Source: WCAG 2.2
   SC 1.1.1 (<https://www.w3.org/WAI/WCAG22/Understanding/non-text-content>).

## Boundaries

- Presentation only: no change to API calls, the generated client, routes, auth, session, idempotency, retry, query
  keys or telemetry; the live region is unchanged, and `role="alert"` stays only where it is today (problem notices,
  the rate-limit notice, the error summary's inner element).
- Theme tokens only: no raw hex or px value in a component. Icons: Lucide, 16px unless HANDOFF names another size,
  2px stroke, `currentColor`, `aria-hidden="true"`.
- Where the mockups drift, DESIGN and EXPERIENCE win (HANDOFF, Sources of truth): notice icons are 16px, not the
  18px of `app.css:214`; the rate-limit text is `waitCopy`'s single sentence, not the title and body split of
  `mockups/link-rate-limited.html`. QA does not report these as mismatches.
- `package.json` and `package-lock.json` are unchanged.
- Touches (under `frontend/src/`): `components/ui/alert.tsx` and `alert.test.tsx` (new),
  `components/ProblemNotice.tsx` and `ProblemNotice.test.tsx` (new), `components/ErrorSummary.tsx`,
  `components/ui/field.tsx` and `field.test.tsx` (new),
  `components/StateSurface.tsx` and its test (new), `components/StepProgress.tsx` and its test (new),
  `pages/SignedOutPage.tsx`, `pages/NotFoundPage.tsx`, `OtpScreen.tsx` (locked state and rate-limit notice only),
  `DetailsScreen.tsx` (rate-limit notice only), `NotFoundCard` and the shared heading's surroundings in
  `DisputeDetailScreen.tsx`, the empty states in `TransactionsScreen.tsx` and `DisputesScreen.tsx`, and their tests.

## Tests and commits

- QA updates the e2e specs that reach these states or assert their copy: `linking-screens.spec.ts`,
  `linking-rate-limits.spec.ts`, `spa-session-end.spec.ts`, `dispute-detail.spec.ts`, `spa-transactions.spec.ts`,
  `spa-disputes-history.spec.ts`, `sign-in-and-out-audit.spec.ts`, `spa-dispute-status-badges.spec.ts`,
  `dispute-raise.spec.ts`, `journeys/uj-1.spec.ts`, `journeys/uj-5a.spec.ts`, and any other spec that a grep under
  `e2e/tests` for "Too many attempts", "Check the details below", "Copy", "Reference" or a state heading finds.
- The pull request holds two commits: the presentation change, then `test(ui-notices-states): update assertions for
  the UI refresh` with every unit-test and e2e assertion update. The squash merge joins them on main.

## Verify

- Builder, in `frontend/`: `npm run typecheck`, `lint`, `format`, `test` and `build` pass; the glyph grep
  (`grep -rnE '➚|◔|▲|✓|✕|↩|ⓘ|⚠|☰|⚑' frontend/src`) prints nothing; the diff has no raw hex or px in a component;
  `package.json` and `package-lock.json` are not in the diff; `alert.tsx` imports `cn` from `@/lib/utils`.
- QA, holding the `stack` lock: `e2e/run-regression.sh` passes with axe on every spec that runs it; with the
  Playwright MCP each changed screen matches its mockup at 360px and 1280px in light and dark; no horizontal scroll
  at 320px or 200% zoom; every interactive element, the Copy pill included, is at least 44px tall; keyboard order is
  unchanged. jsdom cannot prove colours, the 44px height or the radius, so these are QA's visual checks.

## Sofia's refine

Sofia, 2026-11-08, on `docs/verification/ui-notices-states-prebuild-review.md` (no `[decision]` point; every
`[story]` point fixed):

- P1 medium, alert role: criterion 1 puts `role="alert"` on danger and rate limited only, with a test per tone.
- P2 medium, error summary: criterion 3 keeps the group, its name, `tabIndex={-1}`, the inner alert and the `h2`.
- P3 medium, registry defaults: criterion 1 names the edits (no line clamp, tone colour, `rounded-md`, no border,
  `p-4`, type tokens) and a long-title test.
- P4 medium, CLI version and `cn`: pinned `shadcn@4.23.0`, the Manual install as the fallback with `cn` from
  `@/lib/utils`, and a Verify line that the package files are unchanged.
- P5 medium, shared `h1`: criterion 5 keeps the detail screen's one `h1` (the caller passes its heading) and names
  the detail focus tests as the proof.
- P6 low, `ContactHelp`: stays after the surface, outside it.
- P7 low, `action` and no `traceId`: criterion 2 states both.
- P8 low, mockup drift: a Boundaries line (16px icons, `waitCopy`'s single sentence).
- P9 low, step count: fixed at two, `step: 1 | 2`.
- P10 low, grep terms and tests: Tests and commits names the specs and grep terms; each criterion names its test;
  QA's visual checks are named.
