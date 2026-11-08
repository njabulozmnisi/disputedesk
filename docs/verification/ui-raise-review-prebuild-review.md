# ui-raise-review: pre-build review

- Spec: `docs/backlog/story-ui-raise-review.md` (branch `feature/ux-refresh-docs`, commit `eccb33d`)
- Read against: HANDOFF 3.4 to 3.6 and sections 4 and 5, DESIGN Components (Step indicator, Form surface, Amount
  summary, Button), EXPERIENCE Component Patterns and State Patterns, `mockups/dispute-new.html`,
  `dispute-new-wrong-amount.html`, `dispute-review.html`, `dispute-review-retry.html`, `app.css` and the 360px
  screenshots, PRD FR-13 to FR-15 and NFR-CODE-6, architecture (problem slugs and statuses), the base specs
  `ui-tokens-icons` and `ui-notices-states`, the sibling specs, `docs/verification/decisions.md`, and
  `frontend/src/features/disputes/raise/**` with their tests and `e2e/tests/dispute-raise.spec.ts`.
- Date: 2026-11-08. Reviewer changed no code.

## Verdict

`changes`: one high and eight medium `[story]` points for the product owner. No `[decision]` point: nothing here
contradicts a PRD requirement or an architecture decision; every gap is between the spec, the UX sources and the code.

## Findings

### High

1. **[story] The Boundaries forbid the test change that criterion 6 forces.** Boundaries say
   `raiseTelemetry.test.tsx` is unchanged, but `raiseTelemetry.test.tsx:256` clicks
   `screen.getByRole('link', { name: 'Change' })`. Testing Library matches a string name exactly by default, so once the
   accessible name is "Change your dispute" that query throws and the test fails. A builder can't meet criterion 6 and
   the boundary at once, and must not weaken the test.
   Fix: allow one locator change in `raiseTelemetry.test.tsx` (the name only), in the
   `test(ui-raise-review): …` commit; telemetry behaviour and `raiseSpans.ts` stay unchanged.

### Medium

