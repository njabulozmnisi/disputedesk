---
id: 10
type: story
title: "Every route checks in the framework's order: 401, 403, 400, 404, 409, 422"
parent: epic-8-simplicity-sweep
covers: [R19, R20, R30]
after: [2, 4]
hitl: false
risk: high
refined: true
---

# Every route checks in the framework's order: 401, 403, 400, 404, 409, 422

## Description

Slice S9 of the mitigation plan (wave 3), carrying the human's decision D1. FR-24 orders the checks 401, 403, 404, 400, 409 so that a caller without the role cannot probe which dispute IDs exist; 403 before 404 alone meets that reason, and the 404-before-400 step forces `.DisableValidation()` on withdraw and resolve, a hand-written body check after the lookup, and a second order for raise (DES-4, AD-8). This story moves every dispute route to the framework's order: authentication and authorisation, then the built-in minimal API validation of the request, then the lookup, the status and the business rules. The request types validate themselves with data annotations and `IValidatableObject`, and the handlers lose their post-lookup body checks. What changes for a caller: a malformed or over-long body on an unknown ID now gets 400, not 404.

Human decision, 2026-11-03 (D1): yes, the framework's order on every route, raise included; 403 before 404 still stops ID probing, which is FR-24's stated reason. FR-24, R20 and AD-8 change; `DisableValidation`, the hand-written body check after the lookup, raise's second order and the `AcceptsMetadata([])` workaround go; the 3.4 and 5.2 order tests are rewritten to the new order.

## Requirements

Verify: the full backend suite passes with the order tests rewritten as criterion 6 lists; the OpenAPI drift check passes after regenerating `openapi.json` and `frontend/src/api/generated/` (criterion 7); the e2e regression suite passes on the stack after QA's two spec changes.

Inputs (unchanged bounds; only when each is checked changes):
- Withdraw, `POST /api/v1/disputes/{disputeId}/withdraw` (`Contracts/Disputes/WithdrawDisputeRequest.cs:10`): route `disputeId` a GUID; body JSON with `note`, optional, at most 500 characters after trimming.
- Resolve, `POST /api/v1/operations/disputes/{disputeId}/resolve` (`Contracts/Operations/ResolveDisputeRequest.cs:21-23`): route `disputeId` a GUID; body JSON with `outcome`, required, `upheld` or `rejected` (read by `CamelCaseNameOnlyEnumConverter<Outcome>`); `note`, required, 1 to 500 characters after trimming.
- Raise, `POST /api/v1/disputes` (`Contracts/Disputes/RaiseDisputeRequest.cs:21-62`): `accountRef` and `transactionId` required, 1 to 32 characters; `reason` required; `note` optional, at most 1,000 characters after trimming; `expectedAmount` matching `^[0-9]{1,17}(\.[0-9]{1,2})?$`, required for `wrongAmount` and refused for every other reason; header `Idempotency-Key`, an RFC 9651 quoted String of 1 to 255 printable characters.
- Start review and escalate: route `disputeId` a GUID; no body (unchanged).

1. **Withdraw validates its body before the lookup** (D1; R19, R20, FR-24)
   **Given** `WithdrawDisputeRequest` and the withdraw endpoint (`Api/Disputes/DisputeEndpoints.cs:62-63`)
   **When** a linked customer sends a withdraw
   **Then** the checks run 401, 403, 400, 404, 409: the request validates `note` (trimmed length at most 500) through `IValidatableObject`, as `RaiseDisputeRequest` does, so the built-in validation answers 400 `validation` naming `note` before the handler runs; `.DisableValidation()` is removed
   **And** the handler keeps its generic failure return after `dispute.Withdraw` (`WithdrawDisputeHandler.cs:76-80`), because without it a domain refusal would commit the `Disputes` row with no stored event and answer 200 (pitfall from 3.8); only its comment changes, to say the request's validation has already checked the note, so the return guards the domain rule alone; the domain's own note rule (`Domain/Disputes/Dispute.cs:185-190`) and its tests stay

