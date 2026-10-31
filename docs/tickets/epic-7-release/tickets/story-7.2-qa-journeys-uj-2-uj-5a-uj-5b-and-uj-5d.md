---
id: 2
type: story
title: "QA journeys UJ-2, UJ-5a, UJ-5b and UJ-5d"
parent: epic-7-release
covers: [R35, R40]
after: [1, 5.4, 3.12]
hitl: false
risk: medium
refined: false
---

# QA journeys UJ-2, UJ-5a, UJ-5b and UJ-5d

## Description

Adds four thin journeys to story 7.1's `journeys` Playwright project, each on a named, stable seeded dispute or login: UJ-2 withdraws customer A's seeded Under review dispute through the real dialog, UJ-5a opens customer B's dispute as A, UJ-5b tries to withdraw A's resolved dispute, and UJ-5d sends an unlinked login to linking. Each journey is traced through the browser, the network, the database and the events (NFR-TEST-5), and every SPA screen state visited is scanned by axe and covered by the manual keyboard pass. The `journeys` project runs on its own fresh stack (7.1, criterion 1), so UJ-2's withdrawal never meets `seeded-disputes.spec.ts`, which runs in the `regression` project.

## Requirements

Verify: on a fresh stack (`docker compose down -v`, then `docker compose up --build --wait`, behind `scripts/test-lock.sh`), `npm run journeys` in `e2e/` passes UJ-1 and the four journeys below; `docs/verification/7.2-qa.md` holds the axe results and the manual keyboard pass.

