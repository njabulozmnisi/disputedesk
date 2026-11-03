# tf-platform-tests review (Small tier, ad-011 change 4)

- Builder: built. 24 of 37 `PlatformScriptTests` cases (every case reading a file under `infra/`) moved unchanged to `infra/tests/DisputeDesk.PlatformTests/`; 13 stay in backend. Counts from the test runner: before 37, after 24 + 13, sorted union equals the before list. The infra project has its own csproj (same packages and exact versions, same build and analyser settings), `global.json` and `.editorconfig` copied from backend and marked "change together", no reference to `backend/`. CI's `platform` job gains `setup-dotnet` and the test step, run from `infra/tests` (from the root `dotnet test` falls back to VSTest mode). Full backend suite 1600 of 1600 (TRX); infra tests 24 of 24 on macOS and in the Linux SDK container (curl 8.5.0); deliberate break fails the run.
- Reviewer: changes, one medium: the platform job ran no `dotnet format` on `infra/tests`, so the copied IDE and whitespace rules were unenforced. One low (the worktree base was the reverted 1c98ed6; landed as a patch on 24405e3) and two nits.
- Fix round 1 (fresh builder): both format steps added; the "(below)" comment names the backend case; a second deliberate break (`Compose_Api_...`) fails the run and passes after revert.
- `ApiReferenceTests` stays in backend as the second accepted exception (ad-011 change 4).

## Escapes

| Finding | Caught by | Should have been | Category | Countermeasure |
|---|---|---|---|---|
| Copied style rules not enforced in CI for the new project | reviewer | builder | missing test | none: one-off |
| Brief's `dotnet test infra/tests/...` from the root does not work under MTP | builder | supervisor (brief) | spec gap | none: one-off |

## Metrics

| Fix rounds | Full-suite runs | Wall time | Disk change |
|---|---|---|---|
| 1 | 1 (builder) | builder 14 min, reviewer 3 min, fix 2 min | worktrees removed after merge |
