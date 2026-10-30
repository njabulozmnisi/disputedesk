---
id: 5
type: story
title: "Terraform dev environment and OIDC deploy"
parent: epic-7-release
covers: [R38, R32, R36]
after: [4]
hitl: false
risk: high
refined: false
---

# Terraform dev environment and OIDC deploy

## Description

Adds the Terraform for the one AWS dev environment (AD-28: a bootstrap root for the state bucket, the named modules and one root `envs/dev`), the manual `deploy.yml` that assumes the GitHub OIDC role and waits for SSM Run Command's result, and the instance's deploy script that writes `.env` from SSM SecureStrings and starts story 7.4's stack by image digest. The AWS account does not exist, so the story closes on `terraform fmt -check`, `terraform validate` and `terraform test` plans against mocked providers, plus the written apply, deploy, rollback, post-deploy check and destroy procedure; no AWS command is ever run (decided 2026-10-30). The images it deploys are the ones story 7.3 publishes to GHCR; this story only consumes them (AD-32).

## Requirements

Verify: `terraform fmt -check -recursive infra/terraform`, `terraform init -backend=false` plus `terraform validate` in `infra/terraform/bootstrap` and `infra/terraform/envs/dev`, and `terraform test` in `infra/terraform/envs/dev` (every run `command = plan` against `mock_provider "aws"`) pass locally and in a new CI `terraform` job; `actionlint` passes; `infra/aws/deploy.test.sh` passes; no step reaches an AWS endpoint or needs credentials.

