---
id: 6
type: story
title: "Linking shows its step, a six-cell code and one help block"
sources: [docs/ux/HANDOFF.md, docs/ux/DESIGN.md, docs/ux/EXPERIENCE.md, docs/ux/mockups/link.html, docs/ux/mockups/link-otp.html, docs/ux/mockups/link-otp-wrong.html, docs/ux/mockups/link-rate-limited.html, docs/ux/mockups/link-locked.html, docs/ux/mockups/app.css, docs/conventions/frontend.md, docs/prd/prd.md, docs/architecture/architecture.md, infra/images/gateway/templates/security-headers.conf.template]
companions: [docs/verification/ui-linking-prebuild-review.md, docs/verification/decisions.md]
after: [story-ui-notices-states]
refined: true
hitl: false
risk: medium
decisions: settled
---

# Linking shows its step, a six-cell code and one help block

## Description

The fourth area of the UI refresh (HANDOFF section 6, step 4). On `/link/otp` the code is one plain `Input`
(`features/linking/OtpScreen.tsx`); the human decided on 2026-11-08 that it becomes six cells built with shadcn's
Input OTP in the project's `new-york` (Radix) style. During a rate limit the inputs are not disabled and the submit
buttons are only `aria-disabled`, where EXPERIENCE asks for a disabled form. `ContactHelp.tsx` is one caption line,
and "Didn't get a code?" sits apart from it. This spec adds the step indicator, the six cells, the disabled
rate-limited form and the one help block.

input-otp 1.5.0 appends an empty `<style id="input-otp-style">` and fills it through the CSSOM. The gateway's
`style-src 'self'` blocks that element, so its autofill, selection and iOS rules would be lost. The human decided on
2026-11-08 (pre-build review D1, `docs/verification/decisions.md`) to add the hash of the empty string to the app
origin's `style-src`: it admits only an empty style element, so no injected CSS gets through.

