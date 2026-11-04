---
id: 15
type: story
title: "The seeder is the third named ownership-filter bypass"
parent: epic-8-simplicity-sweep
covers: [R17, R24, R25]
after: [5, 14]
hitl: false
risk: medium
refined: true
---

# The seeder is the third named ownership-filter bypass

## Description

Slice S14 of the mitigation plan (wave 3), carrying the human's decision D6. AD-11 allows the ownership filter's bypass, `IgnoreQueryFilters(["Ownership"])`, in exactly two places, `Operations/` and the Worker's sweep. To avoid a third, the seeder builds a second `AppDbContext` per customer over the migrating context's connection and transaction, with that customer's `CurrentCustomer`, and reads each fixture dispute through the filter (DES-7). The seeder is start-up code behind `Seed:Enabled`, not a request path. This story makes it the third named bypass: it reads the fixture disputes once on the migrating context with the named bypass and an explicit customer predicate, the per-customer context goes, and the architecture rule names the seeder (`Seeding/Seeder.cs`) as the third allowed place. What is seeded does not change.

Human decision, 2026-11-03 (D6): yes, make the seeder AD-11's third named bypass (`IgnoreQueryFilters(["Ownership"])`), checked by the same architecture test, instead of one `AppDbContext` per customer. AD-11 and AD-30 change; the per-customer context and AD-30's two-instance seeding analysis go.

## Requirements

Verify: the full backend suite passes, the seeding tests unchanged; the architecture tests pass with the rule's third folder; on a fresh stack, `docker compose up --wait` seeds customers A to D as before and the e2e `seeded-disputes.spec.ts` passes.

Inputs: none for requests. `Seed:Enabled` and the fixture file `backend/fixtures/local.json` are unchanged.

1. **The seeder reads fixture disputes through the named bypass** (D6, DES-7; R17, R24, AD-11)
   **Given** `Infrastructure/Seeding/Seeder.cs` (`SeedAsync` from `:31`, the per-customer loop at `:56-71`; the synchronous `Seed` at `:94-118`)
   **When** it checks whether a fixture dispute already exists
   **Then** it queries the migrating context with `IgnoreQueryFilters(["Ownership"])`, matching the dispute's customer ID as well as its `accountRef` and `transactionId` (`SeedDispute.Matches()`, `SeedDispute.cs:51-56`), so the bypass never widens the match; it raises only the disputes it does not find and saves once per stream, as today: on the migrating context's connection, in the transaction EF Core holds there when no migration was pending, each `SaveChanges` its own savepoint, so a dispute's stream is still all or nothing (the wording of `Seeder.cs:160-165`)
   **And** a new test, for both `SeedAsync` and `Seed`, puts a dispute owned by another customer on a fixture dispute's `accountRef` and `transactionId` and expects seeding to fail on the unique index rather than skip the fixture dispute silently; without the customer predicate the bypass would find the other customer's row and skip

2. **No second context per customer** (D6, DES-7; R26)
   **Given** `ContextFor` (`Seeder.cs:160-176`, which builds `new AppDbContext(options, currentCustomer)` over the migrating connection and joins its transaction)
   **When** this story lands
   **Then** `ContextFor` and every use of it are deleted, in `SeedAsync` and in `Seed`; the seeder constructs no `AppDbContext` and no `CurrentCustomer`

3. **The architecture rule names three places** (D6; R25, NFR-SEC-6)
   **Given** `ProductionSource_IgnoreQueryFilters_AppearsOnlyInOperationsAndSla` and its `BypassFolders` (`backend/tests/DisputeDesk.ArchitectureTests/OwnershipRulesTests.cs:18-21,28-35`, as 8.5 rewrote them on syntax trees)
   **When** it runs
   **Then** the allowed places are exactly the folders `DisputeDesk.Application/Operations/` and `DisputeDesk.Worker/Sla/` and the one file `DisputeDesk.Infrastructure/Seeding/Seeder.cs`; the test is renamed for the three places and fails on a bypass anywhere else, another file in `Seeding/` included; the parameterless-bypass ban is unchanged
   **And** the customer-feature namespace rule (`CustomerFeatureSource_OperationsOrSlaNamespace_IsNeverReferenced`) also refuses a reference to `DisputeDesk.Infrastructure.Seeding` from `Disputes/` or `Transactions/`, with a sample-line case for it

