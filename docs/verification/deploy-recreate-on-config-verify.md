# Verification: deploy-recreate-on-config

- Commit: `575bd39011207e356b294567c73c504ce898c5ac` (uncommitted diff hash `e47307848fe2`, equals the builder's `test_results.diff_hash`)
- Verifier: gpt-6-astra via GitHub Copilot CLI, plus the verifier's own tooling runs
- Date (UTC): 2026-11-09

Small fix, no backlog story file: the criteria are those in `deploy-recreate-on-config-build.json`.

| criterion | test | proved |
|---|---|---|
| A later deploy recreates every container so a changed bind-mounted file takes effect (`--force-recreate`) | `DeployScriptTests.LaterDeploy_RecreatesEveryContainer_SoAChangedMountedFileTakesEffect` | yes |
| `--wait` is kept | same test (exact `ComposeUpCall`), `FirstDeploy_ReadsSsmWritesTheFilesInitsSetsTheRulesThenStartsCompose`, `LaterDeploy_SkipsTheInitAndInsertsTheRulesOnlyOnce` | yes |

## Checks

- Results file: the TRX lists 442 passed, 0 failed (read by the copilot verifier; suite not re-run).
- Diff hash: `scripts/diff-hash.sh` gives `e47307848fe2`, equal to the build report.
- Breaks: `scripts/run-breaks.sh` break 1 (drop `--force-recreate` from deploy.sh) failed as expected.
- `scripts/floor-guard.sh`: clean.
- Builds, `dotnet format` style and whitespace, OpenAPI drift: passed (copilot verifier).
- ShellCheck on `infra/deploy/aws/deploy.sh`: not run. The copilot verifier's call and the verifier's own were both denied by the guard (the path contains "deploy"). The script change is one changed command plus a comment. Left for CI.
- The copilot verdict line read FAIL only because of those permission denials ("not an identified code defect"); it named no code finding.
- Needs the running stack or AWS (QA's or CI's, not failed): a real deploy recreating the containers.

## Standards

None non-standard: `docker compose up --force-recreate` is documented Compose behaviour.

## Findings

None high or medium. Low: ShellCheck on deploy.sh was not run here (guard); CI covers it.
