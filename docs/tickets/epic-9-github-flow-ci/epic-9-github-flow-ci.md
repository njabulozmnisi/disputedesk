---
id: 9
type: epic
title: "Production CI on GitHub flow"
parent: initiative-disputedesk
covers: [R33, R38, R39]
sources: [docs/decisions/ad-012-github-flow-ci.md]
companions: [docs/verification/ad-012-challenge.md]
after: []
risk: high
refined: true
---

# Production CI on GitHub flow

## Description

Carries out `docs/decisions/ad-012-github-flow-ci.md` (accepted 2026-11-05, with the human's decisions in its table). Today CI runs on pushes to `main` and `story/**`, a `changes` job skips the heavy jobs on docs-only pushes, the Compose install is written three times and the Trivy install twice, `main` has no protection, a story reaches `main` by a command-line fast-forward (`scripts/push-and-watch.sh`, `scripts/merge-story.sh`), and deploy is a manual dispatch that resolves the digests again. After this epic, checks run on pull requests and report through two gate jobs, a ruleset protects `main`, a story merges by squash through its pull request, and deploy follows `publish` on `main`, switched off until an AWS account exists.

The epic follows ad-012's rollout order (clause 7): the triggers, the gate jobs and the composite actions land first while the `story/**` trigger still runs (9.1); deploy follows publish (9.2); the ruleset and repository settings are applied once a pull request shows both gates reporting, and then the `story/**` trigger and the two scripts go, with the process rules rewritten (9.3).

## Outcome

A story reaches `main` only through a pull request whose two gate checks passed on the up-to-date branch, and `main`'s own run builds, scans and publishes its squash commit and then deploys it, or says deploy is switched off. Signal: the 9.3 pull request is the first squash merge under the ruleset, and `main`'s run after it is green with the deploy notice.

## Requirements

R33 (parent R33): GitHub Actions builds every pull request, runs every suite and the scans, failing on any untriaged high or critical finding, and pushes images to GHCR on main. This epic delivers: every check job on every pull request with no docs-only skip (ad-012 clause 1, story 9.1); the gate jobs `ci-passed` and `security-passed` (clauses 1 and 3, 9.1); the Compose and Trivy installs as composite actions (clause 5, 9.1); the ruleset that makes the two gates required on an up-to-date branch, with squash as the only merge method (clause 2, 9.3); the supervisor merging through the pull request (clause 6, 9.3).
R38 (parent R38): Terraform for the dev environment, deployed through a GitHub OIDC role with no long-lived keys; AWS-only mechanisms stay as built until an apply is possible. This epic delivers, as the human amended AD-28 on 2026-11-05: deploy runs after `publish` on `main` when `DEPLOY_ENABLED` is `true` and by hand for rollback; the role trusts the subject `environment:dev`, with `dev` restricted to `main`; the mocked Terraform tests pin the new subject (clause 4, 9.2).
R39 (parent R39): the README and the records. This epic delivers: `infra/terraform/README.md`'s one-time subject check and first-deploy steps for the new subject and the switch (9.2); the repository settings recorded with the commands that apply them (9.3).

## Done when

- Stories 9.1 and 9.2 are merged on today's flow with CI and Security green; 9.3 is merged through its pull request by squash under the ruleset.
- `gh api` read-backs show the ruleset on `main` and the repository's merge settings as 9.3 states them.
- `main`'s run after the 9.3 merge succeeds, including `publish` and the deploy notice.
- No workflow or script names `story/**`, `push-and-watch.sh`, `merge-story.sh` or `ci-changes.sh`.

## Boundaries

- What ad-012 keeps: the check content (build, tests, drift checks, lint, the e2e journeys and the ZAP baseline, Terraform checks, the scans, the pull-request image scan), actions pinned by full commit SHA, least-privilege permissions, the caches, images tagged by SHA and deployed by digest.
- No path filter (`on.paths` or `dorny/paths-filter`), no merge queue, no Gitflow branches (ad-012, Options rejected).
- No AWS account, AWS command or AWS API call; the first deploy proves the live role assumption (ad-012, AD-28 row: offline limit).
- The epic 8 lows batch and retrospective are not in this epic; they run on the new flow after it (ad-012, Timing).

## References

- parent — docs/initiative-disputedesk.md, section Requirements (R33, R38, R39)
- decision — docs/decisions/ad-012-github-flow-ci.md, Human decisions and Decision clauses 1 to 7
- challenge — docs/verification/ad-012-challenge.md
- research — ~/disputedesk-research/research_notes/ci-simplification/external-research.md (outside the repository), sections 2 to 5
- architecture — docs/architecture/architecture.md, AD-28, AD-32; docs/architecture/spine-amendments.md
- prd — docs/prd/prd.md, NFR-SEC-8, NFR-INFRA-2, NFR-INFRA-4
- process — AGENTS.md, Commits and Story flow; docs/process/agentic-sdlc.md, section 2

## Notes

- Decision: 2026-11-05 (Sofia) — three stories in ad-012's rollout order, one lane, each after the one before: 9.1 and 9.2 both change `ci.yml` (9.1 the Trivy install in `publish`, 9.2 its outputs and the jobs after it), and 9.3 needs both gates reporting on a pull request before the ruleset is applied.
- Decision: 2026-11-05 (Sofia) — tracer bullet: 9.1. It is the thinnest path through every layer the epic touches (both workflows, a local action, a pull request's checks) and nothing else can be applied until both gates report.
- Decision: 2026-11-05 (Sofia) — `publish`'s five digest outputs move from the proposed 9.1 to 9.2, the story that consumes them, so no output exists without a reader and 9.2's call proves them.
- Decision: 2026-11-05 (Sofia) — 9.1 and 9.2 merge on today's flow (AGENTS.md as it stands: `story/<story>` branches, `push-and-watch.sh`, fast-forward). The flow changes with 9.3's merge, and a rule change mid-story applies from the next story (AGENTS.md, Story flow), so 9.3's own pull request is the first squash merge.
- Decision: 2026-11-05 (Sofia) — branches are named `feature/<story>` from 9.3 on, as the human's branching-model answer in ad-012 says; `scripts/check-ready.sh`'s stacked-dependency lookup and the docs that name `story/<base>` follow in 9.3.
- Decision: 2026-11-05 (Sofia) — set check by a fresh-context validator: no cycle, drift or ordering problem; every ad-012 clause and human-decision row is covered. Folded: `security-local.sh`'s pin comment (9.1); the tftest `variables` block, the variable descriptions and the workflow-level concurrency block (9.2); `gh api`'s implied POST in the guard rule and `git grep` for tracked files (9.3); re-arming after `gh pr update-branch` and opening 9.3's pull request before its push (9.3); the pending-job limit of a concurrency group (9.2 Notes). Declined: `allowed_merge_methods: ["squash"]` in the ruleset, because ad-012 clause 2 enforces squash through the repository settings, and one place for it is enough.
- Waits on epic 8 because: ad-012 builds after epic 8 merges. 9.1 lists epic 8's last stories in each chain (8.6, 8.9, 8.13, 8.15, 8.16, 8.17), which together depend on every other epic 8 story.
- Waits on 1 because: 9.2 changes the `publish` job 9.1 changes. Waits on 2 because: 9.3 applies the ruleset after both gates report, and removes the `story/**` trigger last.
