# Simplicity and standards audit (2026-11-03)

A whole-solution audit against AGENTS.md "How to work": stupid simple and logical code; use established standards and do not invent mechanisms; every design explainable to a new engineer in one or two sentences; every abstraction earns its place; over-engineering is the standing risk. Until now these rules were checked one story diff at a time (review-story, simplicity pass and standards check). This audit checks drift across stories and inside the design decisions themselves.

Six read-only auditors split the solution by file set; this file merges them. The supervisor checked every high finding against the code.

| Area | Report | High | Medium | Low | Accepted as justified |
|---|---|---|---|---|---|
| Design (AD rules, decision records, PRD, production path) | [design.md](design.md) | 1 | 10 | 6 | section 2 of the report |
| Api host, Contracts, Worker, stubs | [api-host.md](api-host.md) | 0 | 2 | 4 | about 28 |
| Domain, Application, Infrastructure | [core-layers.md](core-layers.md) | 0 | 1 | 8 | 25 |
| Frontend | [frontend.md](frontend.md) | 1 | 2 | 8 | 29 |
| Infra, Compose, CI, scripts | [infra-ci-scripts.md](infra-ci-scripts.md) | 1 | 8 | 5 | about 25 |
| Tests (backend, infra, e2e) | [tests.md](tests.md) | 2 | 5 | 4 | section 2 of the report |
| **Total** | | **5** | **28** | **35** | |

## Verdict

The production code is by the book. No auditor found a home-grown mechanism in production code where the framework already provides one. Every complex production mechanism checked was traced to a requirement and a documented source. That covers the BFF and its server-side ticket store, antiforgery, the Idempotency-Key parser, the outbox relay, linking, the rate limiter, the ownership filter, Result types and `IAppDbContext`.

The simplicity rule slipped in three places:

1. **Decisions that forced workarounds.** Several requirements, each reasonable when it was settled, made the code bend around the framework:
   - the 404-before-400 check order;
   - the session-ended screen that must show offline;
   - an actor map with a single allowed value;
   - an internal event type that exists to exercise the pattern.

   The code then implemented these faithfully, so the per-story reviews passed. The complexity sits in the decision, not the diff.
2. **The test layer outside the standard test stack.** This covers hand-written bash harnesses, regex parsing of C# and HCL, hand-written metric and log capture, and home-made clocks. The backend's xUnit, WebApplicationFactory, Testcontainers and Playwright suites are standard. The 2:1 test-to-production ratio reflects thorough coverage (about 2.3 backend tests per criterion), not over-engineering.
3. **Documents that grew by amendment.** AD-7, 8, 12, 13 and 30 carry 65 dated amendments, so several rules fail the two-sentence test as written, even where the mechanism itself is simple.

## High findings (all checked against the code)

| ID | Finding | Simplest standard fix | Tag |
|---|---|---|---|
| DES-4 | FR-24's order 401, 403, 404, 400, 409 puts 404 before 400 for no security reason: 403 before 404 already stops ID probing, which is the reason FR-24 gives (`prd.md:442`). That one step forces `DisableValidation`, a hand-written body check after the lookup, a second order for raise, and the `AcceptsMetadata([])` workaround on resolve. That workaround depends on ASP.NET Core routing source code, not documentation. | Use the framework's order everywhere: 401, 403, 400 (binding and validation), 404, 409. | [decision] |
| FE-1 | Clearing customer data in place takes five modules (`sessionEnd.ts` alone is 147 lines), late-response guards in seven success handlers, a `WeakSet` of ending clients, and a test that forces garbage collection. Story 7.14 needed seven fix briefs and six verifier runs, and P74 still lists dispute IDs left in the router's cache. The driver is `EXPERIENCE.md`: the session-ended screen "shows without a request, so it also shows offline". | Navigate with `location.replace` to the session-ended page. A full navigation drops all memory and needs no clean-up code. **Supervisor's check:** without a service worker, that navigation needs the network, so this fix only works if the offline requirement is relaxed. That decision is the human's. | [decision] |
| INF-1 | Five hand-written bash test harnesses each copy their own section/fail/ok helpers (19 copies) and their own PATH stubs. Infra now has three test styles: bash, xUnit under `infra/tests/`, and Terraform tests. | Move the checks into the ad-011 `infra/tests` xUnit project, or use bats-core, one style. | [code] |
| TST-1 | Metrics are captured by six hand-written `MeterListener` rigs and logs by two hand-written `ILoggerProvider`s. | `MetricCollector<T>` and `FakeLogger` from `Microsoft.Extensions.Diagnostics.Testing` (Microsoft Learn). A new package, so it needs dependency verification first. | [code] |
| TST-2 | The architecture tests check C# source with regexes, with about 94 extra cases that exist only to test those regexes. A real bug has already come from this approach. | Roslyn syntax trees, or `Microsoft.CodeAnalysis.BannedApiAnalyzers` for banned APIs. A new package, so it needs verification. | [code] |

## Decisions for the human

Each of these changes a PRD requirement, an AD or a decision record, so none is acted on without a decision. The order is by how much complexity each one would remove.

