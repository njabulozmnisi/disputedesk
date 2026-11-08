# ui-linking: pre-build review

- Date: 2026-11-08
- Story: `docs/backlog/story-ui-linking.md` (branch `feature/ux-refresh-docs`, commit 6888591), High-stakes, stacked on
  `ui-notices-states`.
- Read against: `docs/ux/HANDOFF.md` (3.4, 3.7, 3.9, 5, 6), `docs/ux/DESIGN.md` (Shapes, Components: Button, Step
  indicator, Help block, OTP input), `docs/ux/EXPERIENCE.md` (Component Patterns, State Patterns, Linking without
  disclosure, Accessibility Floor), `mockups/link*.html` and `mockups/app.css`, `docs/backlog/story-ui-notices-states.md`
  and its pre-build review, `docs/verification/decisions.md` (the 2026-11-08 ui-linking row on the branch),
  architecture AD-23, PRD NFR-SEC-7, NFR-CODE-5, the story 2.5 CSP decision, and the code under `frontend/src/features/linking/**`
  and its tests, `components/ui/{button,input}.tsx`, `components/ProblemNotice.tsx`, `test/setup.ts`,
  `infra/images/gateway/templates/security-headers.conf.template`, `infra/tests/DisputeDesk.PlatformTests/GatewayTests.cs`
  and the linking e2e specs, with Pass 1, Pass 2 and the security checklist.
- Package evidence: the `input-otp@1.5.0` tarball (`npm pack`, not installed, not run), its original source read from
  `dist/index.mjs.map`; `npm view` and the npm downloads API; the shadcn registry entry
  `https://ui.shadcn.com/r/styles/new-york-v4/input-otp.json` (read 2026-11-08).
- Browser evidence: a CSP probe run in Playwright's Chromium 156.0.8078.4 (the repo's installed e2e browser), serving a
  page with the gateway's `style-src 'self'` and running input-otp's exact injection steps (create
  `<style id="input-otp-style">`, append to `head`, `insertRule` if `sheet` exists). Firefox and WebKit are not installed
  and were not probed.
- Result: **changes**. One `[decision]` point (high), six medium `[story]` points and five low `[story]` points. The
  story is not ready until the human answers D1 and Sofia rewrites criterion 5 to match.

## Points

### D1 [decision] high: input-otp's runtime `<style>` is blocked by the gateway CSP, so criterion 5 fails as written

What the package does (`src/input.tsx` in the 1.5.0 source map, mount effect):

```ts
if (!document.getElementById('input-otp-style')) {
  const styleEl = document.createElement('style')
  styleEl.id = 'input-otp-style'
  if (nonce) styleEl.setAttribute('nonce', nonce)
  document.head.appendChild(styleEl)
  if (styleEl.sheet) { safeInsertRule(styleEl.sheet, '[data-input-otp]::selection { … }') /* + :autofill, -webkit-autofill, iOS, PWM badge */ }
}
```

What Chromium 156 does under `style-src 'self'` (probe, verified):

| Policy | `securitypolicyviolation` | `styleEl.sheet` | Rules inserted | Console |
|---|---|---|---|---|
| `style-src 'self'` (today) | one, `style-src-elem`, blockedURI `inline` | `null` | none | "Applying inline style violates the following Content Security Policy directive 'style-src 'self''. Either the 'unsafe-inline' keyword, a hash ('sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU='), or a nonce … is required" |
| `style-src 'self' 'sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU='` | none | present | yes (`insertRule` is CSSOM, not checked by CSP) | none |
| `style-src 'self'`, page already holds `<link id="input-otp-style" rel="stylesheet" href="/…css">` | none | (library skips) | n/a | none |