4. **Seeding behaves exactly as before** (D6; R24)
   **Given** `Seeding/SeededDisputesTests.cs` (`:44`, `:84`, `:103`, `:125`, `:148`, `:170` B's dispute gives A a 404, `:188`, `:213` seeds inside the migration transaction, `:226`, `:245`, `:272`), `SeedFixtureTests.cs` and `InvalidFixtureDisputesTests.cs`
   **When** they run
   **Then** they pass unchanged: the same disputes, stored events and read-model rows, seeding is idempotent across restarts, and a second start seeds nothing (`SeededLinksTests.Seeder_FreshStartThenRunAgain_LinksAAndBOnceAndNotCOrD`); criterion 1's new test is the only addition

## Boundaries

- Must not change: the fixture file, the seeded data, `Seed:Enabled`, the order of seeding inside the migration transaction, `UseAsyncSeeding`/`UseSeeding` registration (`SeedRegistration.cs:20-25`); the `Automation` actor rule (`SourceRulesTests.cs:34-39`, which already allows `Infrastructure/Seeding/`).
- No DI scope per customer: D6 chose the named bypass.

## Supervisor checks

- Throwaway copy: drop the customer predicate from criterion 1's query; criterion 1's new test sees the fixture dispute skipped instead of a failure, for both `SeedAsync` and `Seed`.
- Throwaway copy: add `IgnoreQueryFilters(["Ownership"])` in `Infrastructure/Linking/`, and separately in a new file in `Infrastructure/Seeding/`; criterion 3's test fails both times.

## Build facts

- Owner: backend (backend-builder).
- Tier: Feature (start-up code; the ownership rule is a security boundary, so the security agent reads the diff).
- Flags: security.
- Lane: A, after 8.14.
- Before the build (supervisor, docs-only change recording D6, aligning the requirements to it): AD-11's heading ("two named bypasses") and its bypass sentence ("appears only in the `Operations/` feature folder and the Worker's sweep") name the seeder as the third; AD-30's seeder bullet drops the per-customer context and the two-instance seeding analysis, citing EF Core's `UseAsyncSeeding` contract only, and its *Prevents* matches; PRD NFR-SEC-6 (`docs/prd/prd.md:584`), the initiative's R17 (`docs/initiative-disputedesk.md:47`) and `docs/conventions/backend.md:29` name the same three places. The builder does not edit them.
- Mechanisms and sources:
  - Disabling a named query filter: <https://learn.microsoft.com/ef/core/querying/filters>.
  - Seeding with `UseAsyncSeeding`: <https://learn.microsoft.com/ef/core/modeling/data-seeding>.
- Packages: none.
- Touches: `backend/src/DisputeDesk.Infrastructure/Seeding/Seeder.cs`, `backend/tests/DisputeDesk.ArchitectureTests/OwnershipRulesTests.cs`, a new test beside `Seeding/SeededDisputesTests.cs` (criterion 1).

## Pre-build

- Challenger: `docs/verification/8.15-challenge.md` (simplify).
- Review and security: `docs/verification/epic-8-prebuild-wave3.md`, sections Review 8.15 and Security read 8.15.
- Decisions: settled by the human, 2026-11-04 (`docs/verification/epic-8-decisions.md`: the NFR-SEC-6 and R17 point dismissed as a docs-first alignment).
- Sofia, 2026-11-04: every `[story]` point folded. Review: R17, NFR-SEC-6, `conventions/backend.md:29`, AD-11's heading and AD-30's *Prevents* (docs-first); the Supervisor check that could not fail replaced by a test that fails without the predicate (criterion 1, Supervisor checks); the transaction wording of `Seeder.cs:160-165` (criterion 1). Security: `Seeder.cs` alone allowed and `DisputeDesk.Infrastructure.Seeding` added to the customer-feature namespace rule (criterion 3). Challenger: the wrong-customer row test for both paths (criterion 1); the qualified transaction claim (criterion 1); `SeededLinksTests` named, one new test allowed (criterion 4); its `[decision]` point folded as the docs-first alignment above.

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- plan — docs/verification/simplicity-audit/mitigation-plan.md, section 1 (D6), wave 3, S14
- audit — docs/verification/simplicity-audit/design.md, DES-6, DES-7
- architecture — docs/architecture/architecture.md, AD-11, AD-30
- story — docs/tickets/epic-5-operations/tickets/story-5.4-seeded-disputes-for-customers-a-and-b-in-every-status.md

## Notes

- Risk medium (proposed): a bypass without the customer predicate would match another customer's dispute; criterion 1 and its Supervisor check guard it.
- Human decision, 2026-11-03 (D6), as quoted in the Description; not reopened here. The challenger's `[decision]` point (PRD NFR-SEC-6 and R17 still say only two places bypass) was settled by the human on 2026-11-04 as not a decision: D6 already decides the third bypass, and the docs-first change aligns NFR-SEC-6 and R17 (`docs/verification/epic-8-decisions.md`).
- Decision: 2026-11-04 (Sofia) — the bypass is allowed in `Seeder.cs` only, not the whole `Seeding/` folder, so no new seeding file can bypass without changing the rule.
