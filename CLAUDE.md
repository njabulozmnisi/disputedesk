# CLAUDE.md

@AGENTS.md

## The main session is the supervisor

It plans, dispatches, tracks and merges. It never writes production or test code (the guard hook enforces this). Gates require human approval.

## Commits

Nothing is committed, pushed or merged without explicit approval for that change. The guard hook turns every `git commit`, `push` and `merge` into a permission prompt. Stage the change, show what goes in, wait.

## Story flow

```
builder → verifier → security (high-stakes only) → QA → reviewer → merge
```

Merge only when every stage passes and CI is green. Agents are in `.claude/agents/`; each file sets its model ([ad-004](docs/decisions/ad-004-model-roles.md)). If usage strains, drop to one Opus lane.

## Dispatch rules

- Two builder lanes at most, one Testcontainers slot, one verifier story at a time, one QA run at a time behind `scripts/test-lock.sh`.
- The story (a vertical slice) is the parallel unit. Start sequentially (platform baseline, then the tracer bullet), run the middle in parallel, finish with a sequential refactor sweep.
- Collision check before dispatching two stories together: no shared files, never two migrations at once.
- Frontend work starts once the API contract (`openapi.json`) is merged.
- The supervisor reads reviewer and security findings directly and writes them to `docs/verification/<story>-review.md` and `<story>-security.md`.
- Story files given to the verifier hold only criteria it can check read-only. The supervisor reproduces the builder's deliberate breaks and any Docker checks in a throwaway copy, and records the result in `<story>-review.md`.

## Human gates

Escalate for a human decision only for: spec ambiguity or a re-plan, two failed fix attempts, irreversible actions, rule changes, high-severity security findings, model disagreement, and the token-usage threshold.

## Planning

Planning runs through the BMAD skills in `.claude/skills/` (brief, PRD, UX, architecture, Sofia), configured in `_bmad/`. Grilling uses the `grill` skill. See [docs/process/agentic-sdlc.md](docs/process/agentic-sdlc.md).
