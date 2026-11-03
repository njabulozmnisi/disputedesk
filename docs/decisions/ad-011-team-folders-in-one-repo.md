# ad-011: Team folders inside one repository

**Status:** accepted (2026-11-03)

**Context.** The repository stays one git repository, but its top-level folders model how separate team repositories would be laid out: `backend/` for backend engineering, `frontend/` for the frontend team, `infra/` for DevOps. The repository root holds what an organisation shares across teams. Today some folders read another team's source files directly, so the folder a file sits in does not say which team owns it, and a change in one team's source can break another team's build.

**Decision.** A team folder reads another team folder only through that folder's published contract, never through its source files. Every team folder may read the shared root.

| Folder | Owns | Publishes |
| --- | --- | --- |
| `backend/` | API, Worker, core-banking stub, their tests, `backend/fixtures/local.json` | `openapi.json`, the images (Dockerfile targets), the fixtures file |
| `frontend/` | the SPA, its tests, `frontend/Dockerfile` (the SPA build) | the built SPA files, and the four login branding files in `frontend/src/theme/` (see Options rejected) |
| `infra/` | the gateway, Zitadel provisioning, collector, db init, probe, Terraform, the AWS deploy, `infra/aws/compose.yml`, `infra/runbook.md`, the tests of its own files | the gateway and provision images, the AWS environment |
| root | `docker-compose.yml` (the whole stack locally, in CI and for e2e), `contracts/events/`, `docs/` (PRD, architecture, decisions, conventions, UX specs, problem pages, tickets), `e2e/`, `scripts/`, `AGENTS.md`, `.claude/`, `_bmad/`, `.github/workflows/` | |

Each team folder gets a `README.md`. There is one `AGENTS.md`, at the root, because agentic work runs from the root.

Shared rules and shared contracts stay at the root:

- `docs/conventions/` mix company-wide rules (POPIA, OWASP ASVS, trace context), rules binding two teams (ProblemDetails and the committed `openapi.json`; the generated client and the session cookie) and team style. Placing them under one team would let that team change rules that bind others, and the guard lets each builder write anywhere in its own folder, so a builder could edit its own rules.
- `docs/ux/` holds planning artefacts the BMAD skills read (`_bmad/custom/*.toml`).
- `contracts/events/` stays because every event's `dataschema` URL and each schema's `$id` name this path (AD-19); moving it would change the event contract. A shared schema location is also how a schema registry works.
- `docs/problems/` stays because the problem `type` URLs name it (AD-3).

Changes, one per crossing:

1. `docker-compose.aws.yml` moves to `infra/aws/compose.yml`, still an override merged onto the root file (Docker, [Merge Compose files](https://docs.docker.com/compose/how-tos/multiple-compose-files/merge/)). Compose resolves relative paths in every file from the first file's folder, so build contexts and mounts keep their paths. `deploy.sh`, its tests, `check-compose.sh`, `gateway.test.sh`, `rds-init.test.sh`, the guard hook and the docs update the one path.
2. `fixtures/local.json` moves to `backend/fixtures/local.json`; the stub, the seeder, provision's mount, `e2e/`, `scripts/dev-login.sh` and their tests read the new path.
3. The SPA build moves from `infra/nginx/Dockerfile` to `frontend/Dockerfile`, keeping the `BROWSER_TELEMETRY` build argument. The gateway image takes only the built files, through a named build context: Compose `additional_contexts` with `service:` locally ([Compose build reference](https://docs.docker.com/reference/compose-file/build/#additional_contexts)) and `docker buildx build --build-context` in CI's publish job ([Named contexts](https://docs.docker.com/build/building/context/#named-contexts)). The SPA service is build-only: it sits under a Compose profile, so `docker compose up --build` builds it for the gateway and never starts it, and AD-27's runtime units are unchanged (checked with Compose v5.6.1 on 2026-11-03: the profiled service was built and no container was created). The AWS override keeps using the published gateway image and builds nothing.
4. The backend `PlatformScriptTests` cases that read `infra/` files (the provision script and the gateway templates) move unchanged into an xUnit project under `infra/tests/`. It has its own project file with the same test packages and versions as the backend test projects, pinned exactly, the same `TreatWarningsAsErrors` and analysers, and no reference to `backend/`. CI's `platform` job runs it on every pull request. Cases reading root files (`docker-compose.yml`, `scripts/`) or backend source stay in backend. `ApiReferenceTests` also stays: it compares the API's Scalar headers, served in process, with `infra/nginx/templates/scalar-headers.conf.template`, a check that needs both sides and is the second accepted exception to the rule. The number of moved test cases equals the number removed from backend.
5. `docs/runbook.md` moves to `infra/runbook.md`.

CI stays as it is: one `ci.yml` with its current jobs (`backend`, `frontend`, `platform`, `terraform`, `floor-guard`, `publish`, and the OIDC checks), plus the infra test project as a step in `platform`; `security.yml` is unchanged. AD-32 and the every-pull-request scans and journeys (R33, R35) are unchanged.

Evidence for every change: a repository-wide search finds no remaining reference to a moved path (outside the dated records in `docs/verification/` and `docs/tickets/`), and both `docker compose config` renders (local, and merged with `infra/aws/compose.yml` through `check-compose.sh`) pass. The path inventory includes `.dockerignore`, `security.yml`, CI's publish build, the e2e fixture readers and `docs/production-path.md`.

**Options rejected.**

- *Separate git repositories:* the project stays one repository; the folders only model the split.
- *Conventions and UX specs in team folders:* see above.
- *A standalone AWS Compose file:* it would copy every service definition and drift from the root file.
- *Moving `contracts/events/` or `docs/problems/` into `backend/`:* changes the event and problem contracts for no gain.
- *Moving the login branding files to `frontend/brand/` (human decision 2026-11-03):* it would amend AD-31's theme location. Provision keeps mounting `tokens.css`, the Nunito Sans woff2 and both wordmarks from `frontend/src/theme/`, one file each (story 2.6); those four files count as the frontend's published branding, the first accepted exception to the rule.
- *Rewriting the moved platform tests as shell scripts:* the retry tests start in-process HTTP servers; a rewrite risks losing coverage, while a move keeps every assertion.
- *One workflow per team folder with path filters:* complicates required checks, and the publish job needs every folder.

**Consequences.**

- A reader can tell which team owns a file from its folder, and a cross-team read is visible as a contract read.
- The AWS deploy command becomes `docker compose -f docker-compose.yml -f infra/aws/compose.yml up -d --wait`.
- `infra/` gains a small .NET test project, so the CI infra job needs the .NET SDK.
- AD-31 is unchanged. Updates the file names in AD-21 (fixtures), AD-26 and AD-28 (the AWS override), AD-27 (the gateway build and the build-only SPA service), AD-30 (fixtures) and the architecture's source tree section.
