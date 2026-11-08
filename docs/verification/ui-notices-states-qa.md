# QA record: ui-notices-states

Commit tested: `2f897c37b3b58b11119c6126123213f98defeb43` (worktree `.claude/worktrees/agent-a1758a16625231b46`, uncommitted: base `ui-tokens-icons` after fix round 3 plus this story after fix round 1).
Date: 2026-11-08. Lock: `stack`, owner `qa-ui-notices-states`, held for the run only. The stack is stopped and the lock released.
Status: **pass**.

Scrubbed for POPIA: no cookies, `Authorization` headers, ID numbers or login names are recorded. The seeded logins are dev-only fixtures. Screenshots are in the session scratchpad and are not committed.

## 1. e2e specs

- The base story's two e2e edits were copied in unchanged from `agent-ae1ed41bc1abcb91e`: `e2e/tests/spa-disputes-history.spec.ts` and `e2e/tests/spa-transactions.spec.ts`. They are the only e2e changes. Both are in `e2e/` only.
- This story needed no further e2e change. Every spec the story names passed unchanged, so no check was weakened or loosened.
- `npm ci` and `npm run lint` in `e2e/`: lint is clean. `npm ci` printed only the usual audit notice.

## 2. Full regression

Command: `scripts/test-lock.sh run stack qa-ui-notices-states -- e2e/run-regression.sh`, with OTLP ports 14318 and 14317 because 4318 was taken by another process. Exit 0. Each group ran on a fresh stack.

