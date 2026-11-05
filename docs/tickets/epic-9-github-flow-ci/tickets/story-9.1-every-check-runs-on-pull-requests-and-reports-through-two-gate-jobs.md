---
id: 1
type: story
title: "Every check runs on pull requests and reports through two gate jobs"
parent: epic-9-github-flow-ci
covers: [R33]
after: [8.6, 8.9, 8.13, 8.15, 8.16, 8.17]
hitl: false
risk: medium
refined: true
---

# Every check runs on pull requests and reports through two gate jobs

## Description

ad-012 clauses 1, 3 and 5, the first step of its rollout order (clause 7). Today a `changes` job (`scripts/ci-changes.sh`) decides whether a push is docs-only, and every heavy job in `ci.yml` is skipped when it is; a required check that is skipped counts as passing, so no single check can say "every check passed". This story makes every check job run on every pull request, removes the `changes` job and its script, and adds one gate job per workflow, `ci-passed` and `security-passed`, which fails unless every job it needs succeeded. The Compose install (three copies) and the Trivy install (two copies) become one local composite action each, holding the one version and SHA-256 pin. The triggers stay as they are, `story/**` included, so the flow keeps working until 9.3 applies the ruleset and removes that trigger.

## Requirements

Verify: actionlint and shellcheck pass in CI's `floor-guard` job; on a pull request opened from `story/9.1` to `main`, every `ci.yml` and `security.yml` job runs and `ci-passed` and `security-passed` both report success; the push run on `story/9.1` is green; each Supervisor check below fails as described.

Inputs (no endpoint, no workflow input):
- A gate job's input is the `result` of each job it needs (GitHub Docs, *Contexts*, `needs`): one of `success`, `failure`, `cancelled`, `skipped`. Only `success` passes; for the `codeql` matrix job the result is the matrix's combined result.
- The composite actions take no inputs: each holds its version and SHA-256 as constants, Compose v5.6.1 with `47737f4236e6d3ccea7291995203f8f018058783b4c3a9469119811a0b5db8c0` and Trivy v0.76.1 with `c7cbe3137fb8a48a17574bede3dd6beb16742d1e9772929940fc90082c39d9c0`, today's pins.

