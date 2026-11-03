# Simplicity audit: mitigation plan (2026-11-03)

How to resolve the findings in [README.md](README.md) and the six area reports. Every finding is in exactly one place below: a wave, or "Kept as is" with its reason. Nothing here is acted on until the human answers section 1; waves 1 and 2 need no answers and can start at once.

Base: `main` at `1ba4379`. Since the audit ran at `9836438`, ad-011 changes 2 to 4 have landed, so `infra/tests/DisputeDesk.PlatformTests` now exists and is the target for INF-1 and TST-7.

## Principles

- **Simplify toward the framework, never away from a requirement.** A fix that changes what a user, an operator or the API contract sees is a decision (section 1). A fix that only changes how the code reaches the same behaviour is a code fix (waves 2 and 4).
- **Each slice runs the normal story flow at its tier** (AGENTS.md, Stakes tiers). Removing code is still a change: the tests that prove the criteria stay green, and only the tests that exist to test the removed mechanism go with it.
- **Smallest first.** Docs and dead code go first, test tooling next, and requirement changes last, so the risky slices land on a cleaner tree.
- **Every fix cites its source.** The source is the one named in the area report's "Simplest standard alternative" column.

## 1. Decisions for the human

### Decided by the human on 2026-11-03

| # | Answer | Effect on this plan |
|---|---|---|
| D1 | Yes: the framework's order on every route | S9 runs |
| D2 | Yes: end sessions by navigation; the session-ended screen may need the network | S10 runs, with the per-tab warning kept and the shared deadline moved to the production path |
| D3 | Yes: drop `SlaWarningRaised` | S11 runs (the one migration) |
| D4 | Yes: the `Operations:AllowedClients` allowlist | S12 runs |
| D5 | Yes: strict exact camelCase everywhere | S13 runs |
| D6 | Yes: the seeder is AD-11's third named bypass | S14 runs |
| D7 | **No: keep SSM in AWS** | S15 is dropped. AD-7 records why there are two key-ring mechanisms; DES-9 moves to "Kept as is" |
| D8 | **No: keep the seven modules** | S16 keeps the modules and applies D9 inside them. AD-28 records the reason: the modules are kept ready for a second environment. INF-6 moves to "Kept as is" |
| D9 | Yes: IAM policies as `templatefile()` JSON checked by jq, and the OIDC CI jobs replaced by a recorded one-time check | S16 runs |
| D10 | Yes: remove `GetAccountsAsync` and `Account` | In S1 |
| D11 | Yes: restate R38, the done-when and SM-1/SM-2; freeze the AWS-only mechanisms | In S0b |
| D12 | **Finish ad-011 as written** | Nothing left to build: changes 1 to 4 landed by `1ba4379`, and change 5 (the runbook move) landed in `8dc541d`. S0b only marks ad-011 complete and records the challenge's SIMPLIFY verdict beside the decision. No `CODEOWNERS` file |
| D13 | Yes: keep event sourcing and record the trade-off in ad-009 | In S0b |
| D14 | Yes: FR-8 and AD-12 state only documented limiter behaviour | In S0b |
| D14b | **Change AD-1: Contracts references Domain for the internal enums that are identical on the wire**; the customer enums stay separate | New slice S17 |
| Wave 4 | All four countermeasures approved: the review-story pitfall line, the design lens on AD amendments, the per-epic audit, and the build checks | Wave 4 runs; the skill lines are a rule change that the human has now approved |

### The questions as asked

The questions are numbered and each ends with the recommended answer, worded so that "yes" accepts it. They are ordered by how much complexity each removes.

