---
id: 3
type: story
title: "Linked customer lists their own transactions"
parent: epic-transactions
covers: [R9, R11, R12, R24, R29, R30, R31]
after: [1, 2]
hitl: false
risk: high
---

# Linked customer lists their own transactions

## Description

Adds `CustomerLinks`, the three authorisation states, `CurrentCustomer` and `GET /api/v1/transactions`, the API half of the tracer bullet, and merges the contract the SPA needs.

## Requirements

Verify: Integration tests cover every authorisation state, paging, disputability and the outage; domain tests pin the D + 90 boundary.

1. **Three authorisation states on customer routes** (R9; FR-9, NFR-SEC-3)
   **Given** an anonymous caller, an unlinked login and a linked customer
   **When** each calls `GET /api/v1/transactions`
   **Then** they get 401 `unauthorized`, 403 `linking-required` and 200, never a redirect

2. **Customer from the link table** (R9; FR-9)
   **Given** a linked login
   **When** any customer request runs
   **Then** the customer is resolved from `CustomerLinks` by the session's login, and no request field or token subject changes which customer's data returns

3. **Keyset pagination** (R11; FR-11, NFR-API-3)
   **Given** customer A's transactions
   **When** the list is requested with and without `limit` and `cursor`
   **Then** the default page is 20, the maximum 50, pages follow transaction date then transaction ID newest first, `nextCursor` is null on the last page
   **And** an undecodable cursor or a limit above 50 gets 400 `validation` naming the field

4. **Row fields and wire formats** (R11, R30; FR-11, NFR-API-6)
   **Given** any transaction row
   **When** it is returned
   **Then** money is `{ "amount": "849.00", "currency": "ZAR" }`, the card shows first 6 and last 4, the account shows last 4, and enum values are camelCase

5. **Disputability with the D + 90 boundary** (R12; FR-12, NFR-DATA-3, NFR-DATA-4, NFR-TEST-2)
   **Given** pending, credit, own-account transfer and settled card debits dated D
   **When** disputability is decided at instants around the end of D + 90 South African time
   **Then** only the settled card debit inside the window is disputable, and others carry `notDisputableReason` `notEligible` or `outsideWindow`
   **And** unit tests pin the last instant of D + 90 and the first instant after it

6. **Core banking down** (R11; NFR-RES-2)
   **Given** the stub is unreachable
   **When** a linked customer lists transactions
   **Then** the response is 503 `core-banking-unavailable` after the resilience policies

7. **Seeded links** (R24; FR-29)
   **Given** a fresh start with `Seed:Enabled`
   **When** the seeder runs twice
   **Then** customers A and B are linked to their fixture logins exactly once, and C and D are not linked

8. **No customer identifier in telemetry** (R31; NFR-SEC-5, NFR-OBS-2)
   **Given** a traced transactions request
   **When** spans and logs are exported
   **Then** no exported span attribute or log field contains a seeded customer ID, and every log line carries the trace ID

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- `scripts/dev-login.sh` for A then curl `/api/v1/transactions` through the gateway returns rows.
- With `core-banking` stopped the same call returns 503 within about 5 seconds.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes, migration.
- Lane: A.
- Touches: backend/src/DisputeDesk.Domain/Transactions/ (disputability); `backend/src/DisputeDesk.Application/Abstractions/CurrentCustomer.cs`; `backend/src/DisputeDesk.Application/Transactions/`; `backend/src/DisputeDesk.Contracts/Transactions/`; `backend/src/DisputeDesk.Api/Authorization/`; `backend/src/DisputeDesk.Api/Transactions/`; `backend/src/DisputeDesk.Api/Program.cs`; backend/src/DisputeDesk.Infrastructure/Persistence/ (CustomerLink configuration, Migrations/); `backend/src/DisputeDesk.Infrastructure/Seeding/`; `backend/openapi.json`; `backend/tests/DisputeDesk.Domain.Tests/Transactions/`; `backend/tests/DisputeDesk.Api.IntegrationTests/Transactions/`.

## References

- parent — docs/tickets/epic-transactions/epic-transactions.md

## Notes

- Risk high: customer resolution decides whose data returns; beyond the criteria, the security agent tries a forged `sub`.
