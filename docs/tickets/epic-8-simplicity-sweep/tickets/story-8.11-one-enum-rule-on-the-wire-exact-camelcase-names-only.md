---
id: 11
type: story
title: "One enum rule on the wire: exact camelCase names only"
parent: epic-8-simplicity-sweep
covers: [R13, R20, R30]
after: [10]
hitl: false
risk: medium
refined: true
---

# One enum rule on the wire: exact camelCase names only

## Description

Slice S13 of the mitigation plan (wave 3), carrying the human's decision D5. One API reads enums under two rules: the shared `SingleNameEnumConverter` (`Contracts/Json/SingleNameEnumConverter.cs:12-40`) wraps `JsonStringEnumConverter` and reads any casing, while `CamelCaseNameOnlyEnumConverter<TEnum>` (`Contracts/Json/CamelCaseNameOnlyEnumConverter.cs:13-37`) sits only on `ResolveDisputeRequest.Outcome` and accepts only the exact camelCase name, so `"WrongAmount"` binds on the raise but `"Upheld"` gets 400 on the resolve (API-1, DES-13). This story makes the strict rule the only one: one converter factory, registered once in `DisputeDeskJson.Apply`, reads every enum from exactly its camelCase name and refuses numbers, lists and any other casing. Writing is unchanged. The raise route stops accepting other casings, which is a contract change.

Human decision, 2026-11-03 (D5): yes, one enum rule for the whole wire contract: exact camelCase names only, no numbers, no lists. Strict matches 5.2 AC3, and the code already has the converter. AD-5 records the converter and its reason; `CamelCaseNameOnlyEnumConverter` becomes the shared converter through a factory and `SingleNameEnumConverter` goes; the raise route stops accepting other casings, and the openapi diff shows it.

## Requirements

Verify: the full backend suite passes with criterion 3's test rewritten; the OpenAPI drift check passes after regenerating; the frontend's Orval drift check passes; the e2e regression suite passes on the stack.

Inputs (only the reading rule changes):
- Every enum value the Api reads from a JSON request body: exactly the camelCase name of a member (`JsonNamingPolicy.CamelCase` of the member name), as a JSON string. Refused with 400 `validation` and no field named, as binding failures are today: any other casing, a number, a comma list, an unknown name.
- `null` or a missing property where the enum is required is not a binding failure: the body is well formed, and the built-in validation answers 400 `validation` naming the field, as today (`reason: null`, `outcome: null`); unchanged.
- Out of scope: query, route and header values. Minimal API binds those with `TryParse`, not System.Text.Json, and no enum is read from a query, route or header today; this rule covers JSON bodies only.
- Request enums today: `reason` on raise (`wrongAmount`, `duplicateCharge`, `notReceived`, `cancelledSubscription`, `refundNotProcessed`, `unrecognised`); `outcome` on resolve (`upheld`, `rejected`).

1. **One converter factory reads every enum strictly** (D5, API-1, DES-13; R30, AD-5)
   **Given** `DisputeDeskJson.Apply` (`Contracts/Json/DisputeDeskJson.cs:20-24`), which today adds `SingleNameEnumConverter`
   **When** this story lands
   **Then** it adds one `JsonConverterFactory` that creates `CamelCaseNameOnlyEnumConverter<TEnum>` for every enum type, so the HTTP options (`Api/Program.cs:47`) and the shared `DisputeDeskJson.Options` read the same way; `SingleNameEnumConverter.cs` is deleted, and the `[JsonConverter]` attribute on `ResolveDisputeRequest.Outcome` (`Contracts/Operations/ResolveDisputeRequest.cs:22`) goes because the shared factory covers it

2. **Writing is unchanged** (D5; R23, R28, R30)
   **Given** every response body, outbox CloudEvent payload (`Infrastructure/Persistence/CommitRows.cs:79`), stored event's data (`Application/Disputes/StoredEventRow.cs:61,74`) and stored raise response (`RaiseDisputeHandler.cs:71`)
   **When** they are written after this story
   **Then** each enum is the same camelCase string as before, and the contract tests for `openapi.json` and `contracts/events/*.schema.json` pass unchanged
   **And** writing a value that is not a defined member fails with a `JsonException`, as the built-in converter with integer values off does today, rather than writing a number as a string; a test proves it on the shared options
   **And** every stored event and fixture the suite reads back (`backend/fixtures/local.json` holds only camelCase values) replays as before

3. **The raise reason is read only from its exact name** (D5; R13, FR-13)
   **Given** `Raise_ReasonInEitherCase_BindsToThatReason` (`Api.IntegrationTests/Disputes/RaiseDisputeInputTests.cs:148-161`, which pins `"WrongAmount"` binding)
   **When** a raise sends `reason` as `"wrongAmount"`, `"WrongAmount"`, `"WRONGAMOUNT"`, `1` or `"wrongAmount, notReceived"`
   **Then** only `"wrongAmount"` binds; each other gets 400 `validation` with no field named and the input not echoed, before the lookup, and the test asserts no row in `Disputes`, `StoredEvents`, `OutboxMessages` or `IdempotencyKeys`; the test is renamed `Raise_ReasonNotItsExactCamelCaseName_Returns400` with these cases
   **And** a positive raise test with `"wrongAmount"` (and its expected amount) stays, so the replaced two-case success test still has its valid case

