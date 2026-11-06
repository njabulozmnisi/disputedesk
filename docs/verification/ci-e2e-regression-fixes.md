# ci-e2e-regression: fix round 1

The high and medium findings for the build with diff hash `562c5a2a11fe`.

| Id | Stage | Severity | Finding | Fix |
|---|---|---|---|---|
| R-M1 | reviewer (`ci-e2e-regression-review.md`) | medium | `scripts/regression-paths.list` leaves out `scripts/operations-token.sh`, which the regression project runs (`e2e/tests/operations-read.spec.ts:41`, `operations-actions.spec.ts:200`, `sla-sweep.spec.ts:142`, `scalar-reference.spec.ts:75`). A pull request that changes only that script skips the suite. | Add `scripts/operations-token.sh` to the list and to the spec's trigger table. Add a `scripts/regression-paths.test.sh` check that fails when a `scripts/*.sh` path named anywhere under `e2e/` is not a line of the list. Write the test first and show it failing. |

The verifier passed with lows only. The lows go to `docs/verification/lows.md` at merge.
