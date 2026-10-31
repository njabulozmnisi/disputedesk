# DisputeDesk

## Overview

DisputeDesk is a customer portal for card disputes. A customer signs in, links their login to their bank record, sees
their transactions, raises a dispute on a disputable transaction, and follows it through Submitted, Under review,
Escalated, Resolved or Withdrawn. Operations tools act on disputes through the API, an SLA sweep escalates disputes left
open too long, and every dispute change is published as a dispute event. It runs locally as one Docker Compose stack
with synthetic data only.

## Prerequisites

- Docker running, with Compose v2 (`docker compose`).
- Port 8080 free: the gateway's port is fixed. The other host ports can be moved, as [infra/README.md](infra/README.md)
  says.
- `curl` and `jq`, for the Operations section.
- To build and test outside Docker: the .NET SDK from [backend/global.json](backend/global.json) and Node from
  [.nvmrc](.nvmrc) ([onboarding](docs/onboarding.md)).

## Run

```sh
docker compose up --wait
```

It returns once every service is healthy: migrations applied, Zitadel configured, seed data written. Plain
`docker compose up` stays in the foreground, so run the later commands in a second terminal.

## Origins

| What | Where |
|---|---|
| The app (SPA and API) | <http://localhost:8080> |
| Identity (Zitadel sign-in and console) | <http://auth.localhost:8080> |
| Mailpit (verification emails and linking OTPs) | <http://localhost:8025> |
| Aspire Dashboard (traces, metrics, logs) | <http://localhost:18888> |

[infra/README.md](infra/README.md) has the rest: host ports for Postgres and Redpanda, the credentials and the
smoke check.

## Build and test

These are the build, lint and test commands CI runs ([.github/workflows/ci.yml](.github/workflows/ci.yml)). Start each
block from the repo root and change into the folder it names.

Backend, in `backend/`. The backend tests start Postgres through Testcontainers, so Docker must be running.

```sh
dotnet build DisputeDesk.slnx
dotnet format style DisputeDesk.slnx --verify-no-changes
dotnet format whitespace DisputeDesk.slnx --verify-no-changes
dotnet test --solution DisputeDesk.slnx
```

Frontend, in `frontend/`:

```sh
npm ci
npm run typecheck
npm run lint
npm run format
npm test
```

End-to-end, in `e2e/`, with Playwright against the running stack. The two projects each need a fresh stack:
`journeys` (the user journeys, which CI runs) and the regression project (`npm test`, every other spec).

```sh
npm ci
npx playwright install chromium
docker compose down -v
docker compose up --build --wait
npm run journeys
docker compose down -v
docker compose up --build --wait
npm test
```

On Linux, `npx playwright install --with-deps chromium` also installs the browser's system libraries, as CI does.

## Seed identities

Every value comes from [fixtures/local.json](fixtures/local.json) (FR-29). All data is synthetic, and the passwords are
dev-only fixture values. Sign in with the username. The ID and account numbers are what linking asks for.

| Customer | Login (username / password) | ID number | Account numbers | What it shows |
|---|---|---|---|---|
| A, `CUS-000001` | `thandi.nkosi` / `Dev-Only-Pass-A1` | already linked | already linked | Linked; seeded disputes in every status and outcome |
| B, `CUS-000002` | `pieter.vanwyk` / `Dev-Only-Pass-B1` | already linked | already linked | Linked; one Submitted dispute, which A gets 404 for |
| C, `CUS-000003` | `ayesha.patel` / `Dev-Only-Pass-C1` | `9202295801081` | `1000000031` | Not linked; the SIM-swap check fails, so linking is refused |
| D, `CUS-000004` | none | `8804125123080` | `1000000041`, `1000000042` | Not linked and passes every check: a new sign-up links to D |

To link a new login, sign up at <http://localhost:8080>, then enter D's ID number and one of D's account numbers; the
OTP arrives in Mailpit.

The SLA demo dispute is customer A's on `TXN-A-0011`. It is seeded Under review and falls due 5 minutes after start-up,
so the SLA sweep escalates it a few minutes after `docker compose up --wait` returns.

The seed's dates are relative to start-up. After a day boundary, or to start again from the seed, reset with
`docker compose down -v` (it deletes every volume; see the [runbook](docs/runbook.md)).

An AWS deployment from `infra/terraform/` (story 7.5) uses the same fixture logins and published passwords, and holds
only synthetic data.

## Operations

Run these from the repo root.

Operations tools call the operations routes with a bearer token from the OAuth 2.0 client-credentials grant. Here
Scalar stands in for another team's operations tool (NFR-DOC-4). Scalar holds no credential, and the Api serves it only
when `Scalar:Enabled` is true, as it is in Compose.

These steps act on customer A's seeded Submitted dispute on `TXN-A-0026`, as a fresh stack holds it. The e2e tests act
on seeded disputes, so after them reset first (`docker compose down -v`, then `docker compose up --wait`).

1. Find the dispute ID. Seeded IDs are generated at seeding, so either sign in as customer A, open the dispute in the
   history and copy the GUID from `/disputes/<disputeId>`, or read it with the read-only database role:

   ```sh
   docker compose exec postgres psql -U qa_readonly -d disputedesk -c "SELECT \"Id\" FROM \"Disputes\" WHERE \"TransactionId\" = 'TXN-A-0026';"
   ```

2. Get a token. It is printed to the terminal:

   ```sh
   scripts/operations-token.sh
   ```

