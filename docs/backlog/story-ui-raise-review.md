---
id: 7
type: story
title: "Raising and reviewing a dispute show the step and a quiet transaction block"
sources: [docs/ux/HANDOFF.md, docs/ux/DESIGN.md, docs/ux/EXPERIENCE.md, docs/ux/mockups/dispute-new.html, docs/ux/mockups/dispute-new-wrong-amount.html, docs/ux/mockups/dispute-review.html, docs/ux/mockups/dispute-review-retry.html, docs/ux/mockups/app.css, docs/conventions/frontend.md, docs/prd/prd.md]
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
and review (`ReviewScreen.tsx`) show no step. The form has no surface: `TransactionSummary.tsx` sits outside the
`<form>` as a bordered `bg-card` box. The wrong-amount difference is a plain field description. Review's hero amount
and read-back `dl` are two separate boxes; the `dl` has an 8rem label column from `md` and stacks below it; the masked
card is a caption outside the `dl`; "Change" and Cancel are small text links; the only-once sentence is plain text.
This spec draws both screens to the mockups.

- Tier: Feature. Owner: frontend. Stacking: on `feature/ui-notices-states`; builds beside `ui-lists`, `ui-linking`
  and `ui-detail-start`. No shared file with `ui-linking` or `ui-detail-start`.
- Shared files with `ui-lists` (AGENTS.md, Dispatch rules, collision check): both stories are stacked siblings on
  `feature/ui-notices-states` and both edit `frontend/src/features/disputes/raise/raiseTelemetry.test.tsx` and
  `e2e/tests/dispute-raise.spec.ts`, in different lines. This spec owns only the "Change" lookups: in
  `raiseTelemetry.test.tsx` line 256 (the test "is still one after 4a, 4b and 4a again"); in `dispute-raise.spec.ts`
  lines 311, 386, 450, 456 and 735. `ui-lists` owns the Dispute link lookups (`raiseTelemetry.test.tsx` lines 267,
  406 and 428; `dispute-raise.spec.ts` lines 98, 331, 628 and 632). Neither spec changes the raise screen's `h1`
  assertions ("Dispute this transaction"). Whichever of the two merges second rebases onto main and resolves any
  conflict, keeping both renames.
- Presentation only: no API, route, auth, session, idempotency or retry change; the draft, its `Idempotency-Key` and
  Back behaviour are unchanged.
- Covers: HANDOFF 3.4, 3.5, 3.6; DESIGN Components (Step indicator, Form surface, Button); EXPERIENCE Component
  Patterns (Step indicator, Expected amount, Review card), Interaction Primitives (Idempotent submit, Back and
  forward); PRD FR-13, FR-14, FR-15, NFR-CODE-6. DESIGN Amount summary is not covered (Boundaries).

## Inputs

No new request inputs: this spec is presentation only, and no request body, query, header or route parameter
changes. UI inputs are unchanged: the reason (one of six), the note (at most 1,000 characters after trimming) and,
for Wrong amount, the expected amount (at least R0.00, below the transaction amount, at most 2 decimals), all as
`raiseSchema.ts` bounds them today.

## Criteria

1. **Each screen shows its step exactly while it can go on** (HANDOFF 3.4; EXPERIENCE Step indicator)
   **Given** the raise form and review
   **When** each renders
   **Then** `StepProgress` sits above the `h1`: "Step 1 of 2: reason and note" on the raise form (the error summary
   stays after the `h1`) and "Step 2 of 2: review and submit" on review, shown exactly while the review card shows,
   that is not after a refusal that `endsTheDraft` (404 `not-found`, 409 `already-disputed`, 422 `not-disputable`,
   422 `idempotency-mismatch`); a retryable failure (no response, 503, 504, `idempotency-in-progress` after the last
   resend, an unexpected status) keeps the card and the step, as `mockups/dispute-review-retry.html` shows
   **And** `ReviewScreen.test.tsx` proves each side of the bound: a 404 refusal and a 409 or 422 refusal show no
   step label; a 503 and a network failure keep "Step 2 of 2: review and submit". Focus still moves to the `h1`, so
   a screen-reader user starts below the label, as EXPERIENCE's "the text label is the accessible step" allows.
   Mechanism: `StepProgress` (area 2). Source: WCAG 2.2 SC 1.1.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/non-text-content>).

