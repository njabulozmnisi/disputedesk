# Onboarding

How to start working on DisputeDesk. To run it, see the [README](../README.md).

## Prerequisites

- Docker with Compose v5.6.0 or later. The backend tests start PostgreSQL through Testcontainers.
- The .NET SDK named in [backend/global.json](../backend/global.json).
- Node, at the version in [.nvmrc](../.nvmrc).
- `curl` and `jq`, used by the scripts in `scripts/`.

## Repo map

| Folder | What is in it |
|---|---|
| [`backend/`](../backend/README.md) | The .NET solution: Api, Worker, the core-banking stub, tests and seed data |
| [`frontend/`](../frontend/README.md) | The React SPA |
| [`infra/`](../infra/README.md) | Gateway, Zitadel, collector and postgres images; the AWS deploy and Terraform; the platform tests |
| [`contracts/`](../contracts/) | The dispute event JSON Schemas and samples |
| [`e2e/`](../e2e/) | The Playwright journeys and regression specs |
| [`docs/`](README.md) | Planning, decisions, conventions, records, and how the work is done |
| [`scripts/`](../scripts/) | Developer and pipeline scripts: sign-in, the operations token, locks, checks |
| [`.github/`](../.github/) | The CI, security and deploy workflows, the `main` ruleset, shared actions |
| [`.githooks/`](../.githooks/) | The pre-commit hook: secret scan and forbidden terms |
| [`.zap/`](../.zap/) | The ZAP baseline rules CI runs |
| [`.claude/`](../.claude/), [`_bmad/`](../_bmad/) | The pipeline agents, skills and hooks; the BMAD planning configuration |

After cloning, run `scripts/bootstrap.sh` once. It checks your tools and turns on the pre-commit hook.

## How work flows

1. New work is a **feature spec**, `docs/backlog/story-<id>.md`, listed in the Features table of
   [docs/tickets/README.md](tickets/README.md). It is ready only when it meets the definition of ready in
   [AGENTS.md](../AGENTS.md).
2. A Feature or High-stakes spec gets a pre-build review, and its points are folded in before the build.
3. A builder builds it test-first. A reviewer and a verifier then check it, plus security on a High-stakes spec, with
   up to two fix rounds.
4. QA runs on the real stack when the change touches a user journey, the HTTP surface, data or events.
5. It merges through its pull request, by squash auto-merge, once `ci-passed` and `security-passed` are green.

The full rules are in [AGENTS.md](../AGENTS.md). [docs/README.md](README.md#how-it-was-built) gives the overview.
