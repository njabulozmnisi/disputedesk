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

ad-012 clauses 6 and 7, the last step of its rollout order, split from 9.3 on 2026-11-05 (9.3, Notes). Once 9.3 has merged, `main` takes only a squash merge of a checked pull request, but the flow still names `story/**` branches, `scripts/push-and-watch.sh` and `scripts/merge-story.sh`. Since the human's answer on this story's P1 (2026-11-05, `docs/verification/epic-9-decisions.md`), the guard asks only for actions on a deployed environment, the main session commits, pushes and merges with no prompt, and pipeline agents are denied commit, push and merge. Its force-push deny, though, also blocks the stacked story's rebase, misses several force forms, and a subagent can still change pull requests, settings and secrets through `gh`. After this story the `story/**` trigger and the two scripts are gone; every change is built on a `feature/` branch and reaches `main` through its pull request by written steps; the guard keeps the P1 rule as the flow moves to pull requests: one lease-checked force push of a stacked story runs with no prompt in the main session, every other force push is denied, and a subagent is also denied that force push and one closed list of `gh` writes; a guard-case test in `floor-guard` proves it; and the process rules describe the new flow. That rule diff is the supervisor's to write and the human's to approve (ad-012 clause 6).

Human step (hitl): the human approves the rule diff (criterion 7) as one patch, before dispatch.

## Requirements

Verify: actionlint and shellcheck pass in `floor-guard`, and so does criterion 6's guard-case test; criterion 1's grep finds nothing; each Supervisor check gives its result; 9.4 merges by squash through its own pull request by criterion 2's steps, and `main`'s CI and Security runs on the squash commit succeed.

