# ui-detail-start: QA record

- Date: 2026-11-08
- Story: `docs/backlog/story-ui-detail-start.md` (origin/main), 9 criteria
- Tested: commit `2f897c37b3b58b11119c6126123213f98defeb43` plus the uncommitted tree in
  `.claude/worktrees/agent-af28b3a0f99cd8e64` (ui-tokens-icons fix round 3, ui-notices-states fix round 1 and this story).
  `git diff HEAD | sha256` starts `4fcc9a47beeb2e87`. Nothing was committed, pushed or edited under `frontend/`.
- Stack: Compose from that worktree, OTLP ports 14318/14317 (4318 was taken), under `scripts/test-lock.sh run stack ui-detail-start`.
  The lock was held by `qa-ui-lists` first; the run waited for it and did not break it. One lock hold covered the regression
  run, the journeys and the manual passes. The stack is stopped on release.
- Result: **pass**, with two findings that this story did not cause (F1, F2) and four low observations (O1 to O4).
- Evidence: screenshots in `docs/verification/ui-detail-start-qa/` (69 files, demo data only; no cookies, tokens or personal
  data). Logins used are the local fixture customers; their credentials are not repeated here.

## 1. e2e updates (e2e/ only)

Copied the two base edits from `agent-ae1ed41bc1abcb91e` (`spa-disputes-history.spec.ts`, `spa-transactions.spec.ts`), then
edited only the assertions this story changes. No check was removed or loosened; each was rewritten to assert the new
structure at least as strictly.

| File | Change |
|---|---|
| `tests/dispute-detail.spec.ts` (was :362) | `You · <time>` becomes: the paragraph holding `<time>` has one `span` that reads exactly `You`, and it contains no `·`. |
| `tests/dispute-detail.spec.ts` (was :413) | `ol li` count of 5 is scoped to the `Timeline` region, because the guidance steps are `ol li` too. Still 5. |
| `tests/dispute-detail.spec.ts` (was :424) | The one-string guidance check becomes: region and `h2` "If you disagree with this outcome", two decorative numbers `1`, `2`, and the two step texts exactly. |
| `tests/journeys/uj-1.spec.ts` (:114) | `'You ·'` becomes an actor span of exactly `You` plus no `·` in the entry. |
| `tests/journeys/uj-2.spec.ts` (:87) | Same. |

The first regression run failed my own first draft of the guidance assertion (`li` text includes the number badge, so
`1You can complain...`). I fixed the assertion to read the `p` and the badge separately; the build was right. The FR-17 status
assertions (`:354`, `:421`, uj-1 `:112`, uj-2 `:85`) are untouched and pass. `npm ci` and `npm run lint` in `e2e/` pass.

## 2. Regression and journeys (per project)

`e2e/run-regression.sh`, every group on its own fresh stack (`down -v`, `up --build`):

| Project | Result |
|---|---|
| linking-rate-limits | 7 passed; D sends 3 (cap held) |
| linking-screens | 5 passed; D sends 2 |
| linking-queue | 1 passed; D sends 3 |
| linking-verify-a | 6 passed; D sends 2 |
| linking-verify-b | 3 passed; D sends 2 |
| linking-verify-c | 2 passed; D sends 2 |
| regression | first run: 235 passed, 1 failed, 1 skipped, 4 did not run (the script exited 1). The failure was my guidance assertion (section 1). The 4 that did not run are the rest of `dispute-detail.spec.ts`'s serial block. After the fix, `dispute-detail.spec.ts` re-ran alone on the journeys stack: 12 of 12 passed. The one skip is `scalar-disabled.spec.ts` (needs its own compose override), unchanged. |
| journeys (fresh stack) | 5 passed: uj-1, uj-2, uj-5a, uj-5b, uj-5d |

Caveat for the supervisor: the regression group did not pass in a single clean run, because of the assertion fix. The 4 tests
that did not run passed on a default-limits stack, not the raised-limits one. `dispute-detail.spec.ts` does not use the raised
linking limits, so I expect the same on a full re-run; CI's full run is the authoritative one.

Axe: every spec that runs axe (`axeFindings`, `expectAccessibleAtBothWidths`) passed with no findings, including the loading,
rejected (1280 and 320) and detail journeys. The journeys print two `EVIDENCE` lines that are recorded and not asserted:
hosted register page and hosted verify page, `link-name` (serious, 1 node each). I believe that is Zitadel's own template (the back-arrow
link on the Registration page, visible in `hosted-signup-*.png`, has no text); I did not inspect the flagged node, so I cannot
rule out the logo.

