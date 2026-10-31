---
id: 14
type: story
title: "Customer data cleared at session end, copy and operations docs"
parent: epic-7-release
covers: [R2, R3, R34, R39, R28]
after: [7]
hitl: false
risk: medium
refined: false
---

# Customer data cleared at session end, copy and operations docs

## Description

Fixes the four findings of the production readiness review that the human disposed as "fix, story 7.14" on 2026-10-31 (`docs/verification/7.10-prr.md`, Disposition): E-F8, F-F20, F-F6 and F-F12. The SPA holds customer data only in memory (the query cache and the raise draft); E-F8 makes every way a session ends clear it at once, without needing the network: a 401, the idle limit reached in every open tab, sign-out (cleared before any request, so a failed sign-out leaves nothing behind), another tab's sign-out or expiry, and a page restored from the browser's back/forward cache. The tabs share one idle timer through `BroadcastChannel`, which replaces the per-tab timer shortcut and keeps a tab from ending a session another tab is still using. F-F20 changes one hint to the glossary's "card transaction". F-F6 adds the real-SMS note to the README, and F-F12 adds a runbook procedure for a transient Kafka outage. The frontend part is the frontend builder's; the README, runbook and copy-deck lines are docs, written as in story 7.7.

## Requirements

Verify: Vitest tests pass with `npm run typecheck`, `npm run lint`, `npm run format` and `npm test` in `frontend/`, no new suppression; the five journeys and the regression project pass in CI's `e2e` job (QA updates the two copy expectations of criterion 6); the docs agent runs the new runbook procedure on a fresh stack under `scripts/test-lock.sh run stack 7.14 -- …` and records each command, its exit status and the observed rows in `docs/verification/7.14-run.md`, which holds no token, OTP or password; `scripts/secret-scan.sh` passes; `docs/runbook.md` stays at most 150 lines and the README at most 300 (story 7.7's bounds).

