---
id: 4
type: story
title: "Linking screens for details, OTP, lockout and rate limits"
parent: epic-6-linking
covers: [R4, R7, R8, R9, R40]
after: [3, 2.4, 3.7]
hitl: false
risk: high
refined: false
---

# Linking screens for details, OTP, lockout and rate limits

## Description

Adds screens 2a (`/link`, the details form) and 2b (`/link/otp`, the OTP form) for an unlinked login, with the wrong-code, locked and rate-limited states, all routed by the problem `type` (AD-31). It uses the generated start and verify mutations, the existing form and error-summary pattern of 4a, the one live region and `SessionGate`. The OTP is one pasteable input with no auto-submit. On a link the session cache becomes `linked` before the SPA goes to `/transactions`, so `SessionGate` does not send the customer back to `/link`. `ApiError` keeps the `Retry-After` header so the rate-limited state can say when to try again; the full-queue 429 (story 6.5) has none and says "Try again later." with no wait. The two routes replace the not-found screen an unlinked login sees today.

## Requirements

Verify: Vitest component tests pass, with API responses mocked by MSW from `openapi.json` shapes (stories 6.1, 6.2, 6.3 and 6.5's regenerated client); every new screen state passes the axe-core check the component tests already run; `npm run typecheck`, `npm run lint` and `npm test` pass in `frontend/` with no new suppression.

Inputs:
- `idNumber` (2a): required; exactly 13 ASCII digits `0`–`9` whose last digit is the Luhn check digit of the first 12 (ISO/IEC 7812-1, Annex B; story 6.1, criterion 2); not trimmed or normalised; `inputmode="numeric"`, `autocomplete="off"`, `spellcheck="false"`.
- `accountNumber` (2a): required; 6 to 16 ASCII digits `0`–`9`; not trimmed or normalised; `inputmode="numeric"`, `autocomplete="off"`, `spellcheck="false"`.
- `code` (2b): required; spaces are removed (an SMS code is often pasted as "481 516"), then exactly 6 ASCII digits `0`–`9`, leading zeros kept; sent as a JSON string (story 6.2, criterion 5); `spellcheck="false"`.
- Response values read: the 422 `otp-incorrect` extension `attemptsRemaining` (used only when it is an integer 1 or 2); the `Retry-After` header on a 429, as delay-seconds (an integer 1 to 86 400) or an HTTP-date (RFC 9110 §10.2.3) 1 to 86 400 seconds ahead; `0`, a delay above 86 400 seconds, a date that is now, past or more than 86 400 seconds ahead, an unparseable value or a missing header counts as no `Retry-After`; the 400 `validation` `errors` keys `idNumber`, `accountNumber` and `code`.
- No ID number, account number or OTP is put in the URL (path, query or `location.hash`), the history state, `localStorage`, `sessionStorage`, a query cache or the mutation cache (both mutations set `gcTime: 0`; TanStack Query, *useMutation*), or a browser span attribute; no screen text repeats a typed value.

1. **Two routes for an unlinked login, and nothing kept between them** (R9; FR-9, AD-10, AD-31)
   **Given** the routes `/link` and `/link/otp` inside `SessionGate allow="unlinked"` (TanStack Router, *Code-based routing*)
   **When** each state opens each route, and an unlinked login opens `/link/otp` directly or reloads it
   **Then** an unlinked login sees screen 2a at `/link` and 2b at `/link/otp`; a linked customer is sent to `/transactions` by `SessionGate` before any linking request (MSW records no request under `/api/v1/linking`); an anonymous visitor goes to `/`; the not-found screen no longer shows at `/link`
   **And** a direct open or reload of `/link/otp` shows the same 2b screen, neutral message included, because the challenge lives on the server against the login: nothing from 2a is needed, and a verify with no challenge answers like a wrong code (story 6.2, criterion 4)
   **And** after the flow both storages are empty and no history entry's state, URL or `location.hash` holds a submitted value, as `ReviewScreen.test.tsx` checks; after a 202 has moved the SPA to `/link/otp`, and again after a 204, the serialised `queryClient.getMutationCache().getAll()` holds no ID number, account number or code; with `startTracing` and an in-memory span processor (as `startTracing.test.ts` does), a start and a verify produce no span attribute holding any of them (AD-20; ASVS 5.0 V14.3, V16.2); a test asserts `spellcheck="false"` on all three inputs (WHATWG HTML, *spellcheck*; ASVS 5.0 V14.2)
   **And** the comments in `SessionGate` and `router.tsx` about linking having no screen are removed

2. **The details form checks its bounds and handles every answer** (R4; FR-4, NFR-CODE-6)
   **Given** screen 2a, `h1` "Link your login", with "ID number" and "Account number" and the button "Continue", built with shadcn/ui `Field`, React Hook Form's `Controller` and a zod schema, each field wiring `aria-invalid` and `aria-describedby` itself (AD-31; shadcn/ui, *Forms*)
   **When** the customer submits an empty form, a 12-digit and a 14-digit ID number, a 13-digit number with a wrong check digit, a number with a space or a letter, an account number of 5 and of 17 digits, and then valid values
   **Then** each bad value shows its message under the field and in the error summary "Check the details below", which links to each field and takes focus, with no request sent (GOV.UK Design System, *Error summary*; EXPERIENCE.md, Field errors): "Enter a valid 13-digit ID number." and "Enter a valid account number of 6 to 16 digits."; the field keeps what the customer typed, and the copy never repeats it
   **And** valid values send one start through the generated mutation with `retry: false`, the body exactly `{ "idNumber": …, "accountNumber": … }`, carrying `X-XSRF-TOKEN`; while pending "Continue" has `aria-disabled` and a second press sends nothing, blocked synchronously as `ReviewScreen.tsx` does
   **And** a 400 `validation` naming `idNumber` or `accountNumber` shows that field's message and the summary; a 400 naming neither, a 5xx, a network failure or any other 4xx shows the problem notice "Something went wrong on our side. Try again." with the `traceId`, the values kept; the problem's `title` and `detail` are never shown (AD-3)

3. **The neutral confirmation after any 202** (R4; FR-4)
   **Given** a start that returns 202, for details that match, match nothing or match an already-linked customer (the same empty 202 in each case)
   **When** it arrives
   **Then** the SPA goes to `/link/otp`, whose `h1` "Enter your OTP" takes focus (`usePageHeading`), and the screen shows at its top, always and only: "If these details match our records, we've sent a code to the cellphone number we hold for you." with "The code expires 5 minutes after we send it." and no live countdown; no masked cellphone or other hint is shown (EXPERIENCE.md, Linking without disclosure)
   **And** "Didn't get a code? Start again" is a link to `/link` that sends no request; the old challenge is superseded only by the next successful start (AD-12), so returning to 2a and leaving changes nothing on the server

4. **One OTP input that accepts paste and autofill, and never submits itself** (R7, R40; FR-5, NFR-CODE-6)
   **Given** screen 2b's one plain visible shadcn/ui `Input` labelled "6-digit one-time code", with `autocomplete="one-time-code"` (WHATWG HTML, *Autofill*), `inputmode="numeric"` and no `maxlength`, no visual cells and no hidden input, and the button "Verify"
   **When** the customer pastes "481516" and "481 516", types six digits, types five, types a letter, and presses Verify
   **Then** paste and typing both fill the one input, and nothing is sent until Verify is pressed, so a mistyped digit never spends an attempt (WCAG 2.2 SC 3.3.8 *Accessible Authentication (Minimum)*, SC 3.2.2 *On Input*); six digits with or without spaces send one verify through the generated mutation with `retry: false`, body `{ "code": "481516" }`, carrying `X-XSRF-TOKEN`; Verify is guarded against a second press while pending as in criterion 2
   **And** five digits or a letter show "Enter the 6-digit code." under the field and in the error summary that takes focus, with no request; a 400 `validation` naming `code` shows the same; any other 400, a 5xx, a network failure or any other 4xx shows the problem notice with the `traceId`, the code kept

5. **A wrong code shows the tries left; a lockout shows one screen for every case** (R7; FR-6, FR-7)
   **Given** verify answers in sequence, built from story 6.2's shapes: 422 `otp-incorrect` with `attemptsRemaining` 2, then 1, then 409 `linking-locked`; and separately a login with no challenge (422 with 2 every time) and a correct third code (204)
   **When** each arrives
   **Then** each 422 empties the input, moves focus to it, and shows "That code didn't work. You have 2 tries left." then "That code didn't work. You have 1 try left." as `role="alert"`; the same copy shows for a wrong, an expired and an unsent code and for a login with no challenge, because the API answers them alike; a 422 whose `attemptsRemaining` is missing or not 1 or 2 shows "That code didn't work." with no count; `attemptsRemaining` is read only from `otp-incorrect`
   **And** a 409 `linking-locked` replaces the form with the locked state, one for every lockout, SIM-swap failure included: `h1` "We can't link you online" (focused), "Please contact the bank.", "If you think you mistyped your details or the code, you can start again." and the secondary button "Start again", which goes to `/link`; it shows no count (EXPERIENCE.md, Linking without disclosure, 5)
   **And** a correct third code after two wrong ones links (criterion 6)

6. **A link lands on Transactions and stays there** (R7, R9; FR-6, FR-9)
   **Given** a verify that returns 204, and separately a start or verify that returns 403 `already-linked` (the login was linked in another tab)
   **When** it arrives
   **Then** the session query's cache is set to `{ "state": "linked" }` with `setQueryData` (TanStack Query, *QueryClient.setQueryData*; `SessionResponse` has only `state`) before `navigate({ to: '/transactions', replace: true })`, so `SessionGate` renders Transactions instead of sending the customer back to `/link`; a test asserts the pathname stays `/transactions` and the transactions request is made, with no new sign-in
   **And** after a 204 "Your login is linked." is announced through the one `useAnnounce` live region, with no toast (EXPERIENCE.md, Accessibility Floor); after a 403 `already-linked` nothing is announced
   **And** this differs from criterion 1's path: there `SessionGate` redirects a session already known as linked before any linking request; here the API's 403 arrives during a mutation
   **And** the reverse holds too: when any request answers 403 `linking-required` while the session cache says `linked`, `queryClient.ts` sets the cache to `{ "state": "unlinked" }` before navigating to `/link`, as its 401 branch sets `anonymous`, so `SessionGate allow="unlinked"` shows 2a instead of bouncing to `/transactions` and back; `routing.test.tsx`'s linking-required test asserts the pathname stays `/link`, 2a shows and `/api/v1/transactions` is requested once

7. **Rate limited, with and without `Retry-After`** (R8; FR-8, AD-3)
   **Given** `ApiError` gains the response's `Retry-After` value (raw, or null), set in `customFetch` from `response.headers`, and one function turns it into seconds per RFC 9110 §10.2.3 (delay-seconds or HTTP-date, bounded as in Inputs)
   **When** a start or a verify gets 429 `rate-limited` with `Retry-After: 900`, with an HTTP-date 12 minutes ahead, with the gateway's `Retry-After: 1`, and with no `Retry-After` (story 6.5's full queue); and the reader gets each side of each bound: `0` and `1`, `60` and `61`, `86400` and `86401`, an HTTP-date one second ahead and one equal to now
   **Then** with a usable `Retry-After` the screen's notice reads "Too many attempts. Try again in {n} minutes." with `n` the wait rounded up to whole minutes ("15 minutes", "12 minutes", "1 minute" for 1 to 60 seconds, "2 minutes" for 61), as `role="alert"`; the submit button has `aria-disabled` and sends nothing until the exact wait in seconds has passed (not the rounded minutes), after which the notice goes and the button works again; the text never counts down (EXPERIENCE.md, Error and Edge States; Banned: countdowns); tests use Vitest fake timers; `0`, `86401` and a date equal to now are handled as no `Retry-After`
   **And** the fields stay editable while the button waits; the wait lives in the screen's component state, so moving between 2a and 2b or reloading drops it, and the server still enforces the limit
   **And** with no usable `Retry-After` the notice reads "Too many attempts. Try again later." and the button stays usable, because there is no wait to report (docs/problems/rate-limited.md); no copy tells a per-login, per-IP or full-queue limit apart (FR-8)
   **And** the same behaviour holds on 2a and 2b; the fields keep their values; `problems.ts` gains the slugs `already-linked`, `otp-incorrect`, `linking-locked` and `rate-limited`, and a number reader for `attemptsRemaining`

