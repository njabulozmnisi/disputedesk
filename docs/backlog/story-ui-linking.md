---
id: 6
type: story
title: "Linking shows its step, a six-cell code and one help block"
sources: [docs/ux/HANDOFF.md, docs/ux/DESIGN.md, docs/ux/EXPERIENCE.md, docs/ux/mockups/link.html, docs/ux/mockups/link-otp.html, docs/ux/mockups/link-otp-wrong.html, docs/ux/mockups/link-rate-limited.html, docs/ux/mockups/link-locked.html, docs/ux/mockups/app.css, docs/conventions/frontend.md, docs/prd/prd.md, infra/images/gateway/templates/security-headers.conf.template]
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

- Tier: High-stakes (the code input on the linking path changes), so the security agent reviews beside the reviewer
  and verifier. Owner: frontend. Stacking: on `feature/ui-notices-states`.
- Presentation only: no API, route, auth, session, idempotency or retry change; the request body is still
  `{ code }` from `codeFrom`, and lockout, wrong-code and no-disclosure copy are unchanged.
- Covers: HANDOFF 3.4, 3.9; DESIGN Components (Step indicator, OTP input, Help block, Button disabled); EXPERIENCE
  Component Patterns (Step indicator, Help block, OTP input), State Patterns (Rate limited), Linking without
  disclosure, Accessibility Floor; PRD FR-4, FR-6, FR-7, FR-8, NFR-CODE-5, NFR-CODE-6.

## Inputs

No new request inputs: the verify request still sends `code`, six digits, and no body, query, header or route
parameter changes. UI input, the one-time code: exactly 6 digits, `0-9` only (`otpSchema` is unchanged). Pasted
spaces are removed ("481 516" fills six cells); any other character is not accepted; fewer than six digits shows
"Enter the 6-digit code." on Verify, as today.

