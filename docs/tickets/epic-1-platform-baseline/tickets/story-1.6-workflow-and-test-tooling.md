---
id: 6
type: story
title: "Workflow and test tooling"
parent: epic-1-platform-baseline
covers: [R35, R37]
after: [5]
hitl: false
risk: low
---

# Workflow and test tooling

## Description

Removes the waste found in the 2026-10-27 workflow review: one authoritative test run per commit, a fast deliberate-break check, a verifier pre-flight, timeouts tested without real waits, and the gateway following a rebuilt api.

## Requirements

Verify: Each script runs as described with its documented exit codes; the four timeout tests finish in under a second each; the full suite passes.

1. **One authoritative check run** (R35; NFR-TEST-3)
   **Given** `scripts/check.sh <story>` in a worktree
   **When** it runs
   **Then** it builds once with warnings as errors, runs the full suite under the `testcontainers` lock, the OpenAPI and EF drift checks and `scripts/floor-guard.sh`, and writes a one-page summary keyed by the diff hash that later stages read instead of re-running

2. **Deliberate-break check** (R35; NFR-TEST-3)
   **Given** `scripts/break-check.sh <worktree> <patch> <test-method>`
   **When** it runs
   **Then** it applies the patch to a copy-on-write clone (`cp -c`), builds only the affected test project, runs `--filter-method`, reverts the patch, and exits 0 when the test fails as wanted, 1 when it still passes, 3 when the break does not compile

3. **Verifier pre-flight** (R35)
   **Given** `scripts/verify-with-copilot.sh`
   **When** the active `gh` account lacks the verifier models
   **Then** it exits 2 ("blocked: tooling") before taking any lock, and it holds the `testcontainers` lock only while tests run

4. **Timeouts without real waits** (R35; NFR-TEST-2)
   **Given** the core-banking and SIM-swap timeout tests
   **When** they run
   **Then** the resilience pipeline is driven by `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`, verified and pinned), each finishes in under a second, and each still fails if the 5-second deadline is removed

5. **Gateway follows a rebuilt api** (R37)
   **Given** `docker compose up -d --build api`
   **When** the api container is recreated
   **Then** the gateway restarts with it through `depends_on` `restart: true` (Compose specification), so no manual gateway restart is needed

6. **Redundant tests merged or removed** (R35; NFR-TEST-3)
   **Given** the six cases from the 2026-10-27 redundancy review
   **When** each is changed
   **Then** `UnknownRoute_Get_Returns404ProblemDetailsWithTraceId` and `TryHandleAsync_UnhandledException_CarriesTheInternalSlug` have their one unique assertion merged into `FrameworkResponse_EachStatus_CarriesItsSlug` and `TryHandleAsync_UnhandledException_ReturnsClean500` and are removed; `Antiforgery_Anonymous_Returns401Unauthorized` and `Logout_SignedInWithTokenFromAntiforgeryEndpoint_Passes` are removed; the security-headers 200 row and the `limit=-1` row are dropped
   **And** before each removal, `scripts/break-check.sh` shows the covering test fails for the same break, and the result is recorded in `docs/verification/1.6-review.md`

7. **Over-specified assertions loosened** (R35)
   **Given** assertions that compare exact raw JSON or message wording (money object order and text, session state bodies, the unmasked warning text, the unhandled-exception log template)
   **When** they are rewritten
   **Then** each asserts the specific value (`GetProperty(...).GetString()`, a stable message fragment), and each still fails for the break it proves

8. **Shared fixtures for read-only test classes** (R35; NFR-TEST-3)
   **Given** `TransactionsListTests` (21 cases, all read-only GETs as customer A) and the `ToProblem_*` cases in `ProblemDetailsTests`
   **When** they run
   **Then** each class shares one host, database and sign-in through `IClassFixture` (xUnit shared context), and no test depends on another's state

9. **Two more real waits removed** (R35; NFR-TEST-2)
   **Given** the caller-cancel tests in `CoreBankingResilienceTests` and `SimSwapCheckTests`
   **When** they run
   **Then** they use `FakeTimeProvider` like criterion 4 and finish without the 500 ms waits

## Supervisor checks

- Run `scripts/break-check.sh` with a known break from story 2.7 and see exit 0; with a no-op patch see exit 1.
- Rebuild the api and call `/api/v1/bff/session` through the gateway with no manual restart.

## Build facts

- Owner: backend (backend-builder).
- Flags: none.
- Lane: A or B (no product code).
- Touches: scripts/check.sh (new); scripts/break-check.sh (new); scripts/verify-with-copilot.sh; .claude/skills/verify-story/SKILL.md (reuse the check summary); backend/Directory.Packages.props; backend/tests/DisputeDesk.Api.IntegrationTests/ (CoreBanking/, Transactions/TransactionsListTests.cs, ProblemDetailsTests.cs, Bff/AntiforgeryTests.cs, Bff/SessionStateTests.cs, SecurityHeadersTests.cs, TelemetryTests.cs); docker-compose.yml (gateway depends_on).

## References

- parent — docs/tickets/epic-1-platform-baseline/epic-1-platform-baseline.md

## Notes

- Decision: 2026-10-27 — from the workflow efficiency review: the review stages are cheap; the waste is repeated full runs, long-lived contexts and hand-made break scripts. Tests are not deleted by count.
- `.claude/skills/verify-story/SKILL.md` and `scripts/verify-with-copilot.sh` are outside `backend/`; the supervisor makes those two edits, the builder the rest.
- Decision: 2026-10-27 — 2.2 criterion 5's "the token makes the request pass" is proved by `SignOutTests.Logout_SignedIn_DeletesTheRowClearsBothCookiesAndRedirectsToEndSession` once `Logout_SignedInWithTokenFromAntiforgeryEndpoint_Passes` is removed; `docs/verification/1.6-review.md` records the remap.
