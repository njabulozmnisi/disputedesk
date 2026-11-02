---
id: 15
type: story
title: "Deploy role trusts GitHub's immutable OIDC subject"
parent: epic-7-release
covers: [R38, R33]
after: [5]
hitl: false
risk: high
refined: true
---

# Deploy role trusts GitHub's immutable OIDC subject

## Description

Fixes a live defect in the deploy role (story 7.5, AD-28). `modules/deploy-role` builds the trust policy's subject as `repo:<owner>/<name>:ref:refs/heads/main`. This repository was created on 2026-10-22, after GitHub's 2026-07-15 cut-over, so its OIDC tokens carry the immutable subject `repo:<owner>@<owner-id>/<name>@<repo-id>:ref:refs/heads/main`. GitHub's API confirms it: `GET repos/<owner>/<name>/actions/oidc/customization/sub` returns `use_immutable_subject: true` and a `sub_claim_prefix` with numeric IDs. The first `deploy.yml` run would therefore fail at `AssumeRoleWithWebIdentity`, and the existing mocked test asserts the wrong string.

Human decision, 2026-11-01: the role matches the new immutable subject, and the repository is not opted out. Terraform builds the subject from the repository name and two new numeric ID variables, and the mocked test asserts the exact string. No AWS command is run and nothing calls AWS (there is no AWS account).

Human decision, 2026-11-01 (pre-build review point 1, security points 1 and 2; replaces the earlier decision to request the audience `sts.amazonaws.com`): two new CI jobs on `main` check the subject. `oidc-token` requests the job's own OIDC token for the non-STS audience `disputedesk-oidc-subject-check`, checks `aud` equals it, and outputs only `sub`, so no usable deploy credential is ever minted in CI. `oidc-subject` passes that `sub` to a Terraform test of the deploy-role module fed the `github` context's IDs.

What the check proves: the subject format and the IDs Terraform builds match the tokens GitHub issues for this repository. It is not an end-to-end deploy proof: it does not run `deploy.yml`, assume the role, or check the values given to a real apply.

## Requirements

Verify: in `infra/terraform/envs/dev`, `terraform fmt -check`, `validate` and `terraform test` (mocked provider, `command = plan`) pass locally and in the CI `terraform` job. `terraform test -test-directory=tests-oidc` passes locally with example values and fails with the old subject format (Supervisor checks). `actionlint` passes on `.github/workflows/ci.yml`. `scripts/secret-scan.sh` and `scripts/security-local.sh` pass. Gate before merge: the throwaway-branch run in criterion 5 shows what it names, and the branch is deleted. No step needs AWS credentials or reaches an AWS endpoint.

Inputs:
- New Terraform variables of `envs/dev`, passed to `modules/deploy-role`. Each is required, has no default, and has a `validation` block:
  - `github_repository_owner_id`: the numeric ID of the repository's owner, a string matching `^[1-9][0-9]{0,18}$` (1 to 19 digits, no leading zero);
  - `github_repository_id`: the repository's numeric ID, with the same bound.
  They are strings, not numbers, so Terraform renders them digit for digit. They are public identifiers, not secrets.
- `github_repository`: unchanged. It is `<owner>/<name>` with the existing validation `^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$`, and its owner and name parts go into the subject unchanged, case included (`StringEquals` is case-sensitive). The image pattern stays lower case, as today.
- `oidc-subject` reads `github.repository`, `github.repository_owner_id` and `github.repository_id` from the `github` context (GitHub Docs, *Contexts reference*). These, and `oidc-token`'s `sub` output, reach the script only through `env:`, never through `${{ }}` inside `run:` (GitHub Docs, *Secure use reference*, script injection), and go to Terraform as `-var` values.
- The token request: the runner's `ACTIONS_ID_TOKEN_REQUEST_URL` with `&audience=disputedesk-oidc-subject-check` appended, and `ACTIONS_ID_TOKEN_REQUEST_TOKEN` as the bearer. The response's `value` field holds the token.
- The decoded claims: `aud` must be exactly `disputedesk-oidc-subject-check`; `sub` must be non-empty and not `null`, and goes to Terraform only as `-var expected_subject=...`, compared exactly, with no pattern.

