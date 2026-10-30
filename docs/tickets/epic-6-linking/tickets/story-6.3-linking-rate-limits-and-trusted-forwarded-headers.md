---
id: 3
type: story
title: "Linking rate limits and trusted forwarded headers"
parent: epic-6-linking
covers: [R8, R10, R31]
after: [2]
refined: false
hitl: false
risk: high
---

# Linking rate limits and trusted forwarded headers

## Description

Adds ASP.NET Core's built-in sliding-window limiters on the linking routes, a per-client-IP limiter then a per-login limiter chained with `PartitionedRateLimiter.CreateChained` as the global limiter, and trusts forwarded headers only from the Compose network's configured range through `ForwardedHeadersOptions.KnownIPNetworks` (AD-12, AD-23). It also stops the identity origin's access log from recording the end-session `id_token_hint` (carried from 2.8's security review).

## Requirements

Verify: Integration tests prove both limits with their segment semantics and the chained edge, the 429 body and `Retry-After`, one audit entry per partition per window with later rejections counted, forwarded headers trusted only from the configured range, and the options' bounds; an architecture test proves the pinned Compose subnet equals the configured range and the identity origin's log format; `openapi.json` changes only by the 429 responses on start and verify.

1. **Per-login and per-IP sliding-window limits** (R8; FR-8, NFR-SEC-4)
   **Given** start and verify requests combined, the limits from `Linking:RateLimits` (5 per login, 20 per client IP, a 15-minute window in 15 segments) and the IP limiter chained before the login limiter with `PartitionedRateLimiter.CreateChained` as the `GlobalLimiter`, returning `RateLimitPartition.GetNoLimiter` outside the linking routes (Microsoft Learn, *Rate limiting middleware*, sliding window limiter and chained limiters)
   **When** a login sends a 6th request, and when 4 logins at their limit and then a 5th login send a 21st request from one IP
   **Then** each excess request gets 429, and a rejected request takes no permit from the limiter that rejected it (`QueueLimit` 0)
   **And** a request the login limiter rejects has already spent an IP permit, as chained limiters run in order (human decision, 2026-10-30; FR-8 amended); a test pins this edge
   **And** permits come back segment by segment, as the sliding-window limiter documents (human decision, 2026-10-30; FR-8 amended): with a short test window, a permit spent in the first segment is available again once that segment slides out of the window, and not before
   **And** the linking routes are matched by metadata on the linking endpoint group, not a path string, so an upper-case path to the start route is limited; `UseRateLimiter` runs after `UseAuthentication` and `UseAuthorization`, so 401 and 403 responses take no permit, while a 400 does
   **And** the IP partition is the client IP as 6.1's helper normalises it (IPv4-mapped IPv6 stored as IPv4), an IPv6 address partitions on its /64 prefix (AD-23), and the login partition is the cookie's `sub`

2. **One 429 body, with `Retry-After`** (R8; FR-8, AD-3)
   **Given** a rejection by either limiter
   **When** `OnRejected` writes the response
   **Then** it is the same 429 `rate-limited` ProblemDetails the full queue gives (story 6.5, `ErrorKind.RateLimited` through `ErrorResults.ToProblem`), holding `type`, `title`, `status` and `traceId` only, never the partition key or a count (RFC 6585 §4; RFC 9457)
   **And** `Retry-After` is the window length in whole seconds, rounded up (900 by default; RFC 9110 §10.2.3), because a sliding-window lease carries no `MetadataName.RetryAfter` (checked on the installed .NET 10.0.14, docs/verification/6.3-prebuild-review.md)

3. **The first rejection per partition per window is audited, later ones are counted** (R10, R8; FR-10)
   **Given** repeated rejections for one login and one IP within a window
   **When** they occur
   **Then** only the first writes `LinkingRateLimited` `Rejected` with the login, the client IP and no partition key in the body, through `AuditLog.Record`; the record of a first rejection is an `IMemoryCache` entry per limiter and partition key, expiring one window after that rejection, in a cache with a `SizeLimit` so rotating IPv6 prefixes cannot grow it without bound (Microsoft Learn, *Cache in-memory in ASP.NET Core*); a rejection is audited when either its IP entry or its login entry is absent, and a rare double audit under a race is accepted
   **And** every rejection is counted by ASP.NET Core's built-in `aspnetcore.rate_limiting.requests` metric (Microsoft Learn, *ASP.NET Core built-in metrics*), exported by the OpenTelemetry setup; the builder confirms it is exported and adds the `Microsoft.AspNetCore.RateLimiting` meter if not