1. **Every check job runs on every pull request** (ad-012 clause 1; R33, NFR-INFRA-2)
   **Given** `ci.yml`'s `changes` job, the `needs: changes` and `if: needs.changes.outputs.code == 'true'` lines of `backend`, `frontend`, `platform`, `terraform` and `e2e`, and the `floor-guard` step `scripts/ci-changes.test.sh`
   **When** this story lands
   **Then** the job, those lines and that step are gone, `scripts/ci-changes.sh` and `scripts/ci-changes.test.sh` are deleted, and a pull request that changes only a file under `docs/` runs every `ci.yml` job but `publish`
   **And** both workflows keep their triggers exactly (`pull_request`; `push` to `main` and `story/**`; Security's weekly `schedule`) and their `concurrency` blocks

2. **`ci-passed` fails unless every check job succeeded** (ad-012 clause 1; R33)
   **Given** a job `ci-passed` in `ci.yml` with `needs: [backend, frontend, platform, terraform, floor-guard, e2e]`, `if: always()`, `timeout-minutes: 5` and no permissions beyond the workflow's `contents: read`
   **When** it runs, on a pull request or a push
   **Then** it succeeds only when every needed job's result is `success`, and otherwise fails, naming each job whose result is not `success` with that result; the results reach its one `run:` step through `env`, never as `${{ }}` inside the script
   **And** a needed job that is skipped, cancelled or failed makes it fail, so it is never skipped itself and never passes on a skip (GitHub Docs, *Troubleshooting required status checks*)

3. **`security-passed` fails unless every security job succeeded** (ad-012 clause 3; R33, NFR-SEC-8)
   **Given** a job `security-passed` in `security.yml` with `needs: [gitleaks, codeql, vulnerable-packages, trivy]` and the same `if`, timeout, permissions and result rule as criterion 2
   **When** it runs, on a pull request, a push or the schedule
   **Then** it succeeds only when all four results are `success`
   **And** the `trivy` job still builds and scans the five images on every pull request, as today (the human's answer on the pre-merge image scan)

4. **One Compose install action** (ad-012 clause 5; R33)
   **Given** the three *Install Compose* steps in `backend`, `platform` and `e2e` and the workflow-level `COMPOSE_VERSION` and `COMPOSE_SHA256`
   **When** this story lands
   **Then** a composite action `.github/actions/install-compose/action.yml` does what the steps do today (download with `curl -fsSL --max-time 300`, `sha256sum --check --strict`, install into the user's CLI plugins folder, fail unless `docker compose version --short` is the pinned version), each `run` step naming `shell: bash`, and the three jobs call it with `uses: ./.github/actions/install-compose` after their checkout
   **And** the workflow-level `env` block goes, and the comments that say "change them together" (`ci.yml` and `infra/terraform/modules/compute/main.tf`, `compose_version`) name the action's file

5. **One Trivy install action** (ad-012 clause 5; R33)
   **Given** the *Install Trivy* steps in Security's `trivy` job and CI's `publish` job, with their `TRIVY_VERSION` and `TRIVY_SHA256` in `security.yml`'s workflow `env` and `publish`'s job `env`
   **When** this story lands
   **Then** a composite action `.github/actions/install-trivy/action.yml` holds the one pin and does what the steps do today (download, `sha256sum --check --strict`, extract `trivy`, add it to `GITHUB_PATH`), its download bounded with `--max-time 300` like the Compose download, and both jobs call it
   **And** neither workflow names a Trivy version or SHA-256 any more; `TRIVY_NO_PROGRESS` stays where it is; gitleaks' pin in `security.yml` is unchanged; `scripts/security-local.sh`'s comment that CI pins the version in `security.yml` names the action's file

6. **`publish` is otherwise unchanged** (ad-012, What does not change; R33)
   **Given** the `publish` job
   **When** this story lands
   **Then** its `if`, its `needs` (`backend`, `frontend`, `platform`, `floor-guard`, `e2e`), its permissions and its build, scan and push steps are unchanged apart from the Trivy action, and the comment above it no longer mentions a docs-only push
   **And** `security.yml`'s first-line comment says the two gate checks become required in the ruleset (story 9.3)

7. **Both gates report on a pull request** (ad-012 clause 7; R33)
   **Given** a pull request from `story/9.1` to `main`, which the supervisor opens with `gh pr create` when it pushes the story branch, and never merges through GitHub (main is still fast-forwarded by `merge-story.sh`, and GitHub marks the pull request merged when main reaches its head)
   **When** its checks finish
   **Then** its checks list `ci-passed` and `security-passed`, both successful, reported by the GitHub Actions app (`gh pr checks <number>`), which is the branch-CI evidence this story merges on and the check names 9.3's ruleset requires

## Boundaries

- Must not change: what each check job runs (build, tests, drift checks, lint, the e2e journeys and the ZAP baseline, Terraform checks, the scans), actions pinned by full commit SHA, each job's permissions, the caches, the `publish` steps, `deploy.yml`.
- The `story/**` push trigger stays until 9.3 (ad-012 clause 7). No ruleset or repository setting changes here.
- No path filter of any kind (ad-012, Options rejected).

## Supervisor checks

Throwaway branch with a pull request to `main`, never merged; each must fail as described:
- An `exit 1` step added to `vulnerable-packages`; `security-passed` fails naming `vulnerable-packages: failure`.
- `if: false` added to `e2e`; `ci-passed` fails naming `e2e: skipped`.
- One character of the Compose SHA-256 in `install-compose/action.yml` changed; `backend`, `platform` and `e2e` fail at the checksum, and `ci-passed` fails naming all three.
- A failing assertion in one backend test; `ci-passed` fails naming `backend: failure`.
- A pull request that changes only `docs/glossary.md`; every check job runs (criterion 1).

## Build facts

- Owner: backend (backend-builder; `.github/`, `scripts/` and `infra/` are its paths).
- Tier: Feature (CI). The reviewer runs the security checklist (both workflows' permissions and the scan gate).
- Flags: ci.
- Lane: A, after epic 8's last stories; 9.2 follows in the same lane.
- Before the build (supervisor, docs-only change): AD-32's rule names the two gate jobs, that every check job runs on every pull request, and the two composite actions, and a row in `docs/architecture/spine-amendments.md` (AD-32, 2026-11-05, ad-012 clauses 1, 3, 5). The builder does not edit them.
- Mechanisms and sources:
  - `pull_request` runs on the merge ref: <https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#pull_request>.
  - A skipped job counts as passing; a gate with `always()`: <https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks>.
  - `needs` context and job results: <https://docs.github.com/en/actions/reference/workflows-and-actions/contexts#needs-context>.
  - `jobs.<job_id>.if` and status functions: <https://docs.github.com/en/actions/reference/workflows-and-actions/expressions#status-check-functions>.
  - Composite actions (`runs.using: composite`, `shell` required on each `run`): <https://docs.github.com/en/actions/sharing-automations/creating-actions/creating-a-composite-action>.
  - Untrusted values through `env`: <https://docs.github.com/en/actions/reference/security/secure-use>.
- Packages: none.
- Touches: `.github/workflows/ci.yml` (the `changes` job, five jobs' `needs`/`if`, three Compose steps, the workflow `env`, `floor-guard`'s `ci-changes.test.sh` step, `publish`'s Trivy step and comment, new `ci-passed`), `.github/workflows/security.yml` (Trivy step and `env`, first-line comment, new `security-passed`), `.github/actions/install-compose/action.yml` and `.github/actions/install-trivy/action.yml` (new), `scripts/ci-changes.sh` and `scripts/ci-changes.test.sh` (deleted), `scripts/security-local.sh` (one comment), `infra/terraform/modules/compute/main.tf` (one comment).

## Pre-build

- Not yet reviewed. ad-012 had its decision-time challenge (`docs/verification/ad-012-challenge.md`); its `[story]` points on the gates are folded into criteria 2, 3 and 7.

## References

- parent — docs/tickets/epic-9-github-flow-ci/epic-9-github-flow-ci.md, section Requirements
- decision — docs/decisions/ad-012-github-flow-ci.md, clauses 1, 3, 5, 7
- challenge — docs/verification/ad-012-challenge.md, sections 2, 4, 5
- research — ~/disputedesk-research/research_notes/ci-simplification/external-research.md, sections 2 and 4 (outside the repository)
- architecture — docs/architecture/architecture.md, AD-32

## Notes

- Risk medium (proposed): a gate that passes on a skipped or failed job would let a red pull request merge once 9.3 makes it required; criterion 2's skip rule and the Supervisor checks prove it fails.
- Decision: 2026-11-05 (Sofia) — the gates need every check job, `terraform` included, although `publish.needs` leaves `terraform` out (story 7.16): a pull request must pass every check, while `publish` keeps not depending on Terraform.
- Decision: 2026-11-05 (Sofia) — `publish`'s digest outputs are in 9.2, which reads them (epic Notes).
- Decision: 2026-11-05 (Sofia) — the Trivy download gains the `--max-time 300` the Compose download already has, because every network call has a timeout; it is the only behaviour change inside the moved steps.
- Until 9.3 a pull request from a `story/**` branch runs each workflow twice (its push and its pull request); that lasts two stories and goes with the trigger (ad-012 clause 7).
