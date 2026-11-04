---
id: 2
type: story
title: "Backend tidy: one copy of each helper, and no unused members"
parent: epic-8-simplicity-sweep
covers: [R25, R27, R34]
after: []
hitl: false
risk: medium
refined: true
---

# Backend tidy: one copy of each helper, and no unused members

## Description

Slice S1 of the mitigation plan (wave 2), with the human's decision D10 (2026-11-03: remove `GetAccountsAsync` and `Account`). The audit found the backend's production code by the book, with small drift: the Worker treats a missing connection string as a transient outage and stays green while it can never publish (CORE-1, the one finding that matters in production); five copies of the microsecond cut, four of the not-found error, three of the money format (CORE-2 to CORE-4); a one-caller replay helper that costs withdraw an extra query (CORE-5); a nullable `Result` that means "retry" (CORE-6); unused `Result` factories and null guards the compiler already rules out (CORE-8); Domain errors that take `System.Enum` so Application can pass wire values (CORE-9); linking registered in three calls and mapped in three (API-3); a metric label passed by hand (API-4); a class named for headers it does not set (API-5); a dependency comment two lines from its package (API-6); and a port member no requirement reads (CORE-7, DES-14). Behaviour is unchanged except CORE-1: a Worker without a usable connection string now fails at start.

## Requirements

Verify: `dotnet build` with warnings as errors, `dotnet format --verify-no-changes` and the full backend suite pass; the existing tests named below pass unchanged unless the criterion names the change; the OpenAPI drift check passes with no change to `openapi.json`.

Inputs (no endpoint changes):
- `ConnectionStrings:DisputeDesk` for the Worker: required, non-empty and not only white space. Missing, empty or white space fails start-up.

1. **The Worker refuses to start without its connection string** (CORE-1; R27, NFR-RES-1)
   **Given** the Worker host (`Worker/WorkerHost.cs`)
   **When** it starts with `ConnectionStrings:DisputeDesk` missing, empty or only white space
   **Then** start-up fails through the options pattern (`AddOptions<…>().Validate(…).ValidateOnStart()`, as `WorkerHost.cs:16-19` already does for Kafka), with a message that names `ConnectionStrings:DisputeDesk` and never its value; a valid value starts as today
   **And** the relay reads the connection string from the validated options, so its `?? ""` and its `ConnectionStringMissing` branch (`Infrastructure/Outbox/OutboxRelay.cs:115`, `:123-131`) go, with the `InlineData("", "ConnectionStringMissing")` row of `OutboxRelayDependencyTests.Relay_PostgresUnreachable_WarnsWithoutTheConnectionStringAndRetriesAtLeastEveryTenSeconds`; a new theory proves the missing, empty and white-space cases fail at start and the failure names the setting without a value
   **And** the hosts that start the Worker with no connection string today (`WorkerHostTests.WorkerHost_StartThenStop_CompletesCleanly`, `WorkerTelemetryTests.cs:19,42,65`) are given an unreachable one (`Host=127.0.0.1;Port=1;…`, as `OutboxRelayDependencyTests.cs:46`), so they do not depend on the machine's environment and still never reach a real database

2. **One copy each of the clock cut, the not-found error and the money format** (CORE-2, CORE-3, CORE-4; R34, AD-4)
   **Given** the five `ToWholeMicroseconds` and three `Later` copies (`RaiseDisputeHandler.cs:122,132`, `WithdrawDisputeHandler.cs:113,116`, `OperationsActionHandler.cs:161,164`, `Seeder.cs`, `Worker/Sla/SlaSweep.cs:241,243`), the four `Error.NotFound("not-found", "There is no such dispute.")` copies (`GetDisputeHandler.cs:17`, `WithdrawDisputeHandler.cs:22`, `OperationsActionHandler.cs:23`, `ReadDisputeHandler.cs:22`) and the three money formats (`CustomerView.cs:71`, `OperationsView.cs:60`, `ListTransactions.cs:73`)
   **When** this story lands
   **Then** each is one public member used by every caller: two pure Domain functions, `ToWholeMicroseconds(DateTimeOffset)` and `Later(DateTimeOffset, DateTimeOffset)`, that read no clock (each use case still reads `TimeProvider` once and passes the instant, AD-4; raise also cuts the transaction date with the same function); `DisputeErrors.NotFound`; one `ToMoney` on `DisputeWire`
   **And** the 404 bodies stay identical (AD-11: `Ownership/NotFoundResponsesTests` passes unchanged)

