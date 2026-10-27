---
name: review-story
description: Procedure for a read-only two-pass review (correctness, then simplicity) of one story diff. Used by the reviewer agent.
---

# Review a story

Diff: `git diff main...HEAD` plus any uncommitted changes in the worktree.

## Pass 1: correctness

- Each acceptance criterion is proved by a test that would fail without the change.
- Every `Location` header points to a real GET endpoint, backed by an integration test per create endpoint.
- No exact-equality timestamp assertions; a fixed `TimeProvider` or a tolerance.
- Expected failures return `Result` errors mapped to ProblemDetails exactly as architecture AD-3 maps them (400 malformed input, 404 not found or owned by another customer, 409 action not allowed or conflict, 422 business rule broken, 503 dependency unavailable); exceptions only for the unexpected.
- Ownership is enforced by the global query filter; the customer comes from the identity link, never the request.
- Outbox row written in the same transaction as the change.
- No null-forgiving `!` in production code.

## Pass 2: simplicity

"Is there a smaller design that meets the criteria? Does every abstraction earn its place? Would a developer new to the repo understand each method on first read?" The bar is stupid simple and logical (AGENTS.md); clever or indirect code is a finding even when it works. Flag single-implementation interfaces, speculative options, wrappers over framework features, and anything that belongs in `docs/production-path.md` instead.

Then the standards check (AGENTS.md, "Use established standards; do not invent mechanisms"): for each mechanism the diff adds, name the documented framework mechanism or industry standard it should use (Microsoft Learn, the RFC, OWASP) and flag any home-grown replacement, with the link. Flag any part that can't be explained in a sentence or two.

## Report

Rank findings by severity with file and line.

```json
{"status": "approve", "findings": [{"severity": "high", "file": "...", "line": 0, "issue": "...", "fix": "..."}], "assumptions": [], "open_questions": [], "conflicts": []}
```
