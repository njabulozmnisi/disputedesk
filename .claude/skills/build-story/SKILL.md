---
name: build-story
description: Procedure for building one DisputeDesk story test-first in a worktree and reporting it as built or blocked. Used by backend-builder and frontend-builder.
---

# Build a story

The input is a story file, `docs/tickets/epic-<epic>-<slug>/tickets/story-<epic>.<story>-<slug>.md`, with Given/When/Then criteria tagged to requirement IDs. `<story>` below is its story ID, `<epic>.<story>` (for example `2.3`).

1. **Read first.** The story, the requirements it covers in `docs/initiative-disputedesk.md`, the relevant parts of `docs/architecture/architecture.md`, `docs/glossary.md`, and the code the story touches.
2. **Find the standard first.** For each mechanism the story needs (authorization, validation, errors, persistence, seeding, resilience, telemetry, security headers, UI patterns), look up the framework's documented approach on Microsoft Learn or the library's docs, or the industry standard (RFC, OWASP), and use it (AGENTS.md, "Use established standards; do not invent mechanisms"). Build a custom mechanism only where no standard exists, and say why in the report. Record each in the report's `standards`.
3. **Test first.** For each criterion, write the failing test, then the smallest, plainest code that passes it (AGENTS.md: stupid simple and logical). Before reporting, reread the whole diff and remove anything a first-time reader would not need.
   - Backend: domain unit tests for decisions (state machine, SLA, validation) with an injected `TimeProvider`; integration tests against real Postgres via Testcontainers for plumbing. Take the `testcontainers` lock: `scripts/test-lock.sh run testcontainers <story> -- dotnet test ...`.
   - Frontend: Vitest against the Orval-generated mocks.
   - **Input boundaries.** For each request input (query, route, header, body field), add a test for missing, wrong type, out of range and oversized (boundary-value analysis). Each must give its documented 4xx, never a 500.
   - **Logic down the pyramid.** Put decisions (rules, state changes, calculations) in fast domain unit tests; keep integration tests for wiring, the database and HTTP behaviour. Drive timeouts and delays with an injected `TimeProvider` (`FakeTimeProvider` in tests), never real waits.
4. **Self-check before the checks.** Read `.claude/skills/review-story/SKILL.md` and `.claude/skills/security-story/SKILL.md`, Known pitfalls included, and go through your own diff with each checklist as the reviewer and security agent will. Fix what you find; list anything you leave, with the reason, in the report's `assumptions`.
5. **Run every available check.**
   - Backend: `dotnet build` (warnings are errors), unit, integration and architecture tests, the OpenAPI drift check, `scripts/floor-guard.sh`.
   - Frontend: Vitest, `tsc --noEmit`, lint.
   - Tests that shell out (curl, bash): run that test project once in the Linux SDK container (`mcr.microsoft.com/dotnet/sdk:10.0`), because CI's ubuntu tools differ from macOS's.
   - Both: `scripts/secret-scan.sh` (gitleaks over the whole uncommitted change, untracked files included, as the pre-commit hook will see it). Fix a hit in the source, for example a low-entropy placeholder instead of an example UUID; never an allowlist entry.
   - **One full run with a results file,** on the final diff (again if the smoke check below leads to a change): from `backend/`, `scripts/test-lock.sh run testcontainers <story> -- dotnet test --solution DisputeDesk.slnx --report-xunit-trx --results-directory "$(git rev-parse --show-toplevel)/TestResults"`, and Vitest's JUnit reporter into the same `TestResults/`. Record the diff hash it covers (`scripts/diff-hash.sh`). The verifier reads these files instead of re-running the suite, so any later edit means a new full run.
   - **Breaks.** For each criterion, one deliberate break: a small patch that undoes the behaviour, written as `git diff` output so `git apply --check` takes it; the exact test method it must make fail (an infra test by its full name, `DisputeDesk.PlatformTests.<Class>.<Method>`, which `scripts/run-breaks.sh` runs over `infra/tests`; a script test by its command); and `"expect": "fails"`. The verifier runs each in a throwaway copy; a break whose test still passes is a high finding.
6. **Developer smoke check, before reporting `built`.**
   - Backend: start the API, its Postgres and the shared dev Zitadel; sign in a seeded user with `scripts/dev-login.sh`; call the endpoints with the committed `backend/http/*.http` requests; confirm the rows landed. No browser, no shared lock except during sign-in.
   - Frontend: run the UI against the real API and do one headless Playwright happy path with a screenshot, under `scripts/test-lock.sh run stack <story> -- ...`.
   - **Any container the builder starts directly** (a throwaway Postgres, a dashboard, the Compose stack) runs under `scripts/test-lock.sh run stack <story> -- ...`, because the stack binds host ports (8080, 8025, 18888, 4317, 4318, 5433, 19092) and other lanes share the Docker daemon. Every port but the gateway's 8080 can be moved with the variables in the `docker-compose.yml` header when another process holds it; never with a throwaway override file. Testcontainers in tests use the `testcontainers` lock instead.
7. **Write the report** to `docs/verification/<story>-build.json` in the worktree as well as ending with it.
8. **Stop rules.** After two failed attempts at the same problem, return `blocked`. Never weaken, skip or delete a test to make it pass. Never commit, push, or mark the story done.

## Known pitfalls

- A change that adds or changes infrastructure files (Terraform, Dockerfiles, Compose) runs `scripts/security-local.sh`, the Security workflow's Trivy filesystem and config scan, before reporting (7.5).
- A change that ends a session ends it by a document navigation (`location.replace` to the session-ended screen, or the logout form post), so nothing is cleared in place; the e2e "no customer data in the DOM, also after Back" checks prove it for every way out (8.13, replacing 7.14's in-memory clean-up test).
- Platform: a health check covers every route the service fronts, and no test relies on a stopped container refusing connections, which hangs on CI's network; use an upstream that closes the connection (7.3, 7.12).
- Run the full suite with a results file (`--report-xunit-trx`), never through `tail`, so a failure can be named (tf-aws-override).
- Tag throwaway comparison images `disputedesk-*` and remove them before reporting; the Docker daemon and disk are shared (tf-gateway-spa).
- `infra/smoke.sh` runs `docker compose down -v` and wipes the shared stack's volumes: run it only under the `stack` lock and say so in the report (tf-gateway-spa).
- A change that relies on a tool behaviour (Compose, Docker, curl) checks it on the oldest version any environment runs: local, the CI runner, AWS (tf-gateway-spa: CI's Compose 2.38.2).

## Report

End with the status contract:

```json
{
  "status": "built",
  "worktree": "/abs/path",
  "criteria": [{"id": "AC1", "requirement": "FR-3", "test": "DisputeLifecycleTests.Withdraw_after_resolution_is_rejected"}],
  "smoke": "the commands run and what they showed",
  "standards": [{"mechanism": "Linked authorization policy", "source": "https://learn.microsoft.com/aspnet/core/security/authorization/policies"}],
  "breaks": [{"patch": "unified diff of one deliberate break", "test_method": "exact --filter-method name", "expect": "fails"}],
  "test_results": {"files": ["backend/tests/.../TestResults/....trx"], "diff_hash": "..."},
  "assumptions": [],
  "open_questions": [],
  "conflicts": []
}
```