Inputs: none from a request. Fixed values:
- The idle limit the SPA applies itself: 15 minutes since the last API response other than a 401 in any tab of the same origin (the server's limit, AD-7); the warning at 13 minutes is unchanged.
- The `BroadcastChannel` name: `disputedesk-session`; messages: `activity`, `expired`, `signed-out`, nothing else, and no customer data in any message.

1. **A 401 clears the customer data and the draft** (R2; ASVS 5.0.0 V14.3.1)
   **Given** a signed-in SPA with transactions, disputes and a dispute detail in the query cache and a half-filled raise draft
   **When** any request answers 401
   **Then** outstanding queries are cancelled, the query cache holds no query but the session's (set to `anonymous`), the raise draft, its submitted IDs and its server errors are empty, and the session-ended screen shows as today (TanStack Query, `cancelQueries` and `removeQueries`)
   **And** going Back from the session-ended screen renders no customer data from memory

2. **The idle limit ends the session in the browser without the network** (R2; ASVS 5.0.0 V14.3.1)
   **Given** a signed-in SPA, its last API response 15 minutes ago in every open tab, and the network unavailable
   **When** the idle limit passes
   **Then** the SPA clears as in criterion 1 and shows the session-ended screen with no request made, the warning having appeared at 13 minutes as before (Vitest fake timers, `fetch` failing)
   **And** a response in another tab within the 15 minutes (an `activity` message) restarts this tab's timer, so no tab ends a session another tab is using; the `shortcut:` comment on the per-tab timer is removed (HTML Living Standard, `BroadcastChannel`)

3. **Sign-out clears before it contacts the server, so a failure leaves nothing** (R3; ASVS 5.0.0 V14.3.1)
   **Given** a signed-in SPA with customer data and a draft
   **When** the person chooses Sign out, from the header or the inactivity dialog
   **Then** the data and draft are cleared as in criterion 1 and the start page shows before the request token is fetched; then the logout form is submitted as today (AD-7)
   **And** when the token fetch fails (network down), nothing comes back into memory, and the start page shows in a `role="alert"` message: "We couldn't finish signing you out. Check your connection and try again." with a "Sign out" button that retries (OWASP Session Management Cheat Sheet, logout; WCAG 2.2 SC 4.1.3)

4. **Every other tab clears when one tab's session ends** (R2, R3; ASVS 5.0.0 V14.3.1)
   **Given** two tabs of the SPA signed in to one session, each with customer data in memory
   **When** one tab signs out (it posts `signed-out` before submitting) or ends the session on a 401 or the idle limit (it posts `expired`)
   **Then** the other tab clears as in criterion 1 and shows the start page after `signed-out`, or the session-ended screen after `expired`, with no request made (tested with two app instances sharing one channel)

5. **A page restored from the back/forward cache shows no customer data** (R3; ASVS 5.0.0 V14.3.1)
   **Given** the SPA left by the sign-out navigation, then restored by Back from the browser's back/forward cache
   **When** the browser fires `pageshow` with `persisted` true
   **Then** the SPA clears the customer data and the draft at once and refetches the session; a session that is no longer signed in lands on the start page, and one still signed in loads its data again (web.dev, *Back/forward cache*, "Update stale or sensitive data after bfcache restore"; HTML Living Standard, `PageTransitionEvent.persisted`)

6. **The not-eligible hint says "card transaction"** (R34; NFR-CODE-3)
   **Given** a settled debit that is not a card transaction (a fee, a payment to someone else)
   **When** its row shows the not-disputable hint
   **Then** the hint reads "Not a card transaction. Contact the bank about this transaction." (docs/glossary.md, Transaction and Disputable transaction), in `notDisputableHint`, its Vitest expectation, the copy deck in `docs/ux/EXPERIENCE.md` and the two e2e expectations that name the old text

7. **The README says OTPs go by SMS in a real deployment** (R39; NFR-SEC-11)
   **Given** the README's seed-identities section, where the OTP arrives in Mailpit
   **When** it is read
   **Then** it says that Mailpit stands in for SMS in this demo, that a real deployment sends the OTP by SMS to the cellphone number on the bank's record, and that the AWS Mailpit sits behind basic auth (the first-deploy check stays in `infra/terraform/README.md`, post-deploy check 3)

8. **The runbook covers a transient Kafka outage** (R28; NFR-RES-4, 7.10 criterion 5 Kafka row)
   **Given** `docs/runbook.md`, a new procedure 4 after "Core banking unavailable"
   **When** the docs agent follows it on a fresh stack: raise a dispute, stop Redpanda, raise another, then start Redpanda again
   **Then** the procedure shows where the outage is seen (the Worker's retry warnings and failed producer spans in the Aspire Dashboard, `/api/health/ready` still healthy, raising still answering 201), how to count the `Pending` rows and their attempts with the read-only role, the non-destructive recovery (`docker compose start redpanda`; no row update, no `down -v`), and the check that the backlog drains (no `Pending` row left, the events on the topic in per-dispute order)
   **And** it says that zero-attempt `Pending` rows are a transient backlog the relay sends by itself, while a `Failed` row needs procedure 1, and procedure 1 is never used for `Pending` rows

## Boundaries

- Must not change: the session-ended screen's copy and route, the inactivity warning's copy and timing, the logout form POST and its antiforgery token (AD-7), the 403 `linking-required` handling, the API contract and `frontend/src/api/generated/`.
- No browser storage (localStorage, sessionStorage, IndexedDB) is added: customer data stays in memory only. No service worker, no new package, no new route.
- Out of scope: server-side session changes (C-F3, C-F4, production path); the GET login ending a session (A-F7, accepted); the AWS first-deploy check of Mailpit's basic auth (P46).

## Supervisor checks

Deliberate-break proofs, run by the supervisor in a throwaway copy, not by the verifier.

- Remove the draft reset from the session-end clean-up: criterion 1's draft assertion fails.
- Send the logout form before clearing: criterion 3's failed-fetch test finds the data still cached.
- Ignore `expired` messages: criterion 4's second-tab test fails.
- Restart the idle timer on a 401 response: criterion 2's test fails.

## Build facts

- Owner: frontend (frontend-builder) for criteria 1 to 6 under `frontend/`; docs (a docs agent, as in stories 7.6 and 7.7) for criteria 7 and 8, the copy-deck lines of criteria 3 and 6, and `docs/verification/7.14-run.md`; QA for the two e2e copy expectations of criterion 6, because `e2e/` is QA's.
- Flags: high-stakes (customer data in the browser at session end).
- Lane: M1.
- Mechanisms and sources: TanStack Query `QueryClient.cancelQueries`, `removeQueries`, `clear` (<https://tanstack.com/query/latest/docs/reference/QueryClient>); `BroadcastChannel` (<https://html.spec.whatwg.org/multipage/web-messaging.html#broadcasting-to-other-browsing-contexts>); `pageshow` and `persisted` (<https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-pagetransitionevent-interface>), web.dev back/forward cache guidance (<https://web.dev/articles/bfcache>); OWASP Session Management Cheat Sheet (<https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html>); OWASP ASVS 5.0.0 V7.3, V14.3 (<https://github.com/OWASP/ASVS>); WCAG 2.2 SC 4.1.3 Status Messages; Vitest fake timers (<https://vitest.dev/api/vi.html#vi-usefaketimers>).
- Packages: none.
- Touches:
  - `frontend/src/app/InactivityWarning.tsx`, `frontend/src/api/queryClient.ts`, `frontend/src/app/useSignOut.ts`, `frontend/src/app/router.tsx` or `frontend/src/features/disputes/raise/RaiseDraft.tsx` (whichever holds the draft reset), the start page for criterion 3's message, and their tests (criteria 1 to 5)
  - `frontend/src/features/transactions/notDisputableHint.ts`, `TransactionsScreen.test.tsx` (criterion 6)
  - `docs/ux/EXPERIENCE.md` (the copy deck: criterion 3's message, criterion 6's hint)
  - `e2e/tests/spa-transactions.spec.ts`, `e2e/tests/spa-dispute-status-badges.spec.ts` (criterion 6's text only, by QA)
  - `README.md` (criterion 7), `docs/runbook.md` (criterion 8), `docs/verification/7.14-run.md` (new)

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- verification — docs/verification/7.10-prr.md, Findings E-F8, F-F20, F-F6, F-F12, and Disposition
- story — docs/tickets/epic-7-release/tickets/story-7.7-readme-runbook-and-onboarding.md (the README and runbook bounds and run record)
- experience — docs/ux/EXPERIENCE.md, State Patterns (Session about to end, Session ended) and Copy deck (Not disputable)
- glossary — docs/glossary.md, Transaction, Disputable transaction
- architecture — docs/architecture/architecture.md, AD-7, AD-17, AD-22
- prd — docs/prd/prd.md, NFR-SEC-11, NFR-CODE-3, NFR-RES-4

## Notes

- Risk medium (proposed): a wrong clean-up can sign people out of a live session or leave customer data on screen after sign-out; nothing here serves real customers, and UJ-1's sign-out in the CI journeys exercises the real flow.
- Decision: 2026-10-31 (human, 7.10 disposition) — E-F8, F-F20, F-F6 and F-F12 are fixed in this story; the frontend builder owns E-F8 and F-F20, and the README (F-F6) and runbook (F-F12) are docs in the same story.
- Decision: 2026-10-31 (Sofia, 7.14 write) — the idle timer is shared across tabs through `BroadcastChannel`, the upgrade the existing `shortcut:` comment names; without it, a tab ending the session on its own timer could end a session another tab is using.
- Decision: 2026-10-31 (Sofia, 7.14 write) — after a sign-out, in this tab or another, the SPA shows the start page, not the session-ended screen, whose copy is about inactivity; the session-ended screen stays for a 401 and the idle limit.
- Decision: 2026-10-31 (Sofia, 7.14 write) — the failed sign-out message is new copy, written here and added to the copy deck by the docs agent; there is no design for it: it sits above the start page's sign-in links, styled as the inactivity dialog's error message (`text-destructive`).
- Collision: touches `frontend/`, `e2e/` (two expectations), `README.md`, `docs/runbook.md` and `docs/ux/EXPERIENCE.md` only; 7.12 and 7.13 touch none of them, so all three may build in parallel.
