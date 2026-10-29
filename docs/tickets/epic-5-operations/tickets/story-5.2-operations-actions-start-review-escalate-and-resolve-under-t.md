---
id: 2
type: story
title: "Operations actions start review, escalate and resolve under the allowed-actions table"
parent: epic-5-operations
covers: [R20, R21, R23, R10, R29, R36]
after: [1, 3.4]
hitl: false
risk: high
---

# Operations actions start review, escalate and resolve under the allowed-actions table

## Description

Adds `StartReview`, `Escalate` and `Resolve` to the aggregate with every FR-24 cell, the three action endpoints that load a dispute by replay (AD-13), the version-clash reload (AD-15), audit of refused and forbidden actions, and the status-changed events.

## Requirements

Verify: Domain unit tests cover every FR-24 cell; integration tests cover check order, the version-clash reload, refusal audit and events.

1. **Allowed-actions table** (R20; FR-24, NFR-DATA-3, NFR-TEST-2)
   **Given** every status and every action
   **When** the aggregate is asked to act
   **Then** only the FR-24 cells succeed, and every other cell returns `ActionNotAllowed` with the current status (a unit test per cell)

2. **Action endpoints** (R20; FR-21, FR-22, FR-23, NFR-API-4)
   **Given** a token with the matching role
   **When** it calls start-review, escalate or resolve
   **Then** each moves the dispute as FR-21 to FR-23 allow, and resolve requires an outcome and a customer-visible note of 1 to 500 characters after trimming

3. **Check order** (R20; FR-24, NFR-API-2)
   **Given** requests failing on several checks at once
   **When** they are sent
   **Then** they fail in the order 401, 403, 404, 400, 409

4. **Version clash: reload and decide once more** (R20, R29; FR-24, NFR-TEST-3)
   **Given** two parallel actions on one dispute
   **When** both commit
   **Then** one commits first; the other clears the change tracker, replays the stream once more and decides again with the same request: start review and escalate racing on a Submitted dispute both succeed (escalate is allowed from Under review), while two resolves give one success and one 409 `action-not-allowed` with the current status (AD-15; EF Core *Handling concurrency conflicts*)
   **And** the retry's instant is the later of the use-case instant and the stream's latest `OccurredAtUtc` (AD-4), a second clash returns 409, and there is never a second retry

5. **Refused and forbidden actions audited** (R21, R10; FR-25, NFR-SEC-10)
   **Given** actions refused after authentication: 403 for a missing role or no actor mapping, 404, and 409
   **When** they finish
   **Then** the 403s are recorded as `Denied` by the authorization result handler (`IAuthorizationMiddlewareResultHandler`, AD-10) and the 404s and 409s as `Rejected` by the handlers, each one entry through `AuditLog.Record` with actor type, actor ID and action, and a refused resolve's outcome only in `Details.outcome` (AD-14a)
   **And** a successful action appends its stored event (`ReviewStarted`, `DisputeEscalated` or `DisputeResolved`) and writes no audit entry (ad-009)

6. **Events and customer view** (R23, R18; FR-28, NFR-API-5)
   **Given** a successful action
   **When** the outbox and the customer timeline are read
   **Then** one `status-changed.v1` with `actorType` `operations` (and `outcome` on resolve) is written through story 3.8's mapping, with `id` equal to the stored event ID, and the customer timeline shows actor `theBank` with the resolve note

7. **Action metrics** (R36; NFR-OBS-3)
   **Given** actions and resolutions
   **When** metrics are collected
   **Then** `disputedesk.dispute_actions` counts by action and actor type and `disputedesk.disputes.resolved` by outcome and `sla_escalated`

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- UJ-3 from Scalar: start review then resolve Upheld with "We've reversed this debit."; customer A's timeline shows it.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Touches: `backend/src/DisputeDesk.Domain/Disputes/Dispute.cs`; `backend/src/DisputeDesk.Application/Operations/StartReview/, Escalate/, Resolve/`; `backend/src/DisputeDesk.Api/Operations/`; `backend/openapi.json`; backend/tests/ (Domain.Tests/Disputes/AllowedActions, Api.IntegrationTests/Operations/).

## References

- parent — docs/tickets/epic-5-operations/epic-5-operations.md

## Notes

- Risk high: an action outside FR-24; beyond the criteria, the reviewer reads the table test against the PRD table cell by cell.