2. **Resolve validates its body before the lookup** (D1; R20, FR-23, FR-24)
   **Given** `ResolveDisputeRequest` and the resolve endpoint (`Api/Operations/OperationsEndpoints.cs:52`)
   **When** an operations caller with the resolve role sends a resolve
   **Then** the checks run 401, 403, 400, 404, 409: the request validates `outcome` (required) and `note` (required, trimmed length 1 to 500) through data annotations and `IValidatableObject`, answered by the built-in validation before the handler; `.DisableValidation()` is removed
   **And** `Outcome` stays a nullable property with `[Required]` (a non-nullable enum would default to `Upheld`, the first member), and the handler reads the validated value as raise reads its reason (`RaiseDisputeHandler.cs:33`, `?? throw new ArgumentException(…)`), never with `!` and never as a default; `OperationsActionHandler`'s `OutcomeRequired` branch (`OperationsActionHandler.cs:120-127`) and `DisputeErrors.OutcomeRequired` (`Domain/Disputes/DisputeErrors.cs:57`) are deleted; the handler keeps its generic failure return (`:87-91`), with only its comment changed as in criterion 1; the domain's note rule (`Dispute.cs:241-244`) stays

3. **Raise runs the same order** (D1; R13, R14, R20, FR-24)
   **Given** the raise endpoint (`DisputeEndpoints.cs:148`, remarks)
   **When** a linked customer raises
   **Then** the checks run 401, 403, 400 (the built-in validation and the `Idempotency-Key` filter), 404 (core banking, `RaiseDisputeHandler.cs:39`), 409, 422, then 400 for a rule that needs the loaded resource: the comparison of `expectedAmount` with the loaded transaction's amount stays a 400 after the lookup
   **And** the `Idempotency-Key` filter's own answers before the lookup are unchanged (AD-16: a replay of a stored response, 409 while the first request is in flight, 422 for the key reused with another body); they belong to the key, not to the order of the body's checks
   **And** the endpoint's remarks and the withdraw and resolve remarks (`DisputeEndpoints.cs:100`, `OperationsEndpoints.cs:137-138`) state this one order, with its trailing step

4. **A body error never reveals whether an ID exists** (D1; R17, FR-24, NFR-SEC-6)
   **Given** an over-long withdraw note, and separately a resolve with no `outcome` and a resolve with `"outcome": null`
   **When** each is sent for an unknown ID, for another customer's dispute (withdraw) and for a dispute the caller may act on
   **Then** all three answer the same 400 `validation` body (apart from `traceId`), naming the field and never echoing its value, and no dispute, stored event, outbox or audit row is written; 401 and 403 still come first (`RaiseDisputeAuthorizationTests`, `ReadDispute_DisputeIdNotAGuidWithoutAValidReader_Returns401Or403Before400` unchanged), and an operations 403 still writes its `Denied` entry (AD-14a)

5. **A non-JSON body: 415 for a linked cookie on withdraw, the cookie 401 on resolve** (D1 as answered 2026-11-04; R21, FR-24)
   **Given** a `text/plain` body, and the `AcceptsMetadata([], typeof(ResolveDisputeRequest))` line and its comment (`OperationsEndpoints.cs:53-60`) removed, per the human's answer of 2026-11-04 (`docs/verification/epic-8-decisions.md`)
   **When** it is sent to withdraw with a linked customer's cookie, or to resolve with no token, a refused token or a valid token with the resolve role
   **Then** withdraw answers 415 `validation` (`Withdraw_TextPlainBody_Returns415Validation` unchanged); resolve is answered by routing's 415 endpoint under the cookie fallback policy: 401 with no `WWW-Authenticate: Bearer` in all three cases, no dispute, stored event or outbox row, and no `Denied` entry for the refused token
   **And** `Resolve_TextPlainBodyWithNoTokenOrARefusedToken_Returns401BearerOr403Before415` and `Resolve_TextPlainBody_Returns415ValidationAndWritesNoRow` are renamed and rewritten to that behaviour (for example `Resolve_TextPlainBodyWithAnyToken_Returns401FromTheCookieFallbackAndWritesNoRow`), covering the three token cases

