---
id: 5
type: story
title: "Terraform dev environment and OIDC deploy"
parent: epic-7-release
covers: [R38, R32, R36]
after: [4]
hitl: true
risk: high
---

# Terraform dev environment and OIDC deploy

## Description

Adds the Terraform modules and `envs/dev` (AD-28) and the manual `deploy.yml`; a person creates the AWS free-plan account, runs `terraform apply` and destroy from the maintainer's machine.

## Requirements

Verify: `terraform fmt -check` and `terraform validate` pass; the criteria below hold on reading the code.

1. **Modules and one environment** (R38; NFR-INFRA-3)
   **Given** the Terraform code
   **When** it is validated
   **Then** `infra/terraform/README.md` documents apply, deploy, rollback by SHA and destroy, and the modules define one EC2 c7i-flex.large with IMDSv2 and SSM, RDS PostgreSQL db.t4g.micro private, two CloudFront distributions and the security group admitting only CloudFront

2. **No long-lived keys** (R38; NFR-INFRA-4)
   **Given** the deploy workflow
   **When** it is read
   **Then** it assumes the GitHub OIDC role and runs the deploy script through SSM Run Command, with no AWS key in the repo or secrets

3. **State and cost** (R38; NFR-INFRA-4)
   **Given** the backend and budget module
   **When** they are read
   **Then** state is in S3 with `use_lockfile = true` and a budget alarm excludes credits

4. **Secrets from SSM** (R32; NFR-SEC-9)
   **Given** the deploy script
   **When** it writes `.env`
   **Then** values come from SSM SecureStrings

5. **Clean teardown by design** (R38; NFR-INFRA-5)
   **Given** the Terraform code
   **When** it is read
   **Then** the state bucket lives outside `envs/dev`, RDS sets `skip_final_snapshot = true` and no resource has deletion protection, so a destroy removes everything but the state bucket (the destroy itself is a supervisor check)

6. **Telemetry destination** (R36; NFR-OBS-4)
   **Given** the observability module
   **When** it is read
   **Then** it enables Transaction Search and creates the log group with 7-day retention

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Human step: create the AWS account, `terraform apply`, run `deploy.yml`, sign in over CloudFront, see traces in CloudWatch.
- Human step: `terraform destroy` removes everything except the state bucket (NFR-INFRA-5, manual check).

## Build facts

- Owner: backend (backend-builder).
- Flags: none.
- Lane: B.
- Touches: infra/terraform/modules/ (network, compute, database, edge, deploy-role, observability, budget); `infra/terraform/envs/dev/`; `.github/workflows/deploy.yml`; .github/workflows/ci.yml (terraform fmt and validate); `infra/aws/deploy.sh`.

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md

## Notes

- Unknown: the AWS free-plan account does not exist yet; this story cannot start its human steps until it does.
- Risk high: cost and exposure; beyond the criteria, the budget alarm is confirmed in the AWS console after apply.
