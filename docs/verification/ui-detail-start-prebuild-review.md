# ui-detail-start: pre-build review

- Date: 2026-11-08
- Story: `docs/backlog/story-ui-detail-start.md` (9 criteria; branch `feature/ux-refresh-docs`, worktree
  `/Users/njabulomnisi/Projects/Dojo/DisputeDesk-ux-docs`, commit eccb33d plus the uncommitted refinement)
- Read against: `docs/ux/HANDOFF.md` (sources of truth, 2, 3.1, 3.10-3.13, 4, 5), `docs/ux/DESIGN.md` (Typography, Layout,
  Shapes, Components, hosted-login theming), `docs/ux/EXPERIENCE.md` (copy deck, Component and State Patterns,
  Accessibility Floor, Responsive), the mockups `dispute-submitted`, `-under-review`, `-upheld`, `-rejected`,
  `-withdrawn`, `-withdraw`, `-loading`, `start.html` and `app.css`, `docs/prd/prd.md` (FR-17, FR-18),
  `docs/architecture/architecture.md`, `docs/verification/decisions.md`, the refined sibling specs `ui-notices-states` and
  `ui-tokens-icons` and their pre-build records, and the code: `features/disputes/detail/**`,
  `features/disputes/withdraw/WithdrawDialog.tsx`, `components/ui/alert-dialog.tsx`, `pages/StartPage.tsx`,
  `features/transactions/format.ts`, `theme/wordmark*.png`, `infra/images/zitadel/provision.sh`, `docker-compose.yml`,
  `infra/deploy/aws/compose.yml`, `.github/workflows/ci.yml` (platform job), `e2e/run-regression.sh` and the e2e specs that
  read the detail screen. Pass 1, Pass 2 and the security checklist.
- Result: **changes**. One medium `[decision]` point, seven medium `[story]` points and six low `[story]` points.

## Points

### P1 [decision] medium: criterion 4 removes the per-entry status that FR-17 requires

PRD FR-17, Consequences: "Each timeline entry shows: the new status as the customer sees it; the time …; the actor
type; the note …", and "Escalated appears as 'Under review (prioritised)' in both the list and the detail". Criterion 4
(after HANDOFF 3.10 and DESIGN Timeline) shows "the copy-deck line only, no badge text". Then:

- no entry shows a status name ("Submitted", "Under review", "Resolved · Rejected"). Only the sentence remains, and for
  an unknown action or outcome there is no sentence at all (see P9);
- once an escalated dispute moves on, "Under review (prioritised)" no longer appears anywhere on its detail, because
  the card badge shows only the current status.

The e2e checks that prove FR-17 today assert exactly this: `dispute-detail.spec.ts:354` (`'Submitted'`), `:421`
(`'Rejected'`), `journeys/uj-1.spec.ts:112`, `journeys/uj-2.spec.ts:85` (`'Withdrawn'`). EXPERIENCE.md still says the same
in two places: line 77, "Customer-facing status (badge text; timeline heading)", and line 265 (UJ-4), "The timeline
shows Submitted (You), Under review (The bank), then Under review (prioritised)". HANDOFF lets DESIGN and EXPERIENCE win
over the mockups, but nothing lets the UX pass overrule a PRD consequence.

Decide one of these:

- (a) Accept the copy-deck sentence as "the new status as the customer sees it". Amend FR-17's wording, and note that
  "Under review (prioritised)" is the badge name for the current status only. Align EXPERIENCE.md lines 77 and 265.
  Record the decision in `decisions.md`.
- (b) Keep a status label per entry, and amend HANDOFF 3.10, DESIGN Timeline and criterion 4.

### P2 [story] medium: the detail skeleton must keep the shared `h1` as text, not the mockup's title bar

`mockups/dispute-loading.html` draws the summary card's title as a skeleton bar and puts `aria-hidden="true"` on the
whole split. Today, loading, failure, not-found and loaded share **one `h1` element at one place in the tree**
(`DisputeDetailScreen.tsx:169-175`, `:254-257`). While loading, it reads "Dispute". EXPERIENCE State Patterns, Loading,
says "Page title renders immediately". These checks depend on it:

- `DisputeDetailScreen.test.tsx:156`: the `h1` "Dispute", three `li` under `[aria-busy="true"]`, and the same element
  once loaded;
- the axe test while loading at `:459`;
- e2e `dispute-detail.spec.ts:313`: title `Dispute · DisputeDesk`.