8. **Accessible linking screens** (R40; NFR-CODE-6)
   **Given** every state of 2a and 2b: empty, field errors, pending, wrong code, locked, rate limited with and without a wait, and the problem notice
   **When** each renders
   **Then** each passes the axe-core check, has one `h1` that sets `document.title` ("Link your login · DisputeDesk", "Enter your OTP · DisputeDesk", "We can't link you online · DisputeDesk"), visible labels, errors tied with `aria-describedby`, and "Can't link online? Contact the bank." in the same place on every state (WCAG 2.2 SC 3.2.6 *Consistent Help*); the header shows only the wordmark and Sign out (story 2.4); a direct load leaves focus alone and a route change focuses the `h1` (EXPERIENCE.md, Accessibility Floor)
   **And** in every error state (client validation, a server 400, 422, 409 and 429) the rendered text holds none of the typed values, and each error-summary link points at a field id (`#idNumber`), never at a value; 2a and 2b render the summary through one new `ErrorSummary` component

## Boundaries

- Must not change: the API (stories 6.1 to 6.3, 6.5); the 403 `linking-required` redirect to `/link` in `queryClient.ts` (only the cache update before it is added, criterion 6); the header and navigation (story 2.4); 4a's form behaviour, and the error summaries in `RaiseScreen.tsx` and `WithdrawDialog.tsx` stay where they are (moving them onto `ErrorSummary` is refactor-sweep work).
- `ApiError`'s existing callers keep working: the `Retry-After` value is an optional constructor argument.
- No new browser span or span attribute for linking: story 3.11's automatic fetch spans already cover the start and verify calls with the sanitised URL, method and status and no body, and stay as they are; no auto-submit; no six separate inputs, visual cells or custom paste handling.

