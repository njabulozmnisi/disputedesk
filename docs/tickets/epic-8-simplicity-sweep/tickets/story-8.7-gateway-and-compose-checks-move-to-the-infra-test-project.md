---
id: 7
type: story
title: "Gateway and Compose checks move to the infra test project, edge headers checked once"
parent: epic-8-simplicity-sweep
covers: [R32, R35, R37]
after: [1]
hitl: false
risk: medium
decisions: settled
refined: true
---

# Gateway and Compose checks move to the infra test project, edge headers checked once

## Description

First half of slice S6 of the mitigation plan (wave 2). Infra has three test styles today: shell harnesses, the backend's `PlatformScriptTests` and the ad-011 xUnit project `infra/tests/DisputeDesk.PlatformTests` (INF-1). The local gateway's security headers and the 429 are asserted in `infra/smoke.sh`, in `infra/aws/gateway.test.sh` and in the C# template tests, so a header change means three edits (INF-9). Several C# cases match the exact text of `docker-compose.yml` or an nginx template, whitespace included, so a reformat breaks them and a behaviour change written differently passes (TST-7). This story moves the gateway harness into the infra test project as xUnit tests that start both gateway images and assert on their responses, makes it the one place that checks the gateway's own headers and what each upstream receives, trims `smoke.sh` to "the stack is up and wired", and turns the Compose text matches into assertions on the rendered Compose model, each in the project ad-011 assigns it. 8.8 moves the remaining harnesses; 8.9's nginx change is proven by this story's tests.

## Requirements

Verify: `dotnet test --project DisputeDesk.PlatformTests` passes in `infra/tests/` locally and in CI's *Infra tests* step; the backend `ArchitectureTests` pass locally and in CI's `backend` job; `infra/smoke.sh` passes on a fresh stack under `scripts/test-lock.sh run stack <owner> -- infra/smoke.sh` (developer smoke check; it runs `docker compose down -v` on the shared stack); `infra/aws/gateway.test.sh` no longer exists.

Inputs: none (no endpoint change). The tests read the gateway images built from `frontend/` and `infra/nginx/` exactly as `gateway.test.sh:44-50` builds them, and the model `docker compose config --format json` renders for `docker-compose.yml` alone, with Compose 5.6.1, `--env-file` pointing at an empty file and the child process given only the environment the test sets.

1. **One process helper in the infra test project** (INF-1; R35)
   **Given** `infra/tests/DisputeDesk.PlatformTests`
   **When** a test runs `docker`, `docker compose`, `bash` or `curl`
   **Then** it goes through one helper that starts the process without a shell, passes arguments as a list, sets the child's environment explicitly, reads standard output and standard error while it waits (so a full pipe never blocks it), applies a timeout with `Process.WaitForExitAsync` and kills the process tree when it passes, and returns the exit code and both outputs; every container and network a test class starts is removed when the class ends, also on failure
   **And** the gateway network keeps the harness's documentation subnet `192.0.2.0/24` (RFC 5737, which `test-realip.conf` trusts); if the network cannot be created, the fixture fails naming the subnet