6. **The order tests state the new order** (D1; R35)
   **Given** the tests that pin the old order: `WithdrawDisputeInputTests` (`Withdraw_NoteTooLongOnAnUnknownId_Returns404Before400`, `Withdraw_NoteTooLongOnTheOwnersResolvedDispute_Returns400Before409`), `OperationsActionInputTests` (`Resolve_WellFormedBodyOnAnUnknownId_Returns404NotFound`, `Resolve_MissingOutcomeOrBadNoteOnAKnownDispute_Returns400NamingTheFieldBeforeTheStatusAndWritesNoRow`)
   **When** this story lands
   **Then** each is renamed and rewritten to the new order (for example `Withdraw_NoteTooLongOnAnUnknownId_Returns400BeforeTheLookup`), and a well-formed body on an unknown ID still gets 404 on both routes; the binding tests at `WithdrawDisputeInputTests.cs:80,89` need no rewrite; the Domain tests that order the note error before the status (`DisputeWithdrawTests.cs:123`, `DisputeOperationsActionTests.cs:98`) stay
   **And** each 400's problem type (`validation`), status and named field are unchanged, and each message is today's text, set through the attribute's `ErrorMessage` or the `ValidationResult` the request returns, so the assertions on the messages pass unchanged

7. **The contract shows the change** (D1; R30, NFR-API-2)
   **Given** the regenerated `openapi.json`
   **When** the drift check runs
   **Then** any difference is only the endpoints' descriptions of the order, their 400 responses, and the `WithdrawDisputeRequest` and `ResolveDisputeRequest` schema descriptions that say the body is "checked after the dispute is loaded", reviewed as a contract change; `frontend/src/api/generated/` is regenerated if the document changed

## Boundaries

- Must not change: the authentication and authorisation of any route; the problem types, statuses, named fields and messages (criterion 6); which refusals are audited, apart from criterion 5's `text/plain` resolve with a refused token, which the human accepted unaudited on 2026-11-04 (AD-14a notes it); start review, escalate and the read routes (they already run 401, 403, 400, 404, 409); the SPA (it never sends an invalid body it has not validated itself).
- The enum casing rule is 8.11's (D5); this story keeps both converters as they are.
- No validation layer beside the built-in one: no endpoint filter, no hand-written body check in a handler. The built-in validation already validates `RaiseDisputeRequest` and `StartLinkingRequest` from the Contracts assembly as endpoint parameters, so the two request types here are found the same way; criteria 1 and 2's tests prove it.

## Supervisor checks

- Throwaway copy: put `.DisableValidation()` back on withdraw; criterion 4's unknown-ID case answers 404 and fails.
- Throwaway copy: make resolve's `IValidatableObject` accept a 501-character note; criterion 2's 501-character test fails.
- Throwaway copy: let the over-long-note 400 on another customer's dispute differ by its `detail`; criterion 4 fails.
- Throwaway copy: drop the generic failure return after `dispute.Withdraw` and make the domain refuse a note the request accepted; a test sees 200 with no stored event and fails (criterion 1).
- Throwaway copy: make `Outcome` a non-nullable enum; criterion 4's missing-`outcome` case binds as `Upheld` and fails.
- Docker check (supervisor, throwaway stack): the two e2e cases QA rewrites pass on the stack.

## Build facts

