---
id: 2
type: story
title: "Operations actions start review, escalate and resolve under the allowed-actions table"
parent: epic-5-operations
covers: [R20, R21, R23, R10, R29, R36]
after: [1, 5, 3.4]
hitl: false
risk: high
---

# Operations actions start review, escalate and resolve under the allowed-actions table

## Description

Adds `StartReview`, `Escalate` and `Resolve` to the `Dispute` aggregate with every FR-24 cell, and `POST /api/v1/operations/disputes/<disputeId>/start-review`, `.../escalate` and `.../resolve`, each under its own role policy. One operations action handler loads the dispute by replay through the named ownership bypass (AD-11, AD-13), decides, commits through the one commit path (AD-14), and on a version clash reloads and decides once more (AD-15), as story 3.4's withdraw does. A successful action appends its stored event and publishes `status-changed.v1` through story 3.8's mapping; it writes no audit entry. A refused action is audited: the one authorization result handler records `Denied` for every 403 on an operations route (the read included), and the handler records `Rejected` for 404 and 409 (AD-14a).

## Requirements

Verify: Domain unit tests for every FR-24 cell of the three actions and the resolve inputs; integration tests (Testcontainers PostgreSQL, tokens signed by the test key as in story 5.1) for the endpoints and roles, inputs and check order, the version-clash reload, `Denied` and `Rejected` audit entries, events, the customer timeline, metrics and privacy pass; the architecture tests pass; `openapi.json` changes only by the three operations and `ResolveDisputeRequest`, and `frontend/src/api/generated/` matches it.

