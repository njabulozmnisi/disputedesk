# ad-001: Zitadel as the external identity provider

**Status:** accepted (records AD-6; decision-record set settled at Gate 2, 2026-10-26, NFR-DOC-3)

**Context.** DisputeDesk needs sign-up, email verification, password reset and sign-in (FR-1 to FR-3), but it is not an identity platform: in production the bank's own identity platform owns logins (PRD §7, Non-goals). Building any of that inside DisputeDesk would be code the bank throws away, and two stories could each assume the other enforces email verification (AD-6, Prevents).

**Decision.** Zitadel v4, self-hosted with Login v2, stands in for the bank's identity platform (AD-6).

- Zitadel owns logins, sign-up, email verification and password reset; DisputeDesk builds none of them.
- It is configured with no manual steps: the FirstInstance and DefaultInstance environment variables, plus the idempotent `provision.sh`, which writes the client settings to `zitadel-clients.json` under the configuration keys of AD-26.
- The hosted pages are themed through Zitadel's label policy, generated from the theme tokens at provisioning time.
- An unverified login is blocked twice: Login v2 requires email verification (`EMAIL_VERIFICATION`), and the BFF also rejects an ID token without `email_verified: true` in `OnTokenValidated`.
- DisputeDesk never calls Zitadel management APIs at runtime.
- The Api reaches Zitadel only through the built-in OpenID Connect handler for customers (AD-7) and the built-in JwtBearer handler for operations tools (AD-8); operations roles are Zitadel project roles, one per action (AD-9).

**Options rejected** (AD-6).

- *Keycloak:* kept as the fallback and not used.
- *Duende IdentityServer:* banned by AGENTS.md.
- *A mock OIDC server:* rejected in AD-6.

**Consequences.**

- Sign-up, verification and password reset follow Zitadel's documented flows, so DisputeDesk holds no password and no verification logic.
- The Api depends on Zitadel only through standard OpenID Connect sign-in and bearer-token validation (AD-7, AD-8), plus the configuration keys `provision.sh` writes (AD-26). Moving linking into the bank's identity platform is the bank-scale end state in `docs/production-path.md` (PRD §6.2).
- The hosted login still shows Zitadel's default copy and no favicon (story 2.6 QA); Login v2 text customisation is a production-path item.
- Operations tool credentials are issued by Zitadel to a machine user; how real tools hold them is a production-path item (AD-26, stories 5.1 and 5.5).