3. **Withdraw replays inline and clashes are caught where they are retried** (CORE-5, CORE-6; R34, AD-15)
   **Given** `DisputeReplay.LoadDisputeAsync` (one production caller) and the two `Try…Async` methods that return `null` on a version clash
   **When** this story lands
   **Then** withdraw reads its stored-event rows through the ownership filter and replays them inline, as `OperationsActionHandler` does through its bypass, taking the retry instant from the rows it read (no `LatestOccurredAtAsync` query); `DisputeReplay` is deleted and `StoredEventReplayTests` replays rows it reads itself
   **And** each handler's `HandleAsync` reads the clock once, calls a plain `ActAsync` and catches `UniqueViolationException` when its error is `DisputeErrors.VersionClash`, replays and retries once with the same clock reading, and on a second clash reads the status and answers 409 (EF Core, *Handling concurrency conflicts*); no `Result<T>?` remains
   **And** for withdraw and for an operations action, a test proves a first clash is retried and succeeds with the original instant on the stored event, and a second clash answers today's 409 body with the current status and writes today's refusal audit entry; existing AD-15 tests that already prove a case are named in the report, and a missing case is added

4. **`Result` has no unused factory and no null guard** (CORE-8; R34)
   **Given** `Domain/Result.cs:22,24,35,37`
   **When** this story lands
   **Then** `Result.Failure(Error)` and `Result.Failure<T>(Error)` are deleted with their `ResultTests` cases, the implicit conversions build the failures, and the `?? throw new ArgumentNullException` guards go; `Success`, `Success<T>` and the implicit operators stay

5. **Caller-specific errors are typed and built in Application** (CORE-9; R25, AD-1)
   **Given** `DisputeErrors.AlreadyDisputedAs(Guid, Enum)`, `ActionNotAllowedAs(Enum)`, `ActionNotAllowedWithDisputeStatus(Enum)` (`Domain/Disputes/DisputeErrors.cs:14,37,45`), `Error.ActionNotAllowed(…, string statusField, Enum)` (`Domain/Error.cs:31`) and `Error.BusinessRule(…, string extension, Enum)` (`Error.cs:43`)
   **When** this story lands
   **Then** the base errors stay in Domain, and the variants that carry a wire value are built in Application beside `DisputeWire.ToCustomerStatus`, taking `Contracts.Disputes.CustomerStatus` or `Contracts.Disputes.DisputeStatus`; because `Error.Extensions` is `private init` (`Error.cs:17`), Domain offers `Error.ActionNotAllowed(string code, string message, IReadOnlyDictionary<string, object?> extensions)`, shaped like `Error.Conflict` at `Error.cs:37`, and Application passes the extension dictionary to it and to the existing `Error.Conflict` overload
   **And** `Error.BusinessRule(…, Enum)` is replaced the same way by the existing `BusinessRule(…, IReadOnlyDictionary<string, object?>)` overload at `Error.cs:47`, so no Domain member takes `System.Enum`
   **And** every error code and body is unchanged (`ProblemCatalogueTests` and the 409 and 422 body tests pass unchanged)

6. **Linking is registered and mapped like the customer routes** (API-3; R34)
   **Given** `AddLinkingRoutes`, `AddLinkingStart`, `AddLinkingRateLimits` (`Api/Program.cs:63-65`) and `MapLinkingRoutes().MapLinkingStart().MapLinkingVerify()` (`Program.cs:87`)
   **When** this story lands
   **Then** the linking routes follow the customer routes' shape (`AddCustomerRoutes`/`MapCustomerRoutes` for the authorisation group, then one Add and one Map per feature): `AddLinkingRoutes` and `MapLinkingRoutes` (`Api/Authorization/LinkingRoutes.cs:17,33`) stay as the authorisation group, the feature has one `AddLinkingEndpoints` (the rate limits of `Api/Linking/RateLimiting/LinkingRateLimits.cs:32` included) and one `MapLinking` that maps both routes, and `StartLinkingHandler` is registered beside `VerifyOtpHandler` in Infrastructure's `AddLinking` (`Infrastructure/Linking/LinkingRegistration.cs:41`); Program.cs reads `AddLinkingRoutes(); AddLinkingEndpoints();` and `apiV1.MapLinkingRoutes().MapLinking();`; the linking tests pass unchanged

