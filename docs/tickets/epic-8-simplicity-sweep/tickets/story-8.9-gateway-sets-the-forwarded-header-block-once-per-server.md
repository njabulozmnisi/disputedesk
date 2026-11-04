---
id: 9
type: story
title: "Gateway sets the forwarded-header block once per server"
parent: epic-8-simplicity-sweep
covers: [R32]
after: [7]
hitl: false
risk: medium
refined: true
---

# Gateway sets the forwarded-header block once per server

## Description

Slice S7b of the mitigation plan (wave 2), the nginx part of S7, after 8.7 so one header test proves it. Both gateway templates repeat the same `proxy_set_header` block (`Connection`, `Host`, `X-Forwarded-Host`, `X-Forwarded-Proto`, `X-Forwarded-For`, `Forwarded ""`, and in AWS `X-Origin-Verify ""`) in every proxied location: 50 lines locally and 56 in AWS (INF-3). nginx documents that `proxy_set_header` directives are inherited from the previous level if and only if the current level defines none, so the block can be set once per `server`, and the locations that already set their own different set keep it unchanged. What every upstream receives does not change.

## Requirements

Verify: 8.7's `GatewayTests` pass unchanged on both gateway images; `nginx -t` passes on both rendered templates (the image's own start-up check); the CI `e2e` job and the ZAP baseline pass.

Inputs: none (no endpoint change; the client headers the edge drops stay dropped).

1. **The local template sets the block once per server** (INF-3; R32, AD-23)
   **Given** `infra/nginx/templates/nginx.conf.template`, its app server (`:65`) and its identity server (`:220`)
   **When** this story lands
   **Then** each `server` sets `proxy_http_version 1.1` and its forwarded block once, with that server's `Host` and `X-Forwarded-Host` (`${GATEWAY_APP_HOST}` or `${GATEWAY_AUTH_HOST}`), and no location that used the shared block repeats it
   **And** the one location that sets a different set, `/otlp/v1/(traces|metrics|logs)` (`:193-212`: `Connection ""`, `Cookie ""`, `Authorization ""`, `X-Forwarded-For`, `Forwarded ""`, and no `Host`, `X-Forwarded-Host` or `X-Forwarded-Proto`), keeps its existing directives unchanged, its own `proxy_http_version 1.1` included, with a one-line comment that a location setting any `proxy_set_header` inherits none from its server

2. **The AWS template sets the block once per server** (INF-3; R32, AD-23)
   **Given** `infra/nginx/templates-aws/nginx.conf.template`, its app (`:89`) and identity (`:218`) servers
   **When** this story lands
   **Then** each of those servers sets the block once, `X-Origin-Verify ""` included, with `$forwarded_proto` for the scheme as today, and no location in it repeats the block
   **And** the Mailpit server (`:271-292`) is left as it is: its one location keeps its own set (`Upgrade`, `Connection $connection_upgrade`, `Host`, `X-Forwarded-Proto`, `X-Forwarded-For`, `Forwarded ""`, `X-Origin-Verify ""`, and no `X-Forwarded-Host`), because with one location there is nothing repeated to move

3. **Every upstream receives exactly what it did** (INF-3; R32, NFR-SEC-7)
   **Given** 8.7's `GatewayTests` on both gateways, whose per-route case compares what each proxied location's upstream receives with today's expected set (8.7, criterion 4)
   **When** they run against the changed templates
   **Then** route by route, each upstream receives the same `Host`, `X-Forwarded-Host`, `X-Forwarded-Proto` and `X-Forwarded-For` as before, with today's exceptions kept (`/otlp/` sends no `X-Forwarded-Host` or `X-Forwarded-Proto`; the AWS Mailpit location no `X-Forwarded-Host`), no client `Forwarded` or `X-Origin-Verify` header on any route, and on `/otlp/` no `Cookie` or `Authorization`, with no test changed
   **And** that per-route case is the guard against nginx's inheritance rule: a location that later sets one `proxy_set_header` of its own stops inheriting the block, so a client's `Forwarded` reaches its upstream and the case fails (Supervisor check)

