---
id: 9
type: story
title: "Customer lists their disputes newest first, a page at a time"
parent: epic-3-disputes
covers: [R16, R17, R18, R11, R30, R31]
after: [3]
hitl: false
risk: high
---

# Customer lists their disputes newest first, a page at a time

## Description

Adds `GET /api/v1/disputes`, the history list, on story 3.3's customer view mapping: rows read the `Disputes` read model through the ownership filter with keyset paging (AD-5, AD-11). Proves the paging bounds, ownership across pages and cursors, the authorization states and privacy on the list, the wire format of timestamps and enums on every customer route, and carries 3.8's alias-rule fix.

## Requirements

Verify: Integration tests for the list, paging bounds and cursor validation, list ownership, the authorization states, privacy of every field, the core-banking outage and snapshot precision, and wire timestamps and enums on every customer route pass; the architecture tests pass; `openapi.json` changes only by the list operation, `DisputeSummaryResponse` and the page shape.

1. **History list** (R16; FR-16, NFR-API-3)
   **Given** a customer with disputes, one of them resolved
   **When** they call `GET /api/v1/disputes`
   **Then** the body is `{ "items": [...], "nextCursor": ... }` (AD-5) and each item is a `DisputeSummaryResponse`: `disputeId`, `customerStatus`, `outcome` (`upheld` or `rejected` when resolved, else null), `reason`, `transaction` (the stored snapshot, `TransactionSnapshotResponse`), `disputedAmount` and `createdAt`
   **And** rows come newest first by `CreatedAtUtc` then `Id`, both descending in SQL, read from the `Disputes` read model through the ownership filter (EF Core, *Global query filters*; AD-11) with keyset paging (EF Core, *Pagination*: keyset pagination); `Id` order is PostgreSQL's `uuid` order, which tests compare as the lowercase canonical strings
   **And** the keyset predicate is translated to SQL, not evaluated in memory: the builder uses either `Guid.CompareTo` in the predicate or the Npgsql EF provider's row-value comparison, verifies from the generated SQL that the installed provider (10.0.3) translates it, and cites the source in the report; no index is added, and a `// shortcut:` comment names `IX_Disputes_CustomerId_CreatedAtUtc_Id` for when the list grows

2. **Paging bounds and cursor validation** (R16, R30; NFR-API-3)
   **Given** the list's inputs: `limit`, a typed `int` with `[Range(1, 50)]` defaulting to 20, checked by built-in minimal API validation; and `cursor`, an optional string of at most 128 characters, the base64url (`System.Buffers.Text.Base64Url`, as `TransactionCursor` does; RFC 4648 §5) of `CreatedAtUtc` in the round-trip format and `Id` in the canonical GUID format
   **When** the customer pages with `limit` 0, 51 or `abc`; a `cursor` of 129 characters; a `cursor` that is not base64url; one that decodes to no instant and ID pair; one whose instant has a non-zero offset (such as `2026-10-01T10:00:00+02:00`); and one with content after the ID
   **Then** each gets 400 `validation` naming the field (`limit` or `cursor`), as the transactions list does, and no body contains the submitted `cursor` string or `abc` (AD-3: errors never echo a value; Microsoft Learn, *Validation in minimal APIs*)
   **And** with two disputes sharing one `CreatedAtUtc` split across a page boundary (`limit` 1), each appears exactly once in `Id` order, and the last page's `nextCursor` is null
   **And** the builder verifies whether built-in minimal API validation enforces `[MaxLength(128)]` on a query-string parameter in the installed .NET 10; if it does not, the handler checks the length as `TransactionCursor` does. The 129-character case proves the bound either way

3. **The list holds only the caller's disputes** (R17; NFR-SEC-6, NFR-TEST-3)
   **Given** customers A and B with disputes interleaved by `CreatedAtUtc`, one A and B pair sharing an instant
   **When** A pages through the list with `limit` 1 and 2, then with a cursor that encodes the position of B's dispute, and with `?customerId=<B>` and a customer-ID header
   **Then** every page holds only A's disputes and B's rows never change A's `nextCursor`; the cursor on B's position gets 200 with only A's rows after that position, the same answer as for a position no dispute holds; the query parameter and header are ignored, as `ListTransactions_CustomerIdInQueryAndHeader_StillReturnsTheLinkedCustomer` proves for transactions (ASVS 5.0 V8.2.2)
   **And** the cursor is a position, not a capability: ownership comes only from the filter (AD-11), so the cursor carries no signature

