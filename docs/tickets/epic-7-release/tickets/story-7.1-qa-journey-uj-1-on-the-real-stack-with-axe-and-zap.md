---
id: 1
type: story
title: "QA journey UJ-1 on the real stack with axe and ZAP"
parent: epic-7-release
covers: [R35, R40, R33]
after: [6.4, 3.6, 3.10, 3.11, 3.7, 3.12, 2.5, 2.6, 4.1]
hitl: false
risk: high
refined: false
---

# QA journey UJ-1 on the real stack with axe and ZAP

## Description

Adds the first journey: a thin Playwright test of UJ-1 that signs up a new login, links it to customer D, raises an unrecognised dispute on D's R849.00 card debit and signs out, then traces that one dispute through the browser, the network, the database and the topic, with axe scans of every SPA screen state visited, a recorded manual keyboard pass and the ZAP baseline. It also splits the Playwright suite into two projects, `journeys` and `regression`, each run on its own fresh stack, because one fresh stack per run does not isolate two specs that both link customer D (`linking-verify.spec.ts` links D and UJ-1 links D).

## Requirements

Verify: on a fresh stack (`docker compose down -v`, then `docker compose up --build --wait`, behind `scripts/test-lock.sh`), `npm run journeys` in `e2e/` passes UJ-1, twice, each run on its own fresh stack; `docs/verification/7.1-qa.md` holds the axe results, the manual keyboard pass and the ZAP baseline output with each alert triaged.