| Project | Result | Customer D OTP sends (cap 3) |
|---|---|---|
| linking-rate-limits | 7 passed | 3 |
| linking-screens | 5 passed | 2 |
| linking-queue | 1 passed | 3 |
| linking-verify-a | 6 passed | 2 |
| linking-verify-b | 3 passed | 2 |
| linking-verify-c | 2 passed | 2 |
| regression | 240 passed, 1 skipped (`scalar-disabled.spec.ts:11`, skipped in the default stack by the spec's own condition, not by QA) | 3 |

- No failures and no flaky retries.
- axe (`expectNoAxeViolations`, WCAG 2.2 AA tags) ran inside the specs that carry it and reported no violations.
- The `journeys` project is not part of `run-regression.sh`, so it was run separately on a fresh stack (`docker compose down -v`, `up --wait`, then `playwright test --project=journeys`): **5 passed**. These are UJ-1, UJ-2, UJ-5a, UJ-5b and UJ-5d.
- First attempt at `journeys`: UJ-1 failed with "no OTP SMS reached customer D". Cause: it ran on the stack left by the regression group, where D had already used its linking and its 3 OTP sends an hour. It is an environment effect, not a defect, and it passed on the fresh stack.
- The "EVIDENCE ... axe: link-name" lines in the journeys output come from Zitadel's hosted register and verify pages, not from this app.
- Four hops: the story is presentation only (no API, event, data or auth change), so the regression asserting the unchanged wire behaviour is the network, database and event evidence. Examples: 429 with `Retry-After: 900` on `/linking/start` and `/linking/verify`, 409 `linking-locked`, the 503 reference, and UJ-5a (another customer's dispute is 404 like an unknown id) and UJ-5b (a resolved dispute cannot be withdrawn, 409). UJ-1 traces to the database and the topic. No other request, field or audit row was added.

## 3. Visual walk (Playwright MCP, stack up, seeded customers A and C)

States were reached with the real app. Error states that need a failing backend (503 with `traceId`, 429, 409 locked, empty lists) were forced by intercepting the single API response in the browser, as the e2e specs do. Measurements are computed styles and bounding boxes, at 360 and 1280px, in light and dark. The mockups were measured the same way for comparison.

| Surface | Observed (all four combinations unless noted) | Mockup or spec | Result |
|---|---|---|---|
| Problem notice (503 on transactions) | `role="alert"`, danger tokens (light bg `rgb(253,236,234)`, fg `rgb(154,28,19)`; dark bg `rgb(74,26,22)`, fg `rgb(255,180,171)`), radius 12px, padding 16px, no border; `TriangleAlert` 16x16 | `error.html` same tone | match |
| Reference row | 1px hairline at 20% of the foreground colour, spanning the notice's whole content column (772px at 1280, 268px at 360; the mockup measures 770px, the 2px difference is its 18px icon vs 16px) ; "Reference", the traceId in mono, Copy on one row at 1280. At 360 the 32-character id wraps onto a second line and Copy sits below it | `error.html` | match; the 360 wrap is the same flex-wrap behaviour as the mockup |
| Copy pill | 44px high at 360 and 1280 (66.7px wide); shared secondary Button: cobalt outline on white (light), `rgb(140,170,247)` outline on `rgb(31,41,51)` (dark); radius full | Recorded intent: differs from the mockup's transparent currentColor outline (DESIGN.md wins, review Fix round 1) | as intended |
| Copied state | Click: label "Copied" at once, height still 44px, clipboard holds the traceId exactly; back to "Copy" after 2.3s | spec criterion 2 | pass |
| Error summary (empty Continue on /link) | Group named "Check the details below", `tabindex=-1`, focused after submit, inner `role="alert"`, title is an `h2`, `TriangleAlert` 16px, danger look, one link per field (`#idNumber`, `#accountNumber`) | spec criterion 3 | pass |
| Field errors | Each has a 14px (`size-3.5`) `TriangleAlert`, text unchanged, `aria-describedby` wired; text colour `rgb(180,35,24)` light, `rgb(255,138,126)` dark | spec criterion 3 | pass |
| Rate-limited notice (429 + `Retry-After: 900` on start) | `role="alert"` (the only one on the page), `Clock` 16px, danger colours, single sentence "Too many attempts. Try again in 15 minutes."; the submit stays `aria-disabled` | `link-rate-limited.html`; spec Boundaries (16px icon, single sentence) | pass |
| StateSurface, session ended (`/signed-out?reason=expired`) | `h1` "You've been signed out", surface `card` + 1px hairline + radius 24px, 48px muted tile, 24px `Clock` (`aria-hidden`), one 48px "Sign in again"; 328px wide at 360, 512px at 1280 | `session-expired.html` (same size, radius, padding 32/24, text) | match |
| StateSurface, locked (`/link/otp`, 409 `linking-locked`) | `h1` "We can't link you online" takes focus; `Lock`; one 48px "Start again"; "Can't link online? Contact the bank." sits outside the surface, below it | `link-locked.html` | match |
| StateSurface, dispute not found | `h1` "We can't find that dispute", `Search`, "Go to your disputes" 48px. `/nowhere` shows the same page and copy | `not-found.html` | match |
| StateSurface, transactions empty | `h2` "No transactions in the last 90 days.", `List`, no button; the only link is the inline "Disputes" in the sentence | `transactions-empty.html` | match |
| StateSurface, disputes empty | `h2` "You haven't disputed any transactions.", `Flag`, "Go to transactions" 48px | `disputes-empty.html` | match |
| StepProgress | Renders on no screen yet, as the spec says (only its component and test files reference it), so there is nothing to compare in the browser | spec criterion 6 | not visually applicable |

Common to every StateSurface: 48px tile, `muted` background (`rgb(238,240,243)` light, `rgb(42,52,65)` dark), 24px heading-colour icon with `aria-hidden="true"`, surface background `card` (`rgb(255,255,255)` light, `rgb(31,41,51)` dark), border 1px, radius 24px, padding 32px 24px.

Reflow: no horizontal scroll. `scrollWidth` equals the viewport at 320px and at 640px (200% zoom of 1280, and 400% zoom is 320) on `/transactions` with the 503 notice and its long reference, `/disputes`, the not-found dispute, `/signed-out?reason=expired`, `/link`, `/link/otp` and `/nowhere`. At 360 and 1280 `scrollWidth` also equals the viewport on every state above.

Console: the only console errors came from the deliberately intercepted 4xx and 5xx responses.

## Standards check

- The `role="alert"` is on the danger and rate-limited notices and the error summary's inner element only, one alert on the page at a time (WAI-ARIA alert pattern). Success, info and neutral have no `role`.
- Heading levels follow SC 1.3.1: `h1` on full-screen states, `h2` on empty lists under the page `h1`.
- The error summary and field errors identify the error in text with a link to each field (SC 3.3.1, GOV.UK pattern).
- Target size: Copy 44px; every standalone button and link in the surfaces is 44px or more.
- Observed and left as is: the inline "Disputes" link in the transactions-empty sentence is 22px high. It is a link inside a sentence (WCAG 2.2 SC 2.5.8 inline exception) and the mockup is identical. Not a finding.

## Findings

None high, medium or low. Environment notes: port 4318 was taken, so OTLP ports 14318 and 14317 were used; the journeys project must run on a fresh stack because UJ-1 needs customer D unlinked.

## Process note

The QA run wrote its helper script and logs into the shared session scratchpad directory, which already held other sessions' files. The names `qa-run.sh`, `regression.log` and `lock.log` there belong to this run and replaced earlier files of the same names. Nothing under the repository was touched outside `e2e/tests/` (two copied specs) and this record.
