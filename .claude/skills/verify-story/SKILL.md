---
name: verify-story
description: Procedure for non-author verification of a built story with GPT-6 Astra through GitHub Copilot CLI, reading the builder's test results and running its breaks list, findings keyed by commit SHA. Used by the verifier agent.
---

# Verify a story

The inputs are the story ID (`<epic>.<story>`, for example `2.3`, as in the story's file name), the story file, the builder's worktree path and the main checkout's `docs/verification/` path.

1. `cd` into the worktree. Record `git rev-parse HEAD` and `git status --porcelain` (the builder's work may be uncommitted; the record is keyed by HEAD plus a hash of the diff).
2. Read `docs/verification/<story>-build.json` in the worktree. Its `test_results.diff_hash` must equal `scripts/diff-hash.sh` now; a mismatch, a missing results file or a failed test is a high finding. Do not re-run the suite.
3. Run `scripts/verify-with-copilot.sh <story-id> <story-file>`. It gives GPT-6 Astra (GPT-6 Sol if Astra is unavailable) the results files and asks it, with no write access, to compare the diff against each acceptance criterion, name the test that proves each, and list non-standard mechanisms. It writes `<story-id>-verify.md` in the main checkout's `docs/verification/`.
4. Run `scripts/run-breaks.sh <worktree> <build.json>`: each break is applied in a throwaway copy, its named test is run under the `testcontainers` lock and must fail. A break whose test passes is a high finding; the script adds the results to the record.
5. Run `scripts/floor-guard.sh` in the worktree (read-only). Any hit is a high finding.
6. If a script failed for a tooling reason (auth, quota, model unavailable), return `blocked` with the error; never substitute findings of the verifier's own.
7. Report findings only, ranked high, medium or low, as correctness gaps against the criteria; style is not a finding. A check that needs the running stack is QA's or CI's: name it, don't fail it.

On a re-run after a fix, step 2 reads the fix builder's new results file, step 4 runs its breaks, and the new result is appended to the same record as a dated section.

## Report

```json
{"status": "pass", "sha": "...", "diff_hash": "...", "record": "docs/verification/<id>-verify.md", "findings": [{"severity": "high", "criterion": "AC2", "issue": "...", "evidence": "..."}], "assumptions": [], "open_questions": [], "conflicts": []}
```

`status` is `pass` when no high or medium finding remains, `findings` otherwise, `blocked` for tooling.
