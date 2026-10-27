---
id: 4
type: story
title: "SPA shell, session routing and the transactions screen"
parent: epic-2-transactions
covers: [R2, R3, R9, R11, R12, R34, R40]
after: [3, 7]
hitl: false
risk: medium
---

# SPA shell, session routing and the transactions screen

## Description

Scaffolds the SPA (AD-31) with the start page, session-driven routing, navigation, sign-out and the transactions screen in every state from the mockups; the SPA half of the tracer bullet.

## Requirements

Verify: `npm run build`, `tsc --noEmit`, ESLint, Prettier and Vitest pass.

1. **Generated client only** (R34; NFR-CODE-4, NFR-CODE-1)
   **Given** `backend/openapi.json`
   **When** Orval generates the hooks
   **Then** every API call goes through the generated TanStack Query hooks and one Orval custom mutator that sends same-origin credentials and, on unsafe methods only (RFC 9110), `X-XSRF-TOKEN` from `GET /api/v1/bff/antiforgery`, and TypeScript is strict with ESLint and Prettier clean

2. **Routing by authorisation state** (R9; FR-9, NFR-SEC-3)
   **Given** `/api/v1/bff/session` returns anonymous, unlinked or linked
   **When** the SPA loads any route
   **Then** anonymous sees the start page, unlinked is sent to `/link`, linked to `/transactions`
   **And** a 403 `linking-required` sends the person to `/link` and a 401 while signed in to `/signed-out?reason=expired`, handled once in the `QueryClient`'s `QueryCache` and `MutationCache` `onError`
   **And** the `QueryClient` retries no 4xx, retries a read once on 503 or a network error, and never retries a write, as EXPERIENCE.md sets (TanStack Query otherwise retries every failed query 3 times)

3. **No token in the browser** (R2; NFR-SEC-1)
   **Given** the built SPA
   **When** its code and storage are inspected
   **Then** it reads no token, writes nothing auth-related to `localStorage` or `sessionStorage`, and sign-in is a navigation to `/api/v1/bff/login`

4. **Sign-out is a form POST** (R3; FR-3)
   **Given** a signed-in person
   **When** they choose Sign out
   **Then** the SPA submits a top-level form POST to `/api/v1/bff/logout` carrying `__RequestVerificationToken`

5. **Transactions list** (R11; FR-11)
   **Given** a linked customer
   **When** the transactions screen loads and Load more is chosen
   **Then** rows show newest first with the date from `Intl.DateTimeFormat` in `Africa/Johannesburg`, description, masked card or account, and the amount from `Intl.NumberFormat` given its decimal string (never a float), and Load more uses `useInfiniteQuery` with `nextCursor` as the next page parameter until it is null
   **And** after Load more, focus moves to the first new row and the new rows are announced in the polite live region (WCAG 2.2 SC 2.4.3, 4.1.3)

6. **Disputability on each row** (R12; FR-12)
   **Given** rows that are disputable and not disputable
   **When** they render
   **Then** only disputable rows show the Dispute control, and a not-disputable row shows its reason text from EXPERIENCE.md

7. **Loading, empty and error states** (R11; NFR-RES-2)
   **Given** a slow response, an empty list and a 503 `core-banking-unavailable`
   **When** the screen renders
   **Then** it shows the skeleton with `aria-busy` on the list, the empty state, and for the 503 the notice "We can't load your transactions right now. Your disputes are still available." with `role="alert"`, "Try again" calling the query's `refetch`, and "View disputes"

8. **Tokens in one theme file and accessible structure** (R34, R40; NFR-CODE-4, NFR-CODE-6)
   **Given** the shell and transactions screen
   **When** component tests run
   **Then** colours and type come only from `frontend/src/theme/tokens.css`, exposed to Tailwind through `@theme`, and every control is reachable by Tab in reading order (WCAG 2.2 SC 2.1.1)

9. **Inactivity warning before the idle sign-out** (R2, R40; FR-2, NFR-CODE-6)
   **Given** a signed-in person with no API request for 13 minutes
   **When** the SPA's idle timer fires, 2 minutes before the 15-minute idle sign-out (well over the 20 seconds WCAG 2.2 SC 2.2.1 requires)
   **Then** a shadcn/ui `AlertDialog` shows the EXPERIENCE.md warning; "Stay signed in" closes it and calls `GET /api/v1/bff/session`, which renews the idle limit (AD-7); "Sign out" submits the sign-out form
   **And** every API response restarts the timer, and component tests drive it with fake timers

10. **Routes, not-found and screens not built yet** (R9; FR-9)
   **Given** the route tree defined with TanStack Router
   **When** an unknown path, or a link to a screen whose story has not landed (linking, raise, disputes), is opened
   **Then** the not-found route renders the screen from `docs/ux/mockups/not-found.html` until that screen's story adds its route

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Through the gateway after story 5, customer A signs in and sees the screen matching `docs/ux/mockups/transactions.html`.
- In Playwright at 320 CSS px wide, the shell and transactions screen have no horizontal scroll, and every focused control shows a visible focus indicator not hidden by the tab bar (WCAG 2.2 SC 1.4.10, 2.4.7, 2.4.11).

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Touches: frontend/ (new: package.json, vite.config.ts, tsconfig, eslint and prettier config, orval.config.ts); `frontend/src/api/generated/`; `frontend/src/api/mutator.ts`; `frontend/src/theme/tokens.css`; frontend/src/app/ (TanStack Router route tree, layout, nav, inactivity warning); `frontend/src/features/transactions/`; frontend/src/pages/ (start, signed-out, not-found).

## References

- parent — docs/tickets/epic-2-transactions/epic-2-transactions.md
- design — docs/ux/mockups/start.html
- design — docs/ux/mockups/transactions.html
- design — docs/ux/mockups/transactions-loading.html
- design — docs/ux/mockups/transactions-empty.html
- design — docs/ux/mockups/transactions-error.html
- design — docs/ux/mockups/not-found.html
- experience — docs/ux/EXPERIENCE.md, Copy deck
- experience — docs/ux/EXPERIENCE.md, Session about to end

## Notes

- Decision: 2026-10-27 — inactivity warning (WCAG 2.2 SC 2.2.1 Timing Adjustable): the dialog warns at least 2 minutes before the idle sign-out, and "Stay signed in" renews the session through `GET /api/v1/bff/session` (AD-7, EXPERIENCE.md, prd FR-2).
- Decision: 2026-10-27 — routes are defined with TanStack Router (AD-31); links to screens not built yet render the not-found screen until their stories land.
- Decision: 2026-10-27 — waits on 2.7 as well, so the SPA routes on the resolvable problem `type` form from the start.
