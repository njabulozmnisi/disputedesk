# Epic 8 lows (fixed in one batch at the end of the epic)

| Story | Source | File | Finding |
|---|---|---|---|
| 8.1 | review | `infra/aws/check-compose.sh:2` | The SC2016 disable covers the whole file; all 9 current hits are jq variables. Move to per-line directives if a real hit ever hides. |
| 8.2 | review, security L1 | `backend/tests/DisputeDesk.Worker.IntegrationTests/Outbox/OutboxRelayDependencyTests.cs:34` | The `null` row starts the full Worker host with no override; with `ConnectionStrings__DisputeDesk` set, the relay and SLA sweep would run against that database. Remove the `SlaSweep` registration there, as `WorkerHostTests` does. |
| 8.2 | review | `backend/src/DisputeDesk.Application/Disputes/CustomerView.cs:68`, `Operations/OperationsView.cs:57` | Leftover `// AD-4: exactly two decimals` comments with nothing under them; delete. |
| 8.2 | review | `backend/src/DisputeDesk.Infrastructure/ConnectionStringsOptions.cs:13`, `Worker/WorkerHost.cs:23` | The `DisputeDesk` key is spelled three ways; build the validation message from the constants. |
| 8.2 | review | `backend/src/DisputeDesk.Api/Linking/LinkingEndpoints.cs:16` | `AddLinkingEndpoints` forwards one call (prescribed by criterion 6); retrospective note. |
| 8.3 | review | `e2e/tests/linking-screens.spec.ts:553` | Stale comment "SessionGate sends a linked customer away"; name the `_linked` layout route's `beforeLoad` (QA owns e2e). |