2. **The raise form is one surface with a quiet transaction block** (HANDOFF 3.5; DESIGN Form surface)
   **Given** the raise form
   **When** it renders
   **Then** the `<form>` element is one flat surface (`bg-card`, `border-border` hairline, `rounded-lg`, as
   `app.css .surface`) and `TransactionSummary` moves inside it, at the top; then the reasons, the fields and the
   actions
   **And** `TransactionSummary` is a block with `background` fill and `rounded-md`, no border: merchant, date line,
   "Card 492345•• •••• 3456" (today's text), the amount on the right; its text and the amount's accessible label are
   unchanged. Mechanism: theme tokens. Source: Tailwind v4, Theme variables (<https://tailwindcss.com/docs/theme>).

3. **The wrong-amount difference is drawn** (HANDOFF 3.5; EXPERIENCE Expected amount; FR-14)
   **Given** Wrong amount with a valid expected amount
   **When** the difference shows
   **Then** "You're disputing R349.00" is 14/700 in `heading` (`text-label font-strong text-heading`, not the
   mockup's inline `style`), still the field description tied to the input by `aria-describedby`
   (`expectedAmount-disputing`), and it updates live as today. Mechanism: shadcn Field description. Source:
   <https://ui.shadcn.com/docs/components/field>; WCAG 2.2 SC 1.3.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/info-and-relationships>).

4. **The actions sit below a hairline as 44px pills** (HANDOFF 3.5; DESIGN Form surface, Button)
   **Given** the raise form and review
   **When** each renders
   **Then** on the raise form a `border` hairline separates the fields from the buttons; on both screens Cancel is
   `Button asChild variant="secondary"` around the same `Link` (48px pill, at least 44px), and the buttons (Continue
   or Submit dispute, then Cancel) are full width below 768px; button order, labels and focus order are unchanged
   **And** review's Cancel keeps `disabled={pending}` (`aria-disabled` and inert while a submit is pending, as
   `ReviewScreen.test.tsx` already asserts). Mechanism: a top border in the theme's `border` colour; the shadcn
   Button `asChild` pattern. Source: Tailwind v4, border-width (<https://tailwindcss.com/docs/border-width>); shadcn
   Button, Link (<https://ui.shadcn.com/docs/components/button>).

5. **Review is one surface with a 9rem label column** (HANDOFF 3.6; DESIGN Form surface)
   **Given** review with each reason
   **When** the card renders
   **Then** the hero amount, "Change" and the read-back `dl` sit on one flat surface, a
   `<section aria-label="Your dispute">` (`bg-card`, hairline, `rounded-lg`); at every width each `dt` sits in a
   9rem label column, capped at 40% of the row, beside its `dd` (`--label-col`,
   `minmax(0,min(var(--label-col),40%))`, amended in lows-2026-11-10; values wrapping with
   `overflow-wrap: anywhere`, as the mockup and `screenshots/dispute-review-360.jpg` show), with hairlines between
   rows; the `dl`, `dt` and `dd` semantics stay
   **And** the masked number becomes a `dl` row after Date: `dt` "Card" with `dd` "492345•• •••• 3456" for a card
   transaction, `dt` "Account" with `dd` "•••• 4821" for an account one; the caption line outside the `dl` goes
   **And** `ReviewScreen.test.tsx` asserts `getByRole('region', { name: 'Your dispute' })` and the Card and Account
   rows (the assertions on "Card 492345•• •••• 3456" and on exactly one `/Card/` match change with it, in the test
   commit). Mechanism: CSS grid; a named `section`. Source: Tailwind v4, grid-template-columns
   (<https://tailwindcss.com/docs/grid-template-columns>); WAI-ARIA 1.2, region role
   (<https://www.w3.org/TR/wai-aria-1.2/#region>).

6. **"Change" is a named 44px control** (HANDOFF 3.6; EXPERIENCE Review card)
   **Given** review
   **When** it renders
   **Then** "Change" sits top right beside the hero amount, is at least 44px tall (`--tap-target`), reads visible
   "Change" plus `<span className="sr-only"> your dispute</span>` so its accessible name "Change your dispute" comes
   from its content (no `aria-label`), keeps `disabled={pending}`, and still returns to the raise form with the
   values kept (a changed draft gets a new key, as today). Mechanism: visible label inside the accessible name; name
   from content. Source: WCAG 2.2 SC 2.5.3 (<https://www.w3.org/WAI/WCAG22/Understanding/label-in-name>), SC 2.5.5
   (<https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced>); WAI-ARIA 1.2, name from content
   (<https://www.w3.org/TR/wai-aria-1.2/#namefromcontent>).

7. **The only-once sentence is a static notice** (HANDOFF 3.6)
   **Given** review
   **When** it renders
   **Then** "You can dispute a transaction only once. You can withdraw the dispute while it is open." is a neutral
   `Alert` with `Info`, text unchanged, static: no `role="alert"`, no `role="status"` and no live region
   **And** a `ReviewScreen.test.tsx` test proves that after a 503 on submit exactly one `alert` is on the screen, the
   problem notice. Mechanism: shadcn Alert (area 2), the alert role kept for errors only. Source:
   <https://ui.shadcn.com/docs/components/alert>; WAI-ARIA 1.2, alert role (<https://www.w3.org/TR/wai-aria-1.2/#alert>);
   WCAG 2.2 SC 4.1.3 (<https://www.w3.org/WAI/WCAG22/Understanding/status-messages>).

## Boundaries

- Presentation only: no change to API calls, the generated client, routes, auth, session, idempotency (one key per
  draft), retry, query keys or telemetry (`raiseSpans.ts` unchanged; in `raiseTelemetry.test.tsx` only the "Change"
  locator at line 256 changes, to `{ name: 'Change your dispute' }`, in the test commit).
- Theme tokens only: no raw hex or px value in a component; the 9rem label column is its own value, not
  `--amount-col`. Icons: Lucide, 16px unless HANDOFF names another size, 2px stroke, `currentColor`,
  `aria-hidden="true"`. No mockup inline `style` attribute (CSP).
- Touches (under `frontend/src/features/disputes/raise/`): `RaiseScreen.tsx`, `TransactionSummary.tsx`,
  `ReviewScreen.tsx` and their tests; `raiseTelemetry.test.tsx` (line 256 only).
- Mockup differences, each in or out of scope, so builder and QA share one target:
  - in: the step above the `h1` (criterion 1); the form surface (criterion 2); Cancel as a secondary pill and
    full-width buttons below 768px (criterion 4); the review surface as `<section aria-label="Your dispute">` and the
    Card or Account `dl` row (criterion 5);
  - out: the "Back to transactions" link on the raise form and the "Back" link on review (browser Back and Cancel
    stay the ways back);
  - out: the Expected amount help line "What you agreed to pay. You're disputing the difference." (not in the
    EXPERIENCE copy deck);
  - out: the reasons as one grouped box with hairlines (DESIGN Reason group); the six reason cards stay;
  - out: the quiet block's one-line caption "Mon 26 Oct 2026, card ending 3456"; today's date line and
    "Card 492345•• •••• 3456" stay (criterion 2);
  - out: DESIGN Amount summary's transaction and expected amounts below the hero; no mockup draws them, and review
    keeps only the disputed amount.

## Tests and commits

- QA updates `e2e/tests/dispute-raise.spec.ts`, the "Change" lookups only (lines 311, 386, 450, 456 and 735, shared
  with `ui-lists`, see Description), using `{ name: 'Change your dispute', exact: true }` at least once (a string
  `name` is otherwise a case-insensitive substring match, so the old lookup would still pass), and any other spec
  that names "Change" by its old name.
- The pull request holds two commits: the presentation change, then `test(ui-raise-review): update assertions for the
  UI refresh` with every unit-test and e2e assertion update, including the `ReviewScreen.test.tsx` "Change" queries
  and the `raiseTelemetry.test.tsx` locator. The squash merge joins them on main.

## Verify

- Builder, in `frontend/`: `npm run typecheck`, `lint`, `format`, `test` and `build` pass; the glyph grep
  (`grep -rn '[➚◔▲✓✕↩ⓘ⚠☰⚑]' frontend/src`) prints nothing; the diff has no raw hex or px in a component.
- QA, holding the `stack` lock: `e2e/run-regression.sh` passes with axe on every spec that runs it; with the
  Playwright MCP the raise form, wrong amount and review match their mockups, apart from the out-of-scope
  differences under Boundaries, at 360px and 1280px in light and dark; no horizontal scroll at 320px or 200% zoom;
  every interactive element, "Change" and both Cancels included, is at least 44px tall; keyboard order is unchanged.

## Notes

- Low for `docs/verification/lows.md` (not a criterion here): the existing `aria-disabled:opacity-70` on "Change",
  Cancel and `Button` contradicts DESIGN Button ("never reduced opacity"); it predates this spec and is shared, so
  the lows batch fixes it.

## Sofia's refine

Sofia, 2026-11-08, on `docs/verification/ui-raise-review-prebuild-review.md`. No `[decision]` point; every `[story]`
point fixed with the reviewer's recommended fix:

- 1 high, telemetry test locked: Boundaries allow the one "Change" locator change in `raiseTelemetry.test.tsx`
  (line 256) in the test commit; `raiseSpans.ts` and telemetry behaviour unchanged.
- 2 medium, alert role: criterion 7 makes the notice static (no alert role, no live region) and adds a test that a
  503 leaves exactly one `alert`.
- 3 medium, step bound: criterion 1 shows the step exactly while the review card shows, with tests on both sides
  (404 and 409 or 422 refusals; 503 and network failure).
- 4 medium, form surface: criterion 2 makes the `<form>` the surface and moves `TransactionSummary` inside it;
  criterion 5 names the review surface.
- 5 medium, layout below 768px: criterion 5 uses the 9rem column at every width, as the mockup.
- 6 medium, card place: criterion 5 adds the Card or Account `dl` row and names the changed tests.
- 7 medium, Cancel 44px: criterion 4 makes Cancel a secondary pill on both screens, buttons full width below 768px,
  review's Cancel keeps `disabled={pending}`.
- 8 medium, Amount summary: dropped from Covers; listed out of scope under Boundaries.
- 9 medium, mockup differences: Boundaries lists each as in or out of scope.
- 10 low, Description: corrected to today's code (8rem column from `md`, `bg-card` bordered summary outside the form).
- 11 low, step place: criterion 1 puts the step above the `h1`, the error summary after it, and notes the focus.
- 12 low, e2e exact name: Tests and commits asks for `exact: true` at least once.
- 13 low, name mechanism: criterion 6 names the `sr-only` suffix (name from content) and keeps `disabled={pending}`;
  the opacity-70 pending style goes to Notes as a low for `docs/verification/lows.md`.
- Shared files with `ui-lists`: recorded in the Description with the lines each owns.