4. **Forwarded headers only from the configured range** (R8; FR-8, AD-23)
   **Given** `ForwardedHeaders:TrustedNetwork` holding the Compose default network's pinned subnet (`10.201.89.0/24`)
   **When** a request from an address outside it carries `X-Forwarded-For: 203.0.113.9`, and a request from an address inside it carries the same header
   **Then** the first keeps the connection address as its client IP and the second takes `203.0.113.9`, through `ForwardedHeadersOptions` (Microsoft Learn, *Configure ASP.NET Core to work with proxy servers and load balancers*): `KnownProxies` and the loopback default in `KnownIPNetworks` are cleared, `KnownIPNetworks` holds only the configured range (`KnownNetworks` is obsolete in .NET 10.0.14, ASPDEPR005, and fails the build), `ForwardLimit` is 1, For, Proto and Host are processed, and `UseForwardedHeaders` runs first in the pipeline
   **And** `ForwardedHeadersOptions.AllowedHosts` holds only the public app host without a port (`localhost` in Compose), so another `X-Forwarded-Host` is ignored; host filtering stays as it is, so the in-container health check still passes
   **And** both keys are bound with `ValidateOnStart`: a missing or unparsable range (`IPNetwork.TryParse`) or an empty host stops the Api with an error naming the key
   **And** `docker-compose.yml` pins the default network's subnet (`networks.default.ipam.config`, Docker Compose networks reference) and passes the same range to the api, and an architecture test reads `docker-compose.yml` and proves the two are equal

5. **`Linking:RateLimits` is bounded** (R8; FR-8, AD-26)
   **Given** `PermitsPerLogin`, `PermitsPerIp`, `Window` and `SegmentsPerWindow`
   **When** the Api starts with a permit count of 0 or over 1000, a window of 0 or over 1 hour, or segments of 0, over 60, or not dividing the window into whole seconds
   **Then** it stops with an error naming the key, and starts with the production defaults (5, 20, 15 minutes, 15 segments) (Microsoft Learn, *Options pattern*, `ValidateOnStart`)
   **And** `ApiFactory` sets high limits so the other linking suites (6.1, 6.5, 6.2) are not limited, while the rate-limit tests use the production counts and a short window

6. **The identity origin never logs the end-session `id_token_hint`** (R31; NFR-SEC-5, AD-20)
   **Given** the identity origin's `server` in the gateway template
   **When** a browser ends its session through `/oidc/v1/end_session` with `id_token_hint`
   **Then** that server's `access_log` uses a `log_format` that records `$uri`, never `$request`, `$request_uri` or `$args` (nginx, `ngx_http_log_module`), and an architecture test reads the template and proves it (carried from 2.8's security review; ASVS 5.0 V14.2.1)

## Boundaries