Package: `input-otp`, exactly pinned, added by the shadcn CLI. At writing (2026-11-08) the latest stable is `1.5.0`
(repository <https://github.com/guilhermerodz/input-otp>, peer React `^19.0.0`, met by 19.3.1); the builder
re-checks publisher, weekly downloads, repository and latest stable version (AGENTS.md, Dependency verification;
NFR-CODE-5) and records them in its report.

## Criteria

1. **Both linking screens show their step** (HANDOFF 3.4; EXPERIENCE Step indicator)
   **Given** `DetailsScreen.tsx` and the OTP form in `OtpScreen.tsx`
   **When** each renders
   **Then** `StepProgress` shows "Link your login, step 1 of 2" and "Link your login, step 2 of 2" above the `h1`;
   the locked state shows none. Mechanism: `StepProgress` (area 2). Source: WCAG 2.2 SC 1.1.1
   (<https://www.w3.org/WAI/WCAG22/Understanding/non-text-content>).

2. **Input OTP is added in the project's style** (HANDOFF 3.9; NFR-CODE-5)
   **Given** `components/ui/input-otp.tsx`, added with `npx shadcn@<exact version> add input-otp` against
   `components.json` (`style: new-york`)
   **When** the diff is read
   **Then** `input-otp` is in `dependencies` with an exact version, no other package is added, and no colour value in
   `tokens.css` or `index.css` changes. Mechanism: shadcn Input OTP. Source:
   <https://ui.shadcn.com/docs/components/input-otp>, <https://ui.shadcn.com/docs/cli>.

3. **Six cells, one real input, the same behaviour** (HANDOFF 3.9; EXPERIENCE OTP input; WCAG 2.2 SC 3.2.2, 3.3.8)
   **Given** the OTP form
   **When** a person types, pastes or lets the phone autofill
   **Then** six cells sit over one real `input` with `id="code"`, the label "6-digit one-time code",
   `autocomplete="one-time-code"`, `inputmode="numeric"`, `maxLength` 6, the digits-only pattern, and a
   `pasteTransformer` that removes spaces; `aria-invalid` and `aria-describedby` are on that input; the error
   summary's link focuses it; filling the sixth cell sends nothing, and only Verify submits
   **And** `OtpScreen.test.tsx` replaces the "no length limit" assertion with the six-digit bound and the
   "481 516" paste; every other existing OTP test passes unchanged. Mechanism: input-otp's single input. Source:
   input-otp README (<https://github.com/guilhermerodz/input-otp>); MDN, `autocomplete`
   (<https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Attributes/autocomplete>); WCAG 2.2 SC 3.2.2
   (<https://www.w3.org/WAI/WCAG22/Understanding/on-input>), SC 3.3.8
   (<https://www.w3.org/WAI/WCAG22/Understanding/accessible-authentication-minimum>).

4. **The cells are styled to the handoff** (HANDOFF 3.9; DESIGN OTP input; `mockups/app.css` `.otp`)
   **Given** the six cells at 360px and 1280px
   **When** one cell is active
   **Then** each cell is `rounded-md` with 24/800 digits and at most 48px wide on a phone; the active cell has a 2px
   `ring` border and a 4px `primary-subtle` halo, in theme tokens only. Mechanism: visible focus. Source: WCAG 2.2
   SC 2.4.7 (<https://www.w3.org/WAI/WCAG22/Understanding/focus-visible>).

5. **The gateway's CSP still holds** (security headers `style-src 'self'`)
   **Given** the Compose stack behind the gateway, whose CSP is unchanged
   **When** `/link/otp` loads and a code is typed
   **Then** no `securitypolicyviolation` event fires (listened for as in `dispute-raise.spec.ts:697`) and the digits
   show in the cells. input-otp 1.5.0 appends a `<style id="input-otp-style">` element and adds its rules through
   CSSOM `insertRule`; if the browser reports a violation, the stage returns `blocked`, and the CSP is never
   loosened. Mechanism: CSP violation events. Source: W3C CSP Level 3
   (<https://www.w3.org/TR/CSP3/#violation-events>); MDN, `securitypolicyviolation`
   (<https://developer.mozilla.org/en-US/docs/Web/API/Element/securitypolicyviolation_event>).

6. **A rate limit really disables the form** (HANDOFF 3.9; EXPERIENCE Rate limited; DESIGN Button)
   **Given** a 429 with a `Retry-After` on `/link` or `/link/otp`
   **When** the wait runs
   **Then** the inputs and the submit button (Continue on `/link`, Verify on `/link/otp`) have the `disabled`
   attribute, drawn with `muted` fill, `border` edge and `muted-foreground` text, never reduced opacity; focus
   moves to the rate-limited notice (`tabIndex={-1}`), so it is never lost; when the wait ends they are enabled again
   **And** without `Retry-After` nothing is disabled, and a pending request still uses `aria-disabled`, as today.
   Mechanism: the HTML `disabled` attribute. Source: HTML Standard, Enabling and disabling form controls
   (<https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#enabling-and-disabling-form-controls:-the-disabled-attribute>);
   WCAG 2.2 SC 2.4.3 (<https://www.w3.org/WAI/WCAG22/Understanding/focus-order>).

7. **One help block on every linking state** (HANDOFF 3.9; EXPERIENCE Help block; WCAG 2.2 SC 3.2.6)
   **Given** `/link`, the OTP form, the locked state and the rate-limited state
   **When** each renders
   **Then** each ends with the same block after a hairline: "Need help?" (16/700), then on the OTP form only "Didn't
   get a code? Start again." (link to `/link`), then "Can't link online? Contact the bank."; the OTP help text reads
   "The code expires 5 minutes after we send it. You can paste it, or let your phone fill it in."
   Mechanism: consistent help. Source: <https://www.w3.org/WAI/WCAG22/Understanding/consistent-help>.

## Boundaries

- Presentation only: no change to API calls, the generated client, routes, auth, session, the challenge, lockout,
  idempotency, retry, query keys or telemetry; `gcTime: 0` on the verify mutation stays.
- The gateway's security headers are not changed. Theme tokens only: no raw hex or px value in a component. Icons:
  Lucide, 16px unless HANDOFF names another size, 2px stroke, `currentColor`, `aria-hidden="true"`.
- Touches (under `frontend/src/`): `components/ui/input-otp.tsx` (new), `features/linking/DetailsScreen.tsx`,
  `OtpScreen.tsx`, `ContactHelp.tsx`, their tests and `linkingPrivacy.test.tsx` if it reads the input; the package
  files.

## Tests and commits

- QA updates `e2e/tests/linking-screens.spec.ts` and every linking spec that types or reads the code:
  `linking-verify.spec.ts`, `linking-start.spec.ts`, `linking-rate-limits.spec.ts`, `journeys/uj-1.spec.ts` and
  `support.ts`, and adds the criterion 5 check.
- The pull request holds two commits: the presentation change, then `test(ui-linking): update assertions for the UI
  refresh` with every unit-test and e2e assertion update. The squash merge joins them on main.

## Verify

- Builder, in `frontend/`: `npm run typecheck`, `lint`, `format`, `test` and `build` pass; the glyph grep
  (`grep -rn '[➚◔▲✓✕↩ⓘ⚠☰⚑]' frontend/src`) prints nothing; the diff has no raw hex or px in a component.
- QA, holding the `stack` lock: `e2e/run-regression.sh` passes with axe on every spec that runs it; with the
  Playwright MCP each linking screen and state matches its mockup at 360px and 1280px in light and dark; no
  horizontal scroll at 320px or 200% zoom; every interactive element is at least 44px tall; keyboard order is
  unchanged; paste and typed entry both link a seeded customer.
