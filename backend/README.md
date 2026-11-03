# backend

Everything the backend team owns: the Api, the Worker, the core-banking stub and their tests, as one .NET 10
solution, `DisputeDesk.slnx` (decision record ad-011). Each top-level folder stands for one team's repository; the
rules are the root [AGENTS.md](../AGENTS.md) and [docs/conventions/backend.md](../docs/conventions/backend.md).

| Folder | What |
|---|---|
| `src/` | The Api and the Worker, in Clean Architecture layers (Domain, Application, Infrastructure, Contracts) |
| `stubs/` | The core-banking stub, a stand-in for the bank's core banking and the SIM-swap check |
| `tests/` | Domain unit tests, integration tests (Postgres through Testcontainers) and architecture tests |
| `fixtures/` | `local.json`, the synthetic seed data; [fixtures/README.md](fixtures/README.md) explains it |
| `http/` | Requests for the developer smoke check |

## Build and test

From `backend/`, with Docker running for the integration tests. These are the commands CI's `backend` job runs.

```sh
dotnet build DisputeDesk.slnx
git diff --exit-code -- openapi.json
dotnet format style DisputeDesk.slnx --verify-no-changes
dotnet format whitespace DisputeDesk.slnx --verify-no-changes
dotnet tool restore
dotnet ef migrations has-pending-model-changes --no-build \
  --project src/DisputeDesk.Infrastructure --startup-project src/DisputeDesk.Api
dotnet test --solution DisputeDesk.slnx
```

## What it publishes

Other folders read these, never the backend's source.

- `openapi.json`, the API contract. The build regenerates it and CI fails when it differs from the committed file.
- The images, one per `Dockerfile` target: `api`, `worker`, `api-local`, `worker-local` and `core-banking`.
- `backend/fixtures/local.json`, read by the stub, the seeder, Zitadel provisioning, `scripts/dev-login.sh` and `e2e/`.

## What it reads from the root

- `contracts/events/`, the JSON Schemas and samples of the published events.
- `docs/problems/`, the pages the ProblemDetails `type` URLs name.
- In the tests only, `docker-compose.yml` and `scripts/`; `ApiReferenceTests` also checks that the API's Scalar headers
  match `infra/images/gateway/templates/scalar-headers.conf.template`, a consistency check across teams. The provision and
  gateway-template cases in `PlatformScriptTests` move to `infra/tests/` in a later slice.
