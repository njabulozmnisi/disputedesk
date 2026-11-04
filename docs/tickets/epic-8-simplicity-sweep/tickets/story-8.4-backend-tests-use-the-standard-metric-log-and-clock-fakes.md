---
id: 4
type: story
title: "Backend tests use the standard metric, log and clock fakes"
parent: epic-8-simplicity-sweep
covers: [R35]
after: []
hitl: false
risk: low
refined: true
---

# Backend tests use the standard metric, log and clock fakes

## Description

Slice S3 of the mitigation plan (wave 2), test code only. The backend tests rebuild framework test tools by hand: hand-written `MeterListener` recorders where Microsoft Learn names `MetricCollector<T>`, two `ILoggerProvider` captures where `FakeLogger` exists (TST-1), three home-made `TimeProvider` subclasses although `FakeTimeProvider` is already pinned (TST-3). Tests write stored events by hand, some in streams the aggregate refuses (TST-4). Six poll loops (TST-9), a probe whose reason is out of date (TST-10), two copies of one seeding helper and five of one body helper (TST-11), and two sleeps after the work that weaken absence checks (TST-8) complete the set. Every requirement the tests prove stays proved; only the mechanism changes.

## Requirements

Verify: the full backend suite passes with the same number of tests as before (no test is removed; renamed tests are listed in the report); `dotnet build` with warnings as errors and `dotnet format --verify-no-changes` pass; no production file changes.

Inputs: none (no endpoint, configuration or production change).

