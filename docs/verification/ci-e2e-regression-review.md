# ci-e2e-regression post-build review

- Date: 2026-11-06
- Reviewed: origin/main `d57b1ae` plus the builder's uncommitted patch, diff hash `562c5a2a11fe`. `scripts/diff-hash.sh`
  run on the review copy gives the same value, so this review and the builder's results cover the same change.
- Tier: Feature, so the reviewer also ran the security checklist (`.claude/skills/security-story/SKILL.md`).
- Inputs: `docs/backlog/story-ci-e2e-regression.md`, `docs/verification/ci-e2e-regression-prebuild-review.md`,
  `docs/verification/ci-e2e-regression-build.json`, `TestResults/`.
- Known and accepted by the supervisor, so not raised here: `--wait-timeout 600` on `e2e/run-regression.sh` is not
  built (it goes to the lows); the measured wall time goes in this record after CI's first run; the builder added
  `scripts/check-infra-paths.test.sh` and a floor-guard step for it.
- Verdict: **changes**, for one medium finding (M1).

## The three questions in the brief

### Can the job block the required `ci-passed` gate when it should not?

Not through the job's own logic.

- The job has no job-level `if` or `needs` (`regression-paths.test.sh` asserts this), so it is never `skipped`. A skipped
  job would fail `ci-passed`, and `ci-gates.test.sh` now proves that `failure`, `cancelled` and `skipped` of
  `e2e-regression` each fail the gate.
- On a push, or on a pull request with no matching path, `decide` exits 0 and writes `run=false`. Every later step is
  gated on `run == 'true'`, so the job succeeds. The test runs the step's real script under `bash -e` with stub statuses
  0, 1, 2, 3, 127 and 128 and on `push`.
- Statuses 2 and above fail the job on purpose. In practice that happens only when a SHA from the event payload cannot
  be fetched (L4): the check fails safe, the cause is visible, and it is rare.
- Still unproved: whether the suite itself passes on a hosted Linux runner. That is criterion 4, which needs real CI
  runs. With retries at 0, a 60-minute timeout and no `--wait-timeout` yet (accepted low), a stuck start or a flaky
  spec turns `ci-passed` red. The spec's re-run-once rule covers this.
- `concurrency: cancel-in-progress` cancelling a superseded run fails that run's `ci-passed`. That behaviour already
  existed and only affects runs that have been replaced.

### Can a path filter skip a run that is needed?

Yes, in one case (M1). Otherwise the decision is sound:

- `git diff --name-only --no-renames -z base...head` counts renames in both directions and deletions. It also handles
  names git would quote, and ignores the base's own changes. Each of these has a scratch-repo case, and the breaks show
  the cases fail without the change.
- If `github.event.pull_request.base.sha` is stale, it is an ancestor of the current base tip, because `main` cannot be
  force-pushed. The three-dot range then starts earlier and catches more paths, never fewer. A stale base causes extra
  runs, never missed ones.
- Skipping is safe only because of the ruleset: up-to-date branch required (`strict_required_status_checks_policy:
  true`), squash merges only, no force pushes, and `bypass_actors: []` (`.github/rulesets/main.json`). Under those rules,
  the content of the listed paths on `main` is always a tree that some pull request's merge ref ran the suite on.
- The list covers every Compose build context and bind source. That was checked by reading `docker-compose.yml`, the two
  e2e overlays (which add environment variables only) and the new platform fact.
- The gap: the suite also runs a repository script that is not a Compose input. See M1.
- Out of scope by design (a nightly run was rejected): drift from outside the repo, such as unpinned `apt` packages in
  the Dockerfiles or runner image updates. That kind of break shows up on the next pull request that triggers the suite.

### Does a push to main behave as the spec says?

Yes. `decide` skips the script, writes `run=false`, and prints the notice that names the squash commit and the
up-to-date rule. Every later step is skipped and the job passes. `ci-passed` needs the job; `publish` does not
(`ci.yml:489`, asserted in `regression-paths.test.sh`). Main's real run is still criterion 4's third run.

## Pass 1: correctness

| Criterion | Proved by | Result |
|---|---|---|
| 1 Tested script | `scripts/regression-paths.test.sh` (each list line, the 7 near misses, renames both ways, deletion, quoted name, branch behind base, 8 exit-2 cases); breaks 1 and 2 | Met |
| 2 Unconditional job | `regression-paths.test.sh` (job shape, decide run on stub statuses and on push); `ci-gates.test.sh`; break 3, break 5 | Met |
| 3 Failure fails the PR | Step `if`s, no `continue-on-error`, no `retries`; break 4 | Met in shape. The diagnostics are weak for an early group's failure (L1) |
| 4 Real runs | Not yet: this PR's run, the supervisor's scratch skip PR, main's run | Open. Proved only in CI. Record the wall time here |
| 5 Comments | `regression-paths.test.sh` comment checks; break 6 | Met. The estimate wording goes stale after the first run (L2) |
| 6 AD-32 amendment | `regression-paths.test.sh` docs check; break 7 | Met |
| 7 List cannot drift | `ComposeRenderTests.RegressionPathsList_CoversEveryStackInputAndTheJobsOwnFiles`; breaks 8 and 9 | Met for the sources the spec names (L3) |

