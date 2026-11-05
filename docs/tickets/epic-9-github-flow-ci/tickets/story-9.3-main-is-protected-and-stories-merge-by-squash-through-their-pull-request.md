---
id: 3
type: story
title: "Main is protected by a ruleset, and stories merge by squash through their pull request"
parent: epic-9-github-flow-ci
covers: [R33, R39]
after: [2]
hitl: true
risk: high
refined: true
---

# Main is protected by a ruleset, and stories merge by squash through their pull request

## Description

ad-012 clauses 2 and 6, and the last steps of its rollout order (clause 7). `main` has no protection today, and the supervisor reaches it by a command-line fast-forward (`scripts/merge-story.sh`, then `scripts/push-and-watch.sh` on `main`). After this story a ruleset on `main` requires a pull request, the two gate checks 9.1 added (`ci-passed`, `security-passed`) on an up-to-date branch, and blocks force pushes and deletion; the repository allows only squash merges, with auto-merge on and the head branch deleted on merge. The supervisor opens a pull request from `feature/<story>` and, once every stage has passed, arms `gh pr merge --squash --auto` for the head commit the stages checked. The `story/**` push trigger and the two scripts go, and the process rules follow: AGENTS.md's Commits section and story flow, CLAUDE.md, the skills and `docs/process/agentic-sdlc.md`. That rule diff is the human's to approve (ad-012 clause 6).

Human steps (hitl): the human applies the ruleset and the repository settings with the `gh api` calls in criteria 1 and 2, once 9.3's own pull request shows both gates successful, and approves the rule diff (criterion 7). 9.3's pull request is then the first squash merge under the ruleset (criterion 8).

## Requirements

Verify: actionlint and shellcheck pass in `floor-guard`; the `gh api` read-backs in criteria 1 and 2 match the committed settings; each Supervisor check below fails as described; after the merge, `main`'s CI and Security runs on the squash commit succeed and `gh pr view` shows the pull request merged by squash.

Inputs:
- Story branch: `feature/<story>`, `<story>` matching `^[0-9]+\.[0-9]+[a-z]?$` (the story ID shape `merge-story.sh` checks today).
- Pull request base: `main`, or `feature/<base>` for a story stacked on an unmerged story, `<base>` bounded the same way.
- Head commit for `gh pr merge --match-head-commit`: the full 40-character SHA the verifier and the reviewer checked (the diff hash in `docs/verification/<story>-build.json` covers its tree).
- Required check names: exactly `ci-passed` and `security-passed`, the job IDs 9.1 added, each reported by the GitHub Actions app.
- `check-ready.sh <story-id>`: unchanged usage; a dependency counts as available when `refs/heads/feature/<dependency>` exists on `origin`.

1. **A ruleset protects `main`** (ad-012 clause 2; R33, NFR-SEC-8)
   **Given** `.github/rulesets/main.json`, a ruleset in the shape GitHub's REST API and its import accept, targeting the default branch, `enforcement: active`, with no bypass actors
   **When** the human applies it with `gh api --method POST repos/<owner>/<name>/rulesets --input .github/rulesets/main.json` and the supervisor reads it back with `gh api repos/<owner>/<name>/rulesets/<id>`
   **Then** the read-back holds exactly these rules: `deletion`; `non_fast_forward`; `pull_request` with `required_approving_review_count: 0` (one maintainer cannot approve their own pull request); `required_status_checks` with `strict_required_status_checks_policy: true` and the two checks in Inputs, each pinned to the GitHub Actions app's `integration_id`; and `code_scanning` for CodeQL with `security_alerts_threshold: high_or_higher` and `alerts_threshold: errors` (Notes, Assumption)
   **And** the read-back is recorded in `docs/verification/9.3-review.md`; a second application uses `--method PUT .../rulesets/<id>`, so there is one ruleset

