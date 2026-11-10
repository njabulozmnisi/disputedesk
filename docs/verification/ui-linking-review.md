# ui-linking: post-build review

- Date: 2026-11-08
- Story: `docs/backlog/story-ui-linking.md` (origin/main), High-stakes, stacked on `ui-notices-states`. Companions read:
  `docs/verification/ui-linking-prebuild-review.md`, `docs/verification/decisions.md` (the three ui-linking rows).
- Diff reviewed: the 19 `story_files` in `docs/verification/ui-linking-build.json` of worktree
  `.claude/worktrees/agent-ac84704905088292a`, each compared with the bases-only worktree
  `.claude/worktrees/agent-a1758a16625231b46`. Frontend part: `package.json`, `package-lock.json`,
  `components/ui/{input-otp,button,input,alert-dialog}.tsx`, `features/linking/{ContactHelp,DetailsScreen,OtpScreen,linkSchema,useRateLimit}`,
  their tests, `disabled.test.tsx`, `input-otp.test.tsx`, one comment in `RaiseScreen.test.tsx`. Gateway part:
  `infra/images/gateway/templates/security-headers.conf.template`, `infra/tests/DisputeDesk.PlatformTests/GatewayTests.cs`.
- Diff hash: `scripts/diff-hash.sh <worktree> 2f897c3` gives `e36d8e04ab01`, the same as the build report.
- Results read (not re-run): frontend `frontend-junit.xml` 771 tests, 0 failures, 0 skipped; platform `.trx` 440 of 440
  passed.
