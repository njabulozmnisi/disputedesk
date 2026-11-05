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
| 8.5 | review | `backend/tests/DisputeDesk.ArchitectureTests/SourceTrees.cs:21` | A file that fails to parse is not detected (Roslyn recovers); add one fact asserting no error diagnostics for every scanned file. |
| 8.5 | review | `infra/tests/DisputeDesk.PlatformTests/DisputeDesk.PlatformTests.csproj` | The "change both together" comment no longer holds once backend tests carry BannedApiAnalyzers; reword to "the shared compiler settings". |
| 8.5 | CI (main red, runs 41823293459, 41823293468) | `backend/Dockerfile` | Every backend project needs `BannedSymbols.txt`, which the Dockerfile did not copy; host builds passed, image builds failed. Fixed in the next commit; escape: a change to `Directory.Build.props` inputs builds the images before reporting (build-story pitfall candidate). |
| 8.10 | security S1 | `OperationsEndpoints.cs` (resolve) | A customer cookie on a `text/plain` resolve gets 415 (routing's 415 endpoint carries only the cookie fallback policy); nothing written, no ID revealed. Pin it with a test and one AD-8 clause. |
| 8.10 | security S2 | `CustomerRoutes.cs` | Withdraw body validation runs before the group's `AntiforgeryFilter` (both 400, no handler runs); state the antiforgery check's place in AD-8's order. |
| 8.10 | review | `WithdrawDisputeInputTests.cs:108`, `OperationsActionInputTests.cs:256` | Contract note: a non-GUID route ID plus a bad body now answers 400 naming the body field. |
| 8.11 | review | `RaiseDisputeInputTests.cs:165-182` | The hand-built rejection body is never shown to bind with `"wrongAmount"`; build it with `Body(...)` or add the valid case. |
| 8.11 | review | same test | The no-echo check reads the note marker, not the refused reason value; add the reason value check for the string cases. |
| 8.13 | delta review L1 | `frontend/src/app/InactivityWarning.tsx:74` | `signOutFailed` resets only on the next sign-out, so a later warning can open with a stale failure alert; reset when the warning closes. |
| 8.13 | delta review L2, L3 | `sessionEnd.test.tsx:144`, `queryClient.ts:59` | Rename the "several 401s" test; comment why `handleSuccess` re-reads the typed cache. |
| 8.8 | review L2 | `infra/tests/DisputeDesk.PlatformTests/ShellHarnessTests.cs:11` | Assert each named exception file exists, so 8.16 must remove the `iam-names.test.sh` exception. |
| 8.8 | review L3 | `ComposeRenderTests.cs:120-126` | Delete the temporary env file in a `finally`. |
| 8.12 | security L1 | `OperationsOptions` binding tests | Pin that an old-shape clients file (`Operations:Actors`) binds `AllowedClients` empty (one more `InlineData`). |
| 8.12 | review | `OperationsEndpoints.cs:75,93,106,120` | Route summaries still say "a mapped token sub"; reword to "a token sub in `Operations:AllowedClients`" (no openapi change). |
| 8.12 | verify L1 (tooling) | builder `breaks` format | Break patches had no `@@` hunk headers, so `scripts/run-breaks.sh` refused them; the build-story procedure should require patches made with `git diff` (a check in `run-breaks.sh` or the status hook). |
| 8.12 | verify L2 (process) | M1 lane collect | No `8.12-build.json` reached the verifier (the lane predated the flow change); check that `lane.sh collect` brings it on the next M1 build. |
| 8.12 | verify (tooling) | `scripts/verify-with-copilot.sh` | Its prompt still tells the cross-model check to run the suites; under the faster flow it reads the results file instead. |
| 8.8 | verify finding 2 | `ComposeRenderTests` worker `!override` | The worker's base and override environments share the same six keys, so dropping its tag is unobservable; drop the redundant tag or note it. |
| 8.8 | verify finding 4 | `DeployScriptTests.cs:395-404` | `DerivedPath_IsCheckedAgainstItsBound` calls `within_bounds` directly (`deploy.sh` sets the paths itself; comment explains). |
| 8.8 | verify finding 5 (tooling) | `scripts/run-breaks.sh` | No path for the `infra/tests` project: a break filter that matches no test passes; make a zero-match filter fail and add the infra project. |
| 8.12 | QA (wording) | `e2e/tests/operations-actions.spec.ts:232` | Test title still says "maps to no actor"; reword to "is not in AllowedClients". |
| 8.9 | security L1 | local gateway `/otlp/v1/(traces|metrics|logs)` | Still passes a client's `X-Origin-Verify` to the local OTLP receiver (no effect: no local secret, AWS `/otlp/` is 404). Following the 2026-11-04 decision (strip on every local route), add `proxy_set_header X-Origin-Verify "";` there and the header to `LocalOtlpLocation_UpstreamRequest_*`. |
| 8.9 | review L2 | story 8.9 file | Criterion 3 ("with no test changed"), Boundaries ("any header value") and Pre-build ("Decisions: none raised") predate the 2026-11-04 decision; add a dated note citing `epic-8-decisions.md`. |
| 8.9 | verify L2 (process) | M1 lane collect | No `8.9-build.json` or results file reached the verifier (lane predated the flow change); same as the 8.12 row. |
| 8.8 | verify r2 L1 | `8.8-build.json` break 13 | The builder's break 13 is order-sensitive (fails only when the deploy test runs first); the verifier's replacement (`GRANT pg_monitor TO dbmaster` before the `MasterShape` read) fails in either order. |
