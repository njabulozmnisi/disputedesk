---
id: 14
type: story
title: "Customer data cleared at session end, copy and operations docs"
parent: epic-7-release
covers: [R2, R3, R34, R39, R28]
after: [7]
hitl: false
risk: medium
refined: true
---

# Customer data cleared at session end, copy and operations docs

## Description

Fixes the four findings of the production readiness review that the human disposed as "fix, story 7.14" on 2026-10-31 (`docs/verification/7.10-prr.md`, Disposition): E-F8, F-F20, F-F6 and F-F12. The SPA holds customer data only in memory (the query cache, the mutation cache and the raise draft). E-F8 makes every way a session ends run one clean-up that clears it at once, without the network: a 401, a session answered as `anonymous`, the idle limit, sign-out (cleared before any request, so a failed sign-out leaves nothing behind) and another tab's session end; a page restored from the browser's back/forward cache reloads. The tabs share the idle limit through `BroadcastChannel`, which replaces the per-tab timer shortcut. The browser's limit is a privacy cutoff only: the server's session stays the authority (AD-7), and the tab messages are best-effort notices, not a session protocol. F-F20 changes one hint to the glossary's "card transaction". F-F6 adds the real-SMS note to the README, and F-F12 adds a runbook procedure for a transient Kafka outage. The frontend part is the frontend builder's; the README, runbook, copy-deck and AD-7 lines are docs, written as in story 7.7; QA updates the e2e expectations.

## Requirements

Verify: Vitest tests pass with `npm run typecheck`, `npm run lint`, `npm run format` and `npm test` in `frontend/`, no new suppression; the five journeys and the regression project pass in CI's `e2e` job (QA updates the expectations named in Build facts); the docs agent runs the new runbook procedure on a fresh stack under `scripts/test-lock.sh run stack 7.14 -- …` and records each command, its exit status and the observed keys, offsets, versions and counts in `docs/verification/7.14-run.md`, which holds no event payload, token, OTP or password; `scripts/secret-scan.sh` passes; `docs/runbook.md` stays at most 150 lines and the README at most 300 (story 7.7's bounds).