1. **The trust policy names the immutable subject of this repository's main branch** (R38; NFR-INFRA-4)
   **Given** `modules/deploy-role` with the variables `github_repository_owner_id` and `github_repository_id`, building the subject `repo:<owner>@<owner-id>/<name>@<repo-id>:ref:refs/heads/main` in one local from `github_repository` and the two IDs. The format is GitHub's immutable subject syntax for a branch (GitHub Docs, *OpenID Connect reference*, immutable subject claims; GitHub changelog 2026-04-23). The `@` separator cannot appear in a GitHub user or repository name.
   **When** `deploy_role.tftest.hcl` plans the module with `github_repository = "example-owner/disputedesk"`, `github_repository_owner_id = "123456"` and `github_repository_id = "456789"`
   **Then** the trust policy's `Statement` equals exactly the current one, with `token.actions.githubusercontent.com:sub` = `repo:example-owner@123456/disputedesk@456789:ref:refs/heads/main` under `StringEquals`, `:aud` still `sts.amazonaws.com`, and still no `StringLike`
   **And** with `github_repository = "Example-Owner/disputedesk"` the subject keeps `Example-Owner` while the image pattern still names `example-owner` (the existing lower-case run, extended)

2. **The two IDs are refused outside their bound before anything is planned** (R38; NFR-INFRA-4)
   **Given** the two variables' `validation` blocks in `envs/dev/variables.tf` (Terraform, *Input variables*, custom validation rules)
   **When** `dev.tftest.hcl` plans with each ID set to `""`, `"0"`, `"0123"`, `"12a"`, `"-1"` and a 20-digit value, and separately with `"1"` and a 19-digit value
   **Then** each of the six bad values is an `expect_failures` on its own variable and both edge values plan. These are 2 × 8 runs in the file's existing "rejected" and "accepted" style, and the file's `variables` block gains example values for both IDs.

