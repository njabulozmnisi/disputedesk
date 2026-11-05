---
id: 2
type: story
title: "Deploy follows publish on main, behind a switch, and the deploy role trusts the dev environment"
parent: epic-9-github-flow-ci
covers: [R33, R38, R39]
after: [1]
hitl: true
risk: high
refined: true
---

# Deploy follows publish on main, behind a switch, and the deploy role trusts the dev environment

## Description

ad-012 clause 4 with clause 3's digest outputs, which amend AD-28 (the human's answer of 2026-11-05). Today `deploy.yml` is a manual `workflow_dispatch` that takes a typed SHA and resolves the five digests again from GHCR, and `publish` writes the digests only to its job summary. After this story `publish` exposes the five image references by digest as job outputs; `deploy.yml` is also a reusable workflow that takes the SHA and those references; CI calls it after `publish` on `main` when the repository variable `DEPLOY_ENABLED` is `true`, and otherwise writes a notice and makes no AWS call; the manual dispatch stays for rollback. The called job runs in the GitHub environment `dev`, which only `main` may deploy to, so the deploy role's OIDC trust moves from the subject `ref:refs/heads/main` to `environment:dev`, keeping the exact audience and the immutable repository identity.

Human step (hitl): before the merge, the human creates the environment `dev` with a deployment branch policy that allows only `main`, with the two `gh api` calls in criterion 5. `DEPLOY_ENABLED` is not set: no AWS account exists.

## Requirements

Verify: actionlint passes in `floor-guard`; `terraform fmt -check`, `validate` and `test` (mocked providers) pass for `bootstrap` and `envs/dev`; on the story branch's pull request both gates succeed; after the fast-forward, `main`'s run shows `publish` with five outputs and the deploy notice, and no job assumes an AWS role; no AWS command is run.

