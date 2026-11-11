# Review: lows-2026-11-10, lane 1 (infra, scripts, .github)

- Worktree: `.claude/worktrees/agent-a5148379790fc8974`, base `f6193f5`, uncommitted diff (18 tracked files, 2 new: `scripts/verify-with-copilot.test.sh` and the build record).
- Builder record: `docs/verification/lows-2026-11-10-infra-build.json` (diff hash `1f905bd59eeb`).
- Tier: lows batch below High-stakes, so this review also ran the security checklist (`.claude/skills/security-story/SKILL.md`).
- Read: the whole diff; `deploy.sh`, `input-bounds.sh`, `provision.sh` (project, operations user and lock-down sections), `deploy.yml`'s trigger, `regression-paths.sh`, `run-breaks.sh`, `GatewayFixture.cs`, `DeployScriptTests.cs` (missing-parameter tests), `infra/terraform/README.md` steps 5 and 12, the source records (`aws-admin-lockdown-security.md` one-time sweep, `rds-init-grant-review.md` L1/L2, `ci-e2e-regression-*`).
- Ran: `scripts/verify-with-copilot.test.sh`, `scripts/regression-paths.test.sh` and `scripts/run-breaks.test.sh` (all pass); check 12's new event filter, through jq, on sample bodies: empty, a setup event, a provisioner event of a type not on its list, a foreign editor, and 1000 events. Each gave the expected `[]`, the printed event, or the error with exit 5. Read the builder's `TestResults/platform-tests.trx`: 480 total, 480 passed, every row test among them (the Mailpit Send API gateway test included).
- Not run: the platform suite (the builder's run is the result of record), the breaks (the verifier's), anything on AWS.

## Rows

