---
id: 10
type: story
title: "Production readiness review"
parent: epic-7-release
covers: [R9, R11, R28, R30, R31, R32, R35, R36]
after: [9]
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
   **Then** the review confirms keys, unique indexes, concurrency tokens and ownership filters are consistent across every entity, and lists any table or column no story uses

4. **Reliability** (R11, R28, R36)
   **Given** timeouts, retries, circuit breaking, idempotency, the outbox relay and shutdown
   **When** each is checked
   **Then** each has a test or a recorded run that proves it, and every race (two instances, retried requests, concurrent actions) has a named test

5. **Observability and operations** (R36)
   **Given** health checks, traces, metrics and logs
   **When** a failure is simulated (core banking stopped, Kafka stopped)
   **Then** the review shows where an operator would see it and what the runbook says to do

6. **Findings to the human** (R35)
   **Given** the findings
   **When** the review ends
   **Then** each is ranked with severity and evidence, and the human decides fix, defer to the production path, or accept

## Supervisor checks

- Spot-check three "met" items by re-running their evidence.

## Build facts

- Owner: supervisor runs the review; any fixes become builder work through the normal flow.
- Flags: high-stakes (whole-system security review).
- Lane: none (read-only review), after 7.9 and before 7.1.
- Touches: docs/verification/7.10-prr.md.

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md

## Notes

- Decision: 2026-10-27 — added after the workflow review: per-story checks are strong, but nothing checked the system as a whole; every reviewer except the verifier shares the builders' model family.
