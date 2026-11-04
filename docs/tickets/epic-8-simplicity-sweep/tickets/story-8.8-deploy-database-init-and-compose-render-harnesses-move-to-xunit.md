---
id: 8
type: story
title: "Deploy, database-init and Compose-render harnesses move to the infra test project"
parent: epic-8-simplicity-sweep
covers: [R35, R38]
after: [1, 7]
hitl: false
risk: low
refined: true
---

# Deploy, database-init and Compose-render harnesses move to the infra test project

## Description

Second half of slice S6 of the mitigation plan (wave 2). After 8.7, three shell harnesses remain in `infra/aws/`: `deploy.test.sh` (341 lines), `rds-init.test.sh` (336) and `check-compose.sh` (278). Each defines its own `section`, `fail`, `ok` and assertion helpers and builds its own PATH stubs (INF-1). `check-compose.sh` also tests `input-bounds.sh`'s regexes edge by edge although `deploy.test.sh` tests the same bounds through the deploy, and reads `infra/aws/compose.yml` with an indentation-tracking awk to find `!override` tags (INF-10). This story moves the three into `infra/tests/DisputeDesk.PlatformTests` with one PATH-stub helper beside 8.7's process helper, tests the bounds once, replaces the awk tag reader with exact assertions on the rendered model, and adds a test that infra holds no shell test harness.

## Requirements

Verify: `dotnet test --project DisputeDesk.PlatformTests` passes in `infra/tests/` locally and in CI's *Infra tests* step, within the `platform` job's 15-minute limit; the three scripts no longer exist; `infra/aws/deploy.sh` and `infra/aws/input-bounds.sh` are unchanged apart from comments that name the deleted harnesses.

Inputs (no endpoint change):
- The deploy script's inputs are those `infra/aws/input-bounds.sh` defines (`AWS_INPUTS` and `within_bounds`), plus the deploy-only `QA_DB_PASSWORD` and `RDS_MASTER_PASSWORD` the deploy checks itself; the tests use the values in `infra/aws/compose.test.env` and the edge cases criterion 5 names.

1. **One PATH-stub helper** (INF-1; R35)
   **Given** the infra test project and 8.7's process helper
   **When** a test needs `aws`, `curl`, `docker`, `docker compose`, `iptables`, `iptables-save` or `systemctl` replaced
   **Then** it writes stubs into a temporary directory placed first on the child process's `PATH`, each stub recording its arguments and answering as the test sets; a call the test did not set up fails with exit 97 and a message naming the command and its arguments, so a changed deploy command never gets a success-shaped answer; the helper is shared by the deploy and database-init tests (and takes over 8.7's per-test stubs) and removes its directory when the test ends
   **And** the `docker` stub can pass `docker run` through to the real Docker with `--network <test network>` added, as `rds-init.test.sh:229-231` does, while still stubbing `docker compose`

2. **The deploy cases run as xUnit tests** (INF-1; R38, story 7.5 criterion 6)
   **Given** the sections of `infra/aws/deploy.test.sh` (*a first deploy*, *a later deploy*, *every failure exits non-zero, naming its cause*, *the script never prints what it reads*)
   **When** this story lands
   **Then** each is one or more tests in a `DeployScriptTests` class that runs the unchanged `infra/aws/deploy.sh` from a fresh throwaway copy per test with the stubs, asserting the same calls, exit codes and output; `deploy.test.sh` and the CI step *AWS deploy script* are deleted

3. **The database-init cases run as xUnit tests** (INF-1; R38, story 7.17)
   **Given** the sections of `infra/aws/rds-init.test.sh` (the test CA and certificates, the RDS-shaped PostgreSQL 18, the refused non-TLS connection, the wrong CA or host name, the first and later deploy, no secret in output)
   **When** this story lands
   **Then** each is a test in a `DatabaseInitTests` class with the same throwaway PostgreSQL 18 on an internal Docker network and no host port, reached only through the passed-through `docker run` of criterion 1, so the TLS and role checks still run against a real server; the first and later deploys stay in one ordered test (the later deploy depends on the first), and the class's fixture starts and removes the database once; its `shortcut:` comment is kept; `rds-init.test.sh` and the CI step *RDS database init* are deleted