7. **Names and labels say what the code does** (API-4, API-5, API-6; R34)
   **Given** `ActAsync`'s `string metricAction` (`Api/Operations/OperationsEndpoints.cs:152,165`), `SecurityHeaders.UseSecurityHeaders` (`Api/Security/SecurityHeaders.cs:8,14`, which sets only `Cache-Control: no-store`) and the MailKit comment (`backend/Directory.Packages.props:22`)
   **When** this story lands
   **Then** `DisputeActionMetrics.RecordOperationsAction(DisputeAction)` derives the label with `JsonNamingPolicy.CamelCase.ConvertName`, as `RaiseMetrics.RecordRaised` does, and `ActAsync` drops the parameter, with the metric tests unchanged; the class and method are renamed `NoStoreApiResponses.UseNoStoreOnApiResponses` with the `OnStarting` mechanism kept; the MailKit comment sits directly above the `MailKit` entry

8. **The core-banking port has no accounts member** (CORE-7, DES-14, D10; R25)
   **Given** `ICoreBankingClient.GetAccountsAsync` (`Application/Abstractions/ICoreBankingClient.cs:15`), `CoreBankingClient.cs:38-47`, `Application/Abstractions/Account.cs` and the stub's `GET /accounts` (`stubs/DisputeDesk.CoreBankingStub/Program.cs:34`)
   **When** this story lands
   **Then** all four are removed, with the linking fake's member (`Linking/LinkingFakes.cs`); the `CoreBankingResilienceTests` cases on `GetAccountsAsync` (timeout, cancellation, retry, malformed JSON, circuit open and closed) call `GetTransactionsAsync` instead, each keeping what it proves
   **And** `CoreBankingClientTests.GetAccountsAsync_CustomerWithTwoAccounts_ReturnsBothByReferenceAndLast4` (`:83`) and `MaskedFieldsTests.GetAccountsAsync_FullAccountNumber_ReturnsUnavailable` (`:96`) are deleted, because they prove only the removed member (the masked account number on a transaction row stays proved by `GetTransactionsAsync_RowNotMasked_ReturnsUnavailable`); `FixtureTests.Transactions_EachSeededCustomer_…` (`:23`) checks that each customer's transaction rows carry one or two distinct `accountRef` values instead of counting accounts
   **And** `TransactionShapeTests.AllStubResponsesAsync` (`:75`) stops requesting `accounts`, so its full-account-number check still covers every remaining stub response (transactions, customer match, SIM-swap check)

## Boundaries

- Must not change: any HTTP status, body, header or route other than the stub's `/accounts`; any stored event, outbox row or audit entry; `openapi.json`; the AD-15 retry behaviour (the instant, the replay on the first clash, the status read after the second).
- No new package.

## Supervisor checks

- Throwaway copy: start the Worker with `ConnectionStrings__DisputeDesk=" "`; it exits non-zero naming `ConnectionStrings:DisputeDesk`.
- Throwaway copy: drop the microsecond cut from `ToWholeMicroseconds`; the 2.8 truncation tests fail.
- Throwaway copy: make the shared `NotFound` message differ by one word; `NotFoundResponsesTests` fails.
- Throwaway copy: make withdraw read a fresh clock on its retry; criterion 3's first-clash test fails.

## Build facts

- Owner: backend (backend-builder).
- Tier: High-stakes: it touches account linking's registration, the money format customers see and the dispute write path, so the security agent reads the diff and the human reviews it in a review block (AGENTS.md, Stakes tiers).
- Flags: high-stakes, security.
- Lane: A.
- Mechanisms and sources:
  - Options validation on start: <https://learn.microsoft.com/dotnet/core/extensions/options#options-validation>.
  - Concurrency conflicts, catch and retry: <https://learn.microsoft.com/ef/core/saving/concurrency>.
  - `TimeProvider` read once at the call site: <https://learn.microsoft.com/dotnet/standard/datetime/timeprovider-overview>.
  - `HttpResponse.OnStarting`: <https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.http.httpresponse.onstarting>.
  - `JsonNamingPolicy.CamelCase`: <https://learn.microsoft.com/dotnet/api/system.text.json.jsonnamingpolicy.camelcase>.
  - Abstract on the third real case: AGENTS.md, How to work.
