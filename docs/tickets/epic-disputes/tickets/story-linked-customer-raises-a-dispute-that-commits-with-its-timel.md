---
id: 1
type: story
title: "Linked customer raises a dispute that commits with its timeline, audit and outbox rows"
parent: epic-disputes
covers: [R10, R12, R13, R14, R17, R23, R28, R29, R35, R36]
after: [2.3]
hitl: false
risk: high
---

# Linked customer raises a dispute that commits with its timeline, audit and outbox rows

## Description

Adds the `Dispute` aggregate (AD-13), the one commit path (AD-14), the ownership filter (AD-11) and `POST /api/v1/disputes`; the transactions list starts showing each row's dispute status.

## Requirements

Verify: Domain unit tests and Testcontainers integration tests pass; raising for customer A writes one row each in `Disputes`, `TimelineEntries`, `AuditLogEntries` and `OutboxMessages`.

1. **Raise succeeds** (R13; FR-13)
   **Given** a linked customer and their disputable transaction
   **When** they post a reason and an optional note of up to 1,000 characters
   **Then** the response is 201 with status `submitted`, one timeline entry with action `raise` and actor `you`, and a `Location` that a GET serves
   **And** the snapshot comes from a fresh core-banking read, and any amount, date or description in the request is ignored

2. **Raise refusals in order** (R13; FR-13, NFR-API-2)
   **Given** a missing or another customer's transaction, an ever-disputed one, and a pending or old one
   **When** each is raised
   **Then** they get 404 `not-found`, 409 `already-disputed` and 422 `not-disputable` with `reason`, in that check order
   **And** a note over 1,000 characters after trimming gets 400 `validation`

3. **One dispute per transaction under concurrency** (R13, R29; FR-13, NFR-DATA-5, NFR-TEST-3)
   **Given** two parallel raises for the same transaction
   **When** both commit
   **Then** exactly one returns 201 and the other 409 `already-disputed`, enforced by `UQ_Disputes_AccountRef_TransactionId`

4. **Wrong amount** (R14; FR-14, NFR-TEST-2)
   **Given** the reason `wrongAmount`
   **When** an expected amount is sent or omitted
   **Then** it is required, at least 0.00, below the transaction amount, with at most 2 decimals, and the disputed amount is the difference computed in `decimal`
   **And** an expected amount with any other reason gets 400 `validation`

5. **Core banking down writes nothing** (R13; FR-13, NFR-RES-2)
   **Given** the stub is unreachable
   **When** a raise is posted
   **Then** the response is 503 `core-banking-unavailable` and no row is written

6. **Atomic commit** (R28, R10; NFR-DATA-2, NFR-SEC-10, NFR-TEST-3)
   **Given** a raise whose save fails after the dispute is added
   **When** the transaction rolls back
   **Then** no dispute, timeline entry, audit entry or outbox row remains
   **And** the commit override rejects any modified or deleted audit entry

7. **Raised event in the outbox** (R23; FR-27, NFR-SEC-5, NFR-TEST-4)
   **Given** a successful raise
   **When** the outbox row is read
   **Then** its `Payload` is a CloudEvents 1.0 structured `disputedesk.dispute.raised.v1` whose data is exactly `disputeId`, `reason`, `status`, `occurredAt` and `disputeVersion` 1, with `TraceParent` stored
   **And** a contract test validates it against the committed JSON Schema

8. **Data model rules** (R29; NFR-DATA-1, NFR-DATA-4, NFR-DATA-5, NFR-DATA-7)
   **Given** the migration
   **When** it is applied
   **Then** money columns are `numeric(19,2)` with `Currency`, instants are `timestamptz` ending in `Utc`, `Disputes` maps `xmin` and a `Version`, the partial `IX_Disputes_SlaDueAtUtc` exists, `OutboxMessages` has every AD-22 column the relay needs (`Status`, `Attempts`, `NextAttemptAtUtc`, `SentAtUtc`, `LastError`), and names follow AD-22

9. **Ownership filter and bypass rule** (R17; NFR-SEC-6)
   **Given** the solution
   **When** the architecture tests run
   **Then** `IgnoreQueryFilters(["Ownership"])` appears only under `Operations/` and `Sla/`, `AddDbContextPool` is absent, and with no current customer the filter matches nothing

10. **Rows show their dispute** (R12; FR-12)
   **Given** a transaction with any dispute, in any status
   **When** the transactions list is read
   **Then** the row carries `disputeId` and `customerStatus` and is not disputable with `notDisputableReason` `alreadyDisputed`

11. **Raise metrics** (R36; NFR-OBS-3)
   **Given** raises that succeed and that get 409 or 422
   **When** metrics are collected
   **Then** `disputedesk.disputes.raised` counts by `reason` and `disputedesk.raise.rejections` by `status`, with no personal data in labels

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- `dev-login.sh` as A, raise through the gateway, then `qa_readonly` shows the four rows with one instant.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes, migration.
- Lane: A.
- Touches: `backend/src/DisputeDesk.Domain/Disputes/`; `backend/src/DisputeDesk.Contracts/Disputes/, Events/, Audit/`; `backend/src/DisputeDesk.Application/Disputes/RaiseDispute/`; `backend/src/DisputeDesk.Application/Audit/`; backend/src/DisputeDesk.Application/Transactions/ (dispute status on rows); backend/src/DisputeDesk.Infrastructure/Persistence/ (configurations, SaveChanges override, ownership filter, Migrations/); `backend/src/DisputeDesk.Api/Disputes/`; `backend/src/DisputeDesk.Api/Program.cs`; `backend/openapi.json`; `contracts/events/disputedesk.dispute.raised.v1.schema.json and sample`; backend/tests/ (Domain.Tests/Disputes/, Api.IntegrationTests/Disputes/, ArchitectureTests/).

## References

- parent — docs/tickets/epic-disputes/epic-disputes.md

## Notes

- Risk high: money and the commit path; beyond the criteria, the reviewer checks the migration names against AD-22.
