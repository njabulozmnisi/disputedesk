# Review: P0-2

- Reviewers: `security` and `reviewer` agents (Claude Opus 5.5), read-only
- First verdicts: security `findings` (1 medium, 7 low, nothing high); review `changes`. After fixes: verified PASS (third verifier run, diff hash `1634d2f397f2`). Runtime evidence: `P0-2-smoke.log` (37 asserted checks, `SMOKE OK`).

## Security findings

| # | Severity | Finding | Resolution |
|---|---|---|---|
| M1 | Medium | The IAM_OWNER admin token shared a volume with the login container, which ran as root | Login token in its own volume (read-only for the login container); admin token readable only by zitadel and the provisioner |
| L1 | Low | Unpinned apk packages in the provisioner | Pinned exactly |
| L2 | Low | Gateway forwarded client-controlled Host and appended X-Forwarded-For | Fixed Host per origin; `X-Forwarded-For $remote_addr` on every proxied location |
| L3 | Low | Secrets on process command lines | Header files, stdin, `curl -K -`, `.pgpass`, and `psql \getenv` in the database init script |
| L4 | Low | Login policy loose for a bank (username enumeration, no lockout) | Production path |
| L5 | Low | Synthetic ID numbers and phone numbers could collide with real people | Noted in `fixtures/README.md`; ID numbers never logged; SMS sender must be a stub |
| L6 | Low | Login container ran as root | Runs as uid 1001 |
| L7 | Low | Long-lived admin token stays in its volume | Production path |

## Review findings

| # | Severity | Finding | Resolution |
|---|---|---|---|
| 1 | Medium | Idempotency guard matched "does not exist" as success | Only HTTP 409 counts as "already exists" |
| 2 | Medium | Smoke script printed negative checks instead of failing | Every check now fails the run |
| 3 | Medium | Password check used trust auth on loopback | `-h postgres` (scram); a wrong password is shown to be refused |
| 4 | Medium | Gateway waited for a healthy api: future deadlock | `service_started` |
| 5 | Medium | Login policy configured twice, restating defaults | Removed; `verify.sh` checks the defaults hold |
| 6 | Medium | Native gRPC route with no consumer, probably broken | Removed |
| 7 | Low | Duplicated checks across the three scripts | One job per script |
| 8 | Low | Branding beyond scope | Moved to the theming story |
| 9 | Low | Build-only `scale: 0` service hard to explain | `api-local` stage in `backend/Dockerfile` |

Known gaps: the worker has no healthcheck until the outbox story; PKCE enforcement at token exchange and email verification of new sign-ups are proved in the identity story's browser QA.
