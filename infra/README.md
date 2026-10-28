# infra

Configuration for the local Compose platform (`docker-compose.yml` at the repo root). Only Docker is needed:

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
| Postgres / Redpanda / OTLP for apps run on the host | `127.0.0.1:5433` / `localhost:19092` / `127.0.0.1:4317` (gRPC), `:4318` (HTTP) |

| Folder | Purpose |
|---|---|
| `db/` | Postgres init: `disputedesk` (app, owns its database), `zitadel` (non-superuser, own database), `qa_readonly` (SELECT on the app schema through default privileges, read-only sessions). |
| `nginx/` | The gateway image: the SPA built in a Node stage, served by unprivileged nginx. `templates/` holds the config, rendered from the `GATEWAY_*` variables in `docker-compose.yml` at start: two server blocks, fixed upstream Host per origin, security headers and CSP on the app origin, the coarse rate limit and the `/otlp/` proxy. |
| `zitadel/provision/` | One-shot, idempotent provisioner (`provision.sh`: project, roles, BFF app, operations machine user, fixture logins) and its read-only check of facts invisible from outside (`verify.sh`). |
| `probe/` | The Aspire dashboard image plus a static `wget` so Compose can healthcheck it (the api gets the same probe from the `api-local` stage in `backend/Dockerfile`). |
| `smoke.sh` | The black-box smoke check through the gateway; every check fails the run. |

## Credentials

- Fixed dev-only values (database passwords, Zitadel master key, admin password) are inline defaults in
  `docker-compose.yml`, overridable from an untracked `.env`.
- Logins for customers A, B and C come from [`fixtures/local.json`](../fixtures/local.json). Sign in with the username.
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
