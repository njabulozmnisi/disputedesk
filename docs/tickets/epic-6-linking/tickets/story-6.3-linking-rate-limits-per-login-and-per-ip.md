---
id: 3
type: story
title: "Linking rate limits per login and per IP"
parent: epic-6-linking
covers: [R7, R8, R10]
after: [5, 2]
refined: false
hitl: false
risk: high
---

# Linking rate limits per login and per IP

## Description

Adds ASP.NET Core's built-in sliding-window limiters on the linking routes, a per-client-IP limiter then a per-login limiter chained with `PartitionedRateLimiter.CreateChained` as the global limiter, answering with the 429 `rate-limited` body the full queue gives (story 6.5) plus `Retry-After` (AD-3, AD-12, AD-23). It also proves that the blank challenge a full-queue 429 leaves verifies as a wrong code, which needs both 6.5 and 6.2. Trusted forwarded headers and the identity-origin log are story 6.6.

## Requirements

Verify: Integration tests prove both limits with their segment semantics and the chained edge, the 429 body and `Retry-After`, one audit entry per partition per window with later rejections counted, the full-queue blank verifying as a wrong code, and the options' bounds; `openapi.json` changes only by the 429 responses on start and verify.

1. **Per-login and per-IP sliding-window limits** (R8; FR-8, NFR-SEC-4)
   **Given** start and verify requests combined, the limits from `Linking:RateLimits` (5 per login, 20 per client IP, a 15-minute window in 15 segments) and the IP limiter chained before the login limiter with `PartitionedRateLimiter.CreateChained` as the `GlobalLimiter`, returning `RateLimitPartition.GetNoLimiter` outside the linking routes (Microsoft Learn, *Rate limiting middleware*, sliding window limiter and chained limiters)
   **When** a login sends a 6th request, and when 4 logins at their limit and then a 5th login send a 21st request from one IP
   **Then** each excess request gets 429, and a rejected request takes no permit from the limiter that rejected it (`QueueLimit` 0)
   **And** a request the login limiter rejects has already spent IP permits (two, because the middleware asks the chained limiter twice before rejecting), as chained limiters run in order (human decision, 2026-10-30; FR-8 amended); a test pins this edge
   **And** permits come back segment by segment, as the sliding-window limiter documents (human decision, 2026-10-30; FR-8 amended): with a short test window, a permit spent in the first segment is available again once that segment slides out of the window, and not before
   **And** the linking routes are matched by metadata on the linking endpoint group, not a path string, so an upper-case path to the start route is limited; `UseRateLimiter` runs after `UseAuthentication` and `UseAuthorization`, so 401 and 403 responses take no permit, while a 400 does
   **And** the IP partition is the client IP as 6.1's helper normalises it (IPv4-mapped IPv6 stored as IPv4), an IPv6 address partitions on its /64 prefix (AD-23), and the login partition is the cookie's `sub`

2. **One 429 body, with `Retry-After`** (R8; FR-8, AD-3)
   **Given** a rejection by either limiter
   **When** `OnRejected` writes the response
   **Then** it is the same 429 `rate-limited` ProblemDetails the full queue gives (story 6.5, `ErrorKind.RateLimited` through `ErrorResults.ToProblem`), holding `type`, `title`, `status`, `traceId` and one fixed generic `detail` that is the same for both limits, never the partition key or a count (RFC 6585 §4; RFC 9457)
   **And** `Retry-After` is the window length in whole seconds, rounded up (900 by default; RFC 9110 §10.2.3), because a sliding-window lease carries no `MetadataName.RetryAfter` (checked on the installed .NET 10.0.14, docs/verification/6.3-prebuild-review.md)

3. **The first rejection per partition per window is audited, later ones are counted** (R10, R8; FR-10)
   **Given** repeated rejections for one login and one IP within a window
   **When** they occur
   **Then** only the first writes `LinkingRateLimited` `Rejected` with the login, the client IP and no partition key in the body, through `AuditLog.Record`; the record of a first rejection is an `IMemoryCache` entry per limiter and partition key, expiring one window after that rejection, in a cache with a `SizeLimit` so rotating IPv6 prefixes cannot grow it without bound (Microsoft Learn, *Cache in-memory in ASP.NET Core*); a rejection is audited when either its IP entry or its login entry is absent, and a rare double audit under a race is accepted
   **And** every rejection is counted by ASP.NET Core's built-in `aspnetcore.rate_limiting.requests` metric (Microsoft Learn, *ASP.NET Core built-in metrics*), exported by the OpenTelemetry setup; the builder confirms it is exported and adds the `Microsoft.AspNetCore.RateLimiting` meter if not

4. **The blank a full-queue 429 leaves verifies as a wrong code** (R7; FR-4, FR-6, FR-7)
   **Given** the blank challenge a full-queue 429 leaves (story 6.5, criterion 4: current, unclaimed, `OtpHmac` null)
   **When** the login posts codes three times
   **Then** it gives the same status and body (apart from `traceId`) as customer D's challenge given wrong codes, attempt by attempt: 422 with `attemptsRemaining` 2, then 1, then 409 `linking-locked`, comparing against the fixed 32-byte dummy HMAC when `OtpHmac` is null, as story 6.2's criterion 4 does for every other blank (OWASP Authentication Cheat Sheet, generic responses)

