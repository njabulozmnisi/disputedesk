---
id: 1
type: story
title: "Unlinked login starts linking with a uniform response and an OTP sent after the SIM-swap check"
parent: epic-6-linking
covers: [R4, R5, R6, R7, R9, R10, R31, R36]
after: [2.1, 2.3, 2.8, 3.2]
hitl: false
risk: high
---

# Unlinked login starts linking with a uniform response and an OTP sent after the SIM-swap check

## Description

Adds `LinkingChallenges`, `POST /api/v1/linking/start` and the background job (a `BackgroundService` reading a bounded `Channel`, the documented queue-service pattern) that matches, caps, runs the SIM-swap check and sends the OTP (AD-12).

## Requirements

Verify: Integration tests prove identical responses, the check order, the cap, fail-closed SIM swap and audit; no ID number appears in any table or log.

1. **One response for every outcome** (R4; FR-4, NFR-SEC-4)
   **Given** details that match, match nothing, or match a customer with a linked login
   **When** each starts linking
   **Then** every response is 202 with the same fixed body, and timing does not depend on the match because the lookup runs off the request path (OWASP Forgot Password Cheat Sheet)

2. **Format errors without echo** (R4; FR-4)
   **Given** a malformed ID number or account number
   **When** it is posted
   **Then** the response is 400 `validation` naming the field and never containing the value

3. **SIM-swap before any OTP, failing closed** (R5; FR-4, FR-6)
   **Given** customer C, and a check that errors or times out
   **When** linking starts
   **Then** no OTP is sent, the response is unchanged, and an audit entry `LinkingSimSwapFailed` is written (NIST SP 800-63B §3.1.3.3 SIM-change check)

4. **Send cap** (R6; FR-4, NFR-SEC-4)
   **Given** 3 OTPs sent to one customer within an hour across logins and IPs
   **When** a 4th start matches
   **Then** nothing is sent, the response is unchanged and `LinkingSendCapped` is audited under the advisory lock

5. **OTP issued safely** (R7; FR-5)
   **Given** a match with no linked login that passes the check
   **When** the job sends
   **Then** a 6-digit OTP from `RandomNumberGenerator` (NIST SP 800-63B §3.1.3.2) goes to the cellphone on record, only its HMAC-SHA256 is stored, and it expires at the send instant plus 5 minutes

6. **Restart supersedes** (R4; FR-4)
   **Given** an earlier challenge
   **When** the login starts again
   **Then** the earlier challenge is superseded and a late job for it sends nothing

7. **Linking-route states** (R9; FR-9, NFR-SEC-3)
   **Given** an anonymous caller and a linked customer
   **When** each calls a linking route
   **Then** they get 401 `unauthorized` and 403 `already-linked`

8. **Audit without the ID number** (R10, R31; FR-10, NFR-SEC-5)
   **Given** every stop of the job
   **When** entries are written
   **Then** each is written through `AuditLog.Record` (story 2.8; R41, AD-14) and carries time, login, outcome and client IP, and a test finds no ID number in any table, log or span

9. **Linking metrics** (R36; NFR-OBS-3)
   **Given** starts and capped sends
   **When** metrics are collected
   **Then** `disputedesk.linking.events` counts `started` and `capped`

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Start linking as customer D's details through the gateway; the OTP email lands in Mailpit.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes, migration.
- Lane: M2.
- Touches: `backend/src/DisputeDesk.Application/Linking/StartLinking/`; backend/src/DisputeDesk.Infrastructure/Linking/ (job, conditional fill); backend/src/DisputeDesk.Infrastructure/Persistence/ (LinkingChallenge configuration, Migrations/); `backend/src/DisputeDesk.Api/Linking/`; `backend/src/DisputeDesk.Api/Program.cs`; `backend/src/DisputeDesk.Api/appsettings.json`; docker-compose.yml (Linking__OtpHmacKey dev value); `backend/openapi.json`; `backend/tests/DisputeDesk.Api.IntegrationTests/Linking/`.

## References

- parent — docs/tickets/epic-6-linking/epic-6-linking.md

## Notes

- Risk high: enumeration and personal data; beyond the criteria, the security agent compares response sizes and timings across outcomes.
