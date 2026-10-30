# ad-007: The BFF session, with no tokens in the browser

**Status:** accepted (records AD-7; linking and the session key decided 2026-10-30, story 6.2)

**Context.** The SPA calls the Api for customer data. Tokens held in browser JavaScript can be read by any script that runs on the page, so OAuth 2.0 for Browser-Based Applications describes a backend-for-frontend that keeps tokens on the server (RFC 10017 §6.1, as cited in AD-7). AD-7 also has to prevent lost sessions, session fixation, an idle sign-out earlier than 15 minutes and a logout that can be forged across sites.

**Decision.** The Api is the backend-for-frontend; the browser holds only a cookie with a session key (AD-7).

- **Sign-in.** The Api is a confidential client: the built-in OpenID Connect handler (code flow with PKCE, `client_secret_post`) and the built-in cookie handler.
- **Server-side session.** The cookie holds only a 256-bit random session key, minted by `StoreAsync`. An `ITicketStore` keeps the ticket in `Sessions`, stored under the key's SHA-256 (`KeyHash`). Idle limit 15 minutes, moved forward by the store on each authenticated request; absolute limit 12 hours, never moved.
- **Tokens.** The browser never holds an access token or a refresh token. Access and refresh tokens are never stored at all. Only the ID token is kept, inside the protected server-side ticket, for sign-out.
- **The one exception.** The ID token crosses the browser once: in the end-session redirect to Zitadel, as `id_token_hint` (OpenID Connect RP-Initiated Logout 1.0). By then it is already spent and short-lived. It is sent because without it Zitadel's session outlives sign-out, and Zitadel asks for a click. The redirect is made by the OIDC handler's built-in `SignOutAsync`, which also sends `state`.
- **CSRF.** Every unsafe customer and linking endpoint validates an antiforgery token from the `X-XSRF-TOKEN` header (OWASP synchronizer token pattern); logout is a form POST carrying `__RequestVerificationToken`. No CORS policy is registered.
- **Linking keeps the session key.** A successful link keeps the session and the key minted at sign-in; linked state is read from `CustomerLinks` on every request (decided 2026-10-30, story 6.2). Session fixation is already blocked, because the key is minted server-side at sign-in and the store rejects any key it did not mint. The built-in cookie handler with a server-side ticket store cannot change the key within one request.

**Options rejected** (AD-7).

- *Tokens in the SPA:* the risk this record exists to remove.
- *A static `X-CSRF: 1` header alone:* replaced by the synchronizer token.
- *The Duende BFF:* banned by AGENTS.md.
- *Redis, or a ticket held in the cookie:* the ticket lives in PostgreSQL beside the rest of the data.

**Consequences.**

- Sessions are in PostgreSQL, so the Api can scale out without losing sessions (NFR-ARC-3). An hourly timer in the Api deletes expired rows.
- The cookie carries no expiry date and is never renewed, only written at sign-in and cleared at sign-out, so a request in flight during logout cannot set it again (AD-7, decided 2026-10-30).
- The SPA never polls; two minutes before the idle limit it offers "Stay signed in", which calls `GET /api/v1/bff/session` (WCAG 2.2 SC 2.2.1; story 2.4).
- The session key is not renewed at the privilege change that linking is. Renewing the key at every privilege change (OWASP Session Management Cheat Sheet) is the production-path entry P32, *Session key renewal at a privilege change*, in `docs/production-path.md`.
