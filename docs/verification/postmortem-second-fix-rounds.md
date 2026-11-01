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

## Supervisor escape (2026-10-30, 3.12)

The supervisor accepted a verifier finding (keep the loaded card and dialog on a failed refetch) that contradicted the story's own criterion 2 ("a failed refetch never leaves a stale detail on screen"); the builder implemented it and replaced the criterion 2 test; a fourth round reverts it after a human decision. Category: spec gap (supervisor disposition). Countermeasure: before accepting any stage finding into a fix list, the supervisor searches the story for the behaviour the finding changes and quotes the criterion it satisfies or contradicts in the disposition; a contradiction goes to the human as spec ambiguity before any builder runs.

## 6.4 fix round 2 (2026-10-30)

**What happened.** Fix round 1 added a 2a test for "typing breaks the field again keeps focus in the field". The delta review found it passes without the fix: the typed sequence restores the same wrong ID, so the field is never fixed. The builder's report claimed the test failed without the fix, but that break was not run for 2a.

**Category.** Missing test (a test that cannot fail), with an unrun break reported as run.

**Countermeasure (proposed for the epic 7 retrospective).** A deterministic check is the target: the builder's `breaks` entries are re-run by a script in a throwaway copy before the report goes out (apply the break patch, run the named test, expect a failure). Until then the reviewer runs each break it relies on, as it did here.

## 7.2 fix round 2 (2026-10-31)

**What happened.** Pre-build review's five `[story]` points were never folded into story 7.2 (no "Sofia" resolution was written, and the readiness check did not catch it). The first verifier failed criterion 5 on the unfolded wording; Sofia then folded all five, which added "actor A" for UJ-2's rejected raise to criterion 1 while QA's fix round 1 was already running. The delta review found UJ-2 counted the row without checking the actor (the same gap M2 fixed in UJ-5b) and logged it as a low for later; the re-verify judged it against the amended criterion and failed it, which opened round 2.

**Category.** Spec gap (a pre-build fold skipped), then a story amended mid-round.

**Countermeasure (proposed for the epic 7 retrospective).** A deterministic readiness check: a script fails a story whose `<story>-prebuild-review.md` or `-challenge.md` has `[story]` points but no "Sofia, <date>." resolution, run by the supervisor before dispatch. Until then, the supervisor greps for the resolution line before dispatching. A story amended during a fix round goes back through the delta review against the amended text before the re-verify, so a criterion-level gap is fixed in the same round rather than logged as a low.

## 7.14 fix round 2 (2026-10-31)

**What happened.** Round 1 replaced the builder's in-render draft reset with `key={state}`, as the reviewer's R1 and the story asked. The delta review found that the key changes from `undefined` to `anonymous` on the first session answer, remounting screens that have no session gate; `usePageHeading` then moves focus to the new heading on a direct page load, so the first Tab no longer reaches the skip link (story 3.7 criterion 6). A medium finding after a round opens a second round. Security's re-run added one low item (a 401 on the sign-out token fetch does not tell other tabs), present since round 0.

**Why.** The story named a key "by the session state" without saying which part of the state; the pre-build review and the first review checked the clean-up paths but not the direct-load focus path, which the reviewer's own known pitfall ("a direct load") covers. The builder's original objection to the key was a real signal, read as an unreproduced claim.

**Countermeasures (proposed for the epic 7 retrospective).** (1) A routing test that every top-level route, loaded directly, leaves the first Tab on the skip link: a deterministic check that catches any remount around `<Outlet/>`. (2) When a builder reports a conflict with the story's mechanism, the reviewer reproduces the builder's failure path, not only the full suite, before ruling. Escapes are recorded in `7.14-review.md`.

## 7.12 fix round 2 (2026-10-31)

**What happened.** The build hid Zitadel's `Referrer-Policy` at server level, which weakened Zitadel's own `same-origin`; the supervisor's ruling (narrow the hide to Login v2) went into fix round 1 with the item "exactly one `Referrer-Policy` everywhere" and "no server-level `add_header Referrer-Policy`". Those two cannot both hold for responses with no upstream (the gateway's own 403, 404 and 302, and Zitadel's discovery, which sends none); the builder chose "none", and both the delta review and the security re-run held that against criterion 3, A-F6 and ASVS V3.4.5.

**Why.** The supervisor wrote a fix item from the security constraint without walking each response criterion 3 names; the pre-build reviews did not test the upstream header values per location (only Login v2's were read from the image).

**Countermeasures (proposed for the epic 7 retrospective).** (1) A supervisor fix-list check: every fix item that changes which component sets a header lists each response the criterion names and the expected value, before dispatch. (2) The gateway test models every upstream's real header set per location (taken once from the pinned images), so a header gap shows in the build, not in review.

**7.14 round 3 (2026-11-01, human gate: two failed fix attempts).** Round 2's `key={isSignedIn ? ...}` fixed anonymous direct loads only; a signed-in direct load of a public route still remounted. Any key around `<Outlet/>` remounts on the first session answer, so the story's named mechanism could not meet story 3.7 criterion 6. The human chose the reviewer's fix (the builder's original in-render reset); criterion 1 amended. The countermeasure above (a skip-link test over every route and every session state) would have caught both rounds.

**7.14 QA rounds (2026-11-01, human gate: two failed attempts on QA's F1).** QA's CDP heap snapshot found the first transactions page reachable minutes after the clean-up. The first QA fix disabled customer queries by default, which stops requests but not the leak: TanStack Query reschedules a 5-minute gc timer on a query removed while a screen still observed it, and nothing can clear it. The security re-run reproduced it in Vitest with a `WeakRef` and forced GC and proved "reset in place, remove after navigating". Countermeasure (retrospective): a memory regression test (`WeakRef` plus forced GC) for any clean-up of customer data, beside the cache assertions; a cleared cache is not proof of freed memory.