Inputs:
- Terraform variables of `envs/dev`, each with a `validation` block: `aws_region` (one of the regions the README names; default none); `github_repository` (`<owner>/<name>`, `^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$`); `budget_limit_usd` (a whole number from 1 to 100); `budget_alert_email` (one address, a simple `^[^@\s]+@[^@\s]+$` check); `ssm_path` (default `/disputedesk/dev`, starts with `/`, no trailing `/`).
- `deploy.yml` input `sha`: the full 40-character lower-case commit SHA (`^[0-9a-f]{40}$`), checked before any AWS step; the workflow derives the image tag as story 7.3 defines it (`sha-<short>`) and resolves each of the five images to its digest; a SHA with no published image fails the run before the SSM command.
- SSM parameters under `ssm_path` that a person creates as SecureStrings before the first deploy (the README lists each with its bound, matching story 7.4's Inputs): the database passwords, `ZITADEL_MASTERKEY`, `ZITADEL_ADMIN_PASSWORD`, `LOGIN_SESSION_COOKIE_SECRET`, `LINKING_OTP_HMAC_KEY`, `MAILPIT_UI_AUTH`. Terraform writes the rest as outputs to SSM: `APP_HOST`, `AUTH_HOST`, `DB_HOST`, `OTEL_LOG_GROUP`, `DATAPROTECTION_SSM_PATH`, the generated `GATEWAY_ORIGIN_VERIFY` and the RDS master password.

1. **Pinned Terraform, one environment, state in S3** (R38; NFR-INFRA-3, NFR-INFRA-4, AD-28)
   **Given** `infra/terraform/`
   **When** the CI `terraform` job runs fmt, validate and test
   **Then** they pass with Terraform pinned by `required_version` and the CI setup action, and the `aws` and `random` providers pinned exactly with `.terraform.lock.hcl` committed (Terraform, *Dependency lock file*); the modules are `network`, `compute`, `database`, `edge`, `deploy-role`, `observability` and `budget`, used by the one root `envs/dev`
   **And** `envs/dev` uses the S3 backend with `use_lockfile = true` (Terraform, *S3 backend*, native locking); the state bucket is created by `infra/terraform/bootstrap` (local state, applied once) with public access blocked, versioning on and default encryption, and lives outside `envs/dev`, so destroying `envs/dev` leaves it (NFR-INFRA-5)

2. **Compute and network expose only the CloudFront origin port** (R38; NFR-INFRA-3, AD-28)
   **Given** `terraform test` plans with `mock_provider "aws"` and `override_data` for the CloudFront origin-facing managed prefix list and the AMI (Terraform, *Tests: mocks*)
   **When** the assertions run
   **Then** one `aws_instance` is `c7i-flex.large` on AL2023 with `http_tokens = "required"` and `http_put_response_hop_limit = 2`, no `key_name`, an instance profile with `AmazonSSMManagedInstanceCore`, and an Elastic IP; its security group's only ingress is TCP 80 from the CloudFront origin-facing prefix list (no 22, no `0.0.0.0/0` ingress); it sits in a public subnet with an internet gateway route, so it reaches SSM, GHCR and AWS's `ip-ranges.json`; the plan has no NAT gateway and no load balancer

3. **The database is private and tears down cleanly** (R38; NFR-INFRA-3, NFR-INFRA-5, AD-28)
   **Given** the same plans
   **When** the database module's assertions run
   **Then** RDS PostgreSQL is `db.t4g.micro`, single-AZ, `publicly_accessible = false`, `storage_encrypted = true`, in private subnets, with a security group admitting 5432 only from the instance's security group; `backup_retention_period = 1`, `skip_final_snapshot = true`, `deletion_protection = false`; no resource in `envs/dev` sets deletion protection or `prevent_destroy`

4. **Two CloudFront distributions forward what the gateway needs** (R38, R32; NFR-INFRA-3, AD-23, AD-28)
   **Given** the same plans
   **When** the edge module's assertions run
   **Then** there are two distributions, app and auth, each with IPv6 disabled, viewer HTTPS (redirect), the managed `CachingDisabled` cache policy and the managed `AllViewerAndCloudFrontHeaders-2022-06` origin request policy (Host, cookies, query strings, `CloudFront-Viewer-Address`, `CloudFront-Forwarded-Proto`), an HTTP-only origin on port 80 at the instance's public DNS name, and the custom origin header `X-Origin-Verify` set from a `random_password` (CloudFront, *Managed origin request policies*; *Adding custom headers to origin requests*)
   **And** their domain names, the origin-verify value and the RDS endpoint are written to SSM under `ssm_path` (`aws_ssm_parameter`, SecureString for secrets), which the deploy script reads

5. **The deploy role trusts only this repository's main branch** (R38; NFR-INFRA-4, AD-28)
   **Given** the deploy-role module and `.github/workflows/deploy.yml`
   **When** the plan's assertions run on the role's trust and permission policies (`jsondecode` of the planned JSON) and `actionlint` runs on the workflow
   **Then** the trust policy allows `sts:AssumeRoleWithWebIdentity` from the GitHub OIDC provider only with `StringEquals` on `token.actions.githubusercontent.com:aud` = `sts.amazonaws.com` and `:sub` = `repo:<github_repository>:ref:refs/heads/main` (GitHub, *Configuring OpenID Connect in AWS*); the permission policy allows only `ssm:SendCommand` on the instance's ARN and the `AWS-RunShellScript` document, and `ssm:GetCommandInvocation` and `ssm:ListCommandInvocations`; no IAM user or access key exists anywhere
   **And** `deploy.yml` is `workflow_dispatch` only, runs only when `github.ref` is `refs/heads/main`, has `id-token: write`, `contents: read` and `packages: read`, uses `aws-actions/configure-aws-credentials` pinned by SHA with the role ARN from a repository variable, validates `sha`, resolves the five digests, sends the deploy command, then waits with `aws ssm wait command-executed` and fails the job unless the invocation's status is `Success`, printing its standard error (AWS Systems Manager, *Monitoring command status*); rollback is the same workflow with an earlier `sha`

6. **The instance's deploy script fails closed** (R32, R38; NFR-SEC-9, AD-26, AD-28)
   **Given** `infra/aws/deploy.sh <sha> <five digests>`, run by SSM as root on the instance
   **When** `infra/aws/deploy.test.sh` runs it with `aws`, `docker`, `git` and `psql` replaced by stubs on `PATH`
   **Then** it reads every parameter under `ssm_path` with `aws ssm get-parameters-by-path --with-decryption --recursive`, exits non-zero naming the first missing one before any `docker` call, and writes `.env` with mode 0600 to a temporary file renamed over any existing `.env`, so a failed read never leaves a half-written or stale file
   **And** it checks out the repository at `<sha>`, writes the RDS CA bundle and story 7.4's `set_real_ip_from` file (from `ip-ranges.json`, service `CLOUDFRONT_ORIGIN_FACING`), runs `infra/db/10-roles-and-databases.sh` against RDS with `sslmode=verify-full` only when the `disputedesk` role does not exist yet, then `docker compose -f docker-compose.yml -f docker-compose.aws.yml up -d --wait`, and exits with Compose's exit code; the test proves each failure (missing parameter, init failure, Compose failure) exits non-zero and that no secret value is written to standard output

7. **Telemetry destination, instance permissions and the budget** (R36, R38; NFR-OBS-4, NFR-INFRA-4, AD-29)
   **Given** the same plans
   **When** the observability, compute and budget assertions run
   **Then** the log group `OTEL_LOG_GROUP` has 7-day retention, X-Ray Transaction Search is enabled through the pinned provider's resource for it (or, if the pinned provider has none, the README's apply procedure gives the one documented step, AWS X-Ray, *Transaction Search*), and the instance role has `CloudWatchAgentServerPolicy` plus `ssm:GetParametersByPath` and `ssm:PutParameter` on the key-ring path, `ssm:GetParametersByPath` on `ssm_path`, and `kms:Decrypt` and `kms:Encrypt` on the one key that encrypts them (AWS IAM, *Security best practices*, least privilege)
   **And** one monthly `COST` budget of `budget_limit_usd` with `include_credit = false`, an `ACTUAL` alert at 80 % and a `FORECASTED` alert at 100 %, both emailing `budget_alert_email` (AWS Budgets; credits are included by default, NFR-INFRA-4)

