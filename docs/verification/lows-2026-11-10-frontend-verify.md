# Verification: lows-2026-11-10-frontend

- Commit: `f6193f5b574953f4212a8bbea9d6b4668430f886` (uncommitted diff hash `606931f67f59`)
- Verifier: gpt-6-astra via GitHub Copilot CLI
- Date (UTC): 2026-11-10T18:05:22Z

The diff is frontend-only. I’ll check the saved results against the current diff, run the requested non-suite checks, and assess the frontend lows without changing source files.


The builds and non-suite checks pass, and the saved JUnit report records 866 tests with no failures. Code review found an unchanged E2E assertion that conflicts with the new OTP styling, plus a stale-error case that the OTP fix still misses.


VERDICT: FAIL

| Criterion | Test | Proved (yes/no) |
|---|---|---|
| 1. Hint spacing and optical offset | `TransactionsScreen.test.tsx`: keeps the hint icon 4px from the text; offset comment inspected | yes |
| 2. SearchPriority handle path | `customerStatus.test.ts`: maps underReviewPrioritised to the custom SearchPriority icon | yes |
| 3. Remove placeholder fallback | Same test; explicit guard inspected | yes |
| 4. Document accepted skip-link overlap | No test; comment updated | no |
| 5. Correct flex content-edge comment | No test; comment corrected | no |
| 6. Error-summary links ≥44px | `ErrorSummary.test.tsx`: draws each field link at least 44px tall | yes |
| 7. Shared outline-width token | `theme.test.ts`: declares the outline width once | yes |
| 8. Equal-width active OTP cell | `input-otp.test.tsx`: gives the active cell a 2px ring edge; class assertions only | no |
| 9. Paste `4815167` into `481` | `OtpScreen.test.tsx`: refuses a pasted `"4815167"` into a field holding 481 | yes |
| 10. Reconcile inline Start again target | No test; comment only, reconciliation deferred | no |
| 11. Dim disabled field label | `field.test.tsx`: dims its label while it holds a disabled control | yes |
| 12. Avoid duplicate paste-error alerts | `OtpScreen.test.tsx`: does not make the field error an alert… | yes |
| 13. Clear stale error beside six digits | `OtpScreen.test.tsx`: clears the refused-paste error…; misses full-field edits | no |
| 14. Notice for antiforgery 429 | `OtpScreen.test.tsx`: gateway refusal and non-problem-body 429 tests | yes |
| 15. Prevent clipped list columns | Transactions/disputes row-column tests assert classes, not clipping | no |
| 16. Narrow transaction text at 200% | None; explicitly unfixed | no |
| 17. Prevent header overflow at 200% | `routing.test.tsx` wrapping tests assert classes, not overflow | no |
| 18. Review alignment and bold transaction | `ReviewScreen.test.tsx`: aligns Change with the foot of the hero amount… | yes |
| 19. Label-column token | `theme.test.ts`: declares the list columns; documentation amendment unproved | no |
| 20. Correct amount without second click | `RaiseScreen.test.tsx`: drops the expected-amount error…before the field is left | yes |
| 21. Reconcile pending opacity with DESIGN | Existing test preserves opacity; reconciliation deferred | no |
| 22. Prevent hero-amount overflow | Review/detail tests assert wrapping classes, not measured overflow | no |
| 23. Dialog footer switches at md | `alert-dialog.test.tsx`: turns into a row at md | yes |
| 24. Wordmark corners and baseline | `wordmark.test.ts`: four-corner transparency and dot-position tests | yes |
| 25. Session-warning initial focus and trapping | `InactivityWarning.test.tsx`: opens with focus on Stay signed in… | yes |
| 26. Dialog scroll-lock CSP violation | None; existing limitation retained | no |

### Failures with evidence

1. **Medium — existing E2E assertion contradicts the change.** `frontend/src/components/ui/input-otp.tsx:60` replaces the active cell’s 2px border with a 1px border plus inset ring. `e2e/tests/linking-screens.spec.ts:220` still requires `borderTopWidth >= 2`, so its keyboard-focus check will reject the new styling. This is established from code; E2E was not rerun.

