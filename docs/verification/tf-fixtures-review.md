# tf-fixtures review (Small tier, ad-011 change 2)

- Builder: built; `fixtures/` moved to `backend/fixtures/`; Compose host mounts, the stub's `FixturesPath`, `scripts/dev-login.sh`, the test helper (`FixturesFile.FindPath()` via `RepoRoot`) and three tests that found the repo root through the fixtures folder (`PublishMappingTests`, `RaisedEventTests`, `SharedJsonOptionsTests`) updated; `backend/.dockerignore` keeps `fixtures/` out of the build context; `backend/README.md` new. Full suite 1624 of 1624 (TRX). Smoke on the stack: provision exited 0, dev-login signed in linked, transactions 200 (20), disputes 200 (7), 8 Disputes and 2 CustomerLinks rows match the fixtures.
- QA: the 10 e2e readers moved to the new path; `playwright test --list` parses all 33 specs (269 tests). CI runs the journeys.
- Reviewer: approve. One low (README omitted CI's two drift checks) and two nits (root files the tests read; `FindPath()` name kept).
- Fix round 1 (fresh builder): README only; supervisor read the result.
- Supervisor: root README, architecture (AD-21 line, AD-30, source tree), onboarding, PRD path note and production-path updated. `.claude/hooks/guard.py` and `.claude/agents/backend-builder.md` still list `fixtures/`; `backend/` covers the new path, so both are dead but harmless (left for a batched process change).

## Escapes

| Finding | Caught by | Should have been | Category | Countermeasure |
|---|---|---|---|---|
| Brief listed 3 e2e readers; there are 10, and named `LinkingFakes.cs`, which reads nothing | builder | supervisor writing the brief | spec gap | the brief's reader list comes from `git grep`, pasted, not from memory (second time this epic) |
| Three tests located the repo root through `fixtures/` and broke on the move | builder (red run) | brief | spec gap | same as above: grep for the folder name, not only the file path |

## Metrics

| Fix rounds | Full-suite runs | Wall time | Disk change |
|---|---|---|---|
| 1 | 1 (builder) | builder 13 min, QA 1 min, reviewer 4 min, fix 1 min | worktrees removed after merge |
