---
id: 2
type: story
title: "CI runs the e2e regression project on pull requests that can break it"
sources: [.github/workflows/ci.yml, e2e/run-regression.sh, e2e/playwright.config.ts, docker-compose.yml, scripts/ci-gates.sh, infra/tests/DisputeDesk.PlatformTests/ComposeRenderTests.cs, docs/architecture/architecture.md, docs/decisions/ad-012-github-flow-ci.md]
companions: [docs/verification/ci-e2e-regression-prebuild-review.md, docs/verification/decisions.md]
after: [story-feature-ids]
refined: true
hitl: false
risk: medium
decisions: settled
---

# CI runs the e2e regression project on pull requests that can break it

## Description

On main `ef88282` the `e2e` job runs only the journeys (`ci.yml:336-337`, comment "the regression project is not run
in CI"). The regression suite, `e2e/run-regression.sh`, runs seven groups, each on a fresh stack. It runs only in QA,
so a pull request can break it and merge. This spec adds a job that always runs and that runs the regression suite
when the pull request changes a path the suite's stack is built from. It is a separate job, not a step in `e2e` as A3
proposed: the suite in series would pass `e2e`'s 40-minute timeout, while a parallel job on its own runner and Docker
daemon adds wall time only to the pull requests it triggers, at the cost of one extra cold image build each.

Cost: about 25 to 35 minutes of job time, to be measured in criterion 4's run (unverified estimate: seven stacks, a
cold `up --build --wait` took 2 min 27 s in run 42170110134, the regression group took 9.4 min in QA on macOS, and
`linking-verify-b` waits a real 5-minute expiry). A triggered pull request goes from about 6.5 to about 30 minutes of
wall time. Standard hosted runners are free for a public repository (GitHub Docs, *About billing for GitHub Actions*),
so the cost is wall time, not money.

The decision is a repository script, not a workflow-level `on.paths` filter or a marketplace action: `on.paths` would
leave the required check `ci-passed` pending forever, and ad-012 (*Options rejected*) rejects both. A nightly run was
rejected because it finds a break only after the merge. A check that a QA record names a regression run was rejected
because it trusts a record, not a run.

- Tier: Feature tier. Owner: backend (`ci.yml`, the scripts and the platform test); the product owner writes the AD-32
  docs change (criterion 6). May build beside `guard-allow-list`.
- Covers: R33, R35, R39; countermeasure A3 (approved by the human 2026-11-05), widened by D1.
- Decisions: D1 (the trigger list) and D2 (the AD-32 amendment) were answered by the human on 2026-11-06 and are
  recorded in [docs/verification/decisions.md](../verification/decisions.md).

## Inputs

- Trigger: `pull_request` (as today), never `pull_request_target`. Permissions stay `contents: read`; checkout keeps
  `persist-credentials: false`; no secret is needed.
- On `push` to `main` the job runs and skips the suite: the ruleset requires an up-to-date branch
  (`strict_required_status_checks_policy: true`, `infra/README.md:102`) and allows only squash merges, so the squash
  commit's tree is the tree the pull request's merge ref tested (AD-32, ad-012 clause 6).
- Changed paths, on `pull_request` only: `git diff --name-only --no-renames <base sha>...<head sha>` (three dots: the
  changes on head since the merge base, as A3's `git merge-base` named; `--no-renames` lists both the old and the new
  path of a rename). `<base sha>` is `github.event.pull_request.base.sha`, `<head sha>` is
  `github.event.pull_request.head.sha`; both reach the script through `env:` as `BASE_SHA` and `HEAD_SHA`. Checkout
  uses `fetch-depth: 0`.
- Paths that start the suite (settled by the human, D1, 2026-11-06): every stack input, meaning every Compose build
  or mount source plus the job's own files. They live in `scripts/regression-paths.list`, one per line and nothing
  else; a line ending in `/` matches every path under that directory, any other line matches exactly that path. A
  build context is matched whole, because Compose sends the whole directory. From `docker-compose.yml` on main
  `ef88282`:

  | Line | Why |
  |---|---|
  | `backend/` | build context of `core-banking` and `api` (targets `core-banking`, `api-local`); holds the mount `backend/fixtures/local.json` (zitadel, core-banking, api) |
  | `frontend/` | build context of `spa`, which the gateway takes as `additional_contexts: service:spa`; holds zitadel's four mounts under `frontend/src/theme/` (`tokens.css`, the Nunito Sans font, `wordmark.png`, `wordmark-dark.png`) |
  | `infra/images/zitadel/` | build context of `zitadel` |
  | `infra/images/gateway/` | build context of `gateway` |
  | `infra/images/postgres/` | postgres's mount at `/docker-entrypoint-initdb.d` |
  | `docker-compose.yml` | the stack itself |
  | `e2e/` | the job's own files: the suite it runs, `e2e/run-regression.sh` included |
  | `.nvmrc` | the job's own files: its Node version (`node-version-file`) |
  | `.github/workflows/ci.yml` | the job's own files: the job, so a change to the job proves the job |
  | `.github/actions/install-compose/` | the job's own files: the pinned Compose |
  | `scripts/regression-paths.sh` | the job's own files: the path script |
  | `scripts/regression-paths.list` | the job's own files: this list |
  | `scripts/operations-token.sh` | the job's own files: the regression project runs it for an operations token |
  | `scripts/test-lock.sh` | named in comments in `e2e/playwright.config.ts` and `e2e/run-regression.sh`; the job does not run it; listed so the every-script check in `scripts/regression-paths.test.sh` holds |

  Not in the list: `infra/images/otel-collector/` (no local Compose service builds or mounts it) and the named volumes
  (`pgdata`, `redpanda`, `zitadel-bootstrap`, `zitadel-clients`), which have no source in the repository.
