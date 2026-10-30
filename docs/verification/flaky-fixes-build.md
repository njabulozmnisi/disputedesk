Both flaky tests are fixed and the full suite passes (750 of 750). Item 1 is a test-only change; item 2's root cause was in production code, so I fixed it there. Nothing is committed.

**Item 1: `SignInTests.Callback_VerifiedLogin_SetsOnlyAnHttpOnlyStrictSessionCookieAndStoresTheTicketByKeyHash` (test-only)**
- **Root cause:** the test failed if the callback's headers and body contained the substring `"eyJ"` anywhere. Those headers include the session cookie, which is random Data Protection ciphertext in base64url, so it sometimes contains `eyJ` by chance with no token present.
- **Reproduction:** a throwaway test protected the real cookie ticket (only the session-key claim) with the app's `TicketDataFormat` until the output contained `eyJ`, then ran the old assertion on it. It failed: `Sub-string found: "eyJ"`. I deleted the test afterwards.
- **Fix:** the test now asserts that the callback's headers and body contain neither the exact ID token the fake Zitadel issued (`LastIdToken`) nor its access token (`opaque-access-token`). The HttpOnly, SameSite=Strict, key-hash and protected-ticket checks are unchanged.

**Item 2: e2e `bff-sign-in-and-out.spec.ts:92`, a session cookie left behind after logout (production code)**
- **Root cause:** `OnValidatePrincipal` set `ShouldRenew = true` on every authenticated request. The cookie handler then writes `Set-Cookie: disputedesk-session=…` when the response starts, and it does this even if the session row was deleted in the meantime. In the e2e, `page.goto('/')` starts the SPA's `GET /api/v1/bff/session` and the logout form posts straight away. If that GET authenticates before logout deletes the row but responds after the logout response, the browser gets the ended session's cookie back. The cookie is dead (its row is gone, so the server returns 401 or anonymous), but "logout clears both cookies" is broken in the browser.
- **Reproduction:** new test `SignOutTests.Logout_WhileAnotherRequestIsInFlight_ThatRequestDoesNotSetTheSessionCookieAgain`. It uses the existing `HoldInterceptor` to pause `/session` on its `SELECT EXISTS` query, after authentication. It then runs the logout, releases the hold, and asserts the paused response has no `Set-Cookie` and the cookie jar has no session cookie. Before the fix it failed every run because the `Set-Cookie` header was present.
- **Fix:** the cookie is now written only at sign-in and cleared at sign-out; it is never renewed.
  - `BffAuthentication.cs`: removed the `ShouldRenew` handler and set `ExpireTimeSpan` to the 12-hour absolute limit.
  - `SessionTicketStore.cs`: `RetrieveAsync` moves `ExpiresAtUtc` forward on every request that carries the cookie. It still deletes and rejects an expired row first, and the absolute limit never moves.
  - The behaviour stays the same: a 15-minute idle limit renewed by every authenticated request, and 12 hours in total. All existing `SessionExpiryTests` pass unchanged.
  - Since this cookie has no expiry date and holds only the session key, re-sending it on each request did nothing useful.
- **e2e spec:** no change needed. Its assertion was right; the backend was wrong.

**Before you commit**
- **Architecture conflict:** this changes an auth mechanism that the architecture document names explicitly. Lines 110 and 112 of `docs/architecture/architecture.md` say the idle limit is renewed by "`ShouldRenew = true` in `OnValidatePrincipal`" and that "`RenewAsync` moves only `ExpiresAtUtc`". I can't write under `docs/`, so the document needs updating. Changing an auth mechanism is high-stakes, so the diff needs your review and the security re-run.
- **Existing sessions at deploy:** sessions created before this change stop renewing after deploy. They end at most 15 minutes after their last request before deploy.
- **Not run:** the developer smoke check against dev Zitadel, which wasn't in your list.

**Checks (each run once):** `dotnet build` with warnings as errors: 0 warnings, 0 errors, no `openapi.json` drift. Full suite under `scripts/test-lock.sh run testcontainers flaky2`: 750 total, 750 passed, 0 failed, 0 skipped. `dotnet format --verify-no-changes`: clean. `scripts/floor-guard.sh`: clean. `scripts/secret-scan.sh`: no leaks.

**Files changed:**
- `backend/src/DisputeDesk.Api/Bff/BffAuthentication.cs`
- `backend/src/DisputeDesk.Api/Bff/SessionTicketStore.cs`
- `backend/tests/DisputeDesk.Api.IntegrationTests/Bff/SignInTests.cs`
- `backend/tests/DisputeDesk.Api.IntegrationTests/Bff/SignOutTests.cs`

