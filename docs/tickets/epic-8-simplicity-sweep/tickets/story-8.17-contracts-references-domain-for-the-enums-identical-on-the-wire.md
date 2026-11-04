---
id: 17
type: story
title: "Contracts references Domain for the enums that are identical on the wire"
parent: epic-8-simplicity-sweep
covers: [R25, R30]
after: [11]
hitl: false
risk: medium
refined: true
---

# Contracts references Domain for the enums that are identical on the wire

## Description

Slice S17 of the mitigation plan (wave 3), carrying the human's decision D14b. AD-1 keeps Contracts free of any reference, so nine enums exist twice, once in Domain and once in Contracts, with identical members, and Application maps each pair by hand in both directions; because the names clash, the repo bans `using` aliases and writes fully qualified names (DES-16, last bullet; `docs/conventions/backend.md:14`). This story lets Contracts reference Domain for those nine enums: each exists once, in Domain, the Contracts copies and their mappings go, and the wire stays byte-identical. The customer-facing enums whose members differ from Domain's stay separate in Contracts, with their one mapping.

Human decision, 2026-11-03 (D14b): change AD-1: Contracts references Domain for the internal enums that are identical on the wire; the customer enums stay separate. The openapi diff must show no change, because the wire names stay identical. AD-1's graph, `LayerTests` and the using-alias rule in `conventions/backend.md` change with it.

## Requirements

Verify: the full backend suite passes; the OpenAPI drift check shows no change to `backend/openapi.json`; the event schema contract tests pass with `contracts/events/*.schema.json` unchanged; the frontend's Orval drift check shows no change.

Inputs: none (no request, response or event changes).

1. **Contracts references Domain, and only Domain** (D14b; R25, AD-1)
   **Given** `DisputeDesk.Contracts.csproj`, which references no solution project
   **When** this story lands
   **Then** it references `DisputeDesk.Domain`; Domain still references no solution project; `LayerTests.ContractsAssembly_References_NoOtherSolutionAssembly` (`backend/tests/DisputeDesk.ArchitectureTests/LayerTests.cs:35-40`) is renamed and asserts that Contracts references Domain and no other solution assembly; every other layer rule is unchanged

2. **The nine identical enums exist once, in Domain** (D14b; R25, R30)
   **Given** the pairs with identical members: `Outcome`, `DisputeAction`, `Reason`, `ActorType`, `DisputeStatus` (`Contracts/Disputes/` and `Domain/Disputes/`) and `TransactionKind`, `NotDisputableReason`, `TransactionDirection`, `TransactionState` (`Contracts/Transactions/` and `Domain/Transactions/`)
   **When** this story lands
   **Then** the nine Contracts files are deleted and every request, response, event and operations record in Contracts uses the Domain type; the members, their order and their names are unchanged

3. **The enums with no identical Domain twin stay in Contracts** (D14b; R18, AD-14a)
   **Given** the two customer-view enums, `CustomerStatus` and `TimelineActor`, whose members differ from the Domain enums they are mapped from, and the Contracts-only enums `StoredEventType`, `AuditAction`, `AuditResult` and `SessionState`, which have no Domain twin to share
   **When** this story lands
   **Then** all six stay in Contracts; `DisputeWire.ToCustomerStatus` (`Application/Disputes/DisputeWire.cs:78`) and `ToTimelineActor` (`:89`) stay as the one mapping to each customer-view enum, and the four Contracts-only enums keep whatever they have today

4. **The hand-written pair mappings go** (D14b; R34)
   **Given** `DisputeWire.ToDomain` (`DisputeWire.cs:11`, `:22`), `DisputeWire.ToWire` (`:29`, `:41`, `:51`, `:62`, `:70`) and the transaction `ToWire` mappings (`Application/Transactions/ListTransactions.cs:85`, `:92`, `:101`, `:108`)
   **When** this story lands
   **Then** each mapping between two identical enums is deleted and its callers pass the value through; the comment "which Contracts cannot reference (AD-1)" (`Contracts/Disputes/RaiseDisputeRequest.cs:32`) is corrected to point at `Dispute.NoteMaxLength`
   **And** the typed error variants that 8.2 (criterion 5) builds in Application, taking `Contracts.Disputes.DisputeStatus` in place of today's `System.Enum` parameter (`Domain/Disputes/DisputeErrors.cs:45`), take the Domain `DisputeStatus`; 8.17 runs after 8.2 (through 8.10 and 8.11), so the builder reads 8.2's merged result before editing them