- Script exit status: 0 when a changed path matches, 1 when none does, 2 on a bad commit or usage (grep's convention).
  The caller treats any status other than 0 or 1 as an error.
- Job timeout: 60 minutes (about 2x the estimated job time). Each group's `docker compose up` gets
  `--wait-timeout 600`, as the `e2e` job's start has (`ci.yml:366`), so a stuck start fails in 10 minutes.
- Playwright retries stay 0 (`playwright.config.ts:7`): a retry re-sends OTPs to customer D, and each group's D budget
  (at most 3 an hour, `run-regression.sh:4-8,33-39`) would then fail the D check.

## Criteria

1. **The decision is a tested script** (R33, R35)
   **Given** `scripts/regression-paths.sh <base> <head>`
   **When** it runs on a scratch repository
   **Then** it exits 0 when any path from `git diff --name-only --no-renames <base>...<head>` matches a line of
   `scripts/regression-paths.list` (Inputs), 1 when none does, and 2 on a bad commit or usage
   **And** `scripts/regression-paths.test.sh` covers each line of the list, a near miss (`frontendx/`, `e2e.md`,
   `docs/`, a file under `infra/images/otel-collector/`, `.github/actions/install-trivy/x`, `scripts/regression-paths.test.sh`,
   `docker-compose.yml.bak`), a rename into a matching
   path, a rename out of a matching path (`git mv frontend/x docs/x` exits 0), a deletion, a branch behind its base
   (the base's own changes do not count), and exit 2 for a missing argument and an unknown commit. `floor-guard` runs
   it as one new step after `scripts/guard.test.sh`.
   Mechanism: `git diff --name-only --no-renames` over a three-dot range. Source: <https://git-scm.com/docs/git-diff>.

2. **An unconditional job runs the suite when needed** (R33; architecture AD-32, every check job runs unconditionally)
   **Given** a new `e2e-regression` job with no job-level `if` or `needs`, listed in `ci-passed`'s `needs` and not in
   `publish`'s `needs`
   **When** CI runs
   **Then** the first step after checkout (id `decide`) runs `scripts/regression-paths.sh "$BASE_SHA" "$HEAD_SHA"`,
   with both SHAs under `env:`, only when `github.event_name == 'pull_request'`; it writes `run=true` on status 0 and
   `run=false` on status 1 to `GITHUB_OUTPUT`, and fails the step on any other status. On any other event it writes
   `run=false` with a notice naming the reason (the squash commit's tree is the pull request's tested merge, under the
   ruleset's up-to-date rule)
   **And** the install-compose, Node, `npm ci`, Playwright and `e2e/run-regression.sh` steps run only when
   `steps.decide.outputs.run == 'true'`; when it is `false` the job succeeds with a notice; and
   `scripts/ci-gates.sh .github/workflows/ci.yml ci-passed …` still passes.
   Mechanism: step outputs through `GITHUB_OUTPUT`, step-level `if`, and values passed through `env:` rather than
   inline in `run:`. Source: GitHub Docs, Workflow syntax
   (<https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax>) and Security hardening for
   GitHub Actions (<https://docs.github.com/en/actions/security-for-github-actions/security-guides/security-hardening-for-github-actions>).

3. **A failing regression fails the pull request** (R33)
   **Given** the job ran the suite
   **When** any group fails
   **Then** the job fails, `ci-passed` fails, and the merge is blocked by the required check. A step with
   `if: failure() && steps.decide.outputs.run == 'true'` prints `docker compose ps -a`, the last 200 log lines and
   each `error-context.md`, and a step with `if: always() && steps.decide.outputs.run == 'true'` runs
   `docker compose down -v`
   **And** no `retries`, `continue-on-error`, `test.skip` or `fixme` is added, and the journeys step in `e2e` is
   unchanged.
   Mechanism: status check functions in step-level `if`. Source: GitHub Docs, Status check functions
   (<https://docs.github.com/en/actions/reference/workflows-and-actions/expressions#status-check-functions>).

4. **The job is proved on a real run both ways** (R33)
   **Given** this feature's own pull request, which changes `ci.yml`
   **When** its CI runs
   **Then** `e2e-regression` runs the full suite and passes, the run's log shows each group's line, and the job's
   wall time is recorded
   **And** the supervisor proves the skip: a scratch branch cut from `feature/ci-e2e-regression` that changes only
   `docs/`, opened as a pull request against `feature/ci-e2e-regression` (`on: pull_request` has no branch filter),
   shows the job passing with the skip notice, and is closed unmerged with its branch deleted
   **And** main's run after the merge shows the job passing with the `push` notice.
   Mechanism: the runs themselves. Source: GitHub Docs, required status checks
   (<https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets>).

5. **The comments say what runs** (R39)
   **Given** `ci.yml`
   **When** it is read
   **Then** the `e2e` comment no longer says the regression project is not run in CI, and the new job's comment names
   `scripts/regression-paths.list` as the trigger, the wall time measured in criterion 4, why retries stay 0 (the D
   budget), why the job is not in `publish`'s `needs` (on main it only writes the notice), and that
   `scalar-disabled.spec.ts` is skipped in the suite (it needs `SCALAR_OFF=1`), so a green check does not cover
   Scalar's switch-off (it stays a manual QA step).
   Mechanism: comment. Source: AGENTS.md (no stale statements).

6. **AD-32 names `e2e-regression` as the one path-scoped suite** (R39; settled by the human, D2, 2026-11-06)
   **Given** AD-32's Rule in `docs/architecture/architecture.md` (line 505 on `ef88282`), which says every check job
   runs on every pull request and every `main` push "with no docs-only skip"
   **When** this feature's pull request is read
   **Then** the Rule keeps that sentence and adds that `e2e-regression` is the one path-scoped suite: the job runs on
   every pull request and `main` push, runs the regression suite only on a pull request whose changed paths match
   `scripts/regression-paths.list`, and otherwise passes with a notice, citing A3 (approved 2026-11-05); and
   `docs/architecture/spine-amendments.md` gains an AD-32 row dated 2026-11-06 that says the same
   **And** `scripts/regression-paths.test.sh` ends with the docs check: in AD-32's section (from `### AD-32` to the
   next `### `) `grep -F` finds `e2e-regression`, `path-scoped`, `scripts/regression-paths.list` and `A3`, and
   `spine-amendments.md` has a row starting `| AD-32 | 2026-11-06 |` that names `e2e-regression`; a missing one fails
   the test, naming it. The product owner writes both documents in this feature's docs patch (`docs/architecture/` is
   outside every builder's paths), and the brief applies that patch first, so the one full run covers the check.
   Mechanism: an architecture decision record amendment, held by a text check. Source: the architecture document's
   own AD format and its amendments table; AGENTS.md, Human gates.

7. **The list cannot drift from the stack** (R33, R35; D1)
   **Given** `infra/tests/DisputeDesk.PlatformTests/ComposeRenderTests.cs`
   **When** the platform tests run (CI's `platform` job)
   **Then** one new fact renders `docker-compose.yml` with `COMPOSE_PROFILES=build,console` through the class's
   existing `ComposeModel.RenderAsync` (`docker compose config --format json`), takes every service's `build.context`,
   every `build.additional_contexts` value that is a path (not `service:<name>`) and every `source` of a volume with
   `type: bind`, makes each relative to the repository root, and fails, naming the source, when no line of
   `scripts/regression-paths.list` matches it (Inputs)
   **And** it also fails when the list lacks any of the job's own files (`docker-compose.yml`, `e2e/`, `.nvmrc`,
   `.github/workflows/ci.yml`, `.github/actions/install-compose/`, `scripts/regression-paths.sh`,
   `scripts/regression-paths.list`); a break deletes `infra/images/postgres/` from the list and the fact must fail.
   Mechanism: Compose's rendered canonical model. Source: Docker Docs, `docker compose config`
   (<https://docs.docker.com/reference/cli/docker/compose/config/>), Compose file reference, `build` (`context`,
   `additional_contexts`) and `volumes` (`type: bind`, `source`) (<https://docs.docker.com/reference/compose-file/>),
   and `COMPOSE_PROFILES` (<https://docs.docker.com/compose/how-tos/environment-variables/envvars/>).

Verify: `floor-guard` passes `regression-paths.test.sh`, actionlint and shellcheck; the platform tests pass; criterion
4's three runs.

Flaky and first-run failures: a red run is retried once, by hand, with GitHub's "Re-run failed jobs". A failure that
then passes gets a row in `docs/verification/flaky-fixes.md`, and its cause is fixed in the test. If criterion 4's
run fails on an existing spec for an environment reason (the suite has never run on a hosted Linux runner; QA runs it
on macOS), the builder stops and reports `blocked` with the failing test; the fix is a separate test-only change, or
this feature's Touches grows by that spec, decided before the build goes on.

Touches: `.github/workflows/ci.yml` (the `e2e` comment, the new job and one `floor-guard` step),
`scripts/regression-paths.sh`, `scripts/regression-paths.list`, `scripts/regression-paths.test.sh`,
`infra/tests/DisputeDesk.PlatformTests/ComposeRenderTests.cs` (one fact), `e2e/run-regression.sh` (the
`--wait-timeout 600` on line 47 only), and AD-32 in `docs/architecture/architecture.md` with its row in
`spine-amendments.md` (the docs patch, criterion 6).

Boundaries: the journeys, ZAP and other jobs are unchanged; no action from the marketplace is added (a plain git
step); no e2e spec is changed; wiring `SCALAR_OFF=1` into the suite is out of scope.
