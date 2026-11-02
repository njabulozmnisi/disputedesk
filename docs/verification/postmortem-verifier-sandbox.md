# Postmortem: the verifier's Copilot sandbox cannot run test scripts

2026-11-02. Trigger (AGENTS.md, Continuous improvement): a stage failing for tooling, three times in one batch.

## What happened

- 7.15: the Copilot verdict was FAIL because its sandbox denied running the extracted `oidc-token` step against crafted inputs; no defect.
- 7.16 (first verify and the fix-round re-run): the verdict was BLOCKED because the sandbox denied `shellcheck` and `infra/terraform/iam-names.test.sh`; no defect.
- 7.17 ran because the verifier agent itself ran the test under its own tools; the Copilot model also could not.

Each time the verifier agent and the supervisor ran the missing checks directly and recorded them, so no defect was missed, but the independent model's verdict covered only what it could read.

## Cause

`scripts/verify-with-copilot.sh` allows `dotnet`, `npm`, `npx`, `terraform`, `actionlint`, `trivy`, `gitleaks`, `scripts/floor-guard.sh` and read-only shell tools. Stories in epic 7 increasingly prove themselves with shell test scripts (`infra/aws/*.test.sh`, `infra/terraform/*.test.sh`) and `shellcheck`, none of which is on the list.

## Proposed countermeasure (a deterministic check, needs human approval: it changes what the verifier may run)

Add to the allow list: `'shell(shellcheck:*)'`, `'shell(infra/aws/check-compose.sh)'`, `'shell(infra/aws/deploy.test.sh)'`, `'shell(infra/aws/gateway.test.sh)'`, `'shell(infra/aws/rds-init.test.sh)'` and `'shell(infra/terraform/iam-names.test.sh)'`: named paths, not a `bash` wildcard, so the deny list still holds. The Docker-based tests (`gateway.test.sh`, `rds-init.test.sh`) start throwaway containers; the script already holds the testcontainers lock, and these use their own networks, so they do not collide with a Compose stack, but they would still need the `stack` lock if a stack is running (unverified whether the verifier's sandbox has Docker access).