Inputs:
- UJ-2's dispute: customer A's seeded dispute on `ACC-A-1` / `TXN-A-0022` (Under review, reason unrecognised; story 5.4), found by `AccountRef` and `TransactionId` through `qa_readonly`. Its note: "Parcel arrived" (14 characters; FR-18's limit is 500).
- UJ-5a's dispute: customer B's seeded dispute on `ACC-B-1` / `TXN-B-0025` (Submitted), and one random version 4 GUID for the unknown-ID comparison.
- UJ-5b's dispute: customer A's seeded dispute on `ACC-A-1` / `TXN-A-0015` (Resolved, Upheld).
- UJ-5d's login: the fixture login `customer-c` (customer C, never linked; `fixtures/local.json`).
- Logins `customer-a` and `customer-b` through `support.ts`'s `fixtureLogin`.

1. **UJ-2: A withdraws an Under review dispute in the browser** (R35; NFR-TEST-5, FR-17, FR-18, FR-19)
   **Given** A signed in and the detail of the `TXN-A-0022` dispute showing "Under review"
   **When** A chooses "Withdraw dispute", types "Parcel arrived" in "Note (optional)" and confirms
   **Then** the detail shows the badge "Withdrawn" and no Withdraw control; its timeline's last entry is Withdrawn, actor "You", with the note "Parcel arrived"
   **And** on `/transactions` the `TXN-A-0022` row shows "Withdrawn" and no Dispute control; through the browser context's request client (Playwright, `browserContext.request`), that row in `GET /api/v1/transactions` has `disputable: false`, and a raise on it (`POST /api/v1/disputes` with the body `{ accountRef: 'ACC-A-1', transactionId: 'TXN-A-0022', reason: 'unrecognised' }`, the antiforgery cookie, its `X-XSRF-TOKEN` header and a quoted `Idempotency-Key`, so neither antiforgery nor validation can refuse it first) answers 409 `already-disputed` (FR-13); that rejected raise adds one `AuditLogEntries` row, `Raise`, `Rejected`, actor A (AD-14a), so criterion 2's audit expectations count it

2. **UJ-2 traced to the database and the topic** (R35; NFR-TEST-5, FR-28, NFR-DATA-2, AD-17)
   **Given** the dispute's `Version` and stored events read before the withdraw
   **When** the withdraw's 200 arrives
   **Then** through `qa_readonly` the `Disputes` row has `Status` `Withdrawn` and `Version` one higher; exactly one new `StoredEvents` row, `EventType` `DisputeWithdrawn`, holds the note; exactly one new `OutboxMessages` row has the same `Id`, and within 30 seconds (`expect.poll`, `timeout: 30_000`; a timeout fails) its `Status` is `Sent`
   **And** at least one record on `disputedesk.disputes` is keyed by the dispute ID with `type` `disputedesk.dispute.status-changed.v1`, `id` equal to the new stored event's `Id`, `data.fromStatus` `underReview`, `data.toStatus` `withdrawn`, `data.action` `withdraw` and `data.actorType` `customer` (camelCase wire values, `contracts/events/disputedesk.dispute.status-changed.v1.schema.json`); every record with that `id` is the same event (at-least-once, AD-17)

3. **UJ-5a: another customer's dispute is not found, exactly like an unknown ID** (R35; NFR-TEST-5, FR-17, NFR-SEC-6)
   **Given** B's `TXN-B-0025` dispute, whose `Disputes.CustomerId` is B's customer ID through `qa_readonly`, and which B can read (`GET /api/v1/disputes/<id>` as B answers 200)
   **When** A opens `/disputes/<B's id>` in the browser, and A's request client calls `GET /api/v1/disputes/<B's id>` and `GET /api/v1/disputes/<random GUID>`
   **Then** the browser shows the one not-found screen ("We can't find that dispute"); both API calls answer 404 with the same `Content-Type`, and their complete JSON bodies are equal once `traceId` is removed (RFC 9110 section 15.5.5; RFC 9457)
   **And** the read writes nothing: B's dispute keeps its `Version`, and no stored event, outbox row or audit row is added for it (AD-14a: customer reads write none)

4. **UJ-5b: a resolved dispute cannot be withdrawn** (R35; NFR-TEST-5, FR-20, FR-24)
   **Given** A's `TXN-A-0015` dispute (Resolved, Upheld)
   **When** its detail opens, then A's request client sends `POST /api/v1/disputes/<id>/withdraw` with the body `{}`, the antiforgery cookie and its `X-XSRF-TOKEN` header, so neither antiforgery nor validation can refuse it first
   **Then** the detail shows no Withdraw control; the POST answers 409 `action-not-allowed` with `customerStatus` `resolved` (checked with `support.ts`'s `expectProblemShape`)
   **And** through `qa_readonly` the dispute keeps its `Status`, `Version` and stored events, no outbox row is added for it, and one new `AuditLogEntries` row records `Withdraw`, `Rejected`, actor A, with that `DisputeId` (AD-14a, story 3.4)

5. **UJ-5d: an unlinked login is sent to linking** (R35; NFR-TEST-5, FR-9)
   **Given** customer C's fixture login, signed in
   **When** it opens `/transactions`
   **Then** the browser lands on `/link` and shows the linking details screen with its heading "Link your login" (story 6.4), and sends no `/api/v1/transactions` request: C's session state is `unlinked`, so `SessionGate` redirects `/transactions` to `/link` before the page requests anything (checked with `page.on('request')` or `page.waitForRequest` negated)
   **And** through the browser context's request client (`context.request.get('/api/v1/transactions')`, Playwright `browserContext.request`) the API answers 403 `linking-required` (checked with `support.ts`'s `expectProblemShape`)
   **And** no `CustomerLinks` row exists for C's login afterwards

6. **Accessibility: axe at two widths and the manual keyboard pass** (R40; NFR-CODE-6)
   **Given** the SPA screen states these journeys visit: the Under review detail, the withdraw dialog open, the withdraw dialog with the note, the Withdrawn detail, transactions with the Withdrawn row, the not-found screen, the Resolved detail and the linking details screen
   **When** `@axe-core/playwright` runs on each at 1280px and 320px wide through the journeys' shared `e2e/tests/journeys/screens.ts` (`axeTags`, `expectAccessibleAtBothWidths`, from 7.1) with the tags `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`, `wcag22aa`
   **Then** no violation is reported, and at 320px `document.documentElement.scrollWidth` is at most the viewport width (WCAG 2.2 SC 1.4.10)
   **And** `7.2-qa.md` records a manual keyboard pass over the same states as 7.1 does: logical Tab order, visible focus, no keyboard trap, focus inside the dialog and returned on close (WCAG 2.2 SC 2.1.1, 2.1.2, 2.4.3, 2.4.7), with pass or the finding; axe and this pass together check WCAG 2.2 AA, neither alone proves it

## Boundaries

- Must not change: `seeded-disputes.spec.ts` or any other `regression` spec; the seed (story 5.4).
- Each journey is independent of the order its project runs files: UJ-2 changes only `TXN-A-0022`'s dispute, which no other journey reads; UJ-5a and UJ-5d write nothing; UJ-5b writes one audit row, and UJ-2's rejected raise writes one.
- Never `TXN-A-0011`: it is the SLA demo dispute, escalated five minutes after seeding, so its status moves during a run (AD-30).
- No reset endpoint, database write, new test framework or fixture layer; no application code.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- In a throwaway copy, run `npm run journeys` twice on one stack: UJ-2 fails on the second run, because `TXN-A-0022`'s dispute is already Withdrawn (proves the fresh-stack rule is load-bearing).
- In a throwaway copy, make the customer dispute read answer another customer's dispute with 403 `forbidden`: criterion 3 fails.

## Build facts

- Owner: qa (qa agent).
- Flags: none.
- Lane: QA.
- Mechanisms and sources: Playwright projects (story 7.1), `browserContext.request` (<https://playwright.dev/docs/api/class-browsercontext#browser-context-request>), `page.waitForRequest` and `page.on('request')` (<https://playwright.dev/docs/network>), `expect.poll` (<https://playwright.dev/docs/test-assertions#expectpoll>); `@axe-core/playwright` WCAG tags (<https://github.com/dequelabs/axe-core/blob/develop/doc/API.md#options-parameter>); WCAG 2.2 (<https://www.w3.org/TR/WCAG22/>); RFC 9457 problem details and RFC 9110 404 (<https://www.rfc-editor.org/rfc/rfc9457>, <https://www.rfc-editor.org/rfc/rfc9110#section-15.5.5>).
- Packages: none.
- Touches:
  - `e2e/tests/journeys/uj-2.spec.ts`, `uj-5a.spec.ts`, `uj-5b.spec.ts`, `uj-5d.spec.ts` (new)
  - `docs/verification/7.2-qa.md`

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- prd — docs/prd/prd.md, UJ-2, UJ-5, FR-13, FR-17 to FR-20, FR-24, NFR-CODE-6, NFR-SEC-6, NFR-TEST-5
- architecture — docs/architecture/architecture.md, AD-14a, AD-17, AD-30
- experience — docs/ux/EXPERIENCE.md, Copy deck, Component Patterns (Withdraw), Accessibility Floor
- verification — docs/verification/7.2-challenge.md, docs/verification/7.1-challenge.md

## Notes

- Decision: 2026-10-26 — Gate 2: each QA run starts on a fresh stack (`docker compose down -v`, then `up --wait`); UJ-2's withdrawal of A's Under review dispute is reset the same way. The seeded disputes now come from story 5.4 (was 3.5).
- Decision: 2026-10-27 — WCAG 2.2 AA is checked by axe plus a recorded manual keyboard pass, not proved by axe alone (R40).
- Decision: 2026-10-30 (Sofia, 7.2 challenge) — UJ-2 uses the seeded Under review dispute on `TXN-A-0022`, whose reason is unrecognised, not PRD UJ-2's "not received": the only seeded "not received" dispute, `TXN-A-0011`, is the SLA demo and changes status during a run, and the reason plays no part in withdrawing. The PRD is unchanged; UJ-2's persona context is narrative.
- Decision: 2026-10-30 (Sofia, 7.2 challenge) — the unlinked login is customer C's fixture login; customer D has no fixture login and UJ-1 links it.
- Waits on 3.12 (the detail screen and withdraw dialog), 5.4 (the seeded disputes) and, through 7.1, on 6.4 (the `/link` route UJ-5d lands on).
