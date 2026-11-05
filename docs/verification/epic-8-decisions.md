# Epic 8 pre-build decisions (human, 2026-11-04)

Raised by the challenger (`8.x-challenge.md`), the pre-build review and the pre-build security read of stories 8.10, 8.13 and 8.16. Each answer amends the effects of the decision named.

| Story | Question | Answer | Effect |
|---|---|---|---|
| 8.10 (D1) | Keep `AcceptsMetadata([])` on resolve, which keeps routing's 415 endpoint from answering a bearer caller with a cookie 401? | **Remove it anyway.** | The line, its AD-8 *Resolve* bullet and the 415-ordering workaround go. A `text/plain` resolve is answered by the cookie fallback policy: 401 (no `WWW-Authenticate: Bearer`) for a missing, refused or valid token, and a refused token writes no `Denied` entry. AD-8 and the three resolve `text/plain` tests are rewritten to that behaviour; AD-14a notes the unaudited case. |
| 8.13 (D2) | How do other tabs learn of a sign-out once the `BroadcastChannel` goes? | **TanStack Query `refetchOnWindowFocus` on the session query.** | A tab brought to the front re-reads the session and, on 401 or signed-out, ends by `location.replace`. A window visible side by side is not covered, and a focus renews a live session. Recorded under Accepted risks in `production-path.md`; the story's ASVS 5.0 V14.3.1 claim is corrected to "the leaving tab, and other tabs on focus". |
| 8.13 (D2) | On a failed sign-out (network), clear the data first (EXPERIENCE row 142, 2026-10-31) or keep it? | **Keep the data and show a notice.** | EXPERIENCE row 142 is reversed: the screen keeps its data under the error notice. |
| 8.16 (D9) | Read the IAM policy templates with jq from C# or with `System.Text.Json`? | **`System.Text.Json`.** | D9's "checked by jq" becomes "checked by `System.Text.Json` in `infra/tests`"; no external tool. |

Dismissed as not decisions (fixed in the stories): 8.7's point on `Compose_Api_StartsAfterTheGatewayThatHoldsTheIssuerAlias` (it reads `infra/nginx/templates/nginx.conf.template`, so ad-011 change 4 moves it); 8.15's point on PRD NFR-SEC-6 and R17 (D6 already decides the third bypass; the docs-first change aligns them).

Also approved 2026-11-04: `CLAUDE.md` points at `docs/process/ad-004-model-roles.md`; the backend builder may write `scripts/` (story 8.1).

Approved 2026-11-04 (8.1, criterion 7): `scripts/check-ready.sh` reads a front-matter field `decisions: settled`, which the supervisor sets after the human answers a story's `[decision]` points, citing the decisions record. The supervisor adds the matching line to `docs/process/agentic-sdlc.md` and the product owner's ticket template when 8.1 lands. Also: 8.3's two `returnUrl` test cases (`Callback_NonLocalReturnUrl_RedirectsToRoot`) go to a backend builder as a test-only change before 8.3 merges.

Approved 2026-11-04 (later): a standing approval to run `docker builder prune --filter until=24h -f` whenever free disk drops under 15 GB (the cache is shared with other projects). 8.7 and 8.9 criterion on `X-Origin-Verify`: the local gateway strips the client's header too, in 8.9 (which rewrites the forwarded-header block), so both gateways behave alike and one test covers both.

Approved 2026-11-04 (evening): run `docker builder prune -f` (all unused build cache) whenever free disk is under 10 GB.
