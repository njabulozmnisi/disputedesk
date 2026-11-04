---
id: 13
type: story
title: "Every session ends in the browser by a document navigation"
parent: epic-8-simplicity-sweep
covers: [R2, R3, R40]
after: [3]
hitl: false
risk: high
refined: true
---

# Every session ends in the browser by a document navigation

## Description

Slice S10 of the mitigation plan (wave 3), carrying the human's decision D2. When a session ends, the SPA clears the customer's data from memory without leaving the page, so the session-ended screen can show offline. That needs a `WeakSet` that makes `isSignedIn` lie while the screen changes, a reset-clear-remove sequence, a cache subscriber that writes the old session back, a default `enabled` guard on every query, a renewal call made outside the cache, late-response guards in seven handlers, a cross-tab deadline over `BroadcastChannel`, and test scaffolding that patches the route tree and forces garbage collection (FE-1, DES-8, FE-6); story 7.14 took seven fix briefs. A document navigation drops the whole JavaScript heap, which is what the code already does on a back/forward-cache restore. This story ends every session that way: `location.replace` to the session-ended screen, which may now need the network. The server's idle limit stays the authority; each tab keeps its own inactivity warning.

Human decision, 2026-11-03 (D2): yes, end sessions by navigation; the session-ended screen may need the network. S10 runs with the per-tab warning kept and the shared deadline moved to the production path. Most of `sessionEnd.ts`, `useSessionTabs.ts`, the `BroadcastChannel` protocol, the `WeakSet`, the late-response guards and the GC test go; P74 closes; the mutator's session special case (FE-6) goes.

## Requirements

Verify: `npm run typecheck`, `npm run lint` and `npm test` pass in `frontend/` with no new suppression; the e2e regression suite passes on the stack after QA's changes to `spa-session-end.spec.ts`; the journeys pass.

Inputs:
- The session-ended URL: `/signed-out?reason=expired&returnUrl=<encodeURIComponent(path)>`. `path` is built from `window.location.pathname + window.location.search` (a path, never the absolute `href`, which the BFF's `IsLocalUrl` refuses): `/transactions` for any `/transactions/*` path, `/disputes` for any `/disputes/*` path (compared case-insensitively, as today's `returnUrlAfterSignIn`, `src/app/sessionEnd.ts:94-99`), otherwise the path with its search; `/` when that is longer than 2,048 characters, the login endpoint's bound (`BffEndpoints.cs:34`). `reason` takes only `expired`, as today.
- The idle limit: a wall-clock deadline (a `Date.now()` instant) 15 minutes after the last API response in this tab (`idleLimitMs`, `src/app/useSessionTabs.ts:18`); the warning shows at 13 minutes (`warnAfterIdleMs`, `:19`). The deadline is checked when the timer fires and again on `visibilitychange` and `focus`, because a background timer can be delayed (HTML, Page Visibility).

1. **An ended session leaves the document** (D2, FE-1; R2, AD-7)
   **Given** a signed-in tab
   **When** any query or mutation gets 401, or a network answer of `GET /api/v1/bff/session` is `anonymous`, or the tab's deadline passes with no API response in this tab
   **Then** the tab calls `window.location.replace` with the session-ended URL in Inputs, once; there is no SPA navigation to `/signed-out` and no in-place clean-up of the cache
   **And** Vitest tests with a stubbed `location.replace` prove each trigger calls it exactly once with the exact URL, including a signed-in session query refetch whose answer is `anonymous`; a session value set in the cache by the app itself (the 403 `linking-required` setting `unlinked`, criterion 8) triggers nothing
   **And** a URL test covers a `/transactions/<id>/dispute` path, a `/disputes/<id>` path, another path with a search string, and a path over 2,048 characters

2. **The session-ended screen is a fresh document that makes no request** (D2; R2, R40)
   **Given** a direct load of `/signed-out?reason=expired&returnUrl=…`, with or without a session cookie
   **When** it renders
   **Then** the root layout's session query is disabled on `/signed-out` (TanStack Query `enabled: false` for that path), so the page makes no API request and never renews a session; the Header shows its no-session form (the wordmark only: no navigation, no Sign out, no Sign in), and no inactivity warning runs
   **And** it shows today's heading, copy and "Sign in again" link to `GET /api/v1/bff/login?returnUrl=…`, the first Tab reaches the skip link, as `e2e/tests/spa-session-end.spec.ts:133` checks, and any other `reason` is not found, as today
   **And** a Vitest test of a direct load with a stubbed `fetch` records no request; in the QA stage, the e2e spec asserts no `/api/` request on a direct load with a live session cookie

