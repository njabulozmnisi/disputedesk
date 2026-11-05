# E2E baseline QA: regression project on main

Commit tested: 4efec37b776c255a70c384fd49123a974a36d6c5 (main), spec edits uncommitted (in this worktree's `e2e/tests`).

Stack: `docker compose down -v`, then up with `docker-compose.yml` + `e2e/small-queue.compose.yml` + `e2e/raised-limits.compose.yml` (the regression group in `e2e/run-regression.sh`), `DISPUTEDESK_OTLP_HTTP_PORT=14318` (4318 held by another process), `E2E_LINKING_QUEUE_CAPACITY=2`, `E2E_LINKING_BUDGET_SECONDS=10`. Run under `scripts/test-lock.sh run stack`. Project `regression`, four files only. Stack stopped afterwards.

## Result on unmodified main (fresh volume): 3 failed, 5 did not run, 27 passed

| Spec | Result on main | Cause |
|---|---|---|
| bff-sign-in-and-out.spec.ts:93 | FAIL (pre-existing) | stale against 8.13 (D2), see below |
| sign-in-and-out-audit.spec.ts:131 | FAIL (pre-existing) | same |
| session-cookie-never-renewed.spec.ts:122 | FAIL (pre-existing) | same (session and antiforgery cookies present after logout) |
| dispute-withdraw.spec.ts:113 | PASS on main | not reproduced; the outbox row was Pending |
| dispute-withdraw.spec.ts:180 | PASS on main | not reproduced; the 7-transaction shortfall in 8.14's run followed the file stopping early |

The 5 "did not run" are serial tests after bff:93 and audit:131.

## Cause of the three failures

The page snapshot of audit:131 shows "You've been signed out" with Sign in again `returnUrl=/transactions`. Story 8.13 (D2) makes every session end a document navigation to `/signed-out`: the live SPA tab gets a 401 once the logout deletes the session row and `location.replace`s itself while the browser is still in the end_session chain. The specs posted the logout form from the SPA's own page (`page.goto('/')`, which redirects to `/transactions`), so they raced that. A probe posting from the SPA and waiting ended cleanly on `/`, so it is a race in the spec, not a server fault. Server behaviour (end_session with client_id, id_token_hint, state, post_logout_redirect_uri; callback; redirect to `/`; row deleted; SignOut audited; both cookies cleared) is unchanged and still asserted.

## Spec edits (uncommitted)

bff-sign-in-and-out.spec.ts, sign-in-and-out-audit.spec.ts and session-cookie-never-renewed.spec.ts now do `await page.goto('/api/v1/bff/session')` instead of `page.goto('/')` before posting the logout form, with a comment citing 8.13 (D2). No assertion changed or removed. dispute-withdraw.spec.ts is untouched.

## After the edits, fresh volume, same four files: 35 passed

19 of 19 for the three edited files on a used stack as well. Logs: /private/tmp/claude-501/-Users-njabulomnisi-Projects-Dojo-DisputeDesk/77858989-3f5b-4d04-8184-f4d922dc16d1/scratchpad/qa-baseline-1.log (before) and qa-baseline-2.log (after).

## Notes

- The SPA-driven Sign out button is covered by spa-session-end and the journeys, not re-walked here.
- No cookies, tokens or personal data are recorded in this file.
- dispute-withdraw:113 asserts the outbox row `Pending`; it passed here, so no edit. If the relay wins the race it would need to wait for Sent and check the same fields; not changed without a reproduction.