- The builder's results show platform tests 439 of 439, all nine `scripts/*.test.sh`, shellcheck, actionlint 1.7.12,
  floor-guard, the secret scan, and 11 of 11 breaks failing as expected. The reviewer read these and did not re-run the
  suite.
- The reviewer ran `scripts/regression-paths.sh <c>~1 <c>` over the last five `main` commits. Four exit 0, naming the
  matching path. The docs-only commit `8d365a2` exits 1, which is correct.
- The API-specific Pass 1 items (Location headers, timestamps, ProblemDetails, ownership, stored events) do not apply.
  The diff has no production C# code.

## Pass 2: simplicity

The path script is 37 plain lines: one loop, one matcher, early exits, and grep's exit-code convention. The `decide`
step is one `case`. The platform fact reads top to bottom. No speculative options and no new abstraction. No findings.

## Standards check

| Mechanism | Source | Verdict |
|---|---|---|
| Three-dot `git diff --name-only --no-renames -z` | <https://git-scm.com/docs/git-diff> | Documented |
| Step outputs through `GITHUB_OUTPUT`, step-level `if`, values through `env:` | GitHub Docs, workflow syntax; security hardening | Documented |
| `failure()` and `always()` | GitHub Docs, status check functions | Documented |
| Compose's rendered model (`docker compose config --format json`, `COMPOSE_PROFILES`) | Docker Docs | Documented |
| Repository path script instead of `on.paths` or a marketplace action | ad-012, *Options rejected*: `on.paths` leaves a required check pending | Custom, with the reason stated |
| JSON escape ends a path in `check-infra-paths.sh` | RFC 8259 section 7 | Fine. The two breaks pin both sides (escaped dot kept, `\n` ends the path) |

## Security checklist (below High-stakes)

- Trigger is `pull_request`, never `pull_request_target`. Permissions are inherited (`contents: read`), and the job sets
  none of its own (asserted). `persist-credentials: false`. No secrets.
- The payload SHAs reach the script through `env:`. The `run:` script has no `${{ }}` (asserted).
- Actions are pinned by SHA, with the same pins as the existing `e2e` job. No new action, package or dependency. The yq
  the builder used locally is not part of the change; CI uses the yq preinstalled on the runner, as `ci-gates.test.sh`
  already does.
- The diagnostics print logs and `error-context.md` from synthetic data only, as `e2e` already does.
- No Terraform, Dockerfile or Compose file changed, so `scripts/security-local.sh` does not apply. Auth, session,
  linking, data and header items do not apply.
- No security findings.

## Findings

### M1 (medium): the trigger list misses a script the regression suite runs

- `scripts/regression-paths.list` (whole file). The regression project runs four specs that execute
  `scripts/operations-token.sh` from the repository root:
  - `e2e/tests/operations-read.spec.ts:41`
  - `e2e/tests/operations-actions.spec.ts:200`
  - `e2e/tests/sla-sweep.spec.ts:142`
  - `e2e/tests/scalar-reference.spec.ts:75`
- A pull request that changes only that script does not match any line, so the suite is skipped. It can break those
  four specs and still merge green. The break then shows up on whichever pull request next triggers the suite.
- D1 covers "every input of the stack ... plus the job's own files". A script the suite executes falls under the job's
  own files, in the same way `e2e/` does. So this is a missed case inside the decision, not a new decision.
- Criterion 7's fact cannot catch it, because the script is not a Compose input. The spec's table missed it and the
  pre-build review did not flag it.
- Fix:
  - Add `scripts/operations-token.sh` to the list and to the spec's table (`[story]`, for the product owner).
  - Make it a check rather than a memory: in `regression-paths.test.sh`, fail when a `scripts/<name>.sh` path named
    under `e2e/` is not a line of the list.
  - `regression-paths.test.sh` already loops over every list line, so the new line gets its own match case
    automatically.

### L1 (low): diagnostics show only the last group's stack

- `ci.yml`, the `Stack diagnostics` step. The builder disclosed this.
- `run-regression.sh` removes each group's stack (`down -v`) before starting the next. Each `playwright test` run also
  clears `test-results`.
- So when group 1 fails and group 7 passes, the step prints group 7's logs and no `error-context.md`. Only the failing
  test's own output survives, in the run step's log.
