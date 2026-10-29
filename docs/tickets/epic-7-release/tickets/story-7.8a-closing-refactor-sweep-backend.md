---
id: "8a"
type: story
title: "Closing refactor sweep: backend"
parent: epic-7-release
covers: [R34, R33]
after: [2, 4, 6, 7, 9, 5.3, 5.4, 6.4, 3.7]
hitl: false
risk: medium
---

# Closing refactor sweep: backend

## Description

7.8a. The first of two sequential sweeps after everything else: in `backend/`, `contracts/` and `infra/`, remove duplication, dead code and unexplained suppressions and align glossary terms, with no behaviour change. The frontend sweep (7.8b) follows it.

## Requirements

Verify: Every suite, the contract checks and the E2E journeys stay green; the diff adds no feature and no package.

1. **No behaviour change** (R33; NFR-TEST-6)
   **Given** the sweep diff
   **When** every backend suite and the E2E journeys run
   **Then** all tests and the `openapi.json` and event contract checks pass unchanged, with no test weakened

2. **Glossary and suppressions** (R34; NFR-CODE-3, NFR-CODE-1)
   **Given** the codebase
   **When** it is searched
   **Then** no glossary-listed synonym remains in backend code or routes, and every suppression has a written reason

3. **No new abstraction or package** (R34; NFR-CODE-2, NFR-CODE-5)
   **Given** the sweep diff
   **When** it is reviewed
   **Then** it adds no package and no single-implementation interface

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- The Playwright journeys pass on the stack after the sweep.

## Build facts

- Owner: backend (backend-builder).
- Flags: none.
- Lane: A.
- Touches: `backend/`; `contracts/`; `infra/`; e2e/ (read-only unless a test name changes).

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md

## Notes

- Runs alone: no other builder lane is active.
- Decision: 2026-10-26 — Gate 2: the closing sweep is split into 7.8a backend (owner backend) and 7.8b frontend (owner frontend), run sequentially after everything else.
- Decision: 2026-10-27 — the standards items once listed here (deny by default, `AllowedHosts`, sign-out through the OIDC handler, `TotalRequestTimeout`, Production-environment tests, the testing convention) moved to story 7.9, and Data Protection keys at rest to 7.4; this sweep is again no behaviour change and waits on 7.9.
- Input (from 2.5 review, 2026-10-28): `infra/smoke.sh` 429 loop sends 40 requests and can miss the limit on a slow host; raise it (it stops at the first 429). Under `pipefail` a missing `cache-control` line exits without `SMOKE FAIL` (wrap the `grep` with `|| true`). In `SecurityHeadersTests`, rename `isApiResponse` to `isUnderApiPath`.
- Input (from 3.2 review R2, 2026-10-28): the stored 201 is serialised with a private copy of the JSON options, `CommitRows` holds another; AD-5 asks for one shared instance in Contracts used by both and by the Api's `ConfigureHttpJsonOptions`.
- Input (from 3.2, human decision 2026-10-29): the idempotency claim is raw SQL (`IdempotencyKeyClaims.ClaimAsync`, `INSERT … ON CONFLICT`). Research (`docs/research/ef-core-upsert/ef-core-upsert.md`) recommends replacing it with EF only: read the key, insert it or take it over by saving the tracked row; the unique index and the `LeaseId` concurrency token arbitrate races, and the claim rule moves into one C# method on `IdempotencyKey`. Covered by the existing idempotency integration tests; amend AD-16's "replaced in the same statement".
