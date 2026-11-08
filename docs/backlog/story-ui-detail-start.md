---
id: 8
type: story
title: "Dispute detail shows when it was disputed, and the start page reassures"
sources: [docs/ux/HANDOFF.md, docs/ux/DESIGN.md, docs/ux/EXPERIENCE.md, docs/ux/mockups/dispute-submitted.html, docs/ux/mockups/dispute-under-review.html, docs/ux/mockups/dispute-rejected.html, docs/ux/mockups/dispute-withdraw.html, docs/ux/mockups/dispute-loading.html, docs/ux/mockups/start.html, docs/ux/mockups/app.css, docs/conventions/frontend.md, docs/prd/prd.md]
companions: [docs/verification/ui-detail-start-prebuild-review.md, docs/verification/decisions.md]
after: [story-ui-notices-states]
refined: true
hitl: true
risk: low
decisions: settled
---

# Dispute detail shows when it was disputed, and the start page reassures

## Description

The sixth area of the UI refresh (HANDOFF section 6, step 6). In `features/disputes/detail/DisputeDetailScreen.tsx`
the summary has no "Disputed on" row, Withdraw sits mid-card with no consequence sentence, the "Dispute submitted"
banner has no icon, the rejected guidance is one paragraph (line 29), and the loading skeleton is two generic bars.
`Timeline.tsx` puts the note before the actor and time and joins them with " · ". The withdraw dialog has no hairline
and no phone layout, and the start page has no assurance line. "Disputed on" comes from the detail response's
`createdAt` (decided 2026-11-08, HANDOFF 3.10): the contract guarantees it, while the timeline's order is a mapping
convention. Every timeline entry keeps its status label (PRD FR-17, decided 2026-11-08): the copy-deck sentence sits
beside it, and the upheld and rejected lines carry their outcome sentence.

The hosted sign-in and sign-up pages still show the old leading-dot wordmark, since `ui-tokens-icons` moved the dot
only in the SPA header. This spec regenerates the two wordmark PNGs (HANDOFF 3.1) and re-provisions the local stack.
The AWS environment is not deployed yet (`docs/production-path.md`); once it is, a person refreshes its logos by the
procedure in Verify, because that is a deploy.