Inputs:
- `DEPLOY_ENABLED` (repository variable, read as `vars.DEPLOY_ENABLED`): deploy runs only when it is `true`. GitHub's expressions compare strings ignoring case, so `TRUE` also switches it on; unset, empty or any other text (`1`, `yes`, `on`) means switched off. The README sets exactly `true`.
- `deploy.yml` on `workflow_call`: `sha`, a string, exactly 40 lower-case hex characters; `api`, `worker`, `gateway`, `core-banking`, `provision`, strings, each exactly `ghcr.io/njabulozmnisi/disputedesk-<that name>@sha256:` plus 64 lower-case hex characters (the shape the SSM document's `allowedPattern` also enforces). All six required.
- `deploy.yml` on `workflow_dispatch`: `sha` alone, bounded as today (40 lower-case hex characters, a commit of `main` whose images are published).
- The environment name: exactly `dev`. The deploy-role module takes it as a new variable `github_environment`, validated against `^[A-Za-z0-9_.-]{1,255}$` (GitHub's environment name rules; the builder confirms them in GitHub Docs and cites the page), and `envs/dev` passes `"dev"`.

1. **`publish` exposes the five image references as job outputs** (ad-012 clause 3; R33, AD-32)
   **Given** the *Build, scan and push the five images* step
   **When** each image has been pushed
   **Then** the step writes `<name>=ghcr.io/njabulozmnisi/disputedesk-<name>@<digest>` to `GITHUB_OUTPUT` for `api`, `worker`, `gateway`, `core-banking` and `provision`, and the job declares those five `outputs`; the job summary table is unchanged
   **And** each output has the shape in Inputs, so a pushed image with no repository digest fails the step before the outputs are used

2. **`deploy.yml` is a reusable workflow with its called job in `dev`** (ad-012 clause 4; R38, AD-28)
   **Given** `deploy.yml`
   **When** this story lands
   **Then** it runs on `workflow_call` with the six inputs in Inputs and keeps `workflow_dispatch` with `sha`; its one job sets `environment: dev`, `concurrency: { group: deploy-dev, cancel-in-progress: false }`, `permissions: { id-token: write, contents: read }` and keeps `if: github.ref == 'refs/heads/main'`; the workflow-level `concurrency` block goes, so the group is held only by the job
   **And** before any AWS step the job fails unless `sha` and each image reference match their bounds in Inputs, each value reaching the scripts through `env`, never as `${{ }}` inside `run:`; the steps from *configure-aws-credentials* on are unchanged and take the image references from the call's inputs

3. **CI deploys after `publish` only when switched on** (ad-012 clause 4, human answer "wired, switched off"; R38)
   **Given** two new `ci.yml` jobs, both with `needs: publish`: `deploy` with `if: vars.DEPLOY_ENABLED == 'true'`, which calls `./.github/workflows/deploy.yml` with `sha: ${{ github.sha }}` and the five `publish` outputs and grants `permissions: { id-token: write, contents: read }`; and `deploy-switched-off` with `if: vars.DEPLOY_ENABLED != 'true'`, `timeout-minutes: 5`, no permissions beyond `contents: read`, whose one step writes `::notice::Deploy is switched off: the repository variable DEPLOY_ENABLED is not true, so nothing was sent to AWS.`
   **When** `main`'s run reaches them with `DEPLOY_ENABLED` unset
   **Then** `deploy-switched-off` succeeds with that notice, `deploy` is skipped and no job runs *configure-aws-credentials*
   **And** on a pull request both are skipped because `publish` is, and neither is among `ci-passed`'s needs

4. **Rollback is the manual dispatch** (ad-012 clause 4; R38)
   **Given** `deploy.yml` run by `workflow_dispatch` on `main` with an earlier `sha`
   **When** it runs
   **Then** it resolves the five digests from GHCR and checks each revision label as today, then runs the same job in `dev` and in the same concurrency group, so a rollback never interleaves with an automatic deploy
   **And** `infra/terraform/README.md`'s rollback section still names this dispatch

5. **The environment `dev` allows only `main`** (ad-012 clause 4; R38, NFR-INFRA-4)
   **Given** the human runs `gh api --method PUT repos/<owner>/<name>/environments/dev` with `deployment_branch_policy` `{"protected_branches": false, "custom_branch_policies": true}`, then `gh api --method POST repos/<owner>/<name>/environments/dev/deployment-branch-policies` with `name` `main` and `type` `branch`
   **When** the supervisor reads them back with `gh api repos/<owner>/<name>/environments/dev` and `.../deployment-branch-policies`
   **Then** `dev` has custom branch policies and exactly one, the branch `main`, with no required reviewers and no wait timer (one maintainer, continuous deployment); the read-back is recorded in `docs/verification/9.2-security.md`
   **And** the two commands are written in `infra/terraform/README.md`, section 8, before the repository variables

6. **The deploy role trusts `environment:dev`** (ad-012 clause 4, AD-28 amended; R38, NFR-INFRA-4)
   **Given** `modules/deploy-role/main.tf`'s `oidc_subject` and `envs/dev/tests/deploy_role.tftest.hcl`
   **When** this story lands
   **Then** the subject is `repo:<owner>@<owner-id>/<name>@<repo-id>:environment:<github_environment>`, the trust policy is still exactly `StringEquals` on `token.actions.githubusercontent.com:aud` = `sts.amazonaws.com` and that `:sub`, with no `StringLike`
   **And** both pinned subjects in the test (the exact trust policy and the owner-case case) read `…:environment:dev`, their error messages say "this repository's dev environment", and the test's `variables` block sets `github_environment = "dev"`; the module's comments and variable descriptions and `envs/dev/variables.tf`'s description ("only its main branch may assume") describe the environment subject; no other policy changes

7. **The README's one-time check and first deploy follow** (ad-012 clause 4; R39)
   **Given** `infra/terraform/README.md`'s `modules/deploy-role` row and section 8
   **When** this story lands
   **Then** both name the subject `repo:<owner>@<owner-id>/<name>@<repo-id>:environment:dev`; the one-time check is otherwise unchanged; section 8 creates `dev` (criterion 5), sets the four repository variables, runs the first deploy by dispatch, and only after it succeeds sets `gh variable set DEPLOY_ENABLED --body true`, saying that from then on every `main` run deploys
   **And** the README says the mocked tests pin the subject but cannot prove the live role assumption, which the first deploy's `AssumeRoleWithWebIdentity` proves (the human's offline-limit answer)

## Boundaries

- Must not change: the deploy steps from *configure-aws-credentials* on (SSM Run Command, the wait loop, the final status), the SSM document and its patterns, `deploy.sh`, the four repository variables, any IAM policy but the trust subject, `publish`'s build, scan and push.
- No required reviewer or wait timer on `dev`; no artifact attestation (production path P54).
- No AWS account, command or API call; `DEPLOY_ENABLED` stays unset.

## Supervisor checks

Throwaway copy or branch, each must fail as described:
- The module's subject format back to `:ref:refs/heads/main`; `terraform test` in `envs/dev` fails at the trust-policy assertion.
- `StringEquals` on `:sub` changed to `StringLike`; `terraform test` fails.
- `needs.publish.outputs.apii` in the `deploy` job's `with:`; actionlint fails in `floor-guard`.
- `gh workflow run deploy.yml --ref <a throwaway branch> -f sha=<a main SHA>`; the job is skipped by the `main` guard. With the guard removed on that branch, the run is refused by `dev`'s branch policy before any step runs.
- In a throwaway copy, the published image's digest line replaced by its tag (`ghcr.io/njabulozmnisi/disputedesk-api:sha-<12>`); the step's shape check (criterion 1) fails the `publish` job before its outputs are set. Run it on a scratch branch with `publish`'s `if` and its push removed, so nothing reaches GHCR.

## Build facts

- Owner: backend (backend-builder; `.github/` and `infra/` are its paths).
- Tier: High-stakes (IAM trust, deploy): the security agent reviews the diff, and the human reviews it in a review block.
- Flags: high-stakes, security, hitl (criterion 5).
- Lane: A, after 9.1.
- Before the build (supervisor, docs-only change recording the human's AD-28 answer of 2026-11-05): AD-28's rule (deploy after `publish` on `main` when `DEPLOY_ENABLED` is `true`, by hand for rollback; the role trusts `environment:dev`, `dev` restricted to `main`; "no new AWS-only mechanisms before an apply" amended for these two) and its *Deploy* detail; AD-32's last sentence (`deploy.yml` is called with `publish`'s digests, and dispatched for rollback); the deployment view's GitHub Actions arrow if it names the trigger; a row in `docs/architecture/spine-amendments.md` (AD-28, 2026-11-05, ad-012 clause 4; the offline limit); the initiative's R38 clause "AWS-only mechanisms stay as built until then" with this amendment named; `docs/production-path.md` P55 (the trust now names the environment `dev`, and CI's `deploy` job is the one job that holds `id-token: write`). The builder does not edit them.
- Mechanisms and sources:
  - Reusable workflows, `workflow_call` inputs, `environment` on the called job, permissions passed down: <https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows>.
  - Job outputs: <https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/pass-job-outputs>.
  - `vars` context in `jobs.<job_id>.if`: <https://docs.github.com/en/actions/reference/workflows-and-actions/contexts#vars-context>.
  - The `notice` workflow command: <https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-commands>.
  - Environments and deployment branch policies: <https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments>; REST: <https://docs.github.com/en/rest/deployments/environments>, <https://docs.github.com/en/rest/deployments/branch-policies>.
  - Concurrency without cancelling a deploy: <https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency>.
  - OIDC subject for an environment and the AWS trust: <https://docs.github.com/en/actions/reference/security/oidc>; <https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-aws>.
  - Build, release, run (deploy the published digest, roll back by redeploying): <https://12factor.net/build-release-run>.
- Packages: none.
- Touches: `.github/workflows/ci.yml` (`publish` outputs and its step's output lines; new `deploy` and `deploy-switched-off`), `.github/workflows/deploy.yml` (triggers, the job's `environment`, `concurrency`, input checks, where the image references come from), `infra/terraform/modules/deploy-role/main.tf` (subject, new variable, comments), `infra/terraform/envs/dev/main.tf` (passes `github_environment`), `infra/terraform/envs/dev/variables.tf` (one description), `infra/terraform/envs/dev/tests/deploy_role.tftest.hcl` (`variables` block, two pins and their messages), `infra/terraform/README.md` (row, section 8, rollback).

## Pre-build

- Not yet reviewed. ad-012's challenge `[story]` points for this clause are folded in: digest outputs (criterion 1), `environment` on the called job (criterion 2), the notice from a job that runs rather than a skipped one (criterion 3), `dev` restricted to `main` (criterion 5), audience and immutable identity kept and the Terraform pin and one-time check followed (criteria 6, 7).

## References

- parent — docs/tickets/epic-9-github-flow-ci/epic-9-github-flow-ci.md, section Requirements
- decision — docs/decisions/ad-012-github-flow-ci.md, clause 4 and the human decisions on deploy and AD-28
- challenge — docs/verification/ad-012-challenge.md, sections 1, 3, 4, 5
- research — ~/disputedesk-research/research_notes/ci-simplification/external-research.md, section 3 (outside the repository)
- stories — docs/tickets/epic-7-release/tickets/story-7.5-terraform-dev-environment-and-oidc-deploy.md; story-7.15-deploy-role-trusts-the-immutable-oidc-subject.md; docs/tickets/epic-8-simplicity-sweep/tickets/story-8.16-iam-policies-are-json-templates-checked-in-the-infra-tests.md
- architecture — docs/architecture/architecture.md, AD-28, AD-32
- production path — docs/production-path.md, P53, P55

## Notes

- Risk high (proposed): a trust subject any job can satisfy would hand the deploy role to it; beyond the criteria, the security agent confirms from GitHub's OIDC reference that only a job that names `environment: dev` gets the `environment:dev` subject, and that `dev`'s branch policy is checked before such a job starts.
- Decision: 2026-11-05 (Sofia) — ad-012's one "deploy job that writes a notice or calls the reusable workflow" is two jobs with opposite `if`s, because a job that calls a reusable workflow takes `uses`, `with`, `needs`, `if`, `permissions` and a few other keys but no `steps` (GitHub Docs, *Reuse workflows*, supported keywords), and a skipped job cannot write a notice (challenge, section 3). The meaning is the decision's: one switch, a notice when off, the call when on.
- Decision: 2026-11-05 (Sofia) — the environment name is a module variable, not a literal, because AD-28 keeps the modules ready for a second environment (D8); `envs/dev` passes `"dev"`.
- Decision: 2026-11-05 (Sofia) — the `main` guard on the job stays beside `dev`'s branch policy: it is today's line, it costs nothing, and it keeps a dispatch from another branch from even asking for the environment.
- Decision: 2026-11-05 (Sofia) — the environment is created by the human before the merge (hitl), because a job that names an environment that does not exist creates it with no branch policy (GitHub Docs, *Deployments and environments*; the builder confirms the sentence), and changing repository settings is a human step.
- Known limit, recorded: with `cancel-in-progress: false` a running deploy is never cancelled, but GitHub keeps one pending job per group, so a newer queued deploy replaces an older pending one (a queued rollback can be dropped by an automatic deploy, or the reverse). It matters only once `DEPLOY_ENABLED` is on; the README's rollback section says to dispatch a rollback once the group is idle.
- Collision: `ci.yml`'s `publish` job, which 9.1 also changes; 9.2 builds after 9.1 merges.
