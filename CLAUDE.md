# CLAUDE.md

@AGENTS.md

## The main session is the supervisor

It plans, dispatches, tracks and merges. It never writes production or test code (the guard hook enforces this). Gates require human approval.

## Commits

Nothing is committed, pushed or merged without explicit approval for that change. The guard hook turns every `git commit`, `push` and `merge` into a permission prompt. Stage the change, show what goes in, wait.

## Story flow

```
challenger → builder → verifier → security (high-stakes only) ∥ reviewer → QA → merge
```

The challenger (`scripts/challenge-with-copilot.sh`, a different model family from the builders) reads only the story, the architecture and the code it would touch, never the supervisor's view, and answers proceed, simplify or rethink. Simplify or rethink is a model disagreement, so a human decides before the builder starts. Security and the reviewer are read-only and run in parallel; QA, the most expensive stage, runs last, once their findings are fixed. For high-stakes stories the human review of the diff (AGENTS.md stakes tiers) happens in the review blocks, before the commit is approved.

- **One supervisor session per story.** Start from `docs/tickets/README.md`, the story file and its `docs/verification/<story>-*.md`; hand over in files, not in the conversation; clear the session after the merge and the capture below.
- **Fix rounds.** Collect every stage's findings for one commit into `docs/verification/<story>-fixes.md` and give them to one fresh builder, not a resumed one. At most one fix round per stage pass.
- **Re-runs after a fix.** The verifier always re-runs; the reviewer reviews only the delta; security re-runs when the delta touches auth, identity, customer data or security headers; QA re-runs when the HTTP surface, data or events change. CI stays the full authoritative run.
- **One test run per commit.** The builder's full run is the authoritative result for its diff; later stages reuse it and re-run only after a change.
- **Research.** One agent per question, with a scope and an output file; parallel audits split by file set with one merge owner; no fan-out to more models than the verifier and its backup.
- **A rule change mid-story** applies from the next story unless the human decides to rework the current one.

Stories run in the order of [docs/tickets/README.md](docs/tickets/README.md); `<story>` below is the story ID `<epic>.<story>` (AGENTS.md, Ticket numbering). Keep the index current when a story merges or the plan changes.

Merge only when every stage passes and CI is green. Agents are in `.claude/agents/`; each file sets its model ([ad-004](docs/decisions/ad-004-model-roles.md)). If usage strains, drop to one Opus lane.

## Dispatch rules

- Two builder lanes at most, one Testcontainers slot, one verifier story at a time, one QA run at a time behind `scripts/test-lock.sh`.
- Sized for the build machine: 16 GB RAM, 12 cores, Docker VM capped at 8 GB. A backend lane peaks near 2 GB on the host (build plus the full suite); the Compose stack can take up to 5.8 GB of the VM, so only one stack runs at a time.
- Memory gate: dispatch a new lane only while `sysctl -n kern.memorystatus_vm_pressure_level` prints `1` (normal); on `2` or `4`, wait for a lane to finish. Read-only stages (reviewer, security, challenger, verifier) cost little locally and may run beside a builder.
- Stop the Compose stack (`docker compose stop`) whenever no one holds the `stack` lock.
- The story (a vertical slice) is the parallel unit. Start sequentially (platform baseline, then the tracer bullet), run the middle in parallel, finish with a sequential refactor sweep.
- Collision check before dispatching two stories together: no shared files, never two migrations at once.
- Frontend work starts once the API contract (`openapi.json`) is merged.
- The supervisor reads reviewer and security findings directly and writes them to `docs/verification/<story>-review.md` and `<story>-security.md`.
- Story files given to the verifier hold only criteria it can check read-only. The supervisor reproduces the builder's deliberate breaks and any Docker checks in a throwaway copy, and records the result in `<story>-review.md`.

## Continuous improvement

Claude learns only from what is written down where the right agent loads it.

- **After every merge (capture).** Add an Escapes table to `<story>-review.md`: each finding a later stage caught that an earlier stage should have, with the stage that caught it, the stage that should have, a category (spec gap, input boundary, non-standard mechanism, missing test, environment or tooling, flaky, script error, auth) and the countermeasure. Add one metrics row: fix rounds per stage, full-suite runs, wall time per stage, tokens and peak context per agent, disk change.
- **After every epic (retrospective).** Aggregate the epic's escapes and metrics, propose countermeasures to the human as one batch, and prune stale skill lines.
- **Immediately (trigger).** A recurring escape category, a second fix round, a stage failing for tooling (account, lock, disk), a full pipeline re-run, a supervisor context over 200k tokens, or free disk under 15 GB: write a short postmortem and a fix proposal at once.
- **A repeated mistake becomes a check, not a sentence.** Countermeasures in this order: a deterministic check (guard, analyzer, architecture test, floor-guard, script exit code); one line in the responsible agent's skill, under "Known pitfalls", at most about 10 lines, deleted once a check covers it; a field in the story template. Never in this file or AGENTS.md.
- **Machine hygiene.** After every merge, remove merged clean worktrees and their branches and the session's throwaway copies; weekly, or under 15 GB free, prune only DisputeDesk-scoped Docker leftovers (the Docker daemon is shared with other projects).

## Human gates

Escalate for a human decision only for: spec ambiguity or a re-plan, two failed fix attempts, irreversible actions, rule changes, high-severity security findings, model disagreement, and the token-usage threshold.

## Planning

Planning runs through the BMAD skills in `.claude/skills/` (brief, PRD, UX, architecture, Sofia), configured in `_bmad/`. Grilling uses the `grill` skill. See [docs/process/agentic-sdlc.md](docs/process/agentic-sdlc.md).
