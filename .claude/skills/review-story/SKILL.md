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
- A dispute change is one stored event, appended by an aggregate method, written with its `Disputes` read-model row and (if the publish mapping maps its type) its outbox row in one transaction; stored event ID = outbox ID; no audit row for a successful change (ad-009, AD-14).
- Disputes load by replaying their stored events; a version clash reloads and decides once more, 409 only on a second clash or a forbidding status (AD-15).
- No null-forgiving `!` in production code.

## Pass 2: simplicity

"Is there a smaller design that meets the criteria? Does every abstraction earn its place? Would a developer new to the repo understand each method on first read?" The bar is stupid simple and logical (AGENTS.md); clever or indirect code is a finding even when it works. Flag single-implementation interfaces, speculative options, wrappers over framework features, and anything that belongs in `docs/production-path.md` instead.

Then the standards check (AGENTS.md, "Use established standards; do not invent mechanisms"): for each mechanism the diff adds, name the documented framework mechanism or industry standard it should use (Microsoft Learn, the RFC, OWASP) and flag any home-grown replacement, with the link. Flag any part that can't be explained in a sentence or two.

## Re-reviews

On a re-review, review only the delta since the approved version and say for each earlier finding whether it is closed. Report lows and nits as one batch; they never trigger a separate round on their own.

## Report

## Known pitfalls

- A test that deletes or bulk-updates rows it did not create (a sweep, a cleanup, `ExecuteDelete`) runs on its own database (`PostgresFixture.CreateMigratedDatabaseAsync`), never the shared one: on the shared database it removes parallel tests' rows (2.8, the session sweep).
- A test that compares an instant with a stored column runs on a fixed clock that is not microsecond-aligned and expects the stored, microsecond value (Npgsql truncates on write); the code truncates where it reads the clock only when the same instant is also returned or published (AD-4, the raise). macOS clocks read whole microseconds and hide the mismatch that Linux CI (100 ns) shows (2.8).
- An invariant a design decision relies on (for example "every `Disputes` write commits with a stored event") must be enforced in code and proved by a test, not assumed (3.8).

Rank findings by severity with file and line.

```json
{"status": "approve", "findings": [{"severity": "high", "file": "...", "line": 0, "issue": "...", "fix": "..."}], "assumptions": [], "open_questions": [], "conflicts": []}
```
