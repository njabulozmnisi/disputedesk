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
- Terraform variables of `envs/dev`, each with a `validation` block: `aws_region` (one of the regions the README names; default none); `github_repository` (`<owner>/<name>`, `^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$`); `budget_limit_usd` (a whole number from 1 to 100); `budget_alert_email` (one address, a simple `^[^@\s]+@[^@\s]+$` check; personal data, so it is passed as `TF_VAR_budget_alert_email`, `.gitignore` gains `*.tfvars` with `!*.tfvars.example`, and tests use `example.com`); `ssm_path` (default `/disputedesk/dev`, starts with `/`, no trailing `/`); `dataprotection_ssm_path` (default `/disputedesk-dataprotection/dev`, starts with `/`, no trailing `/`, and not under `ssm_path`, so the deploy never reads the key ring).
- `deploy.yml` input `sha`: the full 40-character lower-case commit SHA (`^[0-9a-f]{40}$`), reaching scripts only through `env:` and checked before any AWS step; the workflow derives the image tag as story 7.3 defines it (`sha-<short>`) and resolves each of the five images to its digest; a SHA with no published image fails the run before the SSM command.
- SSM parameters under `ssm_path` that a person creates as SecureStrings before the first deploy (the README lists each with its bound, matching story 7.4's Inputs): the database passwords `APP_DB_PASSWORD` and `ZITADEL_DB_PASSWORD`, `QA_DB_PASSWORD` (32 to 128 characters of `[A-Za-z0-9]`; `infra/db/10-roles-and-databases.sh` reads it for `qa_readonly`), `ZITADEL_MASTERKEY`, `ZITADEL_ADMIN_PASSWORD`, `LOGIN_SESSION_COOKIE_SECRET`, `LINKING_OTP_HMAC_KEY`, `MAILPIT_UI_AUTH`, each generated and created with `--key-id` naming the customer-managed key. Terraform writes the rest as outputs to SSM: `APP_HOST`, `AUTH_HOST`, `MAILPIT_HOST`, `DB_HOST`, `OTEL_LOG_GROUP`, `DATAPROTECTION_SSM_PATH`, `DATAPROTECTION_KMS_KEY_ID`, the generated `GATEWAY_ORIGIN_VERIFY` and the RDS master password.
- `deploy.sh`'s allow-list: exactly story 7.4's Inputs plus `QA_DB_PASSWORD` for the one-time init; any other parameter under `ssm_path` is ignored, and the RDS master password is read separately, only for that init.

1. **Pinned Terraform, one environment, state in S3** (R38; NFR-INFRA-3, NFR-INFRA-4, AD-28)
   **Given** `infra/terraform/`
   **When** the CI `terraform` job runs fmt, validate and test
   **Then** they pass with Terraform pinned by `required_version` and the CI setup action, and the `aws` and `random` providers pinned exactly with `.terraform.lock.hcl` committed (Terraform, *Dependency lock file*); the modules are `network`, `compute`, `database`, `edge`, `deploy-role`, `observability` and `budget`, used by the one root `envs/dev`
   **And** `envs/dev` uses the S3 backend with `use_lockfile = true` (Terraform, *S3 backend*, native locking); the state bucket is created by `infra/terraform/bootstrap` (local state, applied once) with public access blocked, versioning on, default encryption, `BucketOwnerEnforced` object ownership and a bucket policy denying requests where `aws:SecureTransport` is `false` (Amazon S3, *Security best practices*), and lives outside `envs/dev`, so destroying `envs/dev` leaves it (NFR-INFRA-5)
   **And** the RDS master password comes from an ephemeral `random_password` through the write-only arguments `aws_db_instance.password_wo` and `aws_ssm_parameter.value_wo` (Terraform 1.11 or later, *Write-only arguments*), so state holds only the origin-verify value, which CloudFront's origin header needs in plan; the builder confirms both arguments in the pinned provider's documentation, and if either is missing, the `# shortcut:` of the Notes stands and the report names the missing one

2. **Compute and network expose only the CloudFront origin port** (R38; NFR-INFRA-3, AD-28)
   **Given** `terraform test` plans with `mock_provider "aws"` and `override_data` for the CloudFront origin-facing managed prefix list and the AMI (Terraform, *Tests: mocks*)
   **When** the assertions run
   **Then** one `aws_instance` is `c7i-flex.large` on AL2023 with `http_tokens = "required"` and `http_put_response_hop_limit = 2`, no `key_name`, an instance profile with `AmazonSSMManagedInstanceCore`, and an Elastic IP; its security group's only ingress is TCP 80 from the CloudFront origin-facing prefix list (no 22, no `0.0.0.0/0` ingress), and its only egress is TCP 443 to anywhere (SSM, GHCR, `ip-ranges.json`, CloudFront) and TCP 5432 to the database's security group; it sits in a public subnet with an internet gateway route, so it reaches SSM, GHCR and AWS's `ip-ranges.json`; the plan has no NAT gateway and no load balancer

3. **The database is private and tears down cleanly** (R38; NFR-INFRA-3, NFR-INFRA-5, AD-28)
   **Given** the same plans
   **When** the database module's assertions run
   **Then** RDS PostgreSQL is `db.t4g.micro`, single-AZ, `publicly_accessible = false`, `storage_encrypted = true`, in private subnets, with a security group admitting 5432 only from the instance's security group; a parameter group with `rds.force_ssl = 1` (Amazon RDS, *Using SSL/TLS with PostgreSQL*); `backup_retention_period = 1`, `skip_final_snapshot = true`, `deletion_protection = false`; no resource in `envs/dev` sets deletion protection or `prevent_destroy`

4. **Three CloudFront distributions forward what the gateway needs** (R38, R32; NFR-INFRA-3, AD-23, AD-28)
   **Given** the same plans
   **When** the edge module's assertions run
   **Then** there are three distributions, app, auth and mail (Mailpit's own origin, decided 2026-10-30), each with IPv6 disabled, viewer HTTPS (redirect), the managed `CachingDisabled` cache policy and the managed `AllViewerAndCloudFrontHeaders-2022-06` origin request policy (Host, cookies, query strings, `CloudFront-Viewer-Address`, `CloudFront-Forwarded-Proto`), an HTTP-only origin on port 80 at the instance's public DNS name, marked `# shortcut: CloudFront to origin is plain HTTP for the free-plan demo with synthetic data; an HTTPS or VPC origin, a domain, an ACM certificate (TLS 1.2 minimum viewer) and WAF before real data` (decided 2026-10-30), and the custom origin header `X-Origin-Verify` set from a `random_password` (CloudFront, *Managed origin request policies*; *Adding custom headers to origin requests*)
   **And** their three domain names, the origin-verify value, the RDS endpoint and the KMS key's ARN are written to SSM under `ssm_path` (`aws_ssm_parameter`, SecureString for secrets), which the deploy script reads

5. **The deploy role trusts only this repository's main branch** (R38; NFR-INFRA-4, AD-28)
   **Given** the deploy-role module and `.github/workflows/deploy.yml`
   **When** the plan's assertions run on the role's trust and permission policies (`jsondecode` of the planned JSON) and `actionlint` runs on the workflow
   **Then** the trust policy allows `sts:AssumeRoleWithWebIdentity` from the GitHub OIDC provider only with `StringEquals` on `token.actions.githubusercontent.com:aud` = `sts.amazonaws.com` and `:sub` = `repo:<github_repository>:ref:refs/heads/main` (GitHub, *Configuring OpenID Connect in AWS*), and no `StringLike`; the provider has `client_id_list = ["sts.amazonaws.com"]` and the role `max_session_duration = 3600`; no IAM user or access key exists anywhere
   **And** Terraform creates one `aws_ssm_document` (Command, schema 2.2) whose one step checks out `{{sha}}` and runs `infra/aws/deploy.sh`, with String parameters carrying `allowedPattern` `^[0-9a-f]{40}$` for the SHA and `^ghcr\.io/[a-z0-9-]+/disputedesk-[a-z-]+@sha256:[0-9a-f]{64}$` for each image (AWS Systems Manager, *Document syntax*, `allowedPattern`); the permission policy allows `ssm:SendCommand` only on the instance's ARN and that document, never `AWS-RunShellScript`, plus `ssm:GetCommandInvocation` and `ssm:ListCommandInvocations`
   **And** `deploy.yml` is `workflow_dispatch` only, runs only when `github.ref` is `refs/heads/main`, declares no `environment:` (one would change `sub`), has `concurrency: { group: deploy-dev, cancel-in-progress: false }`, `id-token: write`, `contents: read` and `packages: read`, uses `aws-actions/configure-aws-credentials` pinned by SHA with the role ARN from a repository variable and a `role-session-name` holding the run ID, passes `sha` and the digests to scripts only through `env:` (no `${{` inside any `run:`; GitHub, *Security hardening for GitHub Actions*), validates `sha`, resolves the five digests and checks each image's `org.opencontainers.image.revision` label equals `sha` (story 7.3), sends the deploy command through the document, then waits with `aws ssm wait command-executed` and fails the job unless the invocation's status is `Success`, printing its standard error (AWS Systems Manager, *Monitoring command status*); rollback is the same workflow with an earlier `sha`

6. **The instance's deploy script fails closed** (R32, R38; NFR-SEC-9, AD-26, AD-28)
   **Given** `infra/aws/deploy.sh <sha> <five digests>`, run by SSM as root on the instance
   **When** `infra/aws/deploy.test.sh` runs it with `aws`, `docker`, `git` and `psql` replaced by stubs on `PATH`
   **Then** it reads the parameters under `ssm_path` with `aws ssm get-parameters-by-path --with-decryption --recursive` and keeps only the allow-list (Inputs): a stub returning extra parameters leaves them out of `.env`; it exits non-zero naming the first missing one, or the first value outside its story 7.4 bound (a value with `;` or `$` among the tests), before any `docker` call; and it writes `.env` with mode 0600 to a temporary file renamed over any existing `.env`, so a failed read never leaves a half-written or stale file
   **And** it checks out the repository at `<sha>`, writes the RDS CA bundle and story 7.4's `set_real_ip_from` file (from `ip-ranges.json`, service `CLOUDFRONT_ORIGIN_FACING`), failing closed when the file cannot be fetched or yields no IPv4 range; runs `infra/db/10-roles-and-databases.sh` against RDS with `sslmode=verify-full` only when the `disputedesk` role does not exist yet, with the master password in `PGPASSWORD` (never in argv or `.env`); inserts, once, an `iptables -I DOCKER-USER` rule that drops traffic to 169.254.169.254 from the Compose subnet except from the pinned `api` and `otel-collector` addresses (Amazon ECS Developer Guide, blocking container access to instance metadata); then runs `docker compose -f docker-compose.yml -f docker-compose.aws.yml up -d --wait` and exits with Compose's exit code
   **And** the test proves each failure (missing parameter, value out of bounds, missing `QA_DB_PASSWORD`, empty range list, init failure, Compose failure) exits non-zero, the range failure before Compose runs; it asserts the `DOCKER-USER` rule and its two exceptions in the stubbed `iptables` calls; it greps standard output and standard error of every path for every stubbed secret; the script has no `set -x` and never prints `.env`, `docker compose config` or `docker compose logs`

7. **Telemetry destination, instance permissions and the budget** (R36, R38; NFR-OBS-4, NFR-INFRA-4, AD-29)
   **Given** the same plans
   **When** the observability, compute and budget assertions run
   **Then** the log group `OTEL_LOG_GROUP` has 7-day retention, X-Ray Transaction Search is enabled through the pinned provider's resource for it (or, if the pinned provider has none, the README's apply procedure gives the one documented step, AWS X-Ray, *Transaction Search*), and the instance role has `CloudWatchAgentServerPolicy` plus `ssm:GetParametersByPath` and `ssm:PutParameter` on the key-ring path, `ssm:GetParametersByPath` on `ssm_path`, and `kms:Decrypt` and `kms:Encrypt` on the one customer-managed key that encrypts them, which has `enable_key_rotation = true`; `kms:Decrypt` is conditioned on `kms:ViaService` = `ssm.<region>.amazonaws.com` and `kms:Encrypt` on `kms:EncryptionContext:PARAMETER_ARN` matching the key-ring path (AWS KMS, *How AWS Systems Manager Parameter Store uses AWS KMS*; AWS IAM, *Security best practices*, least privilege)
   **And** one monthly `COST` budget of `budget_limit_usd` with `include_credit = false`, an `ACTUAL` alert at 80 % and a `FORECASTED` alert at 100 %, both emailing `budget_alert_email` (AWS Budgets; credits are included by default, NFR-INFRA-4)

8. **The apply, deploy, check and destroy procedure is written** (R38; NFR-INFRA-3, NFR-INFRA-5)
   **Given** `infra/terraform/README.md`
   **When** it is read beside the code
   **Then** it gives, in order, with exact commands: creating the free-plan account, with MFA on the root user and no root access keys, and an administrator sign-in for the maintainer's machine through IAM Identity Center with MFA and `aws sso login`, never `aws iam create-access-key` or static keys in `aws configure` (AWS IAM, *Security best practices*, temporary credentials); `bootstrap` apply; creating each SecureString with its bound; `envs/dev` init, plan and apply from the maintainer's machine, never CI (AD-28); confirming the budget alert email; setting the repository variable and running `deploy.yml`; the post-deploy checks (sign in over the app distribution, raise a dispute, see its trace in X-Ray Transaction Search, its metrics in CloudWatch and its logs in the log group, Mailpit 401 without credentials on the mail distribution and 404 on `/mailpit/` on the app distribution, a direct request to the Elastic IP refused, RDS not reachable from outside, the init's `CREATE DATABASE … OWNER disputedesk` working under RDS's `rds_superuser` master, a metadata request from the Mailpit container failing while the Api gets credentials, Redpanda within its limit, the CloudFront prefix list fitting the security group's rule quota); that the Zitadel console is reached only through SSM Session Manager port forwarding; rollback by SHA; `terraform destroy` and the check that only the state bucket remains
   **And** it states at the top that the procedure has not been run, because the account does not exist (decided 2026-10-30); and it says plainly: the fixture logins with passwords published in `fixtures/local.json` are live on the AWS demo, which holds only synthetic data; CloudFront reaches the origin over plain HTTP and the default certificate allows TLS 1.0, with no WAF; `AmazonSSMManagedInstanceCore` and `CloudWatchAgentServerPolicy` are broader than least privilege, accepted because the account is dedicated; the realip file can drift from the managed prefix list until the next deploy

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
- In a throwaway copy, put the `AWS-RunShellScript` ARN back into the `ssm:SendCommand` statement: criterion 5's test fails.
- In a throwaway copy, remove the `DOCKER-USER` rule from the deploy script: criterion 6's test fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: none (no AWS resource is created; the account does not exist).
- Lane: B.
- Mechanisms and sources: Terraform tests and mocks (<https://developer.hashicorp.com/terraform/language/tests>, <https://developer.hashicorp.com/terraform/language/tests/mocking>); S3 backend with `use_lockfile` (<https://developer.hashicorp.com/terraform/language/backend/s3>); dependency lock file (<https://developer.hashicorp.com/terraform/language/files/dependency-lock>); sensitive data in state (<https://developer.hashicorp.com/terraform/language/state/sensitive-data>); GitHub OIDC with AWS (<https://docs.github.com/actions/security-for-github-actions/security-hardening-your-deployments/configuring-openid-connect-in-amazon-web-services>); `aws-actions/configure-aws-credentials`; SSM Run Command status (<https://docs.aws.amazon.com/systems-manager/latest/userguide/monitor-commands.html>) and the `aws ssm wait command-executed` waiter; CloudFront managed policies and origin custom headers (<https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/using-managed-origin-request-policies.html>, <https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/add-origin-custom-headers.html>); CloudFront origin-facing prefix list (<https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/LocationsOfEdgeServers.html>); AWS Budgets cost types; X-Ray Transaction Search (<https://docs.aws.amazon.com/AmazonCloudWatch/latest/monitoring/CloudWatch-Transaction-Search.html>); IAM least privilege (<https://docs.aws.amazon.com/IAM/latest/UserGuide/best-practices.html>); SSM document `allowedPattern` (<https://docs.aws.amazon.com/systems-manager/latest/userguide/documents-syntax-data-elements-parameters.html>); blocking container access to instance metadata with `DOCKER-USER` (<https://docs.aws.amazon.com/AmazonECS/latest/developerguide/security-iam-roles.html>); Parameter Store and KMS (<https://docs.aws.amazon.com/kms/latest/developerguide/services-parameter-store.html>); Terraform write-only arguments (<https://developer.hashicorp.com/terraform/language/resources/ephemeral/write-only>); RDS `rds.force_ssl` (<https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/PostgreSQL.Concepts.General.SSL.html>); S3 security best practices (<https://docs.aws.amazon.com/AmazonS3/latest/userguide/security-best-practices.html>); GitHub Actions script injection and `concurrency` (<https://docs.github.com/en/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions>, <https://docs.github.com/en/actions/writing-workflows/workflow-syntax-for-github-actions#concurrency>); OWASP ASVS 5.0 V2.2, V8, V13.3, V16.2 (<https://github.com/OWASP/ASVS>).
- Packages: none in the apps; the Terraform providers `hashicorp/aws` and `hashicorp/random` and the CI actions are verified (publisher, repository, latest stable) and pinned exactly, actions by commit SHA.
- Touches:
  - `infra/terraform/bootstrap/` (new: the state bucket)
  - `infra/terraform/modules/` (new: network, compute, database, edge, deploy-role, observability, budget)
  - `infra/terraform/envs/dev/` (new: the root, `.terraform.lock.hcl`) and `infra/terraform/envs/dev/tests/` (new: `*.tftest.hcl` with `mock_provider`)
  - `infra/terraform/README.md` (new: criterion 8's procedure)
  - `infra/aws/deploy.sh` and `infra/aws/deploy.test.sh` (new)
  - `.github/workflows/deploy.yml` (new)
  - `.github/workflows/ci.yml` (new `terraform` job: fmt, validate, test; `deploy.test.sh` in `platform`)
  - `.gitignore` (`*.tfvars`, `!*.tfvars.example`)

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-23, AD-26, AD-28, AD-29, AD-32, Deferred, Open questions
- prd — docs/prd/prd.md, NFR-SEC-9, NFR-OBS-4, NFR-INFRA-3, NFR-INFRA-4, NFR-INFRA-5
- story — docs/tickets/epic-7-release/tickets/story-7.4-aws-compose-override-and-opentelemetry-collector.md (the inputs this story supplies)
- verification — docs/verification/7.5-challenge.md, docs/verification/7.5-prebuild-security.md, docs/verification/7.4-prebuild-security.md (S7.4-6, S7.4-8, S7.4-9, S7.4-11)

## Notes

- Risk high: cost and exposure once someone applies it; beyond the criteria, the README's post-deploy checks include confirming the budget alert email arrives.
- Decision: 2026-10-27 — scope: this story and 7.4 run last; if the account or time is missing, the story falls back to a documented Terraform plan (initiative scope decision).
- Decision: 2026-10-27 — the fallback is `terraform validate` plus `terraform test` with `mock_provider`, so the modules are still exercised without an account. Now the story's closing check (criteria 1 to 7), per the 2026-10-30 decision below.
- Decision: 2026-10-27 — the instance role grants least privilege for the Data Protection key ring from story 7.4: `ssm:GetParametersByPath`, `ssm:PutParameter` on the key-ring path and `kms:Decrypt`/`kms:Encrypt` on its key only (AWS IAM best practices). Criterion 7.
- Decision: 2026-10-30 (human, 7.5 challenge) — 7.5 closes on `terraform fmt`, `validate` and a plan against mocked providers, with the apply and destroy steps written and a production-path note; epic 7's "Done when" becomes "deploys and destroys cleanly once the AWS account exists". No AWS command is ever run; the account does not exist. The `Unknown` gate is removed and `hitl` is false: no person does part of this story's work; the human steps are the written procedure (criterion 8).
- Decision: 2026-10-30 (human, 7.3 challenge) — story 7.3 publishes the images to GHCR from CI on main; this story only consumes them (AD-32 amended).
- Decision: 2026-10-30 (Sofia, refine; narrowed the same day by the pre-build security decision below) — secrets in Terraform state: exactly two, the generated origin-verify value and the RDS master password, both also written to SSM; the state bucket is private, versioned and encrypted (Terraform, *Sensitive data in state*). Every other secret is created by a person as a SecureString and never passes through Terraform. Marked in code with a `# shortcut:` comment; the upgrade is write-only arguments and ephemeral resources, in the production path.
- Assumption: the five GHCR packages are public, so the instance pulls them without a credential; story 7.3 publishes from a public repository, and if GitHub does not make the packages public on first push, the README's procedure has a person set each one public once.
- Assumption: the budget defaults the README suggests are USD 10 a month; the person applying sets `budget_limit_usd` and `budget_alert_email`.
- Input for 7.6 (production path, 2026-10-30): the AWS dev environment is written and checked against mocked providers only and has never been applied; before any real use, run the README's procedure and its post-deploy checks, and move Terraform secrets to write-only arguments or ephemeral resources so state holds none.
- Decision: 2026-10-30 (human, pre-build security S7.4-8) — only the Api and the collector reach the instance metadata service: 7.4's override pins their addresses, and `deploy.sh` adds the `DOCKER-USER` rule dropping traffic to 169.254.169.254 from every other container (the Amazon ECS Developer Guide's method), with a test. AD-28's Compute line is amended. Criterion 6.
- Decision: 2026-10-30 (human, pre-build security S7.4-9) — CloudFront to origin stays plain HTTP for the free-plan demo with synthetic data, marked `# shortcut:` in Terraform, with a production-path entry (story 7.6). AD-28's Edge line is amended. Criterion 4.
- Decision: 2026-10-30 (human, pre-build security S7.4-1) — Mailpit has its own origin, so this story adds the third (mail) distribution and the `MAILPIT_HOST` output. AD-28's Images and Edge lines are amended. Criterion 4.
- Decision: 2026-10-30 (human, pre-build security S7.4-11 and S7.5-11) — accepted and documented: the fixture logins are live on the AWS demo (the README says so, criterion 8); the default CloudFront certificate's TLS 1.0 minimum and the absence of WAF go to the production path (story 7.6).
- Decision: 2026-10-30 (Sofia, pre-build security S7.5-10) — the RDS master password moves to write-only arguments with an ephemeral `random_password` where the pinned provider supports them, so state holds only the origin-verify value; otherwise the refine decision's `# shortcut:` stands for it.
- Decision: 2026-10-30 (Sofia, pre-build security S7.5-3) — the key ring lives under its own root `dataprotection_ssm_path`, outside `ssm_path`, so `deploy.sh` never reads it, and `deploy.sh` keeps only an allow-list of names.
- Input for 7.6 (production path, 2026-10-30, pre-build security S7.5-7, S7.5-9): pin the OIDC subject to the workflow with a customised subject claim (`job_workflow_ref`); replace `AmazonSSMManagedInstanceCore` and `CloudWatchAgentServerPolicy` with AWS's documented minimal instance policy.
