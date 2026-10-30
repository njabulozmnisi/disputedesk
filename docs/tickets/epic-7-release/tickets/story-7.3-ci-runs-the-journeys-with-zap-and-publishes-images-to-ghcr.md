---
id: 3
type: story
title: "CI runs the journeys with ZAP and publishes images to GHCR"
parent: epic-7-release
covers: [R33, R35]
after: [2]
hitl: false
risk: medium
refined: false
---

# CI runs the journeys with ZAP and publishes images to GHCR

## Description

Adds two jobs to `ci.yml` (AD-32). The `e2e` job, on every pull request and every push to main, brings the Compose stack up fresh, runs story 7.1's `journeys` Playwright project (UJ-1, UJ-2, UJ-5a, UJ-5b, UJ-5d) with its axe checks, then runs the ZAP baseline against the gateway with a reviewed rules file, and fails on any failure. The `publish` job, on pushes to main only and only after every CI job passes, builds the five images from their Dockerfile targets and pushes them to GHCR tagged by commit SHA, recording each digest for the deploy. This story owns publishing; the AWS story 7.5 only consumes the images (decided 2026-10-30, AD-32 amended). The Security workflow's Trivy job also scans the two images published for the first time, `core-banking` and `provision`.

## Requirements

Verify: `actionlint` passes on `.github/workflows/`; the pull request's own run shows the `e2e` job green with the five journeys passed and ZAP exit code 0; after merge, the first main run's `publish` job summary lists five image digests and GHCR's package list shows the five packages with the `sha-<12>` tag (supervisor check).

Inputs:
- The short SHA: the first 12 hexadecimal characters of `github.sha` (`${GITHUB_SHA::12}`), so the tag is `sha-` plus 12 characters.
- `.zap/rules.tsv`: one line per triaged rule, tab separated, `<rule ID>\tIGNORE\t<reason>` (ZAP baseline config file format); a rule ID is a ZAP passive rule number; only `IGNORE` lines with a reason are allowed, so the file is the triage record.

