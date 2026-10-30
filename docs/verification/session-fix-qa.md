# QA: session cookie never renewed (flaky-fixes items 1 and 2)

- Commit tested: `8134305` plus five uncommitted files in `.claude/worktrees/session-fix-1` (BffAuthentication.cs, SessionTicketStore.cs, SessionExpiryTests.cs, SignInTests.cs, SignOutTests.cs). The API image was built from that tree (`docker compose up --build`).
- Standard: AD-7 Expiry (cookie written at sign-in, cleared at sign-out, never renewed; the ticket store slides the 15 minute idle limit; the 12 hour absolute limit never moves); OWASP Session Management Cheat Sheet (logout must end the session); RFC 6265bis (a cookie with no Expires is a browser-session cookie).
- Verdict: **pass for the session fix**. The full e2e suite is not clean, for two reasons that are not the fix (below).
- Evidence is scrubbed: no cookie values, tokens, ID numbers or personal data. Session row IDs are not recorded.

## Stack

- Under `scripts/test-lock.sh run stack qa-session-fix`, from the worktree, with `/private/tmp/.../scratchpad/ov.yml` dropping the Aspire dashboard's 4317/4318 host ports.
- Started with `docker compose up --build --wait` on the existing volumes (`disputedesk_pgdata`, `disputedesk_redpanda`, Zitadel volumes). `docker compose down -v` was not run. "Cold" means freshly started containers on reused volumes, not an empty database.
- Stopped with `docker compose stop`; the lock is released.

## Criteria and the four hops

| # | Criterion | Browser | Network | Database | Events | Result |
|---|---|---|---|---|---|---|
| 1 | After sign-in no later response carries `Set-Cookie` for the session cookie | Cookie has `expires = -1` (browser-session cookie), HttpOnly, Strict | Sign-in: exactly one response sets it (`/api/signin-oidc`). Then five API calls (`/bff/session` x3, `/bff/antiforgery`, `/transactions`), all 200, none with a session `Set-Cookie`; the SPA reload and its calls set none. Also checked with curl: 0 `Set-Cookie` headers on three `/bff/session` calls. | n/a | n/a | pass |
| 2 | `ExpiresAtUtc` moves forward on each request; `AbsoluteExpiresAtUtc` never does | n/a | Requests spaced 1.1 s apart | After sign-in: idle limit 14 to 15 min ahead, absolute limit about 12 h ahead. After each of 5 requests `ExpiresAtUtc` is strictly greater than before; `AbsoluteExpiresAtUtc` is identical to the microsecond. curl run: idle 00:48:18.70, then .20.81, .22.94, .25.02; absolute 12:33:18.68 each time | none: sign-in and sign-out write audit rows only (not changed) | pass |
| 3 | A forged or dead cookie slides nothing | n/a | 401, no session `Set-Cookie` | `Sessions` row count unchanged | n/a | pass |
| 4 | Logout leaves no session cookie even with `/session` requests in flight | After the redirect to `/`, `context.cookies()` is `[]` | A second page in the same browser context sent 2 `/bff/session` calls every 200 ms (paced under the gateway's 10 requests a second) through the logout; all were 200, 5 rounds | The signed-out session row is gone each round (count 0) | n/a | pass, but see the caveat below |

## Caveat: the in-flight logout test did not reproduce the race on the old code

As a negative control I built the API image from `8134305` (without the fix) and ran the new spec:

- Test 2 (no later `Set-Cookie`, idle slides) **failed** on the old code: it received `Set-Cookie` for the session cookie on a later response. So the deterministic check discriminates.
- Test 4 (logout with requests in flight) **passed 3 of 3** on the old code, 5 rounds each. It does not reproduce the logout race, so it is not proof that the race is gone. What proves it is criterion 1: with no response able to set the cookie, a response in flight at logout cannot set it either. I could not find a way to reproduce the original flake on demand; it may need the first test after fresh provisioning, as the flaky-fixes note says.
- The stack was rebuilt from the worktree afterwards.

## `bff-sign-in-and-out.spec.ts`, repeated

- Cold run (first run after `up --wait`): 8 of 8 passed.
- Then 12 more runs back to back: 12 of 12, 8 of 8 each (4.3 s to 12.5 s).
- Total 13 of 13 on the fixed code. No flake seen.

## Full e2e suite (64 tests, one worker)

I ran the suite four times in all, as the per-run data changes. The last run: 53 passed, 2 failed, 1 skipped, 8 did not run (serial blocks after a failure). The 8 skipped by the failure were then run with that one test excluded: 17 of 17 passed in `spa-transactions.spec.ts`, including sign-out, the 401 on Load more, and the inactivity warning with "Stay signed in" renewal.

Failures, neither caused by the fix:

1. `dispute-stored-events.spec.ts:340` "no event is on the dispute topic yet". The test is stale at this base: story 4.1 has removed it in the main checkout's uncommitted changes. It fails here because the shared `disputedesk_redpanda` volume already holds 7 events on `disputedesk.disputes` (published by another build's relay; the worker container image was not built from this worktree). Environment, not the fix.
2. `spa-transactions.spec.ts:268` (axe, focus order): no row shows "Dispute this transaction". Customer A's first transactions page has 0 disputable rows (checked: 20 of 20 `disputable: false`). The data is not reset between runs and each suite run raises more disputes (22 `Disputes` rows now). This is the known disputable-data exhaustion (finding E1 in `3.3-qa.md`). A clean run needs a data reset (`docker compose down -v`, which also wipes the Zitadel and Redpanda volumes shared by other lanes); I did not do that without a decision.