So the empty element alone is a violation, and criterion 5 (`story-ui-linking.md:83-92`: "no `securitypolicyviolation`
event fires … if the browser reports a violation, the stage returns `blocked`") is known to fail before the build
starts. The field itself still works under the strict policy: the library checks `styleEl.sheet` and carries on; the
inline `style` props React sets (the transparent input, `--root-height`) go through the CSSOM and are allowed (probe:
no violation). What is lost are the five rules: the `::selection` mask (input-otp selects the last character on every
focus of a full field, so a highlight shows over the cells), the `:autofill` neutraliser (the browser's autofill tint
and text colour over the cells, on the very path the human required to behave as before), the iOS letter-spacing fix
and the password-manager badge rule. The visual effect of their loss is unverified (no autofill or iOS run here).
React 19.3.1 renders `<noscript>` children as text content (`shouldSetTextContent` in `react-dom-client`), so the
`noScriptCSSFallback` `<style>` never becomes an element and adds no violation (verified in source).

Options, none of which loosens the policy without saying so:

- **A. Keep `style-src 'self'` and accept the blocked element**, as story 2.5 accepted Radix's scroll-lock `<style>`
  (decided 2026-10-28; `alert-dialog.tsx:7` carries the `// shortcut:`). Cost: one console error and one violation event
  per document on `/link/otp`; the five rules above are lost. Note: the e2e allowance `knownRadixStyleViolation`
  (`dispute-raise.spec.ts:16` and four other specs) matches input-otp's message exactly, and the violation event carries
  no sample without `'report-sample'`, so neither can tell the two libraries apart.
- **B. Add the hash of the empty string, `'sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU='`, to the app CSP's
  `style-src`.** CSP Level 3 hash-source (<https://www.w3.org/TR/CSP3/#grammardef-hash-source>). It admits exactly one
  inline style element: an empty one; markup injection gains nothing. The repo already uses a fixed hash for Scalar's
  inline script (AD-23, with a test that recomputes it). It is a policy change: it touches
  `security-headers.conf.template`, `GatewayTests.cs:482` and AD-23's text, so infra joins the story's owners.
- **C. Pre-empt the injection**: ship the five rules in a same-origin stylesheet and give the page an element with id
  `input-otp-style` (a `<link>`, not a `<style>`: an empty parser-inserted `<style>` is blocked too). No policy change,
  but it relies on an undocumented internal check, which is code around the library (review-story Known pitfalls).
- **D. The `nonce` prop** (the README's documented CSP route): needs a per-response nonce in the header and in the
  static `index.html` (nginx `sub_filter`), a new mechanism, and reverses 2.5's "no nonce".

Recommendation: B, the smallest standard mechanism that keeps the library's rules, with A as the fallback if the human
wants the header byte-for-byte unchanged. C and D are not recommended.

What the spec must then require (Sofia, once answered): see S1.

### S1 [story] high: criterion 5 must state the decided outcome as a deterministic check

A criterion whose result is already known cannot stay "return `blocked` if it fails". Rewrite criterion 5 per D1:

- If B: the gateway's `style-src` is `'self' 'sha256-47DEQ…'` and nothing else changes; `GatewayTests.cs` asserts the
  new header and recomputes the hash of `""`; the e2e listener on `/link/otp` asserts **no** violation and no console
  error after load, focus, typing and paste; the `// shortcut:` is not needed. Add the infra files to Touches.
- If A: `components/ui/input-otp.tsx` carries `// shortcut: input-otp injects a <style> that the gateway CSP
  (style-src 'self') blocks, so its autofill, selection and iOS rules do not apply; upgrade if …`; the e2e asserts
  **exactly one** violation on `/link/otp`, `style-src-elem` with blockedURI `inline`, and that the digits show; the
  comment on `knownRadixStyleViolation` names both libraries; QA records the autofill and focus-selection appearance in
  light and dark.

### S2 [story] medium: "every other existing OTP test passes unchanged" is false; the changed assertions must be named

`story-ui-linking.md:69-70`. With input-otp and criteria 6 and 7 these existing assertions must change, and a verifier
must be able to tell an intended update from a weakened test (AGENTS.md, When stuck):

- `OtpScreen.test.tsx:70-80` (no length limit) and `:94-105` (`toHaveValue('481 516')`, now `'481516'`): named by the spec.
- `:137-159` "refuses seven digits" (`'4815167'`): the paste is truncated to `481516` and Verify sends it (S3).
- `:137-159` "refuses a letter" (`'48151a'`): the paste is rejected whole, so `toHaveValue('48151a')` fails.
- Every `document.body.textContent).not.toContain(<code>)` taken while the code is still in the field: `:158` (five
  digits), `:199` (400 naming code), `:224` (problem notice keeps the code), `:493` (rate limited). The six cells render
  the digits as text nodes, so `textContent` holds them by design. Per Known pitfalls, rescope each to the elements that
  could echo the code (the alert, the error summary, the field error), not delete it. Same for
  `DetailsScreen.test.tsx` only where the rate-limit tests change.
- `:63` `getByText('The code expires 5 minutes after we send it.')`: the copy changes (criterion 7).
- `:448-449` and `DetailsScreen.test.tsx:334-335`, `:371`, `:374`: `aria-disabled` and focus on the button become
  `disabled` and focus on the notice (criterion 6).

Fix: replace "every other existing OTP test passes unchanged" with this list and the new expected value for each.

### S3 [story] medium: over-long and non-digit input now behave differently; pin each case with a test on each side of the bound

From the 1.5.0 source: `_pasteListener` builds `value.slice(0, start) + content + value.slice(end)`, then
`.slice(0, maxLength)`, then rejects the whole paste if `pattern` fails; `_changeListener` slices to `maxLength` and
rejects a change that fails `pattern` (no `pasteTransformer` on that path). Consequences the spec does not state:

- A seven-digit paste (`4815167`) fills `481516`; today it is refused with "Enter the 6-digit code.". The new value
  passes validation, so pressing Verify spends one of the three attempts on a code the person never had.
- Pasting into a full field (the code is kept after a 429 or a 500) replaces the selected last character and truncates:
  `481516` + paste `481516` gives `481514`. Today the plain input would hold 12 characters and be refused for free.
- A paste with any non-digit (`Your code is 481516`, `481-516`) is ignored silently; a typed letter or space is dropped.
- Autofill goes through `_changeListener`, so an autofilled value with a space would be rejected whole (the SMS sends
  six contiguous digits today, so this is theoretical).
- `REGEXP_ONLY_DIGITS` is `^\d+$`; JavaScript `\d` without the `u` flag is ASCII `0-9`, so Arabic-Indic digits are
  refused, as today.

Fix: Inputs and criterion 3 state the behaviour for each case and name `REGEXP_ONLY_DIGITS` and
`pasteTransformer={(pasted) => pasted.replaceAll(' ', '')}`; tests cover typed 5, 6 and 7 digits, pasted `481 516`,
`4815167`, `48151a` and `١٢٣٤٥٦`, a paste into a full field, and a programmatic fill (`fireEvent.change`, as autofill
does) that sends nothing until Verify. Recommended answer: accept the library's truncation, because the cells show
exactly what Verify will send; if Sofia reads that as contradicting the 2026-11-08 decision's "validation unchanged",
it goes to the human.

### S4 [story] medium: focus is lost again when the wait ends; every focus path needs a test

Criterion 6 moves focus to the rate-limited notice so it "is never lost". That choice is sound: `disabled` makes the
focused button unfocusable, and the HTML focusing steps then reset focus to the body; moving it to the notice with
`tabIndex={-1}` follows WCAG 2.2 SC 2.4.3 and EXPERIENCE's own Load more precedent. Focusing a `role="alert"` notice is
the GOV.UK error-summary pattern, so it is acceptable. But `useRateLimit.ts:19-21` clears the limit when the wait ends,
which unmounts the notice while it has focus, and focus falls to the body. The criterion is silent on that moment.

Fix: criterion 6 names where focus goes when the wait ends. Recommended: if the notice still has focus, focus moves to
the submit button (Continue or Verify); if the person has moved focus elsewhere (a help link, the header), it stays
put. Tests on both screens: a 429 after a button press, after Enter in a field (the focused input itself becomes
disabled), the end of the wait with focus on the notice and with focus moved elsewhere, no `Retry-After` (focus stays
on the button, nothing disabled), and a pending press (`aria-disabled`, focus kept). Where jsdom may not reset focus
when an element becomes disabled (unverified), the e2e in Chromium proves that focus is never on `body`
(`linking-screens.spec.ts`, a stubbed 429 with `Retry-After: 1` for the end of the wait).

### S5 [story] medium: the component tests will fail on input-otp's password-manager probe in jsdom

With the default `pushPasswordManagerStrategy="increase-width"`, focusing the input schedules `trackPWMBadge` at 0, 2000
and 5000 ms, which calls `document.elementFromPoint(x, y)` when no known extension marker is found. jsdom 30.1.3 (the
installed version) does not implement `elementFromPoint`, so the first focus in a test throws a `TypeError` in a timer,
which Vitest reports as an unhandled error. The strategy also runs a 1-second `setInterval` while mounted and widens
the input 40px past the container.

Fix: criterion 3 names `pushPasswordManagerStrategy="none"` (a documented prop; recommended, as nothing on a one-time
code needs a password-manager badge and it removes the timers and the widening at 320px), or a test-setup stub beside
the `ResizeObserver` one in `test/setup.ts` with the reason.

### S6 [story] medium: the e2e keyboard check would report the OTP input as having no visible focus

`linking-screens.spec.ts:211-213` treats a stop as visibly focused only if `document.activeElement` has an outline or
a box-shadow. input-otp sets `outline: 0 solid transparent` and `boxShadow: 'none'` inline on the real input; the
visible indicator is the active cell. In the rate-limited states the disabled inputs and button are counted by
`:223` as controls the keyboard must reach. And `:376-381` and `:418-419` assert "fields stay editable" and force-click
the waiting button.

Fix: the Tests section tells QA how to adapt without weakening: for the OTP input the indicator is the
`[data-active="true"]` cell's ring and halo (criterion 4), checked on that cell; disabled controls leave the reachable
count only while disabled; the editable-field assertions invert to `toBeDisabled()`.

### S7 [story] low: the disabled look has no named home

`components/ui/button.tsx:10` and `input.tsx` have no `disabled:` styles, and the registry's `input-otp.tsx` adds
`has-disabled:opacity-50` to the container, which criterion 6 forbids. Touches (`:118`) lists neither `button.tsx`
nor `input.tsx`. Fix: name where the `muted` / `border` / `muted-foreground` disabled styles live (recommended: the
shared `Button` and `Input`, since DESIGN states the rule globally and no `Button` uses `disabled` today), that the
cells take it through the container's `has-disabled:` variant in place of the opacity, and add the files to Touches
for the collision check.

### S8 [story] low: the cells and the hint are wired as the mockup draws them

`mockups/link-otp.html` puts `aria-hidden="true"` on the cell row and ties the hint to the input
(`aria-describedby="otp-h"`). Criterion 3 names only the error in `aria-describedby`, and criterion 7 does not say where
the OTP help text sits. Without `aria-hidden`, a screen reader in browse mode reads the digits twice. Fix: the cell
group is `aria-hidden="true"`; the hint sits under the cells and the input's `aria-describedby` holds the hint id and,
when shown, the error id; a test asserts the accessible description in both states.

### S9 [story] low: the generated slot blinks forever and carries an unused part

The registry `InputOTPSlot` draws its fake caret with `animate-caret-blink` (tw-animate-css 1.4.0: 1.25s, `infinite`,
no reduced-motion rule). EXPERIENCE, Accessibility Floor: "Respects `prefers-reduced-motion` (no motion beyond 150ms
fades anyway)". `InputOTPSeparator` (Lucide `MinusIcon`) is not used. Fix: criterion 4 adds
`motion-reduce:animate-none` to the caret (or a static caret) and drops the separator.

### S10 [story] low: the invalid cell look is unspecified

The registry slot styles `aria-invalid:border-destructive`, but nothing passes `aria-invalid` to the slots, and
criterion 4 does not say how a cell looks after a wrong code or a 400. Fix: state it (the `destructive` border, as the
plain `Input` does today, `input.tsx:11`) or say the cells keep their normal border and the field error carries it.

### S11 [story] low: package facts and the help heading

- Inputs (`:40-43`): the peer range is `^16.8 || ^17.0 || ^18.0 || ^19.0.0 || ^19.0.0-rc`, not `^19.0.0` (met by
  19.3.1 either way). Verified 2026-11-08: latest dist-tag `1.5.0` (published 2026-08-18), maintainer `guilhermerodz`,
  repository `github.com/guilhermerodz/input-otp`, MIT, no `dependencies`, no install scripts, 26,689,052 downloads in
  the week to 2026-11-07. The shadcn CLI installs with npm's default caret range and the frontend has no
  `save-exact` `.npmrc`, so the builder pins `1.5.0` after the CLI runs.
- Criterion 7: the mockup's "Need help?" is an `h2` naming a `section` (`aria-labelledby`); say so, so the heading
  order and the region name are tested.

## Security checklist (frontend-only change)

- AuthN/Z, browser session, antiforgery: unchanged; the verify call still sends `{ code }` with `X-XSRF-TOKEN`
  (`OtpScreen.test.tsx:121-135` stays).
- Linking: no-disclosure copy, lockout copy, attempt counts and `gcTime: 0` unchanged. The client bound is now enforced
  by `maxLength` and `pattern`; the server bound (`linking-verify.spec.ts:226-235`) is unchanged. S3 is a user-harm
  point (a spent attempt), not a disclosure.
- Data: the code lives only in React state, the input value and the six cell text nodes, all gone on unmount; the
  container sets `translate="no"`, so Chrome's page translation does not send the digits (verified in source).
  `linkingPrivacy.test.tsx` still covers URL, history, storage, query keys, mutation cache and spans.
- Supply chain: one new package, verified above (S11); `npm audit` runs in the build.
- Headers: D1. Today's policy cannot hold with zero violations; the human chooses.
- Standards: see below.

## Standards check

| Mechanism | Standard or documented source | Note |
|---|---|---|
| Six cells over one input | input-otp README and props (`maxLength`, `pattern`, `pasteTransformer`, `pushPasswordManagerStrategy`, `nonce`); shadcn Input OTP | Documented props only; S5 names one. |
| One-time-code autofill | HTML `autocomplete="one-time-code"` (MDN) | input-otp defaults it; criterion 3 pins it. |
| Rate-limit disabling | HTML `disabled` attribute (HTML Standard) | Correct choice over `aria-disabled` for a form that must not change. |
| Focus on removal | WCAG 2.2 SC 2.4.3; EXPERIENCE Load more precedent | S4 closes the end-of-wait path. |
| CSP for the injected style | CSP Level 3 hash-source (B) or the 2.5 `// shortcut:` precedent (A) | D1. |
| Consistent help | WCAG 2.2 SC 3.2.6 | |

Each fits in a sentence; nothing home-grown is proposed except option C, which this review does not recommend.

## What was run

- `npm pack input-otp@1.5.0` into the scratchpad; source read from the source map. `npm view`, the npm downloads API
  and the shadcn registry JSON, read only.
- A Chromium 156 CSP probe (Playwright from the main checkout's `e2e/node_modules`), the three rows in D1.
- Read-only greps of `react-dom` 19.3.1 (`noscript`), `react-hook-form` 7.91.0 (disabled fields are left out of the
  submitted values, harmless here since submit is blocked), jsdom 30.1.3 (no `elementFromPoint`).
- No test suite was run (a pre-build review has no diff).
