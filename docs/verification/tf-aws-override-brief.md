# The AWS override moves to infra/aws/compose.yml (Small tier)

First slice of decision record `docs/decisions/ad-011-team-folders-in-one-repo.md` (accepted 2026-11-03), change 1. The root `docker-compose.yml` stays the one stack definition; the AWS override becomes DevOps-owned and used only by the AWS deploy. No behaviour changes: the merged AWS render must be identical before and after.

## Criteria

1. **Move.** `git mv docker-compose.aws.yml infra/aws/compose.yml`. Its relative paths stay as they are, because Compose resolves every file's relative paths from the first file's folder, the repository root (Docker, [Merge Compose files](https://docs.docker.com/compose/how-tos/multiple-compose-files/merge/)). The header's usage line names the new command.
2. **Same render.** `docker compose -f docker-compose.yml -f infra/aws/compose.yml --env-file infra/aws/compose.test.env config` gives byte-identical output to the old pair's render on `main` (record both hashes in the report).
3. **Deploy command.** `infra/aws/deploy.sh` runs `docker compose -f docker-compose.yml -f infra/aws/compose.yml up -d --wait`. `deploy.test.sh` and `rds-init.test.sh` assert the new command, and `deploy.test.sh`'s forbidden-pattern list covers the new file name.
4. **Every reader.** `check-compose.sh`, `gateway.test.sh`, `input-bounds.sh`, `compose.test.env`, `infra/otel-collector/config.yaml`, `infra/nginx/Dockerfile`, `infra/nginx/templates-aws/nginx.conf.template`, `infra/README.md`, `infra/terraform/README.md`, `backend/src/DisputeDesk.Api/Bff/KeyRingOptions.cs` (comment) and `PlatformScriptTests` (the worker health-check case) name the new path.
5. **No old path left.** `git grep -n 'docker-compose.aws'` finds nothing in your write paths. The supervisor updates `docs/`, the root `README.md` and `.claude/` in the same commit.
6. **Checks pass.** `infra/aws/check-compose.sh`, `deploy.test.sh`, `gateway.test.sh`, `rds-init.test.sh`, `shellcheck` on every changed script, and the backend `PlatformScriptTests`; the full backend suite once.

## Out of scope

Any other ad-011 change (fixtures, the SPA build, the platform tests' new home, the runbook), any change to the override's content, CI workflow changes (no workflow names the file).
