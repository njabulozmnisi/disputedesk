---
id: 3
type: story
title: "CI runs the journeys with ZAP and publishes images to GHCR"
parent: epic-7-release
covers: [R33, R35]
after: [2]
hitl: false
risk: medium
---

# CI runs the journeys with ZAP and publishes images to GHCR

## Description

Adds the `e2e` job (Compose up, Playwright with axe, ZAP baseline) and the main-only `publish` job (AD-32).

## Requirements

Verify: Workflow lint passes; reading `ci.yml` shows the `e2e` job on pull requests and the `publish` job gated to main with `packages: write` only there.

1. **E2E job** (R33, R35; NFR-TEST-5, NFR-TEST-6, NFR-SEC-8)
   **Given** a pull request
   **When** CI runs
   **Then** the `e2e` job starts the stack with Compose, runs the Playwright journeys with axe and the ZAP baseline, and fails on any failure or untriaged high or critical finding

2. **Publish on main** (R33; NFR-INFRA-2)
   **Given** a push to main
   **When** CI runs
   **Then** only the `publish` job has `packages: write` and pushes `api`, `worker`, `gateway`, `core-banking` and `provision` tagged `sha-<short>`, never `latest`

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- Check the first green PR run and the GHCR package list after merge.

## Build facts

- Owner: backend (backend-builder).
- Flags: none.
- Lane: A.
- Touches: .github/workflows/ci.yml (e2e, publish); `.zap/` (triage file).

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md