4. **The header comment states the rule** (INF-3; R34)
   **Given** the comment at `nginx.conf.template:9-14` ("each proxied location says so itself") and its AWS counterpart (`templates-aws/nginx.conf.template:16`)
   **When** this story lands
   **Then** each says the block is set once per `server` and inherited by every location that sets no `proxy_set_header` of its own, names the locations that set their own, and cites the nginx `proxy_set_header` documentation

## Boundaries

- Must not change: `add_header` blocks and the `include` of the security-header files; `limit_req`, `error_page`, `proxy_pass`, `proxy_hide_header`; the `/otlp/` location and the AWS Mailpit server; the gateway Dockerfile; any header value.
- No new location, server, `include` or variable.

## Supervisor checks

- Throwaway copy: add `proxy_set_header X-Test 1;` to the local `/api/` location only; 8.7's per-route case for `/api/` fails (the location no longer inherits `Forwarded ""`).
- Throwaway copy: drop `X-Origin-Verify ""` from the AWS app server's block; the origin-verify case fails.
- Throwaway copy: add `proxy_set_header X-Forwarded-Host $host;` to the local `/otlp/` location; the per-route case for `/otlp/` fails.

## Build facts

- Owner: backend (backend-builder; `infra/` is its path).
- Tier: Feature: it touches the edge's forwarded headers, so the security agent re-runs on the diff.
- Flags: security.
- Lane: A, after 8.7 (its per-route and `/otlp/` cases are this story's proof; 8.9 changes no test).
- Mechanisms and sources:
  - `proxy_set_header` inheritance: <https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_set_header> ("inherited from the previous configuration level if and only if there are no proxy_set_header directives defined on the current level").
  - `proxy_http_version`: <https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_http_version>.
- Packages: none.
- Touches: `infra/nginx/templates/nginx.conf.template`, `infra/nginx/templates-aws/nginx.conf.template`.

## Pre-build

- Challenger: `docs/verification/8.9-challenge.md` (simplify).
- Review: `docs/verification/epic-8-prebuild-wave2b.md`, section 8.9 (approve with lows). No security read before the build; the security agent reads the diff.
- Decisions: none raised.
- Sofia, 2026-11-04: every `[story]` point folded. Review: the AWS Mailpit location keeps its own set with no `X-Forwarded-Host` (criterion 2); `/otlp/` keeps its own `proxy_http_version 1.1` (criterion 1); the `/otlp/` proof is 8.7's case, which 8.7 now carries (criterion 3, `after: [7]`). Challenger: the common directives move to each server and the `/otlp/` set stays as it is (criterion 1); "its existing location-specific set", not "complete set" (criterion 1); the Mailpit `Upgrade` and `Connection` stay with its one location, not the server (criterion 2); criterion 3 is a route-by-route preservation check with both exceptions, which 8.7's criterion 4 now asserts; the inheritance regression is explicit (criterion 3, Supervisor checks).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- audit — docs/verification/simplicity-audit/infra-ci-scripts.md, INF-3
- plan — docs/verification/simplicity-audit/mitigation-plan.md, wave 2, S7 and its order
- stories — docs/tickets/epic-7-release/tickets/story-7.12-gateway-and-edge-hardening.md; story-8.7-gateway-and-compose-checks-move-to-the-infra-test-project.md
- architecture — docs/architecture/architecture.md, AD-23

## Notes

- Risk medium (proposed): a location that gains one `proxy_set_header` later silently loses the block; criterion 1's comment and 8.7's per-route cases catch it. Check beyond the criteria: the security agent's read of the diff.
- Decision: 2026-11-04 (Sofia) — `proxy_http_version 1.1` moves to server level with the block, because it is inherited the same way and `Connection ""` is only correct with it; the `/otlp/` location keeps its own copy, since it is left unchanged.
- Decision: 2026-11-04 (Sofia) — the AWS Mailpit server is left as it is: its single location repeats nothing, so moving its set to the server would change lines for no gain, and its set differs from the app server's.