3. **Each tab warns before its own limit** (D2; R2, WCAG 2.2 SC 2.2.1)
   **Given** a signed-in tab with no API response for 13 minutes
   **When** the warning dialog shows
   **Then** "Stay signed in" (or Escape) renews through the session query (`GET /api/v1/bff/session` through the query cache, no call made outside it), closes the dialog and moves this tab's deadline; an `anonymous` or 401 answer leaves the document as in criterion 1; any other failure shows today's renewal-failed message
   **And** the deadline stays a wall-clock instant re-checked on `visibilitychange` and `focus`: a Vitest case moves `Date` past the deadline without firing the timer, fires `visibilitychange`, and expects `location.replace` once
   **And** no tab reads or posts another tab's activity: the `BroadcastChannel` (`disputedesk-session`) and its `activity`, `expired` and `signed-out` messages go

4. **Another tab learns of the end when it is brought to the front** (human decision 2026-11-04, D2; R2, R3)
   **Given** two tabs of one session, and the session ended in one of them (sign-out, a 401, or its idle limit on the server)
   **When** the other tab gains focus
   **Then** its session query refetches (TanStack Query `refetchOnWindowFocus: true` on the session query only; every other query keeps the client default `false`), and a 401 or `anonymous` answer leaves the document as in criterion 1; a live session's answer counts as an API response and moves this tab's deadline, as the server renews the session on it
   **And** a Vitest test fires a window focus on a signed-in tab and expects one session request, then `location.replace` once on an `anonymous` answer
   **And** a tab that stays visible beside the other without gaining focus learns only on its next request's 401 or at its own deadline (accepted risk, `docs/production-path.md`)

5. **Sign-out is the form post, and a failed sign-out keeps the screen** (D2, human decision 2026-11-04; R3)
   **Given** the Sign out button (`src/app/useSignOut.ts`)
   **When** the customer signs out
   **Then** the SPA fetches the antiforgery token and submits the top-level form `POST /api/v1/bff/logout`, a document navigation, with no `leaveToStartPage` clean-up before it and no message to other tabs
   **And** when the token fetch fails (no response or a 5xx), the current screen keeps its data and shows today's sign-out-failed notice above it ("We couldn't finish signing you out." "Try again." with a Sign out button); a 401 on the fetch leaves the document as in criterion 1

6. **The in-place clean-up code is gone** (D2, FE-1, FE-6; R34)
   **Given** `src/app/sessionEnd.ts`, `useSessionTabs.ts`, `src/api/queryClient.ts:46-70`, `src/api/mutator.ts:80-90` and the late-response guards
   **When** this story lands
   **Then** `endingSessions`, `resetCustomerData`, `clearCustomerData`, `removeCustomerQueries`, `leaveToStartPage`, the channel helpers, the write-back subscriber, the default `enabled: () => isSignedIn(queryClient)` (and its copy in `DisputeDetailScreen.tsx:183-185`) and the `isSignedIn` guards in `DetailsScreen.tsx:56`, `OtpScreen.tsx:86`, `ReviewScreen.tsx:108,135`, `WithdrawDialog.tsx:167`, `useGoToTransactions.ts:17` and `InactivityWarning.tsx:44,53` are deleted
   **And** the mutator keeps one plain response listener that the idle timer uses, with no special case for the session URL

7. **The test scaffolding for in-place clean-up is gone** (D2, FE-1; R35)
   **Given** `src/test/support.tsx:287-320` (`holdSessionEndedRoute` and `restoreHeldRoutes`), `src/test/holdSessionEndedRoute.test.tsx` and `src/app/sessionEndCollected.test.tsx`
   **When** this story lands
   **Then** all three go, with the multi-tab cases of `src/app/sessionTabs.test.tsx`, `InactivityWarning.test.tsx` and `signOut.test.tsx` that test the channel; criteria 1 to 5 have their own tests