- Owner: backend (backend-builder); QA changes the e2e lines in its stage.
- Tier: High-stakes (the order of authorisation checks): the security agent reviews the diff and the human reviews it in a review block.
- Flags: high-stakes, security, contract.
- Lane: A, one at a time in wave 3.
- Before the build (supervisor, docs-only change recording D1 and the human's answer of 2026-11-04): PRD FR-24 (`docs/prd/prd.md:444`, "Checks run in this order: 401, 403, 404, 400, 409"), the initiative's R20 (`docs/initiative-disputedesk.md:50`) and AD-8's Rule state the order in criterion 3 with its trailing step ("…, 422, then 400 for a rule that needs the loaded resource"); AD-8's *Route-ID endpoints with a body* and *Resolve* details drop `DisableValidation` and the `AcceptsMetadata` bullet and its 415-ordering workaround, and state criterion 5's cookie 401 for a `text/plain` resolve; AD-14a notes that case as unaudited (a refused token writes no `Denied` entry there). The builder does not edit them.
- Mechanisms and sources:
  - Minimal API validation (`AddValidation`, data annotations, `IValidatableObject`, the 400 before the handler): <https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis#validation-support-in-minimal-apis>; <https://learn.microsoft.com/aspnet/core/fundamentals/validation>.
  - Status precedence: RFC 9110 sets none between 400 and 404 (<https://www.rfc-editor.org/rfc/rfc9110#section-15.5>); authorisation first, deny by default: <https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html>.
  - Parameter binding and 415: <https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding>.
  - Idempotency-Key answers before the lookup: draft-ietf-httpapi-idempotency-key-header, as AD-16 cites.
- Packages: none.
- Touches:
  - `backend/src/DisputeDesk.Contracts/Disputes/WithdrawDisputeRequest.cs`, `Contracts/Operations/ResolveDisputeRequest.cs`
  - `Api/Disputes/DisputeEndpoints.cs`, `Api/Operations/OperationsEndpoints.cs`
  - `Application/Disputes/WithdrawDispute/WithdrawDisputeHandler.cs` (comment), `Application/Operations/Actions/OperationsActionHandler.cs`, `Domain/Disputes/DisputeErrors.cs` (`OutcomeRequired` deleted)
  - the tests in criteria 5 and 6, `backend/openapi.json`, `frontend/src/api/generated/` (regenerated; CI fails on drift)
  - QA stage: `e2e/tests/dispute-withdraw.spec.ts:261` ("a 501-unit note gets 404 on an unknown ID") and `e2e/tests/operations-actions.spec.ts:459-470` ("404 first on the unknown ID, then 400"), rewritten to the new order

## Pre-build

- Challenger: `docs/verification/8.10-challenge.md` (rethink, on the `AcceptsMetadata` point).
- Review and security: `docs/verification/epic-8-prebuild-wave3.md`, sections Review 8.10 and Security read 8.10.
- Decisions: settled by the human, 2026-11-04 (`docs/verification/epic-8-decisions.md`, D1).
- Sofia, 2026-11-04: every `[story]` point folded. Review: the generic failure returns stay (criteria 1, 2), `OutcomeRequired` deleted, the order's trailing 400 (criterion 3, docs-first), the schema descriptions in the diff (criterion 7), messages kept (criterion 6), binding tests unchanged, criterion 5 titled for a linked cookie. Security: nullable `Outcome` with `[Required]`, no `!`, `"outcome": null` (criteria 2, 4). Challenger: messages and `detail` kept (criterion 6); cross-assembly validation (Boundaries); no dispute, event or outbox row, with the operations 403's `Denied` entry kept (criterion 4); the `Idempotency-Key` answers and the trailing 400 stated (criterion 3). Its `[decision]` points are the human's answer above (criterion 5).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- plan — docs/verification/simplicity-audit/mitigation-plan.md, section 1 (D1), wave 3, S9
- audit — docs/verification/simplicity-audit/design.md, DES-4, DES-6
- prd — docs/prd/prd.md, FR-23, FR-24
- architecture — docs/architecture/architecture.md, AD-3, AD-8
- stories — docs/tickets/epic-3-disputes/tickets/story-3.4-customer-withdraws-an-open-dispute.md; docs/tickets/epic-5-operations/tickets/story-5.2-operations-actions-start-review-escalate-and-resolve-under-t.md

## Notes

- Risk high (proposed): it changes the order of checks on authorised routes; criterion 4 proves no ID can be probed.
- Human decision, 2026-11-03 (D1), as quoted in the Description; not reopened here.
- Decision: 2026-11-04 (Sofia) — raise's comparison of the expected amount with the transaction amount stays a 400 after the lookup, because the amount is only known once the transaction is loaded; that is the framework order's own tail (a rule that needs the resource runs after it is loaded), so every route now states one order. Changing its status to 422 would change FR-14's contract, which D1 does not decide.
- Human decision, 2026-11-04 (`docs/verification/epic-8-decisions.md`, 8.10): remove `AcceptsMetadata([])` anyway; a `text/plain` resolve gets the cookie fallback's 401 for a missing, refused or valid token, a refused token writes no `Denied` entry, and AD-8, AD-14a and the resolve `text/plain` tests follow (criterion 5). This replaces Sofia's earlier `blocked` instruction.