Inputs: none from a request. Fixed values:
- The idle limit the SPA applies itself: a deadline of 15 minutes after the last API response, other than a 401 or a session answered as `anonymous`, in any tab of the same origin (the server's limit, AD-7); the warning shows from 2 minutes before the deadline (13 minutes), as today. The deadline is a wall-clock time (`Date.now()`), checked when a timer fires, on `visibilitychange` to visible and on `focus`. A response can arrive up to one request's duration after the server renewed the session, so the browser's cutoff can be that much later than the server's; the server stays the authority.
- The `BroadcastChannel` name: `disputedesk-session`; messages: the strings `activity`, `expired`, `signed-out`, nothing else, and no customer data in any message. Any other message is ignored. A tab that receives a message never posts one in reply.

1. **One clean-up clears all customer data on every change to `anonymous`** (R2; ASVS 5.0.0 V14.3.1)
   **Given** a signed-in SPA with transactions, disputes and a dispute detail in the query cache, a withdraw and a raise in flight, and a half-filled raise draft
   **When** any request answers 401, or a session request answers `anonymous`
   **Then** one clean-up function cancels outstanding queries, removes every query but the session's (set to `anonymous`), clears the mutation cache, and resets the raise draft, its submitted IDs and its server errors (`RaiseDraftProvider` moved around `<Outlet/>` in RootLayout and keyed by the session state); the tab posts `expired`, and the session-ended screen shows as today (TanStack Query `cancelQueries`, `removeQueries`, `MutationCache.clear`; react.dev, *Preserving and Resetting State*, a `key`)
   **And** when the in-flight withdraw and raise then succeed, the cache and the draft stay empty: a success handler writes to the query cache or the draft only while the session query says `unlinked` or `linked`; going Back from the session-ended screen renders no customer data from memory; running the clean-up twice (a 401 racing sign-out) leaves the same state

2. **The idle limit ends the session in the browser without the network, shared across tabs** (R2; ASVS 5.0.0 V14.3.1)
   **Given** a signed-in SPA whose deadline has passed and the network unavailable
   **When** the timer fires, or the clock is advanced past the deadline without the timer firing and the page then becomes visible or gains focus
   **Then** the SPA runs the clean-up of criterion 1 without posting `expired` and shows the session-ended screen with no request made (Vitest fake timers and `vi.setSystemTime`, `fetch` failing)
   **And** each API response other than a 401 or an `anonymous` session posts `activity`; a tab receiving `activity` moves its deadline to 15 minutes from then and closes an open warning dialog, so no tab ends a session another tab is renewing; a tab that missed messages (suspended, or opened later) ends only itself on its stale deadline, never another tab, because the idle-limit end posts nothing; the `shortcut:` comment on the per-tab timer is removed (HTML Living Standard, `BroadcastChannel` and page visibility)

3. **Sign-out clears before it contacts the server, so a failure leaves nothing** (R3; ASVS 5.0.0 V14.3.1)
   **Given** a signed-in SPA with customer data and a draft, and a second tab of the same session
   **When** the person chooses Sign out, from the header or the inactivity dialog
   **Then** the tab runs the clean-up of criterion 1 (without posting `expired`) and shows the start page before the request token is fetched; once the token fetch succeeds, it posts `signed-out` and then submits the logout form as today (AD-7)
   **And** when the token fetch fails with no response or a 5xx, nothing comes back into memory, nothing is posted, the other tab keeps its session, and the start page shows a `ProblemNotice` in RootLayout's existing slot (its `isSignedIn` guard dropped): message "We couldn't finish signing you out.", next step "Try again.", with a "Sign out" button that retries the token fetch and submit; when the token fetch answers 401 the session is already over: the start page shows with no notice and nothing is posted; the inactivity dialog's `signOutFailed` prop is removed (OWASP Session Management Cheat Sheet, logout; WCAG 2.2 SC 4.1.3)

4. **Another tab clears when one tab's session ends** (R2, R3; ASVS 5.0.0 V14.3.1)
   **Given** two app instances sharing one channel, each signed in with customer data in memory and the receiving tab with a request in flight
   **When** the receiving tab gets `signed-out` or `expired`
   **Then** it runs the clean-up of criterion 1 without posting anything, and shows the start page after `signed-out` or the session-ended screen after `expired`, initiating no new request; the in-flight request's late result restores nothing
   **And** an `activity` arriving after the tab has ended does not restore the session or start a timer; a message other than the three is ignored on both the sending and receiving side; each instance closes its channel on unmount (tested in Vitest with two app instances)

5. **A page restored from the back/forward cache reloads** (R3; ASVS 5.0.0 V14.3.1)
   **Given** the SPA left by any navigation away while signed in, such as sign-out's form POST
   **When** the browser restores it by Back and fires `pageshow` with `persisted` true
   **Then** the SPA calls `location.reload()`, so nothing from memory shows: the reloaded page fetches the session, an ended session lands on the start page and a live one loads its route again, without the raise draft; a `pageshow` with `persisted` false does nothing (web.dev, *Back/forward cache*, "Update stale or sensitive data after bfcache restore"; HTML Living Standard, `PageTransitionEvent.persisted`)

6. **The not-eligible hint says "card transaction"** (R34; NFR-CODE-3)
   **Given** a settled debit that is not a card transaction (a fee, a payment to someone else)
   **When** its row shows the not-disputable hint
   **Then** the hint reads "Not a card transaction. Contact the bank about this transaction." (docs/glossary.md, Transaction and Disputable transaction), in `notDisputableHint`, its Vitest expectation, the copy deck in `docs/ux/EXPERIENCE.md` and the e2e expectations that name the old text

7. **The README says OTPs go by SMS in a real deployment** (R39; NFR-SEC-11)
   **Given** the README's seed-identities section, where the OTP arrives in Mailpit
   **When** it is read
   **Then** it says that Mailpit stands in for SMS in this demo, that a real deployment sends the OTP by SMS to the cellphone number on the bank's record, and that the AWS Mailpit sits behind basic auth (the first-deploy check stays in `infra/terraform/README.md`, post-deploy check 3)
   **And** the README's Runbook section lists the new procedure beside the other three

8. **The runbook covers a transient Kafka outage** (R28; NFR-RES-4, 7.10 criterion 5 Kafka row)
   **Given** `docs/runbook.md`, a new procedure 4 appended after "Reset the local stack"
   **When** the docs agent follows it on a fresh stack: record the baseline count of `Pending` and `Failed` outbox rows, raise a dispute, stop Redpanda, raise another, then `docker compose start redpanda`
   **Then** the procedure shows where the outage is seen (the Worker's retry warnings and failed producer spans in the Aspire Dashboard, `/api/health/ready` still healthy, raising still answering 201), how to count the `Pending` rows and their attempts with procedure 1's read-only `psql -U qa_readonly`, the non-destructive recovery (start Redpanda; no row update, no `down -v`), and the drain check, re-run for at most 5 minutes: the `Pending` count back to the baseline and both raised disputes' events read with procedure 1's `rpk topic consume`, in the order they were written for each dispute (this single-relay recovery only; AD-17's at-least-once delivery and its handover exception stand)
   **And** it says that zero-attempt `Pending` rows are the transient backlog the relay sends by itself; `Pending` rows with 1 to 4 attempts are a permanent refusal in backoff that turns `Failed` at 5; a `Failed` row needs procedure 1, and procedure 1 is never used for `Pending` rows

## Boundaries

- Must not change: the session-ended screen's copy and route, the inactivity warning's copy and its 2-minute lead, the logout form POST and its antiforgery token (AD-7), the 403 `linking-required` handling, the API contract and `frontend/src/api/generated/`.
- No browser storage (localStorage, sessionStorage, IndexedDB) is added: customer data stays in memory only. No service worker, no new package, no new route, no polling.
- Out of scope: server-side session changes (C-F3, C-F4, production path); the GET login ending a session (A-F7, accepted); the AWS first-deploy check of Mailpit's basic auth (P46).

## Supervisor checks

Deliberate-break proofs, run by the supervisor in a throwaway copy, not by the verifier.

- Drop `MutationCache.clear` from the clean-up: criterion 1's mutation-cache assertion fails.
- Remove the `key` from `RaiseDraftProvider`: criterion 1's draft assertion fails.
- Drop the signed-in check before the withdraw's cache write: criterion 1's late-withdraw test finds the dispute cached.
- Count an `anonymous` session response as activity: criterion 2's test (no `activity` posted, deadline not moved) fails.
- Remove the `visibilitychange` and `focus` deadline checks: criterion 2's clock-advance test fails.
- Ignore `activity` messages: criterion 2's second-tab test fails.
- Clear only after the token fetch succeeds: criterion 3's failed-fetch test finds the data still cached.
- Post `signed-out` before the token fetch: criterion 3's failed-fetch test sees the message.
- Ignore `expired` messages: criterion 4's second-tab test fails.
- Reload on every `pageshow`: criterion 5's `persisted` false test fails.

## Build facts

- Writers, each only its own files:
  - Frontend builder: `frontend/` only (criteria 1 to 6).
  - Docs agent (as in stories 7.6 and 7.7): `docs/ux/EXPERIENCE.md` (the copy deck: criterion 3's notice and criterion 6's hint; a "Sign-out failed" State Patterns row; the Session ended rows also triggered by the idle limit in the browser; the raise screen's Back and forward note that a back/forward-cache restore reloads, so the draft is not kept), one line in `docs/architecture/architecture.md` AD-7 Expiry (the SPA also ends the session itself 15 minutes after the last response and shares that limit across tabs through `BroadcastChannel`), `README.md` (criterion 7), `docs/runbook.md` (criterion 8) and `docs/verification/7.14-run.md` (new).
  - QA: `e2e/tests/spa-transactions.spec.ts` (the hint text near lines 228 and 249; the sign-out failure test near lines 374 to 387, which expects the old alert text and a header "Sign out" button, now the start page's `ProblemNotice` and its "Sign out" button) and `e2e/tests/spa-dispute-status-badges.spec.ts` (the hint text near line 50).
- Flags: high-stakes (customer data in the browser at session end).
- Lane: M1.
- Mechanisms and sources: TanStack Query `QueryClient.cancelQueries`, `removeQueries` (<https://tanstack.com/query/latest/docs/reference/QueryClient>) and `MutationCache.clear` (<https://tanstack.com/query/latest/docs/reference/MutationCache>); resetting state with a `key` (<https://react.dev/learn/preserving-and-resetting-state>); `BroadcastChannel` (<https://html.spec.whatwg.org/multipage/web-messaging.html#broadcasting-to-other-browsing-contexts>); page visibility (<https://html.spec.whatwg.org/multipage/interaction.html#page-visibility>); `pageshow` and `persisted` (<https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-pagetransitionevent-interface>) with web.dev's reload on a persisted restore (<https://web.dev/articles/bfcache>); OWASP Session Management Cheat Sheet (<https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html>); OWASP ASVS 5.0.0 V7.3, V14.3 (<https://github.com/OWASP/ASVS>); WCAG 2.2 SC 4.1.3 Status Messages; Vitest fake timers and `vi.setSystemTime` (<https://vitest.dev/api/vi.html#vi-usefaketimers>).
- Packages: none.
- Touches (frontend):
  - `frontend/src/api/queryClient.ts` (the 401 and `anonymous` paths call the clean-up), `frontend/src/app/InactivityWarning.tsx` (deadline, channel, dead prop), `frontend/src/app/useSignOut.ts` (clear first, token-fetch 401, post then submit), `frontend/src/app/RootLayout.tsx` (the notice slot, `RaiseDraftProvider` keyed around `<Outlet/>`), `frontend/src/app/router.tsx` (the provider moves out of the root route), the success handlers in `ReviewScreen.tsx`, `WithdrawDialog.tsx` and the linking screens, and their tests (criteria 1 to 5)
  - `frontend/src/features/transactions/notDisputableHint.ts`, `TransactionsScreen.test.tsx` (criterion 6)
- `BroadcastChannel` in Vitest: Node 24 has a global `BroadcastChannel`; the builder first confirms it is visible in the jsdom environment and that delivery is asynchronous (await it, close channels on unmount), before writing criteria 2 and 4's tests.

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- verification — docs/verification/7.10-prr.md, Findings E-F8, F-F20, F-F6, F-F12, and Disposition
- verification — docs/verification/7.14-challenge.md, docs/verification/7.14-prebuild-review.md, docs/verification/7.14-prebuild-security.md
- story — docs/tickets/epic-7-release/tickets/story-7.7-readme-runbook-and-onboarding.md (the README and runbook bounds and run record)
- experience — docs/ux/EXPERIENCE.md, Information Architecture (Start page, Session ended), State Patterns (Unexpected error, Session ended), the raise screen's Back and forward, and Copy deck (Not disputable)
- glossary — docs/glossary.md, Transaction, Disputable transaction
- architecture — docs/architecture/architecture.md, AD-7, AD-17, AD-22
- prd — docs/prd/prd.md, NFR-SEC-11, NFR-CODE-3, NFR-RES-4

## Notes

- Risk medium (proposed): a wrong clean-up can sign people out of a live session or leave customer data on screen after sign-out; nothing here serves real customers, and UJ-1's sign-out in the CI journeys exercises the real flow.
- Decision: 2026-10-31 (human, 7.10 disposition) — E-F8, F-F20, F-F6 and F-F12 are fixed in this story; the frontend builder owns E-F8 and F-F20, and the README (F-F6) and runbook (F-F12) are docs in the same story.
- Decision: 2026-10-31 (Sofia, 7.14 write) — the idle limit is shared across tabs through `BroadcastChannel`, the upgrade the existing `shortcut:` comment names.
- Decision: 2026-10-31 (Sofia, 7.14 write) — after a sign-out, in this tab or another, the SPA shows the start page, not the session-ended screen; the session-ended screen stays for a 401, an `anonymous` session answer and the idle limit (EXPERIENCE.md, Information Architecture, Start page; UJ step 7).
- Decision: 2026-10-31 (Sofia, 7.14 refine) — a failed sign-out is a `ProblemNotice`, the pattern EXPERIENCE.md gives an unexpected error (State Patterns, Unexpected error), in RootLayout's existing slot, with copy true for both no response and a 5xx; it replaces the earlier `role="alert"` paragraph.
- Decision: 2026-10-31 (supervisor ruling, 7.14 pre-build review) — `signed-out` is posted only after the token fetch succeeds, immediately before the form submit, so other tabs never show a signed-out start page while the server session is still live.
- Decision: 2026-10-31 (Sofia, 7.14 refine) — the idle-limit end posts nothing: every live tab shares one deadline through `activity`, so each reaches it by itself, and a tab with a stale deadline (suspended, opened later) cannot end another tab's live session. `expired` is posted only when the server said the session is over (a 401 or an `anonymous` session answer).
- Decision: 2026-10-31 (Sofia, 7.14 refine) — a back/forward-cache restore reloads the page (web.dev's simplest documented option), instead of clearing and refetching in place; the reload also drops the raise draft, which the docs agent notes in EXPERIENCE.md.
- Decision: 2026-10-31 (Sofia, 7.14 refine) — the deadline is a wall-clock time checked on the timer, `visibilitychange` and `focus`, because a 15-minute `setTimeout` drifts across OS sleep and background-tab throttling. A separate `pageshow` check is not needed: a persisted restore reloads (criterion 5).
- Collision: touches `frontend/`, `e2e/` (QA's expectations), `README.md`, `docs/runbook.md`, `docs/ux/EXPERIENCE.md` and one AD-7 line in `docs/architecture/architecture.md`; 7.12 and 7.13 touch none of these files' sections, so all three may build in parallel.