- Tier: High-stakes (the code input on the linking path and a security header change), so the security agent
  reviews beside the reviewer and verifier. Owner: frontend, plus backend for the gateway: a backend-builder makes
  the gateway part (criterion 5's header, GatewayTests and AD-23 text) on the same branch, `feature/ui-linking`,
  since the frontend builder may not write under `infra/` or `docs/architecture/`; the two parts touch no file in
  common. Stacking: on `feature/ui-notices-states`.
- No API, route, auth, session, idempotency or retry change; the request body is still `{ code }` from `codeFrom`,
  and lockout, wrong-code and no-disclosure copy are unchanged.
- Covers: HANDOFF 3.4, 3.9; DESIGN Components (Step indicator, OTP input, Help block, Button disabled); EXPERIENCE
  Component Patterns (Step indicator, Help block, OTP input), State Patterns (Rate limited), Linking without
  disclosure, Accessibility Floor; architecture AD-23; PRD FR-4, FR-6, FR-7, FR-8, NFR-SEC-7, NFR-CODE-5,
  NFR-CODE-6.

## Inputs

No new request inputs: the verify request still sends `code`, six digits, and no body, query, header or route
parameter changes. The server bound is unchanged.

UI input, the one-time code: `otpSchema` is unchanged (exactly 6 digits, `0-9`). The field now holds at most 6
characters (`maxLength` 6) and only digits (`REGEXP_ONLY_DIGITS`, `^\d+$`; JavaScript `\d` without the `u` flag is
ASCII `0-9`), with a `pasteTransformer` and an `onPaste` handler that refuse a pasted code longer than 6 digits
(decided 2026-11-08, `docs/verification/decisions.md`):

- the `pasteTransformer` refuses any paste holding more than 6 digits, whatever else it holds (`4815167`,
  `4815 1678`, `481-5167`, `4815167\n`): it returns a single space, which `REGEXP_ONLY_DIGITS` refuses, so input-otp
  drops the paste whole and the cells keep their prior value, exactly as it drops `48151a`; otherwise it returns the
  text without spaces, so a paste with exactly 6 digits and other characters (`481516\n`) is accepted;
- the `onPaste` handler, which input-otp calls after its own paste listener, checks the same condition and calls
  `setError('code', { message: linkingMessages.code })`, as the 400 path already does, so the field shows today's
  six-digit error "Enter the 6-digit code." (`aria-invalid` and the field error; no error summary and no focus
  move until Verify); nothing is sent, so no attempt is spent;
- one small function holds the condition, so the transformer and the handler cannot disagree.

input-otp has no option to refuse an over-long paste: its paste listener always caps the value at `maxLength`
(input-otp 1.5.0, `src/input.tsx`, `_pasteListener`, lines 324 to 362; the `onPaste` order at lines 440 to 443).
This is why the refusal goes through its pattern check rather than a library option. Each case, from input-otp
1.5.0's paste and change listeners:

| Entry | Result in the cells | On Verify |
|---|---|---|
| typed 5 digits | 5 cells filled | "Enter the 6-digit code.", nothing sent (as today) |
| typed 6 digits | 6 cells filled, nothing sent | sends them |
| typed a 7th digit | ignored, 6 cells | sends the first 6 |
| typed a letter or a space | dropped | as for the digits held |
| pasted `481516` or `481 516` (6 digits after spaces) | `481516` | sends `481516` |
| pasted `4815167` or `4815 1678` (more than 6 digits after spaces) | refused: the cells keep their prior value; the field shows "Enter the 6-digit code." | as for what it held; no attempt spent |
| pasted `48151a`, `481-516` or `Your code is 481516` | the paste is ignored whole; the field keeps what it held | as for what it held |
| pasted `١٢٣٤٥٦` (Arabic-Indic digits) | ignored whole | as for what it held, as today |
| paste into a full field | the paste replaces the selected last character, then truncates to 6 (`481516` + `481516` gives `481514`) | sends what the cells show |
| autofill or a programmatic change of `481516` | `481516`, nothing sent | sends `481516` |

Package: `input-otp`, exactly pinned. At writing (2026-11-08) the latest stable is `1.5.0` (published 2026-08-18,
maintainer `guilhermerodz`, repository <https://github.com/guilhermerodz/input-otp>, MIT, no dependencies, no
install scripts, peer React `^16.8 || ^17.0 || ^18.0 || ^19.0.0 || ^19.0.0-rc`, met by 19.3.1). The shadcn CLI
installs with npm's default caret range and the frontend has no `save-exact` `.npmrc`, so the builder pins `1.5.0`
exactly after the CLI runs. The builder re-checks publisher, weekly downloads, repository and latest stable version
(AGENTS.md, Dependency verification; NFR-CODE-5) and records them in its report. Tool: the shadcn CLI pinned to
`shadcn@4.23.0`, as in `ui-notices-states`.

## Criteria

1. **Both linking screens show their step** (HANDOFF 3.4; EXPERIENCE Step indicator)
   **Given** `DetailsScreen.tsx` and the OTP form in `OtpScreen.tsx`
   **When** each renders
   **Then** `StepProgress` shows "Link your login, step 1 of 2" and "Link your login, step 2 of 2" above the `h1`;
   the locked state shows none. Mechanism: `StepProgress` (area 2). Source: WCAG 2.2 SC 1.1.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/non-text-content>).

2. **Input OTP is added in the project's style** (HANDOFF 3.9; NFR-CODE-5)
   **Given** `components/ui/input-otp.tsx`, added with `npx shadcn@4.23.0 add input-otp` against `components.json`
   (`style: new-york`); if the CLI adds any package other than `input-otp` or writes an import other than `cn` from
   `@/lib/utils`, the builder uses the shadcn docs' Manual installation instead
   **When** the diff is read
   **Then** `input-otp` is in `dependencies` at exactly `1.5.0` (or the re-checked latest stable), no other package is
   added, no colour value in `tokens.css` or `index.css` changes, and `InputOTPSeparator` is not kept (unused).
   Mechanism: shadcn Input OTP. Source: <https://ui.shadcn.com/docs/components/input-otp>,
   <https://ui.shadcn.com/docs/cli>.