2. **Squash is the only merge method** (ad-012 clause 2; R33)
   **Given** the human runs `gh api --method PATCH repos/<owner>/<name>` with `allow_squash_merge=true`, `allow_merge_commit=false`, `allow_rebase_merge=false`, `allow_auto_merge=true`, `delete_branch_on_merge=true`, `squash_merge_commit_title=PR_TITLE`, `squash_merge_commit_message=PR_BODY`
   **When** the supervisor reads back `gh api repos/<owner>/<name>` with those seven fields
   **Then** each has that value, recorded beside criterion 1's read-back, so the squash commit on `main` carries the pull request's title in the commit-message convention
   **And** `infra/README.md` gains a *Repository settings* section with the two commands of criteria 1 and 2 and their read-backs, saying why there is no linear-history rule and no merge queue (ad-012 clause 2)

3. **The settings are applied only once both gates report** (ad-012 clause 7; R33)
   **Given** 9.3's pull request, from `story/9.3` to `main`
   **When** its `ci-passed` and `security-passed` checks have both succeeded on its head
   **Then** the human applies criteria 1 and 2, and from then until 9.3 merges nothing is merged by `merge-story.sh` (the ruleset refuses its push to `main`)

4. **The `story/**` trigger and the two scripts go** (ad-012 clauses 1, 6, 7; R33)
   **Given** `ci.yml` and `security.yml` with `push: branches: [main, "story/**"]`, and `scripts/push-and-watch.sh` and `scripts/merge-story.sh`
   **When** this story lands
   **Then** both workflows run on `pull_request` and `push` to `main` only (Security also on its schedule), both scripts are deleted, and no tracked file (`git grep`) under `.github/`, `scripts/`, `infra/` or `.claude/`, nor AGENTS.md or CLAUDE.md, names `story/**`, `story/<`, `push-and-watch.sh` or `merge-story.sh` (records under `docs/` keep their history)
   **And** `scripts/check-ready.sh` looks up `refs/heads/feature/<dependency>` (its message and header follow), and `scripts/lane.sh`'s usage comment gives `origin/feature/8.7` as its base-ref example

5. **A stacked story rebases and retargets after its base merges** (ad-012 clause 6; R33)
   **Given** a story whose pull request has the base `feature/<base>`
   **When** the base's pull request squash-merges and GitHub deletes `feature/<base>`
   **Then** the process (criterion 7) says: GitHub retargets the stacked pull request to `main` when its base branch is deleted, and the supervisor confirms it with `gh pr view <n> --json baseRefName` (else `gh pr edit <n> --base main`); the supervisor runs `git rebase --onto main <base's last head SHA> feature/<story>`, pushes with `git push --force-with-lease --force-if-includes origin feature/<story>`, and the pull request's checks run again before any merge
   **And** a pull request that is only behind `main` is brought up to date with `gh pr update-branch <n>`, with no force push; because that makes a new head, the supervisor re-arms the merge with the new head's SHA once its checks pass, and no other stage re-runs (the pull-request checks cover a merge from `main`)

6. **The guard allows only the force push the flow needs** (ad-012 clause 6; R33)
   **Given** `.claude/hooks/guard.py`
   **When** a Bash command is checked
   **Then** `git push` with `--force-with-lease` and `--force-if-includes` to `origin feature/<story>` asks the main session and is denied to a subagent, like any push; every other force push (`-f`, `--force`, `--force-with-lease` alone, or any force push naming `main`) is still denied
   **And** `gh pr merge` still asks; `gh api` that writes, named by `-X` or `--method` with anything but `GET`, or implied by `-f`, `-F`, `--field`, `--raw-field` or `--input` without `-X GET`, asks the main session and is denied to a subagent, because it can change the ruleset and the repository settings

