# tf-aws-override review (Small tier, ad-011 change 1)

- Builder: built; render of the merged AWS stack byte-identical before and after (sha256 4bd25c9d…); check-compose, deploy, gateway and rds-init tests OK; full backend suite 1623 of 1624, the one Api.IntegrationTests failure unnamed (output truncated) and its project re-run 1170 of 1170; the only backend change is one comment. CI is the authoritative full run.
- Reviewer: approve, no findings. Independent render in two scratch copies identical; the instance holds a full checkout, so `deploy.sh` finds `infra/deploy/aws/compose.yml`; the SSM document and user data name no Compose file.
- Brief correction (builder): `PlatformScriptTests`' worker health-check case reads only the root `docker-compose.yml`; ad-011 change 4 and the platform-tests brief corrected.
- Left as is: `.claude/hooks/guard.py` still lists `docker-compose.aws.yml` for the backend builder; `infra/` already covers the new path, so the entry is dead but harmless (removing it is a guard change). Dated records in `docs/tickets/` and `docs/verification/` keep the old name.

## Escapes

| Finding | Caught by | Should have been | Category | Countermeasure |
|---|---|---|---|---|
| Brief named a test reading the AWS override that reads only the root file | builder | supervisor writing the brief | spec gap | grep the test before naming it in a brief |
| Suite failure unnamed because output was piped through `tail` | builder | builder | environment or tooling | build-story pitfall candidate: write the suite's results file (`--logger trx`) instead of tailing output |

## Metrics

| Fix rounds | Full-suite runs | Wall time | Disk change |
|---|---|---|---|
| 0 | 1 (builder) | builder 12 min, reviewer 3 min | worktree removed after merge |
