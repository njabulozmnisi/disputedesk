---
id: 7
type: story
title: "Problem types resolve to documentation pages"
parent: epic-2-transactions
covers: [R30]
after: [3]
hitl: false
risk: medium
---

# Problem types resolve to documentation pages

## Description

Moves every problem `type` from the tag URI to the resolvable URL of its page under `docs/problems/` (AD-3; RFC 9457 §3.1.1 encourages resolvable type URIs). Built after 2.3 and before 2.4, so the SPA routes on the final form from its first story.

## Requirements

Verify: The ProblemDetails and slug catalogue tests pass; every slug in the catalogue has its page under `docs/problems/`.

1. **Resolvable type for every slug** (R30; NFR-API-2)
   **Given** every slug in the Contracts catalogue
   **When** a problem body is written for it
   **Then** `type` is `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/<slug>.md` (RFC 9457 §3.1.1)
   **And** no `tag:` URI remains in backend code, tests or `openapi.json`

2. **One page per slug** (R30; NFR-API-2)
   **Given** `docs/problems/`
   **When** it is read
   **Then** it holds one `<slug>.md` per catalogue slug, each giving the title, the HTTP status, when it happens and what the client should do, in short plain sentences

3. **The catalogue test checks the pages** (R30; NFR-API-2, NFR-TEST-4)
   **Given** the test that gathers the slug catalogue
   **When** it runs
   **Then** it fails for any slug whose page is missing under `docs/problems/`

4. **Merged code and contract updated** (R30; NFR-API-1)
   **Given** the code merged by 2.2 and 2.3 (`ProblemType` and the tests that assert `type`) and `backend/openapi.json`
   **When** the change is built
   **Then** they use the new `type` values and `openapi.json` is regenerated with no drift

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- After merge, `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/not-found.md` opens the not-found page.

## Build facts

- Owner: backend (backend-builder).
- Flags: API contract change.
- Lane: A.
- Touches: `backend/src/DisputeDesk.Contracts/Problems/ProblemType.cs`; docs/problems/ (new: one page per slug); `backend/tests/DisputeDesk.Api.IntegrationTests/ProblemDetailsTests.cs`; `backend/tests/DisputeDesk.Api.IntegrationTests/Bff/AntiforgeryTests.cs`; the slug catalogue test; `backend/openapi.json`.

## References

- parent — docs/tickets/epic-2-transactions/epic-2-transactions.md
- architecture — docs/architecture/architecture.md, AD-3

## Notes

- Decision: 2026-10-27 — problem types move from tag URIs to resolvable GitHub URLs (RFC 9457 §3.1.1); event `dataschema` values move the same way in AD-19, applied by the event stories (3.1, 3.4).
- The SPA routes on the slug, the last path segment of `type` without `.md` (AD-3).
- The supervisor writes the `docs/problems/` pages (docs are the supervisor's); the builder changes the code and tests and reads the pages' slugs.
- The pages resolve only while the repository is public.