- Limits are held in memory on the single Api instance (FR-8); the README says so. Shared limits for scale-out are a production-path line (story 7.6).
- Every container on the Compose default network is trusted to set forwarded headers; only the gateway publishes a port. AWS trust (CloudFront) is story 7.4, which sets `ForwardedHeaders:TrustedNetwork` for its network.
- The gateway's coarse `limit_req` (AD-23) does not change. The full-queue 429 of story 6.5 keeps no `Retry-After`.
- `OnRemoteFailure` still logs `Failure.Message`; structured logging encodes it, so the risk is log forging only, and no change is made (2.8 security, low).

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- On the running stack, send a start through the gateway with `X-Forwarded-For: 203.0.113.9`: the audited `ClientIp` is the host's client address, neither that value nor the gateway's.
- On the running stack, sign in and out as customer A: the gateway's log line for the end-session request holds no `id_token_hint` value.
- In a throwaway copy, chain the login limiter before the IP limiter: criterion 1's chained-edge test fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Build order: after 6.2 merges; 6.5, 6.2 and 6.3 build one at a time, because they change the same linking files.
- Configuration (AD-26): `Linking:RateLimits:PermitsPerLogin`, `:PermitsPerIp`, `:Window`, `:SegmentsPerWindow`; `ForwardedHeaders:TrustedNetwork`, `ForwardedHeaders:AllowedHosts`. Compose sets `ForwardedHeaders__TrustedNetwork: 10.201.89.0/24` and `ForwardedHeaders__AllowedHosts: localhost`, beside the pinned subnet.
- Touches:
  - `backend/src/DisputeDesk.Api/Linking/RateLimiting/` (new: the options, the chained limiter, `OnRejected`, the first-rejection cache)
  - `backend/src/DisputeDesk.Api/Linking/LinkingEndpoints.cs` (the shared client-IP helper, the group metadata)
  - `backend/src/DisputeDesk.Api/Program.cs` (forwarded headers first, the limiter after authorization)
  - `backend/src/DisputeDesk.Api/appsettings.json` (the limit defaults)
  - `docker-compose.yml` (the pinned subnet, the api's two keys)
  - `infra/nginx/templates/nginx.conf.template` (the identity origin's `log_format`)
  - `README.md` (limits held in memory)
  - `backend/openapi.json`, `frontend/src/api/generated/` (429 on start and verify)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Linking/RateLimits/` (new), `Infrastructure/ApiFactory.cs` (high limits, a settable client address), `backend/tests/DisputeDesk.ArchitectureTests/PlatformScriptTests.cs` (the subnet and the log format)
- No migration.

## References

- parent — docs/tickets/epic-6-linking/epic-6-linking.md
- architecture — docs/architecture/architecture.md, AD-3, AD-12, AD-20, AD-23, AD-26
- prd — docs/prd/prd.md, FR-8, FR-10, NFR-SEC-4, NFR-SEC-5
- challenge — docs/verification/6.3-challenge.md
- review — docs/verification/6.3-prebuild-review.md
- security — docs/verification/6.3-prebuild-security.md
- standard — <https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0>
- standard — <https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0>
- standard — <https://learn.microsoft.com/aspnet/core/performance/caching/memory>
- standard — <https://learn.microsoft.com/aspnet/core/log-mon/metrics/built-in>
- standard — <https://www.rfc-editor.org/rfc/rfc6585#section-4>
- standard — <https://www.rfc-editor.org/rfc/rfc9110#section-10.2.3>
- standard — <https://docs.docker.com/reference/compose-file/networks/>
- standard — <https://nginx.org/en/docs/http/ngx_http_log_module.html>

## Notes

- Risk high: brute force; beyond the criteria, the security agent sends spoofed headers through the gateway.
- Decision: 2026-10-27 — standards audit: the Api reads the gateway's `X-Forwarded-*` through the Forwarded Headers middleware, so the OIDC redirect and sign-out URIs are `https` behind TLS.
- Decision: 2026-10-30 (human, 6.3 challenge) — trust the Compose network's CIDR from configuration, with no name lookup at start-up, through `ForwardedHeadersOptions.KnownIPNetworks` (`KnownNetworks`, named in the challenge record, is obsolete in .NET 10.0.14 and fails the build), with the default network's subnet pinned in `docker-compose.yml`; AD-23 amended (docs/verification/6.3-challenge.md, Human decisions 1).
- Decision: 2026-10-30 (human, 6.3 challenge) — the built-in sliding-window limiter, with FR-8 amended to its documented segment semantics (Human decisions 2).
- Decision: 2026-10-30 (human, 6.3 challenge) — `PartitionedRateLimiter.CreateChained` as documented, with FR-8 amended: an IP permit is spent even when the login limit then rejects (Human decisions 3).
- Decision: 2026-10-30 (Sofia, 6.3 pre-build reviews) — `Retry-After` is the window length, the only value that is always true for a sliding window; 401 and 403 take no permit; the first-rejection record is a size-limited `IMemoryCache`; later rejections are counted by the built-in rate-limiting metric, so AD-20's linking events do not change; `AllowedHosts` is the forwarded-headers option, not host filtering; the identity-origin log item from 2.8 is criterion 6.
- Assumption: 2026-10-30 (Sofia) — the pinned subnet `10.201.89.0/24` sits outside Docker's default address pools, so it does not collide with networks other projects create on the shared daemon. Confirm when the stack first starts with it.
