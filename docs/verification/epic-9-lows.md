# Epic 9 lows

Low findings from the epic's stages, fixed in one batch per epic (AGENTS.md, Story flow).

| Story | Record | Where | Finding |
|---|---|---|---|
| 9.1 | verify L1 | `scripts/ci-gates.sh` | Needs `yq` v4 on PATH and gives no clear "yq is required" message; `run-breaks.sh` reports every break as FAIL/ERROR without it. |
| 9.1 | review L1 | `scripts/ci-gates.sh:18` | A scalar `needs:` (valid workflow syntax) yields no entries from `.needs[]`, so the step names jobs as missing (fails closed); normalise with `.needs \| ([] + .) \| .[]` or fail with "needs must be a list", and add a test case. |
