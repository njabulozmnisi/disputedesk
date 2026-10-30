# Postmortem: second fix rounds (2026-10-30)

Trigger: a second fix round (AGENTS.md, Continuous improvement). Five stories needed one today.

| Story | Round 2 cause | Stage that caught it | Stage that should have | Category |
|---|---|---|---|---|
| 2.6 | duplicate token with whitespace before the colon (round 3: CSS syntax edge) | verifier | builder | input boundary |
| 3.7 | the "older second page" test reused one `createdAt` for every row | verifier | builder | missing test |
| 3.12 | round 1's one-h1 fix left Try again losing focus on a direct load | reviewer | builder | accessibility (regression from a fix) |
| 5.2 | resolve accepted `Upheld`/`UPHELD`; metrics never tested against a failed commit | verifier | builder | input boundary, missing test |
| 6.2 / 6.5 (round 1, recorded here because both verifiers failed the same way) | a parallel test asserting an exact count the design only bounds; a bound in the criterion not enforced | verifier | builder, reviewer | missing test, input boundary |

## Pattern

1. Builders prove the happy path and the named negative cases, but not the bound itself: a criterion that says "camelCase only", "1 to 60 s" or "older rows" is tested with values that do not sit on the bound.
2. A fix moves a focus or timing assumption and the fix's own tests cover only the path the fix list named (3.12).
3. Parallel tests assert exact counts where the design gives an upper bound (6.2).

## Proposed countermeasures (for the epic retrospective, need a human decision)

- build-story Known pitfalls, one line: "For every bound a criterion names (range, casing, order, count), add a test on each side of the bound; a value only inside the range proves nothing."
- review-story Known pitfalls, one line: "A parallel or race test asserts what the design guarantees (at most, exactly one winner), not an exact count that depends on timing."
- review-story Known pitfalls, one line: "For a focus or timing fix, check every path through the changed code (success, each error status, direct load, route change), not only the path the fix list named."

## Decision (2026-10-30)

Approved by the human ("yes to all"): the three pitfall lines are in `.claude/skills/review-story/SKILL.md` (builders read them in their self-check), and the Linux SDK container check for tests that shell out is in `.claude/skills/build-story/SKILL.md` step 5 (from the 2.6 CI escape).
