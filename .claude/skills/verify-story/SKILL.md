---
name: verify-story
description: Procedure for non-author verification of a built story with GPT-6 Astra through GitHub Copilot CLI, verdict keyed by commit SHA. Used by the verifier agent.
---

# Verify a story

You are given the story ID, the story file and the builder's worktree path.

1. `cd` into the worktree. Record `git rev-parse HEAD` and `git status --porcelain` (the builder's work may be uncommitted; the verdict is keyed by HEAD plus a hash of the diff).
2. Run `scripts/verify-with-copilot.sh <story-id> <story-file>`. It asks GPT-6 Astra (GPT-6 Sol if Astra is unavailable) to build, run the tests, check OpenAPI drift and compare the diff against each acceptance criterion, with no write access. It writes `docs/verification/<story-id>-verify.md`.
3. Read the verdict file. If the script failed for a tooling reason (auth, quota, model unavailable), return `blocked` with the error; never substitute your own verdict.
4. Integration tests take the `testcontainers` lock; the script does this.

## Report

```json
{"status": "pass", "sha": "...", "diff_hash": "...", "verdict_file": "docs/verification/<id>-verify.md", "failures": [], "assumptions": [], "open_questions": [], "conflicts": []}
```
