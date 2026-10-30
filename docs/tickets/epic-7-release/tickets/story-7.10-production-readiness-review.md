---
id: 10
type: story
title: "Production readiness review"
parent: epic-7-release
covers: [R9, R11, R28, R30, R31, R32, R34, R35, R36]
after: [2.6, 2.8, 3.7, 4.1, 4.2, 5.3, 5.4, 6.4, 6.6]
hitl: true
risk: high
---

# Production readiness review

## Description

An independent, evidence-based review of the whole system before the QA journeys and the deploy, modelled on the Production Readiness Review in Google's SRE practice. Per-story checks cannot see cross-story weaknesses; this review does. Every claim needs evidence the reviewer produced or read itself.

## Requirements

Verify: `docs/verification/7.10-prr.md` exists with a verdict per checklist item, the evidence for each, and a ranked list of findings with a human decision recorded for each.

1. **Independent reviewer** (R35)
   **Given** the merged system on main
   **When** the review runs
   **Then** it runs on a model family different from the builders (GPT through Copilot CLI, read-only), briefed with the checklist and the repository, not with any agent's conclusions

2. **Security** (R9, R31, R32; NFR-SEC-1 to NFR-SEC-7)
   **Given** OWASP ASVS Level 2 as the checklist (authentication, session management, access control, input handling, error handling, data protection, configuration)
   **When** each area is checked across all merged stories
   **Then** each item is marked met, not met or not applicable, with the test, scan or command output that shows it

3. **Data and domain** (R28, R30)
   **Given** the EF Core model, migrations and the AD-11 ownership filter
   **When** they are read as one schema
   **Then** the review confirms keys, unique indexes, the concurrency mechanisms (the unique `(DisputeId, Version)` on `StoredEvents`, the `IdempotencyKeys` lease) and ownership filters are consistent across every entity, that replaying each seeded stream reproduces its `Disputes` row, and lists any table or column no story uses

4. **Reliability** (R11, R28, R36)
   **Given** timeouts, retries, circuit breaking, idempotency, the outbox relay and shutdown
   **When** each is checked
   **Then** each has a test or a recorded run that proves it, and every race (two instances, retried requests, concurrent actions) has a named test

5. **Observability and operations** (R36)
   **Given** health checks, traces, metrics and logs
   **When** a failure is simulated (core banking stopped, Kafka stopped)
   **Then** the review shows where an operator would see it and what the runbook says to do

6. **Code quality** (R34; NFR-CODE-1, NFR-CODE-3)
   **Given** `backend/`, `frontend/`, `contracts/` and `infra/`
   **When** they are searched
   **Then** every glossary-listed synonym in code, routes or copy and every suppression without a written reason is listed as a finding with its file and line

7. **Named items** (R35, R36)
   **Given** the open follow-ups listed in Notes
   **When** the review runs
   **Then** each gets a verdict with evidence and a human decision: fix (builder work through the normal flow), production path, or accept

8. **Findings to the human** (R35)
   **Given** the findings
   **When** the review ends
   **Then** each is ranked with severity and evidence, and the human decides fix, defer to the production path, or accept

## Supervisor checks

- Spot-check three "met" items by re-running their evidence.

## Build facts

- Owner: supervisor runs the review; any fixes become builder work through the normal flow.
- Flags: high-stakes (whole-system security review).
- Lane: none (read-only review), after the last builder stories and before 7.1.
- Touches: docs/verification/7.10-prr.md.

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md

## Notes

- Decision: 2026-10-27 — added after the workflow review: per-story checks are strong, but nothing checked the system as a whole; every reviewer except the verifier shares the builders' model family.
- Decision: 2026-10-29 — the glossary and suppression check runs here (criterion 6); the project has no separate closing refactor-sweep story.
- Named item (from 2.4 verification round 3 and review, 2026-10-28): `SignInTests.Session_CookieFromOneApiInstance_IsAcceptedByAnother` failed once in a full run and passed in isolation. Suspected cause, unproven: two Api instances starting on an empty Data Protection key store each create a key, and one instance's cached key ring lacks the other's key; if so it is also a production race when two instances start together. Check it against the Microsoft Learn Data Protection key-management docs; never loosen the test.
- Named item (from 2.5 review L2 and N1, 2026-10-28): `infra/smoke.sh`'s 429 loop sends 40 requests and can miss the limit on a slow host (it stops at the first 429, so more requests cost nothing), and under `pipefail` a missing `cache-control` line exits without `SMOKE FAIL` (wrap that `grep` with `|| true`).
- Named item, tooling debt (from 3.2, decided 2026-10-29): the `stack` lock in `scripts/test-lock.sh` covers one command, so another lane can restart the shared stack between a lane's `docker compose up` and its requests. A session-long `stack` lock (bring-up, requests, queries, stop) is tooling debt; until it exists, each brief runs a stack session as one locked script.
- Decision: 2026-10-30 (Sofia, epic 6 parallel re-slice) — waits on 6.6 as well as 6.4: forwarded-header trust moved from 6.3 to the new story 6.6, which 6.4 does not wait on.