`ui-notices-states` criterion 5 already fixes this for not-found, but criterion 6 here only says "`aria-busy` is set as
today". A builder who follows the mockup replaces or hides the `h1`. That breaks direct-load focus and the
route-change focus rule.

Fix: criterion 6 states that:

- the skeleton card holds the real `h1` "Dispute" (the same element as in every other state) above its bars, and is
  not `aria-hidden`;
- the three timeline placeholders stay `li` inside the `aria-busy` element, and their dots are `aria-hidden`;
- `:156` and `:459` pass unchanged.

### P3 [story] medium: criterion 1's test passes even if the code reads `timeline[0]`

The criterion asks for "a timeline whose first entry has a different time". A different time on the same South
African day formats to the same "26 Oct 2026", so the test cannot tell `createdAt` from `timeline[0].occurredAt`.

Fix: the first entry's `occurredAt` falls on a different SAST day. For example, `createdAt` `2026-10-25T22:30:00Z`
reads "26 Oct 2026", and `timeline[0].occurredAt` `2026-10-27T08:00:00Z` must not appear in the row. The formatter's
own SAST rollover is already proved (`format.test.ts:61-63`).

### P4 [story] medium: the summary row order for a wrong-amount dispute is unstated, and today's code disagrees with DESIGN

Criterion 1 puts "Disputed on" "after Transaction date". DESIGN, Dispute summary card, gives the order: reason, then
"(for Wrong amount, the transaction and expected amounts)", then transaction date, then Disputed on.
`mockups/dispute-rejected.html` draws the same order. The code puts Transaction date before the two amounts
(`DisputeDetailScreen.tsx:135-142`). A builder who follows the criterion literally produces Reason, Transaction date,
Disputed on, Transaction amount, Expected amount, and QA's mockup comparison then fails.

Fix: criterion 1 gives the full order: Reason, [Transaction amount, Expected amount], Transaction date, Disputed on.
Add a unit test on a wrong-amount dispute that asserts the `dt` order.

### P5 [story] medium: criterion 9 cites a CI check that does not exist, and names no drawing mechanism or size

- "Beginning with the PNG signature (the CI `platform` job's existing check passes unchanged)": the platform job checks
  only that `tokens.css` names both files and loads neither (`ci.yml:148-154`). Its own comment says the file type and
  the 512 KiB limit are checked by Zitadel at upload (`ci.yml:145-147`). So CI checks neither the signature nor the
  size.
