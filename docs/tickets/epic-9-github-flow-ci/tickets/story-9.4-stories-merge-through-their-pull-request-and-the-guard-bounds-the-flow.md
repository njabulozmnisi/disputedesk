---
id: 4
type: story
title: "Stories merge by squash through their pull request, and the guard bounds the flow"
parent: epic-9-github-flow-ci
covers: [R33, R39]
after: [3]
hitl: true
risk: high
decisions: settled
refined: true
---

# Stories merge by squash through their pull request, and the guard bounds the flow

## Description

ad-012 clauses 6 and 7, the last step of its rollout order, split from 9.3 on 2026-11-05 (9.3, Notes). Once 9.3 has merged, `main` takes only a squash merge of a checked pull request, but the flow still names `story/**` branches, `scripts/push-and-watch.sh` and `scripts/merge-story.sh`, and the guard neither bounds the stacked story's force push nor asks on `gh` writes that change the ruleset or the settings. After this story the `story/**` trigger and the two scripts are gone; every change is built on a `feature/` branch and reaches `main` through its pull request by written steps; the guard allows, in the main session only and with no prompt, exactly the push and merge forms the standing approval covers (the human's answer on P21), asks on one bounded force push and on every `gh` write, and denies every other force push; and the process rules describe the new flow. That rule diff is the supervisor's to write and the human's to approve (ad-012 clause 6).

Human step (hitl): the human approves the rule diff (criterion 7) as one diff.

## Requirements

Verify: actionlint and shellcheck pass in `floor-guard`; criterion 1's grep finds nothing; each guard case in Supervisor checks gives its decision; 9.4 merges by squash through its own pull request by criterion 2's steps, and `main`'s CI and Security runs on the squash commit succeed.