- Packages: none.
- Touches: `backend/src/DisputeDesk.Worker/WorkerHost.cs`, `Infrastructure/Outbox/OutboxRelay.cs`, the five clock copies, the four handlers' `NotFound`, the three views, `Application/Disputes/DisputeReplay.cs` (deleted), `WithdrawDisputeHandler.cs`, `OperationsActionHandler.cs`, `Domain/Result.cs`, `Domain/Disputes/DisputeErrors.cs`, `Domain/Error.cs`, `Application/Disputes/DisputeWire.cs`, `Api/Program.cs`, `Api/Linking/LinkingEndpoints.cs`, `Api/Authorization/LinkingRoutes.cs`, `Api/Linking/RateLimiting/LinkingRateLimits.cs`, `Infrastructure/Linking/LinkingRegistration.cs`, `Api/Operations/OperationsEndpoints.cs`, `Api/Disputes/DisputeActionMetrics.cs`, `Api/Security/SecurityHeaders.cs` (renamed), `Directory.Packages.props`, `Application/Abstractions/ICoreBankingClient.cs`, `Account.cs` (deleted), `Infrastructure/CoreBanking/CoreBankingClient.cs`, `stubs/DisputeDesk.CoreBankingStub/Program.cs`, and the tests named above (`WorkerHostTests`, `WorkerTelemetryTests`, `OutboxRelayDependencyTests`, `StoredEventReplayTests`, `ResultTests`, `CoreBankingResilienceTests`, `CoreBankingClientTests`, `MaskedFieldsTests`, `FixtureTests`, `TransactionShapeTests`, `LinkingFakes`).
- Collision: 8.4 moves the same test projects to standard tools in different files; 8.10 changes the withdraw and operations handlers after this story.

## Pre-build

- Challenger: `docs/verification/8.2-challenge.md` (simplify).
- Review: `docs/verification/epic-8-prebuild-wave2.md`, section 8.2. No security read before the build; the tier now asks for one on the diff.
- Decisions: none raised.
- Sofia, 2026-11-04: every `[story]` point folded. Review: the Worker hosts get an unreachable connection string and the relay reads the validated options (criterion 1); the pure cut also serves the transaction date (criterion 2); the named Domain factory and `BusinessRule(…, Enum)` in scope (criterion 5); `LinkingRoutes.cs` and `LinkingRateLimits.cs` in Touches, the reason corrected and the authorisation group kept apart (criterion 6); the accounts-only tests deleted and `FixtureTests` restated (criterion 8). Challenger: white space refused and the message checked (criterion 1, Inputs); `TransactionShapeTests` keeps covering every remaining response (criterion 8); the retry keeps its instant, first-clash replay and second-clash status read, with tests per handler (criterion 3, Boundaries); the Domain functions stay pure (criterion 2); the tier raised to High-stakes (Build facts).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- audit — docs/verification/simplicity-audit/core-layers.md, CORE-1 to CORE-9; api-host.md, API-3 to API-6; design.md, DES-14
- plan — docs/verification/simplicity-audit/mitigation-plan.md, section 1 (D10), wave 2, S1
- architecture — docs/architecture/architecture.md, AD-1, AD-3, AD-4, AD-11, AD-13, AD-15, AD-21, AD-25

## Notes

- Risk medium (proposed): every change but CORE-1 is covered by tests that stay unchanged, but the story touches linking, money and the dispute write path; CORE-1 turns a silent green heartbeat into a start-up failure. Check beyond the criteria: the security agent's read of the diff.
- Human decision, 2026-11-03 (D10): remove `GetAccountsAsync`, `Account` and the stub's accounts route, and point the resilience tests at `GetTransactionsAsync`.
- Decision: 2026-11-04 (Sofia) — CORE-6 takes the audit's second option (catch the clash in `HandleAsync` around two plain calls), because it is EF Core's documented shape and needs no tuple or `out`.
- Decision: 2026-11-04 (Sofia) — API-5's new name is the audit's suggestion, `NoStoreApiResponses.UseNoStoreOnApiResponses`.
- Decision: 2026-11-04 (Sofia) — AD-21's port list (`architecture.md`, AD-21, "One story ships the whole port") drops `GetAccountsAsync` in a docs-only change the supervisor lands with this story, recording D10; the builder changes only code.
- Decision: 2026-11-04 (Sofia) — API-3 keeps the linking authorisation group (`AddLinkingRoutes`, `MapLinkingRoutes`) apart from the feature, as the customer routes keep theirs, so linking matches the shape already in Program.cs rather than a new one; the audit's "one pair per feature" holds for the feature itself.
- Decision: 2026-11-04 (Sofia) — the tier is High-stakes on the challenger's point: AGENTS.md lists account linking and money, and the change touches both, even though behaviour stays the same.
