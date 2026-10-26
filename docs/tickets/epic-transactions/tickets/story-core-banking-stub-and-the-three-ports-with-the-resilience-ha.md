---
id: 1
type: story
title: "Core-banking stub and the three ports with the resilience handler"
parent: epic-transactions
covers: [R4, R5, R11, R24, R25, R26, R31]
after: [1.2]
hitl: false
risk: medium
---

# Core-banking stub and the three ports with the resilience handler

## Description

Ships the whole core-banking port and stub (AD-21) before linking, transactions and raise use it, with fixtures for customers A to D.

## Requirements

Verify: Stub and client integration tests pass; with the stub stopped, the client returns Unavailable within 5 seconds.

1. **Match by body only** (R4; FR-4, NFR-SEC-5)
   **Given** fixture customers with ID and account numbers
   **When** the client matches an ID number and account number
   **Then** a match returns the customer ID and the cellphone on record, and no match returns none
   **And** the ID number travels only in the POST body, never in a path, query or log

2. **Own transactions only** (R11; FR-11, NFR-SEC-6)
   **Given** a customer with two accounts
   **When** the client lists transactions with a cursor and limit
   **Then** rows from both accounts come back merged, newest first by transaction date then transaction ID
   **And** asking for another customer's account or transaction returns not found

3. **Masked transaction shape** (R31; NFR-SEC-5)
   **Given** any fixture transaction
   **When** the stub returns it
   **Then** the row carries `transactionId`, `accountRef`, `accountLast4`, `transactionDate`, `description`, `amount`, `currency`, `direction`, `kind`, `state` and `maskedCard` (first 6 and last 4 or null)
   **And** no full account or card number exists in the fixtures or the response

4. **Resilience handler exhausts to Unavailable** (R11; NFR-RES-2)
   **Given** the stub is unreachable or slow
   **When** the client is called
   **Then** it retries twice with jitter, gives up within 5 seconds and returns an `Unavailable` error
   **And** after 5 failures in 30 seconds the circuit opens for 15 seconds

5. **SIM-swap check fails closed** (R5; FR-4, FR-6)
   **Given** customer C is flagged with a recent SIM swap in the fixtures
   **When** `ISimSwapCheck` runs for C, or the stub errors or times out
   **Then** the check reports failed in all three cases

6. **SMS sender reaches only the local inbox** (R4; FR-4)
   **Given** an OTP message
   **When** `ISmsSender` sends it
   **Then** it is delivered over SMTP to the configured host (Mailpit locally) and nowhere else

7. **Ports are the only external seam** (R25, R26; NFR-ARC-1, NFR-ARC-5, NFR-ARC-2)
   **Given** the solution
   **When** the architecture tests run
   **Then** the stub references no DisputeDesk project, the three ports are declared in Application and implemented only in Infrastructure

8. **Fixtures for customers A to D** (R24; FR-29)
   **Given** a fresh start
   **When** the stub resolves `daysAgo` from the start of the current South African day
   **Then** each customer has about 90 days of transactions on one or two accounts, including one pending, one credit, one own-account transfer and one older than 90 days
   **And** every account and transaction has an explicit `accountRef` and `transactionId`

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- `docker compose up` shows `core-banking` healthy and running as non-root.
- `docker compose stop core-banking`, then a client call returns Unavailable within about 5 seconds.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: B.
- Touches: `backend/stubs/DisputeDesk.CoreBankingStub/`; backend/src/DisputeDesk.Application/Abstractions/ (ICoreBankingClient, ISimSwapCheck, ISmsSender); `backend/src/DisputeDesk.Infrastructure/CoreBanking/`; `backend/src/DisputeDesk.Infrastructure/Sms/`; `backend/src/DisputeDesk.Infrastructure/DependencyInjection.cs`; `backend/Directory.Packages.props`; `backend/DisputeDesk.slnx`; backend/Dockerfile (core-banking target); `backend/tests/DisputeDesk.ArchitectureTests/LayerTests.cs`; `backend/tests/DisputeDesk.Api.IntegrationTests/CoreBanking/`; `fixtures/local.json`; `fixtures/README.md`; docker-compose.yml (core-banking service).

## References

- parent — docs/tickets/epic-transactions/epic-transactions.md

## Notes

- Assumption: customer D's bank record joins the fixtures here (AD-30 assigns it to the linking story) so the linking stories touch no fixture file.