1. **The `e2e` job runs the journeys on a fresh stack** (R33, R35; NFR-TEST-5, NFR-TEST-6, NFR-INFRA-2)
   **Given** a pull request, or a push to main
   **When** CI runs
   **Then** the `e2e` job (`runs-on: ubuntu-24.04`, `timeout-minutes: 40`, the workflow's `contents: read` only) checks out with `persist-credentials: false`, sets up Node from `.nvmrc`, runs `npm ci` in `e2e/`, installs the browser with `npx playwright install --with-deps chromium` (Playwright, *Continuous Integration*), brings the stack up with `docker compose up --build --wait --wait-timeout 600` (Docker, *docker compose up*), and runs `npm run journeys`, the `journeys` project only (story 7.1, criterion 1)
   **And** the runner's fresh checkout and Docker daemon make the stack fresh, so customer D starts unlinked (AD-30); the `regression` project is not run here (AD-32 names the journeys)
   **And** `playwright.config.ts` sets `forbidOnly: !!process.env.CI`, so a committed `test.only` fails the run instead of shrinking it (Playwright, *Test configuration*)

2. **Any failure fails the job, with diagnostics and teardown** (R33; NFR-TEST-6)
   **Given** the `e2e` job's steps
   **When** Compose start-up (an unhealthy or exited service, or the 600-second wait), `npm ci`, the browser install, any Playwright test or the ZAP scan fails
   **Then** that step exits non-zero and the job fails; no step uses `continue-on-error`, and no test is skipped or retried to pass (`retries` stays 0)
   **And** an `if: failure()` step prints `docker compose ps -a` and `docker compose logs --no-color --tail 200`, and an `if: always()` step runs `docker compose down -v`

3. **ZAP baseline on the gateway, every alert triaged in the repo** (R33; NFR-SEC-8)
   **Given** the stack still running after the journeys
   **When** the job runs `ghcr.io/zaproxy/zaproxy` pinned by digest with `docker run --network host -v "$PWD/.zap:/zap/wrk:rw"` and `zap-baseline.py -t http://localhost:8080 -c rules.tsv -m 2 -T 10` (ZAP, *Baseline Scan*)
   **Then** the job fails on any non-zero exit code: 1 (a FAIL), 2 (a WARN) or 3 (the scan did not complete); no `-I` flag is used, so every alert not marked `IGNORE` in `.zap/rules.tsv` fails the build
   **And** `.zap/rules.tsv` holds only the alerts already triaged in QA, each with its reason: Non-Storable Content (10049) and Cross-Origin-Embedder-Policy Header Missing or Invalid (90004) as recorded in `docs/verification/2.4-qa.md` and `7.1-qa.md`; a new line needs a reason and a reviewer's approval in its pull request
   **And** the scan is unauthenticated and passive: it covers what the spider reaches from the gateway origin, while the signed-in screens are covered by the journeys and per-story QA; this scope is stated in a comment above the step

4. **Only `publish` can write packages, and only on main after every job passes** (R33; NFR-INFRA-2, AD-32)
   **Given** `ci.yml`
   **When** it is read and run
   **Then** the `publish` job has `if: github.event_name == 'push' && github.ref == 'refs/heads/main'`, `needs: [backend, frontend, platform, floor-guard, e2e]`, and job-level `permissions: { contents: read, packages: write }`; no other job or workflow has `packages: write` (GitHub, *Controlling permissions for GITHUB_TOKEN*)
   **And** it logs in with `echo "$GITHUB_TOKEN" | docker login ghcr.io -u "$GITHUB_ACTOR" --password-stdin` (GitHub, *Working with the Container registry*), with no personal access token and no third-party action

5. **Five images, SHA tags, digests recorded** (R33; NFR-INFRA-2, AD-32)
   **Given** a push to main that passes every job
   **When** `publish` runs
   **Then** it builds and pushes exactly these five, each tagged `sha-<12>` and never `latest`:
   - `ghcr.io/njabulozmnisi/disputedesk-api` from `backend`, target `api`;
   - `ghcr.io/njabulozmnisi/disputedesk-worker` from `backend`, target `worker`;
   - `ghcr.io/njabulozmnisi/disputedesk-core-banking` from `backend`, target `core-banking`;
   - `ghcr.io/njabulozmnisi/disputedesk-gateway` from the repository root with `-f infra/nginx/Dockerfile` (the SPA built in);
   - `ghcr.io/njabulozmnisi/disputedesk-provision` from `infra/zitadel/provision`
   **And** after each push it writes the image's `docker inspect --format '{{index .RepoDigests 0}}'` to `$GITHUB_STEP_SUMMARY`, so the deploy can reference digests (AD-32); a failed build or push fails the job

6. **The Trivy gate covers every published image** (R33; NFR-SEC-8)
   **Given** `security.yml`'s `trivy` job
   **When** it runs on a pull request
   **Then** it also builds `core-banking` (`backend`, target `core-banking`) and `provision` (`infra/zitadel/provision`) and scans both with `trivy image --severity HIGH,CRITICAL --exit-code 1`, as it does `api`, `worker` and `gateway`, so all five published images pass the same gate before merge (`scripts/push-and-watch.sh` fails unless CI and Security succeed)

## Boundaries

- Must not change: the existing `backend`, `frontend`, `platform` and `floor-guard` jobs; the Security workflow beyond the two added images.
- No `latest` tag, no long-lived registry credential, no home-grown parser of ZAP output (ZAP's exit status is the gate), no new third-party action.
- Not here: package visibility and how the AWS instance pulls images (story 7.5); the `deploy.yml` workflow (7.5); running the `regression` project in CI.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- On a throwaway branch, delete the `10049` line from `.zap/rules.tsv`: the `e2e` job fails at the ZAP step with exit code 2.
- On a throwaway branch, add `test.only` to one journey: the `e2e` job fails (`forbidOnly`).
- Check the first green PR run, then after merge the `publish` job's summary and the GHCR package list: five packages, each with the `sha-<12>` tag and no `latest`.

## Build facts

- Owner: backend (backend-builder).
- Flags: none.
- Lane: A.
- Mechanisms and sources: GitHub Actions jobs, `needs`, `if`, job-level `permissions` (<https://docs.github.com/en/actions/writing-workflows/workflow-syntax-for-github-actions>, <https://docs.github.com/en/actions/security-for-github-actions/security-guides/automatic-token-authentication#modifying-the-permissions-for-the-github_token>); GHCR with `GITHUB_TOKEN` (<https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry>, <https://docs.github.com/en/actions/use-cases-and-examples/publishing-packages/publishing-docker-images>); `docker compose up --wait` (<https://docs.docker.com/reference/cli/docker/compose/up/>); Playwright on CI and `forbidOnly` (<https://playwright.dev/docs/ci>, <https://playwright.dev/docs/api/class-testconfig#test-config-forbid-only>); ZAP baseline scan, its config file and exit codes (<https://www.zaproxy.org/docs/docker/baseline-scan/>); Trivy image scan (<https://trivy.dev/latest/docs/target/container_image/>); `actionlint` (<https://github.com/rhysd/actionlint>).
- Packages: none. The ZAP image is pinned by digest, as Compose pins its images.
- Touches:
  - `.github/workflows/ci.yml` (the `e2e` and `publish` jobs)
  - `.github/workflows/security.yml` (Trivy builds and scans `core-banking` and `provision`)
  - `.zap/rules.tsv` (new)
  - `e2e/playwright.config.ts` (`forbidOnly`)

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- prd — docs/prd/prd.md, NFR-INFRA-2, NFR-SEC-8, NFR-TEST-5, NFR-TEST-6
- architecture — docs/architecture/architecture.md, AD-30, AD-32
- verification — docs/verification/7.3-challenge.md, docs/verification/7.1-challenge.md, docs/verification/7.2-challenge.md, docs/verification/2.4-qa.md

## Notes

- Decision: 2026-10-30 (human, 7.3 challenge) — story 7.3 owns publishing the images to GHCR from CI on main; AD-32 is amended so the AWS story 7.5 only consumes them.
- Decision: 2026-10-30 (Sofia, 7.3 challenge) — the ZAP gate fails on every untriaged alert, which is stricter than NFR-SEC-8's high or critical: ZAP's own exit status is the documented gate, and filtering by risk would need a home-grown parser of its report. A triaged alert is an `IGNORE` line with its reason in `.zap/rules.tsv`.
- Decision: 2026-10-30 (Sofia, 7.3 challenge) — the short SHA is 12 characters, fixed, so tags never change length as the history grows.
- Handoff to 7.4 and 7.5: the published `api` and `worker` images are the `api` and `worker` targets, without the `api-local` and `worker-local` probes that `docker-compose.yml`'s health checks call; the AWS override must give those services health checks that work on the published images. GHCR creates new packages private; their visibility and the instance's pull are 7.5's.
