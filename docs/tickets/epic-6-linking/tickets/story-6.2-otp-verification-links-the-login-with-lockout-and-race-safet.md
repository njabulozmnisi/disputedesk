---
id: 2
type: story
title: "OTP verification links the login, with lockout and race safety"
parent: epic-6-linking
covers: [R5, R7, R10, R36]
after: [1]
hitl: false
risk: high
---

# OTP verification links the login, with lockout and race safety

## Description

Adds `POST /api/v1/linking/verify` with the atomic attempt counter, constant-time comparison, link insert and session re-issue (AD-12, AD-7).

## Requirements

Verify: Integration tests cover correct, wrong, expired, locked, parallel and racing verifications.

1. **Correct code links** (R7; FR-6)
   **Given** an active challenge
   **When** the correct OTP is entered
   **Then** the login becomes a linked customer with no new sign-in, the challenge is consumed, the session is re-issued under a new key and `Linked` is audited

2. **Wrong, expired and absent** (R7; FR-5, FR-6)
   **Given** a wrong code, a code after 5 minutes, and no active challenge
   **When** each is verified
   **Then** the first two get `otp-incorrect` with `attemptsRemaining` until the last try, and no challenge answers `linking-locked`

3. **Lockout after three** (R7; FR-7)
   **Given** 3 wrong codes
   **When** a 4th attempt uses the correct code
   **Then** it fails with `linking-locked`

4. **Parallel verifications** (R7; FR-7, NFR-TEST-3)
   **Given** 10 parallel verifications of one challenge
   **When** they run
   **Then** at most 3 attempts count and at most 1 succeeds

5. **Two logins racing for one customer** (R7; FR-6, NFR-DATA-5)
   **Given** two logins with correct codes for the same customer
   **When** both verify
   **Then** one links and the other gets the generic failure from `UQ_CustomerLinks_CustomerId`

6. **Customer C ends locked** (R5; FR-6)
   **Given** customer C after a failed SIM-swap check
   **When** codes are entered
   **Then** they behave as wrong codes and end in `linking-locked`

7. **Audit and metrics** (R10, R36; FR-10, NFR-OBS-3)
   **Given** incorrect, expired, locked and linked outcomes
   **When** they occur
   **Then** each is audited and `disputedesk.linking.events` counts `completed` and `locked`

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- None beyond the story flow.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: A.
- Touches: `backend/src/DisputeDesk.Application/Linking/VerifyOtp/`; backend/src/DisputeDesk.Infrastructure/Linking/ (attempt update); `backend/src/DisputeDesk.Api/Linking/`; `backend/openapi.json`; `backend/tests/DisputeDesk.Api.IntegrationTests/Linking/`.

## References

- parent — docs/tickets/epic-6-linking/epic-6-linking.md

## Notes

- Risk high: account takeover; beyond the criteria, the security agent times correct and wrong comparisons.
