---
id: 7
type: story
title: "README, runbook and onboarding"
parent: epic-release
covers: [R39, R28]
after: [5.1, 3]
hitl: false
risk: low
---

# README, runbook and onboarding

## Description

Writes the README that opens with `docker compose up`, plus a short runbook and a short onboarding page; docs only.

## Requirements

Verify: The README sections below exist and every command in them runs on a fresh clone; the ten-area map links to real paths.

1. **README** (R39; NFR-DOC-1, NFR-DOC-4, NFR-DOC-5, FR-29)
   **Given** the README
   **When** it is read
   **Then** it opens with `docker compose up`, then build, run and test steps, the demo logins for A and B and the synthetic details for C and D, driving the operations endpoints from Scalar, the research simplifications, and ends with the ten-area map

2. **Runbook** (R28, R39; NFR-RES-4)
   **Given** `docs/runbook.md`
   **When** it is read
   **Then** in one short page it covers releasing a failed outbox row, stopping core banking to show the 503 and resetting with `docker compose down -v`, and the README links it under a Runbook heading

3. **Onboarding** (R39; NFR-DOC-1)
   **Given** `docs/onboarding.md`
   **When** it is read
   **Then** it lists prerequisites, the repo map and the story flow in one page

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Fresh clone on a clean machine: the README's first command brings the stack up.

## Build facts

- Owner: docs (docs agent, not a builder).
- Flags: none.
- Lane: docs.
- Touches: `README.md`; `docs/runbook.md`; `docs/onboarding.md`.

## References

- parent — docs/tickets/epic-release/epic-release.md

## Notes

- Gold-plating watch: the PRD asks for a README runbook; the separate runbook and onboarding page stay short.
- Decision: 2026-10-26 — Gate 2: owner changes to `docs`, a docs agent rather than a builder; runbook and onboarding stay short.
