# Intermittent test failures awaiting a fix

Each needs its root cause found and fixed in the test (or the code, if the cause is there), with a test that fails without the fix. Test-only fixes go in under the standing approval.

1. **`Bff.SignInTests.Callback_VerifiedLogin_SetsOnlyAnHttpOnlyStrictSessionCookieAndStoresTheTicketByKeyHash`** (CI run 40972946363, `8134305`): `Assert.DoesNotContain("eyJ", cookieValue)` failed because the random Data Protection ciphertext contained `eyJ` by chance (about 1 in 64³ per position). The check is a proxy for "no JWT in the cookie". Fix: assert the cookie value contains neither the ID token nor the access token the fake issued (exact values), and keep the HttpOnly, SameSite and key-hash checks.
2. **`e2e/tests/bff-sign-in-and-out.spec.ts:92`** (3.3 QA rerun, first test after fresh provisioning): after logout the browser context still held a `disputedesk-session` cookie (expires -1); passed 3 of 3 on rerun. Suspected cause: a request in flight during logout renews the session cookie (`OnValidatePrincipal` sets `ShouldRenew` on every request). The session row is deleted, so the cookie is dead (401), but find the cause before calling it test-only.

## Later test-only items

Not intermittent; test-only changes queued here so no story carries them.

1. **`Bff.SignOutTests.Login_AlreadySignedIn_RecordsSignOutForTheEndedSessionBeforeTheNewSignIn`** (2.8 review nit, moved out of 3.4 on 2026-10-30): the name promises an order the test does not assert. Rename it to drop "BeforeTheNewSignIn", or assert the order.
2. **`ArchitectureTests.UsingAliasRulesTests`, the MSBuild alias rule** (3.9 round 1 delta review): replace the comment-stripping regex and line match with `XDocument.Load` (System.Xml.Linq) and a case-insensitive search for `Using` elements with an `Alias` attribute or child element, so comments and multi-line elements are handled by the parser; cite the MSBuild items page for case-insensitive names. Also reword the `finally` comment in `SnapshotPrecisionTests` (it always runs).
