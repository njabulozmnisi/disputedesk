# ui-lists: QA record

- Date: 2026-11-08
- Story: `docs/backlog/story-ui-lists.md` (origin/main). Review: `docs/verification/ui-lists-review.md`.
- Tested: commit `2f897c37b3b58b11119c6126123213f98defeb43` plus the uncommitted tree in
  `.claude/worktrees/agent-a72913a67169034e9` (two bases, this story, the e2e edits below).
- Result: **fail** on one medium finding (Q1: status badge clipped at 200% text size). Everything else holds.
- Lock: `scripts/test-lock.sh run stack qa-ui-lists`, OTLP ports 14318/14317 (4318 taken by another process). Stack stopped on release.

## Regression (earlier run, log `/private/tmp/claude-501/qa-ui-lists/regression.log`)

`e2e/run-regression.sh` under the lock: linking-rate-limits 7, linking-screens 5, linking-queue 1, linking-verify-a 6,
-b 3, -c 2 passed; regression group `240 passed, 1 skipped (9.5m)`; D within its OTP cap (the log ends with the D check, 3 sends).

## e2e edits

Reviewed `git diff -- e2e`. Not weakened: every `Dispute this transaction` lookup became `Dispute ${description}` with
`exact: true`, or `/^Dispute (?!status: )/` where the name is unknown or absence is asserted (uj-1:103, uj-2:118, which
closes review M1). The Tab-order test now asserts (`visited` contains Transactions and Disputes, and a Dispute link when one
is on the page) instead of skipping. The badge glyph checks use `svg.lucide-check` and `svg.lucide-x`. One residue, unchanged
from before: `spa-transactions.spec.ts` keeps its `if (disputeLinkExpected)` branch because a full run can use up A's
disputable rows; it no longer passes vacuously because the test asserts the link is reached whenever one is on the page.
`npm run lint` in `e2e/`: clean.

New: `e2e/tests/spa-lists-layout.spec.ts` (8 tests, runs in the `regression` project; read-only, customer A), the spec's
layout checks with bounding boxes. 767/768/1280px: column header hidden at 767 and visible at 768 and 1280 (always
`aria-hidden="true"`); every amount ends on one right edge (1px); at 768 and 1280 every action starts on one left edge; every
Dispute and badge link at least 44px tall; the Dispute link sits under the amount at 767; on `/disputes` one link per row,
chevron hidden at 767 and shown from 768, badge under the text at 767 and in a column at 768 and 1280; 320px: no horizontal
scroll on either page. Result: 8 passed. Lint clean.

## Journeys