Inputs:
- The `gh` account: every `gh` write and every read-back runs as the repository owner, `GH_TOKEN="$(gh auth token --user <owner>)" gh …`, as 9.3 states; the token is never printed, logged or written to a file.
- Story branch: `feature/<story>`, `<story>` matching `^[0-9]+\.[0-9]+[a-z]?$` (the story ID shape `merge-story.sh` checks today).
- Branch of a change outside a story (a records pull request, a Small fix, a docs-only change): `feature/<slug>`, `<slug>` matching `^[a-z][a-z0-9.-]{0,49}$`, so it never matches the story shape; the records of a merged story ride in `feature/records-<story>`.
- Pull request base: `main`, or `feature/<base>` for a story stacked on an unmerged story, `<base>` bounded as a story.
- Pull request title: the commit-message convention's subject, one line of at most 72 characters; the detail goes in the body, which becomes the squash commit's message (9.3).
- Head commit for `--match-head-commit`: the full 40-character SHA of the commit whose tree the stages checked (`docs/verification/<story>-build.json`'s diff hash covers that tree), or the head `gh pr update-branch` made from it once its checks pass.
- How the guard reads a Bash command (human decisions v4 and v5, 2026-11-05 and 2026-11-06): as shell words, with Python `shlex` in POSIX mode (quotes and backslashes honoured; `(`, `)`, `;`, `&`, `|` and a newline end a simple command). Redirections (the operator, its fd digits and its target word, POSIX Shell Command Language 2.7) are dropped before the words are read. Every word named `git` or `gh` is read as a possible program, so a wrapper's option value with that name (`env -u gh git push …`) hides nothing; git's `-C`, `-c`, `--git-dir` and `--attr-source` each take a value. A command the guard cannot read fails closed: an unbalanced quote, ANSI-C or locale quoting, a quoted or escaped separator, or text fed to a shell through a pipe (`echo '…' | sh`), a here-string or `source`/`.` of stdin. Such a command that names `git` or `gh` is denied to a subagent and asked in the main session, and a force push in it is still denied to both.
- The stacked force form: one exact argv, `git [-C <path>] push --force-with-lease --force-if-includes origin feature/<story>`, as the whole command (one simple command: no `;`, `&`, `|`, newline, comment, redirection or second command), compared word for word after the shell-word reading above; `<path>` matching `/[A-Za-z0-9._/-]+` with no substitution, one refspec with no `+` and no `:`. A match returns no decision, not `"allow"`, which would also skip prompts that the settings ask for (hooks reference).
- Substitutions (human decision v3, 2026-11-05): a command or process substitution (`$(…)`, a backtick, `<(…)`, `>(…)`) runs a command the guard cannot read. Two exact forms are exempt, because the flow writes them and each only reads: the owner-token prefix `GH_TOKEN=$(gh auth token --user njabulozmnisi)` and `"$(git rev-parse --show-toplevel)"` (build-story's full run). Any other substitution is denied to a subagent, and asked in the main session when the command names `git` or `gh`.
- The closed list of `gh` writes denied to a subagent, each group and subcommand matched with any options between them (`gh pr -R <owner>/<repo> merge`), with or without the owner-token prefix:
  - `gh pr create|edit|merge|update-branch|close|reopen|ready`;
  - `gh api` that writes: a method named by `-X` or `--method`, attached or with `=` (`-XPOST`, `--method=PATCH`), other than `GET` compared case-insensitively, or one implied by `-f`, `-F`, `--field`, `--raw-field` or `--input` without `-X GET`;
  - `gh repo edit|delete|rename|archive|unarchive|sync`, `gh repo deploy-key add|delete`;
  - `gh secret set|delete`, `gh variable set|delete`, `gh alias set|import`;
  - `gh workflow run|enable|disable`.
- `check-ready.sh <story-id>`: unchanged usage; a dependency counts as available when `refs/heads/feature/<dependency>` exists on `origin`.

1. **The `story/**` trigger and the two scripts go** (ad-012 clauses 1, 6, 7; R33)
   **Given** `ci.yml` and `security.yml` with `push: branches: [main, "story/**"]`, and `scripts/push-and-watch.sh` and `scripts/merge-story.sh`
   **When** this story lands
   **Then** both workflows run on `pull_request` and `push` to `main` only (Security also on its schedule), both scripts are deleted, and no tracked file outside the records (`docs/verification/`, `docs/tickets/`, `docs/decisions/`) names `story/**`, `story/<`, `story/[0-9$]`, `push-and-watch.sh`, `merge-story.sh` or `ci-changes.sh` (`git grep`)
   **And** `scripts/check-ready.sh` looks up `refs/heads/feature/<dependency>` (its message and header follow), and `scripts/lane.sh`'s usage comment gives `origin/feature/8.7` as its base-ref example

2. **Every change reaches `main` through its pull request by written steps** (ad-012 clause 6; R33)
   **Given** a story, or a change outside a story, committed on its branch (Inputs) once every stage it needs has passed
   **When** the supervisor ships it
   **Then** the steps are: `git push origin feature/<id>`; `GH_TOKEN=… gh pr create --head feature/<id> --base <base> --title <title> --body <body>`; `gh pr checks <n> --watch --required`; arm only when `gh pr view <n> --json baseRefName` reads `main`, with `GH_TOKEN=… gh pr merge <n> --squash --auto --match-head-commit <sha>`; before any later push to an armed pull request, `gh pr merge <n> --disable-auto`, then re-arm with the new head once its checks pass; if it still reads `OPEN` once its checks pass, `gh pr view <n> --json mergeStateStatus` decides: `BEHIND` takes criterion 3's update, `BLOCKED` waits, `DIRTY` goes to a builder as a fix round; confirm the merge and its squash SHA with `gh pr view <n> --json state,mergeCommit`, then `gh run list --branch main --commit <squash sha> --workflow CI` (and `Security`) and `gh run watch <id> --exit-status`
   **And** a change outside a story takes the same steps and waits for the full checks (every check job runs on every pull request, ad-012 clause 1); the capture, the epic's lows and decisions, a stage's read-back record and the story's `docs/tickets/README.md` row set to `done` (which `check-ready.sh` relies on once the squash deletes `feature/<story>`) ride in one records pull request, `feature/records-<story>`, after its merge

3. **A stacked story rebases and retargets after its base merges** (ad-012 clause 6; R33)
   **Given** a story whose pull request has the base `feature/<base>`
   **When** the base's pull request squash-merges and GitHub deletes `feature/<base>`
   **Then** GitHub retargets the stacked pull request to `main` (*Managing branches within your repository*, Build facts), and the supervisor confirms it with `gh pr view <n> --json baseRefName` (else `gh pr edit <n> --base main`); it takes the base's last head from `gh pr view <base-pr> --json headRefOid`, runs `git fetch origin` and `git rebase --onto origin/main <that sha> feature/<story>` (on a conflict, `git rebase --abort` and a fix round), pushes with the stacked force form (Inputs), and the pull request's checks run again before any merge
   **And** a pull request that is only behind `main` gets `gh pr merge <n> --disable-auto`, then `gh pr update-branch <n>`, with no force push; the local branch then takes the remote head first (`git fetch` and a fast-forward), and the merge is re-armed with the new head once its checks pass, with no other stage re-run; a `mergeStateStatus` of `DIRTY`, or a drift check failing after an update, goes to a builder as a fix round, since the supervisor writes no code

4. **The guard bounds the force push** (ad-012 clause 6, the human's answer on P1; R33)
   **Given** `.claude/hooks/guard.py`
   **When** a Bash command is checked
   **Then** the stacked force form in Inputs, one exact argv with or without `-C <path>`, gets no decision in the main session and is denied to a subagent
   **And** every other simple command, read as Inputs say, whose program is a `git` word and whose subcommand is `push`, with any options between them (`-C <dir>`, `-c <key>=<value>`, `--git-dir=…`, `--attr-source <tree>`) and any redirection dropped, is denied to the main session and to a subagent when it carries a short-option cluster containing `f` (`-f`, `-fu`, `-uf`), `--force`, `--force-with-lease` or `--force-if-includes`, `--mirror`, or a `+` refspec; the force form past any bound in Inputs (a destination refspec such as `feature/9.4:main`, a second refspec, another command after it, a path holding `$(`) is one of those, and so is a force push in text fed to a shell (Inputs)

5. **A subagent is denied the `gh` writes that change the flow or the settings, and every substitution but two** (ad-012 clause 6, the human's answer on P1, human decisions v3 to v5; R33)
   **Given** `.claude/hooks/guard.py`
   **When** a Bash command is checked, read as Inputs say, with or without the owner-token prefix
   **Then** a command from the closed `gh` list in Inputs is denied to a subagent and gets no decision in the main session, except these asks, each asked in the main session and denied to a subagent: a deploy dispatch (`gh workflow run deploy.yml`, as the guard gives it today), and `gh variable set|delete DEPLOY_ENABLED` or a `gh api` write to `actions/variables/DEPLOY_ENABLED`, the name matched case-insensitively (it switches deploy on merge; human decision 2026-11-05)
   **And** a subagent is denied every command or process substitution except the two exact forms in Inputs (`GH_TOKEN=$(gh auth token --user njabulozmnisi)` and `"$(git rev-parse --show-toplevel)"`), and a command the guard cannot read fails closed as Inputs say; a `gh` read (`gh api` with no write form, `gh pr view|checks|list`, `gh run list|watch`) gets no decision for either

6. **A guard-case test proves the guard in `floor-guard`** (pre-build review P9; R33)
   **Given** `scripts/guard.test.sh`, holding each guard case in Supervisor checks with its expected decision
   **When** `floor-guard` runs it beside the other script tests (shellcheck lints it)
   **Then** it feeds each case to `python3 -I .claude/hooks/guard.py` as hook JSON, once with no `agent_type` (the main session) and once as `backend-builder`, and fails naming every case whose decision differs
   **And** each guard break in the builder's `breaks` list flips one guard rule and names this test

7. **The process rules describe the new flow** (ad-012 clause 6, human answer "the commit on main"; R39)
   **Given** the rule diff, one patch the human approves before dispatch: AGENTS.md (Commits; Story flow's last line; "CI's ZAP baseline runs on every push"; "Branch CI stays the full authoritative run"; "green on the story branch"; Dispatch rules' "or branch CI's where CI runs it"), CLAUDE.md (the stacked brief's `story/<base>`; the guard sentence gains the stacked force push and the subagent's `gh` writes), `.claude/skills/review-story/SKILL.md` (the stacked diff base), `.claude/skills/qa-story/SKILL.md` (the ZAP line), `.claude/skills/build-story/SKILL.md` and `.claude/skills/verify-story/SKILL.md` (the breaks and `run-breaks.sh` lines), `docs/process/agentic-sdlc.md` (section 2's sequence and its push paragraph; section 3's docs-only CI box, routed here by the 9.1 pre-build review, and its G1 and G3 lines) and `.claude/hooks/guard.py`
   **When** every 9.4 brief, fix rounds included, applies it first with `git apply --binary --exclude='docs/verification/*'`, so the one full run and the diff hash cover it
   **Then** they say: every change is built on a `feature/` branch and reaches `main` only through its pull request by criterion 2's steps; the stacked steps of criterion 3; the ZAP baseline and the full checks run on every pull request and on `main`; and the replacement invariant, in place of the fast-forward rule: pull-request checks test GitHub's merge of the up-to-date branch into `main`, and `main`'s own run builds, scans and publishes the squash commit
   **And** the standing approvals keep their meaning, with "pushed through `scripts/push-and-watch.sh`" replaced by criterion 2's steps; `main` is never force-pushed, and the ruleset refuses it; the verifier applies the approved patch to the dispatch base in a throwaway copy and compares (`cmp`) the eight rule files with the story worktree, any difference being a high finding, and a change to the patch after dispatch is a delta every stage re-runs

8. **9.4 merges through its own pull request** (ad-012 clause 7; R33)
   **Given** criteria 1 to 7 on `feature/9.4`, with every stage passed and the rule diff approved
   **When** the supervisor ships it by criterion 2's steps (the guard on `main` already gives the main session no prompt on the push and the merge, the human's answer on P1)
   **Then** GitHub squash-merges it once both required checks and the code-scanning rule pass on the up-to-date branch, and deletes `feature/9.4`
   **And** `main`'s CI and Security runs on the squash commit succeed; the main checkout is then clean and on `origin/main` (`git status` shows nothing, `HEAD` equals `origin/main`), because every session's hook runs the main checkout's working copy of `guard.py`; from then on the next story follows the new rules

## Boundaries

- Must not change: what any check job runs, except the guard-case test step in `floor-guard` (criterion 6); the gate jobs, `publish`, `deploy.yml`, the ruleset and settings 9.3 applied, the story flow's stages (pre-build review, builder, reviewer, verifier, security, QA, fix rounds), the standing approvals' scope; the guard's deployed-environment asks, its agent commit, push and merge deny, and its write-path rules.
- No merge queue, no required reviewer, no linear-history rule, no Gitflow branches, no `gh stack` extension (ad-012, Options rejected and clause 6).
- Auto-merge is armed only after every stage has passed; it never replaces a stage (challenge, section 2).
- The guard rule is explainable in two sentences: only actions on a deployed environment ask, and the main session commits, pushes and merges with no prompt, the one lease-checked force push of a stacked story included; every other force push is denied, and a subagent is denied commit, push, merge and the closed list of `gh` writes.

## Supervisor checks

Guard cases, held in criterion 6's test (main session / subagent):
- No decision / denied: the stacked force form, with and without `-C /abs/path`; `git push origin feature/9.4`; `git push origin feature/records-9.3`; `GH_TOKEN="$(gh auth token --user <owner>)" gh pr merge 5 --squash --auto --match-head-commit <40 hex>`; `gh pr merge 5 --squash --auto`; `gh pr -R <owner>/<repo> merge 5 --squash`; `gh pr create --head feature/9.4 --base main`; `gh pr edit 5 --base main`; `gh pr update-branch 5`; `gh api -XPOST …`; `gh api --method=PATCH …`; `gh api -X patch …`; `gh api repos/<owner>/<name>/rulesets --input main.json`; `gh api -X DELETE repos/<owner>/<name>/rulesets/<id>`; `gh api repos/<owner>/<name>/issues -f title=x`; `gh repo edit --enable-merge-commit`; `gh repo deploy-key add key.pub --allow-write`; `gh secret set FORBIDDEN_TERMS --body x`; `gh alias set mm 'pr merge'`; `gh workflow disable Security`; each `gh` case with the owner-token prefix.
- Denied / denied: `--force-with-lease` alone; `-f`; `-fu`; `-uf`; `--mirror`; `+feature/9.4`; the force form with `feature/9.4:main`, with `HEAD:feature/9.5`, with a second refspec, with `feature/9.4xy`, and with a path holding `$(`; the force form followed by `; git push -f origin feature/9.5`, by `&`, by `||`, and by a newline and a second command; `git push origin feature/9.4 && git push -f origin feature/9.5`; `git -C <dir> push --force origin feature/9.4`; `git -c x=y push --force origin feature/9.4`.
- Ask / denied: `gh workflow run deploy.yml`; `gh variable set DEPLOY_ENABLED --body true`; `gh variable delete DEPLOY_ENABLED`; `gh api -X PATCH repos/<owner>/<name>/actions/variables/DEPLOY_ENABLED -f value=true`; `git -C /tmp/$(id) status`; each `gh` case also with the owner-token prefix.
- Ask / denied (cannot be read): `echo 'git push origin feature/9.4' | bash`; `echo 'gh pr merge 5 --squash' | bash -s`; `. /dev/stdin <<< 'gh pr merge 5'`; an unbalanced quote naming `git` or `gh`.
- Denied / denied (added by decisions v4 and v5): `echo 'git push --force origin feature/9.4' | sh`; `. /dev/stdin <<< 'git push -f origin feature/9.4'`; `env -u gh git push --force origin feature/9.4`; `sudo -u git git push -f origin feature/9.4`; `git --attr-source HEAD push --force origin feature/9.4`; `git > /dev/null push --force origin feature/9.4`.
- No decision / denied (added by decisions v3 to v5): `echo $(date)`; `env -u git gh pr merge 5 --squash`; `env -u gh git commit -m x`; `git --attr-source HEAD commit -m x`; `git 2>/dev/null commit -m x`; `gh pr > /tmp/out merge 5`; `gh pr 2>/dev/null merge 5`.
- No decision / no decision: `gh api repos/<owner>/<name>/rulesets/<id>`; `gh pr view 5 --json state`; `gh pr checks 5 --watch --required`; `git rebase --onto origin/main <sha> feature/9.4`; `git apply --binary <patch>`; build-story's full run with `"$(git rev-parse --show-toplevel)/TestResults"`.

Flow:
- Throwaway copy: a story whose `after` names a story with only `story/<id>` on `origin`; `check-ready.sh` reports it not ready. A dependency with `feature/<id>` on `origin` reports ready.
- Scratch chain proving criterion 3, never touching `main`: pull request A (`feature/99.1` into `feature/scratch-root`) and pull request B (`feature/99.2` into `feature/99.1`). Squash A (the ruleset covers only `main`) and GitHub deletes `feature/99.1`; B now targets `feature/scratch-root`. Run criterion 3's steps with `origin/feature/scratch-root` in place of `origin/main`, push with the stacked force form (no prompt), and confirm B's checks run again. Then close B and delete the scratch branches.

## Build facts

- Owner: backend (backend-builder) for `.github/` and `scripts/`, the guard-case test included; the supervisor writes the rule diff, eight rule files (AGENTS.md, CLAUDE.md, the four skills, `docs/process/agentic-sdlc.md`, `.claude/hooks/guard.py`), which the guard leaves outside the builder's paths, and the human approves it.
- Tier: Feature, with a human gate: a rule change (AGENTS.md, Human gates). The reviewer runs the security checklist on the guard and reads `guard.py` in the applied patch.
- Flags: hitl, rule change, security.
- Lane: A, after 9.3; it is the only story in flight from 9.3's merge until it merges.
- Dispatch base: `main` holding the guard change of the human's answer on P1 (`story/guard-rule`), so the patch's context is the guard that runs.
- Before the build (supervisor): the rule diff written in a scratch worktree at the dispatch base and exported with `git diff --binary <base>` as the patch; the main checkout's rule files change only when it takes 9.4's squash commit (criterion 8). The human approves the patch; every brief's first step applies it (criterion 7).
- Mechanisms and sources:
  - GitHub flow: <https://docs.github.com/en/get-started/using-github/github-flow>.
  - Auto-merge and what disables it: <https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/automatically-merging-a-pull-request>.
  - Automatic retargeting when a base branch is deleted: *Managing branches within your repository*, Deleting a branch: <https://docs.github.com/en/pull-requests/how-tos/commit-changes/managing-branches-within-your-repository>.
  - `gh pr create`, `gh pr checks`, `gh pr merge` (`--squash`, `--auto`, `--match-head-commit`, `--disable-auto`), `gh pr view`, `gh pr edit --base`, `gh pr update-branch`, `gh run list`, `gh run watch --exit-status`, `gh api`, `gh repo`, `gh secret`, `gh variable`, `gh alias`, `gh workflow`: <https://cli.github.com/manual/>; flags confirmed with `gh help <command>`; `-R/--repo` is inherited, so it can sit between a group and its subcommand.
  - `--force-with-lease`, `--force-if-includes`, `--mirror` and the `+` refspec: <https://git-scm.com/docs/git-push>; `git rebase --onto`: <https://git-scm.com/docs/git-rebase>.
  - Reading a command as shell words: Python `shlex` in POSIX mode with `punctuation_chars`, <https://docs.python.org/3/library/shlex.html>; quoting, comments and substitutions: the Bash manual, <https://www.gnu.org/software/bash/manual/bash.html#Shell-Syntax>; redirections are not arguments: POSIX Shell Command Language 2.7, <https://pubs.opengroup.org/onlinepubs/9799919799/utilities/V3_chap02.html#tag_19_07>. The fail-closed reading of what `shlex` cannot model has no standard; it denies or asks, never allows.
  - The guard is a Claude Code `PreToolUse` hook already in the repository; `agent_type` marks a subagent or an `--agent` session, and `permissionDecision` values: <https://code.claude.com/docs/en/hooks>.
- Packages: none.
- Touches: `.github/workflows/ci.yml` (the `push` branches; `floor-guard`'s guard-case test step) and `security.yml` (the `push` branches), `scripts/push-and-watch.sh` and `scripts/merge-story.sh` (deleted), `scripts/guard.test.sh` (new), `scripts/check-ready.sh`, `scripts/lane.sh` (comment); the supervisor's rule diff, eight rule files: `AGENTS.md`, `CLAUDE.md`, `.claude/skills/review-story/SKILL.md`, `.claude/skills/qa-story/SKILL.md`, `.claude/skills/build-story/SKILL.md`, `.claude/skills/verify-story/SKILL.md`, `docs/process/agentic-sdlc.md`, `.claude/hooks/guard.py`.

## Pre-build

- Reviewed as part of 9.3: `docs/verification/9.3-prebuild-review.md` (this story was split from 9.3 after that review).
- Sofia, 2026-11-05: folded the `[story]` points of `docs/verification/9.3-prebuild-review.md` that belong to the flow and the guard. P1 (the owner token, Inputs, criteria 2, 5), P3 (the rule diff as a patch applied first, criterion 7), P4 (the bounded force form and the deny list, criterion 4), P5 (`gh api` method forms, `gh repo`, and with their reason `gh variable` and `gh workflow run`, criterion 5), P6 (disable auto-merge before a push, criterion 2), P7 (arm only on base `main`, criterion 2), P10 (`feature/<slug>` and the records pull request, Inputs, criterion 2), P13 (guard cases in a file; `gh help`), P14 (the retargeting citation, criterion 3), P15 (the stacked steps' SHA, fetch, fast-forward and conflict path, criterion 3), P16 (the armed SHA, Inputs), P17 (the grep excludes only records; each named line, criteria 1, 7), P19 (the wait steps, criterion 2), and the 9.1 review's finding 9 (`agentic-sdlc.md`'s docs-only box, criterion 7). Applied the human's answer on P21 (criterion 6). Criteria: 8.
- Reviewed: `docs/verification/9.4-prebuild-review.md` (changes; one decision, P1).
- Sofia, 2026-11-05, answering `docs/verification/9.4-prebuild-review.md`: applied the human's answer on P1 (the guard asks only for actions on a deployed environment; the main session commits, pushes and merges with no prompt; pipeline agents are denied them), which supersedes P21: the exact-form allow list and old criterion 6 are gone, criteria 4 and 5 keep the P1 rule as the flow moves to pull requests, and criterion 8 adds the clean main checkout. Folded: P2 (the rule diff written in a scratch worktree, Build facts), P3 (the human approves the patch; the verifier's `cmp`; the Assumption replaced, criterion 7, Notes), P4 (`--exclude='docs/verification/*'` on every brief, criterion 7), P5 (short-option clusters, `--mirror`, `+` refspecs, any options before `push`, criterion 4), P7 (one closed `gh` list denied to a subagent, options between group and subcommand, `gh secret` and `gh alias` added, Inputs, criterion 5), P8 (the scratch chain, Supervisor checks), P9 (the guard-case test in `floor-guard`, criterion 6), P10 (`--disable-auto` before `update-branch`, criterion 3), P11 (`mergeStateStatus` on `OPEN`; `git rebase --abort` on a conflict, criteria 2, 3), P12 (`story/[0-9$]` in the grep; the ready side, criterion 1, Supervisor checks), P13 (the AGENTS.md Dispatch rules line, the index row, the retargeting URL, criteria 2, 7, Build facts), P15 (the token limit, Notes), P16 (Notes). Dropped by the P1 answer: P6 (the bounds of the two no-prompt forms, which no longer exist; its full match, no-`MULTILINE` and no-`"allow"` parts are kept for the stacked force form, the one remaining exception) and P14 (the `<owner>` literal of the merge form; the `-C <path>` bound is kept for the force form). Criteria: 8.
- Decisions: settled by the human, 2026-11-05 (`docs/verification/epic-9-decisions.md`, 9.4 P1: the guard asks only for actions on a deployed environment; supersedes P21).
- Sofia, 2026-11-06, answering `docs/verification/9.4-review.md` (R2-L7, R3-L3) under the human decisions v3 to v5 (`docs/verification/epic-9-decisions.md`): Inputs read commands as shell words, give the stacked form as one exact argv, and add the substitution exemptions and the fail-closed reading; criteria 4 and 5 follow them, criterion 5 lists the `DEPLOY_ENABLED` asks, the Supervisor checks agree, and the rule files read eight. Criteria: 8.

## References

- parent — docs/tickets/epic-9-github-flow-ci/epic-9-github-flow-ci.md, section Requirements
- decision — docs/decisions/ad-012-github-flow-ci.md, clauses 6, 7 and the human decisions on branching and the commit on main
- decisions — docs/verification/epic-9-decisions.md, 9.4 P1 (supersedes P21) and v3 to v5
- challenge — docs/verification/ad-012-challenge.md, sections 1 to 5
- review — docs/verification/9.3-prebuild-review.md
- review — docs/verification/9.4-prebuild-review.md
- previous story — docs/tickets/epic-9-github-flow-ci/tickets/story-9.3-main-is-protected-by-a-ruleset-and-squash-is-the-only-merge-method.md
- process — AGENTS.md, Commits, Story flow and Human gates; docs/process/agentic-sdlc.md, sections 2 and 3

## Notes

- Risk high (proposed): a guard that lets an agent force-push, merge or change the ruleset, secrets or settings would let unchecked code reach `main`, and a wrong process rule would stall every later story; beyond the criteria, the supervisor runs the scratch chain in Supervisor checks, which first exercises criterion 3 against a real remote.
- Decision: 2026-11-05 (Sofia) — the rule diff joins the builder's change as a patch applied first (9.3 pre-build review, P3, simplest fix), so the builder's one full run and diff hash cover it and no stage re-runs on a late delta.
- Decision: 2026-11-05 (Sofia) — the records of a merged story ride in one records pull request after its merge (P10), not in the next story's diff, so a story's diff hash covers only its own change.
- Decision: 2026-11-05 (Sofia) — under the human's answer on P1, `gh variable set|delete` and `gh workflow run` no longer ask the main session (the 9.3 review's P5 reason is superseded); they stay on the subagent's closed list, and a deploy dispatch keeps the guard's existing ask. Human decision, 2026-11-05: setting or deleting the variable `DEPLOY_ENABLED` asks the main session too, because it switches deploy on merge, a deployed-environment change.
- Decision: 2026-11-05 (Sofia) — the committed guard-case test is taken (9.4 pre-build review, P9, reversing the 9.3-time decline): the guard changed outside any story once already, and the verifier's breaks need a named test to run.
- Decision: 2026-11-05 (Sofia) — the `-C <path>` bound and the full match stay on the stacked force form although the main session needs no prompt: the form is the one exception to the force-push deny, and an unbounded path could carry a second command past it.
- Observed (9.4 pre-build review, P3): a backend-builder's `git apply --binary <patch>` gets no decision, while its `Edit` of `AGENTS.md` is denied, because the guard checks paths only for the Write and Edit tools; criterion 7's `cmp` is what ties the applied rule files to the approved patch.
- Decision: 2026-11-05 (Sofia) — a subagent can still run `gh auth token --user <owner>` and print the owner's token into its transcript (9.4 pre-build review, P15); accepted as a limit of a single-maintainer guard, since a subagent can also read the credential store by other means.
- Decision: 2026-11-05 (Sofia) — the supervisor dispatches 9.4 after this disposition and the human's P1 answer, not on `check-ready.sh` alone, which reads any "Sofia, " line and the `decisions` field (9.4 pre-build review, P16); for the epic retrospective, a disposition line names the review file it answers, as the line above does.
- Decision: 2026-11-05 (Sofia) — GitHub's native stacked pull requests (9.3 pre-build review, noted) would remove criterion 3's manual rebase, but they need the `gh stack` extension or the website, availability here is unverified, and ad-012 clause 6 names the manual rebase; a possible later simplification.