2. **The gateway cases run as xUnit tests against both images** (INF-1; R32, NFR-SEC-7)
   **Given** every section of `infra/aws/gateway.test.sh` (story 7.4 criteria 3, 4, 6 and 7; story 7.12 criteria 1 to 4, 6 and 7), its 429 and 413 checks included
   **When** this story lands
   **Then** each section is one or more xUnit tests in a `GatewayTests` class that starts the echo upstream, Mailpit, an OTLP stand-in (an echo answering under the collector's name), the AWS and the local gateway and the two clients on a throwaway Docker network with no host port, as the script does, and asserts the same responses; `gateway.test.sh` and the CI step *AWS gateway* (`ci.yml:148-149`) are deleted

3. **The gateway's own headers are checked in one place** (INF-9; R32)
   **Given** `infra/smoke.sh`'s sections *gateway: the SPA and security headers, each exactly once* (`smoke.sh:61-99`) and *gateway: the coarse limit's 429* (`:132-169`)
   **When** this story lands
   **Then** every assertion in them that `GatewayTests` does not already make moves there: each security header exactly once and the cache headers on the SPA; and on `/api/limit-probe.json` and `/scalar/` (the gateway answers both 429s itself) the 429 with exactly one `Content-Type: application/problem+json`, `Retry-After: 1`, exactly one `Cache-Control: no-store`, each security header once and the SPA's CSP, and a body that is the gateway's fixed RFC 9457 problem (its `type`, `title` and `status` 429, as the template writes it) with no `traceId`; the two sections are removed from `smoke.sh`
   **And** `smoke.sh` keeps the sections a stand-in upstream cannot prove: *gateway: Scalar and the runtime document* (`:100-131`: `servers[0].url`, no `<base>`, the assets' 200, each header once on the real Api's Scalar page) and the identity origin's headers merged with Zitadel's own (`:170-203`), because a test stand-in must send the real image's headers (review-story pitfall, 7.12) and the echo upstream does not; it also keeps readiness through the gateway, the issuer and Login v2, the operations token, the BFF app, the provisioner facts, idempotent provisioning, the Postgres roles, the topic, Mailpit and the dashboard

4. **Gateway template rules are proved by responses** (TST-7, INF-9; R32)
   **Given** `GatewayTemplate_ApiLocations_SendTheGatewaysOwnErrorsWithNoStore` and `GatewayTemplate_ProxiedLocations_DropTheClientsForwardedHeader` (`infra/tests/DisputeDesk.PlatformTests/PlatformScriptTests.cs`, text matches over both templates)
   **When** this story lands
   **Then** on both running gateways, a 429, a 413 and a 502 the gateway generates on `/api/` and on `/api/health/` carry exactly one `Cache-Control: no-store`, and an upstream's own answer keeps its own single `Cache-Control`; the status list of the `error_page … @gateway_error` line (413, 500, 502, 504) stays a text check, with a one-line comment that a 504 needs a 60-second upstream stall per image (nginx's default `proxy_read_timeout`, which no template sets), so the directive's list is the contract
   **And** for every proxied location of both gateways, the echoed request shows the `Host`, `X-Forwarded-Host`, `X-Forwarded-Proto` and `X-Forwarded-For` the location sends today (an expected set per route, read from today's templates, with today's exceptions: `/otlp/` sends no `X-Forwarded-Host` or `X-Forwarded-Proto`, the AWS Mailpit location no `X-Forwarded-Host`), never a client's `Forwarded` or `X-Origin-Verify`; on the local `/otlp/v1/traces` the OTLP stand-in receives no `Cookie` and no `Authorization` sent by the client; the text versions are deleted
   **And** `GatewayTemplate_EveryAccessLog_RecordsThePathWithoutTheQueryOrTheReferer` stays a text check, with a one-line comment that the `log_format` definition is the contract

5. **Compose checks read the rendered model, in the project ad-011 assigns** (TST-7; R37)
   **Given** the Compose text matches in `backend/tests/DisputeDesk.ArchitectureTests/PlatformScriptTests.cs` (`Compose_ZitadelExternalSecure_ComesFromConfiguration`, `Compose_PinnedDefaultNetworkSubnet_IsTheApisTrustedNetwork` with its `ForwardedHeaders__AllowedHosts` line at `:81`, `Compose_DotNetService_RunsUnderAnInitProcess`, `Compose_GatewayHealth_WaitsUntilItReachesTheApi`, `Compose_WorkerHealthCheck_FailsWhenTheHeartbeatIsOlderThanItsThreshold` at `:143`), which read only the root `docker-compose.yml`, and `Compose_Api_StartsAfterTheGatewayThatHoldsTheIssuerAlias` in the infra project, which also reads `infra/nginx/templates/nginx.conf.template`
   **When** they run
   **Then** each reads `docker compose -f docker-compose.yml config --format json` (rendered as Inputs says) with `System.Text.Json` and asserts on the model (for example `services.api.depends_on.gateway.condition == "service_started"`, `services.api.init == true`, the default network's subnet equal to the Api's trusted network, `services.api.environment.ForwardedHeaders__AllowedHosts == "localhost"`, the worker health check's command holding each heartbeat file and threshold), with the same expected values as today and no whitespace or indentation in any assertion
   **And** where a case checks that a value comes from a variable with a default (`${ZITADEL_EXTERNALSECURE:-false}`, `${DISPUTEDESK_SUBNET:-10.201.89.0/24}`), it renders once with the variable unset and once set, and asserts the value follows it
   **And** the root-file cases stay in the backend `ArchitectureTests` (ad-011 change 4: cases reading root files stay in backend), and CI's `backend` job gains the same pinned *Install Compose* step the `platform` job runs (`ci.yml:18-23,125-137`: Compose 5.6.1 and its SHA-256), because the runner's own Compose is older and refuses the gateway's `service:spa` build context; `Compose_Api_StartsAfter…` stays in the infra project

6. **Script text checks stay only where a run cannot show the property** (TST-7; R35)
   **Given** the remaining text matches over `provision.sh`, `scripts/dev-login.sh` and `scripts/operations-token.sh` in both `PlatformScriptTests` files, apart from `ProvisionScript_ClientsFile_MapsTheMachineUserIdToTheOperationsActor` (`infra/tests/…/PlatformScriptTests.cs:41`) and `…_CurrentCheck_AlsoRequiresTheActorMapping` (`:49`), which 8.12 rewrites
   **When** this story lands
   **Then** each one whose subject a run with a `curl` or `docker` stub first on `PATH` can observe (a request the script sends, the arguments of a call such as `curl --max-time`, an exit code, a message) asserts on that run, as the provisioning retry tests do (`ProvisionScript_RetryOptions_…`): the stub records its arguments, answers as the test sets, and fails with a message naming any call the test did not expect; each run's expected calls, exit code and output are stated in the test
   **And** a check stays text only where no run can show it (for example that a script never contains a literal secret or a browser call), with a one-line comment naming why

## Boundaries

- Must not change: the nginx templates, the gateway Dockerfile, `docker-compose.yml`, `infra/aws/compose.yml`, any script under test; what each moved case asserts.
- Not here: `deploy.test.sh`, `rds-init.test.sh`, `check-compose.sh` and 8.8's shared PATH-stub helper (this story's stubs live in the tests that need them; 8.8 folds them into its helper); `iam-names.test.sh` (8.16); the two provisioning actor-mapping cases (8.12).
- No bats-core: the plan picks the ad-011 xUnit project, which already exists, so infra keeps one style.