4. **Authorization states and check order on the list** (R17; NFR-SEC-6, FR-24)
   **Given** `GET /api/v1/disputes`
   **When** it is called anonymously, by an unlinked login, by a linked customer, by an unlinked login with `limit=51` or a 129-character `cursor`, and with only `Authorization: Bearer <token>` and no cookie
   **Then** anonymous gets 401 `unauthorized` with no redirect and no `WWW-Authenticate`; unlinked gets 403 `linking-required`; linked gets 200; unlinked with bad inputs still gets 403, because checks run 401, 403, then 400; bearer-only gets 401 (AD-8, AD-10; Microsoft Learn, *Policy-based authorization*)
   **And** none of these calls writes an `AuditLogEntries` row (AD-14a), and `GET /api/v1/disputes` joins story 3.3's independent customer route list

5. **The list never shows internals** (R18, R31; FR-16, NFR-API-5)
   **Given** disputes acted on by operations and by automation, one escalated, with `SlaWarningRaised` in a stream
   **When** list pages are serialised
   **Then** the page has exactly `items` and `nextCursor`, each item exactly `disputeId`, `customerStatus`, `outcome`, `reason`, `transaction`, `disputedAmount`, `createdAt`, and the snapshot and money objects exactly story 3.3's property names
   **And** the value scan of story 3.3, criterion 2, finds nothing in any page (no actor type value, `escalated`, `CustomerId`, `SlaDueAtUtc`, `ActorId`, `TraceId` or `SlaWarningRaised`); escalated shows as `underReviewPrioritised`; a new Application list-row record that holds the snapshot or the customer ID seals `ToString` (AD-13; 3.8 Known pitfall)

6. **The list works while core banking is down, with the snapshot's precision** (R11, R16; NFR-RES-2, FR-16)
   **Given** a dispute raised on a transaction whose date has 7 fractional digits, then the core-banking stub made unreachable
   **When** the customer lists their disputes
   **Then** the list answers 200 from the stored snapshots, and `transaction.transactionDate` is the same string as in the raise 201 and the detail (story 3.3, criterion 8; PostgreSQL, *Date/Time Types*); the list never calls the core-banking port (AD-21)

7. **Timestamps and enum values on the wire** (R30; NFR-API-1, NFR-API-6)
   **Given** the customer routes and their response timestamps: `transactionDate` in the transactions list items; `createdAt`, `transaction.transactionDate` and `timeline[].occurredAt` in the raise 201 and the detail; `createdAt` and `transaction.transactionDate` in the history list items
   **When** one test reads each literal string from the HTTP response bodies
   **Then** each is RFC 3339 UTC with the offset `+00:00` and fractional seconds as System.Text.Json writes them (its default `DateTimeOffset` format; no custom converter)
   **And** the request enum `reason` on the raise binds through the shared options' `SingleNameEnumConverter`, which wraps the framework's `JsonStringEnumConverter` with camelCase names (AD-5): `"wrongAmount"` and `"WrongAmount"` both bind, since its reads are case-insensitive, and `"notAReason"`, which names no member, gets 400 `validation` (Microsoft Learn, *JsonStringEnumConverter*)