Inputs:
- The `gh` account: every `gh` write and every read-back runs as the repository owner, `GH_TOKEN="$(gh auth token --user <owner>)" gh …`, as 9.3 states; the token is never printed, logged or written to a file.
- Story branch: `feature/<story>`, `<story>` matching `^[0-9]+\.[0-9]+[a-z]?$` (the story ID shape `merge-story.sh` checks today).
- Branch of a change outside a story (a records pull request, a Small fix, a docs-only change): `feature/<slug>`, `<slug>` matching `^[a-z][a-z0-9.-]{0,49}$`, so it never matches the story shape; the records of a merged story ride in `feature/records-<story>`.
- Pull request base: `main`, or `feature/<base>` for a story stacked on an unmerged story, `<base>` bounded as a story.
- Pull request title: the commit-message convention's subject, one line of at most 72 characters; the detail goes in the body, which becomes the squash commit's message (9.3).
- Head commit for `--match-head-commit`: the full 40-character SHA of the commit whose tree the stages checked (`docs/verification/<story>-build.json`'s diff hash covers that tree), or the head `gh pr update-branch` made from it once its checks pass.
- The guard's exact forms, each a full match of the whole command (no `;`, `&&`, `|` or second command):
  - allowed in the main session with no prompt: `git push origin feature/<story>`, and `GH_TOKEN="$(gh auth token --user <owner>)" gh pr merge <n> --squash --auto --match-head-commit <sha>`, `<n>` 1 to 6 digits, `<sha>` 40 lower-case hex characters, `<owner>` the owner's login;
  - asks the main session: `git [-C <absolute path>] push --force-with-lease --force-if-includes origin feature/<story>`, one refspec, no `+` and no `:`.
- `check-ready.sh <story-id>`: unchanged usage; a dependency counts as available when `refs/heads/feature/<dependency>` exists on `origin`.

1. **The `story/**` trigger and the two scripts go** (ad-012 clauses 1, 6, 7; R33)
   **Given** `ci.yml` and `security.yml` with `push: branches: [main, "story/**"]`, and `scripts/push-and-watch.sh` and `scripts/merge-story.sh`
   **When** this story lands
   **Then** both workflows run on `pull_request` and `push` to `main` only (Security also on its schedule), both scripts are deleted, and no tracked file outside the records (`docs/verification/`, `docs/tickets/`, `docs/decisions/`) names `story/**`, `story/<`, `push-and-watch.sh`, `merge-story.sh` or `ci-changes.sh` (`git grep`)
   **And** `scripts/check-ready.sh` looks up `refs/heads/feature/<dependency>` (its message and header follow), and `scripts/lane.sh`'s usage comment gives `origin/feature/8.7` as its base-ref example

2. **Every change reaches `main` through its pull request by written steps** (ad-012 clause 6; R33)
   **Given** a story, or a change outside a story, committed on its branch (Inputs) once every stage it needs has passed
   **When** the supervisor ships it
   **Then** the steps are: `git push origin feature/<id>`; `GH_TOKEN=… gh pr create --head feature/<id> --base <base> --title <title> --body <body>`; `gh pr checks <n> --watch --required`; arm only when `gh pr view <n> --json baseRefName` reads `main`, with `GH_TOKEN=… gh pr merge <n> --squash --auto --match-head-commit <sha>`; before any later push to an armed pull request, `gh pr merge <n> --disable-auto`, then re-arm with the new head once its checks pass; confirm the merge and its squash SHA with `gh pr view <n> --json state,mergeCommit`, then `gh run list --branch main --commit <squash sha> --workflow CI` (and `Security`) and `gh run watch <id> --exit-status`
   **And** a change outside a story takes the same steps and waits for the full checks (every check job runs on every pull request, ad-012 clause 1); the capture, the epic's lows and decisions and a stage's read-back record of a merged story ride in one records pull request, `feature/records-<story>`, after its merge

3. **A stacked story rebases and retargets after its base merges** (ad-012 clause 6; R33)
   **Given** a story whose pull request has the base `feature/<base>`
   **When** the base's pull request squash-merges and GitHub deletes `feature/<base>`
   **Then** GitHub retargets the stacked pull request to `main` (*Managing branches within your repository*, Deleting a branch), and the supervisor confirms it with `gh pr view <n> --json baseRefName` (else `gh pr edit <n> --base main`); it takes the base's last head from `gh pr view <base-pr> --json headRefOid`, runs `git fetch origin` and `git rebase --onto origin/main <that sha> feature/<story>`, pushes with criterion 4's force form, and the pull request's checks run again before any merge
   **And** a pull request that is only behind `main` is brought up to date with `gh pr update-branch <n>`, with no force push; the local branch then takes the remote head first (`git fetch` and a fast-forward), and the merge is re-armed with the new head once its checks pass, with no other stage re-run; a `mergeStateStatus` of `DIRTY`, or a drift check failing after an update, goes to a builder as a fix round, since the supervisor writes no code

4. **The guard bounds the force push** (ad-012 clause 6; R33)
   **Given** `.claude/hooks/guard.py`
   **When** a Bash command is checked
   **Then** the force form in Inputs, as a full match of the whole command, with or without `-C <absolute path>`, asks the main session and is denied to a subagent
   **And** every other push with `-f`, `--force`, `--force-with-lease` or `--force-if-includes`, or with a `+` refspec, is denied, `git -C <dir> push …` included; so are a destination refspec (`feature/9.4:main`, `HEAD:feature/9.5`), a second refspec, and the force form followed by another command

5. **The guard asks on every `gh` write** (ad-012 clause 6; R33)
   **Given** `.claude/hooks/guard.py`
   **When** a Bash command is checked, with or without the owner-token prefix
   **Then** `gh api` that writes asks the main session and is denied to a subagent: a method named by `-X` or `--method`, attached or with `=` (`-XPOST`, `--method=PATCH`), other than `GET` compared case-insensitively, or one implied by `-f`, `-F`, `--field`, `--raw-field` or `--input` without `-X GET`; so do `gh repo edit|delete|rename|archive`, `gh variable set|delete` (`DEPLOY_ENABLED` switches deploy on, 9.2) and `gh workflow run` (a dispatch can deploy)
   **And** `gh pr merge` in any form but the exact one in Inputs still asks; a `gh api` read passes with no decision

6. **The guard allows the exact forms the standing approval covers** (the human's answer on P21, `docs/verification/epic-9-decisions.md`; R33)
   **Given** `.claude/hooks/guard.py`
   **When** the main session runs `git push origin feature/<story>` or the exact `gh pr merge` form in Inputs, each as the whole command
   **Then** the guard allows it with no prompt
   **And** a subagent is denied both; any other push, merge, `gh api` write or `gh repo edit|delete|rename|archive` still asks the main session, and every force push but criterion 4's form is denied (criteria 4, 5); a change outside a story (`feature/<slug>`) is pushed with a prompt

7. **The process rules describe the new flow** (ad-012 clause 6, human answer "the commit on main"; R39)
   **Given** the rule diff the supervisor writes as a patch before dispatch: AGENTS.md (Commits; Story flow's last line; "CI's ZAP baseline runs on every push"; "Branch CI stays the full authoritative run"; "green on the story branch"), CLAUDE.md (the stacked brief's `story/<base>`; the guard sentence, with the new asks and the two allowed forms), `.claude/skills/review-story/SKILL.md` (the stacked diff base), `.claude/skills/qa-story/SKILL.md` (the ZAP line), `docs/process/agentic-sdlc.md` (section 2's sequence and its push paragraph; section 3's docs-only CI box, routed here by the 9.1 pre-build review, and its G1 and G3 lines) and `.claude/hooks/guard.py`
   **When** the builder's first step applies it (`git apply --binary <patch>`, with no `docs/` exclusion), so the one full run and the diff hash cover it
   **Then** they say: every change is built on a `feature/` branch and reaches `main` only through its pull request by criterion 2's steps; the stacked steps of criterion 3; the ZAP baseline and the full checks run on every pull request and on `main`; and the replacement invariant, in place of the fast-forward rule: pull-request checks test GitHub's merge of the up-to-date branch into `main`, and `main`'s own run builds, scans and publishes the squash commit
   **And** the standing approvals keep their meaning, with "pushed through `scripts/push-and-watch.sh`" replaced by criterion 2's steps; `main` is never force-pushed, and the ruleset refuses it

8. **9.4 merges through its own pull request** (ad-012 clause 7; R33)
   **Given** criteria 1 to 7 on `feature/9.4`, with every stage passed and the rule diff approved
   **When** the supervisor ships it by criterion 2's steps (the guard change is not yet on `main`, so the push and the merge ask)
   **Then** GitHub squash-merges it once both required checks and the code-scanning rule pass on the up-to-date branch, and deletes `feature/9.4`
   **And** `main`'s CI and Security runs on the squash commit succeed, and from then on the next story follows the new rules

## Boundaries

- Must not change: what any check job runs, the gate jobs, `publish`, `deploy.yml`, the ruleset and settings 9.3 applied, the story flow's stages (pre-build review, builder, reviewer, verifier, security, QA, fix rounds), the standing approvals' scope.
- No merge queue, no required reviewer, no linear-history rule, no Gitflow branches, no `gh stack` extension (ad-012, Options rejected and clause 6).
- Auto-merge is armed only after every stage has passed; it never replaces a stage (challenge, section 2).
- The guard rule is explainable in two sentences: the two exact forms of the standing approval run with no prompt in the main session, and one exact lease-checked force push to a story branch asks; every other force push is denied, and any `gh` call that writes asks.

## Supervisor checks

Guard cases, each kept in a file written with the Write tool (a Bash command holding a force-push example is itself denied) and run with `python3 -I .claude/hooks/guard.py < case.json`, once as the main session and once as a subagent:
- No prompt in the main session, denied to a subagent: `git push origin feature/9.4`; `GH_TOKEN="$(gh auth token --user <owner>)" gh pr merge 5 --squash --auto --match-head-commit <40 hex>`.
- Ask in the main session, denied to a subagent: the force form, with and without `-C <absolute path>`; `git push origin feature/records-9.3`; `git -C <absolute path> push origin feature/9.4`; `gh pr merge 5 --squash --auto`; `gh api -XPOST …`; `gh api --method=PATCH …`; `gh api -X patch …`; `gh api repos/<owner>/<name>/rulesets --input main.json`; `gh api -X DELETE repos/<owner>/<name>/rulesets/<id>`; `gh repo edit --enable-merge-commit`; `gh variable set DEPLOY_ENABLED --body true`; `gh workflow run deploy.yml`; each of the `gh` cases with the owner-token prefix.
- Denied: `--force-with-lease` alone; `-f`; `+feature/9.4`; `feature/9.4:main`; `HEAD:feature/9.5`; the force form followed by `; git push -f origin feature/9.5`; `git push origin feature/9.4 && git push -f origin feature/9.5`; `git -C <dir> push --force origin feature/9.4`.
- No decision: `gh api repos/<owner>/<name>/rulesets/<id>`.
- Throwaway copy: a story whose `after` names a story with only `story/<id>` on `origin`; `check-ready.sh` reports it not ready.

## Build facts

- Owner: backend (backend-builder) for `.github/` and `scripts/`; the supervisor writes the rule diff (AGENTS.md, CLAUDE.md, the two skills, `docs/process/agentic-sdlc.md`, `.claude/hooks/guard.py`), which the guard leaves outside the builder's paths, and the human approves it.
- Tier: Feature, with a human gate: a rule change (AGENTS.md, Human gates). The reviewer runs the security checklist on the guard and reads `guard.py` in the applied patch.
- Flags: hitl, rule change, security.
- Lane: A, after 9.3; it is the only story in flight from 9.3's merge until it merges.
- Before the build (supervisor): the rule diff written as a patch; the brief's first step applies it.
- Mechanisms and sources:
  - GitHub flow: <https://docs.github.com/en/get-started/using-github/github-flow>.
  - Auto-merge and what disables it: <https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/automatically-merging-a-pull-request>.
  - Automatic retargeting when a base branch is deleted: GitHub Docs, *Managing branches within your repository*, Deleting a branch (the quote is in `docs/verification/9.3-prebuild-review.md`); the builder records the page's URL in its report.
  - `gh pr create`, `gh pr checks`, `gh pr merge` (`--squash`, `--auto`, `--match-head-commit`, `--disable-auto`), `gh pr view`, `gh pr edit --base`, `gh pr update-branch`, `gh run list`, `gh run watch --exit-status`, `gh api`, `gh repo edit`: <https://cli.github.com/manual/>; flags confirmed with `gh help <command>`.
  - `--force-with-lease`, `--force-if-includes` and the `+` refspec: <https://git-scm.com/docs/git-push>; `git rebase --onto`: <https://git-scm.com/docs/git-rebase>.
  - The guard is a Claude Code `PreToolUse` hook already in the repository: <https://code.claude.com/docs/en/hooks>.
- Packages: none.
- Touches: `.github/workflows/ci.yml` and `security.yml` (the `push` branches), `scripts/push-and-watch.sh` and `scripts/merge-story.sh` (deleted), `scripts/check-ready.sh`, `scripts/lane.sh` (comment); the supervisor's rule diff: `AGENTS.md`, `CLAUDE.md`, `.claude/skills/review-story/SKILL.md`, `.claude/skills/qa-story/SKILL.md`, `docs/process/agentic-sdlc.md`, `.claude/hooks/guard.py`.

## Pre-build

- Reviewed as part of 9.3: `docs/verification/9.3-prebuild-review.md` (this story was split from 9.3 after that review).
- Sofia, 2026-11-05: folded the `[story]` points of `docs/verification/9.3-prebuild-review.md` that belong to the flow and the guard. P1 (the owner token, Inputs, criteria 2, 5), P3 (the rule diff as a patch applied first, criterion 7), P4 (the bounded force form and the deny list, criterion 4), P5 (`gh api` method forms, `gh repo`, and with their reason `gh variable` and `gh workflow run`, criterion 5), P6 (disable auto-merge before a push, criterion 2), P7 (arm only on base `main`, criterion 2), P10 (`feature/<slug>` and the records pull request, Inputs, criterion 2), P13 (guard cases in a file; `gh help`), P14 (the retargeting citation, criterion 3), P15 (the stacked steps' SHA, fetch, fast-forward and conflict path, criterion 3), P16 (the armed SHA, Inputs), P17 (the grep excludes only records; each named line, criteria 1, 7), P19 (the wait steps, criterion 2), and the 9.1 review's finding 9 (`agentic-sdlc.md`'s docs-only box, criterion 7). Applied the human's answer on P21 (criterion 6). Criteria: 8.
- Decisions: settled by the human, 2026-11-05 (`docs/verification/epic-9-decisions.md`, P21: allow the exact forms).

## References

- parent — docs/tickets/epic-9-github-flow-ci/epic-9-github-flow-ci.md, section Requirements
- decision — docs/decisions/ad-012-github-flow-ci.md, clauses 6, 7 and the human decisions on branching and the commit on main
- decisions — docs/verification/epic-9-decisions.md, P21
- challenge — docs/verification/ad-012-challenge.md, sections 1 to 5
- review — docs/verification/9.3-prebuild-review.md
- previous story — docs/tickets/epic-9-github-flow-ci/tickets/story-9.3-main-is-protected-by-a-ruleset-and-squash-is-the-only-merge-method.md
- process — AGENTS.md, Commits, Story flow and Human gates; docs/process/agentic-sdlc.md, sections 2 and 3

## Notes

- Risk high (proposed): a guard that lets an agent force-push, merge or change the ruleset would let unchecked code reach `main`, and a wrong process rule would stall every later story; beyond the criteria, the reviewer runs each guard case file once more against the applied patch.
- Decision: 2026-11-05 (Sofia) — the rule diff joins the builder's change as a patch applied first (pre-build review, P3, simplest fix), so the builder's one full run and diff hash cover it and no stage re-runs on a late delta.
- Decision: 2026-11-05 (Sofia) — `gh variable set|delete` and `gh workflow run` also ask (P5's optional part, taken): the first switches deploy on, the second can dispatch a deploy, so both change what reaches AWS once an account exists.
- Decision: 2026-11-05 (Sofia) — the records of a merged story ride in one records pull request after its merge (P10), not in the next story's diff, so a story's diff hash covers only its own change.
- Decision: 2026-11-05 (Sofia) — the human's answer on P21 names two exact forms; a push from another directory (`git -C …`) and a records branch (`feature/records-<story>`) are not among them, so they ask. The supervisor runs `cd <worktree>` in one call and the exact push in the next.
- Decision: 2026-11-05 (Sofia) — a committed guard test in `floor-guard` (P13's optional part) is not taken: `guard.py` is the supervisor's rule diff, and the guard cases run as Supervisor checks; the epic 9 retrospective may propose it as a deterministic check.
- Decision: 2026-11-05 (Sofia) — GitHub's native stacked pull requests (pre-build review, noted) would remove criterion 3's manual rebase, but they need the `gh stack` extension or the website, availability here is unverified, and ad-012 clause 6 names the manual rebase; a possible later simplification.
- Assumption: the guard lets the builder's `git apply` of the supervisor's patch touch the supervisor's paths; if it does not, the supervisor applies the patch after the stages and the verifier and the reviewer re-run on that delta (P3, second fix).