3. Open <http://localhost:8080/scalar/>, paste the token into the `bearer` scheme, and call, in order:

   | Request | Expected |
   |---|---|
   | `GET /api/v1/operations/disputes/{disputeId}` | 200, `status` `submitted` |
   | `POST /api/v1/operations/disputes/{disputeId}/start-review` | 200, `status` `underReview` |
   | `POST /api/v1/operations/disputes/{disputeId}/escalate` | 200, `status` `escalated` |
   | `POST /api/v1/operations/disputes/{disputeId}/resolve` with the body below | 200, `status` `resolved` |
   | The same `resolve` with the same body again | 409, problem `type` ending `action-not-allowed.md` |

   The `resolve` body is JSON, sent with `Content-Type: application/json`; `outcome` is `upheld` or `rejected`, and
   `note` is 1 to 500 characters:

   ```json
   {"outcome": "upheld", "note": "Refund approved."}
   ```

The same calls with `curl`. The first line sets `DISPUTE_ID` with step 1's lookup. Each call gets a fresh token. The
token is in curl's arguments, visible in `ps`, which is acceptable on a single-user dev machine.

```sh
DISPUTE_ID=$(docker compose exec -T postgres psql -U qa_readonly -d disputedesk -Atc "SELECT \"Id\" FROM \"Disputes\" WHERE \"TransactionId\" = 'TXN-A-0026';")
curl -sS -w '\n%{http_code}\n' -H "Authorization: Bearer $(scripts/operations-token.sh)" http://localhost:8080/api/v1/operations/disputes/$DISPUTE_ID
curl -sS -w '\n%{http_code}\n' -X POST -H "Authorization: Bearer $(scripts/operations-token.sh)" http://localhost:8080/api/v1/operations/disputes/$DISPUTE_ID/start-review
curl -sS -w '\n%{http_code}\n' -X POST -H "Authorization: Bearer $(scripts/operations-token.sh)" http://localhost:8080/api/v1/operations/disputes/$DISPUTE_ID/escalate
curl -sS -w '\n%{http_code}\n' -X POST -H "Authorization: Bearer $(scripts/operations-token.sh)" -H 'Content-Type: application/json' -d '{"outcome": "upheld", "note": "Refund approved."}' http://localhost:8080/api/v1/operations/disputes/$DISPUTE_ID/resolve
curl -sS -w '\n%{http_code}\n' -X POST -H "Authorization: Bearer $(scripts/operations-token.sh)" -H 'Content-Type: application/json' -d '{"outcome": "upheld", "note": "Refund approved."}' http://localhost:8080/api/v1/operations/disputes/$DISPUTE_ID/resolve
```

## Runbook

[docs/runbook.md](docs/runbook.md) has the operator procedures for the local stack: releasing a `Failed` outbox row,
core banking unavailable, and resetting the stack.

## Onboarding

[docs/onboarding.md](docs/onboarding.md) has the prerequisites for working on the code, a map of the repo and the
story flow.

## Simplifications

- **The 90-day window.** A customer may dispute a card transaction for 90 days from the transaction date. The card
  schemes allow 120 days from their own processing date, so counting from the transaction date is a simplification
  ([dispute-lifecycle research](docs/research/dispute-lifecycle/dispute-lifecycle.md), recommendation 1).
- **The SLA is an internal trigger, not a promise.** After 10 calendar days an open dispute is escalated. No South
  African rule fixes a number of days: the National Financial Ombud Scheme gives banks 21 working days on referral, and
  the Conduct Standard for Banks asks for a reasonable time
  ([dispute-lifecycle research](docs/research/dispute-lifecycle/dispute-lifecycle.md), recommendation 2).
- **The primary scheme rule texts were not read.** The Visa and Mastercard rules are not public; the time limits come
  from consistent secondary sources
  ([dispute-lifecycle research](docs/research/dispute-lifecycle/dispute-lifecycle.md), caveat).
- **Linking rate limits live in memory** in the one Api instance: they reset when the Api restarts and would not add up
  across several instances (FR-8, NFR-ARC-3).

Everything deferred is in [docs/production-path.md](docs/production-path.md).

## Ten-area map

| Quality area | Where it is met |
|---|---|
| 5.1 Architecture and scalability | [architecture](docs/architecture/architecture.md), [layer tests](backend/tests/DisputeDesk.ArchitectureTests/LayerTests.cs) |
| 5.2 Resilience and operational readiness | [outbox relay](backend/src/DisputeDesk.Infrastructure/Outbox/OutboxRelay.cs), [Compose health checks](docker-compose.yml), [runbook](docs/runbook.md) |
| 5.3 Data and domain modelling | [ad-009 stored events](docs/decisions/ad-009-stored-events.md), [domain tests](backend/tests/DisputeDesk.Domain.Tests/) |
| 5.4 API design | [openapi.json](backend/openapi.json), [problem types](docs/problems/) |
| 5.5 Security (primary) | [ad-007 browser session](docs/decisions/ad-007-browser-session.md), [ad-008 linking](docs/decisions/ad-008-linking.md), [gateway](infra/images/gateway/) |
| 5.6 Code quality and maintainability | [AGENTS.md](AGENTS.md), [build rules](backend/Directory.Build.props), [ESLint config](frontend/eslint.config.js) |
| 5.7 Testing | [backend tests](backend/tests/), [e2e journeys](e2e/tests/journeys/) |
| 5.8 Observability | [telemetry](backend/src/DisputeDesk.Infrastructure/Observability/Telemetry.cs), [architecture AD-20](docs/architecture/architecture.md) |
| 5.9 Infrastructure and DevOps | [Compose stack](docker-compose.yml), [CI](.github/workflows/ci.yml), [security scans](.github/workflows/security.yml) |
| 5.10 Documentation and communication | [decision records](docs/decisions/), [production path](docs/production-path.md), [onboarding](docs/onboarding.md) |
