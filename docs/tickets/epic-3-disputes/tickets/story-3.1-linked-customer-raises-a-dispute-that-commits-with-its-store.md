---
id: 1
type: story
title: "Linked customer raises a dispute that commits with its stored event and outbox row"
parent: epic-3-disputes
covers: [R10, R12, R13, R14, R17, R23, R28, R29, R35, R36]
after: [2.3]
hitl: false
risk: high
---

# Linked customer raises a dispute that commits with its stored event and outbox row

## Description

Adds the `Dispute` aggregate (AD-13), whose raise appends the `DisputeRaised` stored event (ad-009), the one commit path (AD-14), the ownership filter (AD-11) and `POST /api/v1/disputes`; the transactions list starts showing each row's dispute status. Story 3.8 completes the stored-event design: every stored event type, loading by replay and the publish mapping.

## Requirements

Verify: Domain unit tests and Testcontainers integration tests pass; raising for customer A writes one row each in `Disputes`, `StoredEvents` and `OutboxMessages` and no `AuditLogEntries` row.

Request inputs (`POST /api/v1/disputes`, JSON body, built-in minimal API validation):

| Field | Type | Bounds |
|---|---|---|
| `accountRef` | string | required, 1 to 32 characters, the opaque value from the transactions list; a value not shaped like an ID (`CoreBankingClient.IdShape`) gets 404 `not-found`, as in story 2.3 |
| `transactionId` | string | required, 1 to 32 characters, same shape rule |
| `reason` | string enum | required, one of the six AD reasons (`unrecognised`, `duplicateCharge`, `wrongAmount`, `notReceived`, `cancelledSubscription`, `refundNotProcessed`); any other value gets 400 `validation` |
| `note` | string | optional, at most 1,000 UTF-16 code units after trimming (AD-4) |
| `expectedAmount` | decimal string, ZAR implied (AD-4) | required only for `wrongAmount`, rejected for every other reason; matches `^[0-9]{1,17}(\.[0-9]{1,2})?$` (at least 0.00, at most 2 decimals, within `numeric(19,2)`), otherwise 400 `validation`; "below the transaction amount" is checked after the lookup (criterion 4) |

Any other property, such as an amount, date or description, is ignored.

1. **Raise succeeds** (R13; FR-13)
   **Given** a linked customer and their disputable transaction
   **When** they post a reason and an optional note of up to 1,000 characters
   **Then** the response is 201 with status `submitted`, one timeline entry with action `raise` and actor `you`, and `Location: /api/v1/disputes/<disputeId>` (RFC 9110); the GET it names is built in story 3.3, a known gap against AD-5 that closes when 3.3 merges, and story 3.3 proves the GET serves it
   **And** the snapshot comes from a fresh core-banking read, and any amount, date or description in the request is ignored

2. **Raise refusals in order** (R13; FR-13, NFR-API-2)
   **Given** a missing or another customer's transaction, an ever-disputed one, and a pending or old one
   **When** each is raised
   **Then** they get 404 `not-found`, 409 `already-disputed` and 422 `not-disputable` with `reason`, in that check order
   **And** a body that breaks the input table (missing or oversized `accountRef` or `transactionId`, unknown `reason`, note over 1,000 characters after trimming, malformed or negative `expectedAmount`, more than 2 decimals, or an expected amount with another reason) gets 400 `validation` before the transaction is looked up, so the full order is 401, 403, 400 (body shape), 404, 409, 422, 400 (expected amount not below the transaction amount), as AD-8 sets for an ID carried in the body
   **And** an anonymous caller or a bearer token, an unlinked login, and a cookie without `X-XSRF-TOKEN` get 401 `unauthorized`, 403 `linking-required` and 400 `validation`, the first two before any body validation, because the route is on the customer routes group (`RequireAuthorization("Linked")`, AD-10)

3. **One dispute per transaction under concurrency** (R13, R29; FR-13, NFR-DATA-5, NFR-TEST-3)
   **Given** two parallel raises for the same transaction
   **When** both commit
   **Then** exactly one returns 201 and the other 409 `already-disputed`, enforced by `UQ_Disputes_AccountRef_TransactionId` and mapped by constraint name in one place (AD-15)
   **And** afterwards `Disputes`, `StoredEvents` and `OutboxMessages` each hold exactly one row for that transaction

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
   **Given** a raise whose save fails in the database after an earlier insert of the same save (a test-only EF Core interceptor fails the `OutboxMessages` insert)
   **When** the transaction rolls back
   **Then** no dispute, stored event or outbox row remains, and the response is a 500 ProblemDetails
   **And** a successful raise writes no audit entry, and the commit override rejects any modified or deleted stored event or audit entry

