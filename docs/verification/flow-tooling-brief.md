# Faster-flow tooling (Small tier)

The human approved the eight pipeline recommendations on 2026-11-04 (`~/disputedesk-research/research_notes/pipeline-speed/recommendations.md`). The rule text is drafted and lands with this change; this brief is the tooling it needs. Every change is in `scripts/` or `.github/`; no product code changes.

## Criteria

1. **CI runs on story branches.** `ci.yml` and `security.yml` push triggers include `story/**` besides `main`; `publish` and the OIDC jobs stay limited to `main` (they already check `github.ref`). Prove with actionlint and by reading the trigger and job conditions.
2. **Docs-only pushes skip the heavy jobs.** A push whose changed files are all `docs/**`, `*.md` or `_bmad/**` skips `backend`, `frontend`, `platform`, `terraform` and `e2e` in `ci.yml` (GitHub's documented `paths-ignore` cannot skip single jobs, so use one `changes` job that sets an output, read by each job's `if:`; or `dorny/paths-filter` pinned by commit SHA after verification). `floor-guard` (gitleaks, forbidden terms) always runs, and `security.yml` still produces a run on every push so `push-and-watch.sh` never waits for a run that will not come. A job skipped by its `if:` counts as success for `needs:` only where `publish` does not need it: `publish` still requires every job on `main` pushes that change code.
3. **Caches.** `actions/setup-dotnet` with NuGet caching (needs `packages.lock.json`: if the repo has none, use `actions/cache` keyed on `**/*.csproj` and `Directory.Packages.props` instead, and say which) and `actions/setup-node` with `cache: npm` keyed on each `package-lock.json`. Same pinned action SHAs as today.
4. **Merge helper.** `scripts/merge-story.sh <story>`: refuses unless `main` fast-forwards to `story/<story>` and the branch head's CI and Security runs both concluded success (`gh run list --branch story/<story> --commit <sha>`); then fast-forwards `main`, pushes it, and runs `push-and-watch.sh` for main's run (which publishes). Exit 0 merged, 1 refused (says why), 2 tooling.
5. **`push-and-watch.sh` on a branch.** Accepts the branch to watch (default the current branch), so a story branch push is watched the same way; story branches may be pushed with `--force-with-lease` after a rebase, never `main`.
6. **Breaks runner.** `scripts/run-breaks.sh <worktree> <build.json>`: for each entry of the builder's `breaks` list (`patch`, `test_method`, `expect`), applies the patch in a throwaway copy of the worktree, runs that test (dotnet filter under `scripts/test-lock.sh testcontainers`, or the named shell command), checks the expected failure, reverts, and prints one line per break; exit 0 when every break failed as expected.
7. **Diff hash.** `scripts/diff-hash.sh <worktree> <base>` prints the fingerprint `verify-with-copilot.sh` uses today (extract it, do not invent a new one); `verify-with-copilot.sh` calls it.
8. **Lanes.** `lane.sh start` refuses when the host has under 15 GB free (`df`), takes an optional base ref so a lane can stack on `story/<base>`, and `collect` also brings back the worktree's `TestResults/` and `docs/verification/<story>-build.json` (untracked, so the patch does not carry them). `check-ready.sh` treats an `after` story as satisfied when it is done, or when `story/<id>` exists on origin (stacking), and stops expecting `-challenge.md` and `-prebuild-security.md`.

## Checks

actionlint and shellcheck (both now in CI), `bash -n`, a stubbed run for each new script mode (exit codes), and one real `push-and-watch.sh` on a throwaway `story/flow-tooling-check` branch is done by the supervisor after merge.

## Out of scope

`verify-with-copilot.sh`'s prompt rewrite beyond calling `diff-hash.sh` (the verifier's procedure change lands as docs), any product code, Testcontainers container sharing (a later story).
