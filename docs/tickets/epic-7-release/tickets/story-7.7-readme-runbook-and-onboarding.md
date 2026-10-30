---
id: 7
type: story
title: "README, runbook and onboarding"
parent: epic-7-release
covers: [R39, R28]
after: [5.1, 5.5, 5.3, 3]
hitl: false
risk: low
refined: false
---

# README, runbook and onboarding

## Description

Writes three short documents, docs only: the README (how to run, build and test the system, the seed identities, the operations endpoints through Scalar, the research simplifications and the ten-area map), `docs/runbook.md` (three operator procedures, the first of them the release of a `Failed` outbox row under AD-22's one operator exception) and `docs/onboarding.md` (prerequisites, the repo map and the story flow). No new endpoint, script or documentation framework; every procedure uses commands and behaviour that already exist.

## Requirements

Verify: the docs agent runs every command in the three documents, in order, on a fresh clone of the commit under review with Docker running and port 8080 free, and records each command with its exit status (and, for the runbook, the observed responses and rows) in `docs/verification/7.7-run.md`; every relative link in the three documents resolves to a file in the repo (a link check over the Markdown, for example `npx markdown-link-check@<pinned> --offline`, or a plain `grep` plus `test -e` loop); the README's sections appear in the order of criterion 1.

Inputs: none at runtime (docs only). Bounds that apply to the text:
- "Short": `docs/runbook.md` and `docs/onboarding.md` each at most 150 lines; the README at most 300 lines (NFR-DOC-1; the epic's Gate 2 decision).
- Commands are copied exactly as run; none takes a secret on its command line (the token comes from `scripts/operations-token.sh` inside a command substitution).

1. **README: run, build and test** (R39; NFR-DOC-1, FR-29)
   **Given** `README.md`
   **When** it is read top to bottom
   **Then** it opens with one paragraph saying what DisputeDesk is, then the prerequisites (Docker running with Compose v2, port 8080 free, the other host ports movable as `infra/README.md` says), then `docker compose up --wait` as the first command, with a note that plain `docker compose up` stays in the foreground, so later commands run in a second terminal (Docker, *docker compose up*, `--wait`)
   **And** then the app and identity origins (`http://localhost:8080`, `http://auth.localhost:8080`), Mailpit and the Aspire Dashboard, linked to `infra/README.md` for the rest
   **And** then the build and test commands exactly as CI runs them: in `backend/` `dotnet build DisputeDesk.slnx` and `dotnet test --solution DisputeDesk.slnx`; in `frontend/` `npm ci`, `npm run typecheck`, `npm run lint`, `npm test`; in `e2e/` `npm ci`, `npx playwright install chromium`, `npm run journeys` and `npm test` (the two Playwright projects of story 7.1), each against a fresh stack
   **And** then the seed identities from `fixtures/local.json` (FR-29): the demo logins of customers A and B (username and the dev-only fixture password), customer C's login, customer C's and D's synthetic ID and account numbers and what each shows (C fails the SIM-swap check; D has no fixture login, so a new sign-up links to D), the SLA demo dispute on `TXN-A-0011` that the sweep escalates a few minutes after start-up, and a note that the seed's dates are relative to start-up and a reset needs `docker compose down -v` (architecture, Deferred: seeded snapshots after a day boundary)

2. **README: operations through Scalar** (R39; NFR-DOC-4, FR-21 to FR-24, FR-30)
   **Given** the README's Operations section
   **When** a reader follows it on a running stack
   **Then** it gets a token with `scripts/operations-token.sh`, opens `http://localhost:8080/scalar/`, pastes the token into the `bearer` scheme, and calls the four operations routes in order on one seeded Submitted dispute of customer A: `GET /api/v1/operations/disputes/{disputeId}` (200), `POST /api/v1/operations/disputes/{disputeId}/start-review` (200, Under review), `POST …/escalate` (200), `POST …/resolve` with an outcome (200, Resolved); and one refusal: a second `resolve` answers 409 `action-not-allowed` (FR-24); each step names the expected status code and problem `type` (AD-3)
   **And** the same four calls are given as `curl` lines with `-H "Authorization: Bearer $(scripts/operations-token.sh)"`, as the script's header shows, and the section says how the reader finds a dispute ID, since seeded IDs are generated at seeding: open the dispute in customer A's history in the SPA and copy the GUID from `/disputes/<disputeId>`, or query `SELECT "Id" FROM "Disputes" WHERE "TransactionId" = '<transaction ID>';` through `qa_readonly`
   **And** it states that Scalar holds no credential, is served only when `Scalar:Enabled` is true, and stands in for another team's operations tool (NFR-DOC-4; AD-26)

3. **README: what is simplified, and the ten-area map** (R39; NFR-DOC-1, NFR-DOC-5, FR-8, NFR-ARC-3)
   **Given** the README's last two sections
   **When** they are read
   **Then** "Simplifications" states plainly the three NFR-DOC-5 points: the 90-day customer window is counted from the transaction date, against the scheme's 120 days from its processing date; the SLA is an internal trigger, not a promise (the National Financial Ombud Scheme gives banks 21 working days on referral, and the Conduct Standard asks for a reasonable time); the primary scheme rule texts were not read; each cites `docs/research/dispute-lifecycle/dispute-lifecycle.md`
   **And** it states that the linking rate limits live in memory in the one Api instance, reset on restart and do not add up across instances (FR-8, NFR-ARC-3), and links `docs/production-path.md` for everything deferred
   **And** the README ends with the ten-area map: one row per PRD §5 area (5.1 architecture and scalability to 5.10 documentation and communication), each linking at least one real path (a decision record, an architecture section, a test folder, a workflow or a config file) where the area is met

4. **Runbook: release a `Failed` outbox row** (R28, R39; NFR-RES-4, AD-17, AD-22)
   **Given** `docs/runbook.md`'s first procedure, and an outbox row the relay set `Failed` after 5 permanent refusals (its event ID is in the Worker's log line for the failed row and the `disputedesk.outbox.failed` metric has counted it)
   **When** an operator follows it
   **Then** the steps are, in this order:
   1. find the row and its blocked successors with the read-only role: `SELECT "Id", "DisputeId", "DisputeVersion", "Attempts", "LastError" FROM "OutboxMessages" WHERE "Status" = 'Failed';` and the same dispute's later rows by `"DisputeVersion"`, through `qa_readonly`;
   2. fix the cause from `LastError` outside the row (for example raise the topic's `max.message.bytes` with `rpk` for `MsgSizeTooLarge`); `Payload`, `Id` and every other column stay as they are, because the payload is the stored event's CloudEvent byte for byte (AD-14, AD-17); a row whose payload itself is the cause is not released (Boundaries);
   3. as the `disputedesk` role, in one PostgreSQL transaction: `BEGIN;` `UPDATE "OutboxMessages" SET "Status" = 'Pending', "Attempts" = 0, "NextAttemptAtUtc" = now() WHERE "Id" = '<event ID>' AND "Status" = 'Failed';` then `COMMIT;` only if psql printed `UPDATE 1`, otherwise `ROLLBACK;` (PostgreSQL, *Transactions* and *UPDATE*);
   4. verify: within a minute the row is `Sent`, and on `disputedesk.disputes` its record (key = the dispute ID, CloudEvent `id` = the event ID) has a lower offset than the record of the dispute's next version (`rpk topic consume` filtered by key), which proves the relay kept per-dispute order (AD-17)
   **And** the procedure says it is AD-22's one operator exception to "updated only by the relay", that it never sets a row to `Sent` and never deletes a row, and that the Worker needs no restart
   **And** the docs agent runs it once on a fresh stack: it makes one row fail by lowering the topic's `max.message.bytes` below a raise's payload size, raises one dispute and withdraws it (so the dispute has a blocked successor), waits for `Failed`, then follows the four steps and records the rows and offsets in `7.7-run.md`

5. **Runbook: core banking down, and the reset** (R28, R39; NFR-RES-1, FR-9, AD-21, AD-25)
   **Given** `docs/runbook.md`'s second and third procedures
   **When** an operator follows them
   **Then** "Core banking unavailable" stops the stub (`docker compose stop core-banking`), shows that a signed-in customer's transactions request `GET /api/v1/transactions` and a new raise each answer 503 with problem `type` ending `core-banking-unavailable` and the SPA shows its retry notice, that `/health/ready` stays healthy and the dispute history still loads (AD-21, AD-25), where the operator sees it (the Aspire Dashboard's failed spans and the resilience handler's logs), and that `docker compose start core-banking` restores it
   **And** "Reset the local stack" gives `docker compose down -v` then `docker compose up --wait`, under a warning that it is destructive and local only: it deletes every volume (Postgres, Redpanda, Zitadel and the generated credentials), and it is never a troubleshooting step on a shared or deployed environment (Docker, *docker compose down*, `--volumes`)
   **And** the README links the runbook under a heading "Runbook"

6. **Onboarding** (R39; NFR-DOC-1)
   **Given** `docs/onboarding.md`
   **When** it is read
   **Then** it holds three sections: prerequisites (Docker with Compose v2, the .NET SDK from `backend/global.json`, Node from `.nvmrc`), a repo map (one line per top-level folder and `docs/` subfolder, each a link that resolves) and the story flow in five lines that link `AGENTS.md` and `docs/process/agentic-sdlc.md` rather than restate them

## Boundaries

- Docs only: no code, script, endpoint, migration or configuration change. The runbook's release uses the existing schema and relay behaviour (AD-17), under AD-22's operator exception; it does not add a release command.
- A row whose `Payload` is itself refused (for example a record the broker can never take) stays `Failed`; the runbook says so and points at `docs/production-path.md`. The payload is never edited, because it is the stored event's CloudEvent and the topic's consumers deduplicate by its `id` (AD-17).
- No secret in any document: no token, client secret, OTP or password other than the dev-only fixture and admin values that `infra/README.md` already publishes.
- Not here: AWS deployment steps (story 7.5 writes them in `infra/terraform/README.md`); the README links that file.

## Supervisor checks

- Fresh clone on a clean machine: the README's first command brings the stack up, and the journeys command passes.
- Re-run runbook procedure 1 from `7.7-run.md` in a throwaway copy: the released row is `Sent` and its offset is below its successor's.

## Build facts

- Owner: docs (docs agent, not a builder).
- Flags: none.
- Lane: docs.
- Mechanisms and sources: Docker Compose `up --wait` and `down --volumes` (<https://docs.docker.com/reference/cli/docker/compose/up/>, <https://docs.docker.com/reference/cli/docker/compose/down/>); PostgreSQL transactions and `UPDATE` (<https://www.postgresql.org/docs/current/tutorial-transactions.html>, <https://www.postgresql.org/docs/current/sql-update.html>); Redpanda `rpk topic alter-config` and `rpk topic consume` (<https://docs.redpanda.com/current/reference/rpk/rpk-topic/>); OAuth 2.0 client credentials through the existing `scripts/operations-token.sh` (RFC 6749 §4.4); Scalar as served by story 5.5.
- Packages: none (a link checker, if used, runs through `npx` pinned to an exact version and is not added to any `package.json`).
- Touches: `README.md`; `docs/runbook.md` (new); `docs/onboarding.md` (new); `docs/verification/7.7-run.md` (new).

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-17, AD-21, AD-22, AD-25, AD-26, Deferred
- prd — docs/prd/prd.md, NFR-DOC-1, NFR-DOC-4, NFR-DOC-5, NFR-RES-4, FR-8, FR-29
- research — docs/research/dispute-lifecycle/dispute-lifecycle.md
- infra — infra/README.md
- verification — docs/verification/7.7-challenge.md

## Notes

- Risk low (proposed): docs only; a wrong runbook step could mislead an operator, so criterion 4 is run once on a fresh stack and the supervisor re-runs it.
- Decision: 2026-10-27 — the README states where rate-limit state lives (FR-8).
- Gold-plating watch: the PRD asks for a README runbook; the separate runbook and onboarding page stay short (at most 150 lines each).
- Decision: 2026-10-26 — Gate 2: owner changes to `docs`, a docs agent rather than a builder; runbook and onboarding stay short.
- Decision: 2026-10-30 (human, 7.7 challenge) — a `Failed` outbox row is released by one documented operator procedure: a targeted `UPDATE` in a PostgreSQL transaction that sets that row back to `Pending` (attempts reset) after the cause is fixed, verified by its event reaching the topic before its successor. AD-22 is amended with this one operator exception. The runbook gives the exact steps; no new code (criterion 4).
- Decision: 2026-10-30 (Sofia, refine) — the release also sets `NextAttemptAtUtc` to `now()` so the row is due at once, and leaves `LastError` as the record of the last refusal; the relay overwrites it if the row is refused again.
- Decision: 2026-10-30 (Sofia, refine) — waits on 5.3 as well: the README describes the SLA demo dispute the sweep escalates. 6.3 also edits `README.md` (the rate-limit line); 7.7 builds after it (through 3, 2, 1 and 6.4) and keeps that line.