7. **Raised event in the outbox** (R23; FR-27, NFR-SEC-5, NFR-TEST-4)
   **Given** a successful raise
   **When** the outbox row is read
   **Then** its `Payload` is a CloudEvents 1.0 structured JSON event with `specversion` `1.0`, `id`, `source`, `type` `disputedesk.dispute.raised.v1` and the AD-19 `subject`, `time`, `datacontenttype` and `dataschema`, whose data is exactly `disputeId`, `reason`, `status`, `occurredAt` and `disputeVersion` 1, with `TraceParent` stored from the current activity (W3C Trace Context); the column is nullable and stays null when no activity is recorded
   **And** a contract test validates it against the committed JSON Schema

8. **Data model rules** (R29; NFR-DATA-1, NFR-DATA-4, NFR-DATA-5, NFR-DATA-7)
   **Given** the migration
   **When** it is applied
   **Then** money columns are `numeric(19,2)` with `Currency`, instants are `timestamptz` ending in `Utc`, `Disputes` carries a `Version` and maps no concurrency token, `StoredEvents` is unique on (`DisputeId`, `Version`) (AD-15), the partial `IX_Disputes_SlaDueAtUtc` exists, `OutboxMessages` has every AD-22 column the relay needs (`Status`, `Attempts`, `NextAttemptAtUtc`, `SentAtUtc`, `LastError`), and names follow AD-22

9. **Ownership filter and bypass rule** (R17; NFR-SEC-6)
   **Given** the solution
   **When** the architecture tests run
   **Then** `IgnoreQueryFilters(["Ownership"])` appears only under `Operations/` and `Sla/`, `AddDbContextPool` is absent, and with no current customer the filter matches nothing; the filter reads a nullable customer ID held on the context instance (EF Core's documented per-context filter parameter), never `CurrentCustomer.CustomerId`, which throws when unset
   **And** `StoredEvent` carries the matching `Ownership` filter through its dispute, as EF Core advises for filters on required navigations

10. **Rows show their dispute** (R12; FR-12)
   **Given** a transaction with any dispute, in any status
   **When** the transactions list is read
   **Then** the row carries `disputeId` and `customerStatus` and is not disputable with `notDisputableReason` `alreadyDisputed`
   **And** every row has both fields in the contract, nullable and null on a row with no dispute (an additive change to `openapi.json`)

11. **Raise metrics** (R36; NFR-OBS-3)
   **Given** raises that succeed and that get 409 or 422
   **When** metrics are collected
   **Then** `disputedesk.disputes.raised` counts by `reason` and `disputedesk.raise.rejections` by `status`, with no personal data in labels

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- `dev-login.sh` as A, raise through the gateway, then `qa_readonly` shows the dispute, its stored event and its outbox row with one instant.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes, migration.
- Lane: A.
- Touches: `backend/src/DisputeDesk.Domain/Disputes/`; `backend/src/DisputeDesk.Contracts/Disputes/, Events/, Audit/`; `backend/src/DisputeDesk.Application/Disputes/RaiseDispute/`; `backend/src/DisputeDesk.Application/Audit/`; backend/src/DisputeDesk.Application/Transactions/ (dispute status on rows); backend/src/DisputeDesk.Infrastructure/Persistence/ (configurations, SaveChanges override, ownership filter, Migrations/); `backend/src/DisputeDesk.Api/Disputes/`; `backend/src/DisputeDesk.Api/Program.cs`; `backend/openapi.json`; `contracts/events/disputedesk.dispute.raised.v1.schema.json and sample`; backend/tests/ (Domain.Tests/Disputes/, Api.IntegrationTests/Disputes/, ArchitectureTests/).

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md

## Notes

- Risk high: money and the commit path; beyond the criteria, the reviewer checks the migration names against AD-22.
- Decision: 2026-10-27 — 2.7 security review: `already-disputed` (409) is returned only after the ownership check, so another customer's disputed transaction gives 404 `not-found`; the security review confirms the order.
- Decision: 2026-10-28 — challenge (`docs/verification/3.1-challenge.md`, RETHINK), decided by the human: the `Location` target GET stays in story 3.3 as a known temporary gap against AD-5; body-shape 400 runs before the 404 because the IDs travel in the body (AD-8 amended); the request inputs, the row counts under concurrency, the nullable `TraceParent` and the nullable row fields are added above. The lookup uses the existing `GetTransactionAsync(customerId, accountRef, transactionId)` with `accountRef` from the body; `accountRef` is stable per AD-21, so the unique index holds.
- Decision: 2026-10-28 — second challenge (SIMPLIFY), decided by the human: ID bounds match the adapter's 1 to 32; `expectedAmount` is the AD-4 decimal string with a pattern bound; the rollback test fails a real database insert; the ownership filter reads a nullable per-context value. Currency: core banking is assumed to return the ZAR billed amount (AD-4, FR-14; every fixture is ZAR); the snapshot stores the currency as received, and the builder marks the assumption `// shortcut: amount assumed ZAR billed, add a currency check when a non-ZAR source exists` and adds a line to `docs/production-path.md`. No third challenge run.
