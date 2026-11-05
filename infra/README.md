# infra

Everything the DevOps team owns: the local Compose platform (`docker-compose.yml` at the repo root), the AWS environment
(Terraform, the deploy script, the AWS gateway and collector config), identity provisioning, the database init, and the
tests of that code. Each top-level folder stands for one team's repository. Only Docker is needed locally:

```sh
docker compose up --wait        # about 30 s once images are built; fully configured, zero clicks
docker compose down -v          # stop and drop all data (Zitadel, Postgres, Redpanda, generated secrets)
scripts/test-lock.sh run stack <owner> -- infra/smoke.sh   # the developer smoke check
```

The smoke check starts and ends with `docker compose down -v`: it wipes the data of the shared `disputedesk`
stack, whichever worktree started it. Run it only under the `stack` lock.

| What | Where |
|---|---|
| App origin (the SPA; `/api/` -> api, `/api/health/*` -> api `/health/*`, `/otlp/v1/*` -> the Aspire OTLP/HTTP receiver) | <http://localhost:8080> |
| Identity origin (Zitadel API, console, Login v2 at `/ui/v2/login`) | <http://auth.localhost:8080> |
| Mailpit | <http://localhost:8025> |
| Aspire dashboard (anonymous, loopback only) | <http://localhost:18888> |
| Redpanda Console (optional, `console` profile, anonymous, loopback only; below) | <http://localhost:8081> |
| Postgres / Redpanda / OTLP for apps run on the host | `127.0.0.1:5433` / `localhost:19092` / `127.0.0.1:4317` (gRPC), `:4318` (HTTP) |

Redpanda Console (topics, messages and consumer groups in a browser) is optional and local only. It sits in the
`console` Compose profile, so it adds a container only when asked: a plain `docker compose up` never starts it, and
the AWS deploy never enables the profile (`ComposeRenderTests` and `DeployScriptTests` in `infra/tests` fail if either
would). It has no sign-in and listens on loopback only.

```sh
docker compose --profile console up -d --wait redpanda-console   # http://localhost:8081 (DISPUTEDESK_CONSOLE_PORT)
docker compose stop redpanda-console                              # stop it; the rest of the stack keeps running
docker compose --profile console down -v                          # down -v for the whole stack, the console included
```

Compose acts only on the services of the active profiles, so a plain `docker compose stop` or `down` leaves a running
console up; name it, or pass `--profile console`, as above.

Every host port except the gateway's 8080 can be moved when another process holds it, for example
`DISPUTEDESK_OTLP_HTTP_PORT=14318 docker compose up --wait`; the header of `docker-compose.yml` lists the variables.

