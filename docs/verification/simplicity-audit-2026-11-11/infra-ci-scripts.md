# Simplicity and standards audit, 2026-11-11: infra-ci-scripts

Read-only audit for the lows-2026-11-10 retrospective (AGENTS.md, Continuous improvement), on the lows worktree at
`88b28c3` (base `f6193f5`). Same rules and scale as the 2026-11-03 baseline (`~/disputedesk-rename/retro-8/audit-rerun-plan.md`):
AGENTS.md "How to work", `docs/conventions/`, `review-story` pass 2 and its standards check. High = home-grown where a
framework or standard way exists, or code a new engineer cannot follow. Medium = an abstraction, wrapper, option or
indirection that does not earn its place. Low = local cleverness, naming, dead code.

Files read: `infra/images/` (gateway, zitadel, otel-collector, postgres), `infra/deploy/` (aws, runbook),
`infra/tests/smoke.sh`, `infra/terraform/`, `infra/README.md`, `docker-compose.yml`, `.github/workflows/`,
`.github/actions/`, `.github/trivyignore.yaml`, `scripts/`. `infra/tests/DisputeDesk.PlatformTests/` is in the tests
set and is cited here only where a finding depends on it. The known lows (`docs/verification/lows.md`, `epic-8-lows.md`)
are not counted again.

## Findings

