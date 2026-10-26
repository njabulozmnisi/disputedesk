# Review: P0-4

- Reviewer: `reviewer` agent (Claude Opus 5.5), read-only, two passes. First verdict: changes; fixed.
- Verifier (GPT-6 Astra, second run, diff hash `0985aa6ba5a3`): AC1-AC4 proved (build, 59 tests, format, OpenAPI drift, floor guard, actionlint, Trivy filesystem, gitleaks). Verdict recorded as FAIL only because its sandbox cannot run Docker: the image scans and `docker compose config` were refused, and the read-only verifier cannot reproduce deliberate breaks.

## Supervisor evidence for the checks the verifier sandbox cannot run (2026-10-26, in the builder's worktree)

| Check | Result |
|---|---|
| `scripts/floor-guard.sh` on the clean tree | clean, exit 0 |
| Floor guard on a planted `#pragma warning disable` and `DateTime.UtcNow` (throwaway clone) | both reported with file:line, exit 1 |
| `actionlint .github/workflows/*.yml` | clean, exit 0 |
| `docker compose config --quiet` | valid |
| `trivy fs` (vuln, misconfig, secret; HIGH/CRITICAL; trivyignore) | exit 0 |
| `trivy image` on the api and worker images built from `backend/Dockerfile` | both exit 0 (no HIGH/CRITICAL) |
| CodeQL (`build-mode: none`) | not runnable locally; the first CI run is its test |

## Review findings

| # | Severity | Finding | Resolution |
|---|---|---|---|
| 1 | Medium | NuGetAudit + warnings-as-errors failed restore on any advisory severity, so the `dotnet list --vulnerable` + jq step could never run | `NuGetAuditLevel=high` in `backend/Directory.Build.props`; the job is a plain `dotnet restore`. Proved: Moderate restores, direct and transitive High fail with NU1903 |
| 2 | Low | Concurrency comment claimed main runs are never cancelled | Group `workflow-head_ref\|\|run_id` with cancel-in-progress: every push gets its own run |
| 3 | Low | CodeQL and these jobs only block merges as required checks | Comment at the top of `security.yml`; the branch ruleset is configured in the repository settings |
| 4 | Low | Floor guard omitted banned packages and warnings-as-errors switches | Two rules added; 8 planted hits reported, exit 1 |
| 5 | Nit | `rm -f` of the terms file | Kept, with a comment (self-hosted runners keep files) |
