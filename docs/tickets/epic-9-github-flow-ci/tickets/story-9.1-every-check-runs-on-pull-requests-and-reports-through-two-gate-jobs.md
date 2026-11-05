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

ad-012 clauses 1, 3 and 5, the first step of its rollout order (clause 7). Today a `changes` job (`scripts/ci-changes.sh`) decides whether a push is docs-only, and every heavy job in `ci.yml` is skipped when it is; a required check that is skipped counts as passing, so no single check can say "every check passed". This story makes every check job run on every pull request, removes the `changes` job and its script, and adds one gate job per workflow, `ci-passed` and `security-passed`, which fails unless every job it needs succeeded; a `floor-guard` step keeps each gate's `needs` equal to the workflow's check jobs. The Compose install (three copies) and the Trivy install (two copies) become one local composite action each, holding the one version and SHA-256 pin, with its script beside the action so shellcheck still lints it. The triggers stay as they are, `story/**` included, so the flow keeps working until the ruleset is applied (9.3) and the trigger goes (9.4).

## Requirements

Verify: actionlint and shellcheck pass in CI's `floor-guard` job, shellcheck now including `.github/actions/*/*.sh`; on a pull request opened from `story/9.1` to `main`, every `ci.yml` and `security.yml` job runs and `ci-passed` and `security-passed` both report success; the push run on `story/9.1` is green; each Supervisor check below fails as described; after the fast-forward, `main`'s CI run succeeds with `publish` included, which is the proof of `publish`'s call of the Trivy action (no pull request runs `publish`).

