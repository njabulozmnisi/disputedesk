# Verification: session-fix

- Commit: `8134305e578725ce05b36d77eed46b84608bb3fc` (uncommitted diff hash `cb24191ab53c`)
- Verifier: gpt-6-astra via GitHub Copilot CLI
- Date (UTC): 2026-10-29T23:44:51Z

VERDICT: PASS

| Criterion | Test | Proved (yes/no) |
|---|---|---|
| Check exact issued tokens, retaining HttpOnly, SameSite and key-hash assertions | `SignInTests.Callback_VerifiedLogin_SetsOnlyAnHttpOnlyStrictSessionCookieAndStoresTheTicketByKeyHash` | yes |
| Prevent an in-flight response restoring the cookie after logout | `SignOutTests.Logout_WhileAnotherRequestIsInFlight_ThatRequestDoesNotSetTheSessionCookieAgain` | yes |
| Preserve idle renewal and expiration limits | `SessionExpiryTests.Request_InsideTheIdleWindow_RenewsTheIdleLimit`, `Request_After15MinutesIdle_Returns401AndDeletesTheRow`, `Request_After12HoursOfActivity_Returns401AndDeletesTheRow` | yes |

**Failures:** None. Backend build passed with zero warnings/errors; all 750 tests passed, including Testcontainers integration and architecture tests. OpenAPI matches HEAD. Style, whitespace and floor checks passed. No frontend changes.

The regression test deterministically holds an authenticated response until logout finishes. Removing unconditional cookie renewal addresses that response-ordering race. Reverting the fix and reproducing the original browser failure remain supervisor checks.

## Standards

Server-side idle renewal in `SessionTicketStore.RetrieveAsync` replaces cookie reissuance rather than using built-in [`SlidingExpiration`](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.cookies.cookieauthenticationoptions.slidingexpiration?view=aspnetcore-10.0). This is justified here: built-in sliding expiration reissues cookies, recreating the race. The implementation retains the documented `ITicketStore` extension point.

{"status":"pass","sha":"8134305e578725ce05b36d77eed46b84608bb3fc","failures":[],"assumptions":["Code-only verification; browser reproduction and deliberate reversal remain supervisor checks."],"open_questions":[],"conflicts":[]}

## Verifier notes (criteria checked against the diff)

- Criterion 1: SignInTests asserts `factory.Zitadel.LastIdToken` and `opaque-access-token` are absent; the `eyJ` check is gone; the HttpOnly, SameSite and key-hash checks are unchanged.
- Criterion 2: `BffAuthentication` drops `OnValidatePrincipal` renewal and sets `ExpireTimeSpan` to the absolute limit with `SlidingExpiration=false`; `RetrieveAsync` deletes and rejects an expired row first, then sets `ExpiresAtUtc = now + IdleTimeout` and never touches the absolute limit; the new race test is present; SessionExpiryTests are not in the diff (unchanged, passing in the 750); AD-7 (Server-side session and Expiry bullets) matches.
- floor-guard: clean. Supervisor to confirm: revert the fix and watch the new test fail.

## Round 1

- Commit: `8134305e578725ce05b36d77eed46b84608bb3fc` (uncommitted diff hash `5913748627b6`, worktree `.claude/worktrees/session-fix-1`)
- Verifier: gpt-6-astra via GitHub Copilot CLI (full verdict in the worktree at `docs/verification/session-fix-r1-verify.md`)
- Date (UTC): 2026-10-29T23:58:03Z

VERDICT: FAIL (verifier's own verdict, recorded unchanged)

| Criterion | Test | Proved |
|---|---|---|
| Fixes item 1: exact issued tokens, HttpOnly/SameSite/key-hash kept | `SignInTests.Callback_VerifiedLogin_SetsOnlyAnHttpOnlyStrictSessionCookieAndStoresTheTicketByKeyHash` | yes |
| Fixes item 2: no in-flight response restores the cookie after logout; held request body is `unlinked` (round 1 fix 1) | `SignOutTests.Logout_WhileAnotherRequestIsInFlight_ThatRequestDoesNotSetTheSessionCookieAgain` | yes |
| Idle renewal, idle expiry, absolute expiry (incl. round 1 fix 2: slide only a live row) | `SessionExpiryTests.Request_InsideTheIdleWindow_RenewsTheIdleLimit`, `Request_After15MinutesIdle_Returns401AndDeletesTheRow`, `Request_After12HoursOfActivity_Returns401AndDeletesTheRow` | yes |
| A regression test that fails without the first fix | none deterministic | no (verifier) |

**Failure cited by the verifier:** `SignInTests.cs:65-74` replaces the `eyJ` substring check with exact-token checks but the ciphertext is still random, so the old assertion would usually still pass; no test forces ciphertext containing `eyJ`.

**Checks:** build 0 warnings/errors; 751 tests passed, none skipped (750 before, plus the new slide-only-live-row test in SessionExpiryTests); OpenAPI matches HEAD; `scripts/floor-guard.sh` clean (run separately in the worktree). Both round 1 fixes are in the diff: the `unlinked` body assertion in SignOutTests, and `row.ExpiresAtUtc > now && row.AbsoluteExpiresAtUtc > now` in the `RetrieveAsync` slide `Where`, with a SessionExpiryTests test (+33 lines).

**Supervisor note:** the only failure is the "fails without the fix" demand for item 1. Item 1 in `flaky-fixes.md` prescribes exactly the change made (assert the exact tokens), and a probabilistic 1-in-262144 false positive cannot be forced deterministically without a seam; the round 0 verdict accepted this. The supervisor decides whether to accept it as is. Reverting a fix to watch its test fail stays a supervisor check.

**Standards:** the server-side idle slide in `RetrieveAsync` replaces the framework `SlidingExpiration` cookie reissue, deliberately, since reissue is the race. Informational.
