---
id: 7
type: story
title: "Raising and reviewing a dispute show the step and a quiet transaction block"
sources: [docs/ux/HANDOFF.md, docs/ux/DESIGN.md, docs/ux/EXPERIENCE.md, docs/ux/mockups/dispute-new.html, docs/ux/mockups/dispute-new-wrong-amount.html, docs/ux/mockups/dispute-review.html, docs/ux/mockups/app.css, docs/conventions/frontend.md, docs/prd/prd.md]
companions: [docs/verification/ui-raise-review-prebuild-review.md, docs/verification/decisions.md]
after: [story-ui-notices-states]
refined: true
hitl: false
risk: low
decisions: settled
---

# Raising and reviewing a dispute show the step and a quiet transaction block

## Description

The fifth area of the UI refresh (HANDOFF section 6, step 5). The raise form (`features/disputes/raise/RaiseScreen.tsx`)
and review (`ReviewScreen.tsx`) show no step; `TransactionSummary.tsx` draws the transaction as a row with a hairline;
the wrong-amount difference is a plain field description; review's read-back `dl` has no label column, "Change" is a
small link, and the only-once sentence is plain text. This spec draws both screens to the mockups.

- Tier: Feature. Owner: frontend. Stacking: on `feature/ui-notices-states`; builds beside `ui-lists`, `ui-linking`
  and `ui-detail-start` (no shared file).
- Presentation only: no API, route, auth, session, idempotency or retry change; the draft, its `Idempotency-Key` and
  Back behaviour are unchanged.
- Covers: HANDOFF 3.4, 3.5, 3.6; DESIGN Components (Step indicator, Form surface, Amount summary); EXPERIENCE
  Component Patterns (Step indicator, Expected amount, Review card), Interaction Primitives (Idempotent submit, Back
  and forward); PRD FR-13, FR-14, FR-15, NFR-CODE-6.

## Inputs

No new request inputs: this spec is presentation only, and no request body, query, header or route parameter
changes. UI inputs are unchanged: the reason (one of six), the note (at most 1,000 characters after trimming) and,
for Wrong amount, the expected amount (at least R0.00, below the transaction amount, at most 2 decimals), all as
`raiseSchema.ts` bounds them today.

## Criteria

1. **Both screens show their step** (HANDOFF 3.4; EXPERIENCE Step indicator)
   **Given** the raise form and review
   **When** each renders
   **Then** `StepProgress` shows "Step 1 of 2: reason and note" and "Step 2 of 2: review and submit"; the 409 and 422
   result screens (`SubmitFailure.tsx`) show none. Mechanism: `StepProgress` (area 2). Source: WCAG 2.2 SC 1.1.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/non-text-content>).

2. **The transaction is a quiet block** (HANDOFF 3.5; DESIGN Form surface)
   **Given** `TransactionSummary.tsx` at the top of the raise form
   **When** it renders
   **Then** it is a block with `background` fill and `rounded-md`: merchant, date and card ending, the amount on the
   right; no hairline row. Its text and the amount's accessible label are unchanged. Mechanism: theme tokens.
   Source: Tailwind v4, Theme variables (<https://tailwindcss.com/docs/theme>).

3. **The wrong-amount difference is drawn** (HANDOFF 3.5; EXPERIENCE Expected amount; FR-14)
   **Given** Wrong amount with a valid expected amount
   **When** the difference shows
   **Then** "You're disputing R349.00" is 14/700 in `heading`, still the field description tied to the input by
   `aria-describedby` (`expectedAmount-disputing`), and it updates live as today. Mechanism: shadcn Field
   description. Source: <https://ui.shadcn.com/docs/components/field>; WCAG 2.2 SC 1.3.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/info-and-relationships>).

4. **The raise buttons sit below a hairline** (HANDOFF 3.5; DESIGN Form surface)
   **Given** the raise form
   **When** it renders
   **Then** a `border` hairline separates the fields from the buttons, and the button order, labels and focus order
   are unchanged. Mechanism: a top border in the theme's `border` colour. Source: Tailwind v4, border-width
   (<https://tailwindcss.com/docs/border-width>).

5. **The read-back has a label column** (HANDOFF 3.6; DESIGN Amount summary)
   **Given** review with each reason
   **When** the `dl` renders
   **Then** from 768px each `dt` sits in a 9rem label column beside its `dd`, with hairlines between rows; below 768px
   it follows `mockups/dispute-review.html` at 360px; the `dl`, `dt` and `dd` semantics stay. Mechanism: CSS grid.
   Source: Tailwind v4, grid-template-columns (<https://tailwindcss.com/docs/grid-template-columns>).

6. **"Change" is a named 44px control** (HANDOFF 3.6; EXPERIENCE Review card)
   **Given** review
   **When** it renders
   **Then** "Change" sits top right beside the hero amount, is at least 44px tall (`--tap-target`), has the accessible
   name "Change your dispute", and still returns to the raise form with the values kept (a changed draft gets a new
   key, as today). Mechanism: visible label inside the accessible name. Source: WCAG 2.2 SC 2.5.3
   (<https://www.w3.org/WAI/WCAG22/Understanding/label-in-name>), SC 2.5.5
   (<https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced>).

7. **The only-once sentence is a notice** (HANDOFF 3.6)
   **Given** review
   **When** it renders
   **Then** "You can dispute a transaction only once. You can withdraw the dispute while it is open." is a neutral
   `Alert` with `Info`, text unchanged. Mechanism: shadcn Alert (area 2). Source:
   <https://ui.shadcn.com/docs/components/alert>.

## Boundaries

- Presentation only: no change to API calls, the generated client, routes, auth, session, idempotency (one key per
  draft), retry, query keys or telemetry (`raiseSpans.ts` and `raiseTelemetry.test.tsx` unchanged).
- Theme tokens only: no raw hex or px value in a component. Icons: Lucide, 16px unless HANDOFF names another size,
  2px stroke, `currentColor`, `aria-hidden="true"`.
- Touches (under `frontend/src/features/disputes/raise/`): `RaiseScreen.tsx`, `TransactionSummary.tsx`,
  `ReviewScreen.tsx` and their tests.

## Tests and commits

- QA updates `e2e/tests/dispute-raise.spec.ts` and any other spec that names "Change" by its old name.
- The pull request holds two commits: the presentation change, then `test(ui-raise-review): update assertions for the
  UI refresh` with every unit-test and e2e assertion update. The squash merge joins them on main.

## Verify

- Builder, in `frontend/`: `npm run typecheck`, `lint`, `format`, `test` and `build` pass; the glyph grep
  (`grep -rn '[➚◔▲✓✕↩ⓘ⚠☰⚑]' frontend/src`) prints nothing; the diff has no raw hex or px in a component.
- QA, holding the `stack` lock: `e2e/run-regression.sh` passes with axe on every spec that runs it; with the
  Playwright MCP the raise form, wrong amount and review match their mockups at 360px and 1280px in light and dark;
  no horizontal scroll at 320px or 200% zoom; every interactive element, "Change" included, is at least 44px tall;
  keyboard order is unchanged.