2. **Medium — stale OTP error remains for full-length edits.** `OtpScreen.tsx:220–227` clears errors only through `onComplete`, while refused pastes always set an error. Installed `input-otp` invokes `onComplete` only when the previous value was shorter than six digits. Enter six digits, refuse an overlong paste, then replace a digit or paste another six-digit code: the value stays six digits long, so the stale error remains. The new test starts with an empty field and misses this case.

3. **Incomplete proof.** The layout tests above check class names, not geometry. The builder describes a Chromium smoke pass but supplies no corresponding results artifact. Several rows explicitly remain deferred or unfixed. Comment-only rows are marked unproved, not reported as behavioral regressions.

**Checks:** Frontend build, typecheck, lint and formatting passed. Backend build passed with warnings treated as errors; style and whitespace checks passed after restore. Generated `backend/openapi.json` matches HEAD. The sole listed results file, `TestResults/frontend-junit.xml`, contains 866 test cases with zero reported failures/errors. No suites were rerun. No shell scripts or platform tests changed. Diff-fingerprint and floor-guard execution were permission-blocked.

### Standards

None: no new mechanism replaces a documented framework approach or established standard.

```json
{"status":"findings","record":null,"findings":[{"severity":"medium","issue":"OTP styling contradicts the existing E2E focus assertion"},{"severity":"medium","issue":"Stale OTP errors survive full-length edits"}],"assumptions":["Criterion numbering follows the frontend build report"],"open_questions":[],"conflicts":["No record file written: read-only request","Diff fingerprint and floor guard were permission-blocked"]}
```

## Verifier's own checks (2026-11-10)

- Diff hash: scripts/diff-hash.sh . HEAD = 606931f67f59, equal to the build report's test_results.diff_hash. Results file TestResults/frontend-junit.xml is present (866 tests, none failed per Astra). Suite not re-run.
- scripts/floor-guard.sh: clean.
- Breaks (20): scripts/run-breaks.sh ran, but every baseline failed with "Cannot find package". Its throwaway copy has no frontend/node_modules and no gitignored frontend/src/api/generated, and it runs the shell command from the copy root, not frontend. This is a tooling gap, not a finding on the diff. The same 20 patches were then run by hand in a throwaway worktree (story patch applied, node_modules and generated API linked, run from frontend): all 20 pass without the break and fail with it (break 6, "refuses a pasted", fails 2 tests). All 20 count.
- Astra's two medium findings, checked by the verifier:
  1. Confirmed. frontend/src/components/ui/input-otp.tsx now draws the active cell with a 1px border plus an inset ring, but e2e/tests/linking-screens.spec.ts:220 still requires borderTopWidth >= 2 on the active cell, so that e2e keyboard-focus check will fail. It needs the stack: CI's e2e run settles it; not run here.
  2. Confirmed. OtpScreen.tsx clears the code error only in onComplete. Installed input-otp calls onComplete only when the previous value was shorter than six digits (D.length<t && l.length===t). After six digits, then a refused over-long paste, an edit or paste that keeps six digits leaves the stale error. The new test starts from an empty field.
- Astra's "incomplete proof" notes (class-name assertions for layout rows; comment-only rows 4, 5, 10, 21; deferred or unfixed rows 16, 26) are reported, not failed: the builder lists them as comment-only, deferred or unfixed.
- Non-standard mechanisms: none.
- Final status: findings (two medium).

## Fix round 1 (2026-11-11)

Verified: HEAD f6193f5b5749 plus the uncommitted diff, diff hash ade7d85ef9ea (equal to test_results.diff_hash in the build report).