The default network's subnet is pinned, because the api trusts forwarded headers only from it (AD-23):
`DISPUTEDESK_SUBNET`, default `10.201.89.0/24`, sets both the network and the api's `ForwardedHeaders__TrustedNetwork`.
A second stack beside this one (another worktree or a throwaway copy) sets another value, for example
`DISPUTEDESK_SUBNET=10.201.90.0/24`, since two networks on one Docker daemon cannot share a subnet. It also sets its own `COMPOSE_PROJECT_NAME` (the file names the project `disputedesk`, so without it the second stack replaces the first one's containers); the gateway's fixed 8080 still means only one stack runs at a time.

| Folder | Purpose |
|---|---|
| `db/` | Postgres init: `disputedesk` (app, owns its database), `zitadel` (non-superuser, own database), `qa_readonly` (SELECT on the app schema through default privileges, read-only sessions). |
| `nginx/` | The gateway image: the built SPA files, taken from the SPA image (`frontend/Dockerfile`) through the named build context `spa` (the build-only `spa` service in `docker-compose.yml`, `--build-context` in CI), served by unprivileged nginx. `templates/` holds the config, rendered from the `GATEWAY_*` variables in `docker-compose.yml` at start: two server blocks, fixed upstream Host per origin, security headers and CSP on the app origin, the coarse rate limit and the `/otlp/` proxy. |
| `zitadel/provision/` | One-shot, idempotent provisioner (`provision.sh`: project, roles, BFF app, operations machine user, fixture logins) and its read-only check of facts invisible from outside (`verify.sh`). |
| `probe/` | The Aspire dashboard image plus a static `wget` so Compose can healthcheck it (the api gets the same probe from the `api-local` stage in `backend/Dockerfile`). |
| `smoke.sh` | The black-box smoke check through the gateway: the stack is up and wired (the real Api's Scalar page, Zitadel's headers merged with the gateway's, provisioning, Postgres, Redpanda, Mailpit, the dashboard); every check fails the run. The gateway's own headers are `tests/`'s. |
| `aws/` | The AWS deploy and its offline checks (`compose.yml`, the AWS override merged onto the root `docker-compose.yml`, AD-28): `deploy.sh` runs on the instance through SSM (writes `.env`, fetches the RDS CA bundle, runs the database init on first deploy, blocks container access to instance metadata, starts Compose); `input-bounds.sh` holds each input's bound, shared with the deploy script; `compose.test.env` holds a dummy value for each input, for the tests in `tests/`; `test-realip.conf` is the trusted range the gateway tests in `tests/` mount. |
| `terraform/` | The AWS dev environment (never applied: there is no account): `bootstrap/` (the state bucket), `envs/dev/` with its mocked-provider tests (`tests/`, and `tests-oidc/` for the deploy role's GitHub subject, story 7.15), the modules (network, compute, database, edge, observability, deploy-role, budget), and `iam-names.test.sh`, which checks every IAM name against AWS's public service reference (story 7.16). See `terraform/README.md`. |
| `nginx/templates-aws/` | The AWS gateway's config, selected by `NGINX_ENVSUBST_TEMPLATE_DIR`: three server blocks (app, identity, Mailpit), each refusing requests without CloudFront's origin-verify header, with the client address and scheme from CloudFront's headers. |
| `otel-collector/` | The OpenTelemetry Collector config in AWS: OTLP in from the Api and the Worker, traces to X-Ray, logs and metrics to CloudWatch (AD-29). |
| `tests/` | `DisputeDesk.PlatformTests`, the xUnit tests of infra/'s files (ad-011): `GatewayTests` builds both gateway images and checks their responses and what each upstream receives on a throwaway Docker network (stories 7.4 and 7.12), the one place the gateway's own headers are checked; `ComposeRenderTests` renders the AWS override with the dummy inputs in `aws/compose.test.env` and checks the model and the collector config (story 7.4); `DeployScriptTests` runs `aws/deploy.sh` in a throwaway copy with its external commands stubbed (`PathStubs`), and tests each input's bound once (story 7.5); `DatabaseInitTests` runs it unchanged against a throwaway PostgreSQL 18 with TLS and a non-superuser master (story 7.17); `ShellHarnessTests` keeps shell test harnesses out of `infra/`; `PlatformScriptTests` runs the provisioning scripts with a stub `curl`, curls `provision.sh`'s retry options against an in-process stand-in for Zitadel, reads the Compose model from `docker compose config`, and reads a file only where no run can show the property. A standalone project with its own `global.json` (the SDK and Microsoft.Testing.Platform) and `.editorconfig`; nothing under `backend/` is referenced. CI's `platform` job runs it. |

## Tests

The infra tests need the .NET SDK pinned in `tests/global.json`, plus `bash`, `curl`, `jq` and Docker with Compose
v5.6.1 (as CI installs it; the Compose render is authoritative only on that version), and `openssl`. The gateway and
database-init tests start containers on the shared Docker daemon, so run them under the `stack` lock; they bind no host
port. Run them from `infra/tests`, where `dotnet` finds that `global.json`:

```sh
scripts/test-lock.sh run stack <owner> -- bash -c 'cd infra/tests && dotnet test --project DisputeDesk.PlatformTests'
```

The shell test of the IAM names (`terraform/iam-names.test.sh`) runs on its own until story 8.16 moves it here; see the
folder table above.

## Credentials

- Fixed dev-only values (database passwords, Zitadel master key, admin password) are inline defaults in
  `docker-compose.yml`, overridable from an untracked `.env`.
- Logins for customers A, B and C come from [`backend/fixtures/local.json`](../backend/fixtures/local.json). Sign in with the username.
  The Zitadel console admin is `admin` / `Dev-Only-Admin-1`.
- Zitadel's bootstrap PATs sit in two volumes: `zitadel-bootstrap` (the IAM_OWNER PAT, seen only by zitadel and
  the provisioner) and `zitadel-login-pat` (the login client's PAT, seen only by zitadel and zitadel-login;
  `scripts/dev-login.sh` reads it inside the zitadel-login container for one headless sign-in, never storing it).
- Generated values are written by the provisioner to `zitadel-clients.json` in the `zitadel-clients` volume, shaped
  as .NET configuration: the AD-26 keys `Zitadel:Issuer`, `Zitadel:MetadataAddress`, `Zitadel:ProjectId`,
  `Zitadel:Bff:ClientId` and `Zitadel:Bff:ClientSecret`, plus the operations tools' client credentials
  (`Zitadel:OperationsTools:*`, used by `smoke.sh`). The api container mounts it at
  `/run/zitadel/zitadel-clients.json` (owner 1654, the .NET images' app user, mode 0400). To read it from the host:

  ```sh
  docker compose run --rm --no-deps -T --entrypoint cat provision /out/zitadel-clients.json
  ```

  The provisioner never changes an app or authorization that already exists, so after a change to what it creates
  (for example the BFF app's roles, URIs or auth method), run `docker compose down -v` once to re-provision.

  The secrets are regenerated whenever the file is missing or describes another Zitadel instance, so
  `docker compose down -v` gives fresh ones.
