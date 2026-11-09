# Verification: ui-notices-states

- Commit: `2f897c37b3b58b11119c6126123213f98defeb43` (uncommitted diff hash `788246659b5a`, taken in a throwaway copy that also holds the spec file; the worktree's own hash is `f1dfe39d2913`, see the verifier's additions)
- Verifier: gpt-6-astra via GitHub Copilot CLI
- Date (UTC): 2026-11-08T11:41:53Z

VERDICT: FAIL (Astra's verdict; the verifier's weighting is in the additions below)

| Criterion | Test | Proved (yes/no) |
|---|---|---|
| AC1: Alert tones, icons, roles and wrapping | `alert.test.tsx`: five-tone cases, title wrapping, description colour and token assertions | yes |
| AC2: Reference row, action placement and Copy outcomes | `ProblemNotice.test.tsx`: reference presence/absence, action ordering, clipboard success/failure and two-second reset | yes |
| AC3: Error-summary semantics and field icons | `ErrorSummary.test.tsx`: focused group, inner alert, heading and links; `field.test.tsx`: icon and attributes | yes |
| AC4: Rate-limit tone without changing behaviour | `DetailsScreen.test.tsx` and `OtpScreen.test.tsx`: existing wait/countdown cases with Clock assertions | yes |
| AC5: Shared state surfaces and stable heading | `StateSurface.test.tsx`, routing and screen cases; detail tests for heading identity and focus after 404 | yes |
| AC6: Two-step decorative progress | `StepProgress.test.tsx`: label, both segment states and hidden bar | yes |

### Astra's failures

- Package-freeze boundary: `frontend/package.json:28` and `frontend/package-lock.json:21,6702` add `lucide-react` (story lines 140 and 163 require both files unchanged). The builder attributes this to the uncommitted prerequisite.
- Raw-pixel boundary: `frontend/src/components/ProblemNotice.tsx:53` has `border-[1.5px]` in the rewritten Copy button (story lines 135 and 162).

Checks Astra ran: frontend build, typecheck, lint and format passed; backend build (warnings as errors), dotnet format style and whitespace passed; generated OpenAPI matches the committed document; the JUnit file has 713 tests, no failures, errors or skips. Visual measurements remain QA's.

Standards: none.

## Verifier's additions (2026-11-08)

- Commit verified: `2f897c37b3b58b11119c6126123213f98defeb43`, uncommitted. `scripts/diff-hash.sh <worktree> 2f897c3` = `f1dfe39d2913`, equal to `test_results.diff_hash` in the build record. The results file `TestResults/ui-notices-states-vitest-junit.xml` is present.
- Combined tree: the working tree holds the base story ui-tokens-icons plus this story. `lucide-react` in `package.json` and `package-lock.json` comes from the base patch, not from this story, so Astra's package-freeze failure is not counted against this story. The supervisor should confirm the freeze holds against the base commit.
- floor-guard (worktree, read-only): `floor-guard: clean`.
- The builder's worktree was not edited; the Copilot step ran in a throwaway copy.

### Breaks (scripts/run-breaks.sh)

First run: all 9 reported FAIL, "needs a test_method". The build record names each break's command under `test`, but the script reads `test_method`. I re-ran the same script on a copy of the build record with `test_method` set to `test` (jq, kept outside the worktree); every other field is unchanged.

Result: 9 of 9 `ok ... failed as expected` (each test passes without its break and fails with it).

| Break | Criterion | Result |
|---|---|---|
| 1 | AC1 | failed as expected |
| 2 | AC2 | failed as expected |
| 3 | AC3 | failed as expected |
| 4 | AC3 | failed as expected |
| 5 | AC4 | failed as expected |
| 6 | AC5 | failed as expected |
| 7 | AC5 | failed as expected |
| 8 | AC4 | failed as expected |
| 9 | AC6 | failed as expected |

### Findings

- medium, Boundaries and Verify: in `frontend/src/components/ProblemNotice.tsx` the rewritten Copy button adds `border-[1.5px]` (a `+` line in the diff). The story says "no raw hex or px value in a component" and "the diff has no raw hex or px in a component". The value was copied from the old button, but the new line still carries it. Fix: use a theme token or an existing border-width utility.
- low, build record: `breaks[].test` should be `test_method`, so `scripts/run-breaks.sh` runs unmodified.

Not run (QA or CI): visual measurements, `e2e/run-regression.sh` with axe.

## Fix round 1 (2026-11-08)

- Commit verified: 2f897c37b3b58b11119c6126123213f98defeb43, uncommitted (base ui-tokens-icons plus this story plus the fix round). scripts/diff-hash.sh gives 7fcd094aa43d, equal to test_results.diff_hash in the build record. Results file TestResults/ui-notices-states-vitest-junit.xml present: 715 tests, 0 failed. Suite not re-run.
- Copilot step (gpt-6-astra, throwaway copy carrying the same patch; the copy's hash was 7fcd094aa43d before I added the spec and record files): it wrote VERDICT: FAIL, but only because its sandbox denied the fingerprint script ("evidence-provenance blocker, not an observed code defect or confirmed hash mismatch"). I ran the script myself and it matches. Its other results: AC1 to AC6 each proved by a named test; frontend build, typecheck, lint and format pass; backend build and format pass; openapi.json matches; Standards: none. Not a finding. Its verdict file was not copied over this record.
- floor-guard (worktree, read-only): clean.
- Fix list items: #1 (reference row stretches) is proved by break 10, #2 (Copy pill from the shared Button, no border-[1.5px]) by break 11, #3 (test_method key) by the script running the record unmodified.

### Breaks (scripts/run-breaks.sh, unmodified build record)

11 of 11 "ok ... failed as expected" (each test passes without its break and fails with it): AC1, AC3 (2), AC4, AC5 (3), AC6, AC2 (3, the last two for fix-round medium 1 and 2).

### Findings

None. lucide-react in the package files is the base story's, not counted. Not run (QA or CI): visual and real-browser measurements (the 1280px hairline check), e2e/run-regression.sh with axe.