- Fix: in the same `e2e/` batch as the `--wait-timeout` low, have `run_group` print `docker compose logs --tail 200`
  when its group fails, before the next group's `down -v`. Also give each project its own `--output` directory.

### L2 (low): the wall-time comment goes stale after the first run

- `ci.yml:405`: "about 25 to 35 minutes, an estimate until this job's first run measures it".
- Once criterion 4's run is done, that sentence is no longer true. Criterion 5 asks for no stale statements.
- Fix: after the first run, replace it with the measured time, or point the comment at this record.

### L3 (low): the drift fact checks only three kinds of Compose input

- `infra/tests/DisputeDesk.PlatformTests/ComposeRenderTests.cs`, `StackSources`.
- The fact checks `build.context`, path-valued `additional_contexts` and bind sources, exactly as criterion 7 says.
  Compose has other host-file inputs: a `build.dockerfile` outside its context, `env_file`, `configs`/`secrets` `file`,
  `include`, `extends`. Its profiles are also hard-coded to `build,console`.
- None of these exist today, so nothing is missed now. A future one would be missed without any warning.
- Fix: also take every string in the rendered model that starts with the repository root, and render with every
  profile. Whether `COMPOSE_PROFILES='*'` works on the pinned Compose is unverified; check it first.

### L4 (low): payload SHAs can be unreachable for a stacked pull request

- `ci.yml`, step `decide`.
- If a story branch used as a pull request's base is force-pushed (`--force-with-lease`, AGENTS.md *Commits*), the
  payload's `base.sha` may no longer be under any fetched ref. The script then exits 2 and the job stays red until the
  next push. It fails safe and the error is visible.
- The checked-out merge commit's parents (`HEAD^1`, `HEAD^2`) are always present and are exactly what the merge ref
  tests. The spec fixed the payload SHAs, so this is a note for the spec, not a defect in the build.
- Unverified: whether GitHub refreshes `base.sha` in the payload when the base branch moves.

## Criterion 4 (to fill in after CI)

- This pull request's `e2e-regression` run: run ID, result, each group's line, wall time: _pending_.
- Scratch docs-only pull request against `feature/ci-e2e-regression`: skip notice, pass, closed and branch deleted:
  _pending_.
- Main's run after the merge: `push` notice, pass: _pending_.

## Fix round 1 delta (2026-11-06)

- Reviewed: the review copy at diff hash `c8e47da868f4` (`scripts/diff-hash.sh` on the copy gives that value), only the
  change since `562c5a2a11fe`: the two new list lines, the every-script check in `scripts/regression-paths.test.sh`
  (lines 34 to 40, plus header line 3), break 12 in `ci-e2e-regression-build.json`, and the supervisor's two rows in the
  spec's trigger table (`docs/backlog/story-ci-e2e-regression.md:74-75`).
- Hash: the builder's results cover `9af9dbd135e4`. The builder's `conflicts` say the guard blocked its spec edit and
  that the supervisor's two table rows would move the hash with no change to code or tests. That matches what the
  reviewer sees: no test reads those table rows. `regression-paths.test.sh` names the spec only in a comment, and
  `check-ready.sh` reads front matter and review tags, not the table. The reviewer could not recompute the hash without
  the two rows, because the guard blocks a helper script outside `docs/verification/`. So this is consistent, not
  proved.
- Results read, not re-run: `TestResults/summary.txt` (platform tests 439 of 439, nine `scripts/*.test.sh`, the Linux
  run of `regression-paths.test.sh`, shellcheck, actionlint, floor-guard, secret scan), and `run-breaks.txt` (12 of 12,
  break 12 "failed as expected").
- Verdict: **approve**. M1 is closed. One new low (L5).

### M1: closed

- `scripts/regression-paths.list:13` adds `scripts/operations-token.sh`. The existing loop over every list line
  (`regression-paths.test.sh:67-72`) now runs an exit-0 case for it.
- The check (`regression-paths.test.sh:36-40`) runs `git grep --untracked -hoE 'scripts/[A-Za-z0-9._-]+\.sh' -- e2e`
  and fails when a name it finds is not an exact line of the list. The reviewer ran the same search on the review copy
  and found exactly two names. `scripts/operations-token.sh` is executed at the four sites M1 lists, and named in
  comments at `operations-read.spec.ts:5`, `operations-actions.spec.ts:6` and `scalar-reference.spec.ts:7`.
  `scripts/test-lock.sh` is named only in comments (`playwright.config.ts:3`, `run-regression.sh:3`).
- The check fails for the right reasons:
  - Break 12 removes the `operations-token.sh` line, and the test fails. The builder also reports the test failing
    before the list changed.
  - When the search finds nothing, `pipefail` passes git grep's status 1 through to `|| fail`. A broken search fails
    the test instead of passing on an empty list.
  - `--untracked` keeps git's ignore rules, so `node_modules` and test output are not searched. A new, uncommitted spec
    is still searched.