1. **DES-4: the check order.** Move to the framework's order. This removes DES-6's routing-source dependency on resolve and the second order for raise.
2. **FE-1 / DES-8: the session-ended screen offline.** Either keep the offline screen and its in-place clean-up, or relax it and end the session with a full navigation. That would delete most of the session-end code, the cross-tab deadline that duplicates the server's idle limit, and P74.
3. **DES-3: `SlaWarningRaised`.** Drop the internal event type. The deny-by-default allowlist stays proven by the test-only unknown event type. Six ADs carry its cost.
4. **DES-5 / API-2: `Operations:Actors`.** Replace the map, whose only allowed value is `operations`, with a list of allowed subs. AD-14a's second meaning of `ActorType` for unmapped callers goes with it.
5. **DES-13 / API-1: one enum rule.** Case-insensitive on the raise, exact camelCase on the resolve `outcome` (story 5.2 AC3). Pick one and delete one converter.
6. **DES-2: event sourcing.** It is built and follows the documented pattern. No requirement asked for it before the 2026-10-29 decision, the decision-time challenge answered RETHINK, and Microsoft's pattern page advises against it for small domains. The audit's recommendation is to keep it and record that trade-off plainly in ad-009.
7. **INF-6: seven Terraform modules, each called once**, including a one-resource budget module (AD-28). HashiCorp's module guidance advises against modules used once.
8. **DES-9: AWS's own Data Protection key ring.** The Postgres key ring would work unchanged on RDS.
9. **DES-10: the AWS environment.** It carries a lot of hardening but has never been applied, and R38 and the done-when text still promise a deploy. Align the text with reality.
10. **DES-11: ad-011's team-folder rule.** It has no requirement behind it and needed an exception on its first day.
11. **INF-7 / INF-8 and TST-7:** regex HCL parsing, the hand-written JWT-decoding CI jobs, and the whitespace-exact `PlatformScriptTests` (ad-011 moved those tests unchanged).
12. **CORE-7 / DES-14:** `GetAccountsAsync` and `Account` have no production caller (AD-21).

## Code-only fixes (no decision needed)

- **Medium:**
  - **CORE-1:** the outbox relay loops forever with a green heartbeat when the connection string is missing. Add `ValidateOnStart` for the connection string, as the Kafka settings already have.
  - **FE-2:** `SessionGate` is repeated in every route and races the session-end navigation. Use TanStack Router's `beforeLoad` and `redirect` on a pathless layout route.
  - **FE-3:** `WithdrawDialog` skips React Hook Form and zod, which AD-31 requires. It was flagged three times in 3.12 and never dispositioned.
  - **INF-2:** CI runs neither actionlint nor shellcheck, though every infra story requires them.
  - **INF-3:** both nginx templates repeat a 6-line header block in every location. Set it once per server block, using nginx's documented inheritance.
  - **INF-4:** an awk CSS parser for `tokens.css`, plus 11 sed-mutation tests in `ci.yml`.
  - **INF-9:** the gateway headers are tested in three places.
  - **TST-3:** three home-made clocks, although `FakeTimeProvider` is already pinned.
  - **TST-4:** hand-written event streams, one of them Submitted to Resolved, which the domain refuses.
  - **TST-5:** about 11 e2e specs keep their own sign-in copy, and several keep their own database-query copies.
  - **TST-6:** 13 `waitForTimeout` calls, plus a 3-second sleep before every test in `dispute-list.spec.ts`.
- **Low:** copies past the third case (CORE-2 to CORE-4, FE-4); single-caller helpers (CORE-5, CORE-6); test-only paths and redundant null guards (CORE-8, CORE-9); naming and misplaced comments (API-3 to API-6); unused exports and a stale `frontend.md` line about Orval mocks (FE-5 to FE-11); and the rest in each report.
- **Docs (DES-1, DES-17):** consolidate the dated amendments into the current rule in AD-7, 8, 12, 13 and 30, delete the stale divergences section and history text, and split `production-path.md` into deferred scope and accepted security risks.

## Corrections the audit found in existing records

- `docs/verification/flaky-fixes.md` suggests fixing `RateLimitWindowTests` with `FakeTimeProvider`. The built-in rate limiter takes no `TimeProvider` (Microsoft Learn), so that fix cannot work.
- A comment in the core-banking client says the resilience handler turns `HttpClient.Timeout` off. Microsoft Learn does not say so. A test pins the 5-second deadline either way.
- The design auditor could not confirm "RFC 10017". It was confirmed on rfc-editor.org on 2026-11-03: *OAuth 2.0 for Browser-Based Applications*, BCP 212, August 2026.

## Why the per-story reviews did not catch these

The reviewer reads one story's diff against its criteria. A criterion that forces a workaround (DES-4, FE-1) produces a diff that is faithful to the criterion, so the simplicity pass accepts it. The pre-build challenger and reviewer read the story, not the decision behind it. Test and infra tooling crossed many stories, so no single diff showed the duplication. A proportionate countermeasure, for the epic retrospective process: run this audit's design lens (explainability, by the book, proportion, necessity) on each AD amendment before stories are written from it. This is the decision-time challenge, extended to amendments.
