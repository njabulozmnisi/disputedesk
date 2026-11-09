# deploy-recreate-on-config: post-build review

- Date: 2026-11-09
- Tier: Small (infra fix, no production code)
- Worktree: `.claude/worktrees/agent-a8a123f64764f2509`, uncommitted diff against main (575bd39)
- Files: `infra/deploy/aws/deploy.sh`, `infra/deploy/aws/compose.yml` (header comment), `infra/terraform/README.md`,
  `infra/tests/DisputeDesk.PlatformTests/DeployCopy.cs`, `infra/tests/DisputeDesk.PlatformTests/DeployScriptTests.cs`
- Builder record: `docs/verification/deploy-recreate-on-config-build.json` in the worktree
- Result: **approve**. No high or medium findings; two lows.

## What changed

`deploy.sh` step 6 now runs `docker compose -f docker-compose.yml -f infra/deploy/aws/compose.yml up -d --wait --force-recreate`.
The stub's one accepted compose call (`DeployCopy.ComposeUpCall`) gains the flag, and a new test asserts it. The
README tells the operator that every deploy recreates every container and that Mailpit's inbox empties.

## Pass 1: correctness

**Root cause and fix.** Compose decides whether to recreate from the service's configuration hash and its image.
A bind mount's content is not part of that hash. `git checkout` and `replace_file` both write a new file and rename
it over the old path, so on Linux the running container keeps the old inode and still sees the old content. That is
the stale `config.yaml` seen after 575bd39. Docker's reference for `docker compose up` says Compose recreates "if the
service's configuration or image was changed after the container's creation", and `--force-recreate` will "Recreate
containers even if their configuration and image haven't changed"
(<https://docs.docker.com/reference/cli/docker/compose/up/>). The local `docker compose up --help` for v5.6.1 says the
same. v5.6.1 is the version the instance (`modules/compute/main.tf`, `compose_version`) and CI pin. The fix targets
the root cause and covers every bind mount, current and future.

**Test.** `LaterDeploy_RecreatesEveryContainer_SoAChangedMountedFileTakesEffect` (DeployScriptTests.cs:144) fails
without the change. The docker stub answers only `ComposeUpCall` exactly (DeployCopy.cs:193), so the builder's break
(dropping the flag) makes the compose call exit non-zero and fails the exit-code assertion. The exact-sequence
assertions in `FirstDeploy_...` (line 34), `LaterDeploy_SkipsTheInit...` (line 137) and `DatabaseInitTests` (line 115)
also cover the flag. The builder's results file
(`TestResults/njabulomnisi_Njabulos-Ayoba-MacBook-Pro_2026-11-09_10_29_30.124577.trx`) shows 442 of 442 passed, the
new test included. A stub test cannot show a container picking up a new file. The builder's Compose smoke check in the
build record did show that: same container ID and old file with plain `up`, new container and new file with
`--force-recreate`. That is enough for a one-flag shell change.

**Consequences checked (the brief's list).**

- **Most containers were already recreated on every deploy.** `deploy.yml` (lines 50-69) requires each image's
  `org.opencontainers.image.revision` label to equal the deployed SHA. Every commit therefore gives api, worker,
  gateway, core-banking and zitadel a new digest, and Compose recreates them anyway. The flag adds only redpanda,
  mailpit and otel-collector. The builder's claim holds.
- **Named volumes and data.** `--force-recreate` keeps named volumes, and it carries anonymous volumes over unless
  `-V/--renew-anon-volumes` is given (same reference). Redpanda's `redpanda:/var/lib/redpanda/data`, plus
  `zitadel-bootstrap` and `zitadel-clients`, survive. Postgres is RDS in AWS (the `postgres` service is profile
  `local`). Mailpit has no volume and no `MP_DATABASE`, so its inbox empties. The README says so. Mailpit holds only
  dev mail and SMS OTPs, and a linking customer can ask for a new code. See low 1 on redpanda.
- **Zitadel provisioning.** `provision.sh` (header) runs on every create, recreate or restart. It looks up every object
  before creating it, and it regenerates client secrets only when `zitadel-clients.json` is missing or belongs to
  another instance. `PROVISION_MARKER_FILE` lives in the container on purpose, so a recreate reruns provisioning before
  the container reports healthy. Zitadel was already recreated on every deploy (new digest), so this path is not new.
- **Fixed IPs and the metadata rules.** The `DOCKER-USER` rules (deploy.sh:159-168) match the source addresses
  10.201.89.10/.11 and the subnet, not container IDs, so recreating a container leaves them working. Docker hands out
  addresses only from `ip_range: 10.201.89.128/25`, so no other container can take .10, .11 or .12 during a recreate.
  A container with a pinned `ipv4_address` has been recreated on every deploy so far (api .10, gateway .12, through
  their changing digests), so the collector's .11 uses a path that has already worked.
- **`--wait` and health checks.** The two flags work together; only `--no-recreate` conflicts with
  `--force-recreate`. Services in an inactive profile (`postgres`, `aspire-dashboard`, `redpanda-console`, `spa`) are
  not touched. otel-collector has no health check, so `--wait` waits only for it to be running. Redpanda and Mailpit
  have health checks.
- **Duration against the SSM document's 1800 s** (`modules/deploy-role/main.tf:161`). In AWS, redpanda, mailpit,
  otel-collector, core-banking and zitadel have no dependencies or only ones already met, so Compose restarts them in
  parallel. The critical path is unchanged: zitadel's provisioning (health check at most about 380 s), then the api,
  then the worker, and all three were already recreated. A redpanda restart (10 s default stop, then its health check)
  runs beside zitadel, and the worker also waits for the api. No new pressure on the timeout.

**README.** The new paragraph (README.md:259-261) is accurate: the stack is unavailable for the restart chain, which
was already true, and Mailpit's default storage is a temporary database removed on exit
(<https://mailpit.axllent.org/docs/configuration/runtime-options/>).

Pass 1 items that do not apply here: `Location` headers, timestamps, `Result`/ProblemDetails, the ownership filter,
stored events and replay. The change has no C# production code, so no `!`.

## Pass 2: simplicity and standards

- One documented Compose flag, chosen over a per-service content-hash label. The hash label would need a file list
  kept in step by hand, and a missed file brings the bug back. This is the smallest design that meets the criterion,
  and it can be explained in one sentence.
- Standards: `docker compose up --force-recreate`, Docker's documented way to recreate unchanged services
  (<https://docs.docker.com/reference/cli/docker/compose/up/>). Nothing here is home-grown.
- The new test overlaps the existing exact-call assertions. It names the criterion and asserts `--wait` stays. This
  is not a finding.

## Security checklist (below High-stakes)

- AuthN/Z, browser session, linking, data, audit, headers: not touched.
- Instance metadata isolation (AD-28): unchanged. The rules match addresses, the addresses are pinned, and `ip_range`
  keeps them reserved (above).
- Secrets: the change adds no output. Step 6 prints no value, and the "never prints a value it reads" header still
  holds. `AssertNoSecretShown` runs in the existing deploy tests. The builder ran `scripts/secret-scan.sh` (no leaks)
  and `scripts/security-local.sh` (Trivy fs and config, exit 0). Neither was re-run here.
- Supply chain: no new package or image.
- A side effect that helps security: a rewritten `rds-ca-bundle.pem`, `cloudfront-realip.conf` or collector config
  now always takes effect on the deploy that writes it.

## Findings

1. **Low**, `infra/deploy/aws/deploy.sh:174`. Redpanda now stops on every deploy, not only on a reboot, with
   Compose's default 10 s stop grace. In `--mode=dev-container`, Redpanda may acknowledge a write before it reaches
   disk. The relay uses `Acks.All` with idempotence (OutboxRelay.cs:87-88), and an acknowledged record is marked
   published in the outbox. If Redpanda's shutdown ever ran past 10 s, a SIGKILL could drop an acknowledged record
   the outbox will not resend. Unverified: dev-container mode's write-caching default in v26.2.6, and Redpanda's real
   shutdown time. The repo has no consumer, and an instance reboot already carries the same exposure. Fix: none needed
   for the dev stack. If it matters, add a line to `docs/production-path.md`, or give redpanda a `stop_grace_period`.
2. **Low**, `docs/architecture/architecture.md:476`. The living architecture's Deploy bullet still says the script
   "runs `docker compose up -d --wait`". The builder flagged only `ad-011` (line 52), which correctly stays as the
   historical record. Fix: change the architecture bullet to `up -d --wait --force-recreate`, in whichever pull
   request may edit `docs/architecture/`.

## Unverified by this review

- Neither the suite nor the security scripts were re-run. This review relies on the builder's results file (442/442)
  and the scan results in its build record.
- The real AWS deploy has not run with the flag. It needs the human's approval.
