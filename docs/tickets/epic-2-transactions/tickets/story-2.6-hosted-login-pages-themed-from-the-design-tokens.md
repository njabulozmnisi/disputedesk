---
id: 6
type: story
title: "Hosted login pages themed from the design tokens"
parent: epic-2-transactions
covers: [R1]
after: [4, 2]
hitl: false
risk: low
---

# Hosted login pages themed from the design tokens

## Description

Generates the Zitadel label policy from `frontend/src/theme/tokens.css` at provisioning time so sign-up, sign-in and verification pages match DisputeDesk (FR-1, AD-6).

## Requirements

Verify: A provisioning dry run prints label-policy JSON whose colours, logo and font equal the design system's values; a script test compares them.

1. **Label policy from tokens** (R1; FR-1)
   **Given** the theme file
   **When** provisioning runs
   **Then** the Zitadel label policy's primary, background, warning and font colours and the logo come from the token values, and the policy is activated
   **And** the design system's font (Nunito Sans, the file the SPA self-hosts) is uploaded to the label policy through Zitadel's font upload, so the hosted pages use it

2. **Email verification required** (R1; FR-1)
   **Given** `docker-compose.yml`
   **When** it is read
   **Then** `zitadel-login` sets `EMAIL_VERIFICATION: "true"` and the default-instance SMTP configuration points at Mailpit (AD-6)

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Screenshot the hosted sign-up page beside `docs/ux/mockups/start.html`; sign up a new login and find the verification email in Mailpit.
- Run provisioning twice: the label policy is unchanged and the second run exits 0.

## Build facts

- Owner: backend (backend-builder).
- Flags: none.
- Lane: M2.
- Touches: infra/zitadel/provision/ (label policy generation, provision.sh); `infra/zitadel/provision/Dockerfile`; docker-compose.yml (provision mounts tokens.css and the font file).

## References

- parent — docs/tickets/epic-2-transactions/epic-2-transactions.md

## Notes

- Gold-plating watch: theming beyond colours, font and logo is out.
- Decision: 2026-10-27 — the hosted login pages also use the design system's font, uploaded to Zitadel's label policy; the builder checks the font format Zitadel accepts in its documentation.