- The Definition of ready asks each criterion to name its mechanism. Criterion 9 forbids adding a drawing tool but
  doesn't say how the PNGs are drawn. Story 2.6's builder used an unrecorded shell command (`2.6-review.md:15`). This
  machine has no ImageMagick or rsvg. The installed tool that can render Nunito Sans 800 onto a transparent PNG is
  Playwright 1.64.1 in `e2e/package.json`: `page.screenshot({ omitBackground: true, clip })`
  (<https://playwright.dev/docs/api/class-page#page-screenshot>), using the committed WOFF2 from `theme/fonts/`.
- No pixel size is given. Today both files are 620 x 128 RGBA, roughly 4x the 20px wordmark. "8px at the 20px name
  size, 2px gap" leaves the output scale open, and the scale changes how the hosted login sizes the logo.

Fix: name the drawing mechanism (the Playwright screenshot, a throwaway script that is not committed). Keep 620 x 128,
or state the dimensions and scale (for example 4x: name 80px, dot 32px, gap 8px, dot on the baseline). Replace the
CI parenthetical with the real proofs:

- the builder's report shows `file` output (PNG, RGBA) and each file's byte size (at most 512 KiB);
- QA's provisioning log shows "logo uploaded" and "dark logo uploaded" with no 4xx.

### P6 [story] medium: the human AWS step cannot pick up the new PNGs as the story describes it

In AWS, Zitadel's data lives in RDS (`infra/deploy/aws/compose.yml`, `ZITADEL_DATABASE_POSTGRES_DSN` to `DB_HOST`), so
no `down -v` resets the label policy. `provision.sh:113-125` uploads a logo only when the preview's `logoUrl` or
`logoUrlDark` is empty. Re-running provisioning on the dev environment therefore keeps the old leading-dot logos. The
story's "re-provision the AWS dev environment" gives no procedure, and `infra/deploy/runbook.md` has no logo step.

Fix: name the human procedure in the story's Verify and in the runbook, as a docs change. For example: remove the
instance label-policy logo and dark logo through Zitadel's Admin API, then re-run provisioning, which uploads and
activates. Alternatively, lift the story 2.6 shortcut so provisioning always uploads both logos, as an infra change
added to Touches. **Unverified:** I did not check the exact Zitadel v4.20 Admin API route for removing a label-policy
logo.

### P7 [story] medium: the phone sheet has no height limit, so its top is cut off at 200% zoom

Criterion 7 turns the dialog into a bottom sheet below 768px. A `fixed` sheet anchored at the bottom, with no
`max-height` and no inner scroll, grows upward past the viewport. Rough sum for the content: title, description, label,
textarea, counter, hairline, two stacked 48px buttons and `p-6`, about 450px before the error summary or problem
notice is added. At 200% zoom on a 1280 x 720 screen the viewport is 640 x 360 CSS px. Whatever overflows at the top is
unreachable. The page behind still scrolls (the `alert-dialog.tsx:7` scroll-lock shortcut), but the sheet does not.
This breaks WCAG 2.2 SC 1.4.10, which EXPERIENCE's Accessibility Floor and this story's Verify require. Today's centred
dialog has the same gap; the sheet keeps it.

Fix: criterion 7 adds a viewport height cap with inner scrolling on the content in both layouts (for example
`max-h-dvh overflow-y-auto`, tokens only). QA opens the dialog at 320px and at 200% zoom with the error summary and the
problem notice showing, and reaches both buttons and the title. Source: WCAG 2.2 SC 1.4.10
(<https://www.w3.org/WAI/WCAG22/Understanding/reflow>).

### P8 [story] medium: the 768px and 1024px bounds have no check on either side

Criterion 7 switches the dialog at 768px. Criterion 5 stacks card, timeline and guidance below 1024px. Verify checks
only 360px and 1280px, and the only layout check in the code is `dispute-detail.spec.ts:345-348` (two columns at
1280). This is the same gap as `ui-lists` P5.

Fix: Tests and commits has QA check 767px (sheet) and 768px (centred), and 1023px (stacked, in the order card,
timeline, guidance) and 1024px (two columns), on a rejected dispute.

### P9 [story] low: criterion 4 leaves out parts of DESIGN Timeline, and the entry with no line

DESIGN Timeline also specifies:

- earlier entries: a 12px outlined dot and the line in 600 weight;
- the latest entry's line in navy 800;
- the 2px `border` rail;
- the note **after** the actor and time, in a muted quote block. Today the note comes before them
  (`Timeline.tsx:76-80`), and the mockups and EXPERIENCE ("status line, actor …, time …, note") put it last.

With the badge text gone, an entry whose action or outcome this build doesn't know has no line
(`DisputeDetailScreen.test.tsx:605`, AD-13). The story doesn't say what such an entry shows.

Fix: add these parts to criterion 4, including the reading order line, actor and time, note. Add "an entry with no
copy-deck line shows only the actor and time and stays in the list". The `:605` test keeps asserting that no raw value
is shown.

### P10 [story] low: criteria 2 to 8 name no test, and the absence checks need a scope

Only criterion 1 names a test. Name the proofs:

- 2: extend the `:628` `it.each` so the sentence and the button are both present or both absent per status;
- 3: the existing banner test at `:392`, plus `svg.lucide-circle-check`;
- 4: line text per action, no separator character;
- 5: the guidance `h2` by name, two `li`;
- 6: P2;
- 7: the dialog tests at `:688-760` and `:928` pass unchanged; the sheet is QA's;
- 8: `routing.test.tsx`, the caption text and an `aria-hidden` Lock svg.

Scope matters for the absence checks. The card badge reads "Resolved · Upheld" (with " · ") and shows the badge texts,
so "no ' · '" and "no badge text" must be asserted within the Timeline region's list items, never on the body.

### P11 [story] low: the e2e list in Tests and commits points at the wrong specs

- `operations-actions.spec.ts:366` is the `/disputes` list badge test, and `seeded-disputes.spec.ts` checks list rows
  only. This story changes neither screen.
- `journeys/uj-2.spec.ts:85` is missing. It expects the last timeline item to contain `'Withdrawn'`, and it fails once
  the badge text goes.
- The detail asserts that change are `dispute-detail.spec.ts:354`, `:362`, `:421` and `journeys/uj-1.spec.ts:112`,
  `:114`. Name them.
- `e2e/run-regression.sh:45-47` already runs `down -v` and `up --build`, so QA's hosted-page screenshots can follow the
  regression run without a second reset.

### P12 [story] low: housekeeping

- **`<time>` element.** "Disputed on" could carry `<time dateTime={createdAt}>`, as the disputes list does
  (`DisputeRow.tsx:40`).
- **List semantics.** Criteria 4 and 5 rely on ordered-list semantics. Tailwind Preflight removes list styles, and
  its docs advise `role="list"` so VoiceOver keeps announcing the list
  (<https://tailwindcss.com/docs/preflight#lists-are-unstyled>). No list in `frontend/src` has it today. **Unverified**
  on current Safari. Either add it to the two lists here or log it.
- **Glyph grep.** Verify's bracket grep matches bytes under a C locale, so it would flag the curly quotes in
  `Timeline.tsx:76`. `ui-tokens-icons` criterion 6 adds the deterministic `theme.test.ts` case; point Verify at that
  case instead.
- **Spec number.** The front matter carries `id: 8`. AGENTS.md (Ticket numbering) says feature specs have no number,
  and the sibling specs share this.
- **PNG metadata.** The regenerated PNGs carry no text or EXIF chunks, as story 2.6's were checked (`2.6-review.md:3`).

## Checked, no point

- Criterion 3: `ui-notices-states` criterion 1 leaves the success `Alert` with no `role`, so the banner test at `:392`
  (no `role="status"`, `role="alert"` or `aria-live` around the banner) still holds, and "Dispute submitted" is
  announced only by `ReviewScreen.tsx:107`.
- `formatDayWithYear` exists (`format.ts:40`, SAST, rollover tested). `createdAt` is a required field of
  `DisputeResponse`, and the default fixture's `createdAt` (2026-10-30) differs from its entries' day.
- Stacking below 1024px already holds (`lg:grid-cols-2`, `DisputeDetailScreen.tsx:252`), and the guidance sits in the
  timeline column.
- `components/ui/alert-dialog.tsx` has one consumer, `WithdrawDialog`, so the hairline and the sheet reach no other
  dialog.
- Focus return after the dialog (`returnFocus`, `DisputeDetailScreen.tsx:116-118`) is untouched by moving the button
  into the card foot, provided the ref stays on the button.
- Locally, Compose mounts the two PNGs read-only one file at a time (`docker-compose.yml:156-160`), so a reset picks up
  new files with no image rebuild. The guard lets the frontend builder write `frontend/` and QA write `e2e/`.
- Withdraw sentence: the card-foot sentence repeats the dialog's description, but every existing lookup of it is
  scoped to the dialog (`DisputeDetailScreen.test.tsx:694`, `dispute-detail.spec.ts:479`).

## Pass 1: correctness

The criteria are testable and tagged. The gaps are P1 (PRD), P2, P3, P4 and P7. No API, Result, ProblemDetails,
ownership, stored-event, `Location` or timestamp surface is touched, so those items don't apply.

## Pass 2: simplicity and standards

| Mechanism | Standard | Assessment |
|---|---|---|
| Disputed on | `Intl.DateTimeFormat` via the existing `formatDayWithYear` (MDN) | Fits; P3, P4 |
| Success banner | shadcn Alert, WCAG 2.2 SC 4.1.3 | Fits (role settled in area 2) |
| Timeline, guidance | Ordered lists, WCAG 2.2 SC 1.3.1 | Fits; P9, P12 (Preflight) |
| Skeleton | shadcn Skeleton, MDN `aria-busy` | Fits; P2 |
| Phone sheet | Radix Alert Dialog with responsive classes | Fits; P7, P8 |
| Assurance line | Lucide icon beside text | Fits |
| Wordmark PNGs | W3C PNG, Zitadel label-policy assets | Drawing mechanism unnamed; P5. AWS refresh unspecified; P6 |

The story adds no wrapper, layer, option or interface. Responsive classes on the one existing `AlertDialogContent` are
simpler than adding a Sheet or Drawer component.

## Security checklist

- AuthN/Z, browser session, linking, data and audit: not touched. The story is presentation only, and the note still
  renders as plain text (`:573`).
- Headers: no CSP change. Build the skeleton widths with Tailwind classes, not the mockup's inline `style`. The
  uploaded logos are served from the identity origin as in story 2.6, so no `img-src` change is needed.
- Supply chain: no package is added. The suggested drawing tool (Playwright) is already pinned in `e2e/`. The logos
  stay PNG only (2.6 S3); see P12 on metadata.
- The AWS re-provisioning is a deploy, left to a human (the guard prompts). Its procedure is P6.

## Decisions needed

- P1: FR-17's per-entry status against criterion 4's copy-deck-only lines.