- Transitive inputs: `scripts/operations-token.sh` reads only `docker-compose.yml`, which is already a line (it names
  `infra/tests/smoke.sh` in a comment only). The other repository files the specs read, `backend/fixtures/local.json`
  and the e2e overlays, sit under `backend/` and `e2e/`. No input is left out.
- Known limit, disclosed by the builder: a path built at run time, such as `path.join('scripts', name)`, or a script in
  a subdirectory of `scripts/` is not found. Neither exists today (`scripts/` has no subdirectories). Not a finding.
- The spec row for `operations-token.sh` (line 74) gives the right reason, the job's own files, as M1's fix asked.

### Judgement: keeping `scripts/test-lock.sh` in the list

Keep it. This is not a finding.

- CI runs `e2e/run-regression.sh` directly (`ci.yml:468`), not through the lock script. A pull request that changes only
  `scripts/test-lock.sh` therefore starts one suite run (about 30 minutes) it does not need. The builder's conflict
  note says this goes beyond D1's wording.
- The extra path only ever adds runs. It cannot skip a run that is needed. So it errs on the safe side of the decision
  the human made, and it does not reverse it. The cost is rare: the lock script changes seldom.
- The alternatives are worse:
  - Rewording the two `e2e/` comments removes the documented way QA runs the suite, which AGENTS.md *Dispatch rules*
    requires (behind `scripts/test-lock.sh`).
  - Teaching the check to skip comments means parsing comments in both `.ts` and `.sh` files. That is more code, and
    weaker: a script named in a comment today becomes an executed one tomorrow without any warning.
  - An exception list inside the test is a second list to keep in step with the first.
  - Keeping one plain rule ("every script named under `e2e/` is a line") is the simplest choice that is correct.
- The supervisor may want to mention the extra trigger to the human in the after-merge report, because it widens D1.
  It is a superset, so the reviewer does not treat it as a `[decision]`.

### L5 (low): the `test-lock.sh` row names the wrong files

- `docs/backlog/story-ci-e2e-regression.md:75` says the script is "named in the regression specs' comments".
- It is not named in any spec. It is named in `e2e/playwright.config.ts:3` and `e2e/run-regression.sh:3`.
- Fix (product owner, lows batch): "named in comments in `e2e/playwright.config.ts` and `e2e/run-regression.sh`; the
  job does not run it; listed so the every-script check in `regression-paths.test.sh` holds".

### Pass 2 and standards, delta only

- The check is five plain lines: one search, one loop, one exact-line match, and a failure message that names the
  script and what goes wrong. No new abstraction.
- `git grep` with `--untracked`, `-o` and `-h` is documented: <https://git-scm.com/docs/git-grep>.
- `grep -Fxq` matches a whole line, fixed string (POSIX `grep`).
- Security: the delta adds no workflow step, permission, secret, action or dependency.

### Status of the round-1 findings

| Finding | Status |
|---|---|
| M1 | Closed (list line 13, `regression-paths.test.sh:34-40`, break 12) |
| L1 to L4 | Unchanged, lows; to `docs/verification/lows.md` at merge |
| L5 | New, low; to `docs/verification/lows.md` at merge |

## Capture (supervisor, 2026-11-06)

### Escapes

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
|---|---|---|---|---|
| The spec named a made-up example path, `infra/images/otel-collector/x`, which the infra path check refuses | builder | product owner | spec gap | `check-infra-paths.sh` runs in floor-guard, and the spec writer runs it on the spec before handing it over |
| `check-infra-paths.sh` read a JSON `\n` escape as part of a path | builder | the script's own tests (it had none) | missing test | `scripts/check-infra-paths.test.sh`, in floor-guard |
| R-M1: `scripts/operations-token.sh` was missing from the trigger list | reviewer | pre-build review | spec gap | the every-script check in `scripts/regression-paths.test.sh` |
| The fix builder could not write the spec's trigger table, although its brief allowed it | builder (the guard refused) | supervisor at dispatch | script error | briefs never grant a write path that the guard's ownership table does not hold |

### Metrics

| Fix rounds | Full test runs | Breaks | Wall time | Tokens and peak context | Disk |
|---|---|---|---|---|---|
| 1 (R-M1) | 3 builder runs (the first build, the path-check fix, fix round 1) | 12, all fail as expected | builder about 4 h over three runs; reviewer about 6 + 3 min; verifier about 9 + 3 min | not derived | not derived |

### Criterion 4 (CI runs)

To fill in after CI: this pull request's run (the suite runs), main's run after the squash merge (the push path writes run=false with a notice), and the first docs-only pull request after it (the suite skips). The measured wall time also goes here.