8. **The apply, deploy, check and destroy procedure is written** (R38; NFR-INFRA-3, NFR-INFRA-5)
   **Given** `infra/terraform/README.md`
   **When** it is read beside the code
   **Then** it gives, in order, with exact commands: creating the free-plan account and an administrator sign-in for the maintainer's machine; `bootstrap` apply; creating each SecureString with its bound; `envs/dev` init, plan and apply from the maintainer's machine, never CI (AD-28); confirming the budget alert email; setting the repository variable and running `deploy.yml`; the post-deploy checks (sign in over the app distribution, raise a dispute, see its trace in X-Ray Transaction Search, its metrics in CloudWatch and its logs in the log group, Mailpit 401 without credentials, a direct request to the Elastic IP refused, RDS not reachable from outside, Redpanda within its limit, the CloudFront prefix list fitting the security group's rule quota); rollback by SHA; `terraform destroy` and the check that only the state bucket remains
   **And** it states at the top that the procedure has not been run, because the account does not exist (decided 2026-10-30)

## Boundaries

- Never run an AWS command, `terraform apply`, `terraform plan` against a real provider, or `deploy.yml`; tests use mock providers and stubs only.
- No CI-side Terraform plan or apply (AD-28); CI runs fmt, validate and the mocked tests only.
- No ALB, NAT gateway, ECS, ECR, deployment controller or second environment.
- This story does not publish images or change the `publish` job; it reads the images story 7.3 publishes (AD-32).
- Must not change: the local stack, story 7.4's override and templates (it supplies their inputs).

## Supervisor checks

Deliberate-break proofs, run by the supervisor, not the verifier.

- In a throwaway copy, change the trust policy's `:sub` condition to `StringLike` with `repo:<repo>:*`: criterion 5's test fails.
- In a throwaway copy, add an ingress rule for port 22: criterion 2's test fails.
- In a throwaway copy, set `publicly_accessible = true`: criterion 3's test fails.
- In a throwaway copy, make the deploy script write `.env` in place: criterion 6's test fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: none (no AWS resource is created; the account does not exist).
- Lane: B.
- Mechanisms and sources: Terraform tests and mocks (<https://developer.hashicorp.com/terraform/language/tests>, <https://developer.hashicorp.com/terraform/language/tests/mocking>); S3 backend with `use_lockfile` (<https://developer.hashicorp.com/terraform/language/backend/s3>); dependency lock file (<https://developer.hashicorp.com/terraform/language/files/dependency-lock>); sensitive data in state (<https://developer.hashicorp.com/terraform/language/state/sensitive-data>); GitHub OIDC with AWS (<https://docs.github.com/actions/security-for-github-actions/security-hardening-your-deployments/configuring-openid-connect-in-amazon-web-services>); `aws-actions/configure-aws-credentials`; SSM Run Command status (<https://docs.aws.amazon.com/systems-manager/latest/userguide/monitor-commands.html>) and the `aws ssm wait command-executed` waiter; CloudFront managed policies and origin custom headers (<https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/using-managed-origin-request-policies.html>, <https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/add-origin-custom-headers.html>); CloudFront origin-facing prefix list (<https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/LocationsOfEdgeServers.html>); AWS Budgets cost types; X-Ray Transaction Search (<https://docs.aws.amazon.com/AmazonCloudWatch/latest/monitoring/CloudWatch-Transaction-Search.html>); IAM least privilege (<https://docs.aws.amazon.com/IAM/latest/UserGuide/best-practices.html>).
- Packages: none in the apps; the Terraform providers `hashicorp/aws` and `hashicorp/random` and the CI actions are verified (publisher, repository, latest stable) and pinned exactly, actions by commit SHA.
- Touches:
  - `infra/terraform/bootstrap/` (new: the state bucket)
  - `infra/terraform/modules/` (new: network, compute, database, edge, deploy-role, observability, budget)
  - `infra/terraform/envs/dev/` (new: the root, `.terraform.lock.hcl`) and `infra/terraform/envs/dev/tests/` (new: `*.tftest.hcl` with `mock_provider`)
  - `infra/terraform/README.md` (new: criterion 8's procedure)
  - `infra/aws/deploy.sh` and `infra/aws/deploy.test.sh` (new)
  - `.github/workflows/deploy.yml` (new)
  - `.github/workflows/ci.yml` (new `terraform` job: fmt, validate, test; `deploy.test.sh` in `platform`)

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-23, AD-26, AD-28, AD-29, AD-32, Deferred, Open questions
- prd — docs/prd/prd.md, NFR-SEC-9, NFR-OBS-4, NFR-INFRA-3, NFR-INFRA-4, NFR-INFRA-5
- story — docs/tickets/epic-7-release/tickets/story-7.4-aws-compose-override-and-opentelemetry-collector.md (the inputs this story supplies)
- verification — docs/verification/7.5-challenge.md

## Notes

- Risk high: cost and exposure once someone applies it; beyond the criteria, the README's post-deploy checks include confirming the budget alert email arrives.
- Decision: 2026-10-27 — scope: this story and 7.4 run last; if the account or time is missing, the story falls back to a documented Terraform plan (initiative scope decision).
- Decision: 2026-10-27 — the fallback is `terraform validate` plus `terraform test` with `mock_provider`, so the modules are still exercised without an account. Now the story's closing check (criteria 1 to 7), per the 2026-10-30 decision below.
- Decision: 2026-10-27 — the instance role grants least privilege for the Data Protection key ring from story 7.4: `ssm:GetParametersByPath`, `ssm:PutParameter` on the key-ring path and `kms:Decrypt`/`kms:Encrypt` on its key only (AWS IAM best practices). Criterion 7.
- Decision: 2026-10-30 (human, 7.5 challenge) — 7.5 closes on `terraform fmt`, `validate` and a plan against mocked providers, with the apply and destroy steps written and a production-path note; epic 7's "Done when" becomes "deploys and destroys cleanly once the AWS account exists". No AWS command is ever run; the account does not exist. The `Unknown` gate is removed and `hitl` is false: no person does part of this story's work; the human steps are the written procedure (criterion 8).
- Decision: 2026-10-30 (human, 7.3 challenge) — story 7.3 publishes the images to GHCR from CI on main; this story only consumes them (AD-32 amended).
- Decision: 2026-10-30 (Sofia, refine) — secrets in Terraform state: exactly two, the generated origin-verify value and the RDS master password, both also written to SSM; the state bucket is private, versioned and encrypted (Terraform, *Sensitive data in state*). Every other secret is created by a person as a SecureString and never passes through Terraform. Marked in code with a `# shortcut:` comment; the upgrade is write-only arguments and ephemeral resources, in the production path.
- Assumption: the five GHCR packages are public, so the instance pulls them without a credential; story 7.3 publishes from a public repository, and if GitHub does not make the packages public on first push, the README's procedure has a person set each one public once.
- Assumption: the budget defaults the README suggests are USD 10 a month; the person applying sets `budget_limit_usd` and `budget_alert_email`.
- Input for 7.6 (production path, 2026-10-30): the AWS dev environment is written and checked against mocked providers only and has never been applied; before any real use, run the README's procedure and its post-deploy checks, and move Terraform secrets to write-only arguments or ephemeral resources so state holds none.
