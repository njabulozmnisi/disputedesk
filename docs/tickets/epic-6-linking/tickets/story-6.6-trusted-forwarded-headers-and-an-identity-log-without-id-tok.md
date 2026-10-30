---
id: 6
type: story
title: "Trusted forwarded headers and an identity log without id_token_hint"
parent: epic-6-linking
covers: [R8, R31]
after: [1]
refined: false
hitl: false
risk: high
---

# Trusted forwarded headers and an identity log without id_token_hint

## Description

Trusts forwarded headers only from the Compose network's configured range through `ForwardedHeadersOptions.KnownIPNetworks`, with the default network's subnet pinned in `docker-compose.yml` and `ForwardedHeadersOptions.AllowedHosts` holding the public app host (AD-23). It also stops the identity origin's access log from recording the end-session `id_token_hint` (carried from 2.8's security review; AD-20). Both criteria moved unchanged from story 6.3, so this part builds beside 6.5 and 6.2.

## Requirements

Verify: Integration tests prove forwarded headers are trusted only from the configured range, only the public app host is taken from `X-Forwarded-Host`, and the two keys are validated at start-up; an architecture test proves the pinned Compose subnet equals the configured range and the identity origin's log format; `openapi.json` does not change.

1. **Forwarded headers only from the configured range** (R8; FR-8, AD-23)
   **Given** `ForwardedHeaders:TrustedNetwork` holding the Compose default network's pinned subnet (`10.201.89.0/24`)
   **When** a request from an address outside it carries `X-Forwarded-For: 203.0.113.9`, and a request from an address inside it carries the same header
   **Then** the first keeps the connection address as its client IP and the second takes `203.0.113.9`, through `ForwardedHeadersOptions` (Microsoft Learn, *Configure ASP.NET Core to work with proxy servers and load balancers*): `KnownProxies` and the loopback default in `KnownIPNetworks` are cleared, `KnownIPNetworks` holds only the configured range (`KnownNetworks` is obsolete in .NET 10.0.14, ASPDEPR005, and fails the build), `ForwardLimit` is 1, For, Proto and Host are processed, and `UseForwardedHeaders` runs first in the pipeline
   **And** `ForwardedHeadersOptions.AllowedHosts` holds only the public app host without a port (`localhost` in Compose), so another `X-Forwarded-Host` is ignored; host filtering stays as it is, so the in-container health check still passes
   **And** both keys are bound with `ValidateOnStart`: a missing or unparsable range (`IPNetwork.TryParse`) or an empty host stops the Api with an error naming the key (Microsoft Learn, *Options pattern*, Options validation)
   **And** `docker-compose.yml` pins the default network's subnet (`networks.default.ipam.config`, Docker Compose networks reference) and passes the same range to the api, and an architecture test reads `docker-compose.yml` and proves the two are equal

2. **The identity origin never logs the end-session `id_token_hint`** (R31; NFR-SEC-5, AD-20)
   **Given** the identity origin's `server` in the gateway template
   **When** a browser ends its session through `/oidc/v1/end_session` with `id_token_hint`
   **Then** that server's `access_log` uses a `log_format` that records `$uri`, never `$request`, `$request_uri` or `$args` (nginx, `ngx_http_log_module`), and an architecture test reads the template and proves it (carried from 2.8's security review; ASVS 5.0 V14.2.1)

## Boundaries