7. **The process rules describe the new flow** (ad-012 clause 6, human answer "the commit on main"; R39)
   **Given** AGENTS.md (Commits; Story flow's last line), CLAUDE.md (the stacked brief's `story/<base>`), `.claude/skills/review-story/SKILL.md` (the stacked diff base) and `docs/process/agentic-sdlc.md` (section 2's sequence and its push paragraph, section 3)
   **When** this story lands, written by the supervisor and approved by the human as one diff
   **Then** they say: a story is built on `feature/<story>` and reaches `main` only through its pull request; the supervisor opens it with `gh pr create`, and once every stage has passed arms `gh pr merge <n> --squash --auto --match-head-commit <sha>`, then confirms the pull request merged and `main`'s CI and Security runs on the squash commit succeeded (`gh run watch --exit-status`); the stacked steps of criterion 5; and the replacement invariant, in place of the fast-forward rule: pull-request checks test GitHub's merge of the up-to-date branch into `main`, and `main`'s own run builds, scans and publishes the squash commit
   **And** the standing approvals keep their meaning, with "pushed through `scripts/push-and-watch.sh`" replaced by the pull-request steps; `main` is never force-pushed, and the ruleset refuses it

8. **9.3 is the first squash merge under the ruleset** (ad-012 clause 7; R33)
   **Given** criteria 1 to 7 on 9.3's pull request, with every stage passed and the rule diff approved
   **When** the supervisor arms `gh pr merge --squash --auto --match-head-commit <head>`
   **Then** GitHub merges it once both required checks pass on the up-to-date branch and deletes `story/9.3`, and `main`'s run on the squash commit succeeds with `publish` and the deploy notice (9.2)

## Boundaries

- Must not change: what any check job runs, the gate jobs, `publish`, `deploy.yml`, the story flow's stages (pre-build review, builder, reviewer, verifier, security, QA, fix rounds), the standing approvals' scope.
- No merge queue, no required reviewer, no linear-history rule, no Gitflow branches (ad-012, Options rejected and clause 2).
- Auto-merge is armed only after every stage has passed; it never replaces a stage (challenge, section 2).

## Supervisor checks

After criteria 1 and 2 are applied, each must be refused:
- `git push origin <a throwaway commit>:main` (the guard asks; approve it for this check): refused by the ruleset (pull request required).
- A throwaway pull request with a failing backend test, armed with `gh pr merge --squash --auto`: it does not merge; close it and delete its branch.
- `gh pr merge <throwaway> --merge` and `--rebase`: refused (merge method not allowed).
- A throwaway pull request made behind `main` by another merge: not mergeable until `gh pr update-branch`.
- `gh pr merge <n> --squash --match-head-commit <an older SHA>`: refused (head moved).
- Guard, fed the hook JSON on stdin: `git push --force origin feature/9.9` and `git push --force-with-lease --force-if-includes origin main` are denied; `gh api -X DELETE repos/<owner>/<name>/rulesets/<id>` and `gh api repos/<owner>/<name>/rulesets --input main.json` (an implied POST) both ask.
- Throwaway copy: a story whose `after` names a story with only `story/<id>` on `origin`; `check-ready.sh` reports it not ready.

## Build facts

- Owner: backend (backend-builder) for `.github/`, `scripts/` and `infra/README.md`; the supervisor writes the rule diff (AGENTS.md, CLAUDE.md, the skill, `docs/process/agentic-sdlc.md`, `.claude/hooks/guard.py`), which the guard leaves outside the builder's paths, and the human approves it.
- Tier: Feature, with a human gate: a rule change (AGENTS.md, Human gates) and repository settings (hitl). The reviewer runs the security checklist on the ruleset and the guard.
- Flags: hitl, rule change, security.
- Lane: A, after 9.2; nothing else merges between criterion 3 and criterion 8.
- Before the build (supervisor, docs-only change): AD-32's rule gains the ruleset (pull request, the two gates, up to date, squash only) and the invariant of criterion 7, with a row in `docs/architecture/spine-amendments.md` (AD-32, 2026-11-05, ad-012 clauses 2 and 6). The builder does not edit them. The rule diff of criterion 7 is not docs-first: it lands in 9.3's pull request, because it describes a flow that exists only once 9.3 merges.
- Mechanisms and sources:
  - Rulesets and their rules (pull request, required status checks with *require branches to be up to date*, block force pushes, restrict deletions, code scanning results): <https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets>; REST: <https://docs.github.com/en/rest/repos/rules>; import: <https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/managing-rulesets-for-a-repository>.
  - Merge methods and squash settings: <https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/about-merge-methods-on-github>; REST *Update a repository*: <https://docs.github.com/en/rest/repos/repos#update-a-repository>.
  - Auto-merge: <https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/automatically-merging-a-pull-request>.
  - Head branch deletion and automatic retargeting: <https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/managing-the-automatic-deletion-of-branches>; <https://docs.github.com/en/pull-requests/collaborating-with-pull-requests/proposing-changes-to-your-work-with-pull-requests/changing-the-base-branch-of-a-pull-request>.
  - `gh pr create`, `gh pr merge` (`--squash`, `--auto`, `--match-head-commit`), `gh pr edit --base`, `gh pr update-branch`, `gh run watch --exit-status`: <https://cli.github.com/manual/> (the builder confirms each flag in the installed `gh`'s help).
  - `--force-with-lease` and `--force-if-includes`: <https://git-scm.com/docs/git-push>.
  - GitHub flow: <https://docs.github.com/en/get-started/using-github/github-flow>.
- Packages: none.
- Touches: `.github/workflows/ci.yml` and `security.yml` (the `push` branches), `.github/rulesets/main.json` (new), `scripts/push-and-watch.sh` and `scripts/merge-story.sh` (deleted), `scripts/check-ready.sh`, `scripts/lane.sh` (comment), `infra/README.md` (new section); supervisor's rule diff: `AGENTS.md`, `CLAUDE.md`, `.claude/skills/review-story/SKILL.md`, `docs/process/agentic-sdlc.md`, `.claude/hooks/guard.py`.

## Pre-build

- Not yet reviewed. ad-012's challenge `[story]` points for these clauses are folded in: exact check names and squash enforced by repository settings (criteria 1, 2), auto-merge enabled (criterion 2), the `story/**` trigger removed only after the gates are required (criteria 3, 4), stacked stories (criterion 5), auto-merge armed only after the stages (Boundaries), the replacement invariant (criterion 7).

## References

- parent — docs/tickets/epic-9-github-flow-ci/epic-9-github-flow-ci.md, section Requirements
- decision — docs/decisions/ad-012-github-flow-ci.md, clauses 2, 6, 7 and the human decisions on branching, merge method and the commit on main
- challenge — docs/verification/ad-012-challenge.md, sections 1 to 5
- research — ~/disputedesk-research/research_notes/ci-simplification/external-research.md, sections 1, 2 and 5 (outside the repository)
- process — AGENTS.md, Commits, Story flow and Human gates; docs/process/agentic-sdlc.md, sections 2 and 3
- architecture — docs/architecture/architecture.md, AD-32

## Notes

- Risk high (proposed): a ruleset that does not require the gates, or a guard that lets an agent force-push or delete the ruleset, would let unchecked code reach `main`; beyond the criteria, the reviewer confirms from a throwaway pull request's merge box that both checks show as *Required*.
- Human decision, 2026-11-05: the ruleset also carries the `code_scanning` rule for CodeQL (high or higher security alerts, error alerts). NFR-SEC-8 and R33 say a high or critical CodeQL finding fails the build; the CodeQL job uploads its findings and succeeds, so `security-passed` cannot see them, and `security.yml`'s first line already expects "the CodeQL code-scanning result" to block once the ruleset exists. The builder confirms against GitHub Docs that the rule is available on a user-owned public repository; if it is not, the gap is recorded in `docs/production-path.md` instead and the report says so.
- Decision: 2026-11-05 (Sofia) — `--match-head-commit` on every armed merge, so auto-merge merges only the head the stages checked: after a later push the armed merge fails instead of merging an unchecked head (`gh pr merge` manual, `--match-head-commit`; the builder confirms it holds with `--auto`).
- Decision: 2026-11-05 (Sofia) — the ruleset is a committed JSON file applied with `gh api`, so the settings are reviewable and re-appliable; GitHub's import takes the same shape.
- Decision: 2026-11-05 (Sofia) — the guard change is in this story: removing `push-and-watch.sh` moves the stacked story's `--force-with-lease` push to the main session, where the guard denies every force push today.
- Decision: 2026-11-05 (Sofia) — squash commit title and message come from the pull request, so the supervisor writes the commit-message convention once, as the pull request's title.
- 9.3 itself is built on `story/9.3` and pushed with `push-and-watch.sh`, the flow it replaces, because the new flow exists only once it merges. Its head drops the `story/**` trigger, so the push starts no push run: the supervisor opens 9.3's pull request before pushing, and `push-and-watch.sh` finds the pull request's runs on the pushed commit (else it times out after 1800 s).