1. **Allowed-actions table in the aggregate** (R20, R29; FR-21, FR-22, FR-23, FR-24, NFR-DATA-3, NFR-TEST-2)
   **Given** each of the five statuses and `Dispute.StartReview(actor, instant)`, `Dispute.Escalate(actor, instant)` and `Dispute.Resolve(outcome, note, actor, instant)`
   **When** each method is called on a dispute in each status (15 cells, one unit test each, through the methods, not only through story 3.4's `IsAllowed`)
   **Then** only the FR-24 cells succeed (Submitted → Under review by start review; Submitted or Under review → Escalated; Under review or Escalated → Resolved), each appending exactly one pending stored event (`ReviewStarted`, `DisputeEscalated`, `DisputeResolved`) with the next `Version`, from- and to-status, actor and instant; every other cell returns `ActionNotAllowed` with the current status and appends nothing (AD-13)
   **And** `Resolve` checks the note first, before the status: 1 to 500 UTF-16 code units after trimming (AD-4), in a named constant beside the withdraw limit, refused with its own `DisputeErrors` validation error naming `note` and saying 500; the stored note is trimmed; `DisputeResolved` carries the outcome (unit tests: null, whitespace only, 500 and 501 units after trimming, a 500-unit note padded with spaces, a surrogate pair straddling the limit, each on an Under review and on a Resolved dispute)
   **And** `StoredEventReplayTests` replays streams ending in each new type and reproduces each `Disputes` row and timeline (AD-13)

2. **Three action endpoints, each under its own role** (R20, R21; FR-21, FR-22, FR-23, FR-25, NFR-API-4, NFR-SEC-3)
   **Given** the policies `dispute.startReview`, `dispute.escalate` and `dispute.resolve`, each built exactly like 5.1's `dispute.read`: the JwtBearer scheme, an authenticated user, the policy's own role and the actor-mapping requirement (AD-9; Microsoft Learn, *Policy-based authorization*)
   **When** a mapped token with the matching role calls each action on a dispute whose status allows it; and, on each action route, for both a real and an unknown dispute ID, tokens with no role, `dispute.read` only, another action's role, the route's own role with an unmapped `sub`, and a token shaped like a customer's ID token
   **Then** the action succeeds with 200 and an `OperationsDisputeResponse` built by 5.1's `OperationsView` from the replayed dispute and its stream, equal to the body of a following operations read; the action itself writes no `DisputeRead` row and no other audit row (ad-009; NFR-SEC-10)
   **And** each refused token gets 403 `forbidden` with the same body for the real and the unknown ID apart from `traceId` (FR-24: 403 before 404)
   **And** one handler in `Application/Operations/` serves all three, switching on `DisputeAction`, and reads the stream with `IgnoreQueryFilters(["Ownership"])` (AD-11); handlers read only `CurrentOperationsActor`, never `HttpContext.User` (AD-10); `EndpointAuthorizationTests` (5.1) pins the new routes to JwtBearer
   **And** a successful action reads the clock once and cuts it to whole microseconds, as withdraw does (AD-4); on a test clock that is not microsecond-aligned, the 200 body, the stored event and the outbox payload hold the same instant

3. **Inputs and check order** (R20; FR-23, FR-24, NFR-API-2)
   **Given** the inputs: `disputeId`, a route GUID with no route constraint; start review and escalate take no body, and any body sent is ignored; resolve takes a required JSON body, `ResolveDisputeRequest`, whose unknown properties are ignored, with `outcome` (`upheld` or `rejected`, camelCase names only; the existing `SingleNameEnumConverter` refuses numbers and lists) and `note` (a string, 1 to 500 UTF-16 code units after trimming); and an endpoint that calls `.DisableValidation()` (AD-8 as amended 2026-10-30)
   **When** resolve is sent, on an unknown ID, on an Under review dispute and on a Resolved dispute, with: malformed JSON, `[]`, the JSON `null`, no body, `"outcome": 1`, `"outcome": ["upheld"]`, `"outcome": "upheld, rejected"`, an unknown outcome name, `"note": 5`, a `text/plain` body; and with well-formed bodies holding no outcome, `"outcome": null`, no note, a whitespace-only note, a 501-unit note and a valid body; and each action is called with `disputeId` `not-a-guid`
   **Then** the order is 401, 403, then the framework's binding 400, then 404, then 400 for a well-formed body, then 409 (Microsoft Learn, *Parameter binding in minimal APIs*; AD-8 amended): every binding failure gets 400 `validation` before the lookup, also on the unknown ID, with no field named for a body failure and `disputeId` named for the route value, and never echoes the input; the `text/plain` body gets 415 `validation`; there is no custom body reading
   **And** a well-formed body gets 404 `not-found` on the unknown ID; on a known dispute a missing or null outcome gets 400 `validation` naming `outcome` and a bad note 400 naming `note`, both before the status, so a Resolved dispute also gets 400; a valid body on a Resolved dispute gets 409
   **And** the 409 is `action-not-allowed` with the extension `disputeStatus`, the internal status in camelCase, from a `DisputeErrors` factory beside 3.4's `ActionNotAllowedAs`; `docs/problems/action-not-allowed.md` names `disputeStatus` for operations, because a `status` extension would clash with the RFC 9457 §3.1 `status` member

4. **Version clash: reload and decide once more** (R20, R29; FR-24, NFR-DATA-5, NFR-TEST-3)
   **Given** 3.4's `RendezvousInterceptor` (both requests replay before either saves) and `AppendNextVersionInterceptor` (commits the next version's stored event and `Disputes` row ahead of the save, armed once or twice)
   **When** the following race: two resolves on one Under review dispute; resolve against the customer's withdraw on one Under review dispute; escalate with a `ReviewStarted` committed ahead of its save; start review with a `DisputeEscalated` committed ahead of its save; and a resolve armed twice
   **Then** the clash is only the `UniqueViolationException` for `UQ_StoredEvents_DisputeId_Version`, mapped to `DisputeErrors.VersionClash`, never a `DbUpdateConcurrencyException` (AD-15; EF Core *Handling concurrency conflicts*); on the first clash the handler clears the change tracker, replays the stream once more through the named bypass and decides again with the same request
   **And** of the two resolves, and of resolve against withdraw, exactly one succeeds and the other gets 409 with the reloaded status; escalate succeeds on its retry from Under review (`fromStatus` `underReview`); start review gets 409 with `disputeStatus` `escalated`
   **And** the retry's instant is the later of the use-case instant and the stream's latest `OccurredAtUtc` (AD-4); armed twice, the second clash reads the `Disputes` row once through the named bypass and answers 409 with the status as last read, with no further decision attempt; there is never a second retry (AD-15, decided 2026-10-30)