3. **The `oidc-token` job mints only a non-STS token and hands on only its subject** (R38, R33; NFR-INFRA-2)
   **Given** a new `oidc-token` job in `ci.yml` that:
   - runs only on a push to `main` (`if: github.event_name == 'push' && github.ref == 'refs/heads/main'`), with `timeout-minutes: 5`;
   - has job permissions `id-token: write` and nothing else, no checkout and no action (GitHub Docs, *OpenID Connect reference*, setting permissions; OWASP CICD-SEC-5);
   - has one step with `shell: bash` (so `pipefail` is on) and no `set -x`, using `curl` and `jq` only. It requests the token with `curl -sS --fail --max-time 10` (no `-v` or `--trace`), the manual request method in GitHub Docs, *OpenID Connect reference*; the `value` field and the `audience` parameter come from actions/toolkit `oidc-utils.ts`;
   - masks the token with `::add-mask::` first (GitHub Docs, *Workflow commands*, masking a value in a log), fails on an empty or `null` `value`, and keeps the token only in a shell variable: never in `GITHUB_ENV`, `GITHUB_OUTPUT`, the step summary or a file;
   - decodes only the payload, without verifying the signature (RFC 7519 §3, RFC 7515 §2), with `split(".")[1] | gsub("-";"+") | gsub("_";"/") | @base64d | fromjson`. jq's own error output is discarded, so a malformed token fails with the step's own message and no token fragment;
   - fails unless `aud` is exactly `disputedesk-oidc-subject-check` and `sub` is non-empty and not `null`, prints `sub` and `aud`, and writes only `sub` as the job output.
   **When** the job runs on `main`
   **Then** it passes, and its log shows `sub` and `aud` and no other token text (no string starting `eyJ`)
   **And** a malformed token, an empty or `null` value, a wrong `aud` or a missing `sub` each fail the step with no token text in the log (checked by the builder running the step's script locally against crafted inputs)

4. **The `oidc-subject` job compares that subject with the one Terraform builds** (R38, R33; NFR-INFRA-4)
   **Given** a new `oidc-subject` job in `ci.yml` with `needs: oidc-token`, the same `if:` and timeout, and job permissions `contents: read` only. It uses the pinned `actions/checkout` with `persist-credentials: false` and the pinned `hashicorp/setup-terraform` (Terraform 1.17.1, `terraform_wrapper: false`) the `terraform` job uses, then runs `terraform -chdir=infra/terraform/envs/dev init -backend=false -input=false -lockfile=readonly -test-directory=tests-oidc` and `terraform -chdir=infra/terraform/envs/dev test -test-directory=tests-oidc` with `-var` values `github_repository`, `github_repository_owner_id`, `github_repository_id` from the context and `expected_subject` from `oidc-token`'s output.
   `tests-oidc/subject.tftest.hcl` declares `variable "expected_subject" { type = string }` and a `mock_provider "aws"` as `deploy_role.tftest.hcl` does. It plans `../../modules/deploy-role` with fixed example values for the other module variables only, so the four `-var` values are not overridden by a file `variables` block. Its assertion compares the trust policy's `:sub` with `var.expected_subject`; Terraform's assertion output shows both values on failure.
   **When** the job runs on `main`
   **Then** it passes only if the two subjects are equal
   **And** neither job runs an `aws` command or AWS action, holds an AWS credential, or uses the OIDC customization API: the issued token is the authoritative evidence. Neither job gates `publish` (`needs` unchanged). The default `terraform test` still reads only `tests/`, so the `terraform` job is unchanged.

5. **One throwaway-branch run proves the two jobs on GitHub's runner before merge** (R38, R33; NFR-INFRA-4)
   **Given** a branch `throwaway/7.15-oidc` whose `ci.yml` copy, and only that copy, adds the branch to `on.push.branches` and relaxes both jobs' `if:` to `github.event_name == 'push'`
   **When** the builder pushes it once and reads the run
   **Then** `oidc-token` is green and its log shows `sub` = `repo:<owner>@<owner-id>/<name>@<repo-id>:ref:refs/heads/throwaway/7.15-oidc`, `aud` = `disputedesk-oidc-subject-check`, and no string starting `eyJ`; `oidc-subject` fails only at the Terraform assertion, whose output shows the built subject `...:ref:refs/heads/main` equal to the token's `sub` up to `:ref:`
   **And** the builder records the two log lines and the assertion output in its report, then deletes the branch. This proves the request, mask, decode, `aud` check, job-output handoff and Terraform wiring on GitHub's runner, and that the owner and repository part GitHub issues equals the one Terraform builds. The `:ref:refs/heads/main` part is proved by criterion 1 and the post-merge check. `publish` stays main-only, so the branch publishes nothing.

6. **The real-account procedure sets the two IDs, and P55 records the remaining exposure** (R38; NFR-INFRA-4)
   **Given** `infra/terraform/README.md`, step 4 (*The environment*) and *Destroy*, and `docs/production-path.md`, P55
   **When** an operator follows the README
   **Then** both sections set `TF_VAR_github_repository` from `gh api repos/<owner>/<name> --jq .full_name` (exact case), and `TF_VAR_github_repository_owner_id` and `TF_VAR_github_repository_id` from `--jq .owner.id` and `--jq .id`. The `modules/deploy-role` table row, or one sentence under it, says the role trusts GitHub's immutable subject for `main` and that CI's `oidc-token` and `oidc-subject` jobs check its format on every push to `main`.
   **And** P55 gains one sentence: since story 7.15, CI's `oidc-token` job also holds `id-token: write` on `main`; it requests a non-STS audience that STS refuses, but the permission allows any audience, so a change to that step could mint a token the deploy role accepts, and the `job_workflow_ref` subject closes that too. No README text, decision or comment still describes the subject as `repo:<owner>/<name>:...` (checked 2026-11-01: AD-28, P55, `deploy.yml` and the README quote no subject string).

## Boundaries

- Must not change: the OIDC provider (URL `https://token.actions.githubusercontent.com`, client ID `sts.amazonaws.com`); the trust policy's `aud` condition (`sts.amazonaws.com`); `StringEquals` (never `StringLike`); the role's 3600-second session; the permission policy; the SSM document and its image pattern; `deploy.yml` (it sets no `environment:`, which would change its subject); `github_repository`'s validation; the compute module's clone of the repository; the `terraform` job; `publish`'s `needs`.
- The repository is not opted out of immutable subjects, and the subject is not customized, for example with `job_workflow_ref` (that stays P55).
- No AWS command, AWS credential, AWS endpoint or new repository secret. No new package or action beyond the two already pinned in `ci.yml`. No token with the audience `sts.amazonaws.com` is ever requested in CI.
- No `architecture.md` edit: it quotes no subject string.

## Supervisor checks

Deliberate-break proofs, run by the supervisor in a throwaway copy, not by the verifier.

- Put the old subject format `repo:${var.github_repository}:ref:refs/heads/main` back in the module: criterion 1's test fails.
- In `envs/dev`, run `terraform test -test-directory=tests-oidc` with example IDs and `expected_subject` in the old format: it fails, showing the built subject. Run it again with the immutable format for the same values: it passes.
- Swap the owner and repository IDs in the module's subject local: criterion 1's test fails.
- Drop the leading-zero rule from the ID validation: criterion 2's `"0123"` run fails.
- Read the builder's criterion 5 record, and confirm with `gh api` that the branch no longer exists.
- Post-merge: open the first `main` run's `oidc-token` and `oidc-subject` logs; `sub` reads `repo:<owner>@<id>/<name>@<id>:ref:refs/heads/main`, `aud` reads `disputedesk-oidc-subject-check`, both jobs are green, and no string starting `eyJ` appears. A failure here opens a fix, not a revert of the module change.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes (authentication: the deploy role's trust and an OIDC token in CI).
- Lane: A.
- Mechanisms and sources:
  - immutable subject syntax: GitHub Docs, *OpenID Connect reference* (<https://docs.github.com/en/actions/reference/security/oidc>); GitHub changelog (<https://github.blog/changelog/2026-04-23-immutable-subject-claims-for-github-actions-oidc-tokens>);
  - requesting the token and `id-token: write`: the same reference, *Methods for requesting the OIDC token* and permission settings; actions/toolkit `oidc-utils.ts` (<https://github.com/actions/toolkit/blob/main/packages/core/src/oidc-utils.ts>);
  - job outputs and `needs`: GitHub Docs, *Passing information between jobs* (<https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/pass-job-outputs>);
  - `github.repository_owner_id` and `github.repository_id`: GitHub Docs, *Contexts reference* (<https://docs.github.com/en/actions/reference/workflows-and-actions/contexts#github-context>);
  - masking: GitHub Docs, *Workflow commands* (<https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-commands#masking-a-value-in-a-log>);
  - script injection and least privilege: GitHub Docs, *Secure use reference* (<https://docs.github.com/en/actions/reference/security/secure-use>); OWASP Top 10 CI/CD Security Risks, CICD-SEC-5 (<https://owasp.org/www-project-top-10-ci-cd-security-risks/>);
  - JWT and base64url: RFC 7519 (<https://www.rfc-editor.org/rfc/rfc7519>), RFC 7515 §2 (<https://www.rfc-editor.org/rfc/rfc7515#section-2>);
  - AWS trust for GitHub: AWS IAM, *Configuring a role for GitHub OIDC identity provider* (<https://docs.aws.amazon.com/IAM/latest/UserGuide/id_roles_create_for-idp_oidc.html>);
  - Terraform test variables, `variable` blocks in test files and `-test-directory`: HashiCorp, *Tests* (<https://developer.hashicorp.com/terraform/language/tests>) and *Command: test* (<https://developer.hashicorp.com/terraform/cli/commands/test>).
- Checked by Sofia on 2026-11-01 with Terraform 1.15.3, not 1.17.1. A test file that declares `variable "expected_subject"` takes its value from `-var`, and so do module variables of a run with a `module { source }` block. The init needs `-test-directory` to install the module of a test in that directory, and that init replaces the installed test modules: re-init between `tests/` and `tests-oidc/` locally. A file-level `variables` block overrides `-var` for the same name. An undeclared `-var` is parsed as an expression and fails. The builder confirms these with 1.17.1.
- jq's `@base64d` rejects base64url's `-` and `_` (checked with jq 1.7.1), hence the `gsub` pair. The builder tests the decode on a sample payload containing `-` or `_` and with no padding, and confirms the runner image's jq version.
- The throwaway-branch push in criterion 5 is approved by the human (2026-11-01) for this one branch only.
- Packages: none.
- Touches:
  - `infra/terraform/modules/deploy-role/main.tf`: two variables and the subject local (criterion 1)
  - `infra/terraform/envs/dev/variables.tf` and `main.tf`: two variables, passed to the module (criteria 1, 2)
  - `infra/terraform/envs/dev/tests/deploy_role.tftest.hcl` and `dev.tftest.hcl` (criteria 1, 2)
  - `infra/terraform/envs/dev/tests-oidc/subject.tftest.hcl`, new (criterion 4)
  - `.github/workflows/ci.yml`: the new `oidc-token` and `oidc-subject` jobs only (criteria 3, 4)
  - `infra/terraform/README.md`: step 4, *Destroy* and the deploy-role line (criterion 6)
  - `docs/production-path.md`: one sentence in P55 (criterion 6)

## References

- parent: docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- story: docs/tickets/epic-7-release/tickets/story-7.5-terraform-dev-environment-and-oidc-deploy.md, criterion 5
- architecture: docs/architecture/architecture.md, AD-28 (Deploy), AD-32
- prd: docs/prd/prd.md, NFR-INFRA-2, NFR-INFRA-4
- production path: docs/production-path.md, P46, P55
- verification: docs/verification/7.15-challenge.md, 7.15-prebuild-review.md, 7.15-prebuild-security.md
- research: ~/disputedesk-research/research_notes/LocalStack testing/offline-checks.md, Summary and *Recommended minimal set* item 1 (outside the repo)

## Notes

- Risk high (proposed): a wrong subject means no deploy can ever assume the role, and a token with the STS audience could assume it once the account exists. A check beyond the criteria: the supervisor's post-merge read of the first `main` run's logs for any token text.
- Human decision, 2026-11-01: match GitHub's immutable subject, `repo:<owner>@<owner-id>/<repo>@<repo-id>:ref:refs/heads/main`, built by Terraform from inputs with documented bounds. The repository is not opted out. The mocked test asserts the exact string. There is no AWS account, and no story runs any AWS command or calls AWS.
- Human decision, 2026-11-01 (replaces the earlier audience `sts.amazonaws.com` for the CI check): the check requests the non-STS audience `disputedesk-oidc-subject-check` and checks `aud` equals it, in its own `id-token: write`-only shell job that outputs only `sub`; a second `contents: read` job runs the Terraform comparison; neither gates `publish`. Before merge the builder pushes one throwaway branch with the main-only condition relaxed in that copy only, reads its log, then deletes the branch.
- Decision: 2026-11-01 (Sofia, 7.15 write): the CI check gets the expected IDs from the `github` context (`repository_owner_id`, `repository_id`). `envs/dev` commits no variable values (`*.tfvars` is ignored and the README passes `TF_VAR_*`), so there are no committed values to check against. The job feeds the context's values to the deploy-role module through `terraform test`, so the subject format exists only in the module. The README tells the operator to read the same values with `gh api`.
- Decision: 2026-11-01 (Sofia, 7.15 write): the IDs are strings with a 1-to-19-digit bound, not Terraform numbers, so their rendering in the subject is exact. The bound is a sanity limit, not a claim about GitHub's ID range.
- Decision: 2026-11-01 (Sofia, 7.15 refine): the throwaway branch is pushed, not opened as a pull request, so its token carries a `ref:refs/heads/` subject like `main`'s; pointing the test at the branch's ref would need a ref variable nothing else uses, so the expected Terraform failure is read instead.
- Limit: the check covers the subject of a job on `main` with no `environment:`. A later `environment:` or subject customization on `deploy.yml` changes its subject without failing this check (Boundaries keep `deploy.yml` unchanged; P55).
- Collision: in `ci.yml` this story adds two new jobs, while 7.16 adds one step to the `terraform` job and 7.17 one step to the `platform` job. These are different sections, so the story that merges second rebases. 7.16 scans `modules/*/main.tf` for IAM names, and this story adds none.