| # | Finding | Question and recommended answer | What changes if yes | Slice |
|---|---|---|---|---|
| D1 | DES-4, DES-6 (AD-8) | Change FR-24 to the framework's order, 401, 403, 400, 404, 409, 422, on every route, raise included? **Yes.** 403 before 404 still stops ID probing, which is FR-24's stated reason. | FR-24, R20 and AD-8 change. `DisableValidation`, the hand-written body check after the lookup, raise's second order and the `AcceptsMetadata([])` workaround go. The 3.4 and 5.2 order tests are rewritten to the new order. | S9 |
| D2 | FE-1, DES-8, FE-6 | Relax `EXPERIENCE.md` so the session-ended screen needs the network, and end every session with `location.replace('/signed-out?reason=…')`? **Yes.** The server's idle limit stays the authority (OWASP Session Management). A per-tab warning dialog still meets WCAG 2.2 SC 2.2.1. | Most of `sessionEnd.ts`, `useSessionTabs.ts`, the `BroadcastChannel` protocol, the `WeakSet`, the late-response guards and the GC test go; P74 closes; the mutator's session special case (FE-6) goes. The cross-tab shared deadline moves to `production-path.md`. | S10 |
| D3 | DES-3, DES-16 (glossary) | Drop `SlaWarningRaised`, its sweep claim, its column and its partial index? **Yes.** The deny-by-default allowlist stays proven by the test-only unknown event type. | FR-26 and R22 are removed or reworded. One migration drops `SlaWarningRaisedAtUtc` and `IX_Disputes_SlaWarning`. The sweep and seeder lose the warning path. `disputeVersion` has no gaps. The glossary's "Timeline" becomes true again. | S11 |
| D4 | DES-5, API-2 | Replace `Operations:Actors` with a plain allowlist `Operations:AllowedClients` (an array of subs), where every allowed caller is actor type `operations`? **Yes.** | FR-25 consequence 3, AD-9, AD-14a, AD-26 and ad-006 change. The map, its validator and AD-14a's second meaning of `ActorType` go. A `Denied` entry records the token's `sub`. | S12 |
| D5 | DES-13, API-1 | One enum rule for the whole wire contract: exact camelCase names only, no numbers, no lists? **Yes.** Strict matches 5.2 AC3, and the code already has the converter. | AD-5 records the converter and its reason. `CamelCaseNameOnlyEnumConverter` becomes the shared converter through a factory and `SingleNameEnumConverter` goes. The raise route stops accepting other casings; that is a contract change, and the openapi diff shows it. | S13 |
| D6 | DES-7 | Make the seeder AD-11's third named bypass (`IgnoreQueryFilters(["Ownership"])`), checked by the same architecture test, instead of one `AppDbContext` per customer? **Yes.** | AD-11 and AD-30 change. The per-customer context and AD-30's two-instance seeding analysis (DES-6) go. | S14 |
| D7 | DES-9 | Use `PersistKeysToDbContext` in AWS too, with RDS encryption at rest, and move SSM or KMS-wrapped keys to the production path? **Yes.** | The `Amazon.AspNetCore.DataProtection.SSM` package, two configuration keys, the KMS key in Terraform and the environment branch in `BffAuthentication` go. AD-7 and AD-26 change; P68 gains a line. | S15 |
| D8 | INF-6 | Flatten the seven single-use Terraform modules into one `envs/dev` root with one file per concern? **Yes.** HashiCorp advises against thin wrapper modules. | AD-28 changes. The module tests become root tests. Because nothing has been applied, no `moved` blocks or state work are needed. | S16 |
| D9 | INF-7, INF-8 | Move each IAM policy into a JSON file rendered with `templatefile()` and checked by jq, and replace the two JWT-decoding CI jobs with a recorded one-time check in `infra/terraform/README.md`? **Yes.** | `iam-names.test.sh`'s regex HCL parsing goes, and two CI jobs go. The pinned `sub` test stays. | S16 |
| D10 | CORE-7, DES-14 | Remove `GetAccountsAsync`, `Account` and the stub's accounts route, and point the resilience tests at `GetTransactionsAsync`? **Yes.** | AD-21's port list shrinks. | S1 |
| D11 | DES-10, DES-15 | Restate R38 and the done-when as delivered ("passes `fmt`, `validate` and mocked `test`; applied when the account exists"), freeze AWS-only mechanisms at what is built, and state that SM-1 and SM-2 are measured locally only in v1? **Yes.** | Docs only: the initiative, PRD NFR-OBS-4 and §8, and AD-28. | S0 |
| D12 | DES-11 | Stop ad-011 where it now stands, with changes 1 to 4 landed and no further moves, and add a `CODEOWNERS` file so ownership is stated in one line per folder? **Yes.** Undoing changes 3 and 4 now costs more than it saves. | Change 5, if still open, is dropped or kept to paths only. The SPA named build context stays. ad-011 records that the decision-time challenge answered SIMPLIFY and why the move went ahead. | S0 |
| D13 | DES-2 | Keep event sourcing, and record the trade-off plainly in ad-009: no requirement forced it, the challenge answered RETHINK, Microsoft's "when not to use" line applies, and it was chosen to apply the pattern? **Yes.** Rewriting to a row plus an audit log is L effort for no behaviour change. | ad-009 gains a "Trade-off" paragraph. R29 is reworded as the design's rule, not a business requirement. | S0 |
| D14 | DES-6 (FR-8), DES-16 (AD-1) | Drop "two permits" from FR-8, keeping the test as a regression pin; leave AD-1's separate enum graph as is? **Yes, and yes.** | FR-8 and AD-12 cite only Microsoft Learn's documented limiter behaviour. | S0 |

