# Onboarding

How to start working on DisputeDesk. To run it, see the [README](../README.md).

## Prerequisites

- Docker with Compose v2 (`docker compose`). The backend tests start Postgres through Testcontainers, so Docker must be
  running for them too.
- The .NET SDK named in [backend/global.json](../backend/global.json).
- Node, at the version in [.nvmrc](../.nvmrc).
- `curl` and `jq`, used by the scripts in `scripts/`.

## Repo map

| Folder | What is in it |
|---|---|
| [`.claude/`](../.claude/) | The pipeline agents and their procedure skills |
| [`.githooks/`](../.githooks/) | The pre-commit hook: secret scan and forbidden terms |
| [`.github/`](../.github/) | The CI and security workflows |
| [`.zap/`](../.zap/) | The ZAP baseline rules CI runs (story 7.3) |
| [`_bmad/`](../_bmad/) | The BMAD planning configuration |
| [`backend/`](../backend/) | The .NET solution: Api, Worker, the core-banking stub and the tests |
| [`contracts/`](../contracts/) | The dispute event JSON Schemas |
| [`docs/`](README.md) | Planning, decisions, conventions and verification records (below) |
| [`e2e/`](../e2e/) | The Playwright journeys and regression specs |
| [`backend/fixtures/`](../backend/fixtures/) | The synthetic seed data for the local stack |
| [`frontend/`](../frontend/) | The React SPA |
| [`infra/`](../infra/) | The Compose platform's configuration: database init, gateway, Zitadel provisioning |
| [`scripts/`](../scripts/) | Developer and pipeline scripts: locks, sign-in, the operations token, scans |

| `docs/` subfolder | What is in it |
|---|---|
| [`architecture/`](architecture/) | The architecture spine (AD-1 onwards) |
| [`conventions/`](conventions/) | Backend and frontend rules |
| [`decisions/`](decisions/) | Decision records ad-001 to ad-009 |
| [`prd/`](prd/) | The PRD |
| [`problems/`](problems/) | One page per ProblemDetails `type` |
| [`process/`](process/) | How the work is done |
| [`product-brief/`](product-brief/) | The product brief |
| [`research/`](research/) | Research reports, the dispute lifecycle among them |
| [`tickets/`](tickets/) | Epics and stories, in build order |
| [`ux/`](ux/) | The UX design and experience documents and mockups |
| [`verification/`](verification/) | Challenge, review, security, QA and run records per story |

## Story flow

1. Stories run in the order of [docs/tickets/README.md](tickets/README.md); each is ready only when it meets the
   definition of ready in [AGENTS.md](../AGENTS.md).
2. A challenger and a pre-build review read the story first; their points are folded into it before the build.
3. A builder builds it test-first, then a verifier, the reviewer and, for high-stakes stories, security check it, with
   one fix round.
4. QA runs last on the real stack; the story merges only when every stage passes and CI is green.
5. The full rules are in [AGENTS.md](../AGENTS.md) and [docs/process/agentic-sdlc.md](process/agentic-sdlc.md).
