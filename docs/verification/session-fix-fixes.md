# Session fix: fix round 1

Apply both, then run the full backend suite once.

1. **Race test (reviewer, low).** `SignOutTests.Logout_WhileAnotherRequestIsInFlight_ThatRequestDoesNotSetTheSessionCookieAgain`: also assert the held `/api/v1/bff/session` response body has `state == "unlinked"`, so the test proves the held request was the authenticated session read.
2. **Slide only a live row (security L1).** `SessionTicketStore.RetrieveAsync`: the idle-slide `ExecuteUpdateAsync` filters on `KeyHash` only. Add `row.ExpiresAtUtc > now && row.AbsoluteExpiresAtUtc > now` to its `Where`.