- Library source checked: input-otp 1.5.0 `dist/index.mjs` (installed in the frontend builder's worktree), the paste
  listener, the change listener, the `onPaste` order and the style-element mount effect.
- Probe: a throwaway Vitest file in a scratchpad copy of the frontend (not in the worktree) pasted five over-long
  values into the real `/link/otp` screen (results under Finding 1).
- Security checklist: not run here. This is a High-stakes story, so the security agent covers it.
- Result: **changes**. Two medium findings and four low ones.

## Findings

### M1 medium: a paste of more than 6 digits followed by any non-digit is cut to 6, not refused, and Verify spends an attempt

Files: `frontend/src/features/linking/linkSchema.ts:45-56` (`isOverlongCode`, `transformPastedCode`) and
`frontend/src/features/linking/OtpScreen.test.tsx` (the paste-bound tests).

The decision (decisions.md, 2026-11-08) and criterion 3 say a pasted code longer than 6 digits "is refused, never
truncated", because truncating it "could spend one of the three attempts". `isOverlongCode` takes out only U+0020
spaces (`codeFrom`) and then requires the rest to be digits only. Any other character turns the check off. The
transformer then returns the text unchanged, and input-otp's paste listener runs
`(prefix + pasted + suffix).slice(0, maxLength)` before it tests the pattern (`dist/index.mjs`, `ut`). So the first
six digits pass and the rest is dropped. Probe results on the real screen (value after the paste / error shown /
body sent on Verify):

| Pasted | Value | Error | Sent |
|---|---|---|---|
| `"4815167\n"` (a copied line) | `481516` | no | `{"code":"481516"}` |
| `"4815167\r\n"` | `481516` | no | `{"code":"481516"}` |
| `"4815167\t"` | `481516` | no | `{"code":"481516"}` |
| `"481516\u00a07"` (no-break space) | `481516` | no | `{"code":"481516"}` |
| `"4815167a"` | `481516` | no | `{"code":"481516"}` |

A trailing line break is the usual result of copying a whole line from a message. So the harm the decision rules out
(a cut code sent, with an attempt spent towards the lockout) is still reachable. The builder named the junk-suffix
case in its assumptions as library behaviour. It is a gap against the decision, though the spec's Inputs text
("removes spaces … digits only and longer than 6") describes the same narrow condition. The tests stay inside that
condition (`4815167`, `4815 1678`), so no test sits on the whitespace or junk side of the bound.

Fix: put the bound on the pasted text, not only on digits-only text. In the transformer, remove all whitespace
(`/\s/g`, which also covers `\n`, `\r`, `\t` and U+00A0). If more than 6 characters are left, return the single
refused space so the paste is dropped whole and never cut; otherwise return the stripped text. `onPaste` shows the
six-digit error under the condition both share: more than 6 digits once whitespace is removed. Keep the two calling
the same function. Add OtpScreen tests for `"4815167\n"` and `"4815167a"` (cells unchanged, no request) and for
`"481516\n"` (accepted as `481516`, so a copied line holding the right code still works), plus `linkSchema.test.ts`
rows for each. Source: the decision; input-otp 1.5.0 paste listener.

### M2 medium: the paste refusal's error is not announced (WCAG 2.2 SC 4.1.3)

File: `frontend/src/features/linking/OtpScreen.tsx:213-218` and `:233-237`.

When an over-long paste is refused, `onPaste` calls `setError` and the field error appears. Focus stays on the
input, the error has no role (`role` is `alert` only for a wrong code), and no live region speaks. A screen-reader
user pastes, sees nothing change and hears nothing. The `aria-describedby` change on the focused input is not
announced reliably. This is the situation SC 4.1.3 Status Messages covers (Understanding 4.1.3; failure F103). The
spec rules out an error summary and a focus move until Verify, but it does not rule out announcing the error. The
other error paths are covered: the 400 path moves focus to the summary, and the wrong-code path uses `role="alert"`.

Fix: announce the message through the existing `useAnnounce()` live region in the `onPaste` branch
(`announce(linkingMessages.code)`), or give the field error `role="alert"` when the error came from a paste. Add a
test that the refusal reaches the live region (or that an alert holds the message), with focus still on the input.
Source: <https://www.w3.org/WAI/WCAG22/Understanding/status-messages>.

### L1 low: `-m-px` on the active cell: the comment is wrong, and wide cells shift by 1px as the caret moves

File: `frontend/src/components/ui/input-otp.tsx:59-60`.

The comment says the negative margin "keeps the cells' widths equal". With `flex: 1 1 0` the active cell's border box
is still 2px wider than its neighbours (its base size is its 4px of border against 2px). The margin makes only the
margin boxes equal. Where the cells reach `max-w-12` or `md:max-w-14` (1280px), all border boxes are already equal,
and the -1px margins pull every cell to the right of the active one 2px to the left and narrow the gaps beside it to
7px, so the cells move by a pixel as the caret advances. Fix: keep the 1px border and draw the second pixel with
Tailwind's `inset-ring` (a box-shadow that composes with the `ring-4` halo and takes no layout space), and drop
`-m-px` and its comment. Cosmetic; QA's screenshots at 320px and 1280px decide whether it shows.

### L2 low: the "field holding 481" paste-bound test pastes a different value from the one criterion 3 names

File: `frontend/src/features/linking/OtpScreen.test.tsx`, "refuses a pasted 7 digits with a space into a field
holding 481". Criterion 3 asks for pasted `4815167` into an empty field and into a field holding `481`. The second
test pastes `4815 1678`. The two take the same branch, and `linkSchema.test.ts` covers `4815167`, so this is a gap in
the named test only. Fix: add `4815167` to that test as an `it.each` row (the M1 fix round can fold it in).

### L3 low: the inline "Start again" link is 22px tall against DESIGN's 44px rule

Files: `frontend/src/features/linking/ContactHelp.tsx:14-16`; DESIGN.md "Touch targets" (every interactive element at
least 44px tall); the story's Verify ("every interactive element is at least 44px tall").

The link sits inside the sentence "Didn't get a code? Start again.", as in EXPERIENCE.md (Linking help block) and
`mockups/link-otp.html`. WCAG 2.2 SC 2.5.8 exempts a target inside a sentence (the "Inline" exception,
<https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum>), so this is not a WCAG failure. The base code
already had this inline link, at caption size, and it is now larger. It still breaks the project's own written 44px
rule, and the handoff contradicts itself. This is not a builder fix: QA records the inline exception against the
Verify line, or a later lows batch reconciles DESIGN's rule with the inline-link pattern.

### L4 low: the new disabled styles leave the label of a disabled field at full strength

`mockups/app.css:184` dims the label of a disabled field (`.field:has(:disabled) label`), but the build leaves it as
it is. The builder named this in its assumptions (`field.tsx` is not in Touches). Criterion 6 names only the controls,
so this is not a criterion gap. Lows batch.

## Questions from the brief

- **input-otp copied by hand, pinned 1.5.0.** Accepted. Criterion 2 says to use Manual installation if the CLI adds a
  package other than `input-otp` or imports `cn` from somewhere other than `@/lib/utils`, and the build report says it
  did both (`cn` package, `from "cn"`). The copy matches the new-york-v4 registry parts the screen uses. It adds no
  package beyond `input-otp` (lockfile: one new entry, no dependencies, peer React met), is pinned exactly, has no
  separator, and `packagePins.test.ts` and `input-otp.test.tsx` cover the pin and the exports. `components.json` has
  `utils: "@/lib/utils"`, so why the CLI added `cn` is not explained here. That is a tooling question and does not
  affect the diff. Source: <https://ui.shadcn.com/docs/components/input-otp> (Manual).
- **Paste refusal through `pasteTransformer` plus `onPaste` sharing `isOverlongCode`.** The design is right: input-otp
  has no refuse option, its pattern check drops a failing paste whole (verified in `dist/index.mjs`), and its own
  listener runs before the `onPaste` prop, so the shared condition cannot disagree. The gap is the condition itself,
  and the library accepting a paste whose first six characters are digits is a real gap against the decision once the
  paste holds more than 6 digits: M1. A paste of exactly six digits and then junk (`481516a`) is debatable, but the M1
  fix refuses it whole as well, which matches the "ignored whole" rows of the Inputs table.
- **Really-disabled controls, focus to the notice and then to submit.** Correct, on every path I traced: a button
  press, Enter in a field (the focused input is disabled and focus goes to the notice in the effect after commit), the
  end of the wait with focus on the notice (the timer records it before the notice unmounts, then the effect focuses
  submit), focus moved elsewhere (it stays there), no `Retry-After` (nothing disabled, focus stays on the button), a
  pending press (`aria-disabled`, still enabled, keeps focus), unmount mid-wait (the timer is cleared). The
  react-hook-form value survives because `disabled` goes to the element, not to the `Controller`, so the cells keep
  the digits. Both screens have the tests criterion 6 lists. A mouse click on a disabled button sends focus to `body`,
  which is how browsers behave and not a defect. The Chromium never-`body` check is QA's.
- **`useRateLimit` taking refs from the screen.** Accepted. It is plain, the doc comment explains it in two sentences,
  and it keeps the `react-hooks` lint clean. A hook that returned its own refs would be no simpler. Not a finding.
- **`text-(length:--text-heading)`.** Accepted. `--color-heading` and `--text-heading` both exist, the builder measured
  24px in Chromium, and the `(length:…)` type hint is Tailwind v4's documented way to pick the font-size reading
  (<https://tailwindcss.com/docs/font-size>, "Using a custom value"). The line-height token is not applied, which
  does no harm in a fixed-height, flex-centred cell. The builder's note that `StartPage.tsx` and `alert-dialog.tsx`
  rely on `text-heading` for size is outside this story and worth a lows row.
- **`-m-px` on the active cell.** L1.
- **Inline "Start again" at 22px.** L3. The WCAG 2.5.8 inline exception applies. The 44px line in DESIGN and the
  story's Verify is the project's own stricter rule, and the mockup contradicts it.
- **Gateway template and GatewayTests.** Correct. Only `style-src` changes, to exactly
  `'self' 'sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU='`. Every other directive and the Scalar template are
  unchanged, and there is no `'unsafe-inline'`, `'unsafe-hashes'` or nonce. The value is the base64 SHA-256 of the
  empty string, and envsubst does not touch it (no `$`). `AppOrigin_ContentSecurityPolicies_HoldBaseUriNone` asserts
  the exact app CSP on both images from real nginx responses. `AppOrigin_StyleSrcHash_IsTheHashOfTheEmptyString`
  recomputes `SHA256.HashData([])`, and its regex (`style-src 'self' (?<hash>'sha256-[^']*');`) can match only the
  app line, since Scalar's `style-src` holds `'unsafe-inline'`. The result is red before the template change and
  green after (build report). `infra/tests/smoke.sh` pins only the Scalar CSP, so it is unaffected. The one remaining
  copy of the old app CSP is `e2e/tests/support.ts` (`gatewayContentSecurityPolicy`), which is QA's per the spec. The
  CSP Level 3 judgement on the hash itself is the security agent's.

## Pass 1: correctness, by criterion

| Criterion | Proved by | Verdict |
|---|---|---|
| 1 Step on both screens, none when locked | OtpScreen "shows step 2 of 2 above the heading…", DetailsScreen "step 1 of 2", the locked test's `queryByText(/step \d of 2/)` | met |
| 2 Input OTP in the project's style | `packagePins.test.ts`, `input-otp.test.tsx` exports; lockfile adds one package; no colour token changed | met |
| 3 Six cells, one input, same submit | OtpScreen attribute, cell, description, typed 5/6/7, paste 6, the paste bound, junk, Arabic-Indic, full-field, `fireEvent.change` tests; echo checks rescoped to the alert, summary and field error, none deleted | met for the named cases; M1 and M2 cover the gaps; L2 |
| 4 Cell styling | `input-otp.test.tsx` class checks; the look is QA's (jsdom has no layout) | met in unit scope; L1 |
| 5 CSP | GatewayTests exact CSP on both images plus the recomputed hash | gateway met; AD-23 text and the e2e zero-violation check still open (see below) |
| 6 Disabled form, focus never lost | both screens' rate-limit tests, `disabled.test.tsx` | met |
| 7 One help block | region by name and h1/h2 order on `/link`, the OTP form, locked and rate-limited | met |

The backend checks (`Location`, ProblemDetails, ownership, events, the outbox, `TimeProvider`) do not apply: there is
no API, data or event change. No `!` is added. No timestamp assertions.

## Pass 2: simplicity and standards

- The design is small and readable. `codeLength`, `isOverlongCode` and `transformPastedCode` sit next to `otpSchema`
  with one-line reasons, `ContactHelp` takes one boolean, and `useRateLimit` adds one ref and one effect with a doc
  comment that explains the focus rule. No new abstraction, wrapper or option. The `group` and `group-has-disabled:`
  pair on the cells is the plain Tailwind way to style children from a parent's state, and no other unnamed `group`
  sits above it (`field.tsx`, `Page` checked).
- Standards: input-otp's documented props (`pattern`, `pasteTransformer`, `onPaste`, `pushPasswordManagerStrategy`)
  (<https://input-otp.rodz.dev/docs/validation>); React Hook Form `setError`; the HTML `disabled` attribute; WCAG 2.2
  SC 2.4.3, 2.4.7, 2.3.3, 3.2.6; MDN `autocomplete="one-time-code"`; CSP Level 3 hash-source; .NET
  `SHA256.HashData`. No home-grown mechanism. The paste refusal goes through the library's pattern check because the
  library has no refuse option, as the spec records.
- Button `disabled:border-transparent` against criterion 6's "border edge": it matches `mockups/app.css:99`
  (`.btn:disabled … border-color: transparent`). The input and the cells use the `border` edge, as `app.css:183`
  does. Not a finding.

## Open items (not findings)

- AD-23 in `docs/architecture/architecture.md` does not yet state the hash, that it admits only an empty style
  element, or why. Criterion 5's third clause is unmet until the supervisor writes it, as briefed. It should land
  before the merge.
- Criterion 5's zero-`securitypolicyviolation` e2e check, criterion 6's Chromium never-`body` check and the e2e CSP
  expectations (`support.ts`, `knownRadixStyleViolation` in five specs) are QA's.
- For the verifier: the gateway template's file time (19:59) is later than the platform `.trx` (18:01). The diff hash
  matches the build report, so this is probably when the patch was applied. The verifier confirms that the run
  covers the template as it stands.
- Outside this story: `e2e/tests/spa-transactions.spec.ts` and `spa-disputes-history.spec.ts` differ between the
  story worktree and the bases worktree. The story worktree lacks the bases' e2e edits ("without docs and e2e" in the
  build report). These files are not in `story_files`.

## Status

```json
{"status": "changes", "record": "docs/verification/ui-linking-review.md", "findings": [
  {"severity": "medium", "file": "frontend/src/features/linking/linkSchema.ts", "line": 45, "issue": "A paste of more than 6 digits followed by a non-digit (\"4815167\\n\", \"\\r\\n\", \"\\t\", U+00A0, \"4815167a\") passes isOverlongCode, input-otp slices it to 6 before the pattern check, and Verify sends 481516, spending an attempt, against the decision that an over-long paste is refused, never truncated (probe-confirmed).", "fix": "Strip all whitespace; if more than 6 characters remain, the transformer returns the refused space; onPaste shows the error when more than 6 digits remain; tests for \"4815167\\n\", \"4815167a\" (refused) and \"481516\\n\" (accepted)."},
  {"severity": "medium", "file": "frontend/src/features/linking/OtpScreen.tsx", "line": 213, "issue": "The over-long paste error appears with no role, no live region and no focus move, so screen-reader users are not told the paste was refused (WCAG 2.2 SC 4.1.3).", "fix": "announce(linkingMessages.code) through useAnnounce in the onPaste branch, or role=alert on a paste-caused error; add a test."},
  {"severity": "low", "file": "frontend/src/components/ui/input-otp.tsx", "line": 59, "issue": "-m-px does not keep the cells' widths equal as commented (the active border box stays 2px wider), and where the cells are at max width it shifts the following cells by 1-2px as the caret moves.", "fix": "Keep a 1px border and add the second pixel with inset-ring; drop -m-px and the comment."},
  {"severity": "low", "file": "frontend/src/features/linking/OtpScreen.test.tsx", "line": 0, "issue": "The 'field holding 481' bound test pastes '4815 1678', not the '4815167' criterion 3 names.", "fix": "Add '4815167' as a row."},
  {"severity": "low", "file": "frontend/src/features/linking/ContactHelp.tsx", "line": 15, "issue": "The inline 'Start again' link is 22px tall; WCAG 2.5.8's inline exception applies, but DESIGN's 44px rule and the story's Verify line say every interactive element is 44px.", "fix": "QA records the inline exception, or the lows batch reconciles DESIGN with the inline-link pattern."},
  {"severity": "low", "file": "frontend/src/components/ui/field.tsx", "line": 0, "issue": "A disabled field's label is not dimmed as mockups/app.css:184 draws it (outside Touches).", "fix": "Lows batch."}
], "assumptions": ["The security agent covers the CSP judgement and the security checklist (High-stakes).", "Test results were read, not re-run; the probe ran in a scratchpad copy, not in the worktree."], "open_questions": ["AD-23 text (criterion 5, third clause) is still to be written by the supervisor before the merge."], "conflicts": ["Story Verify and DESIGN require 44px for every interactive element, while EXPERIENCE and the mockup draw 'Start again' as an inline link (L3)."]}
```

## Fix round 1 (2026-11-08, delta review)

- Delta: previous tree `.claude/worktrees/agent-ac84704905088292a` against new tree
  `.claude/worktrees/agent-a718a1e0fc36c5203`, both on `2f897c3` with the same `git status` file list. `diff -rq`
  (build outputs and `node_modules` left out) shows six source files changed, plus the build report:
  `components/ui/button.tsx`, `components/ui/disabled.test.tsx`, `features/linking/{linkSchema,linkSchema.test,OtpScreen,OtpScreen.test}`.
  Nothing else changed. The two gateway files (`security-headers.conf.template`, `GatewayTests.cs`) are byte-identical
  (`cmp`), so the earlier platform `.trx` (440 of 440) still covers them.
- Diff hash: `scripts/diff-hash.sh <new tree> 2f897c3` gives `bc234a527a9a`, the same as the build report.
- Results read (not re-run): `TestResults/frontend-junit.xml`, 788 tests, 0 failures, 0 errors, 0 skipped. Its file
  time (22:01:17) is later than every changed source file (latest 21:59:22). Only the build report is newer.
- Breaks: all 25 patches pass `git apply --check` on the new tree (read-only check). Each `-t` filter matches a test
  that exists.
- Result: **approve**. The four mediums are closed. No new high or medium finding. Two new lows.

### Earlier findings

| Finding | Status | Evidence |
|---|---|---|
| M1 paste of >6 digits plus a non-digit cut to 6 (review M1, security M1) | closed | `isOverlongCode` now counts ASCII digits anywhere in the pasted text (`linkSchema.ts:47-48`). The pasteTransformer and onPaste still share it, so the two cannot disagree. `"4815167\n"`, `"\r\n"`, `"\t"`, `"481516 7"` and `"4815167a"` are refused in both `linkSchema.test.ts` and `OtpScreen.test.tsx`: cells stay empty, the error shows, nothing is sent. `"481516\n"` and `"481516a"` are accepted as `481516`. Both sides of the bound are tested. Break 22 (the old condition) makes the refusal tests fail. |
| M2 refusal not announced (review M2) | closed | `onPaste` calls `announce(linkingMessages.code)` through the existing `useAnnounce` (`OtpScreen.tsx:223`). `LiveRegion` renders each message as a new element, so a repeated refusal is spoken again. Tests check `role="status"` holds the message after a refusal and is empty after an accepted paste. Break 23 makes them fail. |
| verify M1 a paste error mounts the summary and moves focus | closed | The summary now mounts on `summaryShown && codeError` (`OtpScreen.tsx:169`). `summaryShown` is set only by a failed Verify (`:193`) or a returned 400 (`:128`), and is cleared at every submit (`:191`). I traced each path. Before any submit: false. After `otp-incorrect`, 429 or 5xx: cleared by that submit and never set. After an invalid submit or a 400: the summary is already mounted, and a paste rewrites the same message with `requests` unchanged, so the effect in `ErrorSummary.tsx:26-28` does not refocus. With `reValidateMode: 'onSubmit'`, `codeError` cannot clear between submits, so a paste cannot remount the summary. Two sequence tests cover these paths, and break 24 (the old `summaryRequests > 0`) fails the first. |
| verify M2 disabled buttons with a transparent edge | closed | `button.tsx:11` uses `disabled:border-border` in place of `disabled:border-transparent`. `disabled.test.tsx:23` asserts it in all three variants, and break 25 fails it. My earlier note in Pass 2 (that the transparent edge matched `mockups/app.css:99`) was answered by the verifier against criterion 6's wording, which names the `border` edge. The criterion wins. |
| L1 to L4 | open, lows | Not in scope for a fix round. They go to `lows.md` at merge. L2 is partly covered now: `"4815167"` is pasted into an empty field. The "field holding 481" test still pastes `"4815 1678"`. |

### The brief's questions

- **`"481-5167"` now refused with the error. Is that within the decision?** Yes. The decision (decisions.md,
  2026-11-08) refuses "a pasted code longer than 6 digits" and shows the six-digit error. `481-5167` holds 7 digits.
  Before, input-otp cut it to `481-51` and its pattern check dropped it with no message. Now it is dropped and the
  error shows. Neither version changes the cells or spends an attempt, so the only change is a message the decision
  asks for. The Inputs table's own rows still hold: `481-516` and `Your code is 481516` (6 digits) are still ignored
  whole with no error (`linkSchema.test.ts` "not over-long" list). One gap: the story's Inputs bullet
  (`story-ui-linking.md:52-54`, "if what is left is digits only and longer than 6") no longer describes the code (N1).
- **Rewritten breaks.** The breaks that changed, by array index 2, 3, 4 and 16, are breaks 3
  (`paste-transformer-removed`), 4 (`overlong-paste-error-removed`), 5 (`password-manager-strategy-removed`) and 17
  (`disabled-button-faded`). In each, only the patch context moved, to the new line numbers, the new comment and
  `disabled:border-border`. The removed or changed line is the same, and the test filter is the same. Break 4's
  `if (false)` now removes the announcement as well as the error, and "refuses a pasted 7 digits with a space" still
  catches it through the field error. Breaks 2 and 16 by 1-based number (`input-otp-unpinned`,
  `focus-always-taken-at-end`) are byte-identical to the earlier ones. All of them still guard what they did.
- **Nothing else changed.** Confirmed above: six source files and the build report. The gateway files are
  byte-identical.

### New findings

#### N1 low: the story's Inputs mechanism text no longer matches the shared check

`docs/backlog/story-ui-linking.md:52-54` says the pasteTransformer refuses when "what is left [after removing spaces]
is digits only and longer than 6". The code now refuses whenever the pasted text holds more than 6 ASCII digits, as
the fix list directed. The behaviour is within the decision. Only the story text is stale. Fix: Sofia changes the
bullet to "holds more than 6 digits, whatever else it holds", and adds `481-5167` and `4815167\n` to the Inputs table
as refused rows. This goes in the records pull request, with no code change.

#### N2 low: after a wrong code, a refused paste is announced twice

`OtpScreen.tsx:241`: once `otp-incorrect` has set `wrongCode`, the `FieldError` keeps `role="alert"`. A refused paste
then changes its text from the wrong-code copy to "Enter the 6-digit code.", and the alert speaks it. The new
`announce` call speaks the same words in the polite region. A screen reader may say it twice. This does no harm to
the decision or the criteria. Fix (lows batch): give `FieldError` the alert role only while the message is the
wrong-code copy (`role={codeError === undefined && wrongCode ? 'alert' : undefined}`), or skip `announce` when
`wrongCode` is set. Add a test that asserts one announcement.

### Pass 2 on the delta

`summaryShown` beside `summaryRequests` is explained in one sentence: one says whether the summary shows, the other
makes it take focus again. The comment at `:86-87` says so. Counting digits with `/[0-9]/g` is simpler than the old
two-part condition. `useAnnounce` is the project's existing live region (WCAG 2.2 SC 4.1.3,
<https://www.w3.org/WAI/WCAG22/Understanding/status-messages>). No new abstraction, option or package. No `!`.

### Status (fix round 1)

```json
{"status": "approve", "record": "docs/verification/ui-linking-review.md", "findings": [
  {"severity": "low", "file": "docs/backlog/story-ui-linking.md", "line": 52, "issue": "The Inputs bullet still describes the old condition (digits only after removing spaces, longer than 6). The code now refuses any paste holding more than 6 digits, which is within the 2026-11-08 decision.", "fix": "Sofia updates the bullet and adds 481-5167 and 4815167\\n as refused rows (records PR)."},
  {"severity": "low", "file": "frontend/src/features/linking/OtpScreen.tsx", "line": 241, "issue": "After otp-incorrect, the FieldError keeps role=alert. A refused paste changes its text and also calls announce, so the same message may be spoken twice.", "fix": "Alert role only while the wrong-code copy shows, or skip announce when wrongCode is set; test one announcement (lows batch)."}
], "assumptions": ["Results read, not re-run; the breaks were checked with git apply --check only, and the verifier runs them.", "The platform .trx from the previous tree still covers the gateway, which is byte-identical."], "open_questions": [], "conflicts": []}
```

## Capture (supervisor, 2026-11-09)

### Escapes

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
|---|---|---|---|---|
| An over-long paste with a trailing non-space character was sliced to six digits and accepted | security and reviewer | pre-build review (the spec's check counted digits after removing spaces only) | input boundary | a paste rule is stated as a count over the pasted text, with trailing whitespace and letters among the test rows |
| A refused paste was not announced to screen readers | reviewer | builder self-check | missing test | each silent error state names its announcement in the spec |
| A paste error after an earlier submit remounted the error summary and moved focus | verifier | builder (no sequence test) | missing test | sequence tests for error states that follow a submit |
| Disabled buttons used a transparent border against criterion 6 | verifier | builder self-check | spec gap | none beyond the check |
| input-otp's empty style element would have broken the CSP | pre-build review | n/a (caught before the build) | n/a | a CSP probe in Chromium is part of any pre-build review that adds a UI package |

### Metrics

| Fix rounds | Full-suite runs | Stages | Notes |
|---|---|---|---|
| 1 (review, security, verify) | 3 (frontend build, gateway PlatformTests, fix round) | pre-build review; frontend builder x2; backend builder x1; reviewer x2; verifier x2; security x2; QA x1 | merged as `83c19a7` (#31); High-stakes, the human's diff review follows |
