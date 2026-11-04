---
id: 12
type: story
title: "Operations callers are a plain allowlist of client subs"
parent: epic-8-simplicity-sweep
covers: [R10, R21]
after: [11]
hitl: false
risk: high
refined: true
---

# Operations callers are a plain allowlist of client subs

## Description

Slice S12 of the mitigation plan (wave 3), carrying the human's decision D4. `Operations:Actors` is a map from a token's `sub` to an actor type, a start-up validator refuses every value but `"operations"`, and the handler reads only the keys, so the value column carries no information (API-2); AD-14a then has to give `ActorType` a second meaning for an unmapped `sub` (DES-5). This story replaces the map with a plain allowlist, `Operations:AllowedClients`, an array of subs: every allowed caller is actor type `operations` with its `sub` as actor ID, roles still decide what it may do, and a refused caller still gets 403 with a `Denied` audit entry that records the token's `sub`.

Human decision, 2026-11-03 (D4): yes, replace `Operations:Actors` with a plain allowlist `Operations:AllowedClients` (an array of subs), where every allowed caller is actor type `operations`. FR-25 consequence 3, AD-9, AD-14a, AD-26 and ad-006 change; the map, its validator and AD-14a's second meaning of `ActorType` go; a `Denied` entry records the token's `sub`.

## Requirements

Verify: the full backend suite and the infra test project pass; `infra/smoke.sh` passes on a fresh stack (the operations token section); the e2e regression suite passes after QA's change to `operations-read.spec.ts`.

Inputs:
- `Operations:AllowedClients` (configuration, read with the options pattern): an array of strings, each a Zitadel machine user's `sub`, compared with the token's `sub` by ordinal (exact) equality. Missing or empty: every bearer caller is refused with 403. No other key under `Operations` is read for actors.
  - Source: only the provisioner's clients file (one element, the machine user's ID). No `appsettings*.json` holds an `Operations` section, because the configuration binder merges arrays by index across providers, so a second source would add or overwrite entries silently.
  - Elements: an empty or whitespace element matches nothing, because the handler refuses a token with a null or empty `sub` before the lookup. No size bound is enforced: the list is operator configuration, not request input, and today holds one entry.
- Operations routes' bearer token (unchanged): JwtBearer validated for issuer, audience, lifetime and signature; `sub` and `role` claims as today.

1. **The options hold a list of subs** (D4, API-2; R21, AD-9)
   **Given** `Api/Operations/OperationsOptions.cs` (`Dictionary<string, string> Actors`, `:15`; `MapsOnlyToOperations`, `:18`) and its validation in `OperationsAuthentication.cs:38-41`
   **When** this story lands
   **Then** `OperationsOptions` has `string[] AllowedClients` (default empty) bound from `Operations:AllowedClients`; `Actors`, `MapsOnlyToOperations`, the `OperationsActorType` constant and the start-up rule "Every Operations:Actors value must be \"operations\"" are deleted

2. **An allowed caller is an operations actor** (D4; R21, FR-25, AD-14a)
   **Given** `OperationsActorHandler` (`Api/Operations/OperationsActorHandler.cs:21-27`)
   **When** a valid bearer token's `sub` is in `AllowedClients` and it holds the action's role
   **Then** the request proceeds with actor `new Actor(ActorType.Operations, sub)`, as today; when the `sub` is not in the list, the answer is 403 with today's body, whatever roles the token holds
   **And** the handler refuses a token whose `sub` is null or empty before it reads the list
   **And** two new tests prove the allowlist's edges: a token whose `sub` differs from a listed one only in case gets 403; with the list empty or missing, every caller gets 403, whatever its roles
   **And** the role tests (`Operations/OperationsRolesTests.cs`) pass apart from the start-up message test, which goes with the validator

3. **A refused caller is audited with its sub** (D4; R10, FR-25)
   **Given** `AuthorizationResultHandler.RecordDeniedAsync` (`Api/Authorization/AuthorizationResultHandler.cs:64-77`, which records `new Actor(ActorType.Operations, sub)` at `:73`)
   **When** a token whose `sub` is not in the list calls an operations route
   **Then** one `Denied` audit entry records actor type `Operations` and the token's `sub`, as today, and its tests pass unchanged; the code comment that explains the unmapped case says the entry records the route's actor type and the token's `sub`, with no second meaning of `ActorType`

