# CLAUDE.md

@AGENTS.md

Every rule is in AGENTS.md. This file adds only how Claude Code carries those rules out.

## Supervisor

The main session is the supervisor (AGENTS.md, Roles). The guard hook (`.claude/hooks/guard.py`) enforces the role: it blocks the supervisor from writing production or test code, limits each agent's writes to its paths, and turns every `git commit`, `push` and `merge` into a permission prompt. `.claude/hooks/status_contract.py` checks that every pipeline agent ends with the status contract.

## Agents and skills

- Pipeline agents are in `.claude/agents/`; each file sets its model ([ad-004](docs/decisions/ad-004-model-roles.md)). If usage strains, drop to one Opus lane.
- Each agent runs its procedure skill in `.claude/skills/` (`build-story`, `verify-story`, `qa-story`, `review-story`, `security-story`); planning uses the BMAD skills there (brief, PRD, UX, architecture, Sofia) and the `grill` skill.
- A builder dispatched with the Agent tool gets its own isolated worktree from the current `HEAD`, so a fix-round brief starts by applying the patch from `scripts/story-patch.sh`.