| Row | Outcome | Assessment |
|---|---|---|
| run-breaks Vitest (ui-tokens-icons verify) | Header documents the shell-command form, with `npm ci` | Justified. `run-breaks.sh:46` runs a non-dotnet `test_method` with `bash -c`, so `cd frontend && npm ci && npx vitest run …` works as written. A mode would be a speculative option. Close. |
| verify-with-copilot (ui-tokens-icons verify) | Refuses a missing spec (exit 2), passes the absolute path, writes to the main checkout (`--git-common-dir`'s parent), appends `## Copilot run` | Correct and tested. The new test covers each behaviour; breaks 1 to 3 each remove one. Close. |
| Collector test timeout (cloudwatch-metric-filter) | 5 min for `docker run -d`; `docker port` and `docker stop` exit codes asserted | Correct. Close. |
| list-metrics (cloudwatch-metric-filter) | `--recently-active PT3H` | Correct: it is the only value the CLI accepts. Close. |
| Collector comment (cloudwatch-metric-filter) | Cites `metric_translator.go` | Correct. Close. |
| Redpanda stop grace (deploy-recreate-on-config) | `stop_grace_period: 90s` in the base Compose file, inherited on AWS | Correct and tested (break 5). Deploys are dispatched by hand, and Redpanda normally exits well inside 90 s, so the longer grace costs nothing in practice. The builder notes that dev-container mode still bypasses fsync, which belongs in production-path (its conflict 2). Close. |
| rds-init-grant L1 | Source assertion: neither script contains `createrole_self_grant` | This is the record's own suggested fix (`rds-init-grant-review.md:58`), and break 6 shows it fails. Close. |
| rds-init-grant L2 | No change | Justified. The record says to add the line only once RDS shows it is needed. RDS has since reached `3 2`, so the half-done state is gone. Close as "not needed". |
| aws-admin-lockdown lock-down scope | `or` gains `resource.organizationId` and `resource.projectId` | Correct, and it goes beyond the row by adding the project. `PROJECT_ID` is set at `provision.sh:160`, before the section, under `set -u`. Tested (break 7). Close. |
| aws-admin-lockdown check 12 | Adds sweeps C and D; the expected output is now five lines | The filter is correct on the cases I ran. The API calls are the security record's, and the supervisor ran that sweep clean on AWS (`aws-admin-lockdown-review.md:216`). An `rpc` failure aborts under `set -euo pipefail`, so the check fails closed. One accepted loosening: see L1. Close. |
| aws-admin-lockdown Mailpit Send API | New SSM input `MAILPIT_SEND_API_AUTH` → `MP_SEND_API_AUTH` | Correct and fails closed: `deploy.sh:68-71` stops with "MAILPIT_SEND_API_AUTH is missing" before any write or docker call, and Compose's `:?` refuses to render without it. `deploy.yml` runs only on a hand dispatch, so merging breaks nothing until someone deploys. The gateway test covers both sides (UI login 401, Send login 200). The documentation of the one-time step is not enough: see M1. Close once M1 is fixed. |
| aws-admin-lockdown ORG_ID | `jq -er` | Correct and tested (break 9): the test runs the script's own line. Close. |
| aws-admin-lockdown empty user answer | `case`: only `true` or `false`, otherwise log and fail | Correct. Without the fix the test would pass with exit 0, so it can fail (break 10). Close. |
| ci-e2e-regression L2 | Measured wall time, about 27 min (26.6 to 28.4) | Close. |
| ci-e2e-regression L3 | `StackSources` walks every rendered string under the repository root, rendered with `COMPOSE_PROFILES=*` | Correct, and it now also covers env_file, configs and secrets. A dockerfile outside its context is still not read (the builder says so): see L3. |
| ci-e2e-regression L4 | `decide` passes `HEAD^1 HEAD^2` | Correct. The `pull_request` checkout is GitHub's two-parent test merge, and the job uses `fetch-depth: 0` (`ci.yml:422`). `regression-paths.sh:19` diffs with three dots, so `HEAD^1...HEAD^2` is still "the head's changes since the merge base", the same range as before, and no payload SHA can be missing. The test builds a real merge commit and checks the parent order (break 4 swaps it). Close. |

Nothing outside the rows changed: every hunk maps to a row above. The process-checks rows and the other ci-e2e-regression rows (L1, `--wait-timeout`) are not in this lane.

## Pass 1: correctness

Each changed behaviour has a test that fails without it (breaks 1 to 11), except the doc-only rows. No timestamps, `Location` headers, Results or aggregates are involved. No `!` was added to production code: the `!` in `ComposeRenderTests` is test code, and existing.

## Pass 2: simplicity and standards

- The `StackSources` switch expression (recurse into objects and arrays, keep strings under the root) replaces three explicit loops with one rule that covers more. It reads in one pass. Not a finding.
- Standards: every mechanism has a documented source.
  - Mailpit's `MP_SEND_API_AUTH` (runtime options; v1.32.0 `sendAPIAuthMiddleware`).
  - Compose `stop_grace_period`, with Redpanda's Helm default of 90 s.
  - `COMPOSE_PROFILES=*` (Docker, profiles).
  - GitHub's `pull_request` merge commit.
  - `git rev-parse --git-common-dir` (git-worktree).
  - `jq -e`.
  - CloudWatch `ListMetrics` `RecentlyActive`.
  - Zitadel `ListAdministrators` `ResourceFilter`, and Admin `ListEvents`.
- No home-grown mechanism.

## Security checklist

- Secrets: the new value is generated (`[A-Za-z0-9]`, bounded as `MAILPIT_UI_AUTH`), stored as an SSM SecureString, written only to the 0600 `.env`, and never printed by `deploy.sh`. The test value is marked test-only. No secret is in the diff (the builder's secret scan was clean).
- Fail closed: the missing SSM parameter, the `jq -er` on `ORG_ID`, the empty GetUserByID answer, check 12's 1000-event cap and `rpc` errors each stop rather than pass.
- Injection: `ORG_ID` and `PROJECT_ID` are spliced into the `ListAdministrators` JSON, but both are Zitadel-issued IDs read with `jq`, and `ORG_ID` now fails on null. This is unchanged in kind from the reviewed design.
- Authorization: the lock-down scope is wider (organization and project roles held by users of other organizations). The Send API no longer accepts the published login, and delete stays behind it, as the security record accepted (P50).
- CI: `decide` no longer reads `${{ }}` payload values at all, which removes the env indirection the prebuild review asked for. `persist-credentials: false` is unchanged.

## Findings

| Id | Severity | File:line | Finding | Fix |
|---|---|---|---|---|
| M1 | medium | `infra/terraform/README.md:191-193` | The one-time step for the live environment says "run its line once before the next deploy". Line 188 alone fails (`put_secret: command not found`): it needs the block's `SSM_PATH`, `KEY_ARN`, `letters_and_digits` and `put_secret` (lines 169-179). The obvious fallback is to run the block, but every `put_secret` uses `--overwrite`. That would replace `ZITADEL_MASTERKEY` (Zitadel can no longer decrypt its stored keys) and the database passwords (the RDS roles keep the old ones) under the running stack, and the next deploy would fail. SSM's parameter history allows a recovery, but by hand. The step must run on the live environment before the next deploy, so the documented procedure has to be exact. | State it as: run lines 169-179 (the variables and the two functions), then line 188 only, and never the other `put_secret` lines on a deployed environment, because `--overwrite` replaces the live values. Or give one self-contained command for the existing environment. |
| L1 | low | `infra/terraform/README.md:417-422` | The automated reading of sweep C accepts an event of a setup-listed type with an empty editor at any date. The security record (`aws-admin-lockdown-security.md:156`) expected those only at instance creation and named a later `user.pat.added` as a compromise. Lines 1 to 3 of check 12 still catch a new organization, administrator or machine user, so the remainder is a later empty-editor PAT, app or SMTP event, which needs a system context. | Optionally, pass setup-typed events only up to the instance's creation (for example, the date of the first `org.added`), or note the accepted limit in the step. |
| L2 | low | `infra/tests/DisputeDesk.PlatformTests/DeployScriptTests.cs:173-175` | The README promises that a deploy without the parameter stops with "MAILPIT_SEND_API_AUTH is missing". The generic loop at `deploy.sh:68-69` does that, but no test names this input. | Add `[InlineData("MAILPIT_SEND_API_AUTH")]` to `MissingParameter_FailsBeforeDockerNamingIt`. |
| L3 | low | `infra/tests/DisputeDesk.PlatformTests/ComposeRenderTests.cs:236-242` | ci-e2e-regression L3 is only partly closed. A `dockerfile` outside its build context renders relative and is still missed. The builder says so in the summary. | Keep that part open in `lows.md` (or close it as accepted), instead of marking the row fully closed. |
| L4 | low | `docs/backlog/story-ci-e2e-regression.md:51-52,105`; `.claude/skills/verify-story/SKILL.md:12`; `docs/production-path.md:221` | Text this lane cannot write is now stale. The spec still names `BASE_SHA`/`HEAD_SHA`. The verify-story skill still passes a relative spec path, which the script now refuses (exit 2, with a hint) from a worktree that lacks the spec. P50 does not say that the Send API has its own login. These are the builder's conflicts 1 and 2, plus the skill line. | The spec and P50 go in the records pull request. The skill line is a process change, so it goes to the human with the retrospective. |

## Status

```json
{"status": "changes", "record": "docs/verification/lows-2026-11-10-infra-review.md", "findings": [{"severity": "medium", "file": "infra/terraform/README.md", "line": 191, "issue": "The one-time MAILPIT_SEND_API_AUTH step says 'run its line once'; line 188 alone fails without the block's variables and functions (169-179), and running the whole block overwrites ZITADEL_MASTERKEY and the database passwords on the live environment (--overwrite).", "fix": "Say: run lines 169-179, then line 188 only, never the other put_secret lines on a deployed environment; or give one self-contained command."}, {"severity": "low", "file": "infra/terraform/README.md", "line": 417, "issue": "Check 12's sweep-C filter accepts setup-typed events with an empty editor at any date; the security record expected them only at instance creation.", "fix": "Bound setup events by the instance's creation date, or note the accepted limit."}, {"severity": "low", "file": "infra/tests/DisputeDesk.PlatformTests/DeployScriptTests.cs", "line": 173, "issue": "No test names MAILPIT_SEND_API_AUTH as a missing parameter that stops the deploy before docker, as the README promises.", "fix": "Add InlineData(\"MAILPIT_SEND_API_AUTH\") to MissingParameter_FailsBeforeDockerNamingIt."}, {"severity": "low", "file": "infra/tests/DisputeDesk.PlatformTests/ComposeRenderTests.cs", "line": 236, "issue": "ci-e2e-regression L3 only partly closed: a dockerfile outside its context is still not read.", "fix": "Keep that part open in lows.md or close it as accepted."}, {"severity": "low", "file": "docs/backlog/story-ci-e2e-regression.md", "line": 51, "issue": "Stale text outside the lane: the spec's BASE_SHA/HEAD_SHA, verify-story SKILL step 3's relative spec path (now refused from a worktree lacking the spec), and production-path P50 (Send API login).", "fix": "Spec and P50 in the records pull request; the skill line to the human with the retrospective."}], "assumptions": ["Zitadel ListEvents field names and event types are the security record's, whose sweep the supervisor ran clean on AWS; check 12's combined form was not run against AWS or a local Zitadel.", "Mailpit v1.32.0's Send API behaviour is taken from the builder's source reading and the passing gateway test, not re-read.", "GitHub's pull_request test merge always has two parents (base first); the regression-paths test models it, and it was not observed on a run."], "open_questions": [], "conflicts": []}
```

## Fix (M1), 2026-11-10

Delta reviewed: `infra/terraform/README.md:191-206` only, the new note and the command block for an environment that predates the secret.

- **Runs on its own.** The block sets `SSM_PATH` and `KEY_ARN` itself. `DATAPROTECTION_KMS_KEY_ID` is a plain `String` parameter (`infra/terraform/envs/dev/main.tf:106-110`), so `get-parameter` without decryption returns the key ARN, as the original block's line 170 does. The generator is inlined, so it needs neither `letters_and_digits` nor `put_secret`.
- **Within the bound.** The value is `send:` plus 64 characters of `[A-Za-z0-9]`, with no trailing newline. I ran the generator line locally into a scratch file: 69 bytes, last byte a letter, matching `^[A-Za-z0-9]{1,32}:[A-Za-z0-9]{32,128}$` (`infra/deploy/aws/input-bounds.sh:38-39`).
- **No secret on the command line.** The value goes only into the `mktemp` file (mode 0600) and reaches the CLI as `file://`. The file is removed afterwards, and nothing is echoed (`> /dev/null`).
- **Cannot replace a live value.** Without `--overwrite`, `put-parameter` refuses a parameter that already exists, as the comment says. The warning names the risk of rerunning the block above: its `--overwrite` would replace the live Zitadel master key and database passwords.
- **One limit, not a finding.** The block sets no `set -e`, so a failed `put-parameter` still runs `rm` and the error shows on the terminal. If the parameter was never created, the next deploy stops with "MAILPIT_SEND_API_AUTH is missing", so the step still fails closed.

M1 is closed. Lows L1 to L4 stand as recorded, for `lows.md`. Not run: the `aws` calls (they act on the deployed environment).

```json
{"status": "approve", "record": "docs/verification/lows-2026-11-10-infra-review.md", "findings": [{"severity": "low", "file": "infra/terraform/README.md", "line": 429, "issue": "Check 12's sweep-C filter accepts setup-typed events with an empty editor at any date; the security record expected them only at instance creation.", "fix": "Bound setup events by the instance's creation date, or note the accepted limit."}, {"severity": "low", "file": "infra/tests/DisputeDesk.PlatformTests/DeployScriptTests.cs", "line": 173, "issue": "No test names MAILPIT_SEND_API_AUTH as a missing parameter that stops the deploy before docker.", "fix": "Add InlineData(\"MAILPIT_SEND_API_AUTH\") to MissingParameter_FailsBeforeDockerNamingIt."}, {"severity": "low", "file": "infra/tests/DisputeDesk.PlatformTests/ComposeRenderTests.cs", "line": 236, "issue": "ci-e2e-regression L3 only partly closed: a dockerfile outside its context is still not read.", "fix": "Keep that part open in lows.md or close it as accepted."}, {"severity": "low", "file": "docs/backlog/story-ci-e2e-regression.md", "line": 51, "issue": "Stale text outside the lane: the spec's BASE_SHA/HEAD_SHA, verify-story SKILL step 3's relative spec path, and production-path P50 (Send API login).", "fix": "Spec and P50 in the records pull request; the skill line to the human with the retrospective."}], "assumptions": ["The aws commands in the new block were not run, since they act on the deployed environment; that put-parameter without --overwrite refuses an existing parameter (ParameterAlreadyExists) is AWS's documented behaviour."], "open_questions": [], "conflicts": []}
```