One earlier run also showed two 429s (`sign-in-and-out-audit.spec.ts:74`, `spa-transactions.spec.ts:120`). Cause: my first draft of the in-flight test sent requests faster than the gateway's `limit_req` (10 requests a second, burst 20; `infra/nginx/templates/nginx.conf.template`), which rate-limited later specs from the same IP. I paced the test and added an assertion that every in-flight response is 200; those two tests then passed.

## ZAP baseline

`ghcr.io/zaproxy/zaproxy:stable zap-baseline.py -t http://host.docker.internal:8080 -m 1`: 0 fail, 3 warn rules, 65 pass.

- Storable and Cacheable Content [10049] x5
- Modern Web Application [10109] x2
- Cross-Origin-Embedder-Policy Header Missing or Invalid [90004] x8

Same three rules as 3.3, 3.8 and 2.8. **No new alert.** The spider is unauthenticated.

## Standards check

- RFC 6265bis: the session cookie has no `Expires` or `Max-Age`, so the browser drops it when the session ends; the server clears it on logout. No deviation.
- OWASP Session Management: the idle limit is enforced server-side per request; the absolute limit is fixed at creation. A request sliding the row only while it is live was checked at the HTTP and database level, not at the race level (the integration tests cover the "ended since read" branch; not re-run here).
- A side effect worth knowing: a session cookie now has no browser-side expiry, so a browser that stays open keeps a dead cookie after the idle limit until sign-out. The server answers 401 and a stale row is swept hourly. This is by design in AD-7, not a finding.

## New test

`e2e/tests/session-cookie-never-renewed.spec.ts` (in the worktree, untracked; it fails on the old code, test 2). Four tests: one sign-in `Set-Cookie`; no later `Set-Cookie` and the idle/absolute limits in the database; a forged cookie; logout with requests in flight over five rounds. It is deterministic apart from the in-flight test, which is a smoke check, not a regression proof (see the caveat).

## Open points

- Hosting the new spec: it lives in the builder's worktree; carry it with the patch (`scripts/story-patch.sh`) or copy it to the main checkout's `e2e/tests/`.
- A clean full-suite pass needs a data reset; decide whether to allow `docker compose down -v` (volumes are shared by name, `disputedesk_*`, with other lanes).
