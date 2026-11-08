# QA: ui-tokens-icons

Commit tested: base `2f897c37b3b58b11119c6126123213f98defeb43` plus the story's uncommitted change (worktree `.claude/worktrees/agent-ad4bfc5f595f1e6a2`, branch `feature/ui-tokens-icons`). Result: **fail** (one medium finding). Date 2026-11-08. No personal data, cookies or Authorization headers recorded; sign-in used the fixture login `customer-a` only.

## 1. e2e specs updated (e2e/ only)

- `e2e/tests/spa-disputes-history.spec.ts:282-283`: the glyph text filters became `.locator('svg.lucide-check')` (Upheld) and `.locator('svg.lucide-x')` (Rejected). Same `toBeVisible` assertions.
- `e2e/tests/spa-transactions.spec.ts:331`: `'☰ Transactions', '⚑ Disputes'` became `'Transactions', 'Disputes'`.
- Grep of `e2e/` for the spec's ten glyphs finds nothing else.
- `npm run lint` in `e2e/` (after `npm ci`): clean.

## 2. Regression suite (`scripts/test-lock.sh run stack ui-tokens-icons -- e2e/run-regression.sh`)

Port 4318 was held by a VS Code process, so the first attempt failed to start every stack; the run was repeated with `DISPUTEDESK_OTLP_HTTP_PORT=14318` and `DISPUTEDESK_OTLP_GRPC_PORT=14317` (the script header's documented remedy).

| Project | Result |
|---|---|
| linking-rate-limits | 7 passed |
| linking-screens | 1 failed, 4 did not run (axe `target-size` at 320, finding F1) |
| linking-queue | 1 failed (same axe `target-size` at 320, F1) |
| linking-verify-a | 6 passed |
| linking-verify-b | 3 passed |
| linking-verify-c | 2 passed |
| regression | 231 passed, 1 failed (`linking-start` criterion 2), 8 did not run (serial after that failure), 1 skipped |

- The `linking-start` criterion 2 failure is a test flake, not the story: the 400 body's random `traceId` (`...96aabc4a3f...`) contains the substring `abc`, which the test asserts the response never echoes. Re-run of `tests/linking-start.spec.ts` on a fresh stack under the lock: 14 passed. Low finding F2 (test hygiene, outside the story).
- Axe (`axeTags` WCAG 2.2 AA) passed in every spec that runs it, except the two linking-screens runs above, where the 320px axe scan reports `target-size`.
- The D check (OTP sends for customer D, cap 3) held in every group (3, 0, 3, 2, 2, 2, 2).

### F1 (medium): the 44px home link is partly covered by the focused skip link, so axe `target-size` fails at 320px

Reproduced with a temporary debug print in `linking-screens.spec.ts` (reverted; the file is unchanged). At 320px on the linking 2a screen the keyboard walk ends on "Skip to main content" (rect x8 y8 181x40). The home link is now `min-h-(--tap-target)`: rect x16 y10 129x44, so the skip link covers y10-48 and leaves 6px of it visible. Axe: "Target has insufficient size because it is partially obscured (smallest space is 128.9px by 6px, should be at least 24px by 24px)". Before the story the home link was shorter, so the skip link covered it fully, which axe does not flag. Both `linking-screens` and `linking-queue` fail in CI as is. Standard: WCAG 2.2 SC 2.5.8 Target Size (Minimum), the rule axe applies. The story's own criterion 5 (44px) is met; its Verify line ("axe on every spec that runs it") is not. No e2e change fixes this without weakening a check; the fix is in the frontend (for example, give the skip link a position or size that covers the home link fully or sits clear of it, then re-run QA). Not fixed here: QA does not write production code.

## 3. Mockup comparison on the running stack (Playwright MCP, customer-a, 360px, 1280px, light and dark)

Compared against `docs/ux/mockups/screenshots/` and the mockup HTML for icon choice, size and placement. All pass:

- Header wordmark: home link x16 y10 129x44; text ends at x134.9; the dot is 8x8 at x137 (2px gap, `ml-0.5`), `aria-hidden`, last element in the inner `inline-flex items-baseline` span, sitting at the text baseline like a full stop. Colour `rgb(43, 87, 201)` light, `rgb(140, 170, 247)` dark (matches DESIGN: #8CAAF7). Accessible name unchanged: "DisputeDesk". Seen at 360 and 1280, light and dark, signed out and signed in.
- Phone tab bar (360 and 320): `lucide-list` (Transactions) and `lucide-flag` (Disputes), 16x16, `aria-hidden`, `stroke=currentColor`, `stroke-width=2`, tabs 48px tall; active pill cobalt light, `#8CAAF7` with dark text in dark. `TopNav` at 1280: text only, no svg, links 44px tall (min-height `44px`), phone nav hidden.
- Status badges (`/disputes` and `/transactions`): Submitted `lucide-send`, Under review `lucide-search`, Under review (prioritised) `lucide-search-priority` (same lens plus arrow, amber tone), Resolved `lucide-flag`, Resolved · Upheld `lucide-check`, Resolved · Rejected `lucide-x`, Withdrawn `lucide-undo-2`. Each is 16x16 `size-4`, `aria-hidden`, `stroke=currentColor`, `stroke-width=2`, gap 4px, padding `2px 10px` (DESIGN), tone colours follow the tokens in light and dark (for example dark info `rgb(166, 224, 234)` on `rgb(16, 58, 67)`).
- Not-disputable hint: `lucide-info` 14x14 (`size-3.5`), `mt-0.5`, `aria-hidden`, placed before the hint text as in the mockup, on pending, money-in, transfer and not-a-card-transaction rows.
- Start page sample badge: `lucide-search` 16x16 in the "Under review" badge, as in `start-360.jpg`; dark at 360 and 1280 matches the dark token set.
- Out of this story and not judged: lock icon on the start page, chevrons, day bands, column headers, the "Mockup" banner (later areas).
- Overflow: 0 at 320px, 360px, 1280px and 640px (200% zoom of a 1280px window). At a 180px window (200% of a 360px phone) the document overflows by 89px; not a WCAG reflow measure, not compared with the base (unverified whether it pre-exists; likely the header's Sign out beside the wordmark).
- Touch targets: header home link 44px, Sign in / Sign out 44px, `TopNav` links 44px, tab bar 48px.
- Observation F3 (low, outside this story): "Dispute this transaction" links are 36px and badge links to a dispute are 42.8px tall (`py-2` around a 26.8px badge) at 360px, below DESIGN's 44px rule. These belong to `ui-lists`; I did not compare with the base, so I cannot say whether the badge icon changed the height.
- Four-hop trace: the story is presentation only (no API, route, auth, database or event change). Browser hop checked as above. Network, database and event hops are covered by the 231-test regression group (stored events, outbox, relay, audit specs all passed on the changed build); no separate trace was needed.
- No screenshots are committed; the MCP's `.playwright-mcp/` output in the main checkout was deleted.

## 4. Stack

Held the `stack` lock for the runs above; stack stopped (`docker compose stop`, then the lock released). No manual lock holder remains (`test-lock.sh status`: no locks held).

## Findings

| # | Rank | Finding |
|---|---|---|
| F1 | medium | Focused skip link covers the 44px home link; axe `target-size` fails at 320px; `linking-screens` and `linking-queue` fail |
| F2 | low | `linking-start` criterion 2 flakes when the random `traceId` contains `abc` |
| F3 | low | 36px "Dispute this transaction" and 42.8px badge-link targets (ui-lists scope; base not compared) |

## Escapes

F1: caught by QA (e2e axe at 320), should have been caught by the builder's story-level check of the header against the existing e2e axe cases; category: missing test or environment (the unit tests cannot see the skip link overlap).

## Re-run after fix round 2

Commit tested: 2f897c37b3b58b11119c6126123213f98defeb43 plus the uncommitted story change (the skip link takes its own 44px row when focused, RootLayout.tsx). Stack from the worktree under the shared stack lock, OTLP ports 14318/14317 (4318 taken), small queue, production linking limits for the linking projects. e2e/node_modules was installed with npm ci in the worktree first.

| Project | Result |
|---|---|
| linking-screens | 5 passed (F1 gone: axe target-size clean at 320 and 1280) |
| linking-queue | 1 failed at e2e/tests/linking-screens.spec.ts:478 ("a second start request": expected 2 calls, got 1). The axe and keyboard walk in this test passed (its EVIDENCE line printed) |
| journeys (smoke) | 5 passed |

F1 is fixed. Playwright MCP, signed-out start page, Tab once, skip link vs the header home link:

| Scheme | Width | Skip link (x y w h) | Home link (x y w h) | Overlap |
|---|---|---|---|---|
| light | 320 | 16 8 182 44 | 16 70 129 44 | 0 |
| light | 1280 | 16 8 182 44 | 104 70 129 44 | 0 |
| dark | 320 | 16 8 182 44 | 16 70 129 44 | 0 |
| dark | 1280 | 16 8 182 44 | 104 70 129 44 | 0 |

### F4 (medium, new): focusing the skip link shifts the page 60px, so a pointer click made while it is focused is lost

The focused skip link is in flow, so the header and everything below move down 60px (home link y10 to y70) and back up when it blurs. A pointer press blurs it on mousedown, the target moves before mouseup, and no click fires. Reproduced in the MCP at 1280px, light: Tab once (focus on "Skip to main content"), mouse down and up at the centre of "Sign in" (x1091 y70, 85x44): no navigation, URL unchanged. Control without the skip link focused: a click on "Sign in" goes to the Zitadel login. This is what fails linking-queue at line 478: the keyboard walk in checkState ends with focus back on the skip link, then the test clicks Continue and nothing is sent. The test is right; a keyboard user who then picks up the mouse hits the same thing. Standards: WCAG 2.2 SC 2.5.2 Pointer Cancellation (a down-event must not make the click fail) and the non-disruptive intent of technique G1; a focus-triggered layout shift is also what WCAG 2.2 SC 3.2.1 On Focus guards against. Fix is in the frontend: keep the skip link out of the flow (overlay it on a strip that holds no 44px target, or reserve its 44px row even when unfocused so nothing moves), then re-run linking-queue. Not fixed here: QA does not write production code, and the e2e test is not weakened.

Stack stopped (no disputedesk containers running) and lock released (status: none held). The MCP's .playwright-mcp output was deleted.

| # | Rank | Finding |
|---|---|---|
| F1 | closed | Skip link overlap fixed |
| F4 | medium | Focused skip link shifts the layout 60px; a pointer click while it is focused is lost; linking-queue fails |

## Re-run after fix round 3

Commit tested: `2f897c37b3b58b11119c6126123213f98defeb43` plus the uncommitted story change (the skip link is now a child of the header's flex row, absolutely positioned and `focus:min-h-(--tap-target)`, `Header.tsx`; `RootLayout` passes `onSkipToMain`). Worktree `.claude/worktrees/agent-ae1ed41bc1abcb91e`, stack lock held for the runs and released after, OTLP ports 14318/14317 (4318 taken), small queue, production linking limits for the linking projects, stack rebuilt (`up -d --wait --build`) from the worktree. `e2e/node_modules` installed with `npm ci`. Date 2026-11-08. No cookies, tokens or personal data recorded (fixture login `customer-a` only; the session cookie went into the MCP browser and the cookie jar was deleted).

### Suites (same group set-up as `e2e/run-regression.sh`; groups 2 and 3 and the journeys run)

| Project | Result |
|---|---|
| linking-screens | 5 passed (axe clean at 320 and 1280 on every screen, keyboard order includes the skip link) |
| linking-queue | 1 passed (the click at `linking-screens.spec.ts:478` now lands; F4 gone) |
| journeys | 5 passed |

D check (OTP sends for customer D, cap 3): 2, 3 and 1, held. The other regression groups were not re-run: the delta is a header layout change only.

### Skip link, Playwright MCP, Tab once from a blurred page

Rects are x y w h in CSS px. "Moved" is the set of header, nav, h1, main-link and main-button rects (20 elements on /transactions) that differ before and after the Tab; the header box and document height are also compared.

| Page | Scheme | Width | Skip link | Home link | Skip contains home | Moved | Header / doc height |
|---|---|---|---|---|---|---|---|
| start (signed out) | light | 320 | 16 10 181.5 44 | 16 10 128.9 44 | yes | none | 65 / 1762 unchanged |
| start | light | 1280 | 104 10 181.5 44 | 104 10 128.9 44 | yes | none | 65 / 1083 unchanged |
| start | dark | 320 | 16 10 181.5 44 | 16 10 128.9 44 | yes | none | 65 / 1762 unchanged |
| start | dark | 1280 | 104 10 181.5 44 | 104 10 128.9 44 | yes | none | 65 / 1083 unchanged |
| /transactions (linked) | light | 320 | 16 10 181.5 44 | 16 10 128.9 44 | yes | none | 65 / 3327 unchanged |
| /transactions | light | 1280 | 224 12 181.5 44 | 224 12 128.9 44 | yes | none | 69 / 2893 unchanged |
| /transactions | dark | 320 | 16 10 181.5 44 | 16 10 128.9 44 | yes | none | 65 / 3327 unchanged |
| /transactions | dark | 1280 | 224 12 181.5 44 | 224 12 128.9 44 | yes | none | 69 / 2893 unchanged |

(The fix-round-2 skip link was 16 8 182 44 and moved the page 60px; now the header does not change height and nothing moves.)

Pointer click while the skip link is focused (real mouse down and up at the target's centre):

- start page, "Sign in" (84.97x44), all four scheme and width combinations: navigated to auth.localhost:8080/ui/login/login (the Zitadel login). The fix-round-2 result at 1280 light was no navigation.
- /transactions linked, "Disputes" (nav link at 1280, tab bar link at 320), all four combinations: URL went from /transactions to /disputes.

F4 is closed.

### Reviewer low 1: the focused skip link over the TopNav "Transactions" link (1280, linked, light and dark identical)

- Skip link box 224 12 181.5 44 (right edge x405.5). TopNav "Transactions" link 380.9 12 125.8 44; "Disputes" 510.7 12 97.5 44.
- Overlap with Transactions: 24.6 x 44 = 1083 px2, 20 % of the link's box. Visible, clickable box: x405.5 to 506.7, 101.1 x 44 (80 %, confirmed by an elementFromPoint grid test: 80 % of the link's points hit the link). Disputes is not overlapped.
- Its label text spans x396.9 to 490.7; the first 8.6px (about 9 %, the "T" and the start of "r") sit under the skip pill while it is focused.
- axe-core 4.10.2 with the e2e tags (wcag2a, wcag2aa, wcag21a, wcag21aa, wcag22aa) run on /transactions with the skip link focused, at 320 and 1280, light and dark: 0 violations; target-size is in the passes, not in violations or incomplete. The 101.1 x 44 visible box is above WCAG 2.2 SC 2.5.8's 24x24 minimum, so the overlap is not flagged.
- Judgement: a cosmetic overlap only while the skip link is focused (the skip link is the focused element, so SC 2.4.11 Focus Not Obscured is not at issue); the link stays operable. Kept as the reviewer's low, no new finding.

### Stack and lock

docker compose stop ran when the second lock hold ended; no disputedesk containers running; test-lock.sh status: no locks held. The MCP's .playwright-mcp output was deleted. No commit, push or frontend edit.

| # | Rank | Finding |
|---|---|---|
| F1 | closed | Skip link overlap (round 2) |
| F4 | closed | Layout shift and lost click (fixed in round 3, linking-queue passes) |
| F2 | low | linking-start criterion 2 flake on a traceId containing abc (unchanged, outside the story) |
| F3 | low | 36px and 42.8px targets in ui-lists scope (unchanged) |
| L1 | low | Focused skip link hides the first about 9 % of the "Transactions" label at 1280 linked; axe passes (reviewer's low 1, measured) |