5. **Every operations 403 audited as `Denied` by the one result handler** (R21, R10; FR-25, NFR-SEC-10)
   **Given** the single registered `IAuthorizationMiddlewareResultHandler`, today `LinkingRequiredResultHandler`, extended and renamed `AuthorizationResultHandler`; it recognises an operations policy by its actor-mapping requirement, which each operations policy builds with its own `AuditAction` (`DisputeRead`, `StartReview`, `Escalate`, `Resolve`); and it leaves every response to the default `AuthorizationMiddlewareResultHandler`, apart from the existing `linking-required` (Microsoft Learn, *Customize the behavior of AuthorizationMiddleware*)
   **When** criterion 2's refused tokens call each action route and the read route; a request carries the session cookie and an unprivileged bearer token; an unlinked customer calls a customer route; and an operations route gets no credential, and gets the session cookie with a valid antiforgery header
   **Then** each operations 403 writes exactly one `AuditLogEntries` row through `AuditLog.Record`, saved before the 403 is written: result `Denied`, the route's action, actor type `Operations` and actor ID the bearer token's `sub`, mapped or not (AD-14a as amended 2026-10-30), `DisputeId` the route value when `Guid.TryParse` accepts it and otherwise null, no customer ID, and no dispute lookup; `IAppDbContext` and `TimeProvider` come from `context.RequestServices`, because the handler is a singleton
   **And** the rows for a real and an unknown ID are equal apart from `Id`, `DisputeId`, `OccurredAtUtc` and `TraceId`, and `not-a-guid` gives a null `DisputeId`; the cookie-plus-bearer request records the bearer's `sub`, never the customer's
   **And** the unlinked customer still gets 403 `linking-required` and no row; the operations 401s (no credential; the cookie with antiforgery, which proves CSRF does not reach these routes) write no row; 5.1's `ReadDispute_Refused401403Or400_WritesNoEntry` changes to expect one `Denied` row for the 403
   **And** a failed `Denied` save (`FailAuditInsertInterceptor`) answers 500 `internal` with no dispute data, failing closed as 5.1's read does (OWASP Logging Cheat Sheet; ASVS 5.0 V16.3.2)

6. **404 and 409 audited as `Rejected`** (R21, R10; FR-25, NFR-SEC-10)
   **Given** mapped tokens with each action's role
   **When** each action gets 404 (an unknown ID) and 409 (a status that forbids it, the reloaded status after a clash, and the second clash), and resolve gets each criterion 3 400 and 415
   **Then** each 404 and 409 writes exactly one row through `AuditLog.Record`: the action (`StartReview`, `Escalate` or `Resolve`), result `Rejected`, actor type `Operations`, actor ID the token's `sub`, `DisputeId` the requested ID for a 404 and the dispute's ID for a 409, no customer ID, and no outcome anywhere in the row (AD-14a as amended 2026-10-30; FR-25: actor, action and result)
   **And** no row is written for a success, a 401 or a 400 or 415, which are validation, not a refusal on rule or status (AD-3, AD-14a)
   **And** after a clash the audit save runs on the cleared change tracker; a failed `Rejected` save answers 500 `internal` with no dispute data (`FailAuditInsertInterceptor`)

