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

Verify: A provisioning dry run prints label-policy JSON whose colours and font equal the token values; a script test compares them.

1. **Label policy from tokens** (R1; FR-1)
   **Given** the theme file
   **When** provisioning runs
   **Then** the Zitadel label policy's primary, background, warning and font colours and the logo come from the token values, and the policy is activated

2. **Email verification required** (R1; FR-1)
   **Given** the login policy set by `provision.sh`
   **When** it is read
   **Then** it requires email verification before sign-in and points SMTP at Mailpit

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Screenshot the hosted sign-up page beside `docs/ux/mockups/start.html`; sign up a new login and find the verification email in Mailpit.
- Run provisioning twice: the label policy is unchanged and the second run exits 0.

## Build facts

- Owner: backend (backend-builder).
- Flags: none.
- Lane: B.
- Touches: infra/zitadel/provision/ (label policy generation, provision.sh); `infra/zitadel/provision/Dockerfile`; docker-compose.yml (provision mounts tokens.css).

## References

- parent — docs/tickets/epic-2-transactions/epic-2-transactions.md

## Notes

- Gold-plating watch: theming beyond colours, font and logo is out.