## Supervisor checks

- Throwaway copy: add a second `X-Content-Type-Options` to the local `security-headers.conf.template`; a `GatewayTests` case fails naming the header.
- Throwaway copy: remove `Cache-Control "no-store"` from the AWS `@gateway_error` location; criterion 4's 502 case fails.
- Throwaway copy: add `proxy_set_header X-Test 1;` to the local `/api/` location only; criterion 4's per-route case fails (the location no longer drops `Forwarded`).
- Throwaway copy: remove `proxy_set_header Cookie "";` from the local `/otlp/` location; criterion 4's OTLP case fails.
- Throwaway copy: rewrite the `api` service's `depends_on: { gateway: { condition: service_started } }` in block style; criterion 5's tests still pass (the model is unchanged).
- Throwaway copy: set `init: false` on `worker`; criterion 5's test fails.
- Throwaway copy: drop `--max-time` from one `curl` call in `scripts/operations-token.sh`; criterion 6's run-based case fails.

## Build facts

- Owner: backend (backend-builder; `infra/`, `backend/tests/` and `.github/workflows/ci.yml` are its paths).
- Tier: Small, test-only (plus one CI install step).
- Flags: none.
- Lane: A, after 8.1 (shellcheck runs on `smoke.sh`); 8.5 merges before it in lane A's order (Collision).
- Mechanisms and sources:
  - `docker compose config --format json`, `--env-file`: <https://docs.docker.com/reference/cli/docker/compose/config/>; <https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/>.
  - `System.Diagnostics.Process` with `ArgumentList`, `Environment` and `WaitForExitAsync`: <https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist>; <https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.environment>; <https://learn.microsoft.com/dotnet/api/system.diagnostics.process.waitforexitasync>.
  - xUnit class fixtures for shared setup and teardown: <https://xunit.net/docs/shared-context>.
  - nginx `proxy_set_header` and `add_header` inheritance, `proxy_read_timeout` default: <https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_set_header>; <https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_read_timeout>.
  - Problem details: RFC 9457, <https://www.rfc-editor.org/rfc/rfc9457>.
