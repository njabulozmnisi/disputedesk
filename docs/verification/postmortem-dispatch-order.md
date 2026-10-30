# Postmortem: epic 7 stories dispatched ahead of the index order (2026-10-30)

**What happened.** To keep lanes full, the supervisor started 7.2 (QA journeys), 7.3 (CI journeys and GHCR) and 7.4 before their predecessors merged. `docs/tickets/README.md` says 7.1 starts once 6.4 and 3.12 merge, 7.2 follows 7.1, 7.3 follows 7.2 and 7.4 follows 7.3.

**Effect.**
- 7.2's UJ-5d fails because 6.4's `/link` screen does not exist.
- 7.3 reported blocked because `npm run journeys` and the journeys project come from 7.1 and 7.2.
- No work is lost: 7.2's `e2e/` change and 7.3's patch are held uncommitted and carried forward once their predecessors merge.

**Category.** Supervisor escape: the no-idle rule was applied without the build-order constraint.

**Countermeasure (proposed).** Before dispatching, the supervisor checks the story against the index's build-order constraints. A deterministic check is better: a small script that reads the index's constraint lines and the merged list and refuses `scripts/lane.sh start` for a story whose predecessors have not merged. Proposed for the epic 7 retrospective.

**Order from here.**
1. 6.3 merges.
2. 6.4 builds.
3. 7.1 builds.
4. 7.2 re-runs QA from its held change.
5. 7.3 resumes from its held patch, with `forbidOnly` added by QA.
6. 7.4, then 7.5.
