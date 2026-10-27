---
name: verify-story
description: Procedure for non-author verification of a built story with GPT-6 Astra through GitHub Copilot CLI, verdict keyed by commit SHA. Used by the verifier agent.
---

# Verify a story

The inputs are the story ID (`<epic>.<story>`, for example `2.3`, as in the story's file name), the story file and the builder's worktree path.

1. `cd` into the worktree. Record `git rev-parse HEAD` and `git status --porcelain` (the builder's work may be uncommitted; the verdict is keyed by HEAD plus a hash of the diff).
2. Run `scripts/verify-with-copilot.sh <story-id> <story-file>`. It asks GPT-6 Astra (GPT-6 Sol if Astra is unavailable) to build, run the tests, check OpenAPI drift and compare the diff against each acceptance criterion, with no write access. It writes `docs/verification/<story-id>-verify.md`.
3. Run `scripts/floor-guard.sh` in the worktree (read-only). Any hit is a failure; add it to `failures`.
4. Read the verdict file. If the script failed for a tooling reason (auth, quota, model unavailable), return `blocked` with the error; never substitute a verdict of the verifier's own.
5. Integration tests take the `testcontainers` lock; the script does this.
6. The verifier is read-only, so it can't reproduce a builder's deliberate breaks or run Docker. Judge the criteria on the code, the tests and the checks that run read-only; report anything that needs a write or Docker as "supervisor to confirm", not as a failure.

## Report

```json
{"status": "pass", "sha": "...", "diff_hash": "...", "verdict_file": "docs/verification/<id>-verify.md", "failures": [], "assumptions": [], "open_questions": [], "conflicts": []}
```
