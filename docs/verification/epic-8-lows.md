# Epic 8 lows (fixed in one batch at the end of the epic)

| Story | Source | File | Finding |
|---|---|---|---|
| 8.1 | review | `infra/aws/check-compose.sh:2` | The SC2016 disable covers the whole file; all 9 current hits are jq variables. Move to per-line directives if a real hit ever hides. |
| 8.2 | review, security L1 | `backend/tests/DisputeDesk.Worker.IntegrationTests/Outbox/OutboxRelayDependencyTests.cs:34` | The `null` row starts the full Worker host with no override; with `ConnectionStrings__DisputeDesk` set, the relay and SLA sweep would run against that database. Remove the `SlaSweep` registration there, as `WorkerHostTests` does. |
| 8.2 | review | `backend/src/DisputeDesk.Application/Disputes/CustomerView.cs:68`, `Operations/OperationsView.cs:57` | Leftover `// AD-4: exactly two decimals` comments with nothing under them; delete. |
| 8.2 | review | `backend/src/DisputeDesk.Infrastructure/ConnectionStringsOptions.cs:13`, `Worker/WorkerHost.cs:23` | The `DisputeDesk` key is spelled three ways; build the validation message from the constants. |
| 8.2 | review | `backend/src/DisputeDesk.Api/Linking/LinkingEndpoints.cs:16` | `AddLinkingEndpoints` forwards one call (prescribed by criterion 6); retrospective note. |
| 8.3 | review | `e2e/tests/linking-screens.spec.ts:553` | Stale comment "SessionGate sends a linked customer away"; name the `_linked` layout route's `beforeLoad` (QA owns e2e). |
| 8.4 | security L1 | `backend/tests/DisputeDesk.Worker.IntegrationTests/Sla/SweepRig.cs` | The sweep privacy test reads two named collectors, not every instrument on the meter; a new sweep instrument would go unchecked. |
| 8.4 | security L2 | `backend/src/DisputeDesk.Infrastructure/CoreBanking/CoreBankingRegistration.cs` | `RemoveAllLoggers` on `ISimSwapCheck` has no test. |
| 8.3 | delta review | `frontend/src/features/disputes/raise/RaiseLayout.tsx:70` | `useParams({ strict: false })` with a `""` default; use `from: "/_linked/transactions/$transactionId/dispute"`. |
| 8.4 | review | `backend/tests/DisputeDesk.Api.IntegrationTests/ProblemDetailsTests.cs:302` | The 403 row now proves `linking-required`; add a framework 403 (`forbidden`) row on an operations route with a bearer token lacking the role; decide whether the cookie `OnRedirectToAccessDenied` branch stays. |
| 8.4 | review | `backend/tests/DisputeDesk.Api.IntegrationTests/Linking/StartLinkingQueueFullTests.cs:63` | Put the absence window after `WaitForStopsAsync` too, so a late refused start is caught by more than timing. |
| 8.4 | review | `backend/tests/DisputeDesk.Api.IntegrationTests/CoreBanking/CoreBankingClientTests.cs:85` | The configuration-Trace test repeats the existing proof; keep (the human asked for the configuration route) or merge. |
| 8.4 | review | `LogLines.cs:14`, `SweepRig.cs:216` | Use `FakeLogRecord.StructuredState ?? []` instead of casting `State`. |
| 8.6 | review | `e2e/tests/spa-transactions.spec.ts:12` | `gateway` duplicates the shared `origin`; import `origin`. |
| 8.6 | review | `linking-start`, `linking-rate-limits`, `linking-screens` (ten polls) | Poll the value (`toBe(4)`) instead of a boolean, so a timeout shows the count. |
| 8.6 | review | `linking-screens.spec.ts:300` and the other absence checks | `.rejects.toThrow(errors.TimeoutError)` states the intent exactly. |