`npx playwright test --project=journeys` on a fresh stack: UJ-1, UJ-2, UJ-5a, UJ-5b, UJ-5d, `5 passed (37.6s)`.
(UJ-1 and UJ-2 now use the new link names; UJ-5a is the other customer's dispute, UJ-5b the resolved dispute that cannot be withdrawn.)

## Browser walk (Playwright MCP, customer A, light and dark at 360, 767, 768, 1280)

Screenshots: `/private/tmp/claude-501/-Users-njabulomnisi-Projects-Dojo-DisputeDesk/56547641-7592-4409-a053-fd12e45c5593/scratchpad/uilists/` (`tx-*`, `dp-*`, `skel-*`, `zoom-*`, `focus-last-360.png`).

| Criterion | Observed | Holds |
|---|---|---|
| 1 lede, header | Lede "Your card and account transactions, newest first. You can dispute those from the last 90 days." exactly. Header display none at 360 and 767, grid `254px 144px 224px` at 768 and `366px 144px 224px` at 1280, `aria-hidden="true"` always | yes |
| 2 bands | `h2` 14px / 700, `muted-foreground` (light rgb(88,96,112), dark rgb(169,178,192)), fill = `background` (light rgb(244,245,247), dark rgb(18,23,30)); hairline above from 768 (1px), the first row's top border is the line below | yes |
| 3 columns | Row grid `366px 144px 224px`, gap 24px at 1280; `254px 144px 224px` at 768; two columns, gap 12px below 768; amounts right-aligned, actions left-aligned (e2e at 768 and 1280) | yes |
| 3 badge, Resolved | Badge link at least 44px; Resolved shows `Flag`, name "Dispute status: Resolved", no outcome | yes |
| 4 Dispute link | 44px tall, text "Dispute" + `ChevronRight` (aria-hidden), name "Dispute Steers Braamfontein", no underline until hover | yes |
| not-disputable hint | 8 hints on the first page, e.g. "Pending. You can dispute it once it has settled." with Info icon, no link | yes |
| 5 disputes row | 7 rows, one link each; name "Netflix.com Cancelled subscription, raised 7 Nov 2026 R199.00 Dispute status: Submitted"; merchant `primary` 700; grid `210px 144px 224px 20px` from 768 (chevron 16px, stroke 2, aria-hidden), two columns below with the chevron `display:none` at 360 and 767; hover: row fill rgb(244,245,247), only the merchant `P` is underlined, no badge element is | yes |
| 6 skeleton | Plain `div` panel with no ARIA attribute; hidden header (display none at 360, grid at 1280); `ul[aria-busy=true][aria-label="Loading transactions"]` with 5 `li` of 4 bars each and 2 `aria-hidden` band divs of 1 bar; no heading | yes (L1 `div` in `ul` is as the review says) |
| dark mode | Same structure and tokens; no unreadable text in the screenshots | yes |

### Text-only scaling (root font 200%, 320 and 360px)

- **Q1 (medium, builder): a status badge is clipped on the left of the panel.** At 200%, on `/transactions` at 360px the
  badge "Under review (prioritised)" starts 151px left of the panel edge; at 320px four badge links start left of it
  (Withdrawn -6px, Under review -31px, Under review (prioritised) -191px). On `/disputes` the "Under review (prioritised)",
  "Resolved · Upheld" and "Resolved · Rejected" badges are cut off at the left ("eview (prioritised)"). The badge is `nowrap`,
  `justify-self-end` in a column narrower than itself, so it overflows towards the start; the new `overflow-hidden` panel hides
  that part. The accessible name is complete, but the visible status is not (WCAG 2.2 SC 1.4.4 resize text, SC 1.4.10).
  Fix: let the badge wrap or shrink inside the column (for example `max-w-full` and `whitespace-normal` on the badge wrapper below
  768px), then re-measure at 200%. The unit test cannot see this; add an e2e case at 360px with the root font at 200% that
  asserts every badge's left edge is at or right of the panel's left edge.
- Q2 (low, builder): at 200% and 320px the transactions text column is 17px wide, so the merchant breaks per character
  (`wrap-break-word`). Nothing is clipped, but it is hard to read; the amount and Dispute link keep their width.
- Known pre-existing low, not failed: at 200% the page scrolls horizontally by 132px (320) and 92px (360) because of the
  signed-in header and the phone tab bar ("Disputes" cut off). The lists add nothing to it at 100%.

### Focus ring on the panel's last row (review L2): confirmed

At 360px, 100% text, with the response cut to three disputable rows: the Dispute link is `:focus-visible` with a 3px ring at 2px
offset; link bottom 558.56, row bottom 558.56, panel bottom 559.56, ring bottom edge 563.56. The ring's whole bottom side is
outside the panel and clipped (`focus-last-360.png`); top, left and right stay visible. Fix as the review says
(`focus-visible:-outline-offset-3` on the link). Rated low, as in the review.

## Four hops: one dispute through the list

- Browser: `/disputes` shows "Netflix.com, Cancelled subscription, raised 7 Nov 2026, R199.00, Submitted".
- Network: `GET /api/v1/disputes` 200, `Cache-Control: no-store`, 7 items for customer A (the other customer's two are absent), each
  with keys `disputeId, customerStatus, outcome, reason, transaction, disputedAmount, createdAt`; `transaction` has
  `transactionId, accountRef, accountLast4, transactionDate, description, amount, maskedCard`; `customerStatus: submitted`,
  `reason: cancelledSubscription`, `disputedAmount: {amount: "199.00", currency: "ZAR"}`. Only `/authentication/session` and
  `/disputes` were requested on load. No change from before this story.
- Database (`qa_readonly`): `Disputes` row `01a1b4dc-5120-7a12-a50f-c53782ab8b56`: Submitted, CancelledSubscription, 199.00,
  TXN-A-0026. `StoredEvents`: `DisputeRaised`, version 1, actor `dd-login-customer-a`. `OutboxMessages` row `01a1b4dc-5120-7f93-b63b-47d3b284520e`,
  `disputedesk.dispute.raised.v1`, version 1, status Sent. Reading the list wrote no audit row (none expected for a customer list read).
- Topic `disputedesk.disputes`: key = the dispute ID; headers `content-type: application/cloudevents+json` and a `traceparent`;
  CloudEvent `id` = the outbox row ID, `type` `disputedesk.dispute.raised.v1`, `subject` = the dispute ID. Trace IDs omitted here.

## Standards

Presentation only; no HTTP, cookie or caching change. The list response is `no-store` (RFC 9111). The visual defects above fall
under WCAG 2.2 SC 1.4.4 (Q1) and SC 2.4.7 (L2, still met: three sides stay visible).

## Evidence hygiene

No `Authorization` header, cookie or credential is recorded. The sign-in used a local fixture login; trace IDs are scrubbed.

## Findings

```json
[{"severity": "medium", "file": "frontend/src/features/transactions/TransactionRow.tsx", "issue": "At 200% text size and 320 to 360px a status badge overflows the left of the overflow-hidden panel and is clipped ('Under review (prioritised)' left 151px outside at 360; Resolved badges on /disputes too). Q1.", "fix": "Let the badge wrap or shrink below 768px; add an e2e case at 360px with root font 200%."},
 {"severity": "low", "file": "frontend/src/features/transactions/TransactionRow.tsx", "line": 97, "issue": "Phone Dispute link focus ring bottom is clipped on the panel's last row (review L2 confirmed in a browser)."},
 {"severity": "low", "file": "frontend/src/features/transactions/TransactionRow.tsx", "issue": "At 200% and 320px the text column is 17px wide and the merchant breaks per character (Q2)."},
 {"severity": "low", "file": "frontend/src/features/transactions/TransactionsScreen.tsx", "line": 56, "issue": "Review L1 confirmed: skeleton band placeholders are div elements in the ul."}]
```

## Re-run after fix round 1

- Date: 2026-11-08. Tested: commit `2f897c37b3b58b11119c6126123213f98defeb43` plus the uncommitted tree in
  `.claude/worktrees/agent-a1c721639cdb71441` (both bases, the story after fix round 1, the QA e2e edits).
- Result: **pass**. Q1 (medium) is closed; L1 and L2 are closed; Q2 stays low. No new finding above low.
- Lock: `scripts/test-lock.sh run stack qa-ui-lists`, OTLP ports 14318/14317 (4318 is taken). Released after the walk; the same run
  stopped the stack (`docker compose stop`). Another QA run (qa-ui-linking) took the lock right after.

### e2e: two defects in my own new test, fixed

The first run of the two new tests in `e2e/tests/spa-lists-layout.spec.ts` failed for the tests' reasons, not the app's:
1. `page.addStyleTag` is blocked by the page's CSP (`style-src 'self'`; the Playwright error text is in the log). The test now sets
   `document.documentElement.style.fontSize = '200%'` through the CSSOM, which the CSP allows.
2. On `/disputes` the badge is not a link (the whole row is), so `getByRole('link', {name: /^Dispute status: /})` found nothing. The test now
   finds the badge as the grandparent of its `sr-only` "Dispute status:" prefix, the same element on both pages.

Lint (`eslint tests/spa-lists-layout.spec.ts`): clean. Both tests assert at least 3 badges and that each badge's left and right edge lie inside
the nearest `overflow-hidden` ancestor (0.5px tolerance), at 360px with the root font at 200%.

### Automated runs

- Regression project, `npx playwright test --project=regression spa-transactions spa-disputes-history spa-dispute-status-badges spa-lists-layout`,
  on a fresh stack (small queue, raised linking limits, as `run-regression.sh` group 7): first run `39 passed, 2 failed` (the two defects
  above). After the test fix, the same four specs on the journeys stack: **`41 passed (57.0s)`** (spa-lists-layout alone: `10 passed`,
  8 earlier plus 2 new). Log: `/private/tmp/claude-501/qa-ui-lists/run1.log`. The full regression group was not re-run: the delta is
  presentation only, and the earlier whole-group run (240 passed, 1 skipped) covered the other specs.
- Journeys, `npx playwright test --project=journeys` on a fresh stack (volumes removed, rebuilt): UJ-1, UJ-2, UJ-5a, UJ-5b, UJ-5d,
  **`5 passed (39.3s)`**.

### Browser walk (Playwright MCP, customer A, signed in through Zitadel; bounding boxes)

Root font set through the CSSOM. Each cell compares every status badge's left and right edge with its panel.

| Viewport | Root font | Schemes | `/transactions` | `/disputes` |
|---|---|---|---|---|
| 320 | 100, 150, 200% | light, dark | 7 badges, 0 outside | 7 badges, 0 outside |
| 360 | 100, 150, 200% | light, dark | 7 badges, 0 outside | 7 badges, 0 outside |
| 768 | 100% | light, dark | 7 badges, 0 outside | 7 badges, 0 outside |
| 1280 | 100% | light, dark | 7 badges, 0 outside | 7 badges, 0 outside |

Before the fix at 200% and 360px, "Under review (prioritised)" started 151px outside; now no badge is outside at any size above. The
badge's computed style is `white-space: normal`, `overflow-wrap: anywhere`.

- Last-row focus ring (response cut to the three newest disputable rows, so the last row holds a Dispute link): at 360/100%, 360/200%,
  320/100%, 768/100% and 1280/100%, light and dark, the Dispute link is `:focus-visible` with `outline-offset: -3px` and width 3px; the
  ring's bottom edge is 1px inside the panel's bottom at 360/100% and 13px inside at 768 and 1280; left, right and top are inside. Nothing is
  clipped (screenshots `focus-last-*.png` in `/private/tmp/claude-501/qa-ui-lists/`). On `/disputes` the last row's ring is inside the panel
  on all four sides at every size above (offset -3px, 1px inside).
- Skeleton (API held back), `/transactions` and `/disputes`, 360 light and 1280 dark: `ul[aria-busy="true"]` labelled "Loading transactions"
  / "Loading disputes". `/transactions`: 7 `li`, 2 of them (the bands) `aria-hidden="true"`, and the accessibility tree lists exactly
  **5 listitem**; `/disputes`: 5 `li`, none hidden, 5 listitem. No child of the `ul` is not an `li` (L1 closed).
- Dispute detail badge (shared component), 360px, 100% and 200%, light and dark: `white-space: normal`, `overflow-wrap: anywhere`;
  "Dispute status: Submitted" spans x 41 to 148 at 100% (viewport 360), 24px tall; at 200% x 81 to 296, 47px tall (two lines), inside its card.

### Known lows seen again (recorded, not failed)

- Whole-page horizontal scroll at 200%: 132px (320) and 92px (360) on both lists; 19px at 320/150%; none at 360/150%. At 200% the dispute
  detail page at 360px scrolls 138px: its main column is 498px wide (set by the signed-in header, "Sign out" right edge 452), so the "Back to
  disputes" row and the card are as wide; the badge sits inside the card. Same cause as the header low, not this story's.
- Merchant breaking per character at 320 and 360px/200% (Q2), visible in `focus-last-light-360-200.png`.
- The md grid overflowing the panel at 768/150-200% and 1280/200% with only the CSS root font raised: not re-measured (known).

### Notes

- Pacing: the gateway limits one address to 10 requests a second (burst 20). A `429` appeared once when the walk loaded pages without a
  pause; that is the gateway working, not a finding. The walk then paced its page loads.
- Database and topic hops: the fix is presentation only (no HTTP, data or event change). The fresh-stack journeys re-run traced them
  (UJ-1 asserts the database rows and the topic message; UJ-5a and UJ-5b the 404 and the 409); the four-hops section above is unchanged.
- Evidence hygiene: no cookie, Authorization header or personal data recorded; the sign-in used the local fixture login.

### Findings after fix round 1

```json
[{"severity": "low", "file": "frontend/src/features/transactions/TransactionRow.tsx", "issue": "At 200% and 320 to 360px the text column is 17px wide and the merchant breaks per character (Q2, unchanged)."},
 {"severity": "low", "file": "frontend/src/app/Header.tsx", "issue": "Known: at 200% text the signed-in header makes the whole page scroll horizontally (132px at 320, 92px at 360 on the lists, 138px on the dispute detail at 360); not this story."}]
```

Closed: Q1 (medium), L1 (skeleton bands are `li aria-hidden`), L2 (last-row focus ring inside the panel).
