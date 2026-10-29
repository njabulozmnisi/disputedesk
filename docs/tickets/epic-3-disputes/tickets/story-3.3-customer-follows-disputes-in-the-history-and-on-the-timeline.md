---
id: 3
type: story
title: "Customer follows disputes in the history and on the timeline"
parent: epic-3-disputes
covers: [R16, R17, R18, R11, R30]
after: [8]
hitl: false
risk: high
---

# Customer follows disputes in the history and on the timeline

## Description

Adds `GET /api/v1/disputes` and `GET /api/v1/disputes/<disputeId>` on story 3.8's customer view mapping: list rows and the dispute fields of the detail read the `Disputes` read model, and the timeline is built from the dispute's stored events through the deny-by-default allowlist (AD-13). Proves the allowlist against every stored event type and a test-only unknown type, and proves ownership on every customer endpoint.

## Requirements

Verify: Integration tests for list, detail, the allowlist over every stored event type and a test-only unknown type, ownership, timestamps and the core-banking outage pass.

1. **History list** (R16; FR-16, NFR-API-3)
   **Given** a customer with disputes
   **When** they list them
   **Then** rows come newest first by `CreatedAtUtc` then `Id` with keyset paging, each with the snapshot summary, reason, disputed amount, `customerStatus` and `outcome` when resolved
   **And** paging matches transactions: `limit` is a typed `int` with `[Range(1, 50)]` defaulting to 20 through built-in validation, `nextCursor` is null on the last page, and an undecodable cursor or out-of-range `limit` gets 400 `validation` naming the field

2. **Detail and timeline** (R16; FR-17)
   **Given** a dispute with several entries
   **When** its detail is read
   **Then** the timeline is built from the dispute's stored events in `Version` order, oldest first, each `timeline` item with `customerStatus`, the UTC instant, actor `you` or `theBank` and any customer-visible note, through the same mapping as the raise 201 body (story 3.8)
   **And** the `Location` returned by a raise serves this detail

3. **Customer never sees internals** (R18; FR-17, NFR-API-5)
   **Given** disputes acted on by operations and automation, and an escalated one
   **When** customer responses are serialised
   **Then** no actor type, actor identity, SLA or due date appears, and escalated shows as `underReviewPrioritised`

4. **Identical 404 on every customer endpoint** (R17; FR-17, NFR-SEC-6, NFR-TEST-3)
   **Given** customer A and a dispute of customer B
   **When** A calls the detail route with B's ID and with an unknown ID, and raises on B's transaction
   **Then** every response is 404 `not-found` with bodies identical apart from `traceId`
   **And** an integration test reads the app's `EndpointDataSource`, finds every endpoint requiring the `Linked` policy (transactions, raise, list, detail) and fails when one is not covered

5. **History works while core banking is down** (R11, R16; NFR-RES-2, FR-16)
   **Given** the stub is unreachable
   **When** the customer lists and opens disputes
   **Then** both succeed from the stored snapshots

6. **Deny by default on the customer timeline** (R18, R16; FR-17, NFR-API-5, NFR-TEST-3)
   **Given** one test stream per stored event type (`DisputeRaised`, `ReviewStarted`, `DisputeEscalated`, `DisputeResolved`, `DisputeWithdrawn`, `SlaWarningRaised`), and a stored event record declared only in the test project
   **When** each is passed through the customer view
   **Then** each of the five lifecycle types gives exactly one `timeline` item, `SlaWarningRaised` gives none, and the test-only record gives none and raises no error, because the allowlist admits record types rather than excluding them (OWASP Authorization Cheat Sheet: deny by default; AD-13)
   **And** a stored event row whose `EventType` the reader does not know is skipped for customers, never an error

7. **Timestamps and enum values on the wire** (R30; NFR-API-1, NFR-API-6)
   **Given** every response timestamp in `openapi.json` (`transactionDate`, `createdAt`, the timeline `occurredAt`) and every request enum
   **When** responses are serialised and requests are read
   **Then** each timestamp is RFC 3339 UTC ending in `Z`, with fractional seconds as System.Text.Json writes them, as the architecture's "Timestamps in JSON" convention says, proved by one test over every response timestamp, and an enum value binds only from its exact camelCase name, so `"Unrecognised"` gets 400 `validation` (RFC 3339; AD-5)

## Boundaries

- The allowlist applies to the customer view only. Operations see every stored event type, `SlaWarningRaised` included (FR-30; story 5.1).
- The API contract shapes (`DisputeResponse`, `TimelineEntryResponse`, the `timeline` array) are 3.8's and do not change here beyond the two new operations.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- None beyond the story flow.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Touches: `backend/src/DisputeDesk.Application/Disputes/ListDisputes/, GetDispute/, CustomerView/`; `backend/src/DisputeDesk.Contracts/Disputes/`; `backend/src/DisputeDesk.Contracts/Json/` (timestamps, exact enum reading); `backend/src/DisputeDesk.Api/Disputes/`; `backend/openapi.json`; `backend/tests/DisputeDesk.Api.IntegrationTests/Disputes/, Ownership/`.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md

## Notes

- Risk high: cross-customer leakage; beyond the criteria, the security agent diffs the 404 bodies byte for byte.
- Open question (from 3.1, 2026-10-28): each customer `timeline` item (`TimelineEntryResponse`) carries the wire `action`, so an escalation would show `escalate` to the customer. Before building, decide whether R18 ("Escalated always shows as Under review (prioritised)") means the action is hidden or mapped in customer responses; `DisputeResponse` is the shape the GET reuses.
- Input (from 3.2 review R5, 2026-10-28): the raise handler builds the `Location` string `/api/v1/disputes/{id}` in Application, because the stored 201 exists before the save. When this story adds the GET, put the route in one Contracts constant used by both the handler and `MapGet`.
- Input (from 3.1, decided 2026-10-28): responses write `DateTimeOffset` values as `+00:00` with 7 fractional digits, while Postgres stores 6 and events write `Z`; criterion 7 settles the `Z` form in the shared JSON options of story 3.8. Open question: whether a response timestamp should round to the 6 digits Postgres stores, so a value read back equals the value returned; the convention says System.Text.Json's precision, so the answer may need an architecture change. From 3.1 QA: the shared enum converter reads names case-insensitively; criterion 7 makes reading exact in `SingleNameEnumConverter`.