| ID | Severity | Tag | File:line | Finding | Simplest fix |
|---|---|---|---|---|---|
| INFR-1 | high | [code] | `scripts/*.test.sh` (10 files, 1,588 lines); helpers copied verbatim at `ci-gates.test.sh:17-26`, `main-protection.test.sh:19-28`, `deploy-workflow.test.sh:20-29`, `check-ready.test.sh:10-19`, `regression-paths.test.sh:18`, `run-breaks.test.sh:10`, `verify-with-copilot.test.sh:11`, `check-infra-paths.test.sh:10`; the rule's scope at `infra/tests/DisputeDesk.PlatformTests/ShellHarnessTests.cs:11` | **New. The baseline's INF-1 pattern is back, this time in `scripts/`.** Ten shell test harnesses, and eight of them define the same `fail` and `expect` helpers (a 10-line `expect` body copied word for word) and build their own scratch repositories and stubs. Stories 8.7 and 8.8 moved infra's harnesses into the xUnit project and added a "no shell test harness" test, but that test only scans `infra/`, so new harnesses went to `scripts/` instead. No AD or decision record picks shell harnesses for scripts. A new engineer now finds two test styles for the same kind of check, and `PlatformTests` already has the process and PATH-stub helpers (`Processes.cs`, `PathStubs.cs`). | Pick one harness, as INF-1 asked. Either move the cases into `infra/tests/DisputeDesk.PlatformTests` (which already runs scripts with stubs), or use bats-core (`run`, `$status`, `$output`, setup and teardown: <https://github.com/bats-core/bats-core>). Then widen `ShellHarnessTests` to `scripts/`. |
| INFR-2 | medium | [code] | `regression-paths.test.sh:130-158, 177, 228-243`; `deploy-workflow.test.sh:37-44`; `ci-gates.test.sh:153-158`; `main-protection.test.sh:102-110`; `.github/workflows/ci.yml:276` | **New.** The workflow tests copy a step's `run:` text out of the YAML with yq into a temporary file and run it with `bash -e` to imitate a runner. Other assertions pin the YAML's layout and wording: `steps[1].id == decide`, the timeout of 60, which step comes after `guard.test.sh` in floor-guard, the words in the e2e-regression comment, and lines of `architecture.md` and `spine-amendments.md`. Rewording a comment or reordering a step fails CI, while a behaviour change written another way can still pass. This is the baseline's TST-7 problem again, now in workflow files. | Move each step body under test (the gate check, `decide`, the image resolve and the forbidden-terms check) into a script under `scripts/`, and have the step call it, as `.github/actions/*/install.sh` already does "so shellcheck lints it". Test those scripts by behaviour. Leave job shape to actionlint and `ci-gates.sh`, and drop the comment and doc-text assertions. |
| INFR-3 | medium | [decision] | `infra/terraform/modules/compute/main.tf:12-89`, `modules/deploy-role/main.tf:9-89`, `modules/observability/main.tf:12-25` | **New, a result of D9 / story 8.16 plus its security L1.** The IAM policies are hand-written `*.json.tftpl` files that drop raw strings into JSON, so 18 module variables repeat the same `!can(regex("[\"\\\\]", ...))` "it reaches a template" validation. Terraform's `jsonencode` would do this escaping. HashiCorp's `templatefile` docs advise against hand-writing JSON in templates and recommend one `jsonencode(...)` call (<https://developer.hashicorp.com/terraform/language/functions/templatefile#generating-json-or-yaml-from-a-template>). Every value is already bounded once at the root (`envs/dev/variables.tf`, `bootstrap/main.tf`), and each module has only that one caller. | A human decides. Either (a) build each policy with `jsonencode` and have `IamNames` read the names from it, which drops all 18 validations, or (b) keep the JSON templates, trust the root's bounds (validate at the boundary), and remove the module copies with a line in `docs/production-path.md` for a second environment. |
| INFR-4 | medium | [code] | `scripts/diff-hash.sh:18-19`; compare `scripts/story-patch.sh:19-22` | **New.** The change's fingerprint is home-grown: a SHA-256 over the `git diff <base>` text plus `shasum` lines of untracked files. Git already gives a content address for a tree, and the sibling script already builds one: `story-patch.sh` reads the base into a temporary index and runs `add -A`. So two scripts define "the uncommitted change" in two ways. The open low on intent-to-add (lows.md, process-checks: marking files "changes the worktree index and so `diff-hash.sh`") exists only because this hash depends on the index. | `GIT_INDEX_FILE=<tmp> git read-tree <base> && GIT_INDEX_FILE=<tmp> git add -A && GIT_INDEX_FILE=<tmp> git write-tree`, with the same two excludes. The tree ID is the fingerprint (git-write-tree: <https://git-scm.com/docs/git-write-tree>), it does not depend on the worktree's index, and it is one mechanism shared with `story-patch.sh`. |
| INFR-5 | low | [code] | `infra/terraform/modules/deploy-role/main.tf:9-13, 15-22, 75-89, 173` | **New.** The "it reaches a template" validation on `name` is wrong: `name` only reaches resource names. `aws_region`, `clone_directory` and `ssm_path` only reach the `jsonencode`'d SSM document, which escapes them. In the `runCommand` shell line, the character that would break `AWS_REGION='...'` is a single quote, and the validation does not check for one. So these checks guard nothing they claim to, and the root's regexes already rule out every quote. | Delete the four validations. Keep only those on values that really reach a `.json.tftpl` (or settle them under INFR-3). |
| INFR-6 | low | [code] | `infra/images/zitadel/provision.sh:161, 166, 194, 230, 235, 243, 280`; `verify.sh:29, 33, 51` | **New.** Many request bodies are written as JSON in strings with escaped quotes (`"{\"filters\":[{\"projectNameFilter\":...`). Yet the same script builds its other bodies with `jq -n --arg` (`provision.sh:252-257, 339-346`), and `label-policy.sh:47` gives the reason for that way. The values are constants and Zitadel IDs, so nothing is injectable today; the cost is that a reader has to unescape each line. | Build the bodies with `jq -nc --arg ...` as the script already does elsewhere. |
| INFR-7 | low | [code] | `scripts/check-infra-paths.sh:18-26` | **New.** Each mention goes through a dense grep and sed regex, then three parameter-expansion trims: a `\n` escape ends the path, `\.` becomes `.`, and trailing `.`, `,` or `-` is cut. Every new false positive will add another trim. The check itself (every named `infra/` path exists) is a fair custom check with no standard tool, and it can be explained in one sentence. | Keep the check but match only what the repo writes: paths in backticks or quotes, plus Compose and workflow fields. Then no trimming rules are needed. Or list the trims as a table of cases in its test. |
| INFR-8 | low | [code] | `infra/images/zitadel/verify.sh:20-27` | **New.** `api`, `rpc` and `check` are one-line functions. `check` packs `if ... then printf ...; else printf ...; FAILED=1; fi; }` into two lines, against the "no dense one-liners" rule. `provision.sh:70-82` writes the same kind of helper over several lines. | Write `check` and `api` on several lines, as `provision.sh`'s `call` is. |

## Baseline findings (2026-11-03), by kind

| Baseline | Kind | Fate |
|---|---|---|
| INF-1 (high) | five infra shell harnesses with copied helpers | Closed for infra by 8.7, 8.8 and 8.16: the harnesses moved to `infra/tests/DisputeDesk.PlatformTests` (now counted in the tests set, so a move, not a drop). The same pattern grew back in `scripts/`: INFR-1. |
| INF-2 (medium) | no actionlint or shellcheck in CI | Closed (8.1): `ci.yml:283-314`. |
| INF-3 (medium) | `proxy_set_header` block repeated per location | Closed (8.9): set once per server in both templates; only `/otlp/` and Mailpit set their own. |
| INF-4 (medium) | CSS comment stripper and 11 sed mutation cases | Closed (8.1): `label-policy.sh` is a line lookup; the mutation step is gone. |
| INF-5 (low) | PNG/WOFF2 byte checks in CI | Closed (8.1): `ci.yml:145-154` keeps only the wordmark-name greps. |
| INF-6 (medium, [decision]) | seven single-caller Terraform modules | Accepted (D8); unchanged. INFR-3 is a cost of that choice. |
| INF-7 (medium, [decision]) | HCL read with regexes | Closed (8.16): policies are `*.json.tftpl` read with System.Text.Json. Its side effect is INFR-3. |
| INF-8 (medium, [decision]) | live OIDC subject jobs | Closed (8.16): the jobs are gone; the one-time check is in `infra/terraform/README.md`. |
| INF-9 (medium) | gateway headers asserted in three harnesses | Closed (8.7): `smoke.sh:79-80` hands the gateway headers to GatewayTests and keeps only the Scalar and identity-origin checks, as 8.7 decided. |
| INF-10 (low) | bounds tested twice; awk tag reader | Closed (8.8): `check-compose.sh` is gone. |
| INF-11 (low) | hand-rolled `gh run list` poll | Closed: `push-and-watch.sh` was removed (ad-012 rollout). |
| INF-12 (low) | `security-local.sh` forwarder separate from CI | Closed (8.1): `security.yml:119` runs the script, so there is one command. |
| INF-13 (low) | `[decision]` settled by grepping prose | Closed (8.1): `check-ready.sh:51-56` reads a `decisions: settled` front-matter field. |
| INF-14 (medium) | `lane.sh` remote scripts | Closed (8.1): `scripts/lane-runner.zsh` was split out. The baseline row's exact text could not be recovered; judged by the 8.1 touches list. |

Checked and not counted (each has a decision or a documented standard): the `deploy.yml` polling loop (AWS CLI waiter limit),
the composite install actions (ad-012 clause 5), `ci-gates.sh` (ad-012 clauses 1 and 3), `regression-paths.sh`
(ad-012, on.paths rejected), the `!override`/`!reset` AWS override, `input-bounds.sh`, the `deploy.sh` steps (AD-28),
the mkdir lock in `test-lock.sh` (portable, no `flock` on macOS), `refresh_logo` (#41), the collector config, and the
header include repetition (nginx `add_header` inheritance).

## Counts

| Severity | 2026-11-11 | Baseline 2026-11-03 |
|---|---|---|
| High | 1 | 1 |
| Medium | 3 | 8 |
| Low | 4 | 5 |
| Total | 8 | 14 |

All eight findings are new. Of the 14 baseline findings, 13 are closed and 1 is accepted by decision (INF-6). INF-1's infra harnesses count as moved to the tests set.

## Trend

Every baseline finding in this set is closed or accepted, and the infra code itself (gateway, provisioner, deploy,
Compose) is plainer than at the baseline. The new findings come from the flow tooling added in epics 8 and 9. The
biggest is that the shell-harness pattern INF-1 removed from `infra/` grew back in `scripts/`, because the
no-harness check covers only `infra/`. Widening that check to `scripts/` (INFR-1), and testing workflow steps as
scripts rather than as YAML text (INFR-2), would stop the same escape from coming back.