2. **[story] Criterion 7: shadcn's `Alert` carries `role="alert"`, which a static notice must not have.** shadcn's
   registry `alert.tsx` puts `role="alert"` on its root `div`. Check the file `ui-notices-states` adds; the spec must
   hold either way. With that role the only-once sentence would be announced as an assertive alert every time review
   opens. It would also break existing queries whenever a retryable failure shows above the card: `findByRole('alert')`
   in `ReviewScreen.test.tsx:471, 485, 501, 513, 535, 560, 575, 590, 613` throws on two matches, and Playwright strict
   mode fails `page.getByRole('alert')` at `dispute-raise.spec.ts:414, 418, 449, 455`. The tempting fix (filter the
   locators) would hide the defect. `ui-detail-start` criterion 3 already says "static, with no second announcement".
   Fix: criterion 7 says the notice is static, with no `role="alert"` and no live region. A test proves that on a 503
   the only `alert` is the problem notice. Sources: WAI-ARIA 1.2 `alert` role
   (<https://www.w3.org/TR/wai-aria-1.2/#alert>); WCAG 2.2 SC 4.1.3
   (<https://www.w3.org/WAI/WCAG22/Understanding/status-messages>).

3. **[story] Criterion 1's bound ("the 409 and 422 result screens") doesn't match the code's set of refusals.** The
   review card gives way to a result screen exactly when `endsTheDraft(error)` (`SubmitFailure.tsx:600-608`,
   `ReviewScreen.tsx:474, 497`). That covers 409 `already-disputed`, 422 `not-disputable`, 422 `idempotency-mismatch`
   and also 404 `not-found` (architecture: raise order 404, 409, 422; duplicates 422). Retryable failures (no
   response, 504, `idempotency-in-progress` after the last resend, 503, an unexpected status) keep the card, and
   `mockups/dispute-review-retry.html` shows the step there.
   Fix: "StepProgress shows on review exactly while the review card shows (not after a refusal that `endsTheDraft`)",
   with a test on each side of the bound: a 404 refusal and a 409 or 422 refusal show no step, and a 503 or
   network failure keeps "Step 2 of 2: review and submit".

4. **[story] Criterion 2 draws the quiet block, but no criterion adds the form surface it sits on.** The spec covers
   DESIGN Form surface ("The raise form is one flat surface: the transaction … as a quiet block at the top …, then the
   reasons, fields, and the actions below a hairline"), but no criterion builds that surface. Today the form has no
   surface, and `TransactionSummary` sits outside the `<form>` (`RaiseScreen.tsx:158`) as a `bg-card` bordered box
   (`TransactionSummary.tsx:302`). A literal build of criterion 2 (`bg-background`, no hairline) puts a
   `background`-filled block on the `background` canvas, where it can't be seen.
   Fix: add a criterion that the raise form is one flat surface (`bg-card`, `border-border` hairline, `rounded-lg`,
   per `app.css .surface`) holding the quiet block at the top, the reasons, the fields and the divided actions. Say
   whether `TransactionSummary` moves inside the `<form>` element or the surface wraps both. Criterion 5 or 6 should
   name the same surface for the review read-back.

5. **[story] Criterion 5 contradicts itself below 768px.** It says "from 768px each `dt` sits in a 9rem label
   column", then "below 768px it follows `mockups/dispute-review.html` at 360px". The mockup applies the 9rem column
   at every width (`app.css:241`, no breakpoint), and `screenshots/dispute-review-360.jpg` shows `dt` and `dd` side by
   side at 360px. Today's code stacks below `md` (`ReviewScreen.tsx:375`). At 320px a 9rem column leaves the values
   about 94px wide; with `minmax(0,1fr)` and `overflow-wrap: anywhere` it still doesn't scroll sideways (merchant
   names break mid-word, as the screenshot shows).
   Fix: name one layout. Either a 9rem column at every width, as the mockup does, or stacked below 768px and a 9rem
   column from 768px. QA's 320px and 200% zoom check then has one target.

6. **[story] The masked card's place on review is unspecified.** The mockup reads it back as a `dl` row ("Card",
   "492345•• •••• 3456"). Today `MaskedNumber` renders outside the `dl` as the caption "Card 492345•• •••• 3456"
   (`ReviewScreen.tsx:506`), and `ReviewScreen.test.tsx:70-71` asserts that printed form and exactly one `/Card/` match.
   An account transaction prints "Account •••• 4821".
   Fix: say whether the card or account becomes a `dl` row (with its `dt` for each kind: "Card", "Account") or stays a
   caption line, and which tests change.

7. **[story] The Verify step's 44px check fails on Cancel, which no criterion restyles.** Verify (and HANDOFF 5) says
   every interactive element is at least 44px tall. Cancel on both screens is a plain text link (`RaiseScreen.tsx:288`,
   `ReviewScreen.tsx:536-542`), about one line tall. Both mockups draw it as a secondary pill (`btn--secondary`), and
   DESIGN Button makes buttons full width on phones (Continue and Submit included). Criterion 4 only keeps "button
   order, labels and focus order".
   Fix: add it to criterion 4 and the review actions. Cancel becomes `Button asChild variant="secondary"` around the
   same `Link`, and the buttons go full width below 768px. Review's Cancel keeps `disabled={pending}` (aria-disabled
   and inert while pending, `ReviewScreen.test.tsx:193`). The other route is to exempt Cancel from the 44px check,
   with the reason.

8. **[story] "Covers DESIGN Amount summary", but no criterion implements it.** DESIGN Amount summary: "On review …:
   the disputed amount in amount-hero, with the transaction amount and, for wrong amount, the expected amount below."
   Review shows only the disputed amount today (`ReviewScreen.tsx:499-504`); criterion 5 only adds the label column.
   For Wrong amount the customer never sees the two amounts the difference comes from. FR-14 is cited.
   Fix: either add a criterion (Wrong amount review shows the transaction amount and the expected amount below the
   hero, both already in the draft and the row, so presentation only), or drop Amount summary from Covers.

9. **[story] The mockup differences the spec leaves out aren't listed, so QA's "matches its mockup" check will flag
   each one.** Present in the mockups (most since before this pass) but not in the code or a criterion:
   - a "Back to transactions" link (raise) and a "Back" link (review) above the step, `.back`, 44px;
   - the Expected amount help line "What you agreed to pay. You're disputing the difference." (`exp-help`, before the
     difference in `aria-describedby`);
   - the reasons as one grouped box with hairlines between rows (`app.css:193-197`); today they are six separate cards
     (`RaiseScreen.tsx:193`);
   - the quiet block's caption "Mon 26 Oct 2026, card ending 3456" on one line. Criterion 2 keeps today's text (date
     line plus "Card 492345•• •••• 3456"), which is a choice QA should be told about;
   - the review surface as `<section aria-label="Your dispute">`, a named region. If it is built, the test asserts
     `getByRole('region', { name: 'Your dispute' })` (axe has no name check for it).
   Fix: for each, add it to a criterion or list it under Boundaries as out of scope, so builder and QA share one
   target.