- Packages: none (the project exists; `System.Text.Json` is in the BCL). Compose 5.6.1 is already pinned in `ci.yml`.
- Touches: `infra/tests/DisputeDesk.PlatformTests/` (new `GatewayTests.cs`, a process helper, `PlatformScriptTests.cs`), `backend/tests/DisputeDesk.ArchitectureTests/PlatformScriptTests.cs` (the `Compose_*` and script cases), `infra/aws/gateway.test.sh` (deleted), `infra/smoke.sh`, `infra/README.md` (the `aws/` and `tests/` rows), `.github/workflows/ci.yml` (*AWS gateway* step removed; *Install Compose* added to the `backend` job; the *Infra tests* step's timeout if the gateway tests need it).
- After merge: `scripts/verify-with-copilot.sh` allows `shell(infra/aws/gateway.test.sh)`; the supervisor drops it as a Small-tier tooling fix.
- Collision: 8.5 rewrites `ApiProgram_ForwardedHeaders_RunFirstInThePipeline` in the same backend `PlatformScriptTests.cs` and merges first; this story rebases onto it. 8.8 adds to the infra project in different files. 8.9 changes the nginx templates these tests prove, after this story. 8.12 changes the two provisioning actor-mapping cases.

## Pre-build

- Challenger: `docs/verification/8.7-challenge.md` (rethink).
- Review: `docs/verification/epic-8-prebuild-wave2b.md`, section 8.7. No security read.
- Decisions: the challenger's one `[decision]` point (keep the root-file Compose cases in backend) was dismissed by the human on 2026-11-04 as not a decision, because `Compose_Api_StartsAfterTheGatewayThatHoldsTheIssuerAlias` reads an infra file and moved correctly (`docs/verification/epic-8-decisions.md`). The root-file cases stay in backend as the point asked, which is ad-011 change 4 as written (criterion 5).
- Sofia, 2026-11-04: every `[story]` point folded. Review: the pinned Compose reaches the root-file cases by an install step in the `backend` job instead of a move, which ad-011 change 4 would forbid (criterion 5); an empty `--env-file` and a set environment (Inputs, criterion 1); Scalar and the identity origin stay in `smoke.sh`, with why the echo cannot stand in (criterion 3); the `/otlp/` `Cookie` and `Authorization` case here, with an OTLP stand-in (criteria 2, 4); `Compose_WorkerHealthCheck_…` and the `ForwardedHeaders__AllowedHosts` line listed, the two actor-mapping cases left to 8.12, the order with 8.5 and the `stack` lock (criteria 5, 6, Verify, Collision). Challenger: real-stack wiring checks kept and the gateway's own headers moved (criterion 3); one fixture and a process helper that drains both streams (criterion 1); the subnet's availability (criterion 1); 504 kept as the `error_page` list with the reason, both `/api/` locations, 413 and the upstream's answer kept (criterion 4); the 429 body's fields and no `traceId` (criterion 3); `--max-time` observed through a recording stub, the stub runs bounded (criterion 6). For 8.9: per-route forwarded values with the `/otlp/` and Mailpit exceptions (criterion 4).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- audit — docs/verification/simplicity-audit/infra-ci-scripts.md, INF-1, INF-9; tests.md, TST-7
- plan — docs/verification/simplicity-audit/mitigation-plan.md, wave 2, S6
- decision — docs/decisions/ad-011-team-folders-in-one-repo.md (change 4, the infra test project and the root-file cases)
- stories — docs/tickets/epic-7-release/tickets/story-7.4-aws-compose-override-and-opentelemetry-collector.md; story-7.12-gateway-and-edge-hardening.md

## Notes

- Risk medium (proposed): the gateway cases guard the edge's security headers; each moved case must fail on the same break as before (Supervisor checks). Check beyond the criteria: the supervisor runs the Supervisor checks on both images in a throwaway copy.
- Decision: 2026-11-04 (Sofia) — S6 is split: this story takes the gateway, header and Compose checks (INF-9 and TST-7), 8.8 the deploy, database-init and Compose-render harnesses (INF-1's rest and INF-10), so each is one session.
- Decision: 2026-11-04 (Sofia) — the root-file Compose cases stay in backend with a pinned Compose in the `backend` job, not moved to the infra project as the review suggested: ad-011 change 4 keeps cases that read root files in backend, and the install step is the pattern every Compose job already uses.
- Decision: 2026-11-04 (Sofia) — the Scalar section and the identity-origin headers stay in `smoke.sh`, the review's first option: they prove how the gateway meets the real Api and Zitadel, which an echo cannot imitate.
- Decision: 2026-11-04 (Sofia) — 504 is not induced: the gateway's 502 by response proves `@gateway_error` adds `no-store`, and the status list on its `error_page` line stays the contract, which saves a 60-second stall per image in a 15-minute job.