5. **`Linking:RateLimits` is bounded** (R8; FR-8, AD-26)
   **Given** `PermitsPerLogin`, `PermitsPerIp`, `Window` and `SegmentsPerWindow`
   **When** the Api starts with a permit count of 0 or over 1000, a window of 0 or over 1 hour, or segments of 0, over 60, or not dividing the window into whole seconds
   **Then** it stops with an error naming the key, and starts with the production defaults (5, 20, 15 minutes, 15 segments) (Microsoft Learn, *Options pattern*, `ValidateOnStart`)
   **And** `ApiFactory` sets high limits so the other linking suites (6.1, 6.5, 6.2) are not limited, while the rate-limit tests use the production counts and a short window

## Boundaries

- Limits are held in memory on the single Api instance (FR-8); the README says so. Shared limits for scale-out are a production-path line (story 7.6).
- Trusted forwarded headers, the pinned Compose subnet and the identity origin's log format are story 6.6; the IP partition reads the client IP as 6.1's helper does, which 6.6's middleware sets from a trusted header.
- The gateway's coarse `limit_req` (AD-23) does not change. The full-queue 429 of story 6.5 keeps no `Retry-After`.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- In a throwaway copy, chain the login limiter before the IP limiter: criterion 1's chained-edge test fails.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Build order: after 6.5 and 6.2 both merge: criterion 2 needs 6.5's `ErrorKind.RateLimited`, the limits cover 6.2's verify route, and criterion 4 needs both. It may build beside 6.6 and 5.3 (different sections of `Program.cs` and `ApiFactory.cs` with 6.6; no shared file with 5.3).
- Configuration (AD-26): `Linking:RateLimits:PermitsPerLogin`, `:PermitsPerIp`, `:Window`, `:SegmentsPerWindow`.
- Touches:
  - `backend/src/DisputeDesk.Api/Linking/RateLimiting/` (new: the options, the chained limiter, `OnRejected`, the first-rejection cache)
  - `backend/src/DisputeDesk.Api/Linking/LinkingEndpoints.cs` (the shared client-IP helper, whose doc comment then names story 6.6 for trusted headers; the group metadata)
  - `backend/src/DisputeDesk.Api/Program.cs` (the limiter after authorization)
  - `backend/src/DisputeDesk.Api/appsettings.json` (the limit defaults)
  - `README.md` (limits held in memory)
  - `backend/openapi.json`, `frontend/src/api/generated/` (429 on start and verify)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/Linking/RateLimits/` (new), `Linking/` (the full-queue blank added to 6.2's blank-challenge verify tests), `Infrastructure/ApiFactory.cs` (high limits; the client address is 6.1's `ClientAddressStartupFilter`)
- No migration.

## References

- parent — docs/tickets/epic-6-linking/epic-6-linking.md
- architecture — docs/architecture/architecture.md, AD-3, AD-12, AD-23, AD-26
- prd — docs/prd/prd.md, FR-4, FR-6, FR-7, FR-8, FR-10, NFR-SEC-4
- split to — docs/tickets/epic-6-linking/tickets/story-6.6-trusted-forwarded-headers-and-an-identity-log-without-id-tok.md
- challenge — docs/verification/6.3-challenge.md
- review — docs/verification/6.3-prebuild-review.md
- security — docs/verification/6.3-prebuild-security.md
- standard — <https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0>
- standard — <https://learn.microsoft.com/aspnet/core/performance/caching/memory>
- standard — <https://learn.microsoft.com/aspnet/core/log-mon/metrics/built-in>
- standard — <https://www.rfc-editor.org/rfc/rfc6585#section-4>
- standard — <https://www.rfc-editor.org/rfc/rfc9110#section-10.2.3>
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options>
- standard — <https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html>

## Notes

- Risk high: brute force; beyond the criteria, the security agent drives the limits through the gateway.
- Decision: 2026-10-30 (human, 6.3 challenge) — the built-in sliding-window limiter, with FR-8 amended to its documented segment semantics (Human decisions 2).
- Decision: 2026-10-30 (human, 6.3 challenge) — `PartitionedRateLimiter.CreateChained` as documented, with FR-8 amended: an IP permit is spent even when the login limit then rejects (Human decisions 3).
- Decision: 2026-10-30 (Sofia, 6.3 pre-build reviews) — `Retry-After` is the window length, the only value that is always true for a sliding window; 401 and 403 take no permit; the first-rejection record is a size-limited `IMemoryCache`; later rejections are counted by the built-in rate-limiting metric, so AD-20's linking events do not change. (The `AllowedHosts` and identity-origin log items of this decision moved to story 6.6.)
- Resolution: 2026-10-30 (Sofia, parallel re-slice) — split for parallel builds: criteria 4 (forwarded headers) and 6 (the identity-origin log) moved word for word to story 6.6, which needs only 6.1 and builds beside 6.5 and 6.2; this story keeps the limits (criteria 1, 2, 3 and 5) and waits on both 6.5 (`ErrorKind.RateLimited`) and 6.2 (the verify route). The full-queue-blank case of 6.2's criterion 4 moved here as criterion 4, so 6.2 no longer waits on 6.5; it is the same check, run once both stories have merged. No criterion was added, and no PRD requirement or architecture decision changed. The title changed with the scope; the id stays 3. The subnet assumption moved to 6.6.
- Resolution: 2026-10-30 (human decision after the 6.3 security review) — keep the IP-first chain; a login-rejected request spends two IP permits (the middleware's double acquire), worded in FR-8, AD-12 and criterion 1. Criterion 2 allows the fixed generic `detail` both limits share (6.3-verify.md).