8. **Other session behaviour is unchanged** (D2; R2, R9)
   **Given** a back/forward-cache restore, and a 403 `linking-required`
   **When** each happens
   **Then** the restore still reloads the page (`src/app/RootLayout.tsx:36-45`), and the 403 still sets the session to `unlinked` and navigates to `/link` inside the SPA (`queryClient.ts:24-34`)
   **And** in the QA stage, `spa-session-end.spec.ts` keeps its "no customer data in the DOM, also after Back" checks for every way out: a 401, the idle limit, a sign-out, and the other tab's end on focus

## Boundaries

- Must not change: the server's session (AD-7: the ticket store's 15-minute idle limit is the authority); the session-ended and sign-out-failed copy; the BFF endpoints; the bfcache reload; the routing guards 8.3 built (`/signed-out` and not-found stay root children outside the three layout routes, with no root `beforeLoad` reading the session).
- No offline session-ended screen: offline, the browser shows its own error page, which holds no customer data (D2).
- No cross-tab deadline and no cross-tab message: other tabs learn on focus, on their next request or at their own deadline (D2; human decision 2026-11-04).
- A failed sign-out does not clear the screen: EXPERIENCE row 142's clean-up is reversed by the human (2026-11-04); the server session is still live, so the data on screen is still the customer's.

## Supervisor checks

- Throwaway copy: replace `location.replace` with `router.navigate`; criterion 1's tests fail.
- Throwaway copy: leave the session query enabled on `/signed-out`; criterion 2's no-request test fails.
- Throwaway copy: remove the `visibilitychange` re-check; criterion 3's moved-`Date` case fails.
- Throwaway copy: set `refetchOnWindowFocus: false` on the session query; criterion 4's focus test fails.
- Throwaway copy: clear the cache before the token fetch in `useSignOut`; criterion 5's failed-sign-out test (data still on screen) fails.
- Docker check (supervisor, throwaway stack): sign in, open a dispute, expire the session (cookie removed in the BFF's ticket store), make a request; the heap snapshot of the new document holds no dispute ID (P74's check from 7.14 QA).

## Build facts

