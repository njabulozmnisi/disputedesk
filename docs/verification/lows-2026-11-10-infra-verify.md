# Verification: lows-2026-11-10-infra

- Commit: `f6193f5b574953f4212a8bbea9d6b4668430f886` (uncommitted diff hash `1f905bd59eeb`)
- Verifier: gpt-6-astra via GitHub Copilot CLI
- Date (UTC): 2026-11-10T17:29:22Z

VERDICT: FAIL

| Criterion | Test | Proved (yes/no) |
|---|---|---|
| Row 1: document Vitest through shell-command breaks | `scripts/run-breaks.test.sh`; no saved execution result supplied | no |
| Row 2: reject missing specs; use main-checkout records; append reruns | `scripts/verify-with-copilot.test.sh`; no saved execution result supplied | no |
| Row 3: collector timeout and Docker exit checks | `ComposeRenderTests.CollectorConfig_ExportsOnlyTheAppsMetricsEachWithItsOwnAttributesAsItsOneDimensionSet` | yes |
| Row 6: Redpanda shutdown grace | `ComposeRenderTests.Redpanda_HasTheStopGracePeriodOfRedpandasHelmChart` | yes |
| Row 7 L1: prevent reintroducing `createrole_self_grant` | `DeployScriptTests.Source_NeverSetsCreateroleSelfGrant` | yes |
| Row 8: include foreign users administering DisputeDesk resources | `PlatformScriptTests.ProvisionSection_HumanAdministratorsInScope_AreDeactivated_AndAFreshReadingFindsNoneActive` | yes |
| Row 10: separate Mailpit Send API credentials | `GatewayTests.AwsMailpitOrigin_SendApi_RefusesTheUiLoginAndTakesItsOwn` | yes |
| Row 11: reject missing organization | `PlatformScriptTests.ProvisionScript_ItsOwnUserWithoutAnOrganization_FailsTheRun` | yes |
| Row 12: reject empty user response | `PlatformScriptTests.ProvisionSection_EmptyUserAnswer_FailsTheRun` | yes |
| Row 19 L3: cover every Compose input | `ComposeRenderTests.RegressionPathsList_CoversEveryStackInputAndTheJobsOwnFiles`; incomplete coverage | no |
| Row 19 L4: compare merge-commit parents | `scripts/regression-paths.test.sh`; no saved execution result supplied | no |

Scope follows the build report's code criteria; documentation-only and explicitly deferred rows are excluded.

## Failures and evidence

- **Medium (model's ranking) - Compose dependency coverage remains incomplete.** `infra/tests/DisputeDesk.PlatformTests/ComposeRenderTests.cs:684-699` discovers only absolute repository paths retained in rendered JSON. It explicitly excludes Dockerfiles outside their build contexts, the original low finding's case. `ComposeModel.cs` also renders without preserving `env_file` references. The passing test covers today's stack, not these omitted dependency forms.
- **Evidence gap - shell-test results are not preserved.** The manifest lists only `TestResults/platform-tests.trx` (480 passing platform tests) and no shell-test results.
- **Blocked checks.** The model's execution guard refused ShellCheck for `infra/deploy/aws/deploy.sh` and `infra/deploy/aws/input-bounds.sh`, and the diff-hash check.

Backend and platform builds passed with zero warnings/errors. Both projects passed style and whitespace verification. Generated OpenAPI matched the committed contract exactly. The other five changed shell scripts passed ShellCheck; floor-guard passed. No suites were rerun.

## Standards

None identified.

{"status":"blocked","assumptions":["Acceptance scope is the build report's code criteria."],"open_questions":[],"conflicts":["Required ShellCheck and diff-hash checks were denied by execution permissions."]}

## Verifier's run (2026-11-10)

- HEAD `f6193f5b574953f4212a8bbea9d6b4668430f886`; `scripts/diff-hash.sh <worktree> f6193f5` prints `1f905bd59eeb`, equal to `test_results.diff_hash`. Results: `TestResults/platform-tests.trx`, 480 total, 480 passed, 0 failed (read, not re-run).
- The model's "blocked" and its ShellCheck and diff-hash conflicts came from its own execution permissions, not from a script failure; the hash check above was run by the verifier and matches.
- `scripts/run-breaks.sh`: 11 of 11 breaks passed without the break and failed with it (breaks 1-3 verify-with-copilot.test.sh, 4 regression-paths.test.sh, 5-11 PlatformTests, including break 8 with the Gateway image build). Exit 0. This also shows the shell tests pass on the unbroken diff.
- `scripts/floor-guard.sh`: clean.

### Findings (verifier's ranking)

- low, row 19 L3 (the model ranked it medium): `StackSources` now reads every root-prefixed string of the model with all profiles and asserts the spa context. A `dockerfile` outside its context, which renders relative, is still not read; the builder says so in the report and the code comment. The current stack is covered and the row was itself a low, so it stays low and open for that residual form.
- low: rows 1, 2 and 19 L4 have shell tests only; the build report lists no results file for them. Their breaks ran green here.
- Non-standard mechanisms: none.
- Needs AWS or the running stack (QA's or the owner's): rows 4, 9 and 10 on AWS. The owner must create `/disputedesk/dev/MAILPIT_SEND_API_AUTH` before the next deploy.