4. **The resolve outcome keeps its rule** (D5; R20, 5.2 AC3)
   **Given** `OperationsActionInputTests.BodiesThatDoNotBind` (`:38-42`: `"Upheld"`, `"UPHELD"`, `"Rejected"`, `"rEjected"`)
   **When** they run against the shared factory
   **Then** they pass unchanged

5. **Every client already sends exact names** (D5; R30)
   **Given** the SPA's generated types (`frontend/src/api/generated/`, camelCase literals) and the e2e specs
   **When** the stack runs the e2e regression suite
   **Then** no request the SPA or a spec sends as a valid request is refused; `e2e/tests/operations-actions.spec.ts:428` (a PascalCase outcome gets 400) passes unchanged

6. **The contract change is visible** (D5; R30, NFR-API-2)
   **Given** the regenerated `openapi.json`
   **When** the drift check runs
   **Then** the document is regenerated in this story and the build report records the result: an empty diff is the expected and acceptable result (`EnumSchemaTransformer` already writes the enum components as plain lists of camelCase names), and any difference is reviewed as the contract change D5 names, with `frontend/src/api/generated/` regenerated if it changed
   **And** the behaviour change itself is proved by criterion 3's tests, not by the document

## Boundaries

- Must not change: how any enum is written; the enum member names; which enums exist (8.17 changes where the internal ones live); what `EnumSchemaTransformer` does (only its doc comment changes: it crefs `SingleNameEnumConverter`, which this story deletes, and with `TreatWarningsAsErrors` and the doc file a dangling cref breaks the build).
- Accepted contract change: a caller sending another casing on raise is refused. Every known caller (the SPA's generated types, Scalar's enum lists, the e2e specs) sends exact names; any other caller of the raise route is broken by design (D5).
- No `Enum.IsDefined` check after binding: the converter refuses unknown names itself.

## Supervisor checks

- Throwaway copy: make the factory's read case-insensitive; criterion 3's `"WrongAmount"` case binds and fails.
- Throwaway copy: make the factory accept a number for an enum; criterion 3's `1` case binds and fails.
- Throwaway copy: write an undefined value as its number; criterion 2's undefined-value test fails.

## Build facts

- Owner: backend (backend-builder).
- Tier: Feature; the openapi diff is reviewed as a contract change.
- Flags: contract.
- Lane: A, after 8.10.
- Before the build (supervisor, docs-only change recording D5): AD-5 (`docs/architecture/architecture.md`, AD-5 "HTTP API shape") names the one converter factory and its reason (exact camelCase names, no numbers, no lists) in place of `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`, and AD-8's enum clause matches; the architecture's *Casing* row, PRD NFR-API-6 (`docs/prd/prd.md`, "enum values use camelCase") and `docs/conventions/backend.md:10` (*Enum casing*) name the same converter and say other casings are refused. The builder does not edit them.
- Mechanisms and sources:
  - Custom converters and the factory pattern: <https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/converters-how-to> (*Sample factory pattern converter*).
  - `JsonNamingPolicy.CamelCase`: <https://learn.microsoft.com/dotnet/api/system.text.json.jsonnamingpolicy.camelcase>.
- Packages: none.
- Touches: `backend/src/DisputeDesk.Contracts/Json/DisputeDeskJson.cs`, `CamelCaseNameOnlyEnumConverter.cs` (plus the factory beside it), `SingleNameEnumConverter.cs` (deleted), `Contracts/Operations/ResolveDisputeRequest.cs`, `Api/EnumSchemaTransformer.cs` (doc comment only), `Api.IntegrationTests/Disputes/RaiseDisputeInputTests.cs`, a converter test for criterion 2, `backend/openapi.json`, `frontend/src/api/generated/` (if regenerated).

## Pre-build

- Challenger: `docs/verification/8.11-challenge.md` (simplify).
- Review and security: `docs/verification/epic-8-prebuild-wave3.md`, section Review 8.11 (security raised nothing for 8.11).
- Decisions: none raised.
- Sofia, 2026-11-04: every `[story]` point folded. Review: `EnumSchemaTransformer`'s cref (Boundaries, Touches); NFR-API-6, `conventions/backend.md:10` and the *Casing* row (docs-first); bodies only (Inputs); an empty openapi diff acceptable (criterion 6). Challenger: query and header binding out of scope (Inputs); an undefined value refused on write (criterion 2); `null` and a missing property kept apart from binding failures (Inputs); the four tables asserted and a positive `wrongAmount` raise kept (criterion 3); the breaking change for other callers stated (Boundaries); the no-diff result recorded (criterion 6).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- plan — docs/verification/simplicity-audit/mitigation-plan.md, section 1 (D5), wave 3, S13
- audit — docs/verification/simplicity-audit/api-host.md, API-1; design.md, DES-13
- architecture — docs/architecture/architecture.md, AD-5, AD-8
- verification — docs/verification/5.2-verify.md, 5.2-build-fix2.md (the strict rule's origin)

## Notes

- Risk medium (proposed): a client sending another casing is now refused; the only clients are the SPA (generated camelCase types), Scalar (the document's enum lists) and the e2e specs.
- Human decision, 2026-11-03 (D5), as quoted in the Description; it replaces the 2026-10-29 decision (stories 3.3 and 3.9) that the raise reason binds in either case.
