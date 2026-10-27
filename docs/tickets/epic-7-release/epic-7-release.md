---
id: 7
type: epic
title: "Journeys proven on the stack, deployed and documented"
parent: initiative-disputedesk
covers: [R28, R32, R33, R34, R35, R36, R38, R39, R40]
sources: []
companions: []
after: []
risk: high
---

# Journeys proven on the stack, deployed and documented

## Description

The Playwright journeys with accessibility and ZAP scans pass on the real stack and in CI, images publish to GHCR, the dev environment deploys to AWS through Terraform and the OIDC role, the README and records are written, and two sequential refactor sweeps, backend then frontend, close the project.

## Outcome

Reviewers run and trust the whole system; signal: the initiative Done when checks pass.

## Requirements

R28 (parent R28): A dispute change commits with its timeline entry, audit entry and outbox event, or none of them commit; raising and actions keep working while Kafka is down and events publish in per-dispute order when it returns; a row the broker rejects permanently is marked failed after 5 attempts, counted and logged, and later events for that dispute wait for an operator per the README runbook. This epic delivers: the README runbook for failed outbox rows.
R32 (parent R32): The nginx gateway sets the security headers and the coarse per-IP limit in NFR-SEC-7; secrets come from the environment, never the repo; in AWS, Mailpit's web UI sits behind basic auth. This epic delivers: secrets from SSM and Mailpit basic auth in AWS (NFR-SEC-9, NFR-SEC-11).
R33 (parent R33): GitHub Actions builds every pull request, runs every suite and the scans in NFR-SEC-8, failing on any untriaged high or critical finding, and pushes images to GHCR on main; no test is weakened to pass. This epic delivers: the CI e2e job with the ZAP baseline and GHCR publish.
R34 (parent R34): CI enforces nullable references, warnings as errors and `dotnet format` in .NET, and strict TypeScript with ESLint and Prettier; no suppression is added without a written reason; glossary terms are used in code, API and copy; the frontend is React and TypeScript with shadcn/ui on Tailwind and Orval-generated TanStack Query hooks never edited by hand, with design tokens in one theme file; every new package is verified and pinned exactly. This epic delivers: glossary terms and suppressions swept.
R35 (parent R35): Every FR consequence not tagged `[manual check]` maps to an automated test listed in its story's report: unit tests for every FR-24 cell, the D + 90 boundary, amounts and SLA timing; integration tests on real PostgreSQL in Testcontainers with no in-memory fakes; contract tests for `openapi.json` and each event schema; Playwright E2E for UJ-1, UJ-2, UJ-5a, UJ-5b and UJ-5d on the real stack. This epic delivers: the Playwright journeys (NFR-TEST-5).
R36 (parent R36): The SPA, Api host and Worker emit OpenTelemetry traces, metrics and logs, and one trace runs browser to API to outbox to Kafka to Worker with trace context injected by hand; the business metrics in NFR-OBS-3 are emitted; the Aspire Dashboard (locally) and CloudWatch (AWS) show them with no manual setup. This epic delivers: CloudWatch through the collector.
R38 (parent R38): Terraform modules build the one dev environment in NFR-INFRA-3 in a new AWS free-plan account, deployed through a GitHub OIDC role with no long-lived keys, with state in S3 under native locking, a budget alarm that excludes credits, and a clean `terraform destroy`.
R39 (parent R39): The README gives build, run and test steps, the seed identities, how to drive the operations endpoints from Scalar (the dev API viewer and stand-in operations tool), the research simplifications, and ends with the ten-area map; `docs/production-path.md` records every §6.2 deferral with a reason; `docs/decisions/` holds the decision records NFR-DOC-3 names. This epic delivers: README, production path, runbook and decision records.
R40 (parent R40): The customer portal meets WCAG 2.2 AA (contrast, keyboard access, visible focus, labelled errors, reflow at 320px), proved by an automated accessibility scan in the E2E tests. This epic delivers: the axe scans.

## Done when

- UJ-1, UJ-2, UJ-5a, UJ-5b and UJ-5d pass in CI with axe and ZAP clean.
- Images publish to GHCR on main; the dev environment deploys and destroys cleanly.
- README ends with the ten-area map; `docs/production-path.md` and the decision records exist.

## Boundaries

- The refactor sweep changes no behaviour and adds no feature.
- Terraform plans and applies from the maintainer's machine, never CI (AD-28).

## References

- parent — docs/initiative-disputedesk.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-26, AD-28, AD-29, AD-32, Deferred
- experience — docs/ux/EXPERIENCE.md
- design — docs/ux/DESIGN.md
- prd — docs/prd/prd.md
- verification — docs/verification/

## Notes

- Decision: 2026-10-26 — epic Requirements are the parent's lines only, ids kept (`R<n> (parent R<n>)`); each line names the part this epic delivers where the parent line is shared.
- Decision: 2026-10-26 — tracer bullet: story 1, QA journey UJ-1 on the real stack with axe and ZAP; it is the thinnest path through every layer this epic touches.
- Waits on 6.4, 3.6, 3.7, 2.5, 2.6, 4.1 because: 1 needs what those stories build (see its References and Description).
- Waits on 5.4 because: 2 needs what those stories build (see its References and Description).
- Waits on 5.1 because: 4 needs what those stories build (see its References and Description).
- Waits on 5.1 because: 7 needs what those stories build (see its References and Description).
- Waits on 5.3, 5.4, 6.4, 3.7 because: 8a needs what those stories build (see its References and Description); 8b waits on 8a.
- Waits on: epic 2 (the themed hosted login for UJ-1), epic 3 (dispute screens for UJ-1, UJ-2 and UJ-5), epic 4 (events on the topic for the four-hop trace), epic 5 (Scalar and the sweep for the README and the sweep story), epic 6 (linking screens for UJ-1).
- Decision: 2026-10-26 — Gate 2: the closing refactor sweep 7.8 is split into 7.8a backend (owner backend) and 7.8b frontend (owner frontend), run sequentially after everything else.
- Decision: 2026-10-26 — Gate 2: the docs stories 7.6 (decision records and production path) and 7.7 (README, runbook and onboarding) change owner to `docs`, a docs agent rather than a builder; runbook and onboarding stay short.
- Decision: 2026-10-26 — Gate 2: QA resets customer D by starting a fresh stack per QA run (`docker compose down -v`, then `up --wait`), not through a test-only database role (7.1, 7.2).
- Decision: 2026-10-26 — Gate 2: decision records follow kickoff amendment 1: ad-001 Zitadel as the external identity provider, ad-002 `IAppDbContext`, ad-003 Result types, ad-004 model roles (exists), ad-005 QA data integrity, ad-006 actor-agnostic dispute actions, ad-007 BFF session (no tokens in the browser), ad-008 enrolment and account linking (7.6).
- Decision: 2026-10-26 — Gate 2: backend-builder may write `scripts/dev-login.sh`, `contracts/`, `.nvmrc`, `docker-compose.aws.yml` and `.zap/` (guard updated), so 7.3 and 7.4 keep owner backend.
- Decision: 2026-10-26 — Gate 2: code in every builder-owned story of this epic is stupid simple and logical, per AGENTS.md (smallest change that meets the criteria; every abstraction earns its place).
