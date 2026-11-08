# ui-notices-states: pre-build review

- Date: 2026-11-08
- Story: `docs/backlog/story-ui-notices-states.md` (branch `feature/ux-refresh-docs`, commit 6888591)
- Read against: `docs/ux/HANDOFF.md` (2, 3.4, 3.7, 3.8, 6), `docs/ux/DESIGN.md` (Components, Shapes), `docs/ux/EXPERIENCE.md`
  (Component Patterns, State Patterns, Accessibility Floor), `docs/ux/mockups/app.css` and the error, session-expired,
  link-locked, link-rate-limited, link-errors, not-found, transactions-empty and disputes-empty mockups,
  `docs/architecture/architecture.md`, `docs/conventions/frontend.md`, `docs/verification/decisions.md` (2026-11-08 rows),
  the sibling specs that consume this one (`ui-linking`, `ui-raise-review`, `ui-detail-start`), and the code under
  `frontend/src` (identical to main), with Pass 1, Pass 2 and the security checklist.
- shadcn registry read on 2026-11-08: `https://ui.shadcn.com/r/styles/new-york-v4/alert.json`; shadcn CLI 4.23.0 and
  `@shadcn/registry` 0.2.1 tarballs inspected (not run).
- Result: **changes**. Four medium `[story]` points and six low `[story]` points; no `[decision]` point. The story sits
  within the PRD (NFR-CODE-4, NFR-CODE-6) and the architecture; no point contradicts a decision.

## Points

### P1 [story] medium: shadcn `Alert` puts `role="alert"` on every tone, but only errors may carry it

The registry's `Alert` hard-codes `role="alert"` on its root (`<div data-slot="alert" role="alert" … {...props} />`).
Criterion 1 builds five tones on it, and the sibling specs that use them expect static notices: `ui-detail-start`
criterion "Dispute submitted is a success `Alert` … static, with no second announcement", and `ui-raise-review`'s
neutral `Alert` ("You can dispute a transaction only once…"), drawn with no role in `mockups/dispute-review-retry.html`.
EXPERIENCE.md, Accessibility Floor: "Errors use `role="alert"`"; async results go through the one live region. A
success, info or neutral `Alert` left with the built-in role would be announced as an alert, and the success banner
would double the live region's "Dispute submitted".