```json
{
  "status": "built",
  "worktree": "/Users/njabulomnisi/Projects/Dojo/disputedesk/.claude/worktrees/flaky2",
  "criteria": [
    {"id": "item-1 eyJ false positive (test-only)", "requirement": "2.2 AC1/AC2: no token reaches the browser", "test": "SignInTests.Callback_VerifiedLogin_SetsOnlyAnHttpOnlyStrictSessionCookieAndStoresTheTicketByKeyHash (asserts the exact LastIdToken and opaque-access-token are absent; reproduction: a throwaway test produced a token-free cookie containing eyJ and the old assertion failed; deleted)"},
    {"id": "item-2 in-flight request re-sets the session cookie after logout (production code)", "requirement": "2.2 AC6: logout clears both cookies; AC3 idle and absolute limits unchanged", "test": "SignOutTests.Logout_WhileAnotherRequestIsInFlight_ThatRequestDoesNotSetTheSessionCookieAgain (failed before the fix: Set-Cookie present on the held /session response); SessionExpiryTests.* unchanged and passing"}
  ],
  "smoke": "Not run (not in the requested check list). Run once: dotnet build (0 warnings, 0 errors, no openapi.json drift); full suite under scripts/test-lock.sh run testcontainers flaky2: 750 total, 750 passed, 0 failed, 0 skipped; dotnet format --verify-no-changes exit 0; scripts/floor-guard.sh clean; scripts/secret-scan.sh no leaks.",
  "standards": [
    {"mechanism": "Server-side session via CookieAuthenticationOptions.SessionStore / ITicketStore: only a session identifier goes to the client, so the cookie need not be re-sent per request", "source": "https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.cookies.cookieauthenticationoptions.sessionstore?view=aspnetcore-10.0"},
    {"mechanism": "Cookie handler renewal behaviour (FinishResponseAsync re-appends the cookie whenever renewal was requested)", "source": "https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs"},
    {"mechanism": "Idle and absolute session timeouts enforced server-side; logout invalidates the session", "source": "https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html"}
  ],
  "breaks": [
    {"patch": "--- a/backend/src/DisputeDesk.Api/Bff/BffAuthentication.cs\n+++ b/backend/src/DisputeDesk.Api/Bff/BffAuthentication.cs\n@@\n         cookie.SlidingExpiration = false;\n+        cookie.Events.OnValidatePrincipal = context =>\n+        {\n+            context.ShouldRenew = true;\n+            return Task.CompletedTask;\n+        };\n", "test_method": "DisputeDesk.Api.IntegrationTests.Bff.SignOutTests.Logout_WhileAnotherRequestIsInFlight_ThatRequestDoesNotSetTheSessionCookieAgain", "expect": "fails"},
    {"patch": "--- a/backend/src/DisputeDesk.Api/Bff/SessionTicketStore.cs\n+++ b/backend/src/DisputeDesk.Api/Bff/SessionTicketStore.cs\n@@\n-        await db.Set<Session>()\n-            .Where(row => row.KeyHash == keyHash)\n-            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, now + IdleTimeout));\n", "test_method": "DisputeDesk.Api.IntegrationTests.Bff.SessionExpiryTests.Request_InsideTheIdleWindow_RenewsTheIdleLimit", "expect": "fails"}
  ],
  "assumptions": [
    "Item 1 is test-only. Item 2's root cause is in production code (BffAuthentication.cs, SessionTicketStore.cs) and is not test-only.",
    "Sessions created before deploy carry a ticket expiry 15 minutes after their last renewal, so they end at most 15 minutes after deploy.",
    "RenewAsync is kept unchanged because ITicketStore requires it; the handler now calls it only on a sign-in over a live session.",
    "No e2e spec change is needed: the spec's empty-cookie assertion is correct and should pass once the backend fix is in.",
    "Auth mechanism change: high-stakes tier, so it needs the human diff review and a security re-run before commit."
  ],
  "open_questions": [],
  "conflicts": [
    "docs/architecture/architecture.md lines 110 and 112 (AD-7 Server-side session and Expiry) name `ShouldRenew = true` in `OnValidatePrincipal` and say `RenewAsync` moves only `ExpiresAtUtc`. After this fix, RetrieveAsync slides `ExpiresAtUtc` and the cookie is never renewed. The behaviour is the same (15-minute idle limit renewed by every authenticated request, 12 hours in total), but the named mechanism changed, so the document needs updating; I cannot write under docs/."
  ]
}
```