## Supervisor checks

Deliberate-break proofs, run by the supervisor, not the verifier.

- In a throwaway copy, remove the `setQueryData` before the navigation to `/transactions`: criterion 6's "stays on `/transactions`" test fails.
- In a throwaway copy, treat a 429 with no `Retry-After` as a zero wait with the "{n} minutes" copy: criterion 7's full-queue test fails.
- In a throwaway copy, submit the OTP when the sixth digit is typed: criterion 4's no-request test fails.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: high-stakes (account linking, AGENTS.md Stakes tiers; set by the supervisor 2026-10-30 after the pre-build review).
- Lane: B.
- Mechanisms and sources: TanStack Router routes and `Navigate` (<https://tanstack.com/router/latest/docs/framework/react/routing/code-based-routing>, <https://tanstack.com/router/latest/docs/framework/react/guide/navigation>); the Orval-generated start and verify mutations; TanStack Query `setQueryData` (<https://tanstack.com/query/latest/docs/reference/QueryClient#queryclientsetquerydata>); TanStack Query `useMutation` `gcTime` (<https://tanstack.com/query/latest/docs/framework/react/reference/useMutation>); HTML `spellcheck` (<https://html.spec.whatwg.org/multipage/interaction.html#spelling-and-grammar-checking>); OWASP ASVS 5.0 V14.2, V14.3 and V16.2 (<https://github.com/OWASP/ASVS>); shadcn/ui forms with React Hook Form and zod (<https://ui.shadcn.com/docs/forms/react-hook-form>); GOV.UK error summary (<https://design-system.service.gov.uk/components/error-summary/>); Luhn check digit (ISO/IEC 7812-1, Annex B); HTML `autocomplete="one-time-code"` and `inputmode` (<https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#autofill>); `Retry-After` (RFC 9110 §10.2.3, <https://www.rfc-editor.org/rfc/rfc9110.html#section-10.2.3>); problem `type` matching (RFC 9457 §3.1.1; AD-3); WCAG 2.2 SC 3.3.8, 3.2.2 and 3.2.6 (<https://www.w3.org/TR/WCAG22/>); the one polite live region `useAnnounce` (story 2.4); MSW and the existing axe-core check.
- Packages: none.
- Touches:
  - `frontend/src/features/linking/` (new: the details and OTP screens, the locked and rate-limited states, the zod schemas with the Luhn check, `Retry-After` parsing, their tests)
  - `frontend/src/components/ErrorSummary.tsx` (new: the error summary 2a and 2b share)
  - `frontend/src/app/router.tsx` (`/link`, `/link/otp`, and its comment that linking has no screen) and `frontend/src/app/SessionGate.tsx` (comment only)
  - `frontend/src/api/queryClient.ts` and its tests (a 403 `linking-required` sets the session cache to `unlinked` before navigating)
  - `frontend/src/app/routing.test.tsx` (`/link` and `/link/otp` now show the screens to an unlinked login and send a linked customer to `/transactions`, replacing every not-found expectation for them; the linking-required test asserts no loop)
  - `frontend/src/api/mutator.ts` and `mutator.test.ts` (`ApiError` keeps `Retry-After`)
  - `frontend/src/api/problems.ts` and `problems.test.ts` (four slugs, the `attemptsRemaining` reader)
  - `frontend/src/test/support.tsx` (start and verify MSW helpers)
  - `e2e/tests/spa-transactions.spec.ts` (QA stage: an unlinked login at `/link` now sees "Link your login", and an anonymous visit to `/link` lands on the start page `/`, neither the not-found screen)
- Reuses, not rewrites: 4a's `Field` form and error-summary pattern (lifted into `ErrorSummary` for 2a and 2b), `ProblemNotice`, `usePageHeading`, `useAnnounce`, the synchronous double-submit guard of `ReviewScreen.tsx`.

## References

- parent — docs/tickets/epic-6-linking/epic-6-linking.md, section Requirements
- design — docs/ux/mockups/link.html
- design — docs/ux/mockups/link-errors.html
- design — docs/ux/mockups/link-otp.html
- design — docs/ux/mockups/link-otp-wrong.html
- design — docs/ux/mockups/link-locked.html
- design — docs/ux/mockups/link-rate-limited.html
- experience — docs/ux/EXPERIENCE.md, Copy deck, Error and Edge States, Linking without disclosure, Accessibility Floor
- architecture — docs/architecture/architecture.md, AD-3, AD-10, AD-12, AD-31
- prd — docs/prd/prd.md, FR-4 to FR-9
- problems — docs/problems/rate-limited.md
- verification — docs/verification/6.4-challenge.md
- verification — docs/verification/6.4-prebuild-review.md
- verification — docs/verification/6.4-prebuild-security.md

## Notes

- Risk high (account linking): a screen that hints whether details matched breaks FR-4's non-disclosure, and a link that bounces back to `/link` blocks every new customer; beyond the criteria, the supervisor's `setQueryData` break proves the bounce test.
- Input (from 2.4 QA, 2026-10-28): until this story, an unlinked login lands on `/link`, which renders the not-found screen ("We can't find that dispute", with "Go to your disputes" leading to the also-unbuilt `/disputes`). This story's route replaces it.
- Handoff (2026-10-30, 6.1 refine): start can also answer 429 `rate-limited` without `Retry-After` when the linking queue is full (story 6.5); the rate-limited screen handles it with no countdown (criterion 7).
- Decision: 2026-10-30 (Sofia, refine, 6.4 challenge) — the OTP is one input with no `maxlength` and no auto-submit, as EXPERIENCE.md's OTP input row says; `link-otp.html`'s note "Verify (or the sixth digit) links the login" predates that row and is not followed.
- Decision: 2026-10-30 (Sofia, refine, 6.4 challenge) — `/link/otp` opened directly or reloaded shows the normal 2b screen; nothing from 2a is carried, because the challenge is held server-side against the login.
- Assumption: 2026-10-30 (Sofia, refine) — new copy where EXPERIENCE.md has none: "Enter a valid account number of 6 to 16 digits.", "Enter the 6-digit code.", "You have 1 try left." (singular), "Try again in 1 minute." (singular), "That code didn't work." (no usable count) and "Too many attempts. Try again later." (no `Retry-After`).
- Assumption: 2026-10-30 (Sofia, refine) — a field with a format error keeps what the customer typed (GOV.UK error pattern); `link-errors.html`'s "the field is cleared" is read as "the value is never echoed back" (EXPERIENCE.md, Field errors).
- Assumption: 2026-10-30 (Sofia, refine) — spaces are removed from the pasted OTP before the 6-digit check; nothing else is normalised.
- Decision: 2026-10-30 (supervisor, pre-build review 5) — the story is high-stakes (account linking): the security pre-build review ran (`6.4-prebuild-security.md`), and the human reviews the diff before the commit.
- Decision: 2026-10-30 (Sofia, pre-build review 6) — 2a and 2b are the third and fourth error summary, so they share one new `ErrorSummary` component (AGENTS.md: abstract on the third real case); the copies in `RaiseScreen.tsx` and `WithdrawDialog.tsx` move onto it in the refactor sweep, not here.
- Decision: 2026-10-30 (Sofia, pre-build review 7) — EXPERIENCE deviation: 2b uses one plain visible `Input`, not EXPERIENCE.md's "six visual cells" nor `link-otp.html`'s hidden input under `aria-hidden` cells, because cells over a hidden input hide the focus indicator (WCAG 2.2 SC 2.4.7).
- Decision: 2026-10-30 (Sofia, pre-build review 8) — EXPERIENCE deviation: while rate limited only the button is `aria-disabled`; the fields stay editable, not "Form disabled" as in EXPERIENCE.md and `link-rate-limited.html`, because disabled fields leave the tab order and the server enforces the limit anyway. The wait lives in component state and is lost on moving between 2a and 2b.
- Decision: 2026-10-30 (Sofia, pre-build review 3) — a usable `Retry-After` is 1 to 86 400 seconds; `0` or a date equal to now is no wait; the button comes back after the exact seconds, while the copy rounds up to whole minutes, so the gateway's `Retry-After: 1` reads "1 minute" and the button returns after one second.