- Tier: Feature. Owner: frontend. The wordmark PNGs are under `frontend/src/theme/`, which the frontend builder
  writes. The builder draws them with a throwaway script run from `e2e/` (QA's path), where Playwright is installed;
  the builder commits nothing under `e2e/`. `provision.sh` is unchanged, QA runs the local re-provisioning, and a
  human runs the AWS one. Stacking: on `feature/ui-notices-states`; builds beside `ui-lists`, `ui-linking` and
  `ui-raise-review` (no shared file). The detail skeleton is here, not in `ui-lists`.
- Presentation only: no API, route, auth, session, idempotency or retry change; `createdAt` is already in
  `DisputeResponse`, so no API field is added.
- Covers: HANDOFF 3.1 (the hosted pages), 3.10, 3.11, 3.12, 3.13 (detail); DESIGN Components (Wordmark, Dispute
  summary card, Timeline, Rejected guidance, Start page); EXPERIENCE Component Patterns (Timeline, Withdraw), State
  Patterns (Just submitted, Loading), Responsive & Platform, Accessibility Floor; PRD FR-17, FR-18, NFR-CODE-6.

## Inputs

No new request inputs: this spec is presentation only, and no request body, query, header or route parameter
changes. UI inputs are unchanged: the withdraw note stays optional, at most 500 characters after trimming.

## Criteria

1. **"Disputed on" comes from `createdAt`, in DESIGN's row order** (HANDOFF 3.10; DESIGN Dispute summary card; FR-17)
   **Given** a dispute whose `createdAt` is `2026-10-25T22:30:00Z` (00:30 SAST on 26 Oct) and whose first timeline
   entry's `occurredAt` is `2026-10-27T08:00:00Z` (a different South African day)
   **When** the summary renders
   **Then** a row "Disputed on" reads "26 Oct 2026" in a `<time dateTime={createdAt}>`, formatted in South African
   time by `formatDayWithYear` in `features/transactions/format.ts`, and "27 Oct 2026" appears nowhere in the summary
   **And** the summary rows run in this order: Reason, then (Wrong amount only) Transaction amount and Expected amount,
   then Transaction date, then Disputed on
   **And** unit tests prove both: the date case above, and the `dt` order on a wrong-amount dispute. Mechanism: the
   existing formatter over `Intl.DateTimeFormat`. Source: MDN, `Intl.DateTimeFormat`
   (<https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat>).

2. **Withdraw sits at the card's foot with its consequence** (HANDOFF 3.10; EXPERIENCE Withdraw; FR-18)
   **Given** an open dispute
   **When** the summary renders
   **Then** after a hairline at the card's foot comes "Changed your mind? You can withdraw while the dispute is open.
   You can't dispute this transaction again after you withdraw." then the Withdraw button; closed disputes show
   neither; the focus-return ref stays on the button, so focus return after the dialog is unchanged
   **And** the `it.each` over statuses in the Withdraw button block (`DisputeDetailScreen.test.tsx:628`) asserts the sentence and the button are
   both present or both absent per status. Mechanism: theme tokens. Source: WCAG 2.2 SC 2.4.3
   (<https://www.w3.org/WAI/WCAG22/Understanding/focus-order>).

3. **The just-submitted banner is a success notice** (HANDOFF 3.10; EXPERIENCE Just submitted)
   **Given** the detail opened straight after a submit
   **When** it renders
   **Then** "Dispute submitted" is a success `Alert` with `CircleCheck`, static, with no second announcement
   **And** the banner test (`:392`) passes and also finds `svg.lucide-circle-check` in the banner. Mechanism: shadcn
   Alert (area 2). Source: <https://ui.shadcn.com/docs/components/alert>; WCAG 2.2 SC 4.1.3
   (<https://www.w3.org/WAI/WCAG22/Understanding/status-messages>).

4. **The timeline keeps each status and adds the copy-deck sentence** (HANDOFF 3.10; DESIGN Timeline; EXPERIENCE
   Timeline; FR-17; decision 2026-11-08)
   **Given** `Timeline.tsx`
   **When** it renders
   **Then** it is a flat surface (`card`, hairline, `rounded-lg`) with the `h2` "Timeline" and an ordered list on a
   2px `border` rail; each entry reads, in this order: its status label as the customer sees it ("Submitted",
   "Under review", "Under review (prioritised)", "Resolved · Upheld", "Resolved · Rejected", "Withdrawn"), the
   copy-deck sentence beside it (EXPERIENCE's Timeline line, so "The bank agreed with you." and "The bank didn't
   uphold your dispute." on the resolved lines), then the actor and the time with no separator character, then any
   note in a muted quote block; earlier entries have a 12px outlined dot and the sentence in 600 weight; the latest
   entry has the 16px `primary` dot with a 4px `primary-subtle` halo and the sentence in navy 800; open disputes end
   with "We'll update the timeline as your dispute moves." below a hairline inside the surface; `<time datetime>`
   stays
   **And** an entry whose status, action, actor or outcome this build does not know shows no label or sentence for
   that value, never the raw value, keeps whatever else it has (at least the time), and stays in the list (AD-13)
   **And** unit tests assert, within the Timeline region's list items (never on the page body, whose card badge
   holds " · "): the label and sentence per action, the order sentence, actor and time, note, and no " · " between
   actor and time; the unknown-value test (`:605`) still finds no raw value. The e2e status assertions that prove
   FR-17 pass unchanged: `dispute-detail.spec.ts:354` ("Submitted"), `:421` ("Rejected"), `journeys/uj-1.spec.ts:112`
   ("Submitted") and `journeys/uj-2.spec.ts:85` ("Withdrawn"). The timeline mockups predate the decision and show the
   sentence only; DESIGN wins (HANDOFF, sources of truth). Mechanism: ordered list semantics. Source: WCAG 2.2 SC
   1.3.1 (<https://www.w3.org/WAI/WCAG22/Understanding/info-and-relationships>).

5. **Rejected guidance is its own surface** (HANDOFF 3.10; DESIGN Rejected guidance)
   **Given** a rejected dispute
   **When** the detail renders
   **Then** below the timeline a flat surface has the `h2` "If you disagree with this outcome" and an ordered list of
   two steps: "You can complain to the bank first." and "If you're still not satisfied after the bank has answered
   your complaint, you can take it to the National Financial Ombud Scheme South Africa, an independent service that
   is free for customers."; below 1024px card, timeline and guidance stack in that order
   **And** a unit test finds the guidance `h2` by name and two `li` in its list. Mechanism: responsive variants.
   Source: Tailwind v4, Responsive design (<https://tailwindcss.com/docs/responsive-design>).

6. **The detail skeleton matches the layout and keeps the shared `h1`** (HANDOFF 3.13; EXPERIENCE State Patterns,
   Loading)
   **Given** the first load of `/disputes/:id`
   **When** the skeleton shows
   **Then** it is a skeleton summary card plus a timeline surface with three entries (a dot and two bars each); the
   skeleton card holds the real `h1` "Dispute" as text above its bars, the same element as in every other state, and
   is not `aria-hidden` (the mockup's title bar and its `aria-hidden` split are not followed); the three placeholders
   stay `li` inside the `aria-busy` element, with their dots `aria-hidden`; bar widths are Tailwind classes, not
   inline `style`
   **And** the loading test (`:156`), the loading axe test (`:459`) and e2e `dispute-detail.spec.ts:313` (title
   "Dispute · DisputeDesk") pass unchanged. Mechanism: shadcn Skeleton, `aria-busy`. Source:
   <https://ui.shadcn.com/docs/components/skeleton>; MDN, `aria-busy`
   (<https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA/Reference/Attributes/aria-busy>).

7. **The withdraw dialog has a hairline, a phone sheet and a height cap** (HANDOFF 3.11; EXPERIENCE Withdraw;
   EXPERIENCE Accessibility Floor)
   **Given** `WithdrawDialog.tsx` and `components/ui/alert-dialog.tsx`
   **When** the dialog opens
   **Then** the buttons sit below a hairline; below 768px the content is a bottom sheet through responsive classes;
   in both layouts the content is capped at the viewport height and scrolls inside (for example `max-h-dvh
   overflow-y-auto`, tokens only), so the title and both buttons stay reachable at 320px and at 200% zoom with the
   error summary and the problem notice showing; initial focus is still "Keep dispute", Esc still keeps the dispute,
   and success still announces "Dispute withdrawn" and moves focus to the `h1`
   **And** the dialog tests (`:688` to `:760`, `:928`) pass unchanged; the sheet, the 767px and 768px bounds and the
   zoom check are QA's (Verify). Mechanism: Radix Alert Dialog with responsive classes. Source:
   <https://www.radix-ui.com/primitives/docs/components/alert-dialog>;
   <https://ui.shadcn.com/docs/components/alert-dialog>; WCAG 2.2 SC 1.4.10
   (<https://www.w3.org/WAI/WCAG22/Understanding/reflow>).

8. **The start page carries the assurance line** (HANDOFF 3.12; DESIGN Start page)
   **Given** `pages/StartPage.tsx`
   **When** it renders
   **Then** under the Sign in and Create a login buttons a 13px caption with `Lock` reads "You'll confirm it's you
   with your ID number, account number and a one-time code."
   **And** `app/routing.test.tsx` asserts the caption text and an `aria-hidden` Lock svg. Mechanism: Lucide icon beside
   text. Source: <https://lucide.dev/guide/packages/lucide-react>.

9. **The hosted pages' wordmark puts the dot after the name** (HANDOFF 3.1; DESIGN Wordmark; story 2.6 criterion 2)
   **Given** `frontend/src/theme/wordmark.png` and `frontend/src/theme/wordmark-dark.png`
   **When** the frontend builder regenerates them
   **Then** each is drawn as DESIGN Wordmark says: "DisputeDesk" in Nunito Sans 800 followed by the dot after the
   name, on a transparent background; light in navy #14287a with a cobalt #2b57c9 dot, dark in #c9d6ff with a
   #8caaf7 dot; each is 620 x 128 px, 8-bit RGBA, as today, drawn at 4x the 20px name size (name 80px, dot 32px,
   gap 8px, dot on the baseline), whole within the canvas
   **And** they are drawn with Playwright's `page.screenshot({ omitBackground: true, clip })` from a throwaway script
   that the builder runs with `e2e/` as its working directory (Playwright is pinned in `e2e/package.json`); the page
   loads the committed `frontend/src/theme/fonts/nunito-sans-latin-800-normal.woff2` through `@font-face`; the script
   is not committed, nothing under `e2e/` changes, and no drawing tool is added to any image or `package.json`
   **And** story 2.6's rules hold: PNG only, each at most 512 KiB, no text or EXIF metadata chunks (no `tEXt`, `zTXt`,
   `iTXt` or `eXIf`); the builder's report shows `file` output for both files (PNG, 620 x 128, RGBA), each file's byte
   size and its chunk types; QA's provisioning log shows "logo uploaded" and "dark logo uploaded" with no 4xx
   **And** QA re-provisions locally with `docker compose down -v` and then up, because `provision.sh` uploads a logo
   only when none exists (its `# shortcut:`), and screenshots the hosted sign-in and sign-up pages in light and dark
   beside `mockups/sign-in.html` and `mockups/sign-up.html`: the dot follows the name and the console shows no CSP
   violation. Mechanism: Playwright screenshot to PNG; PNG files uploaded to the identity provider's label policy
   (story 2.6). Source: Playwright, `page.screenshot` (<https://playwright.dev/docs/api/class-page#page-screenshot>);
   W3C PNG Specification (<https://www.w3.org/TR/png-3/>); Zitadel label policy assets, as story 2.6 cites them.

## Boundaries

- Presentation only: no change to API calls, the generated client, routes, auth, session, idempotency, retry, query
  keys or telemetry; the withdraw request, its 409-as-success handling and the dialog's focus rules are unchanged.
- Theme tokens only: no raw hex or px value in a component. Icons: Lucide, 16px unless HANDOFF names another size,
  2px stroke, `currentColor`, `aria-hidden="true"`.
- The note still renders as plain text: no markup, no autolinking.
- Touches (under `frontend/src/`): `features/disputes/detail/DisputeDetailScreen.tsx` (not `NotFoundCard`, which area
  2 owns), `Timeline.tsx`, `features/disputes/withdraw/WithdrawDialog.tsx`, `components/ui/alert-dialog.tsx`,
  `pages/StartPage.tsx` (the assurance line only), `theme/wordmark.png`, `theme/wordmark-dark.png`, and their tests.
- `infra/images/zitadel/provision.sh`, the label policy generator and every other `infra/` file are unchanged; only
  the two PNG files change.

## Tests and commits

- QA updates only the detail assertions this story changes: the separator assertions `dispute-detail.spec.ts:362`
  ("You · …"), `journeys/uj-1.spec.ts:114` ("You ·") and `journeys/uj-2.spec.ts:87` ("You ·"), and any other detail
  assertion on the " · " separator or the note's position. The FR-17 status assertions (`dispute-detail.spec.ts:354`,
  `:421`, `journeys/uj-1.spec.ts:112`, `journeys/uj-2.spec.ts:85`) stay as they are. `operations-actions.spec.ts` and
  `seeded-disputes.spec.ts` check the lists, which this story does not change.
- `e2e/run-regression.sh` already runs `down -v` and `up --build`, so QA's hosted-page screenshots follow the
  regression run with no second reset.
- The pull request holds two commits: the presentation change, then `test(ui-detail-start): update assertions for the
  UI refresh` with every unit-test and e2e assertion update. The squash merge joins them on main.

## Verify

- Builder, in `frontend/`: `npm run typecheck`, `lint`, `format`, `test` and `build` pass; the glyph check is the
  `theme.test.ts` case that `ui-tokens-icons` criterion 6 adds (not a byte grep, which flags curly quotes); the diff
  has no raw hex or px in a component; the report carries criterion 9's `file` output, byte sizes and chunk types.
- QA, holding the `stack` lock: `e2e/run-regression.sh` passes with axe on every spec that runs it; with the
  Playwright MCP each detail state, the withdraw dialog and the start page match their mockups at 360px and 1280px in
  light and dark (the timeline as DESIGN describes it, with each status label); on a rejected dispute, 767px shows the
  sheet and 768px the centred dialog, 1023px stacks card, timeline and guidance in that order and 1024px shows two
  columns; the dialog at 320px and at 200% zoom, with the error summary and the problem notice showing, lets the
  title and both buttons be reached; no horizontal scroll at 320px or 200% zoom; every interactive element is at
  least 44px tall; keyboard order is unchanged; after the regression run's `down -v` and up, the hosted sign-in and
  sign-up pages show the new wordmark and the provisioning log lines of criterion 9.
- Human, once the AWS environment is deployed (it is not yet; `docs/production-path.md`): in AWS, Zitadel's data
  lives in RDS, so no `down -v` resets the label policy, and `provision.sh` keeps existing logos. Remove both
  instance label-policy logos (light and dark) through Zitadel's Admin API, then re-run provisioning, which uploads
  and activates the new PNGs; check the hosted sign-in page in light and dark. The exact Admin API routes are to be
  confirmed against Zitadel's documentation for the deployed version at that time. This is a deploy, outside the
  pipeline.

## Notes

- Follow-up (not a criterion; this story stays frontend-only): add the AWS logo refresh above to
  `infra/deploy/runbook.md` as an infra docs change, with the confirmed Admin API routes.
- Assumption: the front matter keeps `id: 8`, though AGENTS.md (Ticket numbering) gives feature specs no number,
  because every sibling `ui-*` spec carries an `id` (3 to 8); removing it is one change across all of them.
- Assumption: Tailwind Preflight removes list styles, and its docs advise `role="list"` so VoiceOver keeps announcing
  the list (<https://tailwindcss.com/docs/preflight#lists-are-unstyled>). No list in `frontend/src` has it today, and
  current Safari behaviour is unverified, so this story does not add it; it goes to `docs/verification/lows.md` as a
  low for the lows batch.

## Sofia's refine

Sofia, 2026-11-08, on `docs/verification/ui-detail-start-prebuild-review.md` (one `[decision]` point, answered; every
`[story]` point fixed):

- P1 medium `[decision]`, per-entry status: answered in `decisions.md` (2026-11-08), keep FR-17. Criterion 4 keeps
  each entry's status label with the copy-deck sentence beside it and names the four e2e status assertions that
  pass unchanged. DESIGN Timeline and EXPERIENCE Component Patterns, Timeline, now say the same; HANDOFF 3.10 already
  did. EXPERIENCE's status table ("badge text; timeline heading") and UJ-4 already agreed. The copy-deck sentence
  stays on every entry, not only the resolved ones: EXPERIENCE gives each status a Timeline line, and the current
  code and e2e checks show both.
- P2 medium, shared `h1`: criterion 6 keeps the real `h1` "Dispute" in the skeleton card, not `aria-hidden`, the
  placeholders as `li` under `aria-busy`, and names `:156`, `:459` and the e2e title check.
- P3 medium, `timeline[0]`: criterion 1's first entry falls on a different South African day, and that day must not
  appear.
- P4 medium, row order: criterion 1 gives the full order and a `dt` order test on a wrong-amount dispute.
- P5 medium, PNG mechanism: criterion 9 names Playwright's `page.screenshot` with `omitBackground` from a throwaway
  script run in `e2e/` by the frontend builder (nothing committed there), the committed Nunito Sans 800 font, 620 x
  128 RGBA at 4x, no metadata chunks, and replaces the wrong CI claim with the builder's `file` output, sizes and
  chunk types plus QA's provisioning log lines.
- P6 medium, AWS refresh: AWS is not deployed yet; Verify names the human procedure for when it is (remove both
  label-policy logos through Zitadel's Admin API, then re-run provisioning), with the route to be confirmed then.
  The runbook line is a follow-up in Notes, so no `infra/` file is touched.
- P7 medium, sheet height: criterion 7 caps the content at the viewport height with inner scrolling in both layouts,
  and QA checks 320px and 200% zoom with the error summary and the problem notice.
- P8 medium, breakpoints: Verify has QA check 767 and 768px, and 1023 and 1024px, on a rejected dispute.
- P9 low, timeline parts: criterion 4 adds the dots, weights, rail, reading order and the entry with unknown values.
- P10 low, tests: each criterion names its test; the absence checks are scoped to the Timeline list items.
- P11 low, e2e list: Tests and commits names the separator assertions that change and the status ones that stay,
  drops the list specs, and uses the regression run's reset.
- P12 low, housekeeping: `<time dateTime>` on "Disputed on" (criterion 1); `role="list"` logged as a low (Notes);
  the glyph check points at `theme.test.ts`; `id: 8` kept for consistency with the sibling specs (Notes); no
  metadata chunks (criterion 9).
