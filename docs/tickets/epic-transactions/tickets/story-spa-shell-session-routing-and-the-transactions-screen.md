---
id: 4
type: story
title: "SPA shell, session routing and the transactions screen"
parent: epic-transactions
covers: [R2, R3, R9, R11, R12, R34, R40]
after: [3]
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
   **Then** every API call goes through the generated TanStack Query hooks and one mutator that sends same-origin credentials and `X-XSRF-TOKEN`, and TypeScript is strict with ESLint and Prettier clean

2. **Routing by authorisation state** (R9; FR-9, NFR-SEC-3)
   **Given** `/api/v1/bff/session` returns anonymous, unlinked or linked
   **When** the SPA loads any route
   **Then** anonymous sees the start page, unlinked is sent to `/link`, linked to `/transactions`
   **And** a 403 `linking-required` sends the person to `/link` and a 401 while signed in to `/signed-out?reason=expired`

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
   **Then** rows show newest first with date in South African time, description, masked card or account and amount, and Load more fetches the next cursor until it is null

6. **Disputability on each row** (R12; FR-12)
   **Given** rows that are disputable and not disputable
   **When** they render
   **Then** only disputable rows show the Dispute control, and a not-disputable row shows its reason text from EXPERIENCE.md

7. **Loading, empty and error states** (R11; NFR-RES-2)
   **Given** a slow response, an empty list and a 503 `core-banking-unavailable`
   **When** the screen renders
   **Then** it shows the skeleton, the empty state and "Something went wrong on our side. Try again." with a Retry that refetches

8. **Tokens in one theme file and accessible structure** (R34, R40; NFR-CODE-4, NFR-CODE-6)
   **Given** the shell and transactions screen
   **When** component tests run
   **Then** colours and type come only from `frontend/src/theme/tokens.css`, controls are keyboard reachable with visible focus, and the layout reflows at 320px without horizontal scroll

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Through the gateway after story 5, customer A signs in and sees the screen matching `docs/ux/mockups/transactions.html`.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Touches: frontend/ (new: package.json, vite.config.ts, tsconfig, eslint and prettier config, orval.config.ts); `frontend/src/api/generated/`; `frontend/src/api/mutator.ts`; `frontend/src/theme/tokens.css`; frontend/src/app/ (router, layout, nav); `frontend/src/features/transactions/`; frontend/src/pages/ (start, signed-out, not-found).

## References

- parent — docs/tickets/epic-transactions/epic-transactions.md
- design — docs/ux/mockups/start.html
- design — docs/ux/mockups/transactions.html
- design — docs/ux/mockups/transactions-loading.html
- design — docs/ux/mockups/transactions-empty.html
- design — docs/ux/mockups/transactions-error.html
- design — docs/ux/mockups/not-found.html
- experience — docs/ux/EXPERIENCE.md, Copy deck
