# ad-005: QA data integrity on a fresh stack

**Status:** accepted (records AD-30, *QA reset*; Gate 2, 2026-10-26)

**Context.** QA checks each story's criteria on the running stack and traces every one through four hops: what the browser shows, the request and status on the network, the rows in the database, and the event on the topic (`qa-story` procedure). That trace only means something when the starting data is known. Customer D is the one customer who completes linking in the E2E journey (FR-29), so a run that starts with D already linked by an earlier run cannot prove linking. The data must also arrive through the real paths: seeded disputes are built through the aggregate and the full commit path, never by setting a status (AD-30, NFR-DATA-2).

**Decision.** Each QA run starts on a fresh stack, and QA only reads (AD-30, FR-29).

- A run begins with `docker compose down -v`, then `docker compose up --wait`. Removing the volumes means the stack starts again from the seed, and customer D starts unlinked (FR-29).
- There is no test-only database role that resets data and no reset endpoint (Gate 2, 2026-10-26).
- QA reads the database through the read-only `qa_readonly` role (AD-27, `infra/db/`): the dispute's stored event, its `Disputes` row and, for a published type, the outbox row with the same ID, written in one transaction (AD-14).
- One QA run holds the stack at a time, behind `scripts/test-lock.sh` (AGENTS.md, Dispatch rules).
- Passing walkthroughs become committed Playwright tests in `e2e/`, and the e2e regression project runs on its own fresh stack in each story's QA (NFR-TEST-6; decided 2026-10-30, story 7.3).

**Options rejected.**

- *A test-only database role that resets customer D:* rejected at Gate 2 (2026-10-26); the fresh stack resets D instead (FR-29).
- *A reset endpoint:* rejected at Gate 2 (AD-30).

**Consequences.**

- Every run starts from the same seeded state, so a database or topic check in one story's QA is never disturbed by another story's data.
- The seed is resolved against the start of the current South African day (AD-30), so seeded snapshots on a stack kept across a day boundary without `down -v` are a known demo limitation; the README says to reset with `docker compose down -v` (architecture, Deferred).
- A fresh stack per run costs a full start-up each time, and with one stack at a time per build machine (AGENTS.md, Dispatch rules) QA runs queue behind each other.
- Relay tests set up their own rows and never assume an empty outbox, because seeded disputes have outbox rows like any other (AD-17, AD-30).
