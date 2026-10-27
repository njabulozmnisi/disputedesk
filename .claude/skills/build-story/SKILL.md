---
name: build-story
description: Procedure for building one DisputeDesk story test-first in a worktree and reporting it as built or blocked. Used by backend-builder and frontend-builder.
---

# Build a story

The input is a story file from `docs/tickets/` with Given/When/Then criteria tagged to requirement IDs.

1. **Read first.** The story, the requirements it covers in `docs/initiative-disputedesk.md`, the relevant parts of `docs/architecture/architecture.md`, `docs/glossary.md`, and the code the story touches.
2. **Test first.** For each criterion, write the failing test, then the smallest, plainest code that passes it (AGENTS.md: stupid simple and logical). Before reporting, reread the whole diff and remove anything a first-time reader would not need.
   - Backend: domain unit tests for decisions (state machine, SLA, validation) with an injected `TimeProvider`; integration tests against real Postgres via Testcontainers for plumbing. Take the `testcontainers` lock: `scripts/test-lock.sh run testcontainers <story> -- dotnet test ...`.
   - Frontend: Vitest against the Orval-generated mocks.
3. **Run every available check.**
   - Backend: `dotnet build` (warnings are errors), unit, integration and architecture tests, the OpenAPI drift check.
   - Frontend: Vitest, `tsc --noEmit`, lint.
4. **Developer smoke check, before reporting `built`.**
   - Backend: start the API, its Postgres and the shared dev Zitadel; sign in a seeded user with `scripts/dev-login.sh`; call the endpoints with the committed `backend/http/*.http` requests; confirm the rows landed. No browser, no shared lock except during sign-in.
   - Frontend: run the UI against the real API and do one headless Playwright happy path with a screenshot, under `scripts/test-lock.sh run stack <story> -- ...`.
   - **Any container the builder starts directly** (a throwaway Postgres, a dashboard, the Compose stack) runs under `scripts/test-lock.sh run stack <story> -- ...`, because the stack binds fixed ports (8080, 8025, 18888, 4317, 4318, 5433, 19092) and other lanes share the Docker daemon. Testcontainers in tests use the `testcontainers` lock instead.
5. **Stop rules.** After two failed attempts at the same problem, return `blocked`. Never weaken, skip or delete a test to make it pass. Never commit, push, or mark the story done.

## Report

End with the status contract:

```json
{
  "status": "built",
  "worktree": "/abs/path",
  "criteria": [{"id": "AC1", "requirement": "FR-3", "test": "DisputeLifecycleTests.Withdraw_after_resolution_is_rejected"}],
  "smoke": "the commands run and what they showed",
  "assumptions": [],
  "open_questions": [],
  "conflicts": []
}
```