- Every container on the Compose default network is trusted to set forwarded headers; only the gateway publishes a port. AWS trust (CloudFront) is story 7.4, which sets `ForwardedHeaders:TrustedNetwork` for its network.
- The rate limits, their 429 and their client-IP partition are story 6.3; this story changes no linking file, and 6.1's client-IP helper keeps reading `HttpContext.Connection.RemoteIpAddress`, which the middleware now sets from a trusted header.
- `OnRemoteFailure` still logs `Failure.Message`; structured logging encodes it, so the risk is log forging only, and no change is made (2.8 security, low).

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- On the running stack, send a start through the gateway with `X-Forwarded-For: 203.0.113.9`: the audited `ClientIp` is the host's client address, neither that value nor the gateway's.
- On the running stack, sign in and out as customer A: the gateway's log line for the end-session request holds no `id_token_hint` value.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Build order: after 6.1 merges (the client IP it audits is 6.1's); it may build beside 6.5, 6.2 and 5.3. It shares no file with 6.5 or 6.2 except `Program.cs` (a different section: `UseForwardedHeaders` first in the pipeline and its registration), and shares `docker-compose.yml` (the default network and the api's environment, not the worker's health check) and `PlatformScriptTests.cs` (new tests) with 5.3, in different sections; the later merge rebases.
- Configuration (AD-26): `ForwardedHeaders:TrustedNetwork`, `ForwardedHeaders:AllowedHosts`. Compose sets `ForwardedHeaders__TrustedNetwork: 10.201.89.0/24` and `ForwardedHeaders__AllowedHosts: localhost`, beside the pinned subnet.
- Test setup: the integration tests observe the client IP through the `ClientIp` a linking start audits (story 6.1), with the connection address set by `ClientAddressStartupFilter`. `ApiFactory` sets `ForwardedHeaders:TrustedNetwork` to a range that excludes its fixed client address, so 6.1's forged-header test still stores the fixed address.
- Touches:
  - `backend/src/DisputeDesk.Api/Security/` (the forwarded-headers options, their validation and registration)
  - `backend/src/DisputeDesk.Api/Program.cs` (`UseForwardedHeaders` first, its registration)
  - `docker-compose.yml` (the pinned default-network subnet, the api's two keys)
  - `infra/nginx/templates/nginx.conf.template` (the identity origin's `log_format`)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/ForwardedHeaders/` (new), `Infrastructure/ApiFactory.cs` (the two keys)
  - `backend/tests/DisputeDesk.ArchitectureTests/PlatformScriptTests.cs` (the subnet and the log format)
- No migration.

## References

- parent — docs/tickets/epic-6-linking/epic-6-linking.md
- split from — docs/tickets/epic-6-linking/tickets/story-6.3-linking-rate-limits-per-login-and-per-ip.md (its criteria 4 and 6 before the split)
- architecture — docs/architecture/architecture.md, AD-20, AD-23, AD-26
- prd — docs/prd/prd.md, FR-8, NFR-SEC-5
- challenge — docs/verification/6.3-challenge.md (Human decisions 1)
- review — docs/verification/6.3-prebuild-review.md
- security — docs/verification/6.3-prebuild-security.md
- standard — <https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0>
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options>
- standard — <https://docs.docker.com/reference/compose-file/networks/>
- standard — <https://nginx.org/en/docs/http/ngx_http_log_module.html>

## Notes

- Risk high: a spoofed client IP would defeat the per-IP limit and the audit trail; beyond the criteria, the security agent sends spoofed headers through the gateway.
- Decision: 2026-10-27 — standards audit: the Api reads the gateway's `X-Forwarded-*` through the Forwarded Headers middleware, so the OIDC redirect and sign-out URIs are `https` behind TLS (moved from story 6.3).
- Decision: 2026-10-30 (human, 6.3 challenge) — trust the Compose network's CIDR from configuration, with no name lookup at start-up, through `ForwardedHeadersOptions.KnownIPNetworks` (`KnownNetworks`, named in the challenge record, is obsolete in .NET 10.0.14 and fails the build), with the default network's subnet pinned in `docker-compose.yml`; AD-23 amended (docs/verification/6.3-challenge.md, Human decisions 1; moved from story 6.3).
- Decision: 2026-10-30 (Sofia, 6.3 pre-build reviews) — `AllowedHosts` is the forwarded-headers option, not host filtering; the identity-origin log item from 2.8 is a criterion here (moved from story 6.3).
- Resolution: 2026-10-30 (Sofia, parallel re-slice) — split from story 6.3 so it builds beside 6.5 and 6.2 once 6.1 merges: forwarded-header trust and the identity-origin log need nothing from the send cap, verification or the limiters, and touch no linking file. Criteria 4 and 6 of 6.3 moved here word for word as criteria 1 and 2; no criterion was added, and no PRD requirement or architecture decision changed. The challenge and pre-build reviews of 6.3 cover these criteria.
- Assumption: 2026-10-30 (Sofia) — the pinned subnet `10.201.89.0/24` sits outside Docker's default address pools, so it does not collide with networks other projects create on the shared daemon. Confirm when the stack first starts with it (moved from story 6.3).