## 2. Waves and slices

Slice IDs are working names. Sofia gives each one a story number in a new epic (proposed: epic 8, "Simplicity sweep"), after the challenger runs, as for any story. The size column is the area reports' effort estimate.

### Wave 1: docs (no decision needed, plus S0 once section 1 is answered)

| Slice | Findings | Change | Tier |
|---|---|---|---|
| S0a | DES-1, DES-12, DES-16 (all bullets except AD-1), DES-17, FE-11, the two corrections (README "Corrections") | Fold the dated amendments into each AD as Rule, Prevents and Rejected, and move the dated rationale to the decision records (Nygard: supersede, don't amend). Delete the "PRD and code divergences" section, the story-ownership sentences and the "This run adds…" note, and fix the frontmatter date. Drop NFR-DOC-3's closed list, add ad-010 and ad-011 to the map, and move ad-004 to `docs/process/`. Fix the four contradictions (`now()`, the shadcn Form line, expand-migrate-contract, the RFC 10017 citation, which is now confirmed). Split `production-path.md` into *Deferred scope* and *Accepted risks*, and move its mapping table to the 7.6 record. Correct `flaky-fixes.md` (the rate limiter takes no `TimeProvider`) and the core-banking timeout comment (cite the test, drop the unverified claim). | Docs-only commit (standing approval) |
| S0b | D11 to D14 | The text changes listed in section 1. | Docs-only commit that records the human's decisions |

S0a is a pure restatement: when it is done, every AD's rule fits in two to five sentences and no rule text changes meaning. The reviewer checks it against that test.

### Wave 2: code fixes that change no behaviour (no decision needed)

These slices run in parallel within the lane limits. None of them shares a file section with another, and none adds a migration.

| Slice | Findings | Change | Packages | Tier | Size |
|---|---|---|---|---|---|
| S1 backend tidy | CORE-1 to CORE-6, CORE-8, CORE-9, API-3 to API-6, D10 | `ValidateOnStart` for the relay's connection string (CORE-1 is the only one that matters in production: a missing string is a green heartbeat today). One clock helper, one `NotFound`, one `ToMoney`. Withdraw reads through the filter and `DisputeReplay` goes. Plain clash handling. Delete the unused `Result.Failure` factories and null guards. Wire-specific errors move to Application. One `MapLinking`. Metric label from the enum. Rename `SecurityHeaders`. Move the MailKit comment. Remove `GetAccountsAsync` if D10 is yes. | none | Small (each), one story | M |
| S2 frontend tidy | FE-2 to FE-5, FE-7 to FE-10 | `beforeLoad` and `redirect` on three pathless layout routes replace `<SessionGate>`. `WithdrawDialog` moves to React Hook Form plus zod (AD-31). `ErrorSummary` everywhere, and `z.config({ jitless: true })` once. `isProblem`/`validationFields` in place of status checks. Simpler `returnUrl` parsing (the BFF decides). `FieldSet`/`FieldLegend` for the radio group. One `partValue`. `isStartPage`. | none | Feature (FE-2 touches routing) | M |
| S3 standard test tools | TST-1, TST-3, TST-4, TST-9, TST-10, TST-11, TST-8 | `MetricCollector<T>` and `FakeLogger` replace the six `MeterListener` rigs and two `ILoggerProvider`s. `FakeTimeProvider` (already pinned, 10.11.0) replaces three home-made clocks. A `DisputeIn(status)` helper drives the aggregate instead of hand-written streams, which removes the Submitted-to-Resolved stream the domain refuses. One `Eventually.UntilAsync` per project. `ForbidProbe` goes. One seeding helper and one `BodyWithoutTraceIdAsync`. Absence checks wait on the linking fake's hold signal. | `Microsoft.Extensions.Diagnostics.Testing`: verify the publisher (Microsoft), pin it exactly, and keep its version in step with the other `Microsoft.Extensions.*` 10.x pins | Small, test-only | M |
| S4 architecture tests on a parser | TST-2 | The regex source rules become Roslyn syntax-tree checks (`Microsoft.CodeAnalysis.CSharp`) or, for "never call X", `Microsoft.CodeAnalysis.BannedApiAnalyzers` with a `BannedSymbols.txt`. The ~94 regex self-tests go; the rules and their intent stay. `LayerTests` is already standard. | One of the two, verified and pinned. Prefer BannedApiAnalyzers wherever a rule is "this API is banned", because it fails the build with no test code | Small, test-only | M |
| S5 e2e | TST-5, TST-6 | Every spec imports `signInContext`, `queryRows`, `origin` and `axeTags` from `support.ts`. `until` becomes `expect.poll`/`toPass`. The 13 `waitForTimeout` calls and the 3-second sleep become waits on a response, focus or event. | none | Small, test-only; QA owns `e2e/` | M |
| S6 one infra test style | INF-1, INF-9, INF-10, TST-7 | The five bash harnesses move into `infra/tests/DisputeDesk.PlatformTests` as xUnit tests, where they share one process helper and one PATH-stub helper. Edge headers are checked once, for both gateways. `smoke.sh` keeps only "the stack is up and wired". Compose is checked by parsing `docker compose config --format json`, not by matching whitespace. | none (the project exists) | Small, test-only | L |
| S7 CI and scripts | INF-2 to INF-5, INF-11 to INF-14 | `actionlint` (which runs shellcheck on `run:` blocks) and `shellcheck` on `infra/**/*.sh` and `scripts/*.sh` in CI. The nginx header block is set once per `server` (documented inheritance), and the gateway header tests from S6 prove the output is unchanged. `label-policy.sh` becomes a plain lookup plus the exact-values check, and the 11 sed mutations go. The upload's own refusal replaces INF-5's check. `gh run watch --exit-status` in `push-and-watch.sh`. One security command. An exact front-matter field for `check-ready.sh`. `lane.sh` reads JSON with jq. | actionlint and shellcheck as pinned CI actions or binaries, verified | Small; the nginx change is Feature, because it touches security headers, so security re-runs | M |

Order inside wave 2: S7's linters go first, because they will flag shell problems that S6 would otherwise carry into C#. S6 lands before S7's nginx change, so that change is proven by one header test. S2 is independent; S1, S3 and S4 share backend test projects but different files.

### Wave 3: decision-driven slices (after section 1)

Run these one at a time in this order, because several touch the same endpoints and only S11 has a migration.

| Slice | Decision | Tier | Why this position | Size |
|---|---|---|---|---|
| S9 check order | D1 | High-stakes (auth order): security agent plus a human diff review | It removes the most code and unblocks D5's converter work on the same endpoints | M |
| S13 enum rule | D5 | Feature; the openapi diff is reviewed as a contract change | Same request types as S9 | S |
| S12 operations allowlist | D4 | High-stakes (authorisation) | Independent of S9 in code, but the same AD-9 text | S |
| S10 session end by navigation | D2 | High-stakes (session): security plus a human diff review | Frontend only; it can run beside S12 | M |
| S11 drop the SLA warning | D3 | High-stakes (migration) | The only migration, so no other migration runs beside it | M |
| S14 seeder bypass | D6 | Feature | Start-up code only | S |
| ~~S15 one key ring~~ | D7: no | Dropped | | |
| S16 IAM policies as JSON | D9 (D8: modules kept) | Feature, infra; security re-runs, because it touches IAM | Independent of the backend slices | M |
| S17 Contracts references Domain | D14b | Feature. The openapi diff must show no change, because the wire names stay identical | After S13, which changes the enum converter on the same types. AD-1's graph, `LayerTests` and the using-alias rule in `conventions/backend.md` change with it | M |

### Wave 4: stop it coming back

The audit's lesson is that the per-story reviews check a diff against its criteria, so complexity forced by a criterion passes. Following AGENTS.md ("a repeated mistake becomes a check, not a sentence"), the countermeasures are, in order:

1. **Deterministic checks, which come with the slices above.** BannedApiAnalyzers bans `MeterListener` and hand-written `ILoggerProvider`s in test projects once S3 lands. ESLint's Playwright plugin (`eslint-plugin-playwright`, rule `no-wait-for-timeout`; package to be verified) runs in `e2e/` once S5 lands. actionlint and shellcheck come with S7. An architecture test asserts that infra has no `*.test.sh`, once S6 lands.
2. **One skill line each**, which is a process change and needs the human's approval:
   - in `review-story` "Known pitfalls": *"A criterion that forces code around the framework (a disabled validator, a routing workaround, a copy of framework behaviour) is a `[decision]` finding against the criterion, not an accepted diff."*
   - in the challenger's decision mode: run this audit's design lens (explainable, by the book, proportionate, necessary) on every AD amendment before stories are written from it.
3. **Re-run this audit once per epic**, in the retrospective, with the same six file sets, and compare the counts.

## 3. Kept as is

| Finding | Reason |
|---|---|
| DES-2's rewrite | D13 keeps the design and records the trade-off; the rewrite is L effort with no change in behaviour. |
| DES-10's existing AWS hardening | It is built and tested by `validate` and mocked `test`; D11 freezes it, and removing it saves nothing at run time. |
| DES-9, two key-ring mechanisms | D7: the human keeps SSM with KMS in AWS. AD-7 records the reason. |
| INF-6, seven single-use Terraform modules | D8: the human keeps them ready for a second environment. AD-28 records the reason. |
| The accepted complexity in each report's "justified" section (about 135 spots) | Traced to a requirement and a documented source; not touched. |

## 4. Size and cost

- **Wave 1:** two docs commits, with no build stages beyond the reviewer's check of S0a.
- **Wave 2:** seven slices, mostly Small-tier and test-only. That means about seven builder runs, of which S2 and the S7 nginx change take the full feature flow.
- **Wave 3:** eight slices, five of them high-stakes. Each takes the full flow, the security agent and a human diff review, as the stakes tiers require; under the 2026-10-30 waiver they are committed once CI is green and reviewed after.
- **Expected effect:** the largest removals are D2 (most of five frontend modules and seven fix rounds' worth of guards), D1 (two validation orders and a routing workaround), S6 (about 1,500 lines of bash harness) and S4 (about 94 regex self-tests). No user-visible behaviour changes except the four the human chooses: D1, D2, D3 and D5.
