---
id: 3
type: story
title: "Linking rate limits and trusted forwarded headers"
parent: epic-6-linking
covers: [R8, R10]
after: [2]
hitl: false
risk: high
---

# Linking rate limits and trusted forwarded headers

## Description

Adds the built-in sliding-window limiters, chained as Microsoft Learn documents for limiters partitioned on different keys (confirm the chaining form against the installed .NET), on linking routes and restricts forwarded headers to the gateway (AD-12, AD-23).

## Requirements

Verify: Integration tests prove the limits, `Retry-After`, uncounted rejections, audit of the first rejection and spoofed headers being ignored.

1. **Per-login and per-IP limits** (R8; FR-8, NFR-SEC-4)
   **Given** start and verify requests combined
   **When** a login exceeds 5, or a client IP exceeds 20, within 15 minutes
   **Then** the excess gets 429 `rate-limited` (RFC 6585 §4) with `Retry-After` from the lease's `MetadataName.RetryAfter`, and rejected requests are not counted, except that a request the second limiter rejects has already used a permit from the first

2. **Rejections audited once** (R10, R8; FR-10)
   **Given** repeated rejections in one window
   **When** they occur
   **Then** only the first per partition per window writes `LinkingRateLimited`

3. **Forwarded headers only from the gateway** (R8; FR-8)
   **Given** a spoofed `X-Forwarded-For` from an address other than the gateway
   **When** it reaches the Api
   **Then** the client IP stays the connection address; the loopback defaults are cleared, `KnownProxies` holds only the gateway, `KnownIPNetworks` is empty, `ForwardLimit` is 1, `AllowedHosts` holds the public app host, and IPv6 partitions use the /64 prefix

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- None beyond the story flow.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes.
- Lane: M2.
- Touches: `backend/src/DisputeDesk.Api/Linking/RateLimiting/`; backend/src/DisputeDesk.Api/Program.cs (ForwardedHeaders); `backend/src/DisputeDesk.Api/appsettings.json`; `backend/tests/DisputeDesk.Api.IntegrationTests/Linking/RateLimits/`.

## References

- parent — docs/tickets/epic-6-linking/epic-6-linking.md

## Notes

- Risk high: brute force; beyond the criteria, the security agent sends spoofed headers through the gateway.
- Decision: 2026-10-27 — standards audit: the Api reads the gateway's `X-Forwarded-*` through the Forwarded Headers middleware with the gateway as the known proxy, so the OIDC redirect and sign-out URIs are `https` behind TLS.
- Carried from 2.8's security review (pre-existing, medium): the end-session request carries the ID token, which holds the email, as `id_token_hint` in the GET query, and nginx's default combined `access_log` on the identity origin records `$request` with it. This story gives the identity origin's server a `log_format` that logs `$uri`, not `$request` or `$args`, and a test that a logged end-session request carries no `id_token_hint` value (ASVS 5.0 V14.2.1; AD-20, no personal data in logs).
- Carried from 2.8's security review (pre-existing, low): `OnRemoteFailure` logs `Failure.Message`, which can carry the identity provider's `error_description`; structured logging encodes it, so the risk is log forging only. Note it with this story's log hardening; no change is required.