7. **Status-changed events and the customer timeline** (R23, R18; FR-23, FR-28, NFR-API-5, NFR-TEST-4)
   **Given** a successful start review, escalate and resolve
   **When** the outbox and customer A's detail are read
   **Then** each writes one outbox row, a CloudEvents 1.0 structured `disputedesk.dispute.status-changed.v1` with the AD-19 attributes and `id` equal to its stored event ID, through story 3.8's one mapping (no new mapper): data `fromStatus`, `toStatus`, `action` (`startReview`, `escalate`, `resolve`), `actorType` `operations`, `outcome` on resolve only, `occurredAt` and `disputeVersion`, valid against the committed schema, and no note and no actor identity (AD-14, AD-19; Microsoft Learn, *Domain events versus integration events*)
   **And** the customer timeline (story 3.8's allowlist) shows each with actor `theBank`, an escalation as `underReviewPrioritised`, and the resolve with its outcome and trimmed note (FR-23)

8. **Action metrics, and no note in telemetry** (R36; NFR-OBS-3, NFR-SEC-6)
   **Given** each action succeeding, one succeeding on its retry, and refused, clashing-then-refused and failed-save attempts
   **When** metrics, logs and spans are collected
   **Then** after each successful commit only, `disputedesk.dispute_actions` rises by exactly 1 with exactly the labels `action` and `actor_type` `operations`, and each successful resolve adds 1 to `disputedesk.disputes.resolved` with exactly the labels `outcome` and `sla_escalated`; `sla_escalated` is `true` when the stream holds a `DisputeEscalated` with actor ID `worker:sla-sweep` (appended by the test as a stored event row, as 5.1 does) and `false` otherwise, a manual escalation included; refusals, the losing attempt and a failed save add nothing (AD-20; Microsoft Learn, *Creating metrics*, `IMeterFactory`, in 3.4's `DisputeActionMetrics`)
   **And** a telemetry test, as `TransactionsTelemetryTests` and `StoredEventPrivacyTests`, resolves with a marker note and finds it in no log line, exported span attribute, metric label, outbox payload, audit row or problem body; no metric label holds a dispute ID or a `sub`; `ResolveDisputeRequest` seals `ToString` (ASVS 5.0 V16.2.5)

## Boundaries

- Customer routes and the linking group behave as before: `linking-required`, `already-linked`, the customer 403 and 401 bodies, and story 3.4's withdraw are unchanged; their 403s write no row.
- Story 5.1's read is unchanged apart from its 403, which now writes a `Denied` row.
- `AuditLogEntries` gets no `Details` column and there is no migration in this story (decided 2026-10-30).
- Operations actions take no `Idempotency-Key`; repeating an action that succeeded gets 409 (FR-24).
- Start review and escalate take no note (FR-24).
- The operations routes get no rate limiter (a production-path item, story 7.6's Notes).
- After a second version clash the reported status is as last read and may already be stale (AD-15).

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- UJ-3 from Scalar: as customer A, raise a dispute through the SPA (5.4's seed comes later); paste a token from `scripts/operations-token.sh` (story 5.5), start review, then resolve Upheld with "We've reversed this debit."; customer A's timeline shows the bank and the note, and `AuditLogEntries` has no row for either success.
- In a throwaway copy, drop the actor-mapping requirement from `dispute.resolve`: criterion 2's unmapped-`sub` case fails.
- In a throwaway copy, register a second `IAuthorizationMiddlewareResultHandler` instead of extending the one: criterion 5's `linking-required` case fails.
- In a throwaway copy, answer the first clash with 409: criterion 4's escalate retry case fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes. No migration.
- Lane: M2, after 3.4, 5.1 and 5.5 (they edit the dispute, authorization, contract and OpenAPI files this story extends).
- Names: one handler in `Application/Operations/Actions/` serving all three actions; `ResolveDisputeRequest` and the three route patterns in `Contracts/Operations/`; the policies `dispute.startReview`, `dispute.escalate`, `dispute.resolve`; `AuthorizationResultHandler` (renamed from `LinkingRequiredResultHandler`).
- Touches:
  - `backend/src/DisputeDesk.Domain/Disputes/Dispute.cs` (`StartReview`, `Escalate`, `Resolve`, their `Apply` cases if not yet present, the resolve note limit), `DisputeErrors.cs` (resolve note error, the operations action-not-allowed factory)
  - `backend/src/DisputeDesk.Application/Operations/Actions/` (new), `Application/Abstractions/CurrentOperationsActor.cs` (a non-throwing read if the handler needs one)
  - `backend/src/DisputeDesk.Contracts/Operations/` (`ResolveDisputeRequest`, routes), `Contracts/Audit/AuditAction.cs` if a value is missing
  - `backend/src/DisputeDesk.Api/Operations/` (endpoints, policies, the actor requirement carrying its `AuditAction`), `Api/Authorization/LinkingRequiredResultHandler.cs` → `AuthorizationResultHandler.cs`, `Api/Authorization/CustomerRoutes.cs` (its registration), `Api/Disputes/DisputeActionMetrics.cs` (the resolved counter)
  - `docs/problems/action-not-allowed.md`, `backend/http/operations.http`
  - `backend/openapi.json`, `frontend/src/api/generated/` (regenerated with `npm run generate`; CI's Orval drift step fails otherwise)
  - `backend/tests/DisputeDesk.Domain.Tests/Disputes/` (the 15 cells, resolve inputs)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Operations/` (actions, inputs, clash, audit, events, metrics; `ReadDisputeAuditTests.cs` changes as criterion 5 says), `Disputes/StoredEventReplayTests.cs`, `Disputes/StoredEventPrivacyTests.cs`, `Authorization/EndpointAuthorizationTests.cs`
- Used unchanged: `Application/Audit/AuditLog.cs`, 5.1's `OperationsView`, story 3.8's publish mapping, `Infrastructure/Persistence/UniqueViolations.cs`, and in tests `RendezvousInterceptor`, `AppendNextVersionInterceptor`, `FailAuditInsertInterceptor`.
- Not touched: `Infrastructure/Persistence/Migrations/`, `AuditLogEntry` and its configuration, the Worker.

## References

- parent — docs/tickets/epic-5-operations/epic-5-operations.md
- architecture — docs/architecture/architecture.md, AD-3, AD-4, AD-8, AD-9, AD-10, AD-11, AD-13, AD-14, AD-14a, AD-15, AD-19, AD-20
- decision — docs/decisions/ad-009-stored-events.md
- prd — docs/prd/prd.md, FR-21, FR-22, FR-23, FR-24, FR-25, FR-28, NFR-API-2, NFR-API-4, NFR-OBS-3, NFR-SEC-10, SM-4
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding?view=aspnetcore-10.0>, parameter binding in minimal APIs
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/validation?view=aspnetcore-10.0>, minimal API validation and `DisableValidation`
- standard — <https://learn.microsoft.com/aspnet/core/security/authorization/policies?view=aspnetcore-10.0>, policy-based authorization
- standard — <https://learn.microsoft.com/aspnet/core/security/authorization/customizingauthorizationmiddlewareresponse?view=aspnetcore-10.0>, customize the behavior of AuthorizationMiddleware
- standard — <https://learn.microsoft.com/ef/core/saving/concurrency>, handling concurrency conflicts
- standard — <https://learn.microsoft.com/ef/core/querying/filters>, global query filters (named filters)
- standard — <https://learn.microsoft.com/azure/architecture/patterns/event-sourcing>, Event Sourcing pattern
- standard — <https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation>, domain events versus integration events
- standard — <https://learn.microsoft.com/dotnet/core/diagnostics/metrics-instrumentation>, creating metrics
- standard — <https://www.rfc-editor.org/rfc/rfc9457#section-3.1>, ProblemDetails members
- standard — <https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html>, OWASP Logging Cheat Sheet
- standard — <https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html>, OWASP CSRF Prevention Cheat Sheet
- standard — <https://github.com/OWASP/ASVS>, ASVS 5.0 V8.2.1, V16.2.5, V16.3.2, V16.5.1
- challenge — docs/verification/5.2-challenge.md
- review — docs/verification/5.2-prebuild-review.md, docs/verification/5.2-prebuild-security.md

## Notes

- Risk high: an action outside FR-24; beyond the criteria, the reviewer reads the cell tests against the PRD table cell by cell.
- Input (from 5.1 pre-build review, 2026-10-30): the result handler of criterion 5 records `Denied` on every operations route, the read route of story 5.1 included; 5.1 audits the read's success and 404 and writes no `Denied` row. The UJ-3 supervisor check pastes a token from `scripts/operations-token.sh` into Scalar (story 5.5).
- Decision: 2026-10-30 (human, 5.2 challenge `[decision]` point) — binding failures (malformed JSON, wrong JSON types, a non-JSON body) answer the framework's 400 before the lookup; only a well-formed body is checked after the 404, as story 3.4 settled for withdraw. AD-8 is amended; no custom body reading (criterion 3).
- Decision: 2026-10-30 (human, 5.2 pre-build review `[decision]` point) — a `Denied` entry for a token whose `sub` maps to no actor records actor type `Operations` with the token's `sub` as actor ID; "Operations" means presented to an operations route. Written into AD-14a (criterion 5).
- Decision: 2026-10-30 (human, 5.2 records) — the refused-resolve audit entry holds no outcome (`Details.outcome` dropped); FR-25 asks for actor, action and result. No `Details` column and no migration in this story; AD-14a amended (criterion 6).
- Decision: 2026-10-30 (Sofia, 5.2 pre-build review) — a successful action answers 200 with the operations view of the dispute, built by `OperationsView` without calling the read handler, so no `DisputeRead` row is written beside a successful change (criterion 2).
- Decision: 2026-10-30 (Sofia, 5.2 pre-build review) — the operations 409 carries `disputeStatus`, not `status`, which RFC 9457 §3.1 reserves for the HTTP status code (criterion 3).
- Decision: 2026-10-30 (Sofia, 5.2 pre-build review and security S1) — the `Denied` record lives in the one registered result handler, renamed `AuthorizationResultHandler`, because the authorization middleware resolves only one; the action comes from the operations actor requirement each policy builds with its `AuditAction` (criterion 5).
- Decision: 2026-10-30 (Sofia, 5.2 pre-build review) — `sla_escalated` means the stream holds a `DisputeEscalated` with actor ID `worker:sla-sweep` (SM-4: resolved without an SLA escalation); actions are counted after commit only (criterion 8).
- Decision: 2026-10-30 (Sofia, 5.2 pre-build review) — one handler serves the three actions, switching on `DisputeAction`, rather than three copies of the reload and retry (AGENTS.md: abstract on the third case); withdraw keeps its own (criterion 2).
- Decision: 2026-10-30 (Sofia, 5.2 challenge) — the two-resolves race starts from Under review, and the start-review-against-escalate race is staged in both orders with `AppendNextVersionInterceptor`, so each outcome is fixed (criterion 4).
- Production path: lines for `docs/production-path.md` are in story 7.6's Notes (Input from 5.2, 2026-10-30).
