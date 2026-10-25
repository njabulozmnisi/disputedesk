---
name: build-story
description: Procedure for building one DisputeDesk story test-first in a worktree and reporting it as built or blocked. Used by backend-builder and frontend-builder.
---

# Build a story

You get a story file from `docs/tickets/` with Given/When/Then criteria tagged to requirement IDs.

1. **Read first.** The story, the requirements it covers in `docs/initiative-disputedesk.md`, the relevant parts of `docs/architecture/architecture.md`, `docs/glossary.md`, and the code you'll touch.
2. **Test first.** For each criterion, write the failing test, then the smallest code that passes it.
   - Backend: domain unit tests for decisions (state machine, SLA, validation) with an injected `TimeProvider`; integration tests against real Postgres via Testcontainers for plumbing. Take the `testcontainers` lock: `scripts/test-lock.sh run testcontainers <story> -- dotnet test ...`.
   - Frontend: Vitest against the Orval-generated mocks.
3. **Run every check you can.**
   - Backend: `dotnet build` (warnings are errors), unit, integration and architecture tests, the OpenAPI drift check.
   - Frontend: Vitest, `tsc --noEmit`, lint.
4. **Developer smoke check, before reporting `built`.**
   - Backend: start the API, its Postgres and the shared dev Zitadel; sign in a seeded user with `scripts/dev-login.sh`; call the endpoints with the committed `backend/http/*.http` requests; confirm the rows landed. No browser, no shared lock except during sign-in.
   - Frontend: run the UI against the real API and do one headless Playwright happy path with a screenshot, under `scripts/test-lock.sh run stack <story> -- ...`.
5. **Stop rules.** After two failed attempts at the same problem, return `blocked`. Never weaken, skip or delete a test to make it pass. Never commit, push, or mark the story done.

## Report

End with the status contract:

```json
{
  "status": "built",
  "worktree": "/abs/path",
  "criteria": [{"id": "AC1", "requirement": "FR-3", "test": "DisputeLifecycleTests.Withdraw_after_resolution_is_rejected"}],
  "smoke": "what you ran and what you saw",
  "assumptions": [],
  "open_questions": [],
  "conflicts": []
}
```
