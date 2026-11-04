# Epic 8 pre-build review: stories 8.1 to 8.5 (2026-11-04)

Base `fed7f85`. The reviewer is read-only; the supervisor recorded its findings here. Every point is `[story]`; none contradicts a PRD requirement or an AD. 8.3 was reviewed only in part, and 8.6 to 8.9 not at all (turn limit); a second review run covers them (`epic-8-prebuild-wave2b.md`).

## 8.1, verdict changes
1. High. Criterion 2's pathspec `'infra/**/*.sh'` misses `infra/smoke.sh`; use `':(glob)infra/**/*.sh'`.
2. Medium. Criterion 8: the runner also takes the agent (`${LANE_AGENT:-backend-builder}`, `lane.sh:32`) as a second argument; `wait` reads `subtype` and `terminal_reason` (`lane.sh:45`), only `collect` reads `result` (`:54`).
3. Medium. Criterion 5: `gh run watch --exit-status` cannot tell a failed run (exit 1) from a `gh` failure (exit 2); read `gh run view <id> --json conclusion` after the watch, and restate what `DISPUTEDESK_WATCH_TIMEOUT` bounds.
4. Medium. Criterion 3: once the 11 mutations (`ci.yml:172-197`) go, add the missing-token and not-`#rrggbb` rejections as tests in `infra/tests/DisputeDesk.PlatformTests`; say what the lookup does with a token declared twice and with one inside a comment (`tokens.css:1-8`).
5. Low. Criterion 4 drops the check that `tokens.css` names both wordmarks and loads neither (`ci.yml:212-216`); keep the grep or record the drop.
6. Low. shellcheck v0.11.0 publishes no checksum file; use GitHub's asset digest (`sha256:8c3be12b05d5c177a04c29e3c78ce89ac86f1595681cab149b65b97c4e227198` for `shellcheck-v0.11.0.linux.x86_64.tar.xz`). actionlint publishes `actionlint_1.7.12_checksums.txt`. Both are the latest stable.
7. Low. Criterion 7: the human-decision prose match is `check-ready.sh:45-48` (40-43 is the `[story]` rule, which stays). The `decisions: settled` field needs a documented home and writer (`docs/process/agentic-sdlc.md:83`, the product owner's ticket template): a process change for the human.
8. Nit. shellcheck does not lint zsh; say `scripts/lane-runner.zsh` is not linted.

## 8.2, verdict changes
1. High. Criterion 1's `ValidateOnStart` breaks `WorkerHostTests.WorkerHost_StartThenStop_CompletesCleanly` (`WorkerHostTests.cs:17`) and the `WorkerTelemetryTests` hosts at `:42` (and likely `:65`), which start the Worker with no connection string; give each an unreachable one (`Port=1`, as `OutboxRelayDependencyTests.cs:46`), and say what the relay reads once `?? ""` goes.
2. Medium. Criterion 2: `RaiseDisputeHandler.cs:122` cuts a transaction date, not a clock reading; expose the plain cut too (for example `ToWholeMicroseconds(DateTimeOffset)`).
3. Medium. Criterion 5: `Error.Extensions` is `private init` (`Error.cs:17`); name the Domain factory (for example `Error.ActionNotAllowed(code, message, extensions)`, shaped like `Error.Conflict` at `:37`); `Error.BusinessRule(..., Enum)` at `:43` is in scope or exempt.
4. Low. Criterion 6: Touches miss `Api/Authorization/LinkingRoutes.cs` (`:17`, `:33`) and `Api/Linking/RateLimiting/LinkingRateLimits.cs:32`; the "one Add and one Map" reason is inaccurate; say whether the authorisation group folds into `MapLinking`.
5. Low. Criterion 8: delete `CoreBankingClientTests.GetAccountsAsync_...` (`:83`), which proves only the removed member; say what `FixtureTests.cs:23` checks instead.

## 8.3, partial
1. Low. `sessionEnd.ts:101-112`, `queryClient.ts:54-56` and `useGoToTransactions.ts:10` explain their order by `SessionGate`; correct them here or leave them to 8.13 on purpose.
2. Low, not checked. `src/test/support.tsx:287-320` (`holdSessionEndedRoute`) patches `beforeLoad` on the shared route tree; name it in Touches or say it stays.

## 8.4, verdict changes
1. High. Criterion 3: `FakeTimeProvider` fires timers only on `Advance` or `SetUtcNow`, and the test clock is the app's `TimeProvider` (`ApiFactory.cs:106-108`, `CoreBankingStubFactory.cs:50`), which drives Polly's timeouts and delays (inferred from its binary), `DatabaseMigrator`'s retry delay (`:37`) and `ExpiredSessionCleanup`'s `PeriodicTimer` (`:28`). Say which tests keep a real-timer clock, or how each timer is advanced.
2. Medium. Criterion 8: linking jobs run in parallel (`LinkingJobService.cs:23`); name a deterministic signal for "no second call while held" or accept a bounded wait.
3. Low. Criterion 2: `SeededDisputesTests.cs:276` registers `LogCapture` as an `ILoggerProvider`; name it. `NotFoundResponsesTests` is under `Ownership/`.

## 8.5, verdict changes
1. High. Criterion 7 must ban `AddPooledDbContextFactory` as well as `AddDbContextPool`, with every generic overload, as the deleted `ProductionSource_AddDbContextPool_IsAbsent` (`OwnershipRulesTests.cs:95-103`) does.
2. Low. Say whether `PlatformScriptTests.ApiProgram_ForwardedHeaders_RunFirstInThePipeline` (regex over `Program.cs`) is in scope.
3. Low. Criterion 4: "an identifier starting with" `AuditLogEntr` or `StoredEvent` (today's prefix match, `AuditLogRulesTests.cs:98`).

Checked: SDK 10.0.403 ships Roslyn 5.9.0-1, so BannedApiAnalyzers 5.6.0 and Microsoft.CodeAnalysis.CSharp 5.9.0 load; `Microsoft.Extensions.Diagnostics.Testing` 10.11.0 is the latest.
