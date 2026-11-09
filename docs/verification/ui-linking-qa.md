# QA: ui-linking

- Commit tested: `2f897c37b3b58b11119c6126123213f98defeb43` plus the uncommitted tree (diff hash `5d6b9dc2aaf0` with the e2e edits below; the builder's own hash was `bc234a527a9a` before them).
- Worktree: `/Users/njabulomnisi/Projects/Dojo/DisputeDesk/.claude/worktrees/agent-a718a1e0fc36c5203`
- Date (UTC): 2026-11-08. Lock `stack`, owner `qa-ui-linking`, taken after `qa-ui-lists` released it; OTLP ports 14318/14317 (4318 was taken). Stack stopped, lock released, no ZAP.
- Verdict: **pass**. No high or medium finding; four low observations below.

## 1. e2e changes (e2e/ only, nothing weakened)

- `support.ts` `gatewayContentSecurityPolicy`: style-src is now `'self' 'sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU='`.
- `knownRadixStyleViolation` in `dispute-raise`, `spa-transactions`, `dispute-detail`, `spa-disputes-history`, `spa-dispute-status-badges`: quotes the directive with the hash (the `+` and `/` escaped); still matches Radix's violation only.
- Copied the base's two glyph edits (`spa-disputes-history`, `spa-transactions`) from `agent-ae1ed41bc1abcb91e`.
- `linking-screens.spec.ts`:
  - keyboard pass checks the `[data-active="true"]` cell (border at least 2px, box-shadow set) for the code input; disabled controls count as stops only while enabled.
  - `toBeDisabled()` on the code input, Verify, ID number, account number and Continue under a rate limit, plus focus on the notice; the forced clicks and "still editable" fills are gone and replaced by "no request in a 700 ms window".
  - `toBeEnabled()` for the no-`Retry-After` case; the new expiry and help text; the "Need help?" region, then h1 first and "Need help?" last (the error summary's h2 is the only heading allowed between) on every `checkState` (form, errors, locked, rate-limited); step label, six cells, `maxlength` 6, `id=code`, `aria-hidden` group.
  - `pasteInto` dispatches a real paste event (`DataTransfer`, `text/plain`) for "111 111" and the linking code (the cells are asserted); `clearCode` empties by select-all + Backspace. A typed letter after five digits is asserted dropped (`12345`).
  - new tests: focus never on `body` with stubbed 429 and `Retry-After: 1`, sampled every 5 ms, on `/link/otp` (notice, then Verify) and `/link` (notice, then Continue); zero `securitypolicyviolation` events and no console error on `/link/otp` after load, focus, typing and paste, with the app CSP header asserted; a 7-digit paste refused (empty field and a field holding `481`), no summary, focus stays, no request, six digits plus newline accepted, `48151a` ignored.
- `npm ci` and `npm run lint` in `e2e/`: clean.
- Two test-side corrections made on the way (not product defects): Playwright `fill('')` is not applied by input-otp after an error, so the clear is done with keys; the refusal error is asserted on `#code-error` because the same text also goes to the live region (WCAG 4.1.3), which is why `getByText` saw two.

## 2. Suite on the rebuilt stack (`--build`, this worktree)

`e2e/run-regression.sh` under the lock. D = customer D's OTP sends (cap 3 an hour).

| Project | Result | D sends |
|---|---|---|
| linking-rate-limits | 7 passed | 3 |
| linking-screens (first run) | 1 failed, 8 did not run: my new heading-order assertion forbade the error summary's h2 | 0 |
| linking-queue | 1 passed | 3 |
| linking-verify-a | 6 passed | 2 |
| linking-verify-b | 3 passed | 2 |
| linking-verify-c | 2 passed | 2 |
| regression (all other specs, including the five CSP specs) | 240 passed, 1 skipped (`scalar-disabled`, needs its own compose) | 3 |
| linking-screens, re-run on a fresh stack after the assertion fix and two more test-side fixes | **9 passed** | 2 |
| journeys (fresh stack, uj-1 types the six-digit code into the cells) | 5 passed | 1 |

The linking-screens re-run went through two intermediate failures, both in my new assertions (above), before its final 9 of 9. axe ran on every state in the screens group at 1280 and 320 (2a empty, field errors, rate-limited; 2b initial, field error, wrong code with 2 and 1 tries left, locked, rate-limited): zero violations, no horizontal scroll at 320, keyboard order unchanged (the code input is one stop; the cells are `aria-hidden`). The other groups ran their own axe checks and passed. The privacy scan (no typed value or code in URLs, spans, logs, Api logs or topic) passed.

## 3. Browser pass (Playwright MCP, fresh stack, throwaway logins)

Screenshots (no personal data; fixture ID numbers were never typed into a captured frame, the 2a rate-limited frames that showed a typed number were deleted): `/Users/njabulomnisi/Projects/Dojo/DisputeDesk/docs/verification/ui-linking-qa/` (`link-*`, `otp-*`, `locked-*`).

| Check | Observed |
|---|---|
| `/link` at 320, 360, 1280, light and dark | "Link your login, step 1 of 2" above the h1; inputs and Continue 48px tall; scrollWidth minus clientWidth 0; headings h1 then h2 "Need help?" |
| `/link/otp` initial | step "2 of 2"; six cells `rounded-md` (12px radius), 24px/800 digits; cell width 41 (320), 48 (360), 56 (1280), height 56 (64 from 768px); `maxlength` 6, `inputmode` numeric, `autocomplete` one-time-code, `aria-describedby` `code-hint`; hint reads "The code expires 5 minutes after we send it. You can paste it, or let your phone fill it in."; no overflow |
| Active cell | 2px border `rgb(43,87,201)` (light) / `rgb(140,170,247)` (dark) and a 4px halo `rgb(232,238,251)` / `rgb(34,53,95)`; none on other cells |
| Invalid | each cell border 1px `rgb(180,35,24)` light, `rgb(255,138,126)` dark; `aria-invalid` true on the input; `aria-describedby` becomes `code-hint code-error` |
| Pastes on a fresh page | `481 516` gives `481516`; `481516\n` gives `481516`; `4815167` and `4815167\n` refused: cells empty, "Enter the 6-digit code." under the field, `aria-invalid` true, no summary, focus on the input, no linking request; `48151a`, `481-516`, `Your code is 481516` and `١٢٣٤٥٦` ignored whole; `481516a` gives `481516` (finding L1) |
| Wrong code (real server) | alert "That code didn't work. You have 2 tries left."; cells emptied; focus on the code input; cell border destructive |
| Rate limited (stubbed 429, `Retry-After` 900) on 2b and 2a | `disabled` on the code input and Verify or Continue; fill `muted` (`rgb(238,240,243)` light, `rgb(42,52,65)` dark), edge `border` (`rgb(225,228,234)` / `rgb(50,61,75)`), text `muted-foreground`, opacity 1 on button and cells; focus on the `role=alert` notice; no overflow. (A computed-style read in the first 300 ms shows the primary fill because of the colour transition; it settles to the muted values.) |
| Locked (stubbed 409) | no step indicator; h1 "We can't link you online" focused; "Need help?" region after it; no overflow at 320, 360, 1280 in both schemes |
| CSP header (curl and browser) on `/` and `/link/otp` | `style-src 'self' 'sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU='`, every other directive as before |
| `/link/otp` events | zero `securitypolicyviolation` events and zero console errors after load, focus, typing and paste; `#input-otp-style` present with 5 CSSOM rules and empty text (admitted by the hash) |

Note on the gateway: its coarse limiter (10 requests a second, burst 20 per client) returned 429 on `/api/v1/antiforgery` while my first probe loop ran fast; I slowed the loop. A user hitting it sees no notice on the OTP form (the Verify press does nothing visible). That is outside this story's change, so it is reported as an observation (L4), not a finding against it.

### WCAG 2.5.8 and the "Start again" link

The link is 81 by 22 CSS px at 320, 360 and 1280, below the 24px minimum. The inline exception of SC 2.5.8 (Target Size (Minimum)) applies: the target is in a sentence, "Didn't get a code? Start again.", and its size is constrained by the line height of the surrounding text. No spacing workaround is needed. Not a finding. (Source: <https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum>.) The criterion's 44px-tall check holds for the form controls (inputs and the button, 48px).

## 4. Four hops, one successful link (login `qa-ul-1`, customer D, fresh stack)

1. Browser: `/link` then Continue goes to `/link/otp` (step 2 of 2); a wrong code `111111` shows "You have 2 tries left."; the real code pasted as `ddd ddd` fills six cells; Verify lands on `/transactions`.
2. Network (CSRF header on each POST, no query strings): `POST /api/v1/linking/start` 202; `POST /api/v1/linking/verify` 422 (otp-incorrect); `POST /api/v1/linking/verify` 204; `GET /api/v1/transactions` 200. The request bodies were `{idNumber, accountNumber}` and `{code}` (a six-digit string).
3. Database (`qa_readonly`): after start, `LinkingChallenges` Attempts 0, OtpHmac present (a hash, no plaintext), sent, not consumed; audit `LinkingStart|Succeeded`, `LinkingOtpSent|Succeeded`. After verify: Attempts 2, consumed true, not superseded; `CustomerLinks` qa-ul-1 to `CUS-000004`; audit `SignIn|Succeeded`, `LinkingStart`, `LinkingOtpSent`, `LinkingOtpIncorrect|Rejected`, `Linked|Succeeded`.
4. Events: linking publishes nothing on `disputedesk.disputes`; the automated check (`linking-screens` login 3, "linking emits no event on the topic": topic count unchanged across the link) passed. I did not take a before-count in the manual pass.

Not run by hand: the other-customer 404 and resolved-dispute 409 checks (not part of this story's surface; the regression group covers them: `dispute-detail`, `dispute-withdraw`, both passed).

## Findings

| # | Rank | Finding |
|---|---|---|
| L1 | low | A paste of six digits followed by a non-digit, `481516a`, is accepted as `481516` with no error. input-otp 1.5.0 truncates to `maxLength` before it tests the pattern, so a trailing non-digit hides. The spec's table covers `48151a` (ignored whole) and more-than-six digits (refused) but not this; the effect is harmless (the cells show what Verify sends) but it differs from the "never truncate" principle of the decision. |
| L2 | low | At 320px the active cell is 43px wide against 41px for the others (the `-m-px` that is meant to keep widths equal widens it by 2px when the row is width-constrained). Equal at 360 and 1280. |
| L3 | low | After a refused paste the field error stays under six valid digits until the next Verify (`reValidateMode: onSubmit`, the repo's GOV.UK pattern), so a person who pastes `4815167` and then `481516` sees "Enter the 6-digit code." beside a full code. |
| L4 | low (outside the story) | When the gateway's coarse limiter answers `/api/v1/antiforgery` with 429, pressing Verify shows no notice. Pre-existing, not changed by this diff. |

## Assumptions and notes

- The base's e2e glyph edits were copied from `agent-ae1ed41bc1abcb91e`; the diff is the same two lines the build record names.
- Throwaway Zitadel logins `qa.ul1` to `qa.ul3` were created for the browser pass in the volumes the run removed; the helper and run scripts were deleted from `e2e/`.
- Nothing committed, pushed, or edited outside `e2e/` and `docs/verification/`.
