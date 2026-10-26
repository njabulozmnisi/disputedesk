---
id: 3
type: story
title: "Linking rate limits and trusted forwarded headers"
parent: epic-linking
covers: [R8, R10]
after: [2]
hitl: false
risk: high
---

# Linking rate limits and trusted forwarded headers

## Description

Adds the chained built-in rate limiter on linking routes and restricts forwarded headers to the gateway (AD-12, AD-23).

## Requirements

Verify: Integration tests prove the limits, `Retry-After`, uncounted rejections, audit of the first rejection and spoofed headers being ignored.

1. **Per-login and per-IP limits** (R8; FR-8, NFR-SEC-4)
   **Given** start and verify requests combined
   **When** a login exceeds 5, or a client IP exceeds 20, within 15 minutes
   **Then** the excess gets 429 `rate-limited` with `Retry-After`, and rejected requests are not counted

2. **Rejections audited once** (R10, R8; FR-10)
   **Given** repeated rejections in one window
   **When** they occur
   **Then** only the first per partition per window writes `LinkingRateLimited`

3. **Forwarded headers only from the gateway** (R8; FR-8)
   **Given** a spoofed `X-Forwarded-For` from an address other than the gateway
   **When** it reaches the Api
   **Then** the client IP stays the connection address; `KnownProxies` holds only the gateway, `ForwardLimit` is 1 and IPv6 partitions use the /64 prefix

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- None beyond the story flow.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: A.
- Touches: `backend/src/DisputeDesk.Api/Linking/RateLimiting/`; backend/src/DisputeDesk.Api/Program.cs (ForwardedHeaders); `backend/src/DisputeDesk.Api/appsettings.json`; `backend/tests/DisputeDesk.Api.IntegrationTests/Linking/RateLimits/`.

## References

- parent — docs/tickets/epic-linking/epic-linking.md

## Notes

- Risk high: brute force; beyond the criteria, the security agent sends spoofed headers through the gateway.