5. **The wire is byte-identical** (D14b; R23, R30, NFR-API-2)
   **Given** the regenerated `openapi.json`, the outbox CloudEvent payloads, the stored events' data and the stored raise responses
   **When** the drift check and the contract tests run
   **Then** `openapi.json` has no diff (schema names, enum values and descriptions included: where a Contracts enum's XML summary appeared in the document, including `ActorType`, `DisputeAction` and `Reason`, whose Contracts summaries differ from Domain's today, the Domain enum carries the same summary text exactly), and `frontend/src/api/generated/` has no diff; if the Domain enums' XML summaries do not reach the document, the builder adds what the OpenAPI XML comment support needs (a direct `ProjectReference` from the Api to Domain, or the Domain XML file as `AdditionalFiles`), and the empty diff proves it
   **And** a new test, written first and passing on the code before the change, serialises fixed inputs for each persisted and published shape (one outbox CloudEvent payload per published event type, the stored event data of each stored event type, one stored raise response) and compares each with an exact expected string; it passes unchanged after the change, so the wire is proved byte-identical, beside the publish mapping and stored-event round-trip tests, which also pass unchanged

6. **Names are written without qualification where nothing clashes** (D14b; R34)
   **Given** the fully qualified references such as `Contracts.Disputes.ActorType.Customer` that the shared names forced (`docs/conventions/backend.md:14`)
   **When** this story lands
   **Then** each reference to one of the nine enums names the Domain type through its namespace's `using`, unqualified; a qualified name stays only where a Contracts-only type and a Domain type still share a name; `UsingAliasRulesTests` stays and passes (no alias is introduced)

## Boundaries

- Must not change: any wire value, schema, route or event; the customer enums; Domain's dependency-free rule; the using-alias ban itself.
- No enum is moved into Contracts or into a shared new project.

## Supervisor checks

- Throwaway copy: rename the Domain `Reason.WrongAmount` member to `IncorrectAmount`; the OpenAPI drift check fails (proving the wire now reads the Domain type).
- Throwaway copy: add a reference from Contracts to Application; criterion 1's layer test fails.
- Throwaway copy: change one Domain enum's XML summary; the OpenAPI drift check fails (criterion 5).

## Build facts

- Owner: backend (backend-builder).
- Tier: Feature; the openapi diff must be empty.
- Flags: contract.
- Lane: A, after 8.11 (which changes the enum converter on the same types).
- Before the build (supervisor, docs-only change recording D14b): AD-1's reference graph and rule (`docs/architecture/architecture.md`, AD-1, and AD-14a's ownership sentence "Contracts owns … the wire enums") state that Contracts references Domain for the enums identical on the wire; `docs/conventions/backend.md:14` states why the using-alias ban stays and that qualified names are needed only where names still clash. The builder does not edit them.
- Mechanisms and sources:
  - Project references: <https://learn.microsoft.com/visualstudio/msbuild/common-msbuild-project-items#projectreference> (MSBuild `ProjectReference`).
  - Architecture layer rules through assembly metadata (as `LayerTests` does): <https://learn.microsoft.com/dotnet/api/system.reflection.assembly.getreferencedassemblies>.
  - Enum serialisation by the shared converter (8.11): <https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/converters-how-to>.
- Packages: none.
- Touches: `backend/src/DisputeDesk.Contracts/DisputeDesk.Contracts.csproj`, the nine deleted Contracts enum files, the Contracts records that use them, `Application/Disputes/DisputeWire.cs`, `Application/Transactions/ListTransactions.cs`, the Application error variants from 8.2, the callers of the deleted mappings, the Domain enums' XML summaries (to match the rendered text), `backend/tests/DisputeDesk.ArchitectureTests/LayerTests.cs`, criterion 5's exact-string test (new), and the tests that name the Contracts copies; if needed, `DisputeDesk.Api.csproj` (criterion 5).

## Pre-build

- Challenger: `docs/verification/8.17-challenge.md` (simplify).
- Review and security: `docs/verification/epic-8-prebuild-wave3.md`, section Review 8.17 (proceed; security raised nothing for 8.17).
- Decisions: none raised.
- Sofia, 2026-11-04: every `[story]` point folded. Review: the XML summaries' route into `openapi.json` (criterion 5, Touches); the comment pointing at `Dispute.NoteMaxLength` (criterion 4). Challenger: the Domain summaries carry the rendered text exactly (criterion 5, Supervisor checks); byte identity proved by a fixed-input exact-string test written first (criterion 5); 8.2's typed variants are 8.2's to build, read after it merges (criterion 4); the docs-first update gates the build (Notes); the two customer-view enums told apart from the four Contracts-only enums (criterion 3).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- plan — docs/verification/simplicity-audit/mitigation-plan.md, section 1 (D14, D14b), wave 3, S17
- audit — docs/verification/simplicity-audit/design.md, DES-16 (last bullet); core-layers.md, CORE-9
- architecture — docs/architecture/architecture.md, AD-1, AD-14a
- conventions — docs/conventions/backend.md

## Notes

- Risk medium (proposed): many files change, but the wire must not; criterion 5's empty diffs prove it.
- Human decision, 2026-11-03 (D14b), as quoted in the Description; it replaces D14's second answer (leave AD-1's separate enum graph as is). Not reopened here.
- Build gate: the docs-first change (AD-1, AD-14a, `conventions/backend.md:14`) lands before the builder starts; on 2026-11-04 `architecture.md` still says Contracts references nothing and owns the wire enums.
