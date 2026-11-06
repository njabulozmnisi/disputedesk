# CLAUDE.md

@AGENTS.md

Every rule is in AGENTS.md. This file adds only how Claude Code carries those rules out.

## Supervisor

The main session is the supervisor (AGENTS.md, Roles). The guard hook (`.claude/hooks/guard.py`) enforces the role: it blocks the supervisor from writing production or test code, limits each agent's writes to its paths, denies every force push except a stacked story's lease-checked push in the main session (AGENTS.md, Commits), keeps pipeline agents from committing, pushing or merging and from the closed list of `gh` writes that change pull requests, repository settings, secrets, variables, aliases or workflows, turns actions on a deployed environment (`terraform apply`, `destroy` and `import`, any `aws` call, a deploy, and setting or deleting `DEPLOY_ENABLED`) into a permission prompt, and fails closed on a command substitution (denied to a pipeline agent; a prompt in the main session when the command names `git` or `gh`, as for a command it cannot read; the owner-token prefix and `$(git rev-parse --show-toplevel)` are exempt). `.claude/hooks/status_contract.py` checks that every pipeline agent ends with the status contract.

## Agents and skills

- Pipeline agents are in `.claude/agents/`; each file sets its model ([ad-004](docs/process/ad-004-model-roles.md)). If usage strains, drop to one Opus lane.
- Each agent runs its procedure skill in `.claude/skills/` (`build-story`, `verify-story`, `qa-story`, `review-story`, `security-story`); planning uses the BMAD skills there (brief, PRD, UX, architecture, Sofia) and the `grill` skill.
- A builder dispatched with the Agent tool gets its own isolated worktree from the current `HEAD`, so a fix-round brief starts by applying the patch from `scripts/story-patch.sh`, and a stacked story's brief starts with `git checkout --detach feature/<base>` in that clean worktree.
- The reviewer, security agent and verifier briefs give the main checkout's absolute `docs/verification/` path for their records.