3. **Six cells, one real input, the same submit** (HANDOFF 3.9; EXPERIENCE OTP input; WCAG 2.2 SC 3.2.2, 3.3.8)
   **Given** the OTP form
   **When** a person types, pastes or lets the phone autofill
   **Then** six cells sit over one real `input` with `id="code"`, the label "6-digit one-time code",
   `autocomplete="one-time-code"`, `inputmode="numeric"`, `maxLength` 6, `pattern={REGEXP_ONLY_DIGITS}`, the
   `pasteTransformer` and `onPaste` handler above (a pasted code longer than 6 digits is refused, never truncated)
   and `pushPasswordManagerStrategy="none"` (no password-manager timers, no widening, no
   `elementFromPoint` call in jsdom); the cell group is `aria-hidden="true"`; the help text sits under the cells and
   the input's `aria-describedby` holds the help text's id and, when shown, the error's id; `aria-invalid` is on the
   input; the error summary's link focuses it; filling the sixth cell sends nothing, and only Verify submits; each
   entry behaves as the Inputs table says
   **And** `OtpScreen.test.tsx` covers typed 5, 6 and 7 digits, pasted `481 516`, `48151a` and `١٢٣٤٥٦`, a paste
   into a full field, and a programmatic fill (`fireEvent.change`) that sends nothing until Verify, and asserts the
   input's accessible description with and without the error
   **And** it tests each side of the paste bound: pasted `481516` fills the cells and Verify sends it; pasted
   `4815167` into an empty field and into a field holding `481` leaves the cells as they were (`''` and `'481'`),
   shows "Enter the 6-digit code." with `aria-invalid="true"`, and sends no request (none on the paste, and Verify
   then refuses as for what the field holds)
   **And** exactly these existing assertions change, each to the value named: `:70-80` "no length limit" becomes
   `maxlength="6"`; `:94-105` `toHaveValue('481 516')` becomes `'481516'`; `:137-159` "refuses seven digits" moves
   out for typed entry (a typed 7th digit is ignored, the field holds `481516` and Verify sends it; the pasted seven
   digits are refused by the new paste tests above), and "refuses a letter" expects the value `'48151'` and the
   5-digit refusal; `:151` `toHaveAccessibleDescription('Enter the 6-digit code.')` becomes the help text plus the
   error; `:63` takes criterion 7's help text; every `document.body.textContent).not.toContain(<code>)` taken while
   the code is still in the field (`:158`, `:199`, `:224`, `:493`) is rescoped to the elements that could echo the
   code (the alert, the error summary, the field error), never deleted; the rate-limit assertions change as
   criterion 6 says. Every other existing OTP test passes unchanged. Mechanism: input-otp's single input and its
   documented props (`pattern`, `pasteTransformer`, `onPaste`); React Hook Form `setError`. Source: input-otp README
   (<https://github.com/guilhermerodz/input-otp>) and its Validation page
   (<https://input-otp.rodz.dev/docs/validation>, `pattern` and `pasteTransformer`); its 1.5.0 source,
   `packages/input-otp/src/input.tsx`, the paste listener; React Hook Form,
   setError (<https://react-hook-form.com/docs/useform/seterror>); MDN, `autocomplete`
   (<https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Attributes/autocomplete>); WCAG 2.2 SC 3.2.2
   (<https://www.w3.org/WAI/WCAG22/Understanding/on-input>), SC 3.3.8
   (<https://www.w3.org/WAI/WCAG22/Understanding/accessible-authentication-minimum>).

4. **The cells are styled to the handoff** (HANDOFF 3.9; DESIGN OTP input; `mockups/app.css` `.otp`)
   **Given** the six cells at 360px and 1280px
   **When** one cell is active, or the code is invalid
   **Then** each cell is `rounded-md` with 24/800 digits and at most 48px wide on a phone; the active cell has a 2px
   `ring` border and a 4px `primary-subtle` halo; after a wrong code or a 400 each cell takes the `destructive`
   border (the slots receive `aria-invalid`, as the plain `Input` does today); the fake caret is
   `motion-reduce:animate-none`; theme tokens only. Mechanism: visible focus. Source: WCAG 2.2 SC 2.4.7
   (<https://www.w3.org/WAI/WCAG22/Understanding/focus-visible>), SC 2.3.3
   (<https://www.w3.org/WAI/WCAG22/Understanding/animation-from-interactions>).

5. **The app CSP admits only an empty style element, and `/link/otp` has no violation** (decision 2026-11-08, D1;
   AD-23; NFR-SEC-7)
   **Given** the gateway's `infra/images/gateway/templates/security-headers.conf.template`
   **When** the app origin answers, on both gateway images (local and AWS)
   **Then** its CSP's `style-src` is exactly `'self' 'sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU='` and every
   other directive is unchanged; the Scalar locations' CSP is unchanged
   **And** `AppOrigin_ContentSecurityPolicies_HoldBaseUriNone` in
   `infra/tests/DisputeDesk.PlatformTests/GatewayTests.cs` asserts that exact app CSP, and a GatewayTests test
   asserts that the template's `style-src` hash equals `'sha256-' + Convert.ToBase64String(SHA256.HashData([]))`,
   the hash of the empty string, recomputed in the test
   **And** AD-23 in `docs/architecture/architecture.md` states the hash, that it admits only an empty style element,
   and why (input-otp's runtime element)
   **And** in the running stack, an e2e test on `/link/otp` (listening as in `dispute-raise.spec.ts:697`) records
   zero `securitypolicyviolation` events and no console error after load, focus, typing and paste, and the digits
   show in the cells. Mechanism: CSP Level 3 hash-source, as AD-23 already uses for Scalar's script. Source: W3C CSP
   Level 3, hash-source (<https://www.w3.org/TR/CSP3/#grammardef-hash-source>) and violation events
   (<https://www.w3.org/TR/CSP3/#violation-events>); MDN, `securitypolicyviolation`
   (<https://developer.mozilla.org/en-US/docs/Web/API/Element/securitypolicyviolation_event>).

6. **A rate limit really disables the form, and focus is never lost** (HANDOFF 3.9; EXPERIENCE Rate limited; DESIGN
   Button)
   **Given** a 429 with a `Retry-After` on `/link` or `/link/otp`
   **When** the wait runs
   **Then** the inputs and the submit button (Continue on `/link`, Verify on `/link/otp`) have the `disabled`
   attribute, drawn with `muted` fill, `border` edge and `muted-foreground` text, never reduced opacity: those
   `disabled:` styles live in the shared `components/ui/button.tsx` and `input.tsx`, and the cells take them through
   the container's `has-disabled:` variant in place of the registry's `has-disabled:opacity-50`; focus moves to the
   rate-limited notice (`tabIndex={-1}`)
   **And** when the wait ends the controls are enabled again; if the notice still has focus as it unmounts, focus
   moves to the submit button; if focus is elsewhere (a help link, the header), it stays there
   **And** without `Retry-After` nothing is disabled and focus stays on the button; a pending request still uses
   `aria-disabled` and keeps focus, as today
   **And** tests on both screens cover a 429 after a button press, after Enter in a field, the end of the wait with
   focus on the notice and with focus moved elsewhere, no `Retry-After`, and a pending press; the existing
   `OtpScreen.test.tsx:448-449` and `DetailsScreen.test.tsx:334-335`, `:371`, `:374` change from `aria-disabled` and
   focus on the button to `disabled` and focus on the notice; `linking-screens.spec.ts` proves in Chromium, with a
   stubbed 429 and `Retry-After: 1`, that focus is never on `body`. Mechanism: the HTML `disabled` attribute and
   focus management. Source: HTML Standard, Enabling and disabling form controls
   (<https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#enabling-and-disabling-form-controls:-the-disabled-attribute>);
   WCAG 2.2 SC 2.4.3 (<https://www.w3.org/WAI/WCAG22/Understanding/focus-order>).

7. **One help block on every linking state** (HANDOFF 3.9; EXPERIENCE Help block; WCAG 2.2 SC 3.2.6)
   **Given** `/link`, the OTP form, the locked state and the rate-limited state
   **When** each renders
   **Then** each ends with the same block after a hairline: a `section` named by its `h2` "Need help?" (16/700,
   `aria-labelledby`), then on the OTP form only "Didn't get a code? Start again." (link to `/link`), then "Can't
   link online? Contact the bank."; the OTP help text under the cells reads "The code expires 5 minutes after we
   send it. You can paste it, or let your phone fill it in."
   **And** a test asserts the region's name and the heading order (`h1`, then `h2` "Need help?") on each state.
   Mechanism: consistent help. Source: <https://www.w3.org/WAI/WCAG22/Understanding/consistent-help>.

## Boundaries

- No change to API calls, the generated client, routes, auth, session, the challenge, lockout, idempotency, retry,
  query keys or telemetry; `gcTime: 0` on the verify mutation stays.
- Security headers: only the app origin's `style-src` changes, by the one hash in criterion 5; no other header or
  directive changes, the Scalar CSP is unchanged, and no `'unsafe-inline'` or nonce is added. Radix's scroll-lock
  `<style>` holds CSS text, so it stays blocked as accepted in story 2.5, and the `alert-dialog.tsx` shortcut stays.
- Theme tokens only: no raw hex or px value in a component. Icons: Lucide, 16px unless HANDOFF names another size,
  2px stroke, `currentColor`, `aria-hidden="true"`.
- Touches, frontend builder (under `frontend/src/`): `components/ui/input-otp.tsx` (new), `components/ui/button.tsx`
  and `input.tsx` (the `disabled:` styles), `features/linking/DetailsScreen.tsx`, `OtpScreen.tsx`, `ContactHelp.tsx`,
  their tests and `linkingPrivacy.test.tsx` if it reads the input; the comments that quote the old policy in
  `components/ui/alert-dialog.tsx:7` and `features/disputes/raise/RaiseScreen.test.tsx:435`; the package files.
- Touches, backend builder: `infra/images/gateway/templates/security-headers.conf.template`,
  `infra/tests/DisputeDesk.PlatformTests/GatewayTests.cs`, `docs/architecture/architecture.md` (AD-23 only).

## Tests and commits

- QA updates `e2e/tests/linking-screens.spec.ts` and every linking spec that types or reads the code:
  `linking-verify.spec.ts`, `linking-start.spec.ts`, `linking-rate-limits.spec.ts`, `journeys/uj-1.spec.ts` and
  `support.ts`, and adds criterion 5's zero-violation check and criterion 6's focus check.
- QA updates the CSP expectations the new header changes: `gatewayContentSecurityPolicy` in `e2e/tests/support.ts`
  (read by `dispute-raise.spec.ts:726` and `spa-transactions.spec.ts:66`), and the `knownRadixStyleViolation`
  pattern in `dispute-raise.spec.ts`, `spa-transactions.spec.ts`, `dispute-detail.spec.ts`,
  `spa-disputes-history.spec.ts` and `spa-dispute-status-badges.spec.ts`, whose quoted directive now includes the
  hash; each still matches Radix's violation only.
- QA adapts `linking-screens.spec.ts` without weakening it: for the OTP input the visible focus indicator is the
  `[data-active="true"]` cell's ring and halo, checked on that cell (input-otp sets `outline: 0` and
  `box-shadow: none` on the real input, `:211-213`); disabled controls leave the reachable count (`:223`) only while
  disabled; the "fields stay editable" assertions (`:376-381`, `:418-419`) invert to `toBeDisabled()`, and the forced
  click on the waiting button goes.
- The pull request holds the presentation change, the gateway change, then `test(ui-linking): update assertions for
  the UI refresh` with every unit-test and e2e assertion update. The squash merge joins them on main.

## Verify

- Frontend builder, in `frontend/`: `npm run typecheck`, `lint`, `format`, `test` and `build` pass; the glyph grep
  (`grep -rnE '➚|◔|▲|✓|✕|↩|ⓘ|⚠|☰|⚑' frontend/src`) prints nothing; the diff has no raw hex or px in a component;
  `input-otp` is pinned exactly.
- Backend builder: the `infra/tests` platform tests pass with the new GatewayTests assertions.
- Security agent: reviews the `style-src` change against CSP Level 3 and AD-23.
- QA, holding the `stack` lock: `e2e/run-regression.sh` passes with axe on every spec that runs it; with the
  Playwright MCP each linking screen and state matches its mockup at 360px and 1280px in light and dark; no
  horizontal scroll at 320px or 200% zoom; every interactive element is at least 44px tall; keyboard order is
  unchanged; paste and typed entry both link a seeded customer; the browser console on `/link/otp` shows no CSP
  violation.

## Notes

- Decision (2026-11-08, `docs/verification/decisions.md`): a pasted code longer than 6 digits is refused, not
  truncated. The cells keep their prior value and the field shows today's six-digit error; no attempt is spent, so
  `otpSchema`, the submit and the three-attempt limit are unchanged. This replaces the earlier truncation
  assumption. A typed 7th digit is still ignored (the cells hold 6), and a paste of 6 or fewer digits that overflows
  a part-filled field is still capped by input-otp, as the Inputs table says; the bound is on the pasted text.

## Sofia's refine

Sofia, 2026-11-08, on `docs/verification/ui-linking-prebuild-review.md`. D1 `[decision]` answered by the human
(option B, `docs/verification/decisions.md`); every `[story]` point fixed:

- S1 high, criterion 5: rewritten as option B's deterministic check (exact `style-src`, the recomputed empty-string
  hash in GatewayTests, AD-23 text, zero violations on `/link/otp`); owner adds backend for the gateway; Touches and
  the e2e CSP expectations named.
- S2 medium, changed assertions: criterion 3 names each, with its new value; criterion 6 the rate-limit ones.
- S3 medium, input bounds: the Inputs table states each case; criterion 3 names `REGEXP_ONLY_DIGITS`, the
  `pasteTransformer` and the tests on each side of the bound. Truncation was first accepted; the human then decided
  (2026-11-08) that an over-long paste is refused, see the update below.
- S4 medium, focus at the end of the wait: criterion 6, the submit button when the notice had focus, else unchanged;
  tests on both screens and the Chromium e2e.
- S5 medium, password-manager probe: `pushPasswordManagerStrategy="none"`.
- S6 medium, e2e keyboard check: Tests and commits tells QA how to adapt each assertion.
- S7 low, disabled look: `disabled:` styles in `button.tsx` and `input.tsx`, cells through `has-disabled:`; Touches.
- S8 low, cells and hint: group `aria-hidden`, hint id in `aria-describedby`, description test.
- S9 low, caret and separator: `motion-reduce:animate-none`; separator dropped.
- S10 low, invalid cells: the `destructive` border through `aria-invalid` on the slots.
- S11 low, package facts and help heading: peer range corrected, exact pin after the CLI; "Need help?" is an `h2`
  naming a `section`.

Update, Sofia, 2026-11-08, on the human decision that a pasted code longer than 6 digits is refused
(`docs/verification/decisions.md`): the Inputs table's paste rows, criterion 3 (the `pasteTransformer` and `onPaste`
mechanism with its input-otp 1.5.0 source, and tests on both sides of the bound: 6 digits accepted, 7 refused with
the cells unchanged, the error shown and no request) and Notes (the truncation assumption replaced by the decision)
now say so.