Inputs:
- The new login: email `uj1-<label>-<epoch milliseconds>@disputedesk.test` from the shared registration helper given the prefix `uj1` (unique per run, at most 254 characters, RFC 5321 section 4.5.3.1.3), the helper's dev-only password `Dev-Only-Qa62-Pass1!` (it meets the provisioned Zitadel password policy), and the 6-character email verification code read from Mailpit's API.
- Linking details: customer D's ID number and first account number read from `fixtures/local.json` (13 and 6 to 16 ASCII digits, story 6.4's bounds); the 6-digit OTP read from Mailpit's API. None of them is printed in test output or in `7.1-qa.md`.
- The raise: transaction `TXN-D-0030` (R849.00, ZX Digital Services, settled card debit, 3 days ago), reason `unrecognised`, note "I don't recognise this merchant." (32 characters; FR-12's limit is 1,000).
- Edge case row: `TXN-D-0031` (pending card debit).

1. **The suite has two projects, each on its own fresh stack** (R35; NFR-TEST-5, FR-29)
   **Given** `e2e/playwright.config.ts` with `testDir: './tests'`
   **When** this story lands
   **Then** the config declares two Playwright projects (Playwright, *Projects*): `journeys` with `testMatch: 'journeys/**/*.spec.ts'` and `regression` with `testIgnore: 'journeys/**'`; `e2e/package.json` gets the scripts `"journeys": "playwright test --project=journeys"` and `"test": "playwright test --project=regression"`, so every supported run names one project
   **And** each QA run of a project starts on a fresh stack (`docker compose down -v`, then `docker compose up --build --wait`), so customer D starts unlinked from the seed in both; no test-only database role, reset endpoint or test framework is added (AD-30, Gate 2)
   **And** every journey spec is independent of the order the project runs its files: UJ-1 changes only customer D and its new login

2. **UJ-1 through the browser** (R35; NFR-TEST-5, FR-1, FR-2, FR-4, FR-6, FR-11, FR-13, FR-16, FR-17)
   **Given** an anonymous browser context on the gateway origin
   **When** the test signs up on the hosted login page, verifies the email with the code from Mailpit, enters D's details on `/link`, sees "If these details match our records, we've sent a code to the cellphone number we hold for you.", enters the OTP from Mailpit on `/link/otp`, lands on `/transactions`, opens `TXN-D-0030`, chooses Dispute, picks "unrecognised", adds the note and submits
   **Then** the `TXN-D-0030` row shows the badge "Submitted"; `/disputes` lists the dispute; its detail timeline has exactly one entry: Submitted, a time ending " SAST", actor "You"
   **And** the `TXN-D-0031` row (pending) shows as not disputable and has no Dispute control (UJ-1 edge case)
   **And** `registerLogin` moves from `linking-verify.spec.ts` into `support.ts` with the helpers it needs (`mailpitSearch`, `messageText`, `pause`, the Mailpit URL and origin constants) and the SMS reader `smsTo`, taking the run's start instant as an argument; `registerLogin` gains one argument, the address prefix, and `linking-verify.spec.ts` passes `qa62`, so its addresses and assertions stay as they are; `nextOtpForD` stays in `linking-verify.spec.ts`, because it records codes in that spec's own set, and UJ-1 reads its one OTP with `smsTo`

3. **The raise traced to the database** (R35, R23; NFR-TEST-5, FR-13, FR-14, NFR-DATA-2, NFR-SEC-10)
   **Given** the raise POST captured with `page.waitForResponse` (Playwright, *Network*)
   **When** its 201 arrives
   **Then** the request carried an `Idempotency-Key` header and `X-XSRF-TOKEN`; the dispute ID is read from the response body, and every database check below is scoped to that ID through `qa_readonly`
   **And** exactly one `Disputes` row has that `Id`, with `Status` `Submitted`, `Reason` `Unrecognised` and `Version` 1; exactly one `StoredEvents` row has that `DisputeId`, with `EventType` `DisputeRaised` and `Version` 1; exactly one `OutboxMessages` row exists for it and its `Id` equals the stored event's `Id`
   **And** within 30 seconds (`expect.poll` with `timeout: 30_000`, Playwright, *Assertions*; a timeout fails the test) that outbox row's `Status` is `Sent`
   **And** no `AuditLogEntries` row has that `DisputeId` (a successful raise is a stored event only, NFR-SEC-10)

4. **The raise traced to the topic** (R35, R23; NFR-TEST-5, FR-27, AD-17, AD-19)
   **Given** the outbox row is `Sent`
   **When** the test reads `disputedesk.disputes` with `support.ts`'s `topicRecords()`
   **Then** at least one record is keyed by the dispute ID; every record keyed by it is a structured CloudEvent with `type` `disputedesk.dispute.raised.v1` and `id` equal to the stored event's `Id`, and carries a `traceparent` header; more than one such record is accepted, because delivery is at-least-once and consumers deduplicate by CloudEvent `id` (AD-17; CloudEvents 1.0, `id`)

5. **Sign-in, sign-out and session end** (R35; FR-2, FR-3, NFR-SEC-10)
   **Given** the new login's subject, read from `CustomerLinks` where `CustomerId` is D's customer ID
   **When** the test signs out through the SPA's sign-out form
   **Then** `AuditLogEntries` holds a `SignIn` and a `SignOut` entry with that subject as `ActorId`, both later than the run's start instant; the session cookie captured before sign-out, replayed on `GET /api/v1/transactions` in a fresh request context, gets 401 `unauthorized`; and `/api/v1/bff/login` in the same browser context shows the hosted login's account picker or username page, after which the password page (`/ui/v2/login/password`) is required and no navigation to `/api/signin-oidc` happens, as `bff-sign-in-and-out.spec.ts` checks, so the identity provider session ended too (OpenID Connect RP-Initiated Logout 1.0)

6. **Accessibility: axe on every SPA screen state, at two widths** (R40; NFR-CODE-6)
   **Given** the SPA screen states UJ-1 visits: the linking details screen, the same screen with its error summary after submitting empty fields, the OTP screen, transactions, each raise screen to the review and its submitted state, the dispute history, the dispute detail and the signed-out page
   **When** `@axe-core/playwright` runs on each at 1280px and 320px wide with the tags `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa` and `wcag22aa` (axe-core, *API*, `runOnly` tags), through the journeys' shared `e2e/tests/journeys/screens.ts` (`axeTags`, `expectAccessibleAtBothWidths`); the regression specs keep their private copies
   **Then** no violation is reported, and at 320px `document.documentElement.scrollWidth` is at most the viewport width, so content reflows with no horizontal scroll (WCAG 2.2 SC 1.4.10)
   **And** the hosted sign-up and email verification pages are scanned too, and their violations are recorded in `7.1-qa.md` without failing the test, because they are the identity provider's pages that DisputeDesk only themes (AD-6, story 2.6)

7. **Manual keyboard pass recorded** (R40; NFR-CODE-6)
   **Given** the same SPA screen states
   **When** a person completes UJ-1 by keyboard only
   **Then** `7.1-qa.md` records per screen: the Tab order is logical, focus is always visible, there is no keyboard trap, the error summary takes focus and links to its fields, and each form error is shown next to its labelled field (WCAG 2.2 SC 2.1.1, 2.1.2, 2.4.3, 2.4.7, 3.3.1), with pass or the finding; the record says axe and this pass together check WCAG 2.2 AA, and neither alone proves it

8. **ZAP baseline on the gateway, every alert triaged** (R33; NFR-SEC-8)
   **Given** the running stack
   **When** `zap-baseline.py -t http://host.docker.internal:8080 -m 2` runs from `ghcr.io/zaproxy/zaproxy:stable` with the digest recorded (ZAP, *Baseline Scan*)
   **Then** `7.1-qa.md` lists each alert with its rule ID, risk and either a triage reason or a finding; the known alerts are triaged by rule ID with their earlier reasons: 10049 Storable and Cacheable Content, 10109 Modern Web Application (a WARN in every baseline since story 2.5) and 90004 Cross-Origin-Embedder-Policy; a High or Critical alert with no triage fails the story
   **And** the record states the scan's scope: an unauthenticated passive scan of what the spider reaches, so the signed-in screens are covered by the journeys and per-story QA, not by ZAP

## Boundaries

- Must not change: what any existing spec asserts; moving the registration helper changes no behaviour.
- No reset endpoint, test-only database role, second Playwright config or second package; no application code.
- A run with no `--project` would put both projects on one database; it is not a QA run.
- No traces, videos or screenshots of signed-in pages are kept (they would capture cookies and personal data); the config's `trace: 'off'` stays.
- Not here: UJ-2 and UJ-5 (story 7.2); the CI job (story 7.3).

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- In a throwaway copy, run `npm run journeys` without `docker compose down -v` after a first run: UJ-1 fails at linking, because D is already linked.
- In a throwaway copy, stop the worker before the raise: criterion 3's `Sent` poll fails at its 30-second timeout.
- Re-run the ZAP command from `7.1-qa.md` against the running stack: the same alerts.

## Build facts

- Owner: qa (qa agent).
- Flags: none.
- Lane: QA.
- Mechanisms and sources: Playwright projects, `testMatch` and `testIgnore` (<https://playwright.dev/docs/test-projects>, <https://playwright.dev/docs/test-configuration>); `page.waitForResponse` (<https://playwright.dev/docs/network>); `expect.poll` (<https://playwright.dev/docs/test-assertions#expectpoll>); `@axe-core/playwright` with WCAG tags (<https://github.com/dequelabs/axe-core/blob/develop/doc/API.md#options-parameter>); WCAG 2.2 SC 1.4.10, 2.1.1, 2.1.2, 2.4.3, 2.4.7, 3.3.1 (<https://www.w3.org/TR/WCAG22/>); ZAP baseline scan (<https://www.zaproxy.org/docs/docker/baseline-scan/>); CloudEvents 1.0 `id` (<https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md>); OpenID Connect RP-Initiated Logout 1.0 (<https://openid.net/specs/openid-connect-rpinitiated-1_0.html>).
- Packages: none (the pinned `@playwright/test` 1.64.1 and `@axe-core/playwright` 4.14.0).
- Touches:
  - `e2e/playwright.config.ts` (the `journeys` and `regression` projects)
  - `e2e/package.json` (the `journeys` and `test` scripts)
  - `e2e/tests/journeys/uj-1.spec.ts` (new)
  - `e2e/tests/support.ts` (`registerLogin` with its prefix argument, `mailpitSearch`, `messageText`, `pause`, `smsTo`, moved), `e2e/tests/linking-verify.spec.ts` (imports them, passes `qa62`)
  - `e2e/tests/journeys/screens.ts` (the journeys' axe helpers; added by whichever of 7.1 and 7.2 lands first, reused by the other)
  - `docs/verification/7.1-qa.md`

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- prd — docs/prd/prd.md, UJ-1, FR-29, NFR-CODE-6, NFR-SEC-8, NFR-TEST-5
- architecture — docs/architecture/architecture.md, AD-6, AD-17, AD-19, AD-30
- experience — docs/ux/EXPERIENCE.md, Copy deck, Accessibility Floor
- verification — docs/verification/7.1-challenge.md, docs/verification/7.1-prebuild-review.md, docs/verification/e2e-data-budget.md

## Notes

- Decision: 2026-10-26 — Gate 2: QA resets customer D by starting a fresh stack per QA run (`docker compose down -v`, then `up --wait`), not through a test-only database role; `qa_readonly` stays read-only for inspection.
- Decision: 2026-10-27 — WCAG 2.2 AA is checked by axe plus a recorded manual keyboard pass, not proved by axe alone (R40).
- Decision: 2026-10-30 (Sofia, 7.1 challenge) — the journeys and the regression specs are two Playwright projects, each run on its own fresh stack. This keeps AD-30's "each QA run starts on a fresh stack": a QA run is one project's run. It is needed because `linking-verify.spec.ts` and UJ-1 both link customer D, who links once per database.
- Decision: 2026-10-30 (Sofia, 7.1 challenge) — the hosted sign-up and verification pages are scanned by axe and their violations recorded, not failed on: they are the identity provider's pages (AD-6), and R40 binds the customer portal.
- Decision: 2026-10-30 (Sofia, 7.10 moves last) — this story no longer waits on 7.10; the production readiness review now runs after 7.1 to 7.7.
- Waits on 3.12 for the dispute detail and its timeline (UJ-1's climax).
- Risk high: the main demo path; beyond the criteria, the run is repeated twice, each on a fresh stack, to prove the D reset.
- Not a prerequisite of this story: the regression project's fixture additions `TXN-A-0031` to `TXN-A-0048` (`e2e-data-budget.md`) are backend work for the regression suite; UJ-1 uses only customer D's rows.
- Decision: 2026-10-30 (Sofia, pre-build review 1) — after sign-out the hosted login may show an account picker, because Zitadel remembers the username; the check is that the password is asked again, as `bff-sign-in-and-out.spec.ts` already does.
- Decision: 2026-10-30 (Sofia, pre-build review 2) — `registerLogin` takes an address prefix rather than moving unchanged, so UJ-1's addresses read `uj1-…` and `linking-verify.spec.ts` keeps `qa62-…`; the password stays the helper's dev-only one.
- Decision: 2026-10-30 (Sofia, pre-build review 4) — the journeys share `e2e/tests/journeys/screens.ts` for the axe helpers; the five regression specs keep their private copies (Boundaries: no change to an existing spec); folding them into one is refactor-sweep work.
