# Verification: ui-detail-start

- Commit: `2f897c37b3b58b11119c6126123213f98defeb43` (uncommitted diff hash `e4888ece9a39`)
- Verifier: gpt-6-astra via GitHub Copilot CLI
- Date (UTC): 2026-11-08T16:28:15Z

The changes are frontend-only, including two uncommitted prerequisite patches. The build could not start because dependencies are missing; I’ll restore them and check the recorded test results without running the suites.


VERDICT: FAIL

| Criterion | Test | Proved (yes/no) |
|---|---|---|
| AC1: Disputed date and row order | `DisputeDetailScreen.test.tsx`: “shows Disputed on from createdAt…”; “runs the summary rows…” | yes |
| AC2: Withdraw consequence and placement | Status parameterization; “puts the sentence and the button at the card foot…” | yes |
| AC3: Static success notice | “shows the ‘Dispute submitted’ banner, not as a live region…” | yes |
| AC4: Timeline labels, ordering and unknown values | Timeline ordering, dots, surface, unknown-value and promise tests | yes |
| AC5: Separate rejected guidance | “shows the Rejected guidance as its own surface with two steps…” | yes |
| AC6: Skeleton and shared heading | Loading skeleton and loading accessibility tests | yes |
| AC7: Responsive withdraw dialog | “has a hairline above the buttons, a phone sheet and a height cap”; focus and withdrawal tests | yes |
| AC8: Start assurance | `routing.test.tsx`: “shows the assurance caption with a decorative Lock…” | yes |
| AC9: Wordmark assets | `wordmark.test.ts`: dimensions, metadata, transparency and trailing-dot tests | yes |

**Failure — verification blocked:** Execution of the diff-fingerprint check was denied. The builder records `e4888ece9a39`, but I could not independently confirm that the recorded results cover this exact worktree. No code correctness defect was identified.

Both builds, backend style/whitespace, frontend typecheck/lint/format, and OpenAPI drift checks passed. The listed JUnit file contains **731 tests, zero failures/errors/skips**; no suites were rerun. No changed shell scripts or platform tests require checks. Browser layout and hosted-logo provisioning remain QA-owned, outside these code-only proofs.

## Standards

None.

## Verifier's own checks (2026-11-08)

- HEAD 2f897c37b3b58b11119c6126123213f98defeb43; scripts/diff-hash.sh on the builder's worktree gives e4888ece9a39, equal to test_results.diff_hash in ui-detail-start-build.json. The results file TestResults/ui-detail-start-vitest.xml reports 731 tests, 0 failures, 0 errors. Suite not re-run.
- The Copilot FAIL above is a tooling artefact, not a code finding: the model's sandbox denied scripts/diff-hash.sh, so it could not confirm the hash itself. It found no code defect, and AC1 to AC9 are each proved by a named test. The hash was confirmed here, so the failure is set aside. The Copilot run was made in a throwaway copy (same hash e4888ece9a39); the builder's worktree was not written.
- scripts/run-breaks.sh on the builder's worktree: all 11 breaks (including the AC9 binary PNG reverse patch) failed as expected, each passing without the break. Exit 0.
- scripts/floor-guard.sh in the worktree: clean.
- Non-standard mechanisms: none reported. lucide-react counted as the base's, as instructed.
- QA-owned, not failed: browser layout and hosted logo provisioning.

Findings: none.
