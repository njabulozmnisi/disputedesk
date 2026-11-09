# Security: ui-linking

Security, 2026-11-08, on the uncommitted change in `.claude/worktrees/agent-ac84704905088292a` (the story files listed
under `story_files` in its `ui-linking-build.json`, compared with the UI bases in
`.claude/worktrees/agent-a1758a16625231b46`; build diff hash `e36d8e04ab01`). Verdict: **findings**: one medium, no
high. The CSP change and the new package pass. The paste refusal decided on 2026-11-08 can be bypassed, and that is
the medium finding.

| Sev | Finding | Action |
|---|---|---|
| medium M1 | A pasted code of more than 6 digits followed by any character that is not a space (`"4815167\n"`, `"4815167\r\n"`, `"4815167a"`) is cut to `481516` and accepted silently, with no error. The 2026-11-08 decision (`decisions.md`: "Refuse the paste ... no attempt is spent") and the spec's Inputs row ("refused, never truncated") say it must be refused. Verify then spends one of the three server attempts on a code the person did not have. This is the exact outcome the decision was made to prevent. | Fix round: base `isOverlongCode` on the number of digits in the pasted text, not on "only digits and spaces". For example `(pasted.match(/[0-9]/g) ?? []).length > codeLength`. Leave `codeFrom`, `otpSchema` and the submit unchanged. Add `OtpScreen.test.tsx` cases for `"4815167\n"` and `"4815167a"`: the cells keep their value, the error shows and no request is sent. |

No high finding, so nothing is escalated.

## 1. The CSP change (`infra/images/gateway/templates/security-headers.conf.template`)

- **Exactly one token added.** The app origin's `style-src` changes from `'self'` to
  `'self' 'sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU='`. Every other directive is byte-identical. No
  `'unsafe-inline'`, `'unsafe-hashes'`, nonce, `style-src-elem` or `style-src-attr` is added. Recomputed here:
  `printf '' | openssl dgst -sha256 -binary | openssl base64` gives `47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=`, the
  SHA-256 of the empty string. GatewayTests also recomputes it with `SHA256.HashData([])`.
- **Only an empty `<style>` element is admitted.** In CSP Level 3 a hash-source matches an inline `<style>` element
  only when the hash of its child text content equals the hash. A hash never matches a `style=""` attribute unless
  `'unsafe-hashes'` is present (W3C CSP3, hash-source and "Does element match source list for type and source"). The
  attribute is therefore still blocked: there is no `'unsafe-inline'`, no `'unsafe-hashes'` and no `style-src-attr`.
  This is unchanged from before.
- **An admitted empty element cannot be filled with CSS text later.** The HTML Standard runs "update a style block"
  whenever a style element's children change, and each run checks the new text against CSP again ("Should element's
  inline behavior be blocked by Content Security Policy?"). Writing `textContent` into input-otp's element, or into any
  empty element that was injected, is still blocked. Radix's scroll-lock `<style>`, which holds CSS text, stays
  blocked, as accepted in story 2.5. The comment in `alert-dialog.tsx` now says so.
- **CSSOM rules do not open an injection path.** CSP does not govern CSSOM calls (`insertRule`, `element.style`,
  constructable stylesheets). Any script that runs under `script-src 'self'` could already add rules before this
  change, through `new CSSStyleSheet()` or the CSSOM of a `'self'` stylesheet. The hash therefore gives script no new
  capability. Markup injected without script gains only an inert empty element. One residual effect: an element
  injected with `id="input-otp-style"` before mount would make input-otp skip its own five cosmetic rules. That changes
  how autofill looks and exposes no data. It also needs an HTML injection, which React's escaping prevents. Accepted;
  not a finding.
- **The noscript fallback adds no second inline style.** input-otp renders `<noscript><style>…</style></noscript>`.
  React DOM's client renderer treats `noscript` children as text content (`shouldSetTextContent`) and creates no
  `<style>` for them. The builder's Chromium 156 evidence is consistent with this: one `style-src-elem` violation
  without the hash, none with it.
