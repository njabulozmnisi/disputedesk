# ui-raise-review: QA record

Commit tested: `2f897c3` plus the uncommitted tree (ui-tokens-icons fix round 3, ui-notices-states fix round 1, this story after its fix round 1) in
`.claude/worktrees/agent-a0a85c91ff0f853f5`. Status: **fail** (one medium finding, below). Evidence is synthetic demo data only; no `Authorization`
header, cookie or ID number is recorded here (the session and antiforgery values were never read out).

## Setup

- The base's two e2e edits were copied from `agent-ae1ed41bc1abcb91e` (`spa-disputes-history.spec.ts`, `spa-transactions.spec.ts`).
- `e2e/tests/dispute-raise.spec.ts` lines 311, 386, 450, 456, 735: lookup is now `{ name: 'Change your dispute', exact: true }` (every one, no check weakened).
- New `e2e/tests/spa-raise-review-axe.spec.ts` (regression project): axe (wcag2a, 2aa, 21a, 21aa, 22aa) on the raise form, its error state, Wrong amount with the
  difference, review, and review after a stubbed 503 (exactly one `alert`), at 320 light, 360 light and dark, 1280 light and dark. 5 passed, 0 violations. This
  adds the dark and phone widths that `dispute-raise.spec.ts` does not run.
- `npm ci` and `npm run lint` in `e2e/`: lint clean (also after adding the new spec).

## Suites (under `scripts/test-lock.sh run stack`, OTLP ports 14318/14317 because 4318 is taken by another process)

