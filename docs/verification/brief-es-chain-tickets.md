# brief-es chain: tickets

Sources: `brief-es-decisions.md` (decisions 1 to 15), `brief-es-rewording-rules.md`, `docs/glossary.md`, the architecture spine, `ad-009-stored-events.md`, the PRD, the initiative and `docs/conventions/backend.md`. Scope: `docs/tickets/**` only. Nothing staged or committed.

## Stories written

- **2.8** Sign-in and sign-out are audited, and every endpoint is denied by default (`epic-2-transactions`, id 8, after 2.2 and 2.3, lane M1, high-stakes, no migration). `Audit.Record` with existing fields only, `SignIn`/`SignOut`, sign-out through the OIDC handler's `SignOutAsync`, `FallbackPolicy` with an endpoint-enumeration test, the security and error-shape tests in Development and Production. The whole-call deadline is named as 2.1's `TotalRequestTimeout`.
- **3.8** Dispute changes are recorded as stored events, and disputes load by replay (`epic-3-disputes`, id 8, after 3.2, before 3.3, lane M2, high-stakes, migration). 15 criteria: six types, replay, loading, commit with no audit row, rollback, failed save, exhaustive publish mapping, insert-only guard and source-rules test, unique `(DisputeId, Version)` with no concurrency token on `Disputes`, ownership filter, replay test, deny-by-default customer timeline with `openapi.json` unchanged, shared JSON options, no logging of stored events, migrations in final shape with the EF drift check. Request inputs: none new. A `## Code to change` section is marked `<!-- builder-only: the supervisor removes this section at merge -->`; it includes the local-volume reset (`docker compose down -v`, then `docker compose up --wait`, `infra/README.md`).

## Stories amended

