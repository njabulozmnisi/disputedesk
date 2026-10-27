---
id: 4
type: story
title: "Linking screens for details, OTP, lockout and rate limits"
parent: epic-6-linking
covers: [R4, R7, R8, R9, R40]
after: [3, 2.4, 3.7]
hitl: false
risk: medium
---

# Linking screens for details, OTP, lockout and rate limits

## Description

Adds the details and OTP screens with the locked and rate-limited states, routed by problem type.

## Requirements

Verify: Vitest component tests pass, with API responses mocked from `openapi.json` shapes.

1. **Neutral confirmation** (R4; FR-4)
   **Given** any well-formed start
   **When** it returns 202
   **Then** the screen shows "If these details match our records, we've sent a code to the cellphone number we hold for you." and moves to the OTP screen

2. **OTP entry** (R7; FR-6, FR-7)
   **Given** `otp-incorrect` with `attemptsRemaining`, then `linking-locked`
   **When** they arrive
   **Then** the attempts left show, and locked shows "We can't link you online. Please contact the bank." with Start again

3. **Rate limited** (R8; FR-8)
   **Given** a 429 `rate-limited` with `Retry-After`
   **When** it arrives
   **Then** the rate-limited state shows when to try again

4. **Routing** (R9; FR-9)
   **Given** an already linked customer opening `/link`
   **When** 403 `already-linked` arrives
   **Then** the SPA goes to `/transactions`, and a successful link also lands there

5. **Accessible inputs** (R40; NFR-CODE-6)
   **Given** the details and OTP forms
   **When** used by keyboard
   **Then** fields are labelled, errors are announced, and the OTP input uses `autocomplete="one-time-code"` and `inputmode="numeric"` and accepts paste (WCAG 2.2 SC 3.3.8)

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- None beyond the story flow.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Touches: `frontend/src/features/linking/`; frontend/src/app/ (routes); `frontend/src/api/generated/`.

## References

- parent — docs/tickets/epic-6-linking/epic-6-linking.md
- design — docs/ux/mockups/link.html
- design — docs/ux/mockups/link-otp.html
- design — docs/ux/mockups/link-locked.html
- design — docs/ux/mockups/link-rate-limited.html
- experience — docs/ux/EXPERIENCE.md, Copy deck
