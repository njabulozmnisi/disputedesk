# The fixtures file moves to backend/fixtures/ (Small tier)

Slice of decision record `docs/decisions/ad-011-team-folders-in-one-repo.md` (accepted 2026-11-03), change 2. `fixtures/local.json` becomes part of the backend folder's published contract. The data does not change.

## Criteria

1. **Move.** `git mv fixtures backend/fixtures` (the file and its `README.md`).
2. **Readers in your paths.** `docker-compose.yml` (provision, core-banking, api mounts), the stub (`Fixtures.cs`, `Program.cs`, `appsettings.json`), the seeder (`SeedOptions.cs`, `Seeder.cs`, `Fixtures.cs`), `backend/Dockerfile`'s comment, `scripts/dev-login.sh`, and every backend test reading the file (`FixturesFile.cs`, `CoreBankingStubFactory.cs`, `SeedFixtureTests.cs`, `SeededDisputesTests.cs`, `LinkingFakes.cs`, and any `PlatformScriptTests` case) use `backend/fixtures/local.json`.
3. **Readers outside your paths.** `e2e/tests/linking-screens.spec.ts`, `e2e/tests/sla-sweep.spec.ts` and `e2e/run-regression.sh` belong to QA; `docs/`, the root `README.md` and `.claude/` (the guard's `fixtures/` entry) belong to the supervisor. List each in the report with the line to change.
4. **`backend/README.md`.** The team folder's entry point, short: what the folder is, how to build and test, what it publishes (`openapi.json`, the images, `fixtures/local.json`), and the root files it reads (`contracts/events/`, `docs/problems/`). Link the root `AGENTS.md` and `docs/conventions/backend.md`.
5. **No old path left.** `git grep -nE '(^|[^/])fixtures/local\.json'` finds no reader of the old path in your write paths.
6. **Checks.** The full backend suite once; `docker compose config` renders; `shellcheck scripts/dev-login.sh`.

## Out of scope

The fixture content, `contracts/events/` (stays at the root: the event `dataschema` URLs name it).
