# Verification: ui-linking

- Commit: `2f897c37b3b58b11119c6126123213f98defeb43` (uncommitted diff hash `a09f7e0bc41f`)
- Verifier: gpt-6-astra via GitHub Copilot CLI
- Date (UTC): 2026-11-08T19:43:55Z

The diff includes prerequisite UI work as well as linking changes. I’ll review the combined change, read the saved test results, and run build and static checks without rerunning suites.


The builds and static checks passed, and the saved platform results show 440 passes. The frontend JUnit file is outside this worktree and access was denied. I also found a mismatch with the required disabled-button border and am checking the paste-error focus path.


VERDICT: FAIL

| Criterion | Test | Proved (yes/no) |
|---|---|---|
| AC1: Step indicators | `DetailsScreen.test.tsx` / `OtpScreen.test.tsx`: step-before-heading and locked-state assertions | no |
| AC2: Input OTP installation | `packagePins.test.ts`; `input-otp.test.tsx`: “exports the three parts … and no separator” | no |
| AC3: Input, paste and submission | `OtpScreen.test.tsx`: typing, paste-bound, autofill and submission cases; recovery-path gap below | no |
| AC4: Cell styling | `input-otp.test.tsx`: dimensions, active ring, invalid border and reduced-motion cases | no |
| AC5: CSP | `GatewayTests.AppOrigin_ContentSecurityPolicies_HoldBaseUriNone` and `AppOrigin_StyleSrcHash_IsTheHashOfTheEmptyString` passed; browser coverage remains unproved | no |
| AC6: Disabled forms and focus | Both screen suites’ rate-limit cases; `disabled.test.tsx` asserts the wrong border | no |
| AC7: Help block | Both screen suites’ help-region, heading-order and locked/rate-limited cases | no |

Frontend tests exist, but their passing results could not be verified; therefore their criteria are not marked proved.

### Failures

- **Medium — AC3: an overlong paste can unexpectedly focus the error summary.** Submit an invalid code, then submit six digits and receive `otp-incorrect`, then paste seven digits. `summaryRequests` remains positive; the successful client validation cleared the previous field error. The paste’s `setError` therefore remounts `ErrorSummary`, whose mount effect moves focus. This contradicts the required field-only error with no summary or focus movement until Verify. Evidence: `OtpScreen.tsx:87,166–170,213–216`; `ErrorSummary.tsx:26–28`. Existing paste tests start from fresh forms and miss this sequence.

- **Medium — AC6: disabled buttons lack the required border-token edge.** `frontend/src/components/ui/button.tsx:11` uses `disabled:border-transparent`, not `disabled:border-border`. `disabled.test.tsx:23` explicitly expects that incorrect styling.

- **Verification blocker — frontend results inaccessible.** `docs/verification/ui-linking-build.json:327` references `frontend-junit.xml` in another worktree. Access was denied, and this worktree’s `TestResults` contains only the TRX. This establishes unavailable evidence, not failed frontend tests. The fingerprint and floor-guard commands were also denied.

**Completed checks:** frontend build, typecheck, lint and formatting passed; backend and platform builds passed with zero warnings; backend/platform style and whitespace checks passed. Generated OpenAPI exactly matches `backend/openapi.json`. The TRX records **440 passed, zero failed**. No shell scripts changed. No suites were rerun or source files edited. Running-stack CSP and Chromium focus checks remain QA responsibilities.

### Standards

None identified.

{"status":"blocked","record":null,"findings":[{"severity":"medium","criterion":"AC3","issue":"Overlong paste can remount and focus the error summary."},{"severity":"medium","criterion":"AC6","issue":"Disabled buttons use a transparent border instead of the border token."}],"assumptions":[],"open_questions":["Frontend JUnit results and diff fingerprint could not be verified."],"conflicts":[]}

## Verifier's own checks (2026-11-08)

- Worktree HEAD 2f897c37b3b58b11119c6126123213f98defeb43; scripts/diff-hash.sh on the builder's worktree gives e36d8e04ab01, equal to test_results.diff_hash in ui-linking-build.json. (The Copilot hash a09f7e0bc41f above is of the throwaway copy, which also holds the spec file.)
- Results: PlatformTests TRX 440 total, 440 passed, 0 failed. Frontend JUnit (agent-a260c2761196a0cc9): 771 tests, 0 failures, 0 errors. diff -rq of frontend/ between that worktree and the builder's (excluding node_modules, dist, coverage) shows no difference, and that worktree's HEAD is the same base commit. The Copilot "results inaccessible" blocker is a sandbox path limit only; the file was read here.
- Package: frontend/package.json pins "input-otp": "1.5.0" exactly; lucide-react 1.70.0 counted as the base's.
- scripts/floor-guard.sh in the builder's worktree: clean.
- scripts/run-breaks.sh: 21 of 21 breaks failed as expected (19 Vitest, 2 PlatformTests: AppOrigin_ContentSecurityPolicies_HoldBaseUriNone, AppOrigin_StyleSrcHash_IsTheHashOfTheEmptyString); none passed, none errored.
- Non-standard mechanisms: none reported.
- Findings (medium, from the Copilot read; the code lines were checked against the spec but not reproduced by running): AC3 an overlong paste after a prior submit remounts ErrorSummary and moves focus (OtpScreen.tsx: summaryRequests > 0 && codeError, onPaste setError); AC6 button.tsx has disabled:border-transparent where the spec says a border edge, and disabled.test.tsx asserts the transparent border.
- QA's or CI's: CSP in a browser, Chromium focus behaviour.

## Fix round 1 (2026-11-08)

- Worktree HEAD 2f897c37b3b58b11119c6126123213f98defeb43; scripts/diff-hash.sh <worktree> HEAD gives bc234a527a9a, equal to test_results.diff_hash in ui-linking-build.json. Results: frontend-junit.xml (788 tests, builder's worktree) and the PlatformTests .trx (440 passed, 0 failed, previous worktree agent-ac84704905088292a). cmp shows security-headers.conf.template and GatewayTests.cs byte-identical between the two worktrees, so the .trx still covers the gateway part.
- Copilot (gpt-6-astra, throwaway copy with the spec, the JUnit and the TRX; its hash 129a817dad7f is of the copy, which also holds the spec): VERDICT FAIL on two mediums, both about e2e files: e2e/tests/support.ts:42 (gatewayContentSecurityPolicy lacks the style-src hash) and linking-screens.spec.ts:376-380,418-421 (expects aria-disabled, fills rate-limited fields), plus a missing /link/otp zero-violation test and Chromium focus test. The spec assigns each of these to QA (Tests and commits: QA updates the CSP expectations, QA adapts linking-screens.spec.ts, the zero-violation e2e, the Chromium focus e2e), and the builder lists them under e2e_for_qa. Reported as QA's, not failed. AC4 viewport proof and the running-stack CSP check are QA's too.
- Copilot confirmed: frontend build, typecheck, lint and format, backend and platform builds (warnings as errors) and style checks pass; OpenAPI matches; no shell script changed; 788/788 and 440/440. Its round-0 mediums (AC3 summary focus on paste, AC6 disabled border) no longer appear. AC1, AC2, AC3, AC7 proved; AC4, AC5, AC6 not proved by unit tests alone (QA).
- scripts/floor-guard.sh in the builder's worktree: clean.
- scripts/run-breaks.sh: 25 of 25 breaks failed as expected (23 Vitest, 2 PlatformTests), exit 0; none passed, none errored.
- Non-standard mechanisms: none reported.
- Findings: none high or medium.
