# DisputeDesk

A customer portal for card disputes. A customer signs in, links their login to their bank record, picks a card
transaction and raises a dispute, then follows it to an outcome. Operations tools act on disputes through the API, an
SLA sweep escalates disputes left open too long, and every change is published as a dispute event.

It's built with .NET 10, React 19, PostgreSQL 18 and Redpanda, and runs as one Docker Compose stack with synthetic
data only ([the full stack](docs/architecture/architecture.md#stack)). Zitadel stands in for the bank's identity
platform. The same stack also runs on AWS.

## Run it

You need Docker with Compose v5.6.0 or later, and port 8080 free.

```sh
docker compose up --build --wait   # a few minutes the first time; returns once everything is healthy
```

| Open | URL |
|---|---|
| The app | <http://localhost:8080> |
| Sign-in (Zitadel) | <http://auth.localhost:8080> |
| Mailpit: emails and linking OTPs | <http://localhost:8025> |
| Aspire dashboard: traces, metrics, logs | <http://localhost:18888> |
| API reference (Scalar) | <http://localhost:8080/scalar/> |

The seed's dates are relative to start-up. To start over, after a day boundary or an e2e run, run
`docker compose down -v` (it deletes all data) and then `up` again.

## Try it

| Customer | Sign in with | What it shows |
|---|---|---|
| A | `thandi.nkosi` / `Dev-Only-Pass-A1` | Linked, with a dispute in every status |
| B | `pieter.vanwyk` / `Dev-Only-Pass-B1` | Linked, with one dispute that A can't see |
| C | `ayesha.patel` / `Dev-Only-Pass-C1` | Not linked. Linking is refused after a recent SIM swap: ID `9202295801081`, account `1000000031` |
| D | sign up a new login | Not linked, and passes every check: ID `8804125123080`, account `1000000041` |

These values are synthetic and dev-only, and the same logins work on AWS. The full seed is in
[backend/fixtures/](backend/fixtures/README.md).

1. As A, raise a dispute on a recent card debit, then withdraw it. Watch the timeline.
2. About 5 minutes after start-up, the SLA sweep escalates A's dispute on `TXN-A-0011`.
3. As B, open one of A's dispute links. It's *not found*.
4. As C, try to link. It's refused.
5. Sign up a new login and link it to D. The OTP arrives in Mailpit, which stands in for SMS here. A real deployment
   texts it to the cellphone on the bank's record.
6. Act as an operations tool: run `scripts/operations-token.sh`, paste the token into Scalar's `bearer` scheme, and
   call these on A's Submitted dispute on `TXN-A-0026`. Its ID is in A's browser URL (`/disputes/<id>`).

   | Request | Expect |
   |---|---|
   | `GET /api/v1/operations/disputes/{disputeId}` | 200, `submitted` |
   | `POST …/start-review`, then `…/escalate` | 200, `underReview`, then `escalated` |
   | `POST …/resolve` with `{"outcome": "upheld", "note": "Refund approved."}` | 200, `resolved` |
   | The same `resolve` again | 409, `action-not-allowed` |

Which action is allowed from which status is in the
[dispute lifecycle](docs/architecture/architecture.md#ad-13---the-dispute-aggregate-owns-every-rule-customers-see-a-mapped-view).

## On AWS

One environment, `dev`, runs the same stack in Cape Town (af-south-1). Its account is on the AWS Free plan: it can
run out of credits, but it can't run up a bill.

| Open | URL | Sign in with |
|---|---|---|
| The app | <https://d8189wokwcff7.cloudfront.net> | the [Try it](#try-it) logins |
| API reference (Scalar) | <https://d8189wokwcff7.cloudfront.net/scalar/> | nothing to read it; step 6's operations calls work only on the local stack |
| Mailpit | <https://d3qqsd2cm8xthz.cloudfront.net> | user `mailpit`, password `et2piRc46uyoseLmRZLexuNwhQXloE5RCEUjHJ6U2iFOQmZ27wXIEw3W2UGvcTHJ` |

Everyone reading this shares that inbox, and anyone can read what lands in it, password-reset emails included. Sign
up with a throwaway address. Because anyone can reset them, the shared logins may stop working, and customer D can be
linked only once, so step 5 works for the first reader to try it.

The layout, deploys and what traffic costs are in [infra/README.md](infra/README.md#deployed-on-aws). Standing it up
and checking it are in [infra/terraform/README.md](infra/terraform/README.md).

## Build and test

| Area | Command |
|---|---|
| Backend | `cd backend && dotnet test --solution DisputeDesk.slnx` (needs Docker) |
| Frontend | `cd frontend && npm ci && npm test` |
| Platform | `cd infra/tests && dotnet test --project DisputeDesk.PlatformTests` |
| End to end | `cd e2e && npm ci && npx playwright install chromium && npm run journeys` (on a fresh stack) |

CI ([ci.yml](.github/workflows/ci.yml)) runs every check on every pull request. The longer e2e regression suite
(`npm test`) runs when a pull request touches a path in [regression-paths.list](scripts/regression-paths.list). Each
area's README has its full command list.

## Simplifications

- The 90-day window counts from the transaction date. The card schemes allow 120 days from their processing date.
- The 10-day SLA is an internal trigger, not a promise to customers. No South African rule fixes a number of days.
- The scheme rule texts (Visa, Mastercard) aren't public, so the time limits come from consistent secondary sources.
- Linking rate limits live in the memory of the one Api instance. They reset on restart and aren't shared between
  instances.

The sources are in the [dispute-lifecycle research](docs/research/dispute-lifecycle/dispute-lifecycle.md), and
everything deferred is in the [production path](docs/production-path.md).

## Learn more

| To see | Read |
|---|---|
| How it's designed, and why | [architecture](docs/architecture/architecture.md), [decision records](docs/decisions/) |
| How it was built: planning, AI agents and the checks they pass | [docs/README.md](docs/README.md#how-it-was-built) |
| The repo map and working on the code | [onboarding](docs/onboarding.md), [AGENTS.md](AGENTS.md) |
| Why one repo holds three teams' folders | [ad-011](docs/decisions/ad-011-team-folders-in-one-repo.md) |
| Running the local stack | [runbook](infra/deploy/runbook.md) |
| What's deferred, and the accepted risks | [production path](docs/production-path.md) |

## Where each quality area is met

| Quality area | Where |
|---|---|
| Architecture and scalability | [architecture](docs/architecture/architecture.md), [layer tests](backend/tests/DisputeDesk.ArchitectureTests/LayerTests.cs) |
| Resilience and operational readiness | [outbox relay](backend/src/DisputeDesk.Infrastructure/Outbox/OutboxRelay.cs), [Compose health checks](docker-compose.yml), [runbook](infra/deploy/runbook.md) |
| Data and domain modelling | [ad-009 stored events](docs/decisions/ad-009-stored-events.md), [domain tests](backend/tests/DisputeDesk.Domain.Tests/) |
| API design | [openapi.json](backend/openapi.json), [problem types](docs/problems/) |
| Security (primary) | [ad-007 browser session](docs/decisions/ad-007-browser-session.md), [ad-008 linking](docs/decisions/ad-008-linking.md), [gateway](infra/images/gateway/) |
| Code quality and maintainability | [AGENTS.md](AGENTS.md), [build rules](backend/Directory.Build.props), [ESLint config](frontend/eslint.config.js) |
| Testing | [backend tests](backend/tests/), [e2e journeys](e2e/tests/journeys/) |
| Observability | [telemetry](backend/src/DisputeDesk.Infrastructure/Observability/Telemetry.cs), [AD-20](docs/architecture/architecture.md#ad-20---one-trace-from-browser-to-the-relay-named-signals-adopted) |
| Infrastructure and DevOps | [Compose stack](docker-compose.yml), [CI](.github/workflows/ci.yml), [security scans](.github/workflows/security.yml), [Terraform](infra/terraform/) |
| Documentation and communication | [docs/](docs/README.md), [decision records](docs/decisions/), [production path](docs/production-path.md) |