Fix: criterion 1 states which tones carry `role="alert"` (danger and rate limited, as today through `ProblemNotice`)
and that success, info and neutral render with no alert role (the `role` prop overrides it, since the registry spreads
`props` after `role`), with a test per tone asserting the icon and the role or its absence. Source: WAI-ARIA APG,
Alert pattern (<https://www.w3.org/WAI/ARIA/apg/patterns/alert/>).

### P2 [story] medium: the error summary must keep its group, its name and its `h2`

`ErrorSummary.tsx` is a GOV.UK error summary: an outer `role="group"` named by `aria-labelledby="error-summary-title"`
with `tabIndex={-1}` that takes focus, an inner `role="alert"`, and the title as an `h2`. The unit tests find it 14
times as `getByRole('group', { name: 'Check the details below' })` (`DisputeDetailScreen.test.tsx:796`, `:1204`,
`RaiseScreen.test.tsx`, `DetailsScreen.test.tsx`, `OtpScreen.test.tsx`). Moving it onto `Alert` as criterion 3
reads ("a danger `Alert` titled …") puts `role="alert"` on the focused root and invites `AlertTitle`, a `div`, in
place of the `h2`. That breaks the group name the tests and screen readers use, and drops the heading GOV.UK specifies.

Fix: criterion 3 adds "the root keeps `role="group"`, its `aria-labelledby` and `tabIndex={-1}`; the inner
`role="alert"` stays; the title stays an `h2`". Source: GOV.UK Design System, Error summary
(<https://design-system.service.gov.uk/components/error-summary/>).

### P3 [story] medium: the registry `Alert` defaults clip the title and break the notice shape

The registry file as the CLI writes it carries defaults that conflict with DESIGN and EXPERIENCE:

- `AlertTitle` has `line-clamp-1`. `ProblemNotice`'s message is the natural title, and messages such as "This
  transaction already has a dispute. You can only dispute a transaction once." (`SubmitFailure.tsx:89`) wrap at
  360px, so the clamp hides words behind an ellipsis: content loss at reflow (WCAG 2.2 SC 1.4.10).
- `AlertDescription` sets `text-muted-foreground`, not the tone's foreground, on a status background.
- The root is `rounded-lg border px-4 py-3 text-sm`. In this theme `rounded-lg` is 24px (`tokens.css:121`); DESIGN,
  Shapes, puts notices at `{rounded.md}` (12px), and `app.css:213` draws them with no border and `--space-4` padding.
  `text-sm` and `font-medium` sit outside the project's type tokens (`text-body`, `text-label`, `font-strong`).

Criterion 1 only says "its colours are the theme's status tokens", so the builder can't tell whether to keep these
defaults. Fix: criterion 1 names the edits to the generated file (shadcn: you own the code): the title wraps (no line
clamp), the description takes the tone colour, each tone variant sets `rounded-md`, no border, `p-4`, and the theme's
type tokens. Add one unit test that renders a long title and asserts no `line-clamp` class. Source:
<https://ui.shadcn.com/docs/components/alert>; WCAG 2.2 SC 1.4.10 (<https://www.w3.org/WAI/WCAG22/Understanding/reflow>).

### P4 [story] medium: the CLI version is a placeholder, and today's registry declares a `cn` package

Criterion 1 writes `npx shadcn@<exact version> add alert` but names no version, and the Definition of ready and
Dependency verification need one. Separately, the registry entry served today lists `"dependencies": ["cn"]` and its
file imports `cn` from `"cn"`. The same holds for `badge`, which shows the registry changed after the project's
components were added. `cn` 0.5.2 is shadcn's new clsx and tailwind-merge replacement (repository `shadcn-ui/cn`),
published 2026-11-02. The CLI fetches the registry by style URL, so pinning an older CLI does not pin the registry
content. If `add` installs the registry dependencies, it adds `cn` to `package.json` and leaves an import that
differs from the `@/lib/utils` the other `components/ui/` files use. That breaks criterion 1's "adds nothing to
`package.json`". **Unverified:** I inspected the CLI bundle but did not run it, so whether `add` installs `cn` here is
not confirmed.

Fix: name the exact CLI version, and give the documented fallback: if the CLI adds or changes any package, or writes
an import other than `@/lib/utils`, use the shadcn docs' Manual installation for Alert (copy the component, import `cn`
from `@/lib/utils`; `class-variance-authority` is already installed). Add a Verify line: `package.json` and
`package-lock.json` are unchanged in the diff. Source: <https://ui.shadcn.com/docs/components/alert> (Manual tab),
<https://ui.shadcn.com/docs/cli>; AGENTS.md, Dependency verification.

### P5 [story] medium: the detail screen's not-found shares one `h1` with every other state

`DisputeDetailScreen` deliberately renders loading, failure, not-found and the loaded dispute with **one `h1` element
at one place in the tree** (`DisputeDetailScreen.tsx:170-176`, the `<section>` at `:254-257`). `NotFoundCard`
(`:78-89`) is only the sentence and the button under that shared `h1`. The "Try again" handler focuses
`headingRef.current` straight after `refetch()` resolves (`:227-229`), and the withdraw 404 path relies on the same
element. Criterion 5 says `NotFoundCard` becomes a `StateSurface` with its own heading. A `StateSurface` that renders
its own `h1`, or wraps the `h1` in a new parent only when the dispute is not found, remounts the heading. The handler
then focuses a detached element, and the tests that prove the focus move are likely to fail:
`DisputeDetailScreen.test.tsx:238` ("moves focus to the heading when Try again is answered 404") and the not-found
cases at `:1218-1222`. "Heading focus (`usePageHeading`) unchanged" does not tell the builder about this constraint.

Fix: criterion 5 states that on the detail screen the shared `h1` stays one element at one place in the tree. For
example, `StateSurface` takes the caller's heading element instead of rendering its own, and the detail screen keeps the
tile slot and the surface classes on the same elements in every state, null when not needed. Name the existing detail
focus tests as the proof, passing unchanged.

### P6 [story] low: the locked state's help line sits outside the surface

`LockedState` (`OtpScreen.tsx:31-52`) ends with `<ContactHelp />` after the action. `mockups/link-locked.html` puts the
help block after the `.state` surface, not inside it, and HANDOFF 3.9 gives its restyle to `ui-linking`. Criterion 5
("the existing sentences, then the one action") doesn't say where `ContactHelp` goes. Fix: add "`ContactHelp` stays
after the surface, outside it, unchanged".

### P7 [story] low: `ProblemNotice`'s action and its form without a reference are not specified

Criterion 2 covers only `ProblemNotice` with a `traceId`. Two existing forms are not covered:

- The `action` prop: the sign-out-failed notice renders a "Sign out" button inside the notice (`RootLayout.tsx:90-93`;
  EXPERIENCE, sign-out failed).
- The form with no `traceId`, which most `SubmitFailure.tsx` notices use (`:78`, `:88`, `:107`).

Fix: criterion 2 adds "`action`, when given, renders inside the `Alert` after the reference row, as today; with no
`traceId` there is no reference sentence and no row".

### P8 [story] low: say which source wins where the mockups drift from the copy and icon size

- The notice icon is 18px in `app.css:214`. It is 16px in DESIGN (Icons, line 256), in HANDOFF section 2 and in the
  registry's `[&>svg]:size-4`. DESIGN wins (HANDOFF, Sources of truth), but QA compares against the mockup.
- `mockups/link-rate-limited.html` splits "Too many attempts" (title) from "Try again in 12 minutes." (body), while
  criterion 4 keeps `waitCopy`'s single sentence, which matches EXPERIENCE's copy deck.

Fix: one line in Boundaries saying notice icons are 16px and the rate-limit text is `waitCopy`'s single sentence, so
QA doesn't report these as mockup mismatches.

### P9 [story] low: `StepProgress`'s step count has one value

All four uses (HANDOFF 3.4) have two steps, and criterion 6 fixes the count at 2. A step-count prop with a single value
is a speculative option (AGENTS.md, "no speculative options or extension points"). Fix: two fixed segments and
`step: 1 | 2`. Or, if the prop stays, bound it and test the bound. The criterion's test already sits on both sides of
the current-step boundary (step 1: one `primary` and one `border`; step 2: two `primary`).

### P10 [story] low: the e2e grep term finds nothing, and criteria 1-5 name no proving test

- "Any other its grep for 'quote this reference' finds": no e2e spec contains that phrase (only `ProblemNotice.tsx`
  does). Other specs assert these states' copy too: `sign-in-and-out-audit.spec.ts`,
  `spa-dispute-status-badges.spec.ts`, `dispute-raise.spec.ts`, `journeys/uj-1.spec.ts` and `journeys/uj-5a.spec.ts`
  match the state headings, "Too many attempts", "Check the details below" or "Copy". Fix: name these grep terms.
- Only criterion 6 names its unit test. Fix: give each of criteria 1-5 its test, for example an `alert` test per tone
  (P1), the `ProblemNotice` row and Copy tests, a `FieldError` icon test, and a `StateSurface` test for heading level and
  the tile. Name the parts jsdom can't prove (colours, 44px height, radius) as QA's visual checks.

## Pass 1: correctness

- Criteria are testable and tagged to HANDOFF, DESIGN and EXPERIENCE requirements; the gaps are P1, P2, P3 and P5.
- No API, Result, ProblemDetails, ownership, stored-event or timestamp surface is touched; those Pass 1 items don't
  apply.
- The cited line references hold: `DetailsScreen.tsx:103` is the rate-limit notice, and `OtpScreen.tsx:31-52` is
  `LockedState`.

## Pass 2: simplicity and standards

| Mechanism | Standard | Assessment |
|---|---|---|
| Notices | shadcn Alert (<https://ui.shadcn.com/docs/components/alert>), decided 2026-11-08 | Fits; see P1, P3, P4 |
| Alert roles | WAI-ARIA APG Alert pattern | Needs P1 |
| Error summary | GOV.UK Error summary | Needs P2 |
| Copy reference | Clipboard API, `writeText` (MDN) | Unchanged behaviour; fits |
| Target size | WCAG 2.2 SC 2.5.5 and the project's 44px `--tap-target` | Fits |
| `StateSurface` | WCAG 2.2 SC 1.3.1 headings | Product component from DESIGN, with no framework equivalent; explainable in a sentence; see P5 |
| `StepProgress` | WCAG 2.2 SC 1.1.1, decorative bar `aria-hidden` | Fits; see P9 |

No wrapper, layer or single-implementation interface is added. `StepProgress` has no caller in this story, which is
deliberate so that areas 4 and 5 can build in parallel.

## Security checklist

- AuthN/Z, browser session, linking, data and audit: not touched (presentation only; the boundary is stated).
- Supply chain: P4 (CLI version unbound; the registry's new `cn` dependency could add an unverified package).
- Headers: no inline `style` is added (Tailwind classes; lucide renders SVG attributes), so the gateway's
  `style-src 'self'` holds.
- The `traceId` shown and copied is the existing reference, not personal data.

## Decisions needed

None.