### Low

10. **[story] The Description misstates today's code.** Review's `dl` already has an 8rem label column from `md`
    (`ReviewScreen.tsx:375`): the change is 8rem to 9rem plus the hairline and surface. `TransactionSummary` is a
    bordered `bg-card` box (`TransactionSummary.tsx:302`), not "a row with a hairline".
11. **[story] Criterion 1 doesn't place the step.** `ui-linking` says "above the `h1`", and the mockups put it above
    the `h1`. Say the same here, and that the error summary stays after the `h1` (`RaiseScreen.tsx:149`). Note that
    `usePageHeading` focuses the `h1`, so a screen-reader user starts below the label; that matches the GOV.UK caption
    pattern and EXPERIENCE's "the text label is the accessible step".
12. **[story] The e2e update won't prove criterion 6 unless it uses an exact name.** Playwright matches a string
    `name` case-insensitively as a substring, so `getByRole('link', { name: 'Change' })` (`dispute-raise.spec.ts:311,
    386, 450, 456, 735`) still passes after the change. QA's update should use
    `{ name: 'Change your dispute', exact: true }` at least once. The unit queries at `ReviewScreen.test.tsx:77, 128,
    138, 209, 219` do break and belong in the test commit.
13. **[story] Criterion 6 should fix its mechanism and keep the pending state.** Name the mockup's form: visible
    "Change" plus `<span class="sr-only"> your dispute</span>`, so the name comes from content (WAI-ARIA name from
    content), not an `aria-label` that could drift from the visible text. State that `disabled={pending}` stays
    (EXPERIENCE Idempotent submit). The existing `aria-disabled:opacity-70` on Change, Cancel and `Button` contradicts
    DESIGN Button ("never reduced opacity"). That predates this spec and is shared, so it belongs in `lows.md`, not
    here.

## Standards check

| Mechanism | Standard | Status |
|---|---|---|
| Step label with decorative bar | WCAG 2.2 SC 1.1.1; `aria-hidden` bar | Fine; bound in finding 3 |
| Quiet block, hairline, surface | Tailwind v4 theme variables and utilities | Fine once finding 4 adds the surface |
| Difference as field description | shadcn Field; `aria-describedby` (WCAG 1.3.1) | Fine. `text-label font-strong text-heading` gives 14px, 700, `heading` colour: compiled with the installed Tailwind 4.3.3, `text-heading` resolves to `color: var(--heading)` only. Don't copy the mockup's inline `style` attribute. |
| Read-back label column | CSS grid, Tailwind `grid-cols-[9rem_minmax(0,1fr)]` (rem, not px, so within the token rule) | Fine; layout bound in finding 5 |
| "Change" name | WCAG 2.2 SC 2.5.3, 2.5.5; name from content | Fine; finding 13 |
| Only-once notice | shadcn Alert | Needs the role override (finding 2) |

No home-grown mechanism is proposed. Each criterion can be explained in a sentence.

## Simplicity

The smallest design fits in the three named files. The step on review is one condition (`refused ? null : …`,
already in `ReviewScreen.tsx:497`), so no new prop or wrapper is needed. Don't add a `ReadbackRow` beyond today's
`Row`, and don't reuse `--amount-col` for the label column: same value, different meaning.

## Security checklist (below High-stakes)

Presentation only. No endpoint, cookie, session, antiforgery, idempotency key, problem mapping or telemetry change. The
note still renders as React text (`ReviewScreen.tsx:514`), with no HTML injection. No new package (`lucide-react` and
`alert.tsx` arrive in the base stories). CSP is unchanged, provided the builder uses utilities, not the mockups' inline
`style` attributes. No findings.

## Not run

No tests were run: this is a design review of a spec, and the worktree has no `node_modules`. The only thing executed
was an in-memory Tailwind compile of `text-heading` and `text-label` with the main checkout's installed
`@tailwindcss/node` 4.3.3.
