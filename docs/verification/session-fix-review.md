# Review: session fix (flaky-fixes items 1 and 2)

Reviewer, base `8134305`, four files. Verdict: approve.

| Sev | Finding | Fix |
|---|---|---|
| low | The race test can pass vacuously if another `SELECT EXISTS` query is held first | also assert the held `/session` response body has `state == "unlinked"` |
| nit | AD-7 repeated "deletes and rejects an expired row" | fixed in the docs by the supervisor |
| nit | AD-7 must ship with the code | committed together |
| nit | Sessions active at deploy end within 15 minutes of their last pre-deploy request | fails closed; noted, no change |

## Round 1 (delta)

Reviewer: approve. The low is closed (`SessionAsync` answers `anonymous` before the held `SELECT EXISTS`, so `unlinked` proves the held request was the authenticated read). The new `SessionExpiryTests` test cannot pass vacuously: the hold catches only the store's read, and without the live-row filter the slide writes `now + 15 min`. Nits, no change: the in-flight response status is not asserted; `RenewAsync` still slides on `KeyHash` only, unreachable today since sign-in always issues a new key.

Verifier (round 1): FAIL on one point, that item 1 has no deterministic test failing without the fix. Supervisor decision: accepted as met. The fault was a chance `eyJ` in random ciphertext (about 1 in 262144), which no test can force without a seam; item 1 prescribes the exact-token assertion the builder wrote. Build, 751/751, OpenAPI drift and floor-guard all passed.

## Supervisor checks

Throwaway APFS copy of the round 1 worktree, filtered runs under the `testcontainers` lock, restored files touched.
- Control: both tests pass (2/2).
- `BffAuthentication.cs` from `8134305` (cookie renewed on every request): `Logout_WhileAnotherRequestIsInFlight_...` fails (and the expiry test, since the old ticket expiry differs).
- Slide filtered on `KeyHash` only: `Request_WhoseRowExpiresBetweenTheReadAndTheSlide_...` fails; the race test passes.

## Escapes

| Finding | Caught by | Should have | Category | Countermeasure |
|---|---|---|---|---|
| `Assert.DoesNotContain("eyJ", cookie)` matched random ciphertext | CI (intermittent) | reviewer (2.8) | flaky | review-story pitfall on absence assertions (2026-10-30) |
| `ShouldRenew` on every request let an in-flight request set the cookie again after logout | QA (3.3 rerun, intermittent e2e) | security (2.8) | auth | AD-7 now places the idle limit in the ticket store; the e2e `session-cookie-never-renewed` spec guards it |
| The idle slide could revive a row that ended between read and update | security | builder | auth | conditional `UPDATE` on a live row, with a test |

## Metrics

Fix rounds: 1. Full-suite runs: builder 1 (750), fix round 1, re-verify 1 (751). Supervisor breaks: 2, both caught. QA: 13/13 runs of the formerly flaky spec. CI and Security green on `1b5fc87`. Tokens and wall time: not recorded.
