# Verification: ci-e2e-regression

- Commit: `d57b1ae96e51dcb835bba74b2e392d9ed1508385` (origin/main), builder's patch uncommitted
- Diff hash: `562c5a2a11fe` (equals `test_results.diff_hash` in `ci-e2e-regression-build.json`; recomputed with `scripts/diff-hash.sh <tree> HEAD`)
- Date (UTC): 2026-11-06
- Tree: review copy `.../scratchpad/review-ci-e2e` (read only; the breaks ran in `run-breaks.sh`'s own throwaway worktree)
- Verifier model: gpt-6-astra via `scripts/verify-with-copilot.sh` (no fallback needed)
- Rule diff: none. Suite not re-run; results read from the builder's `TestResults/` (summary: PlatformTests 439 of 439, nine `scripts/*.test.sh`, shellcheck, actionlint, floor-guard, secret scan, forbidden terms all pass).

## Breaks (`scripts/run-breaks.sh`, exit 0, 11 of 11 failed as expected)

yq v4.54.1 was not installed and the builder's `/tmp/dd-ci-e2e-yq/bin` was gone. I downloaded `yq_darwin_arm64` from the mikefarah/yq v4.54.1 release and checked its SHA-256 (`fee511a1...2844`) against the release's `checksums` file (match). I ran `run-breaks.sh` on a copy of `build.json` whose `PATH=` prefix pointed at that binary; the patches and test names are unchanged.

| # | Criterion | Break | Test | Result |
|---|---|---|---|---|
| 1 | AC1 | rename out of a matching path missed | `scripts/regression-paths.test.sh` | failed as expected |
| 2 | AC1 | two dots instead of three | `scripts/regression-paths.test.sh` | failed as expected |
| 3 | AC2 | status 2 no longer fails the step | `scripts/regression-paths.test.sh` | failed as expected |
| 4 | AC3 | failing suite no longer fails the job | `scripts/regression-paths.test.sh` | failed as expected |
| 5 | AC4 | real run's diff would have no merge base | `scripts/regression-paths.test.sh` | failed as expected |
| 6 | AC5 | stale e2e comment returns | `scripts/regression-paths.test.sh` | failed as expected |
| 7 | AC6 | amendment row gone | `scripts/regression-paths.test.sh` | failed as expected |
| 8 | AC7 | mount source missing from list | `ComposeRenderTests.RegressionPathsList_CoversEveryStackInputAndTheJobsOwnFiles` | failed as expected |
| 9 | AC7 | job's own file missing from list | same | failed as expected |
| 10 | check-infra-paths | escape no longer ends a path | `scripts/check-infra-paths.test.sh` | failed as expected |
| 11 | check-infra-paths | any backslash ends a path | `scripts/check-infra-paths.test.sh` | failed as expected |

`run-breaks.sh` requires each test to pass without the break, so each of the 11 is a real guard, not a test that already fails.

## floor-guard

`scripts/floor-guard.sh` in the review copy: `floor-guard: clean`, exit 0.

## Criteria against the diff

| Criterion | Test | Proved |
|---|---|---|
| AC1 path script | `scripts/regression-paths.test.sh`; `scripts/regression-paths.sh` uses `git diff --name-only --no-renames -z <base>...<head>`, exits 0/1/2; new floor-guard step after `scripts/guard.test.sh` in `ci.yml` | yes |
| AC2 unconditional job | job has no job-level `if` or `needs`; `decide` step first after checkout, SHAs under `env:`, `run=false` and notice on non-PR events; in `ci-passed` needs, not in `publish` needs; `scripts/ci-gates.test.sh` | yes |
| AC3 failure fails the PR | no `continue-on-error`, retries or skips; the failing step fails the job and `ci-passed` (ci-gates cases); the diagnostics and `down -v` steps carry the `decide` output gate | yes by text; the real failing-suite path is not exercised (see low 1) |
| AC4 three real CI runs | none yet | no: needs CI's real runs, the supervisor's |
| AC5 comments | `regression-paths.test.sh` comment checks; the `e2e` comment is updated; the new job's comment gives the estimate, not a measurement | yes; the wall time is to go in the review record by the supervisor's decision |
| AC6 AD-32 amendment | `regression-paths.test.sh` docs check; AD-32 edit and `spine-amendments.md` row present | yes |
| AC7 list cannot drift | `ComposeRenderTests.RegressionPathsList_CoversEveryStackInputAndTheJobsOwnFiles` plus breaks 8 and 9 | yes |

Copilot (gpt-6-astra) said `VERDICT: FAIL` on two points it ranked medium and one low (below); it also could not run the OpenAPI comparison, the diff-hash or floor-guard (permission-blocked). No OpenAPI change is in the diff, and I ran the hash and floor-guard myself.

## Findings

No high or medium finding.

1. **Low, AC3 (disclosed by the builder).** `e2e/run-regression.sh` runs all seven groups and starts each with `docker compose down -v`, and Playwright clears `test-results` on each run. A failure in groups 1 to 6 therefore loses its `docker compose ps`, logs and `error-context.md` before the `failure()` step runs; that step shows the last group only. The failing test's own output stays in the step log, and the job still fails correctly. Copilot ranked this medium; I rank it low because it costs debuggability, not the gate, and the fix is in `e2e/`, which the builder may not touch. Suggest the supervisor add it to the epic's lows with item 2.
2. **Low, spec Inputs (deviation, supervisor's decision).** `--wait-timeout 600` is not on `e2e/run-regression.sh:47` (`up -d --wait --build`). A stuck start runs to the job's 60-minute timeout instead of failing in 10 minutes. The builder and supervisor both recorded this as filed low. Copilot ranked it medium; I follow the supervisor's recorded decision and name it so it is not lost.
3. **Low, AC5.** The new job's comment gives the 25 to 35 minute estimate, which is allowed until AC4's run measures it; the measured wall time must go in the review record, not `ci.yml`, as the supervisor decided.

## Non-standard mechanisms (reported, not failed)

- `scripts/regression-paths.sh` is a custom path script; the builder gives ad-012 (*Options rejected*: `on.paths` leaves `ci-passed` pending, marketplace actions rejected) as the reason no standard fits. Its pieces follow documented mechanisms (`git diff`, `GITHUB_OUTPUT`, `env:`).
- Beyond the spec's Touches, disclosed by the builder: `scripts/ci-gates.test.sh` (pins `ci-passed` needs), `scripts/check-infra-paths.sh` plus new `scripts/check-infra-paths.test.sh` and one floor-guard step for it (the JSON-escape fix), and `docs/tickets/README.md`. The additions are small and fall within the pipeline's own files.
- The builder's `breaks` hard-code `/tmp/dd-ci-e2e-yq/bin` for yq. That path does not exist on another run, so the breaks as written need yq v4 on `PATH`; a reader who re-runs them must install it.

## Needs CI or the supervisor

- AC4: the PR's own run with the full suite and per-group lines, the wall time, the scratch docs-only skip PR and main's `push` notice.
- The real failing-suite path (AC3) and the first run of the suite on a hosted Linux runner.

## Report

```json
{"status": "pass", "sha": "d57b1ae96e51dcb835bba74b2e392d9ed1508385", "diff_hash": "562c5a2a11fe", "record": "docs/verification/ci-e2e-regression-verify.md", "findings": [{"severity": "low", "criterion": "AC3", "issue": "diagnostics of an early failing group are lost; the step shows the last group only", "evidence": "e2e/run-regression.sh:45 down -v at each group start; builder assumption"}, {"severity": "low", "criterion": "Inputs", "issue": "--wait-timeout 600 not added to e2e/run-regression.sh:47", "evidence": "supervisor's decision, filed as low"}, {"severity": "low", "criterion": "AC5", "issue": "wall time is an estimate until AC4's run measures it", "evidence": "ci.yml comment"}], "assumptions": ["yq v4.54.1 was downloaded and its SHA-256 checked against the release checksums"], "open_questions": [], "conflicts": []}
```

## Fix round 1 (2026-11-06), review copy at origin/main d57b1ae plus the change, diff hash `c8e47da868f4`

- Hash: `scripts/diff-hash.sh <copy> origin/main` prints `c8e47da868f4`. The fix builder's `test_results.diff_hash` is `9af9dbd135e4`; the difference is the supervisor's two rows in the spec's trigger table (`docs/backlog/story-ci-e2e-regression.md`, docs-only; the brief says check-ready, check-infra-paths and rule-text re-ran). I did not recompute `9af9dbd135e4`, so the docs-only claim is the brief's. Suite not re-run; results read from the builder's `TestResults/` (PlatformTests 439 of 439, nine `scripts/*.test.sh`, `regression-paths.test.sh` also on Linux, shellcheck, actionlint, floor-guard, secret scan).
- `scripts/floor-guard.sh`: clean.

### R-M1: fixed

- `scripts/regression-paths.list` now has `scripts/operations-token.sh` and `scripts/test-lock.sh`. My own search of `e2e/` for `scripts/*.sh` names finds exactly those two, both on the list.
- `scripts/regression-paths.test.sh` has the new check: every `scripts/*.sh` path named under `e2e/` (found with `git grep --untracked`; an empty search fails) is a line of the list. Break 12 (removes the two lines) makes it fail.
- The spec's trigger table has the two rows.

### Breaks (`scripts/run-breaks.sh`, exit 0, 12 of 12 failed as expected)

yq v4.54.1 was not on `PATH`. I downloaded `yq_darwin_arm64` from the mikefarah/yq v4.54.1 release into a new directory and checked its SHA-256 (`fee511a181bd...2844`) against the release's `checksums` file (match). I ran `run-breaks.sh` on a copy of `build.json` whose `PATH=` prefix pointed at that binary; patches and test names are unchanged.

| # | Criterion | Test | Result |
|---|---|---|---|
| 1 to 7, 12 | AC1 to AC6, R-M1 | `scripts/regression-paths.test.sh` | failed as expected (8 of 8) |
| 8, 9 | AC7 | `ComposeRenderTests.RegressionPathsList_CoversEveryStackInputAndTheJobsOwnFiles` | failed as expected |
| 10, 11 | check-infra-paths fix | `scripts/check-infra-paths.test.sh` | failed as expected |

### Findings

No high or medium finding. The three lows of the first pass stand (AC3 early-group diagnostics, `--wait-timeout 600` not added, AC5 wall time is an estimate); AC4 still needs CI's real runs. The builder's breaks still hard-code `/tmp/dd-ci-e2e-yq/bin`, so a re-run needs yq v4 on `PATH`.

```json
{"status": "pass", "sha": "d57b1ae96e51dcb835bba74b2e392d9ed1508385", "diff_hash": "c8e47da868f4", "record": "docs/verification/ci-e2e-regression-verify.md", "findings": [{"severity": "low", "criterion": "AC3", "issue": "diagnostics of an early failing group are lost; the step shows the last group only", "evidence": "e2e/run-regression.sh:45 down -v at each group start"}, {"severity": "low", "criterion": "Inputs", "issue": "--wait-timeout 600 not added to e2e/run-regression.sh:47", "evidence": "supervisor's decision, filed as low"}, {"severity": "low", "criterion": "AC5", "issue": "wall time is an estimate until AC4's run measures it", "evidence": "ci.yml comment"}], "assumptions": ["the 9af9dbd135e4 to c8e47da868f4 difference is the docs-only spec edit, as the brief says; not recomputed"], "open_questions": [], "conflicts": []}
```