- Results file: TestResults/frontend-junit.xml present, 869 testcases, 0 failures or errors (read, suite not re-run). The count is 866 + 2 (M1) + 1 (M2), as the builder states.
- scripts/floor-guard.sh: clean.
- Breaks (24): scripts/run-breaks.sh cannot run frontend breaks, so the 24 were run by hand in a throwaway worktree at f6193f5 with the story diff applied, node_modules linked, the generated API copied, from frontend/. Each named test passes without the break and fails with it; no filter matched zero tests; every patch applied and reverted cleanly. All 24 count. (A first attempt in a plain rsync copy outside any worktree failed the baseline of breaks 8 and 10, and the whole OtpScreen file, on antiforgery requests; the same tests pass in the real worktree and in the throwaway worktree, so it was my copy's environment, not the diff.)
- M1 (stale code error after a full-length edit): fixed. OtpScreen.tsx now clears the code error from the field's onChange whenever the value holds six digits; onComplete is gone. The new it.each test starts from a field holding six digits, refuses a 7-digit paste, then edits to a value that still holds six, and expects no error and aria-invalid="false" (two cases, "edited by"); break 21 (back to onComplete) fails it. The wrong-code role="alert" is withheld only when a paste error replaces it (break 8). The first build's rows 12 and 13 tests still pass (OtpScreen file: 76 passed in the throwaway worktree).
- M2 (wrapped tab bar hides the page end): fixed in design. The bar is "sticky bottom-4 mx-4 mb-4" in the layout's flex column and RootLayout keeps no pb-24, so the page ends above the bar at any bar height; the segments use px-2 instead of px-3 (reviewer's L8). The new routing test asserts sticky, bottom-4, not fixed, no pb-24; breaks 22 and 23 (back to fixed, back to pb-24) and 13 and 24 (wrap, px) fail it. jsdom has no layout, so the proof is class-level; the builder's headless Chromium numbers at 320 and 360px and 100, 150, 200% are reported in its message, with no results artifact. I did not re-measure.
- Rows from the first build: their tests pass in the results file, and their breaks (1 to 7, 9 to 11, 14 to 20) still fail as expected.

Reported, not failed: layout rows stay class-level (jsdom); the e2e focus check at e2e/tests/linking-screens.spec.ts:220 is the carried medium from round 0, assigned to QA in the same batch (needs the stack, not run); the dispute detail page still scrolls 10px sideways at 320px with 200% text (builder's carried note). Non-standard mechanisms: none.

Result: no new high or medium finding; M1 and M2 closed.

## Fix round 2 (2026-11-11)

Verified: HEAD f6193f5b5749 plus the uncommitted diff, diff hash c3301d13d4a7 (scripts/diff-hash.sh <worktree> f6193f5; equal to test_results.diff_hash in the build report).

- Results file: TestResults/frontend-junit.xml present, 867 testcases, 0 failures or errors (read, suite not re-run).
- Navigation.tsx and RootLayout.tsx: the diff against f6193f5 is empty for both (byte-identical to main). The tab bar is back to its one-row, fixed form.
- routing.test.tsx: the only change against f6193f5 is the added header-row wrap test ("lets the header row wrap"); no tab-bar test remains and none of main's tests was removed.
- scripts/floor-guard.sh: clean.
- Breaks (20): run by hand in a throwaway worktree at f6193f5 with the story diff applied (plus the new alert-dialog.test.tsx), node_modules linked, the generated API copied, from frontend/ (scripts/run-breaks.sh still cannot run frontend breaks). Each named test passes without the break (exit 0) and fails with it (exit 1); every patch applied and reverted cleanly; no filter matched zero tests. All 20 count. The four tab-bar breaks of round 1 are gone from the list, as the fix list asked.
- Earlier rounds: M1's onChange fix is still covered (break 20, "edited by", and break 9) and the rows from rounds 0 and 1 have passing tests in the results file and failing breaks (1 to 19).

Reported, not failed: the tab bar's 320px and 200% text overflow is carried to lows.md by the supervisor's decision (fix list, Fix round 2); layout rows stay class-level (jsdom); the e2e focus check at e2e/tests/linking-screens.spec.ts:220 is carried to QA/CI (needs the stack, not run); the builder's Chromium measurements have no results artifact and were not re-measured. Non-standard mechanisms: none.

Result: no high or medium finding.