1. **Named instruments are read with `MetricCollector<T>`** (TST-1; R35)
   **Given** the hand-written `MeterListener` recorders in `Api.IntegrationTests/Infrastructure/MeasurementRecorder.cs`, `Linking/VerifyApi.cs` (`ListenToLinkingEvents`, used by `VerifyOtpAuditTests` and `VerifyOtpRaceTests`), `Linking/LinkingEvents.cs`, `Linking/RateLimits/RateLimitRejectionTests.cs`, `Linking/StartLinkingUniformTests.cs`, `Disputes/RaiseMetricsTests.cs`, `Disputes/Withdraw/WithdrawMetricsTests.cs` and `Worker.IntegrationTests/Sla/SweepRig.cs`
   **When** this story lands
   **Then** each test reads its instrument with `new MetricCollector<T>(meterFactory, "DisputeDesk", "<instrument>")` on the app's own `IMeterFactory` (so measurements from tests running alongside are not seen), waiting with `WaitForMeasurementsAsync` where it waited before, and asserts the same values and tags; histogram metadata is read from the collector's `Instrument`
   **And** `StoredEventPrivacyTests`' `LabelRecorder` (`:194-210`) stays as it is, the one `MeterListener` left in a test project: it must see every instrument of one Api's meter factory, including instruments added later, which a `MetricCollector<T>` (one named instrument) and the OpenTelemetry in-memory exporter (process-wide, so other tests' measurements land in it) cannot both do

2. **Logs are read with `FakeLogger`** (TST-1; R35)
   **Given** `CoreBanking/LogCapture.cs` (registered as an `ILoggerProvider`, for example at `Seeding/SeededDisputesTests.cs:276`) and `SweepRig.LineCapture`
   **When** this story lands
   **Then** both are replaced by `FakeLogCollector` (with the category where `LineCapture` kept it), registered through the `Microsoft.Extensions.Diagnostics.Testing` logging extensions, and no hand-written `ILoggerProvider` remains in a test project; the log assertions are unchanged

3. **One fixed start instant, and `FakeTimeProvider` where no host timer runs** (TST-3; R35)
   **Given** `Infrastructure/TestClock.cs` (83 uses), `CoreBanking/FixedTimeProvider.cs`, `Seeding/SeededDisputesTests.cs:333` (`CountingClock`) and the start instant `…AddTicks(1_234_567)` written out in `TestClock.cs:8`, `TransactionsStub.cs` and `OperationsToolFixture.cs:25`
   **When** this story lands
   **Then** the start comes from one `TestInstants.NotMicrosecondAligned()` (keeping the 2.8 reason: a clock that is not microsecond-aligned exposes Npgsql's truncation) and one `AsStored(DateTimeOffset)` helper, used by all three places
   **And** a clock registered as a host's `TimeProvider` (`ApiFactory.cs:106-108`, `CoreBankingStubFactory.cs:50`) stays a `TestClock`: those hosts' timers must run in real time (Polly's timeouts and delays, `DatabaseMigrator`'s retry delay at `:37`, `ExpiredSessionCleanup`'s `PeriodicTimer` at `:28`), and `FakeTimeProvider` fires timers only on `Advance` or `SetUtcNow`; `TestClock` gains a constructor taking its start, says why in its summary, and replaces `FixedTimeProvider`, which is deleted
   **And** a test whose clock reaches no running timer uses `new FakeTimeProvider(start)` moved with `Advance` or `SetUtcNow`; the report lists which uses moved; `CountingClock` stays (it counts reads of a host's clock)

4. **Tests reach a status through the aggregate** (TST-4; R35, AD-14, ad-005)
   **Given** `Disputes/DisputesApi.cs:343` (`AppendAsync`, 19 uses), `Withdraw/WithdrawDisputeTests.cs` (`StatusChange`), `Withdraw/WithdrawDisputeInputTests.cs:133` and `Operations/OperationsToolFixture.cs:31-58` (`DisputeInAsync`)
   **When** this story lands
   **Then** one `DisputeIn(status, …)` helper raises through `Dispute.Raise` and calls `StartReview`, `Escalate`, `Resolve` or `Withdraw` as `SweepRig.NewDispute` does, then saves once through the one commit path; it takes the customer and transaction, the target status (Submitted, UnderReview, Escalated, Resolved or Withdrawn), the outcome for Resolved (default upheld) and the instant of each step (default the test clock's reading), with the operations actor for review, escalation and resolution and the customer for withdrawal, as the aggregate requires; when the aggregate refuses a step it throws naming the status and the error, never writing a partial stream
   **And** every caller that only needs a dispute in a status uses it, so no test writes a stream the aggregate refuses (the Submitted-to-Resolved streams go); `AddRowsAsync` stays only for rows the aggregate cannot write or a test must control directly, each saying so in its name or a one-line comment: `Ownership/NotFoundResponsesTests.cs:75-83` (the every-event-type stream) and any concurrent-append or chosen-actor case, which the report lists

5. **One poll helper per test project** (TST-9; R35)
   **Given** the six poll loops (`HealthTests.cs:101-115` `WaitUntilReadyAsync`, used by `Seeding/SeededApi.cs:35`; `Linking/LinkingApi.cs:107-126`; `Linking/LinkingDatabase.cs:30-42,48-70`; `Infrastructure/TelemetryCapture.cs:48-63`; `Worker.IntegrationTests/Outbox/OutboxFixture.cs:282-298`)
   **When** this story lands
   **Then** each project has one `Eventually.UntilAsync(Func<Task<bool>> condition, TimeSpan timeout)` in its `Infrastructure/` folder, used by all of them with each caller's current timeout, and `WaitUntilReadyAsync` moves out of `HealthTests` into that folder
   **And** the Worker project's helper also takes an optional `FakeTimeProvider` that it advances one second on each attempt, as `OutboxFixture.WaitUntilAsync` does today

6. **No probe or comment describes a state that no longer exists** (TST-10; R35)
   **Given** `Infrastructure/ForbidProbe.cs:9` ("No endpoint in the Api forbids a caller yet"), `DisputesApi.cs:340-341` ("Only the raise exists yet") and `OutboxFixture.cs:168` ("no outbox relay before story 4.1")
   **When** this story lands
   **Then** the forbid ProblemDetails test runs against a real 403 (an unlinked login on a linked-only route, as `GetDisputeAuthorizationTests.cs:38-51` does) and `ForbidProbe` is deleted; the two comments are corrected or removed

7. **One seeding helper and one body helper** (TST-11; R35)
   **Given** `DisputesApi.cs` `SeedDisputeAsync` and `RaiseDirectlyAsync`, and the five copies of "the JSON body without `traceId`" (`Linking/VerifyApi.cs:104-111`, `Operations/OperationsApi.cs:130-136`, inline in `ListDisputesPagingTests.cs:47`, `VerifyOtpInputTests.cs:101`, `StartLinkingQueueFullTests.cs:128`)
   **When** this story lands
   **Then** one seeding helper takes the customer, transaction and instant as arguments, and one `BodyWithoutTraceIdAsync` in `Infrastructure/Browser.cs` serves all five

8. **Absence is observed while the first call is held, and confirmed after it ends** (TST-8; R35)
   **Given** `Linking/StartLinkingUniformTests.cs:108` and `Linking/StartLinkingQueueFullTests.cs:65`, which sleep 500 ms after the work has finished and then assert that core banking saw no extra call
   **When** this story lands
   **Then** each holds the first core-banking call with `LinkingFakes.CoreBankingFake.HoldNext()` (`LinkingFakes.cs:69-99`), waits until that call has arrived, then keeps a bounded 500 ms observation window open while it is held (linking jobs run in parallel, `LinkingJobService.cs:23`, so a second job would reach core banking inside it) and asserts the count has not grown; then it releases the hold, waits for the first job's final audit entry, and asserts the count is still the expected one (1, or 5 in the queue-full test), so a call that only starts after the release is also caught
   **And** the window is the one wait left in an absence check, written as a named constant with a one-line comment saying why it is a bounded observation and not a signal

## Boundaries

- Must not change: any production file; what any test asserts, apart from the mechanism; the tests' requirement coverage (each story's criterion-to-test map still holds, with renamed tests listed in the report).
- `TelemetryCapture`'s OpenTelemetry in-memory path stays: it proves the export itself.

## Supervisor checks

- Throwaway copy: make `RaiseMetrics` record the wrong reason label; the `MetricCollector` test fails.
- Throwaway copy: let `StartLinkingHandler` call core banking twice at once; criterion 8's test fails inside the window.
- Throwaway copy: let the first linking job call core banking a second time after the first call returns; criterion 8's test fails after the release.
- Throwaway copy: make `DisputeIn(Resolved)` skip `StartReview`; the domain refuses and the helper throws naming the status.
- Throwaway copy: swap `TestClock` for `FakeTimeProvider` in `ApiFactory`; a core-banking timeout test hangs to its limit, which shows why criterion 3 keeps it.

## Build facts

- Owner: backend (backend-builder).
- Tier: Small, test-only.
- Flags: none.
- Lane: A or B (test projects only).
- Mechanisms and sources:
  - Testing metrics with `MetricCollector<T>`: <https://learn.microsoft.com/aspnet/core/metrics/overview> (Test metrics in ASP.NET Core apps); <https://learn.microsoft.com/dotnet/core/diagnostics/metrics-instrumentation> (Test custom metrics).
  - `MeterListener` scoped to one meter factory (the one recorder kept): <https://learn.microsoft.com/dotnet/core/diagnostics/metrics-collection>.
  - `FakeLogger` and `FakeLogCollector`: <https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.testing.fakelogger>.
  - `FakeTimeProvider` and its timers: <https://learn.microsoft.com/dotnet/api/microsoft.extensions.time.testing.faketimeprovider>.
- Packages: `Microsoft.Extensions.Diagnostics.Testing` 10.11.0 (publisher Microsoft, nuget.org; latest stable on 2026-11-04 and the same 10.11.0 line as the pinned `Microsoft.Extensions.TimeProvider.Testing` and `Microsoft.Extensions.Http.Resilience`). The builder re-checks publisher, downloads and repository per AGENTS.md, Dependency verification, and pins it exactly in `backend/Directory.Packages.props`, referenced by the two integration test projects only.
- Touches: `backend/Directory.Packages.props`, the two integration test `.csproj` files, and the test files named in the criteria.
- Collision: 8.2 changes production files and their tests' expectations in different files; 8.5 bans hand-written `ILoggerProvider`s in test projects once this story has removed them.

## Pre-build

- Challenger: `docs/verification/8.4-challenge.md` (simplify).
- Review: `docs/verification/epic-8-prebuild-wave2.md`, section 8.4. No security read.
- Decisions: none raised.
- Sofia, 2026-11-04: every `[story]` point folded. Review: host clocks keep real timers and only timer-free clocks become `FakeTimeProvider` (criterion 3, Supervisor checks); a bounded window while held plus a check after release, with the parallel jobs named (criterion 8); `LogCapture`'s registration named and `NotFoundResponsesTests` under `Ownership/` (criteria 2, 4). Challenger: the privacy test keeps its factory-scoped listener, so it sees new instruments and only its own Api (criterion 1); the helper's inputs, actors and refusal stated, with the cases that keep `AddRowsAsync` (criterion 4); the Worker's poll helper keeps advancing its clock (criterion 5); no test removed (Verify); a check after the release (criterion 8).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- audit — docs/verification/simplicity-audit/tests.md, TST-1, TST-3, TST-4, TST-8 to TST-11
- plan — docs/verification/simplicity-audit/mitigation-plan.md, wave 2, S3; wave 4, item 1
- skill — .claude/skills/build-story/SKILL.md, step 3 (`FakeTimeProvider` in tests)

## Notes

- Risk low (proposed): test-only; a wrong replacement shows as a failing or vacuous test, which the supervisor checks target.
- Decision: 2026-11-04 (Sofia) — `StoredEventPrivacyTests` keeps its `LabelRecorder`, replacing the earlier plan to move it to the OpenTelemetry in-memory exporter: the exporter is process-wide, so it could not attribute labels to this Api, and the recorder already listens only to this Api's meter factory and sees every instrument. As a result 8.5 does not ban `MeterListener` in test projects.
- Decision: 2026-11-04 (Sofia) — TST-3 keeps `TestClock` for the clocks a host's timers run on, because `FakeTimeProvider` would freeze Polly's timeouts, the migrator's retry delay and the session clean-up timer; advancing it by hand in every Api test would be the larger change. `FixedTimeProvider` folds into it and the start instant is written once.
- Decision: 2026-11-04 (Sofia) — TST-8 keeps a bounded 500 ms window, placed while the first call is held and followed by a check after it ends: no deterministic signal says "no second job will start", and the hold makes the window meaningful where the old sleep after the work was not.
- Decision: 2026-11-04 (Sofia) — TST-10's e2e parts (stale spec headers and the resolved-409 withdraw case) are in 8.6, which QA owns.
