# Challenge: ad-011 (decision)

- Challenger: gpt-6-sol via GitHub Copilot CLI
- Base commit: `commit "docs: record the libexpat and SLA-test follow-ups" (2026-11-03)`
- Date (UTC): 2026-11-03T18:16:14Z

The main risk is whether the folder moves preserve the existing build and deploy boundaries. I’ll check the Compose, Dockerfile, CI, and test wiring against the decision before judging it.

VERDICT: SIMPLIFY

## 1. Pre-mortem

- [story] **The AWS deployment stops starting.** The override is merged with the root Compose file, and `infra/deploy/aws/check-compose.sh` and `deploy.sh` currently name `docker-compose.aws.yml`. The move must preserve root-relative paths *and* update every invocation and test.
- [story] **The gateway image ships without the SPA.** Today one root-context Dockerfile builds the SPA and copies its output. Splitting that build requires both Compose and the CI publish job to pass the built SPA image as a named context; the AWS override must still consume the published gateway image without trying to build locally.
- [story] **A green backend job hides lost platform coverage.** `PlatformScriptTests` reads provision and gateway files and runs in-process HTTP retry tests. Moving cases without registering and running the new `infra/tests/` project on pull requests would silently remove those checks.

## 2. Simplest design

- [story] Keep the root Compose file as the sole stack definition and the AWS file as its override. Build one SPA image from `frontend/Dockerfile`; have the existing gateway Dockerfile copy only its built files through a [named build context](https://docs.docker.com/build/building/context/#named-contexts). Move the fixture and branding files, update their readers, and move only the tests that actually read `infra/`. Keep the existing backend, frontend, platform, Terraform, e2e, security and publish checks; add the infra test project to the platform check. No new runtime SPA container, separate repository, workflow-per-folder or custom cross-folder protocol is needed.

## 3. Assumptions

- [story] **“Published contract” includes a fixture containing demo identities.** `fixtures/local.json` is already mounted into the stub, Api and provisioner and read by e2e and `dev-login.sh`. Calling the relocated file a backend contract preserves that arrangement; it does not make the fixture a stable public API.
- [story] **The extracted theme remains the single theme file.** The SPA currently imports `src/theme/tokens.css`, whose relative font URLs, Tailwind declarations and tests depend on its location. The proposed `frontend/brand/` move works only if those imports, URLs and tests move together; creating a second copy would break the single-source requirement.
- [story] **“One job per team folder” does not mean only three CI jobs.** The current `ci.yml` also has Terraform, e2e and publish jobs, and `security.yml` is separate. The decision does not say whether these remain.

## 4. Standards

- [story] No invented runtime mechanism is necessary. Use Compose’s documented [multi-file merge](https://docs.docker.com/compose/how-tos/multiple-compose-files/merge/) for the override, [Compose `additional_contexts`](https://docs.docker.com/reference/compose-file/build/#additional_contexts) for local builds, and Buildx [named contexts](https://docs.docker.com/build/building/context/#named-contexts) for CI. Specify the build-only SPA service/context and its published-image equivalent explicitly; folder ownership itself is a repository convention, not a Docker-enforced isolation boundary.

## 5. Conflicts and gaps

- [decision] **AD-32, R33 and R35:** Read literally, “one job per team folder” replaces AD-32’s explicit `platform` and `e2e` jobs and threatens the PRD’s every-PR scans and journeys. State that those checks and `security.yml` remain, with an infra test step added, or explicitly amend AD-32 and its requirement mapping.
- [decision] **AD-31:** It explicitly locates the theme at `frontend/src/theme/tokens.css`; AD-011 moves it to `frontend/brand/`. Name AD-31 among the decisions being amended, alongside AD-26 and AD-30. **AD-21, AD-28 and AD-27** also name the old fixture, AWS override or gateway build arrangement and need their references reconciled.
- [story] **Build-only service versus runtime units is undecided.** If `service:` supplies the SPA context, say how that service is excluded from `docker compose up`’s runtime units while still built for the gateway. AD-27 and R26 permit no extra running SPA container.
- [story] **Infra test wiring is underspecified.** The current test project inherits `backend/Directory.Build.props` and `Directory.Packages.props` and references backend projects; the proposed independent project cannot inherit those files by location. Specify its pinned test dependencies and CI command without moving backend-dependent cases or dropping the existing test standards.
- [story] **The path inventory is incomplete.** Beyond the named scripts, `.dockerignore`, `security.yml`, the CI publish build, frontend theme tests, e2e fixture readers and production-path documentation reference moved files. Require a repository-wide old-path check and both local and AWS Compose render checks as acceptance evidence.
