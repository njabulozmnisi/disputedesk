# Epic 9 lows

Low findings from the epic's stages, fixed in one batch per epic (AGENTS.md, Story flow).

| Story | Record | Where | Finding |
|---|---|---|---|
| 9.1 | verify L1 | `scripts/ci-gates.sh` | Needs `yq` v4 on PATH and gives no clear "yq is required" message; `run-breaks.sh` reports every break as FAIL/ERROR without it. |
| 9.1 | review L1 | `scripts/ci-gates.sh:18` | A scalar `needs:` (valid workflow syntax) yields no entries from `.needs[]`, so the step names jobs as missing (fails closed); normalise with `.needs \| ([] + .) \| .[]` or fail with "needs must be a list", and add a test case. |
| 9.2 | security L1 | `scripts/deploy-workflow.test.sh:923`, `.github/workflows/` | The one-holder pin counts `id-token` only in `ci.yml` and nothing limits `environment: dev` to `deploy.yml`'s job; a later `pull_request_target` or `workflow_run` workflow naming `dev` would get the trusted subject. Extend the pin to every workflow file (P55's `job_workflow_ref` is the full fix). |
| 9.2 | verify L1 | `9.2-build.json` `test_results.files` | Lists two deliberate failing negative runs (test-before, old floor-guard arguments) among the results; keep them apart from `test_results.files`. |
| 9.2 | verify L2 | AC3, AC5, AC7 | No automated test: GitHub-side run on main, the human environment step, README text. |
| 9.2 | review L1 | `scripts/deploy-workflow.test.sh:110,115,119,163,175` | Five new `# shellcheck disable=SC2016` lines carry no written reason; add the reason (or one file-wide directive with it). |
| 9.2 | review L2 | `scripts/deploy-workflow.test.sh:43` | Extracted steps run with `bash --noprofile --norc -eo pipefail`, but GitHub runs them as `bash -e {0}`; run them with `bash -e` or set `shell: bash` on them. |
