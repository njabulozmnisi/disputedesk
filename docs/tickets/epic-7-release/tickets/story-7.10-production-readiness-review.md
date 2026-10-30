---
id: 10
type: story
title: "Production readiness review"
parent: epic-7-release
covers: [R9, R11, R28, R30, R31, R32, R34, R35, R36]
after: [2.6, 2.8, 3.7, 3.12, 4.1, 4.2, 5.3, 5.4, 6.4, 6.6, 1, 2, 3, 6, 7, 11, 4, 5]
hitl: true
risk: high
refined: false
---

# Production readiness review

## Description

An independent, evidence-based review of the whole system, run last, once the journeys, CI, the records, the README and runbook and the AWS code have merged, modelled on the Production Readiness Review in Google's SRE practice. Per-story checks cannot see cross-story weaknesses; this review does. The reviewer works read-only at one recorded commit and fills one checklist matrix; every verdict carries evidence the reviewer produced or read itself. A person then decides each finding in a separate disposition step. What cannot be observed without an AWS account is marked "not yet assessable" with the gate that closes it, never "met".

## Requirements

Verify: `docs/verification/7.10-prr.md` exists with the recorded commit SHA, the checklist matrix of criterion 2 with no empty verdict, the ranked findings of criterion 8, and its Disposition section filled by a person; the supervisor's three re-runs of "met" evidence are recorded beside the matrix.

Inputs: none at runtime. Bounds: the commit is one SHA on main, recorded before the review starts; the checklist is OWASP ASVS 5.0.0 plus the PRD sections named below; the evidence stack is a fresh, isolated stack (criterion 3).

1. **An independent reviewer at one commit** (R35)
   **Given** main at a recorded SHA, after every story this one waits on has merged
   **When** the review runs
   **Then** it runs on a model family different from the builders (GPT through GitHub Copilot CLI, read-only), briefed with the checklist, this story and the repository at that SHA, never with any agent's conclusions or earlier review files
   **And** the supervisor re-runs the evidence of three "met" rows it picks, at the same SHA, and records the result; a re-run that disagrees becomes a finding