## 3. Criterion 9, locally (hosted pages)

After `docker compose down -v` and an attached `docker compose up --build` (so the post-start hook's output is kept), the
provisioning log reads, in order:

```
[provision] label policy colours updated
[provision] font uploaded
[provision] logo uploaded
[provision] dark logo uploaded
[provision] label policy activated
```

Every Zitadel request logged during provisioning returned 200; there was no 4xx. (The earlier journeys stack's hook output is
not kept by `docker compose up -d`, which is why I redid this attached.)

Screenshots, 1280px, light and dark: `hosted-signin-{light,dark}.png`, `hosted-signup-{light,dark}.png` (the Registration page
reached through Register). In all four the wordmark reads "DisputeDesk" with the dot after the name, navy with a cobalt dot in
light and #c9d6ff with a #8caaf7 dot in dark, on a transparent background. The hosted image is 620 x 128.
No response of 400 or more and no CSP violation was logged on the hosted pages (checked by response and console listeners in
both themes). `mockups/sign-in.html` and `sign-up.html` differ only in Zitadel's own layout, which this story does not touch.
One 404 `favicon.ico` on `auth.localhost` showed in the first pass (Zitadel, not ours).

## 4. Walk of each criterion (browser, network, database, events)

Customer A signed in through Zitadel. The seeded A disputes: Submitted, Under review x2 (one later became prioritised through
the SLA sweep, unrelated), Withdrawn, Resolved Upheld (Wrong amount), Resolved Rejected (Duplicate charge). I also raised two
real disputes on TXN-A-0028 and TXN-A-0027 through the UI for the banner and the data hops.

| # | Criterion | Browser | Network | Result |
|---|---|---|---|---|
| 1 | Disputed on | Rejected dispute: rows Reason, Transaction date, Disputed on = `13 Sep 2026`. Wrong-amount dispute: Reason, Transaction amount R310.75, Expected amount R210.75, Transaction date, Disputed on = `19 Sep 2026`. | Detail `200`, `Cache-Control: no-store`, `createdAt` present; keys are exactly `disputeId, customerStatus, outcome, reason, note, transaction, expectedAmount, disputedAmount, createdAt, timeline`. No other field. | pass |
| 2 | Withdraw at the foot | Open dispute: hairline (1px solid) above the foot, the sentence "Changed your mind? You can withdraw while the dispute is open. You can't dispute this transaction again after you withdraw." above the 48px button, in both themes, all six widths. Rejected, upheld, withdrawn: neither. Dialog closes by Esc and by Keep dispute, focus returns to Withdraw (checked at 360, 767, 1280). | `409` on withdrawing a resolved or withdrawn dispute. | pass |
| 3 | Success banner | After a real raise: `Dispute submitted` in a success alert, one decorative `lucide-circle-check` (`aria-hidden`), no `role` on the alert, 0 assertive regions, exactly one `status` announcement reading "Dispute submitted". 360 light and 1280 dark. | `POST /api/v1/disputes` 201, `Idempotency-Key` and `X-XSRF-TOKEN` sent, `Location` `/api/v1/disputes/<id>`. | pass |
| 4 | Timeline | A flat surface (card, 1px hairline, rounded-lg, `box-shadow: none`), `h2` Timeline, each entry reads status label, then sentence, then actor, time (no separator), then the note quote. Earlier dots are 12px outlined, the latest is the primary dot with a halo, the sentence in navy on the latest. Resolved lines read "The bank agreed with you." (upheld) and "The bank didn't uphold your dispute." (rejected). Open disputes end with "We'll update the timeline as your dispute moves." below a hairline. | Same detail responses; timeline order as the API sent it. | pass |
| 5 | Rejected guidance | Own surface below the timeline with `h2` "If you disagree with this outcome" and two numbered steps with the exact texts. 1023px stacks card, timeline, guidance (tops 193, 615, 1091). 1024px shows card left and timeline right with guidance under the timeline (tops 193, 193, 771). 360, 767 and 768px stack. | n/a | pass |
| 6 | Skeleton | Request held open: the real `h1` "Dispute" is in the skeleton card, inside `aria-busy`, not `aria-hidden`; three `li` with `aria-hidden` dots; 0 inline `style`; `h1` count 1; title "Dispute · DisputeDesk". After load the same `h1` reads the merchant. 360 and 1280, both themes. | n/a | pass |
| 7 | Withdraw dialog | Hairline above the buttons. 767px: bottom sheet (`[0,443,767,357]`, bottom edge at 800, top corners only). 768px: centred dialog (`[104,214,560,373]`, 24px radius). Content max-height equals the viewport, `overflow-y: auto`. Initial focus Keep dispute at every width; Esc keeps the dispute. At 320x568, 320x256 (400% zoom) and 640x400 (200% zoom of 1280), with the problem notice and the error summary both showing: no horizontal scroll (scrollWidth equals viewport) and both buttons reachable by scrolling the dialog. See F1 for the 200% root font size case. | A forced `500` on the withdraw POST showed the generic notice and reference; a 501-character note showed the error summary. | pass (F1) |
| 8 | Assurance line | Under Sign in and Create a login: 13px caption with a decorative `lucide-lock` reading "You'll confirm it's you with your ID number, account number and a one-time code." 320, 360, 768, 1280, both themes; Sign in 44px, Create a login 48px; no horizontal scroll. | n/a | pass |
| 9 | Wordmark | Section 3. | provisioning requests 200 | pass |

### Database (read-only role), for the two raised disputes

- `Disputes`: one row each, `Status` Submitted, `Version` 1, reasons Unrecognised and NotReceived, on TXN-A-0028 and TXN-A-0027.
- `StoredEvents`: one `DisputeRaised`, `Version` 1 each.
- `OutboxMessages`: one row each with the same `Id` as the stored event (written in one transaction), `Status` Sent.
- `AuditLogEntries`: none for the raised disputes. For the refusals below: three `Withdraw | Rejected | Customer` rows (the other
  customer's dispute, the resolved one, the withdrawn one), plus the `SignIn | Succeeded` row. No `DisputeWithdrawn` event was
  written for either refused dispute.

### Events (topic `disputedesk.disputes`)

Both raises appear once: key = the dispute ID, type `disputedesk.dispute.raised.v1`, CloudEvent `id` = the stored event `Id` and
outbox `Id`, `subject` = dispute ID, `data` = `{disputeId, reason, status, occurredAt, disputeVersion}`, headers
`content-type: application/cloudevents+json` and `traceparent` (present). The note is not in the event.

### Unhappy paths (RFC 9110, RFC 9457)

| Request | Result |
|---|---|
| GET another customer's dispute | `404 application/problem+json`, `not-found`, detail "There is no such dispute." |
| GET an unknown ID | The same body shape and detail as the above, so nothing separates the two. |
| POST withdraw on another customer's dispute | `404`, same body. |
| POST withdraw on a resolved dispute | `409 application/problem+json`, `action-not-allowed`, `customerStatus: resolved`. |
| POST withdraw on an already withdrawn dispute | `409`, `customerStatus: withdrawn`. |

## 5. Breakpoints, themes and checks the brief listed

Playwright MCP, light and dark, at 360, 767, 768, 1023, 1024 and 1280px (rejected dispute: structure and layout JSON for all 12
combinations; open dispute with dialog for all 12). Compared with `mockups/dispute-rejected.html`, `dispute-withdraw.html`,
`dispute-loading.html`, `start.html` (screenshots `mockup-*.png`): layout, order, surfaces and copy match. Differences are the
decided ones: the status label on each entry (FR-17), and the dialog title and start `h2` at 16px (known, L4 in the review).

- No horizontal scroll at 320 and 360px on the open, rejected and upheld detail in both themes (`scrollWidth` equals the
  viewport) and at 640x400 (200% zoom) and 320x256 (400% zoom) with the dialog and both error surfaces.
- Every interactive element on those pages is at least 44px tall (0 smaller). That includes the "Back to disputes" link (44px).
  The ErrorSummary link is 22px at 640px inside the dialog (known, ui-notices-states; recorded, not failed); at 320px it
  measured 46px. I did not measure it at 767 or 1280px at the normal font size.
- Keyboard order on the open detail: skip link, DisputeDesk, Transactions, Disputes, Sign out, Back to disputes, Withdraw dispute.
- Known items seen as briefed: dialog buttons go side by side from 640px (L1; at 767px they sit in a row inside the sheet);
  `text-heading` renders the dialog title at 16px.
- Session warning (review L2), 360px: `session-warning-{light,dark}-360.png`. A bottom sheet (`[0,371,360,269]`, top corners 24px),
  hairline above two stacked 48px buttons, "You'll be signed out soon." / "For your security, we sign you out after 15 minutes without
  activity." / Stay signed in / Sign out. It reads well in both themes. Reached with Playwright's fake clock (13 minutes idle).

## Findings

| # | Rank | Finding | Caused by this story | Route |
|---|---|---|---|---|
| F1 | low | At 360px with the **root font size at 200%**, every page scrolls horizontally: the header overflows (Sign out at x=452 on Transactions, 536 on Disputes, 498 on the detail). On the detail, the unbreakable hero amount and the rem-based card padding (48px) make the card 466px wide. In the withdraw dialog (inner width 360) the `nowrap` buttons make the content 368px wide, so the dialog's own scrollbar appears and both buttons need a horizontal scroll to reach. At 1280px with the same font size nothing overflows, and real 200% zoom (viewport 640x400) and 400% zoom (320x256) are clean. WCAG 1.4.4 does not require no horizontal scroll for text-only resize, 1.4.10 passes, so I rank it low. | No. The header, the shared Button and the hero amount are not this story's. The dialog's rem padding and `overflow-x` are part of the shared primitive. | Lows batch: let the shared Button wrap or shrink, and the header collapse, at large root font sizes. |
| F2 | medium | **The session warning does not move focus into the dialog, and Tab leaves it.** When the warning opens (13 minutes idle), `document.activeElement` stays on `body`; the next six Tab presses walk the page behind the modal (skip link, DisputeDesk, Sign out, rows) and never reach "Stay signed in" or "Sign out". Escape still acts as Stay signed in, so a keyboard user has one way out. Cause (read, not run): Radix `AlertDialogContent` moves focus on open only to an `AlertDialogCancel`, and `InactivityWarning` has none (the withdraw dialog has Keep dispute, which is why that one works). Against the WAI-ARIA APG alertdialog pattern (focus moves inside on open) and WCAG 2.2 SC 2.4.3 and 2.2.1. | No. `InactivityWarning.tsx` and its behaviour are unchanged by this story; the story's `alert-dialog.tsx` change is classes only. Seen because review L2 asked for the warning's screenshot. | Does not block this story. Route to the lows batch or a small fix: give the warning an initial focus on Stay signed in (`onOpenAutoFocus`) and add a keyboard test (the unit test checks content and renewal, not focus). Unverified: I reached the dialog only with Playwright's fake clock (13 real minutes would have held the lock longer), so I did not rule out that the clock plays a part; the Radix reading above and the unchanged component make it unlikely. |

## Observations (no action for this story)

- O1 (low, pre-existing): opening any dialog logs one CSP violation, "Applying inline style violates ... `style-src 'self'`", from
  the app bundle (the scroll-lock library's injected `<style>`). One per open (a clean context logged 0 on load and 1 on the first dialog open; this session's console
  holds 37). The gateway CSP is doing its job; the library's gap-compensation style is dropped. Not on the hosted pages
  (criterion 9's "no CSP violation" holds there). Same behaviour on the existing withdraw dialog before this story.
- O2 (low): my own walk tripped the API's built-in rate limiter once, a `429` on the detail GET, and the SPA stayed on the
  skeleton for the 8s I waited (TanStack Query retrying). Not recovered or timed further. Expected limiter behaviour, noted so
  the skeleton under a `429` is not mistaken for a layout bug.
- O3: the open dispute used for the dialog checks was escalated to "Under review (prioritised)" by the SLA sweep during the
  session. It did not affect any check.
- O4: the hosted Registration page `link-name` axe finding (section 2) is Zitadel's template.

## Assumptions and notes

- The screenshots show seeded demo names only. Dispute IDs in this record are fixture UUIDs on a throwaway local stack.
- Not done, by instruction: the AWS logo refresh (a human step), a ZAP scan (CI), a commit or push.
- I did not add new committed walkthrough tests beyond the five updated specs; the existing `dispute-detail`, `uj-1` and `uj-2`
  specs now assert the new timeline structure and the guidance steps. Disputed on, the Withdraw foot sentence and the sheet
  layout have unit tests (`DisputeDetailScreen.test.tsx`) and were measured here; adding e2e assertions for them is optional.
