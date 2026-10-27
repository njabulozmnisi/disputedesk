---
id: "8b"
type: story
title: "Closing refactor sweep: frontend"
parent: epic-7-release
covers: [R34, R33]
after: ["8a"]
hitl: false
risk: medium
---

# Closing refactor sweep: frontend

## Description

7.8b. The second sequential sweep, after the backend sweep: in `frontend/`, remove duplication, dead code and unexplained suppressions and align glossary terms in code and copy, with no behaviour change.

## Requirements

Verify: `npm run build`, `tsc --noEmit`, ESLint, Prettier, Vitest and the E2E journeys stay green; the diff adds no feature and no package.

1. **No behaviour change** (R33; NFR-TEST-6)
   **Given** the sweep diff
   **When** every frontend suite and the E2E journeys run
   **Then** all tests pass unchanged, with no test weakened, and `frontend/src/api/generated/` is untouched apart from regeneration from `openapi.json`

2. **Glossary and suppressions** (R34; NFR-CODE-3, NFR-CODE-1)
   **Given** `frontend/`
   **When** it is searched
   **Then** no glossary-listed synonym remains in code or copy, and every suppression has a written reason

3. **No new abstraction or package** (R34; NFR-CODE-2, NFR-CODE-5)
   **Given** the sweep diff
   **When** it is reviewed
   **Then** it adds no package

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- The Playwright journeys pass on the stack after the sweep.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Touches: `frontend/`; e2e/ (read-only unless a test name changes).

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md

## Notes

- Runs alone, after 7.8a is merged: no other builder lane is active.
- Decision: 2026-10-26 — Gate 2: split from the closing refactor sweep (7.8) as 7.8b, owner frontend.