Inputs (no endpoint, no workflow input):
- A gate job's input is the `needs` context (GitHub Docs, *Contexts*, `needs`), passed whole as one variable `NEEDS: ${{ toJSON(needs) }}` and read with jq. Each entry's `result` is one of `success`, `failure`, `cancelled`, `skipped`; only `success` passes. For the `codeql` matrix job, `needs.codeql.result` is the matrix's combined result: observed, not documented (GitHub Docs do not say how a matrix's legs combine), so a Supervisor check proves that one failing leg fails the gate.
- The composite actions take no inputs: each holds its version and SHA-256 as constants, Compose v5.6.1 with `47737f4236e6d3ccea7291995203f8f018058783b4c3a9469119811a0b5db8c0` and Trivy v0.76.1 with `c7cbe3137fb8a48a17574bede3dd6beb16742d1e9772929940fc90082c39d9c0`, today's pins.
- Download bounds: every download in the moved steps and the Gitleaks download has `--max-time 300`; the Compose download also has `--retry 3 --retry-all-errors` (curl's retry with its built-in backoff; the download is a GET, so a retry is safe).

1. **Every check job runs on every pull request** (ad-012 clause 1; R33, NFR-INFRA-2)
   **Given** `ci.yml`'s `changes` job, the `needs: changes` and `if: needs.changes.outputs.code == 'true'` lines of `backend`, `frontend`, `platform`, `terraform` and `e2e`, and the `floor-guard` step `scripts/ci-changes.test.sh`
   **When** this story lands
   **Then** the job, those lines and that step are gone, and `scripts/ci-changes.sh` and `scripts/ci-changes.test.sh` are deleted; in `ci.yml` no job has a job-level `if:` or `needs:` except `publish` and `ci-passed`, and in `security.yml` none except `security-passed`, a structure today's files fail
   **And** both workflows keep their triggers exactly (`pull_request`; `push` to `main` and `story/**`; Security's weekly `schedule`) and their `concurrency` blocks

2. **`ci-passed` fails unless every check job succeeded** (ad-012 clause 1; R33)
   **Given** a job `ci-passed` in `ci.yml` with `needs: [backend, frontend, platform, terraform, floor-guard, e2e]`, `if: always()`, `timeout-minutes: 5` and no permissions beyond the workflow's `contents: read`
   **When** it runs, on a pull request or a push
   **Then** its one `run:` step reads `NEEDS` (Inputs) with jq and succeeds only when every entry's result is `success`; otherwise it fails, naming each job whose result is not `success` with that result; no `${{ }}` appears inside the script, so the job list exists only in `needs`
   **And** a needed job that is skipped, cancelled or failed makes it fail, so it is never skipped itself and never passes on a skip; `always()` is chosen over `!cancelled()` because a gate skipped in a cancelled run would report `skipped`, which a required check counts as success (GitHub Docs, *Expressions*, `always`; *Troubleshooting required status checks*)

3. **`security-passed` fails unless every security job succeeded** (ad-012 clause 3; R33, NFR-SEC-8)
   **Given** a job `security-passed` in `security.yml` with `needs: [gitleaks, codeql, vulnerable-packages, trivy]` and the same `if`, timeout, permissions, `NEEDS` variable and result rule as criterion 2
   **When** it runs, on a pull request, a push or the schedule
   **Then** it succeeds only when all four results are `success`, a `codeql` leg that fails while the other succeeds included
   **And** the `trivy` job still builds and scans the five images on every pull request, as today (the human's answer on the pre-merge image scan)

4. **One Compose install action** (ad-012 clause 5; R33)
   **Given** the three *Install Compose* steps in `backend`, `platform` and `e2e` and the workflow-level `COMPOSE_VERSION` and `COMPOSE_SHA256`
   **When** this story lands
   **Then** a composite action `.github/actions/install-compose/action.yml` runs its script `install.sh` from its own folder (`"$GITHUB_ACTION_PATH/install.sh"` with `shell: bash`, GitHub's composite-action example), which does what the steps do today (download with `curl -fsSL --max-time 300 --retry 3 --retry-all-errors`, `sha256sum --check --strict`, install into the user's CLI plugins folder, fail unless `docker compose version --short` is the pinned version), and the three jobs call it with `uses: ./.github/actions/install-compose` after their checkout
   **And** the workflow-level `env` block goes, and the comments that say "change them together" (`ci.yml` and `infra/terraform/modules/compute/main.tf`, `compose_version`) name the action's folder

5. **One Trivy install action** (ad-012 clause 5; R33)
   **Given** the *Install Trivy* steps in Security's `trivy` job and CI's `publish` job, with their `TRIVY_VERSION` and `TRIVY_SHA256` in `security.yml`'s workflow `env` and `publish`'s job `env`
   **When** this story lands
   **Then** a composite action `.github/actions/install-trivy/action.yml` runs its script `install.sh` from its own folder the same way, which holds the one pin and does what the steps do today (download, `sha256sum --check --strict`, extract `trivy`, add it to `GITHUB_PATH`), its download bounded with `--max-time 300`, and both jobs call it after their checkout
   **And** neither workflow names a Trivy version or SHA-256 any more; `TRIVY_NO_PROGRESS` stays where it is; the Gitleaks download gains `--max-time 300` and its pin is unchanged; the comments that say where the pins live name the action's folder: `security.yml`'s comment above its workflow `env` (Gitleaks only), `floor-guard`'s comment ("as security.yml installs Trivy") and `scripts/security-local.sh`'s comment that CI pins the version in `security.yml`

6. **`publish` is otherwise unchanged** (ad-012, What does not change; R33)
   **Given** the `publish` job
   **When** this story lands
   **Then** its `if`, its `needs` (`backend`, `frontend`, `platform`, `floor-guard`, `e2e`), its permissions and its build, scan and push steps are unchanged apart from the Trivy action, and the comment above it no longer mentions a docs-only push
   **And** `security.yml`'s first-line comment says the two gate checks become required in the ruleset (story 9.3)

7. **Both gates report on a pull request** (ad-012 clause 7; R33)
   **Given** a pull request from `story/9.1` to `main`, which the supervisor opens with `gh pr create` when it pushes the story branch, and never merges through GitHub (`main` is still fast-forwarded by `merge-story.sh`)
   **When** its checks finish
   **Then** its checks list `ci-passed` and `security-passed`, both successful, reported by the GitHub Actions app, and the supervisor runs `gh pr checks <number>` before `merge-story.sh`, with every check of both events (`push` and `pull_request`) passing, because the scripts read only the newest run on the head commit; this is the branch-CI evidence this story merges on and the check names 9.3's ruleset requires
   **And** after the fast-forward the supervisor confirms the pull request is marked merged (GitHub "can" mark it merged once its head is reachable from `main`, *About pull request merges*), and closes it otherwise

8. **Each gate's `needs` is every check job** (ad-012 clauses 1 and 3; R33)
   **Given** a new `floor-guard` step that reads `ci.yml` and `security.yml` with yq (preinstalled on `ubuntu-24.04`)
   **When** it runs
   **Then** for each workflow it fails unless the gate's `needs` equals the workflow's job IDs minus the gate itself and the jobs the step lists as running only on `main` after the checks (`publish` in this story), naming each job missing from or extra in the gate's `needs`
   **And** so a check job added later and left out of its gate fails `floor-guard` instead of never blocking a merge

## Boundaries

- Must not change: what each check job runs (build, tests, drift checks, lint, the e2e journeys and the ZAP baseline, Terraform checks, the scans), actions pinned by full commit SHA, each job's permissions, the caches, the `publish` steps, `deploy.yml`, the Gitleaks pin.
- The `story/**` push trigger stays until 9.4 (ad-012 clause 7). No ruleset or repository setting changes here.
- No path filter of any kind (ad-012, Options rejected).

## Supervisor checks

Throwaway branch with a pull request to `main`, never merged; each must fail as described:
- An `exit 1` step added to `vulnerable-packages`; `security-passed` fails naming `vulnerable-packages: failure`.
- An `exit 1` step with `if: matrix.language == 'csharp'` added to `codeql`, so one leg fails and the other succeeds; `security-passed` fails naming `codeql: failure`.
- `if: false` added to `e2e`; `ci-passed` fails naming `e2e: skipped`.
- `gh run cancel <id>` on the pull request's CI run while `e2e` runs; `ci-passed` still runs and fails naming each cancelled job.
- One character of the Compose SHA-256 in `install-compose/install.sh` changed; `backend`, `platform` and `e2e` fail at the checksum, and `ci-passed` fails naming all three.
- One character of the Trivy SHA-256 in `install-trivy/install.sh` changed; `trivy` fails at the checksum, and `security-passed` fails naming `trivy: failure`.
- A failing assertion in one backend test; `ci-passed` fails naming `backend: failure`.
- A dummy job added to `ci.yml` and left out of `ci-passed`'s `needs`; `floor-guard` fails naming it (criterion 8).
- Against today's `ci.yml` and `security.yml`, criterion 1's structural assertion fails (job-level `needs: changes` and `if:` on five jobs).

## Build facts

- Owner: backend (backend-builder; `.github/`, `scripts/` and `infra/` are its paths).
- Tier: Feature (CI). The reviewer runs the security checklist (both workflows' permissions and the scan gate).
- Flags: ci.
- Lane: A, after epic 8's last stories; 9.2 follows in the same lane.
- Before the build (supervisor, docs-only change): AD-32's rule names the two gate jobs, that every check job runs on every pull request, and the two composite actions, and a row in `docs/architecture/spine-amendments.md` (AD-32, 2026-11-05, ad-012 clauses 1, 3, 5). In the same change, ad-012 clause 1's "it is the only required status check" becomes "it is the CI required status check" (one word, `only` to `CI`), so it agrees with clause 2's two gates (pre-build review, finding 12). The builder does not edit them.
- Mechanisms and sources:
  - `pull_request` runs on the merge ref: <https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#pull_request>.
  - A skipped job counts as passing; a gate with `always()`: <https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks>.
  - `needs` context, job results and `toJSON`: <https://docs.github.com/en/actions/reference/workflows-and-actions/contexts#needs-context>.
  - `jobs.<job_id>.if` and status functions (`always()` runs even when cancelled): <https://docs.github.com/en/actions/reference/workflows-and-actions/expressions#status-check-functions>.
  - Composite actions (`runs.using: composite`, `shell` required on each `run`, a script beside `action.yml` run through the action's path): <https://docs.github.com/en/actions/sharing-automations/creating-actions/creating-a-composite-action>; metadata syntax: <https://docs.github.com/en/actions/reference/workflows-and-actions/metadata-syntax>.
  - Untrusted values through `env`: <https://docs.github.com/en/actions/reference/security/secure-use>.
  - yq and jq on the runner image: <https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2404-Readme.md>; the builder confirms the yq expression against the installed version.
  - `curl --retry` and `--retry-all-errors`: <https://curl.se/docs/manpage.html>.
  - `gh pr create` and `gh pr checks`: <https://cli.github.com/manual/gh_pr_create>, <https://cli.github.com/manual/gh_pr_checks>.
  - A pull request marked merged when its head reaches the base: <https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/incorporating-changes-from-a-pull-request/about-pull-request-merges>.
- Packages: none.
- Touches: `.github/workflows/ci.yml` (the `changes` job, five jobs' `needs`/`if`, three Compose steps, the workflow `env`, `floor-guard`'s `ci-changes.test.sh` step, its tool-pin comment, its shellcheck pathspec and the new `needs` step, `publish`'s Trivy step and comment, new `ci-passed`), `.github/workflows/security.yml` (Trivy step and `env`, the pin comment above `env`, the Gitleaks `--max-time`, first-line comment, new `security-passed`), `.github/actions/install-compose/action.yml` and `install.sh`, `.github/actions/install-trivy/action.yml` and `install.sh` (new), `scripts/ci-changes.sh` and `scripts/ci-changes.test.sh` (deleted), `scripts/security-local.sh` (one comment), `infra/terraform/modules/compute/main.tf` (one comment).

## Pre-build

- ad-012 had its decision-time challenge (`docs/verification/ad-012-challenge.md`); its `[story]` points on the gates are folded into criteria 2, 3 and 7.
- Sofia, 2026-11-05: folded every `[story]` point of `docs/verification/9.1-prebuild-review.md`. Findings 1 (each action runs `install.sh` beside it; shellcheck's pathspec gains `.github/actions/*/*.sh`; criteria 4, 5, Touches), 2 (codeql combined result labelled observed, a one-leg Supervisor check), 3 (the docs-only check replaced by criterion 1's structural assertion and a check against today's files), 4 (criterion 8, the `needs` step, and its Supervisor check), 5 (`NEEDS: toJSON(needs)` read with jq, criterion 2), 6 (the cancelled-run check, and the `always()` reason in criterion 2), 7 (the Trivy checksum check), 8 ("after their checkout" in criterion 5; `main`'s run after the merge in Verify), 9 (the `security.yml` and `floor-guard` comments in criterion 5 and Touches; the `agentic-sdlc.md` docs-only box goes to 9.4's rule diff), 10 (criterion 7: "can be marked", close otherwise, `gh pr checks` before `merge-story.sh`, the `gh` manual cited), 11 (Gitleaks `--max-time 300`), 12 (the one-word ad-012 fix in Build facts, the supervisor's docs-first change). Added beside them: the Compose download's `--retry 3 --retry-all-errors` (`main`'s CI run 41998059314 on 2026-11-05 failed at the Compose download, exit 22, and its rerun passed). Criteria: 8.

## References

- parent — docs/tickets/epic-9-github-flow-ci/epic-9-github-flow-ci.md, section Requirements
- decision — docs/decisions/ad-012-github-flow-ci.md, clauses 1, 3, 5, 7
- challenge — docs/verification/ad-012-challenge.md, sections 2, 4, 5
- review — docs/verification/9.1-prebuild-review.md
- research — ~/disputedesk-research/research_notes/ci-simplification/external-research.md, sections 2 and 4 (outside the repository)
- architecture — docs/architecture/architecture.md, AD-32

## Notes

- Risk medium (proposed): a gate that passes on a skipped or failed job would let a red pull request merge once 9.3 makes it required; criterion 2's skip rule, criterion 8's `needs` step and the Supervisor checks prove it fails.
- Decision: 2026-11-05 (Sofia) — the gates need every check job, `terraform` included, although `publish.needs` leaves `terraform` out (story 7.16): a pull request must pass every check, while `publish` keeps not depending on Terraform.
- Decision: 2026-11-05 (Sofia) — `publish`'s digest outputs are in 9.2, which reads them (epic Notes).
- Decision: 2026-11-05 (Sofia) — the Trivy download gains the `--max-time 300` the Compose download already has, because every network call has a timeout; it is the only behaviour change inside the moved Trivy steps.
- Decision: 2026-11-05 (Sofia) — each composite action runs a script beside its `action.yml` rather than an inline `run:` (pre-build review, finding 1, first fix): actionlint does not shellcheck a composite action's `run:` steps, and GitHub's own example puts the script beside the action, so the moved lines stay linted with no `shortcut:`.
- Decision: 2026-11-05 (Sofia) — the Compose download retries (`--retry 3 --retry-all-errors`) after a transient failure on `main` (run 41998059314, exit 22). Only the Compose download is named; the Trivy and Gitleaks downloads keep a single attempt unless they fail the same way.
- Until 9.4 a pull request from a `story/**` branch runs each workflow twice (its push and its pull request); that lasts until the trigger goes (ad-012 clause 7).