4. **The Compose render cases run as xUnit tests** (INF-1; R38, story 7.4 criteria 1, 2 and 5)
   **Given** `infra/aws/check-compose.sh`'s sections *every input is required, and only the inputs are*, *only the AWS services start, and nothing local is left* and *the collector config*
   **When** this story lands
   **Then** each is a test in a `ComposeRenderTests` class that renders `docker compose -f docker-compose.yml -f infra/aws/compose.yml --env-file infra/aws/compose.test.env config --format json` through 8.7's helper (the child's environment set by the test) and asserts on the model, and validates the collector config with the pinned collector image as today; `check-compose.sh` and the CI step *AWS Compose override* are deleted
   **And** what a rendered model cannot show stays a focused source check on `infra/aws/compose.yml`, as `check-compose.sh` does today: the exact set of `${…}` references equals `AWS_INPUTS`, and every reference uses `:?`; and the render with each input removed in turn fails naming that input

5. **The input bounds are tested once, both sides** (INF-10; R38)
   **Given** `check-compose.sh:70-130` (each bound's edges) and `deploy.test.sh:288-311` (the same bounds through the deploy)
   **When** this story lands
   **Then** each bound is tested once, through the deploy, on both sides, each refusal naming the input: for a length range, the shortest and longest lengths are accepted and one character shorter and one longer are refused (32 and 128 for the passwords and secrets, exactly 32 for `ZITADEL_MASTERKEY`, 1 to 32 and 32 to 128 for the two parts of `MAILPIT_UI_AUTH`, 253 for a host name, 512 for `OTEL_LOG_GROUP`); for a pattern with no upper length (`DATAPROTECTION_SSM_PATH`, `RDS_CA_BUNDLE`, `GATEWAY_REALIP_CONF`, the image references, `AWS_REGION`, `DATAPROTECTION_KMS_KEY_ID`), the test env's value is accepted and one value with a forbidden character (and, for a path, a trailing `/`) is refused; `ZITADEL_ADMIN_PASSWORD` is refused without each required character class and without `-`; `LINKING_OTP_HMAC_KEY` is accepted at 32 decoded bytes and refused at 31 and when not padded base64; `QA_DB_PASSWORD` and `RDS_MASTER_PASSWORD` get the same edges as the deploy checks them
   **And** `ComposeRenderTests` checks only that `compose.test.env`'s values meet the bounds (story 7.4 criterion 1)

6. **No awk reader for override tags: the rendered environments are exact** (INF-10; R38)
   **Given** the awk that tracks indentation to find `!override` tags in `infra/aws/compose.yml` (`check-compose.sh:158-175`) and the render checks that assert only chosen environment keys (`check-compose.sh:205-242`)
   **When** this story lands
   **Then** the awk is not ported; instead, for each service that `compose.yml` overrides with `!override` (for example `worker`, `infra/aws/compose.yml:173`), the test asserts the rendered `environment` key set equals the expected set exactly, beside today's checks (no `localhost`, no `dev-only`, the exact published ports and the chosen values), so a dropped tag, which merges the local keys back in, fails naming the service

7. **Infra holds no shell test harness** (wave 4 item 1; INF-1)
   **Given** the repository after this story
   **When** the infra test project runs
   **Then** a test fails if any `*.test.sh` exists under `infra/`, or any `check-*.sh` under `infra/aws/`, with exactly one named exception, `infra/terraform/iam-names.test.sh`, which the test's comment says story 8.16 replaces (8.16 removes the exception); `infra/smoke.sh`, the developer smoke check, is not a harness and stays

## Boundaries

- Must not change: what `infra/aws/deploy.sh`, `input-bounds.sh`, `compose.yml`, `compose.test.env`, `infra/db/10-roles-and-databases.sh` and the collector config do; what each moved case asserts, apart from criterion 5's single home for the bounds and criterion 6's exact key sets. Comments in `deploy.sh` (`:14`, `:36`) and `input-bounds.sh` (`:2`) that name the deleted harnesses are updated to name the new test classes; nothing else in them changes.
- `iam-names.test.sh` stays as it is (8.16 replaces it, D9).
- The render is authoritative in CI, where Compose 5.6.1 is pinned; locally it matches only on the same version.

## Supervisor checks

- Throwaway copy: make `deploy.sh` print a fetched secret; a `DeployScriptTests` case fails.
- Throwaway copy: add an unexpected `aws` call to `deploy.sh`; the stub fails with exit 97 naming it, and the test fails.
- Throwaway copy: change `PGSSLMODE=verify-full` to `require` in `deploy.sh:120`; a `DatabaseInitTests` case fails.
- Throwaway copy: remove the `!override` tag from the `worker` service's `environment` in `infra/aws/compose.yml`; a `ComposeRenderTests` case fails naming `worker`.
- Throwaway copy: replace one `${APP_HOST:?…}` in `compose.yml` with a literal host; the source check fails naming `APP_HOST`.
- Throwaway copy: widen the password bound in `input-bounds.sh` to `{32,129}`; criterion 5's 129-character case fails.
- Throwaway copy: add `infra/aws/new.test.sh`; criterion 7's test fails.

## Build facts

- Owner: backend (backend-builder).
- Tier: Small, test-only.
- Flags: none.
- Lane: A, after 8.7. Locally the builder runs the infra tests under `scripts/test-lock.sh run stack <owner> --`, as `rds-init.test.sh:9` asks today; in CI the `platform` job has its own runner.
- Mechanisms and sources:
  - `docker compose config --format json`: <https://docs.docker.com/reference/cli/docker/compose/config/>.
  - Child-process environment (`ProcessStartInfo.Environment`): <https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.environment>.
  - xUnit shared context: <https://xunit.net/docs/shared-context>.
  - Compose `!override` merge tag: <https://docs.docker.com/reference/compose-file/merge/#replace-value>.
- Packages: none.
- Touches: `infra/tests/DisputeDesk.PlatformTests/` (new `DeployScriptTests.cs`, `DatabaseInitTests.cs`, `ComposeRenderTests.cs`, a PATH-stub helper, the harness rule test), `infra/aws/deploy.test.sh`, `rds-init.test.sh`, `check-compose.sh` (deleted), comments in `infra/aws/deploy.sh` and `input-bounds.sh`, `.github/workflows/ci.yml` (three `platform` steps removed; the *Infra tests* step's timeout if needed), `infra/README.md` (the test commands).
- After merge: `scripts/verify-with-copilot.sh` names the deleted scripts in its allowed commands (`shell(infra/aws/check-compose.sh)` and others); the supervisor updates it as a Small-tier tooling fix.
- Collision: 8.16 removes criterion 7's named exception and rewrites `iam-names.test.sh` in this project's style.

## Pre-build

- Challenger: `docs/verification/8.8-challenge.md` (simplify).
- Review: `docs/verification/epic-8-prebuild-wave2b.md`, section 8.8. No security read.
- Decisions: none raised.
- Sofia, 2026-11-04: every `[story]` point folded. Review: the `docker run` pass-through with `--network` and exit 97 for an unexpected call (criterion 1); exact rendered environment key sets per overridden service, with a dropped-tag Supervisor check (criterion 6); both sides of every range (criterion 5); 8.16 already removes the exception (criterion 7, Collision); the render authoritative in CI only (Boundaries). Challenger: the real database reached through the pass-through (criteria 1, 3); the job's time limit and the `stack` lock kept for local runs (Verify, Build facts); the source checks for `:?` and the exact reference set, and missing-input renders (criterion 4); cases named per input, the deploy-only passwords included (criterion 5); unexpected calls fail (criterion 1); comment-only updates allowed (Boundaries); 8.7 first stays in `after`.

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- audit — docs/verification/simplicity-audit/infra-ci-scripts.md, INF-1, INF-10
- plan — docs/verification/simplicity-audit/mitigation-plan.md, wave 2, S6; wave 4, item 1
- stories — docs/tickets/epic-7-release/tickets/story-7.4-aws-compose-override-and-opentelemetry-collector.md, story-7.5-terraform-dev-environment-and-oidc-deploy.md, story-7.17-deploy-database-setup-proved-against-postgresql-18-with-tls.md

## Notes

- Risk low (proposed): test-only; each moved case keeps its Supervisor break.
- Decision: 2026-11-04 (Sofia) — `iam-names.test.sh` is the harness rule's one named exception until 8.16, which replaces its line rules with `System.Text.Json` over JSON policy files (D9, as answered 2026-11-04) and writes the check in this project, so it is rewritten once.
- Decision: 2026-11-04 (Sofia) — exact key sets replace the awk, the review's option: they fail on a dropped tag without reading YAML layout, and the source checks keep what a render cannot show (which inputs are required).