8. **The alias rule covers the stubs and MSBuild aliases** (R30; NFR-TEST-3)
   **Given** the alias rule test (`UsingAliasRulesTests`, `docs/conventions/backend.md`)
   **When** it runs
   **Then** it also scans `backend/stubs`, through a stubs root in `SourceRoots`, and fails on an MSBuild `<Using ... Alias="..." />` item in any `*.csproj` or `*.props` under `backend/`; its regex comment states that it matches one line at a time (carried from 3.8's review; Microsoft Learn, *MSBuild items*, `Using` items with `Alias`; `docs/conventions/backend.md`)

## Boundaries

- Story 3.3's detail, its route list, the identical 404 and the allowlist stay green; this story only adds the list route to the route list.
- The disputes list has no filter in v1; the SPA groups loaded pages into open and closed (AD-5; story 3.7).
- The JSON options of 3.8 (`DisputeDeskJson`, `SingleNameEnumConverter`) do not change: timestamps keep System.Text.Json's default format and enums the framework converter's reading.
- Customer reads, refused ones included, write no audit row (AD-14a).
- The rejected-outcome guidance (FR-17) is static copy in the SPA, story 3.7; this story returns `outcome` so the SPA can show it.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- In a throwaway copy, drop the ownership filter from the list query (`IgnoreQueryFilters`): criterion 3's test and `OwnershipRulesTests` fail.
- In a throwaway copy, accept a cursor instant with a non-zero offset: criterion 2's test fails with a 500, not 400.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2, after 3.3 and before 3.4. It reads 2.8's `TransactionsApi.cs` unchanged; if 2.8 merges first with a changed helper, the builder calls it as merged.
- Names: the handler is `ListDisputesHandler` in `Application/Disputes/ListDisputes/`, so the class is not named like its namespace.
- Touches:
  - `backend/src/DisputeDesk.Application/Disputes/ListDisputes/` (new: `ListDisputesHandler.cs`, `DisputeCursor.cs`)
  - `backend/src/DisputeDesk.Application/Disputes/CustomerView.cs` (the summary mapping)
  - `backend/src/DisputeDesk.Contracts/Disputes/` (new `DisputeSummaryResponse.cs`, `DisputePageResponse.cs`; `DisputeRoutes.cs` gains the list pattern)
  - `backend/src/DisputeDesk.Api/Disputes/DisputeEndpoints.cs` (the list `MapGet`, the handler registration)
  - `backend/openapi.json`
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Disputes/` (new `ListDisputesTests.cs`, `ListDisputesPagingTests.cs`, `ListDisputesAuthorizationTests.cs`, `WireFormatTests.cs`; `CustomerPrivacyTests.cs` and `SnapshotPrecisionTests.cs` extended; `DisputesApi.cs` helpers), never `RaiseDisputeAuthorizationTests.cs`
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Ownership/` (`CustomerRoutesTests.cs` gains the list route; new `ListOwnershipTests.cs`)
  - `backend/tests/DisputeDesk.ArchitectureTests/UsingAliasRulesTests.cs`, `SourceRoots.cs` (the stubs root)
- Used unchanged: `Transactions/TransactionsApi.cs` (the wire-format test reads the transactions list through it), `Infrastructure/ApiFactory.cs`, `Infrastructure/TelemetryCapture.cs`, `Contracts/Json/`, `ApiV1.cs`.
- Not touched: `Infrastructure/Persistence/` (no migration), `Program.cs`, `Authorization/` tests (2.8's), `ApiReferenceTests.cs`, `Application/Audit/`, CoreBanking stub files, the Worker.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md
- architecture — docs/architecture/architecture.md, AD-3, AD-5, AD-8, AD-10, AD-11, AD-13, AD-14a
- challenge — docs/verification/3.3-challenge.md (this story's criteria come from 3.3's challenged set)
- review — docs/verification/3.3-prebuild-review.md, docs/verification/3.3-prebuild-security.md

## Notes

- Risk high: cross-customer leakage through the list or its cursor; beyond the criteria, the security agent pages A's list with B's positions.
- Decision: 2026-10-29 (Sofia, pre-build review) — split from 3.3 to keep each story within about 8 criteria (AGENTS.md, Definition of ready); its criteria are 3.3's challenged criteria 1, 2, 7, 9, 10 (list part) and 11 plus the pre-build review's points.
- Decision: 2026-10-29 (human, 3.3 challenge) — enum values are read by the framework default, case-insensitive (criterion 7).
- Collision check (2026-10-29): runs on M2 after 3.3; no file in its Touches is in 2.8's (Bff/, Authorization tests, `ApiReferenceTests.cs`, `TransactionsApi.cs`, `Application/Audit/`, CoreBanking stub files) or in 4.1's or 4.2's. No migration.