2. **One checklist matrix** (R35; NFR-SEC-1 to NFR-SEC-11)
   **Given** `7.10-prr.md`
   **When** the matrix is read
   **Then** each row has: requirement ID (an ASVS 5.0.0 ID such as `V7.2.1`, or a PRD ID such as `NFR-RES-2`), applicability with a one-line reason, verdict (met, not met, not applicable, or not yet assessable), evidence reproducible at the recorded SHA (a test name, a command and its output, or a file and line), and a finding ID for every "not met"
   **And** the security denominator is every Level 1 and Level 2 requirement of OWASP ASVS 5.0.0, every chapter; a whole chapter outside the system (for example V5 file handling: no uploads; V17 WebRTC) is one "not applicable" row with its reason
   **And** "not yet assessable" names the gate that closes it (for example the first deploy once the AWS account exists, through story 7.5's written procedure); absent evidence is never "met" or "not applicable"

3. **Data and domain on a fresh, isolated stack** (R28, R30; NFR-DATA-1 to NFR-DATA-7, AD-11, AD-13, AD-15, AD-22, AD-30)
   **Given** the EF Core model, the migrations, the AD-11 ownership filter, and a fresh stack (`docker compose down -v`, then `up --wait`) brought up in a throwaway copy under `scripts/test-lock.sh run stack`, as one locked script that also runs every query, on the same South African day as its seeding (AD-30)
   **When** they are read as one schema
   **Then** the review confirms keys, unique indexes, the concurrency mechanisms (the unique `(DisputeId, Version)` on `StoredEvents`, the `IdempotencyKeys` lease) and the ownership filters are consistent across every entity
   **And** for every seeded dispute it compares every column of its `Disputes` row with the state its replayed stream gives, reusing the existing replay test's output where it already compares all columns, else listing the columns it does not compare as a finding
   **And** it lists any table or column no story reads or writes, excluding the framework-owned ones it names (`DataProtectionKeys`, `__EFMigrationsHistory`)

4. **Reliability and the named races** (R11, R28, R36; NFR-RES-1 to NFR-RES-4)
   **Given** timeouts, retries, circuit breaking, idempotency, the outbox relay and shutdown
   **When** each is checked
   **Then** each has a named test or a recorded run that proves it
   **And** each of these races has a named test or is "not met": two Api instances cold-starting together on an empty key store (the Data Protection key ring), two instances seeding at once, two raises with one `Idempotency-Key`, two raises on one transaction, two actions on one dispute (a version clash), the sweep and an operations action on one dispute, parallel OTP verifications, two logins linking one customer, and a relay leader handover
   **And** the key-ring race is checked against Microsoft Learn's Data Protection key management and shared key ring guidance, by starting two Api instances at once on an empty key store several times; the review records what it saw, never loosens the test, and prescribes no custom key-refresh mechanism on one flaky run

5. **Observability and operations** (R36; NFR-OBS-1 to NFR-OBS-4, NFR-RES-4)
   **Given** health checks, traces, metrics, logs and `docs/runbook.md` (story 7.7)
   **When** core banking and then Kafka are stopped on the criterion 3 stack, inside the same locked script (`infra/smoke.sh` is not used, because it starts with `docker compose down -v`)
   **Then** each failure's row shows where an operator sees it (the health endpoint, the Aspire Dashboard trace or metric, the log line) and which runbook procedure applies, or "not met" where the runbook has none
   **And** CloudWatch and X-Ray export (AD-29) are "not yet assessable" with the gate of criterion 2, checked only as far as the collector config and story 7.4's local checks go

6. **Code quality: glossary and suppressions** (R34; NFR-CODE-1, NFR-CODE-3)
   **Given** `backend/src`, `frontend/src`, `contracts/`, `infra/` and the problem pages under `docs/problems/`
   **When** they are searched
   **Then** each glossary _Avoid_ synonym found in a domain identifier, a route, a contract field or customer-facing copy is a finding with its file and line; a match in third-party, generated (`frontend/src/api/generated/`, EF Core migrations), tooling or test-only code, or a word used in its other sense (for example "account" for a bank account), is not, and the matrix says which exclusions it applied
   **And** each suppression (`#pragma warning disable`, `[SuppressMessage]`, `<NoWarn>`, `eslint-disable`, `@ts-expect-error`, `@ts-ignore`) without a written reason on the same or the preceding line is a finding with its file and line

7. **Named items** (R35, R36)
   **Given** the named items in Notes
   **When** the review runs
   **Then** each gets a matrix row with a verdict and evidence, and a finding ID if it is not met

8. **Findings, then a person's disposition** (R35)
   **Given** the findings
   **When** the reviewer reports
   **Then** each finding is ranked once, high, medium or low by business impact if wrong, with its evidence and the matrix rows it affects; the reviewer proposes fix, production path or accept for each, but decides nothing
   **And** in a separate Disposition section a person records, per finding, fix (builder work through the normal story flow), production path (an entry added to `docs/production-path.md` through a docs-only change) or accept, with the date; the story stays open while any disposition is pending, and a high finding left unfixed blocks epic 7's Done unless the person records an accept with a reason

## Boundaries

- Read-only for the reviewer: it changes no code, test, configuration or record; fixes go through the normal story flow.
- No AWS command is run (the account does not exist; decided 2026-10-30).
- The shared stack is never used for evidence; only the throwaway copy's locked session (Notes, the `stack` lock item).

## Supervisor checks

- Re-run three "met" rows' evidence at the recorded SHA (criterion 1).
- Confirm the recorded SHA is on main after the last story this one waits on.

## Build facts

- Owner: supervisor runs the review; any fixes become builder work through the normal flow.
- Flags: high-stakes (whole-system security review).
- Lane: none (read-only review), last in epic 7, after 7.1 to 7.7, 7.11, 7.4 and 7.5.
- Mechanisms and sources: Production Readiness Review (Google, *Site Reliability Engineering*, "The Evolving SRE Engagement Model", <https://sre.google/sre-book/evolving-sre-engagement-model/>); OWASP ASVS 5.0.0 (<https://owasp.org/www-project-application-security-verification-standard/>); ASP.NET Core Data Protection key management and configuration (<https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-management?view=aspnetcore-10.0>, <https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0>).
- Packages: none.
- Touches: `docs/verification/7.10-prr.md`.

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-11, AD-13, AD-15, AD-17, AD-22, AD-25, AD-28, AD-29, AD-30
- prd — docs/prd/prd.md, §5
- glossary — docs/glossary.md
- verification — docs/verification/7.10-challenge.md

## Notes

- Decision: 2026-10-27 — added after the workflow review: per-story checks are strong, but nothing checked the system as a whole; every reviewer except the verifier shares the builders' model family.
- Decision: 2026-10-29 — the glossary and suppression check runs here (criterion 6); the project has no separate closing refactor-sweep story.
- Named item (from 2.4 verification round 3 and review, 2026-10-28): `SignInTests.Session_CookieFromOneApiInstance_IsAcceptedByAnother` failed once in a full run and passed in isolation. Suspected cause, unproven: two Api instances starting on an empty Data Protection key store each create a key, and one instance's cached key ring lacks the other's key; if so it is also a production race when two instances start together. Check it against the Microsoft Learn Data Protection key-management docs; never loosen the test (criterion 4).
- Named item (from 2.5 review L2 and N1, 2026-10-28): `infra/smoke.sh`'s 429 loop sends 40 requests and can miss the limit on a slow host (it stops at the first 429, so more requests cost nothing), and under `pipefail` a missing `cache-control` line exits without `SMOKE FAIL` (wrap that `grep` with `|| true`).
- Named item, tooling debt (from 3.2, decided 2026-10-29): the `stack` lock in `scripts/test-lock.sh` covers one command, so another lane can restart the shared stack between a lane's `docker compose up` and its requests. A session-long `stack` lock (bring-up, requests, queries, stop) is tooling debt; until it exists, each brief runs a stack session as one locked script (criteria 3 and 5).
- Decision: 2026-10-30 (Sofia, epic 6 parallel re-slice) — waits on 6.6 as well as 6.4: forwarded-header trust moved from 6.3 to the new story 6.6, which 6.4 does not wait on.
- Decision: 2026-10-30 (human, 7.10 challenge) — 7.10 moves to run last, after 7.1 to 7.7, 7.4 and 7.5 (and 7.11, split from 7.4), so the final verdict covers every component it claims to assess. The 2026-10-29 order (7.10 first) is replaced; the epic's build order and `docs/tickets/README.md` are fixed to match.
- Decision: 2026-10-30 (Sofia, refine) — the reviewer's findings and a person's disposition are two steps (criterion 8); "not yet assessable" names its closing gate (criterion 2); the security denominator is every ASVS 5.0.0 Level 1 and Level 2 requirement (criterion 2); the race scenarios are enumerated (criterion 4); the evidence stack is a fresh throwaway copy in one locked session (criterion 3).