- **3.1** (done): retitled "…commits with its stored event and outbox row" and renamed to `story-3.1-linked-customer-raises-a-dispute-that-commits-with-its-store.md`; row counts, rollback, data model and filter criteria in stored-event terms. Status and decision lines kept.
- **3.2** (done): the AD-14 rule reads "stored event and outbox row".
- **3.3**: after 3.8; reads on 3.8's mapping; criterion 6, deny by default over every type plus a test-only record; criterion 7, timestamps with `Z` and exact enum reading; Boundaries: operations see every type (5.1).
- **3.4**: after 3.3 and 2.8; load by replay; criterion 6, version-clash reload (AD-15); criteria 9 and 10, raise and withdraw refusals `Rejected` through `Audit.Record` and the "only `Audit.Record`" source-rules test; status-changed schema now from 3.8.
- **3.7**: criterion 7, skip link.
- **4.1**: CloudEvent `id` = outbox ID; criterion 8, tests own their rows; lane M1.
- **5.1**: after 3.8 and 2.8; every stored event type on the operations timeline; `DisputeRead` audited.
- **5.2**: load by replay, AD-15 retry (start review vs escalate both succeed), `Denied` from the authorization result handler, `Rejected` from handlers, no audit row on success.
- **5.3**: `SlaWarningRaised` once per dispute (criterion 5), load by replay, no duplicate stored event; now high-stakes with a migration (AD-18's column); `unknown` set: the lead time gates the start.
- **5.4**: back-dated instants, full commit path with outbox rows, `Customer`/`worker:seeder` actors, replay reproduces rows.
- **6.1**: after 2.8; entries through `Audit.Record`.
- **7.1**: `StoredEvents` row, no raise audit row, `SignIn`/`SignOut` rows.
- **7.4**: Data Protection decision line reworded.
- **7.6**: ad-009 listed; production path with the decision 8 context, ad-009 deferred items and the EF-only idempotency claim.
- **7.10**: after the last builder stories; criterion 6 glossary and suppressions; criterion 7 named items.
- Epic files 2, 3, 4, 5, 6, 7 and `docs/tickets/tickets.toml`: Requirements lines taken from the initiative (R10, R16, R18, R20 to R22, R24, R28, R29, R31) with their "This epic delivers" parts; R10, R35, R41 added to epic 2, R30, R31, R41 to epic 3, R29, R41 to epic 5; descriptions, Done when, needs and Notes updated. The epic 7 table runs 7.10, 7.1, 7.2, 7.3, 7.6, 7.7, 7.4, 7.5 (decision 6). Lanes M1 and M2 set for 2.6, 2.8, 3.3, 3.4, 3.8, 4.1, 5.1 to 5.4 and 6.1 to 6.3.
- `README.md`: rows for 2.8 and 3.8, 3.1 renamed, epic 7 reordered, the intro sentence names 3.8 before 3.3 and 7.10 before 7.1.

## Stories removed

7.9, 7.8a, 7.8b and 1.6: files deleted, entries taken out of the epic 1 and epic 7 `tickets.toml` files and the README, and every mention removed. `next_id` is pinned at 7 (epic 1) and 11 (epic 7) so the ids are never reused.

## Follow-up placements

| Follow-up | Placed in |
|---|---|
| Shared JSON options, one instance (3.2 review R2) | 3.8 criterion 13 |
| Response timestamps `Z`, exact enum reading (3.1, 3.1 QA) | 3.3 criterion 7 and Notes |
| Constant problem `title` per type (2.7 review finding 3) | 3.4 Notes input |
| Lost-race `fail:` log lines (3.2 QA finding 1) | 3.4 Notes input |
| `isApiResponse` rename (2.5 review N2) | 2.8 Notes input |
| `<h1>` focus and skip link; `satisfies never` (2.4 QA and review) | 3.7 criterion 7 and Notes |
| `smoke.sh` 429 loop and `pipefail` (2.5 review L2, N1) | 7.10 named item |
| Data Protection key-race flake (2.4) | 7.10 named item |
| Session-long `stack` lock (tooling debt) | 7.10 named item |
| EF-only idempotency claim | 7.6 criterion 2 (production path) |
| Glossary and suppression sweep | 7.10 criterion 6 |
| Deny by default, OIDC sign-out, Production-environment tests | 2.8 |
| `AllowedHosts` | 6.3 (unchanged) |

Dropped with 1.6 and given no home: `check.sh`, `break-check.sh`, the verifier pre-flight, `FakeTimeProvider` timeout tests, the gateway's `restart: true`, and the redundant-test and assertion clean-ups.

## Touched files: 2.8 and 3.8 (disjoint)

**2.8:** `Application/Audit/Audit.cs` (new); `Contracts/Audit/AuditAction.cs`; `Api/Bff/BffAuthentication.cs`; `Api/Bff/BffEndpoints.cs`; `Api/Health/HealthEndpoints.cs`; `Api/Program.cs`; tests `Bff/SignInTests.cs`, `Bff/SignOutTests.cs`, `Infrastructure/FakeZitadel.cs`, `Infrastructure/ApiFactory.cs` (only if needed), a new `Authorization/` enumeration test, `SecurityHeadersTests.cs`, `ProblemDetailsTests.cs`, `Transactions/TransactionsAuthorizationTests.cs`, `Disputes/RaiseDisputeAuthorizationTests.cs`.

**3.8:** `Domain/Disputes/` (`Dispute.cs`, stored event records, `Outcome`, deletes `DisputeChange.cs`, `TimelineEntry.cs`); `Application/Disputes/CustomerView.cs`, `DisputeWire.cs`, the loading method, `RaiseDispute/RaiseDisputeHandler.cs`; `Contracts/Events/`; `Contracts/Json/DisputeDeskJson.cs`; `Infrastructure/Persistence/` (`AppDbContext.cs`, `CommitRows.cs`/the mapping, `DisputeConfiguration.cs`, a `StoredEvents` configuration, deletes `TimelineEntryConfiguration.cs`, `UniqueViolations.cs`, `Migrations/20261028084134_AddDisputes.*`, `Migrations/20261028150231_AddIdempotencyKeys.Designer.cs`, `AppDbContextModelSnapshot.cs`); `contracts/events/…status-changed.v1…`; tests `Domain.Tests/Disputes/`, `Api.IntegrationTests/Disputes/` (every file except `RaiseDisputeAuthorizationTests.cs`), `Infrastructure/FailOutboxInsertInterceptor.cs` (if needed), `ArchitectureTests/AuditLogRulesTests.cs`.

No shared source, test or migration file. Neither changes `openapi.json`. One semantic link, not a file overlap: 2.8's `RaiseDisputeAuthorizationTests.cs` calls `DisputesApi.cs` helpers that 3.8 edits; 3.8's Boundaries keep their signatures.

## Open questions

1. AD-14a and the spine's Open questions give raise-refusal auditing to 3.8 [ASSUMPTION]. The human placed it in 3.4, and the stories say so. The architecture owner should update AD-14a.
2. 2.8: with the fallback policy, an anonymous request to an unknown route gets 401, not 404 (Microsoft Learn: the fallback applies to requests with no endpoint). The proposal is to keep 404 for signed-in callers and run the 2.2 unknown-route test as a signed-in login. A human decides before the build.
3. 3.8 assumptions: the `status-changed.v1` record, schema and sample, and the `Apply` cases for all five lifecycle types, are written in 3.8, because the exhaustive mapping and the replay test need them. 3.4 no longer creates the schema.
4. 5.3: the SLA warning lead time is not decided, so 5.3 is not ready (`unknown`). 5.4 is built first and assumes open seeded disputes are at most 2 days old, which holds for any lead time under 8 days.
5. 5.4: the `worker:seeder` actor value is still an architecture assumption and needs confirming.
6. 3.3: whether response timestamps should round to Postgres's 6 digits; the convention says System.Text.Json's precision. 3.3 also keeps the open question of whether the customer `timeline` item hides or maps `action` for an escalation (R18).
7. Epic 6 (existing): which story adds the `LoginSub`, `ClientIp` and `Details` audit columns. 5.2's `Details.outcome` needs `Details` before epic 6.
8. The skill's Set check expects one closing refactor-sweep story. The human removed it, and epic 7's Notes record that 7.10 checks glossary terms and suppressions instead.

## Validation

- `tickets.py status docs`: 37 tickets; no `problems`, `drift`, `unpinned_after`, `undeclared_after` or `order_conflict`.
- A script check of each epic's `covers` against its Requirements lines, and each story's `covers` against its epic, reports only hits that were there before: 3.7 R17, 5.2 R18, 7.1 R23, 7.4 R2, 7.10 R9/R11/R30/R31.
- Remaining hits for `grep -rnE 'TimelineEntr|DisputeEvents|backfill|xmin|7\.9|7\.8a|7\.8b|1\.6|superseded|no longer' docs/tickets` (12):
  - 3.8's builder-only section (lines 148 to 157): 6 hits.
  - `TimelineEntryResponse`: 3.3 (2 hits) and 3.8 criterion 12.
  - `pg_snapshot_xmin`: 7.6.
  - 3.8's touches list: `DisputeEvents.cs`, the existing Contracts file of the published dispute event types.
  - 6.1:50, "the earlier challenge is superseded": the linking-challenge term (`SupersededAtUtc`, AD-15), not design history.

```json
{"status": "built", "assumptions": ["status-changed.v1 record, schema and sample, and the five lifecycle Apply cases, are written in 3.8", "SignIn is recorded in OnTicketReceived; its CustomerId is set only for a linked login", "the 'only Audit.Record writes audit entries' source-rules test is in 3.4, the first story after both 2.8 and 3.8", "open seeded disputes are at most 2 days old until the SLA warning lead time is decided", "follow-ups placed as in the table above"], "open_questions": ["AD-14a names 3.8 for raise-refusal audit; stories place it in 3.4 (architecture update)", "anonymous unknown route: 401 under the fallback policy, or keep 404", "SLA warning lead time (gates 5.3)", "worker:seeder actor value", "timestamp precision in responses", "customer timeline action for escalations (R18)", "which story adds the LoginSub, ClientIp and Details audit columns"], "conflicts": ["the skill's Set check expects a closing refactor sweep; the human removed it (7.10 covers the glossary and suppression check)"]}
```
