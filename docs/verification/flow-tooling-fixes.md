# flow-tooling fixes (round 1, from the review)

Patch: `~/disputedesk-lanes/flow-tooling-r1.patch` on the M1 (base fe8922f; applies cleanly on current main).

1. **High.** The `ci.yml` changes job (about lines 49-55) diffs `github.event.before..sha`, so a docs-only push after a red code push on `story/**` concludes success with every heavy job skipped, and `merge-story.sh` trusts that head run. On `story/**` pushes, diff from `git merge-base origin/main "$AFTER"` to `$AFTER` (fetch enough history), so a story head's run covers the whole story; keep `BEFORE` for `main`. Prove both cases with a stubbed or scripted run.
2. **Medium.** `docs/problems/*` is read by `ProblemCatalogueTests`; treat it as code in the changes job. Use `git diff --name-only --no-renames`.
3. **Medium.** `scripts/run-breaks.sh`: run each break's test once without the break and require a pass, then with the break and require a failure (the mutation-testing baseline); a break whose baseline fails is reported, not counted.
4. **Medium.** `scripts/lane.sh collect` copies `<worktree>/TestResults`, which nothing writes. The builder procedure's suite command writes TRX into `<worktree>/TestResults` (check the result-directory option name against the installed xUnit v3 and Microsoft.Testing.Platform `--help`), and collect copies from there.
5. **Lows, take them:** in `push-and-watch.sh`, add git's `--force-if-includes` beside the existing lease option; in `check-ready.sh`, query `refs/heads/story/$dependency` exactly; add `backend/.config/dotnet-tools.json` to the NuGet cache key; `lane.sh start` with a base ref exits 2 with a message when the worktree or branch already exists.