- **Both gateway images, and the Scalar CSP.** `infra/images/gateway/Dockerfile` copies `templates/` to
  `/etc/nginx/templates/` (local) and copies `templates/security-headers.conf.template` into
  `/etc/nginx/templates-aws/` (AWS). Both `nginx.conf.template` files include `/etc/nginx/security-headers.conf` in the
  server block, `/`, `/assets/`, `@rate_limited` and `@gateway_error`. `scalar-headers.conf.template`,
  `identity-headers.conf.template` and `mailpit-headers.conf.template` are not in the diff, and
  `AppOrigin_ContentSecurityPolicies_HoldBaseUriNone` still asserts the exact Scalar CSP on both images. The builder's
  PlatformTests run (440/440, red before the template change) is reported; not re-run here.
- Standard: W3C CSP Level 3, hash-source (https://www.w3.org/TR/CSP3/#grammardef-hash-source); HTML Standard, the style
  element (https://html.spec.whatwg.org/multipage/semantics.html#update-a-style-block). ASVS 5.0 V3.4.3 (a CSP that
  allows trusted content only through an allowlist or hashes, with `object-src 'none'` and `base-uri 'none'`). The
  headers are tightened or equal everywhere except this one hash, which the human decided on 2026-11-08 (D1).

## 2. The new dependency, input-otp 1.5.0

- Registry (`npm view input-otp@1.5.0`): `latest` dist-tag 1.5.0, sole maintainer `guilhermerodz`, repository
  `github.com/guilhermerodz/input-otp`, MIT. The published `scripts` hold only build and lint scripts: no `preinstall`,
  `install`, `postinstall` or `prepare`. It has no dependencies, and its peers are react and react-dom
  `^16.8 || … || ^19.0.0`.
- Pin: `"input-otp": "1.5.0"` in `package.json`. The lockfile entry resolves to
  `registry.npmjs.org/input-otp/-/input-otp-1.5.0.tgz`, and its `sha512-3Acfd…tGQ==` integrity equals the registry's
  `dist.integrity`. The lockfile delta against the UI base adds only this package (the shadcn CLI's extra `cn` package
  was reverted). `packagePins.test.ts` guards the exact pin.
- Runtime, read from the installed 1.5.0 `dist` and its source map (`src/input.tsx`, `src/use-pwm-badge.tsx`):
  - On mount it appends one empty `<style id="input-otp-style">` (with no `nonce`, since none is passed) and inserts
    five constant rules for selection, autofill, iOS and badges through `insertRule`.
  - If a rule is rejected, it calls `console.warn` with that constant rule text. No personal data, and the SPA does not
    forward console output.
  - Inline `style` props are set through React's CSSOM, which CSP does not govern.
  - No `fetch`, `XMLHttpRequest`, `eval` or `new Function`, no storage, cookies or `postMessage`, and no
    `innerHTML`/`outerHTML`.
  - With `pushPasswordManagerStrategy="none"`, every password-manager effect returns early: no `setInterval`, no
    `elementFromPoint`, no badge `querySelectorAll` and no width change (`use-pwm-badge.tsx`).
- Standard: AGENTS.md, Dependency verification; npm lockfile integrity (Subresource Integrity format). ASVS 5.0 V15.2.1
  (no component with known vulnerabilities) and V15.2.4 (components come from the expected repository).

## 3. The code flow on the linking path

- **Autofill and bound.** The real input has `id="code"`, `autocomplete="one-time-code"` (passed explicitly; it is also
  the library's default), `inputmode="numeric"`, `maxLength` 6 and `pattern={REGEXP_ONLY_DIGITS}` (`^\d+$`; ASCII
  digits without the `u` flag). input-otp's change listener drops any value that fails the pattern. A typed 7th digit
  is cut off by `maxLength`.
- **Paste refusal.** Simulated with verbatim copies of `codeFrom`, `isOverlongCode` and `transformPastedCode` and
  input-otp 1.5.0's `_pasteListener` value path (scratch script, not a test in the tree):
  - `"4815167"` and `"4815 1678"` are refused, the field keeps `''` or `'481'`, and the error shows. This matches the
    decision.
  - `"481516"`, `"481 516"` and `"481516\n"` become `481516`.
  - `"Your code is 4815167"`, `"4815\t1678"` and `"4815 1678"` with a no-break space are dropped whole and silently.
  - `"4815167\n"`, `"4815167\r\n"` and `"4815167a"` become `481516` with no error. This is M1.
  - The library takes the first six characters (`newValueUncapped.slice(0, maxLength)`) before it checks the pattern,
    so any non-space junk after more than six digits escapes `isOverlongCode`, whose test is "only digits after
    spaces".
  - `"481516a"` holds only 6 digits, so accepting `481516` stays within the decided bound ("longer than 6 digits"). It
    is not part of M1. The builder disclosed it as an assumption.
- **No attempt spent on the paste itself.** `onComplete` is not passed and filling the sixth cell sends nothing; only
  Verify submits (`onValid`, guarded by `attemptInFlight` and `rateLimit.isWaiting`). In the refused cases no request
  is made. The paste `setError` uses the constant `linkingMessages.code` and never echoes the pasted text.
- **Server validation unchanged.** No file under `src/`, `openapi.json` or the generated client is in the diff. The
  body is still `{ code: codeFrom(values.code) }`, `otpSchema` is unchanged and `gcTime: 0` stays. Hashing, expiry,
  single use, lockout after 3 and the per-user and per-IP limits are the server's and were not touched (AD-12).
- **Rate-limit disabling** is presentation only. A `Retry-After` 429 sets the HTML `disabled` attribute on the inputs
  and the submit button, and the server limiter stays the control (ASVS 5.0 V6.6.3, unchanged). Without `Retry-After`
  nothing is disabled. A pending request is still only `aria-disabled`.
- **No echo, no logging.** The code is in the input's value and in the six cells (the design). input-otp adds only
  selection offsets and a placeholder flag as attributes. The alert, the error summary and the field error never hold
  it: `echoingText()` in `OtpScreen.test.tsx` rescopes the old whole-body checks, none was deleted, and the locked
  state still checks the whole body. No `console`, telemetry or logging call was added under `features/linking/`.
- Standard: input-otp documented props (https://github.com/guilhermerodz/input-otp); MDN `autocomplete`
  (https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Attributes/autocomplete); React Hook Form `setError`;
  the HTML `disabled` attribute. ASVS 5.0 V2.2.1 (input validated against its expected structure, on the client as a
  convenience; the server remains authoritative).

## Checklist

- AuthN/Z, browser session, data, audit: no server, route, cookie, antiforgery, session or audit change in the diff. Not
  applicable beyond confirming that nothing changed.
- Linking: identical-response copy unchanged (the neutral sentence is untouched), and no ID number or code is logged
  (see 3).
- Headers: unchanged except the one decided hash (see 1).
- Supply chain: see 2 and the scanners below.

## Scanners (run in the story worktree)

| Scanner | Result |
|---|---|
| `scripts/secret-scan.sh` (the whole uncommitted change, untracked files included) | no leaks, exit 0 |
| `npm audit --package-lock-only --omit=dev` | 0 vulnerabilities |
| `npm audit --package-lock-only` (dev included) | 10 moderate, all minimatch GHSA-g3hv-75wg-m249 through eslint and typescript-eslint. The main checkout's lockfile shows the same 10, so they come from before this story and are not in the runtime bundle. |
| `scripts/security-local.sh` (Trivy fs, vuln and misconfig, HIGH and CRITICAL) | exit 0 |
| Semgrep `p/react`, `p/typescript`, `p/secrets` on the story's frontend files, untracked included (`--no-git-ignore`) | 0 findings (110 rules) |
| `dotnet list package --vulnerable` | not run: no NuGet change (GatewayTests only adds `using System.Security.Cryptography`) |

## Open before merge (not findings)

- Criterion 5's third clause (AD-23 in `docs/architecture/architecture.md` states the hash, that it admits only an
  empty style element, and why) is not in the diff. The build report assigns it to the supervisor. AD-23 on main does
  not mention it yet.
- The e2e CSP expectations (`gatewayContentSecurityPolicy` in `e2e/tests/support.ts` and the `knownRadixStyleViolation`
  patterns) and criterion 5's zero-violation check on `/link/otp` belong to QA and are not yet in the tree. Each
  pattern must still match Radix's violation only.
- Verified in reading and in a simulation only. Firefox, WebKit and iOS autofill were not exercised here.

## Fix round 1 (2026-11-08)

Delta-only re-run on the fix round 1 tree `.claude/worktrees/agent-a718a1e0fc36c5203`, compared with the previous tree
`.claude/worktrees/agent-ac84704905088292a`. The fix list is `docs/verification/ui-linking-fixes.md`. Both trees sit on
`2f897c3` and change the same files: their `git status --short` lists are identical. `diff -rq` over `frontend/src` and
`infra` shows six changed files, and nothing else differs (`infra/tests/TestResults` exists only in the old tree as a
build output):

- production code: `features/linking/linkSchema.ts`, `features/linking/OtpScreen.tsx`, `components/ui/button.tsx`;
- tests: `OtpScreen.test.tsx`, `linkSchema.test.ts`, `disabled.test.tsx`.

Verdict: **pass**. M1 is closed and the fix round adds no new finding.

| Sev | Finding | Status |
|---|---|---|
| medium M1 | An over-long paste with a trailing character that is not a space was cut to six digits and accepted. | **Closed.** See below. |

### M1 closed

- **The fix.** `isOverlongCode` now counts the ASCII digits in the pasted text:
  `(pasted.match(/[0-9]/g) ?? []).length > codeLength`. It stays the one check that both the `pasteTransformer`
  (`transformPastedCode` returns `' '`, which `REGEXP_ONLY_DIGITS` refuses) and the `onPaste` error use. `codeFrom`,
  `otpSchema` and the submit body `{ code: codeFrom(values.code) }` are unchanged.
- **Simulation.** A scratch script, run with Node 24 and not added to the tree, imported the new tree's real
  `linkSchema.ts` instead of a copy. It ran that file against input-otp 1.5.0's paste listener value path, transcribed
  from the installed `dist/index.mjs` (`ut`). That path:
  1. transforms the pasted text;
  2. splices it in at the selection;
  3. keeps the first `maxLength` characters (`.slice(0,t)`);
  4. drops the whole paste if the result fails `^\d+$`.

  Each case was run against three field states: empty (caret 0), holding `481` (caret 3), and full with the last cell
  selected (5..6). The script reported 0 unexpected results:
  - **Refused**: the field keeps its value and the error is set. This held for the five cases in the brief,
    `"4815167\n"`, `"4815167\r\n"`, `"4815167\t"`, `"481516 7"` and `"4815167a"`. It also held for `"4815167"`,
    `"4815 1678"`, `"Your code is 4815167"` and `" 4815167"`.
  - **Accepted with no error**: `"481516\n"`, `"481516a"`, `"481516"` and `"481 516"` each fill an empty field with
    `481516`.
  - Pasting six digits into a field that already holds digits still splices and cuts to six (`481` + `481516` gives
    `481481`; a full field gives `481514`). That paste holds no more than 6 digits, so it is outside the decided
    bound. The spec's Inputs row ("paste into a full field ... truncates to 6") makes it the decided behaviour, and
    the round did not change it. Not a finding.
- **Tests.**
  - `OtpScreen.test.tsx` adds the four refused pastes. Each asserts empty cells, the field error, the announcement and
    no request.
  - It adds the two accepted pastes. Each asserts `481516`, no field error, an empty live region and no request.
  - `linkSchema.test.ts` adds the trailing-character cases to the over-long list. It moves `"481-5167"` from the "not
    over-long" list to the over-long list. That case holds 7 digits, so the move follows the decided rule ("longer
    than 6 digits") and does not loosen a test. `"481516a"` and `"481516\n"` are pinned as not over-long.
- **Against the decision.** This now matches `decisions.md` (2026-11-08): "Refuse the paste: the cells keep their
  value and the field shows today's six-digit error; no attempt is spent". No refused case makes a request. Only
  Verify submits, and nothing in the delta changes that.

### The announcement and the summary change leak nothing

- **Announcement.** The refused-paste path calls `announce(linkingMessages.code)`, and only that. The argument is the
  constant `'Enter the 6-digit code.'`, never the clipboard text. `LiveRegionProvider` (`app/LiveRegion.tsx`, not in
  the delta) renders each message as plain React text in a `<p>` inside the one `role="status"` `aria-live="polite"`
  region. There is no raw HTML and no attribute holds the message. The only other `announce` call on the screen is the
  existing constant `'Your login is linked.'`. The new tests assert that the live region holds the fixed message on a
  refusal and is empty on an accepted paste.
- **Summary change.** `summaryShown` decides only whether `ErrorSummary` mounts. Its items are still
  `[{ fieldId: 'code', message: codeError }]`, and `codeError` is only ever set from the constant
  `linkingMessages.code`. Nothing new reaches the DOM, and no state, request, cookie or token handling changed.
- **The disabled button.** `button.tsx` changes one class (`disabled:border-transparent` becomes
  `disabled:border-border`). The change is presentation only.
- No `console`, telemetry or logging call was added. No file under `src/`, `openapi.json` or `frontend/src/api` is in
  the delta.

### Headers and packages

- **Gateway CSP files byte-identical.** `cmp` finds no difference between the two trees for the CSP
  template, GatewayTests and the gateway Dockerfile, and `diff -rq` over `infra/images` finds none.
- **No new package.** `frontend/package.json` and `frontend/package-lock.json` are byte-identical between the
  trees.

| File (identical in both trees) | SHA-256 |
| --- | --- |
| `infra/images/gateway/templates/security-headers.conf.template` | `11a8a3cbbd4afce7312daa074a1454ca4cabba97fc00de6d49ef5aafc693a6b9` |
| `infra/tests/DisputeDesk.PlatformTests/GatewayTests.cs` | `2a6d4c2b792ef2b04edb45ce0077320d5def91bd02e31af6bf0a64d0709717c9` |
| `infra/images/gateway/Dockerfile` | `45395f90f028e3b16abdcb27179c9ef99d7668b5afd44500caaf5fbf54c96d2b` |
| `frontend/package.json` | `9256717a6a80d7f24ea533151c09926abca798ff13de6be6b9fc9b0eece81657` |
| `frontend/package-lock.json` | `e25f915d9c205300b2fd07b2236605b955a90aa36607869011574ba8523ef073` |

### Standards

- Client-side input validation through input-otp's documented `pasteTransformer` and `pattern` props
  (https://github.com/guilhermerodz/input-otp) and React Hook Form `setError`. ASVS 5.0 V2.2.1: input is checked
  against its expected structure on the client, as a convenience, and the server stays authoritative.
- The status message goes through the existing single polite live region, WAI-ARIA `role="status"`
  (https://www.w3.org/TR/wai-aria-1.2/#status). It carries only fixed text. ASVS 5.0 V14.2 (no sensitive data
  exposed in the client) holds.

### Scanners (fix round 1 tree)

| Scanner | Result |
|---|---|
| `scripts/secret-scan.sh` (the whole uncommitted change, untracked files included) | no leaks, exit 0 |
| `npm audit --package-lock-only --omit=dev` | 0 vulnerabilities (the lockfile is byte-identical to the previous round) |
| Semgrep `p/react`, `p/typescript`, `p/secrets` on the six changed files (`--no-git-ignore`) | 0 results, 0 errors, 6 files scanned |
| `scripts/security-local.sh` (Trivy) | not re-run: `infra/images` is byte-identical to the previous round, which passed |
| `dotnet list package --vulnerable` | not run: no NuGet change |

### Still open before merge (not findings, unchanged from round 0)

- The hash is not yet in AD-23 (`docs/architecture/architecture.md`) on main or in the fix tree. That is the
  supervisor's step.
- The hash is not yet in the e2e CSP expectations (`e2e/tests/support.ts`). That, and the zero-violation check on
  `/link/otp`, are QA's.
- Checked by reading and a simulation only. The builder's Chromium clipboard smoke is reported, not reproduced here.