4. **Provisioning writes the allowlist** (D4; R21, R37, AD-26)
   **Given** `infra/zitadel/provision/provision.sh` (the clients file write at `:237`, `Operations: { Actors: { ($opsUserId): "operations" } }`, and the "current" check at `:221`)
   **When** the provisioner runs on a fresh stack, and again on a stack whose clients file still has the `Actors` shape
   **Then** it writes `Operations: { AllowedClients: [$opsUserId] }`; its current check is exact array equality, `.Operations.AllowedClients == [$opsUserId]`, as today's check is exact on the map, so a file with the old shape, or with any other list, is not current and is rewritten
   **And** a rewrite regenerates both client secrets, as for any file that is not current (the provisioner's existing `else` branch); the provisioner runs before the Api starts, so the Api reads the new secrets, and the Supervisor Docker check proves sign-in and the operations token both work after the rewrite
   **And** `ProvisionScript_ClientsFile_MapsTheMachineUserIdToTheOperationsActor` and `ProvisionScript_CurrentCheck_AlsoRequiresTheActorMapping` (`infra/tests/DisputeDesk.PlatformTests/PlatformScriptTests.cs:41,49`) are renamed for the allowlist and assert the new shape

5. **Every test and doc comment uses the allowlist** (D4; R21)
   **Given** the other readers and writers of `Operations:Actors` (`Api.IntegrationTests/Infrastructure/ApiFactory.cs:41`, `ApiReferenceTests.cs:97`, `Operations/OperationsRolesTests.cs:15,66,70`, `Application/Abstractions/CurrentOperationsActor.cs:7`)
   **When** this story lands
   **Then** each sets or names `Operations:AllowedClients`; `grep -rn "Operations:Actors\|Operations__Actors\|\.Actors\b"` finds nothing in `backend/`, `infra/` or `e2e/`
   **And** in the QA stage, `e2e/tests/operations-read.spec.ts:80` asserts the machine user's `sub` is in `.Operations.AllowedClients`

6. **No configuration file adds operations callers** (D4; R21)
   **Given** `backend/src/DisputeDesk.Api/appsettings*.json`
   **When** a test reads every one of them
   **Then** none holds an `Operations` section, so the provisioner's clients file is the only source of `Operations:AllowedClients`

## Boundaries

- Must not change: the roles and which role each action needs (AD-9); JwtBearer validation; the 401 and 403 bodies; the audit entry's fields; the customer and automation actors.
- No per-client actor types, no wildcard entry, no start-up validation of the list's contents (an unknown sub in the list is harmless: no token carries it).
- No openapi change: the routes, their responses and their security schemes stay as they are.
- No secret-preserving rewrite of an old-shape clients file: regenerating both secrets is the provisioner's existing path for a file that is not current, and it costs a developer nothing (criterion 4).

## Supervisor checks

- Throwaway copy: compare subs case-insensitively; a test with a token whose `sub` differs from the list only in case gets 403 and fails.
- Throwaway copy: let an empty list allow every caller; criterion 2's empty-list test fails.
- Throwaway copy: add an `Operations` section to `appsettings.json`; criterion 6's test fails.
- Docker check (supervisor, throwaway stack): a clients file with the old `Actors` shape is rewritten on the next `docker compose up`; afterwards a customer signs in through the BFF and the operations token reads a dispute.

## Build facts

- Owner: backend (backend-builder); QA changes the e2e line in its stage.
- Tier: High-stakes (authorisation): the security agent reviews the diff and the human reviews it in a review block.
- Flags: high-stakes, security.
- Lane: A, after 8.11 (one at a time in wave 3); 8.13 may build beside it in lane B.
- Before the build (supervisor, docs-only change recording D4): PRD FR-25 consequence 3 (`docs/prd/prd.md:454`), the initiative's R21 (`docs/initiative-disputedesk.md:51`, "a credential not mapping to exactly one actor type"), AD-9's rule (`Operations:Actors:<sub>`), AD-14a's unmapped-sub sentence (`ActorType` keeps one meaning: the route's credential type), AD-26's options keys and `docs/decisions/ad-006-actor-agnostic-dispute-actions.md` (lines 12 and 25) name `Operations:AllowedClients`. The builder does not edit them.
- Mechanisms and sources:
  - Options pattern, binding an array: <https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options>; <https://learn.microsoft.com/aspnet/core/fundamentals/configuration#bind-an-array>.
  - Role-based authorisation (roles decide the action): <https://learn.microsoft.com/aspnet/core/security/authorization/roles>.
  - Allowlist, deny by default: <https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html>.
- Packages: none.
- Touches: `backend/src/DisputeDesk.Api/Operations/OperationsOptions.cs`, `OperationsAuthentication.cs`, `OperationsActorHandler.cs`, `Api/Authorization/AuthorizationResultHandler.cs` (comment), `Application/Abstractions/CurrentOperationsActor.cs` (comment), `infra/zitadel/provision/provision.sh`, `infra/tests/DisputeDesk.PlatformTests/PlatformScriptTests.cs`, the tests in criteria 2, 5 and 6; QA stage: `e2e/tests/operations-read.spec.ts:80`.

## Pre-build

- Challenger: `docs/verification/8.12-challenge.md` (simplify).
- Review and security: `docs/verification/epic-8-prebuild-wave3.md`, sections Review 8.12 and Security read 8.12.
- Decisions: none raised.
- Sofia, 2026-11-04: every `[story]` point folded. Review: the secrets regenerate on a rewrite (criterion 4, Notes); the two edge tests named (criterion 2); no openapi change (Boundaries); `ActorType`'s one meaning (docs-first). Security: exact array equality (criterion 4); no `Operations` section in any `appsettings*.json` (criterion 6) and a null or empty `sub` refused first (criterion 2). Challenger: the credential point settled by stating the regeneration and proving sign-in after it rather than preserving secrets (Notes); element and size bounds (Inputs); the edge tests (criterion 2).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- plan — docs/verification/simplicity-audit/mitigation-plan.md, section 1 (D4), wave 3, S12
- audit — docs/verification/simplicity-audit/api-host.md, API-2; design.md, DES-5
- prd — docs/prd/prd.md, FR-25
- architecture — docs/architecture/architecture.md, AD-9, AD-14a, AD-26
- decision — docs/decisions/ad-006-actor-agnostic-dispute-actions.md

## Notes

- Risk high (proposed): it is the operations routes' authorisation input; criterion 2 and the Supervisor checks prove an unlisted sub is refused and an empty list refuses all.
- Human decision, 2026-11-03 (D4), as quoted in the Description; not reopened here.
- Decision: 2026-11-04 (Sofia) — an old-shape clients file is rewritten by the provisioner's own current check, with both secrets regenerated as for any file that is not current, so no migration step or README note is needed for a developer's existing volume. The challenger's alternative (rewrite only `Operations`, keep the secrets) adds a second write path to the provisioner for a one-time local transition; the review's rewording was chosen.