| Project | Result |
|---|---|
| linking-rate-limits | 7 passed |
| linking-screens | 5 passed |
| linking-queue | 1 passed |
| linking-verify-a / b / c | 6 / 3 / 2 passed |
| regression | 240 passed, 1 skipped (the suite's own skip, not a story spec), includes `dispute-raise`, `spa-transactions`, `spa-disputes-history` with their axe checks |
| every group's customer D check | within the 3-OTP cap |
| journeys (own fresh stack) | 5 passed (UJ-1, 2, 5a, 5b, 5d) |

The journeys log prints two `EVIDENCE` axe lines for Zitadel's hosted register and verify pages (`link-name`); they are not this story's pages.
The stack was stopped (`docker compose stop`) and the lock released after the walkthrough.

## Walkthrough, per criterion (Playwright MCP, seeded customer A, 360 and 1280 px, light and dark)

All measured with computed styles and bounding boxes; screenshots in `/Users/njabulomnisi/Projects/Dojo/DisputeDesk/.playwright-mcp/` (raise-, wrong-, review-, refusal-*.png).

1. Step (AC1): raise shows "Step 1 of 2: reason and note" above the h1 (all four combinations); review shows "Step 2 of 2: review and submit" above the h1. After a 503 the step and card stay;
   after a 409, 422 and 404 refusal there is no step, no card, h1 "Review your dispute" stays and only "Back to transactions" (48 px) and Copy (44 px) remain. The 409 was also produced for real (see Hops).
2. Raise surface (AC2): `<form>` is `bg-card` (light rgb(255,255,255), dark rgb(31,41,51)), 1px hairline (rgb(225,228,234) / rgb(50,61,75)), radius 24px; the transaction block is inside it,
   fill rgb(244,245,247) light / rgb(18,23,30) dark, border 0, radius 12px; text unchanged ("Card 492181•• •••• 4417", amount right).
3. Difference (AC3): "You're disputing R37.23" (expected R50.00 of R87.23) is 14px, weight 700, heading colour (rgb(20,40,122) light, rgb(201,214,255) dark), `id=expectedAmount-disputing`, live-updates.
4. Actions (AC4): 1px hairline above the buttons; Continue and Cancel 48px tall, Cancel a 9999px secondary pill; full width (294px at 360, 328px on review) below 768px, 116/100 and 163/100 px at 1280. Review Cancel inert while pending is covered by the `dispute-raise` spec (passed).
5. Review surface (AC5): `region "Your dispute"` (bg-card, hairline, 24px radius); dl columns `144px 1fr` (9rem) at 360 and 1280, hairlines between rows, `overflow-wrap: anywhere`; rows Transaction, Date, Card, Reason, Note.
   The accessibility tree reads the Card row as "card ending 4417" (printed number hidden). The caption outside the dl is gone.
6. Change (AC6): link "Change your dispute" (no `aria-label`, name from content), 64x44 px, top right of the section; `getByRole('link', { name: 'Change your dispute', exact: true })` matches exactly 1; returns to the form with values kept (dispute-raise spec).
7. Notice (AC7): the only-once sentence has no `role`, no `status`, no `aria-live` on itself or any ancestor; after a forced 503, exactly one `alert` (the problem notice, with Reference and a 44px Copy). 409, 422, 404 each show exactly one alert and not the static notice.

Keyboard order, raise: reason radio, note, Continue, Cancel; review: Change your dispute, Submit dispute, Cancel (header links follow). Target sizes: every interactive element >= 44 px except the six Radix reason radio buttons (24 px, accepted, pre-existing: low).
Reflow: no horizontal scroll at 320 px and at 640 px (200% browser zoom of 1280) on raise, Wrong amount, review (with a 90-character unbroken word in the note) and review after a 503.

## Four hops, happy path (raise TXN-A-0029 "Unrecognised" with a note, customer A)

- Browser: review, Submit, lands on `/disputes/<id>` (detail "Steers Braamfontein").
- Network: `POST /api/v1/disputes` with `X-XSRF-TOKEN` present and one `Idempotency-Key`; body `{accountRef, transactionId, reason, note, expectedAmount: null}`; 201 `{disputeId, customerStatus: "submitted", reason: "unrecognised", transaction{...}, disputedAmount R87.23, timeline[1]}`, no other customer's data.
- Database (read-only role): `Disputes` row status Submitted, Version 1, customer CUS-000001, 87.23; `StoredEvents` one `DisputeRaised` (Customer actor, version 1); `OutboxMessages` row with the same ID as the stored event, `disputedesk.dispute.raised.v1`, Sent, Attempts 0, no LastError; no audit row for the success.
- Events: topic `disputedesk.disputes`, key = dispute ID (partition 2), headers `content-type: application/cloudevents+json` and `traceparent` whose trace ID equals the stored event's `TraceId`; CloudEvent `id` = the outbox ID, data `{disputeId, reason, status, occurredAt, disputeVersion}`, no note or personal data.

Unhappy: a real 409 (the same transaction was raised first by a second request with a different key, then the UI submit) returned `409 application/problem+json` type `already-disputed`, one `Disputes` row, one stored event, and an audit row `Raise | Rejected | Customer` for the dispute whose trace prefix equals the Reference shown (b459e305). RFC 9457 shape is correct. Another customer's dispute 404 and a resolved dispute 409 on withdraw are covered by journeys UJ-5a and UJ-5b (passed).

## Findings

| # | Rank | Finding |
|---|---|---|
| 1 | medium | The review `dl` label column is `9rem`, which scales with the root font size. With text-only scaling the value column collapses: at 360 px and 150% the value column is 22 px wide, at 360 px and 200% it is 0 px and the page scrolls horizontally (452 px); at 320 px and 150% it is 0 px with a 339 px scroll width. Real browser zoom (320 px, 640 px) is fine. Standard: WCAG 2.2 SC 1.4.4 Resize Text (content lost at 200% text) and SC 1.4.10 Reflow. Criterion 5 asks for 9rem "at every width", so this is a spec and standard conflict: the spec needs a bound such as `grid-cols-[minmax(0,9rem)_minmax(0,1fr)]` with a share cap (for example `min(9rem,40%)`), a decision for the builder and Sofia. |
| 2 | low | Pre-existing, not this story: after an invalid expected amount is shown, correcting it and clicking Continue needs a second click. The `onBlur` revalidation removes the error and the "You're disputing" growth, so Continue moves 28 px up between pointer down and up (y 721 to 693). Enter works. `mode: 'onBlur'` and the summary are on main; for `lows.md`. |
| 3 | low | Pre-existing, accepted: the six Radix reason radio buttons are 24 px. |
| 4 | low | From the spec's Notes: `aria-disabled:opacity-70` on Change, Cancel and Button vs DESIGN Button; lows batch. |
| 5 | low | Note for the QA tooling: the Playwright MCP browser context can carry another session's `/api` route stubs (a first load showed a stubbed 503 with a sample trace ID); a fresh context was used and the findings above come from it. |

Accepted, not mismatches: items the caller listed (review head row `items-start`, Transaction `dd` not bold, Copy pill is the secondary Button).

## Not done / unverified

- No ZAP scan (CI's).
- The mockup comparison is by measured values and screenshots, not pixel diff.

## Re-run after fix round 2

Commit tested: 2f897c37b3b58b11119c6126123213f98defeb43 (HEAD; the uncommitted tree holds both bases, the story after round 2 and the e2e edits). Status: **pass**.
Stack on OTLP ports 14318/14317, because port 4318 is held by a non-Docker process on the machine (an editor). Two earlier attempts failed on that port before any test ran
(every group and the journeys errored with `bind: address already in use`); they are discarded, not results.

### New spec

`e2e/tests/spa-raise-review-textscale.spec.ts` (regression project; `npm run lint` in `e2e/` is clean). Review screen with a 90-character unbroken note (five rows), root font
size 150% and 200% at 320 and 360 px: every `dt` and `dd` in the review list is at least two font sizes wide, none scrolls inside itself (`scrollWidth <= clientWidth`) and none
leaves the list box; at 1280 px the first row's label column computes to exactly 9rem. Whole-page scroll width is not asserted (the known header residual).
My first version expected ten cells without a note and failed on my own count (eight cells); fixed by filling the note. It was not run against the old 9rem column (that needs an edit
to `frontend/`), so that it fails on the pre-fix layout is reasoned, not shown: the old value column was 0 px wide at 200%, which the width check rejects.

### Runs (under the `stack` lock, owner qa-ui-raise-review)

| Run | Result |
|---|---|
| `e2e/run-regression.sh`, seven groups, fresh stack each | linking-rate-limits 7, linking-screens 5, linking-queue 1, linking-verify-a 6, -b 3, -c 2 passed; every group's customer D check within the 3-OTP cap; regression 246 passed, 1 skipped (the suite's own skip), 4 failed: only my new spec's four scaling tests (the miscount above). Every other regression test passed, `dispute-raise`, `spa-raise-review-axe` (5), `spa-transactions` and `spa-disputes-history` included |
| journeys, own fresh stack | 5 passed (UJ-1, 2, 5a, 5b, 5d) |
| fixed spec on that journeys stack: `dispute-raise`, `spa-raise-review-axe`, `spa-raise-review-textscale` (regression project) | 21 passed (11 + 5 + 5) |

The full regression run is red as run (the four miscounted tests); the fixed spec's pass comes from the targeted run, not a second full regression run.

### Playwright MCP (seeded customer A, review for TXN-A-0012, 90-character unbroken note)

First row, computed columns:

| Viewport | Text | Label | Value | List width |
|---|---|---|---|---|
| 320 | 100% | 101.6 px | 136.4 px | 254 |
| 320 | 200% | 102.9 px | 122.4 px | 257 |
| 360 | 100% | 117.6 px | 160.4 px | 294 |
| 360 | 200% | 102.9 px | 122.4 px | 257 |
| 1280 | 100% | 144 px (9rem) | 302 px | 462 |
| 1280 | 200% | 178.8 px (40% cap) | 236.2 px | 447 |

Identical in light and dark (page background rgb(244,245,247) light, rgb(18,23,30) dark). In all twelve combinations no `dt` or `dd` is clipped or leaves the list.
Narrower than 9rem on phones at 100% is the decided behaviour (`min(var(--label-col), 40%)`), not reported. `--label-col` is still 9rem.
The rest of the first run still holds at 360 px, 100%: "Step 2 of 2: review and submit", one "Change your dispute" link, region "Your dispute", Submit 48 px tall, no horizontal scroll,
and the only live element is the app's empty `role=status` announcer (the only-once sentence is inside none). The criteria walk, the four hops and the refusals are covered again by
`dispute-raise` and the journeys (all passed); the network, database and event hops were not repeated by hand, since the delta is CSS only.

### Findings, this run

None high or medium. The first run's medium (value column collapsing under text scaling) is fixed: the smallest value column at 150% and 200% text is 122 px at 320 and 360 px.

| # | Rank | Finding |
|---|---|---|
| 6 | low | Known, predates the story, for `lows.md`: at 320 and 360 px with 200% text the signed-in header scrolls horizontally (WCAG 1.4.4, 1.4.10). Not asserted by the new spec. |
| 7 | low | Known, predates the story, for `lows.md`: at 320 px and 200% text the 2rem hero amount widens the card slightly. |
| 8 | low | Tooling: `e2e/run-regression.sh` fails every group with only a docker error when port 4318 is taken; it could check the port first. Parallel QA agents in one session that share a scratchpad `regression.log` overwrite each other's logs. |

Stack stopped (`docker compose stop`) and the lock released.