- Owner: frontend (frontend-builder); QA changes the e2e spec in its stage.
- Tier: High-stakes (session): the security agent reviews the diff and the human reviews it in a review block.
- Flags: high-stakes, security.
- Lane: B, after 8.3; may build beside 8.12.
- Before the build (supervisor, docs-only change recording D2 and the human's answers of 2026-11-04): in `docs/ux/EXPERIENCE.md`, the *Session ended* screen row (`:36`, "reached in any tab; another tab's 401") reads that each tab reaches its own limit and other tabs leave on focus or their next request; the *Session about to end* row (`:141`, "Activity in another tab … moves the limit") drops the cross-tab sentence; the *Sign-out failed* row (`:142`) is reversed: the current screen keeps its data under the notice, and a 401 leaves to the session-ended screen; the *Session ended* state row (`:143`, "shows without a request, so it also shows offline"; "a deadline 15 minutes after the last API response in any tab") reads that the screen is a fresh document that may need the network. AD-7's *Expiry* bullet (`docs/architecture/architecture.md`, AD-7) drops the shared `BroadcastChannel` deadline and names the per-tab deadline with its focus refetch. `docs/production-path.md` gains the cross-tab shared deadline under *Deferred scope*, and under *Accepted risks* the focus-refetch gap (a window visible beside another is not covered; a focus renews a live session), and closes P74. The builder does not edit them.
- Mechanisms and sources:
  - Navigating to a new document (`Location.replace`): <https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigate>; <https://developer.mozilla.org/docs/Web/API/Location/replace>.
  - Back/forward cache and reloading on restore: <https://web.dev/articles/bfcache>.
  - Server-enforced idle timeout: <https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html>.
  - Warning before a time limit: <https://www.w3.org/WAI/WCAG22/Understanding/timing-adjustable.html>.
  - TanStack Query `refetchOnWindowFocus` and `enabled`: <https://tanstack.com/query/latest/docs/framework/react/guides/window-focus-refetching>; <https://tanstack.com/query/latest/docs/framework/react/guides/disabling-queries>.
  - Page Visibility (`visibilitychange`), for the wall-clock re-check: <https://developer.mozilla.org/docs/Web/API/Document/visibilitychange_event>.
  - ASVS 5.0 V14.3.1 (clear data at session end), met by the document navigation for the leaving tab, and for other tabs on focus: <https://github.com/OWASP/ASVS>.
- Packages: none.
- Touches: `src/app/sessionEnd.ts`, `src/app/useSessionTabs.ts`, `src/app/InactivityWarning.tsx`, `src/app/useSignOut.ts`, `src/app/RootLayout.tsx`, `src/api/queryClient.ts`, `src/api/mutator.ts`, `src/features/linking/DetailsScreen.tsx`, `OtpScreen.tsx`, `src/features/disputes/raise/ReviewScreen.tsx`, `src/features/disputes/withdraw/WithdrawDialog.tsx`, `src/features/disputes/detail/DisputeDetailScreen.tsx`, `useGoToTransactions.ts`, `src/test/support.tsx`, the deleted tests in criterion 6, and new tests; QA stage: `e2e/tests/spa-session-end.spec.ts` (`:39` "sign-out in a second tab clears the first" and `:92` "a second tab ends on the first tab's 401" are rewritten: the other tab leaves when brought to the front; the no-request check on a direct load of `/signed-out` is added; the "after Back" checks stay).

## Pre-build

- Challenger: `docs/verification/8.13-challenge.md` (simplify).
- Review and security: `docs/verification/epic-8-prebuild-wave3.md`, sections Review 8.13 and Security read 8.13.
- Decisions: settled by the human, 2026-11-04 (`docs/verification/epic-8-decisions.md`, D2: other tabs on focus; a failed sign-out keeps the data).
- Sofia, 2026-11-04: every `[story]` point folded. Review: the root's session query disabled on `/signed-out` and the Header's form named (criterion 2); EXPERIENCE rows `:36`, `:141`, `:142`, `:143` (docs-first); the URL from `pathname + search` with today's collapse (Inputs). Security: the wall-clock deadline and its re-checks kept, with a moved-`Date` case (criterion 3); the "after Back" checks for every way out (criterion 8). Challenger: the same-tab re-checks kept (criterion 3); the no-request load with a cookie (criterion 2); the failed sign-out's screen (criterion 5, per the human); the URL's search, IDs and 2,048-character bound (Inputs, criterion 1); an `anonymous` network answer told apart from an app-set cache value (criterion 1). The split was not needed: 8 criteria.

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- plan — docs/verification/simplicity-audit/mitigation-plan.md, section 1 (D2), wave 3, S10
- audit — docs/verification/simplicity-audit/frontend.md, FE-1, FE-6; design.md, DES-8
- story — docs/tickets/epic-7-release/tickets/story-7.14-customer-data-cleared-at-session-end-copy-and-operations-docs.md, criteria 1 and 2 (replaced)
- experience — docs/ux/EXPERIENCE.md, rows "Session about to end", "Sign-out failed", "Session ended"
- architecture — docs/architecture/architecture.md, AD-7 *Expiry*
- production path — docs/production-path.md, P74

## Notes

- Risk high (proposed): session end is the customer-data boundary; criterion 1's three triggers and the heap check prove nothing of the old document survives.
- Human decision, 2026-11-03 (D2), as quoted in the Description; not reopened here.
- Human decisions, 2026-11-04 (`docs/verification/epic-8-decisions.md`, 8.13): other tabs learn of an ended session through TanStack Query `refetchOnWindowFocus` on the session query, with the side-by-side gap recorded under Accepted risks and the ASVS claim corrected to "the leaving tab, and other tabs on focus" (criterion 4); a failed sign-out keeps the data on screen with the notice, reversing EXPERIENCE row 142 (criterion 5).
- Decision: 2026-11-04 (Sofia) — the session-ended page makes no request, because any authenticated request would renew the sliding session the page reports as ended; the root's session query is disabled on that path (criterion 2).
- Decision: 2026-11-04 (Sofia) — the per-tab wall-clock deadline and its `visibilitychange` and `focus` re-checks stay (criterion 3): D2 removes the shared deadline, not the tab's own, and a background timer alone is not a reliable privacy cutoff.
- Process: this story deletes `src/app/sessionEndCollected.test.tsx`, which a 7.14 pitfall line names in the build-story and security-story skills; that line changes when 8.13 merges, a skill change that goes to the human.
