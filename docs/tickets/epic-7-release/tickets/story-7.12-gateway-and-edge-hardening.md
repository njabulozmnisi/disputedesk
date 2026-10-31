---
id: 12
type: story
title: "Gateway and edge hardening"
parent: epic-7-release
covers: [R32, R31, R38]
after: [4, 5]
hitl: false
risk: medium
refined: false
---

# Gateway and edge hardening

## Description

Fixes the nine gateway and edge findings of the production readiness review that the human disposed as "fix, story 7.12" on 2026-10-31 (`docs/verification/7.10-prr.md`, Disposition): A-F4, A-F5, A-F6, B-F1, B-F2, F-F3, F-F4, F-F21 and F-F22. HSTS in AWS gains `includeSubDomains`; both CSPs set `base-uri 'none'`; the identity origin sends the browser-security baseline on every response, its own 403, 404 and 302 included; textual responses name their charset; CloudFront refuses plain HTTP on `/api/*` instead of redirecting it; the gateway access logs record the path without the query string or the Referer on every origin; every `/api` response the gateway produces itself carries `Cache-Control: no-store`; and the developer smoke check's throttling probe and missing-header diagnostic are fixed. The identity origin's error log keeps the request line on an upstream error; that part of F-F3 stays in the production path (P43). Every check runs locally or against mocked providers; no AWS or Terraform apply runs here.

## Requirements

Verify: `infra/aws/gateway.test.sh` (criteria 1 to 4, 6 and 7, against both gateway images on its throwaway network), `infra/aws/check-compose.sh`, `terraform test` in `infra/terraform/envs/dev` (criterion 5, mocked providers) and `PlatformScriptTests` (criterion 6) pass locally and in CI's `platform`, `terraform` and `backend` jobs; `infra/smoke.sh` passes on a fresh local stack under `scripts/test-lock.sh run stack` (criteria 2, 3, 7 and 8); the five journeys pass in CI's `e2e` job (the SPA and the hosted login still work under the new headers).

Inputs: none from a request. One deployment value changes: `GATEWAY_HSTS` in `docker-compose.aws.yml` is fixed in the override (not an input) as `max-age=31536000; includeSubDomains`; locally it stays empty.

1. **HSTS covers subdomains in AWS, on every response** (R32; ASVS 5.0.0 V3.4.1, NFR-SEC-7)
   **Given** the AWS gateway with the override's `GATEWAY_HSTS`
   **When** `gateway.test.sh` requests a page on the app, identity and Mailpit hosts, plus the gateway's own 403 (no origin header) and 404 (`/otlp/v1/traces` on the app host, `/ui/console` on the identity host)
   **Then** each response carries exactly one `Strict-Transport-Security: max-age=31536000; includeSubDomains` (RFC 6797 §6.1, §6.1.2), and the local gateway still sends none
   **And** the three served hosts are CloudFront default domains, under which DisputeDesk serves no other name, so `includeSubDomains` covers only HTTPS (recorded in the build report; `preload` is not added)

2. **Both CSPs forbid base elements** (R32; ASVS 5.0.0 V3.4.3, NFR-SEC-7)
   **Given** `security-headers.conf.template` and `scalar-headers.conf.template`
   **When** `/`, a client route and `/scalar/` are fetched from both gateways
   **Then** each `Content-Security-Policy` contains `base-uri 'none'` and no other `base-uri` source, every other directive unchanged (CSP Level 3, `base-uri`; OWASP ASVS 5.0.0 V3.4.3)
   **And** the served `index.html` and the Scalar page contain no `<base` element, checked in `smoke.sh` beside its updated Scalar CSP string; the Scalar script hash and `ApiReferenceTests` are unchanged

3. **The identity origin sends the browser-security baseline on every response** (R32; ASVS 5.0.0 V3.4.4, V3.4.5, V3.4.6, NFR-SEC-7)
   **Given** the identity server block in both templates, with one header include of its own (as `mailpit-headers.conf.template` is for Mailpit)
   **When** `gateway.test.sh` requests the identity host's 403 (no origin header, AWS), the console 404 (AWS), the root 302, discovery and `/ui/v2/login/loginname`, and `smoke.sh` requests discovery and `/ui/v2/login/loginname` from real Zitadel locally
   **Then** every response carries exactly once `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` and a `Content-Security-Policy: frame-ancestors 'none'` policy (OWASP HTTP Headers Cheat Sheet), each proxied location hiding an upstream copy of the first two with `proxy_hide_header` (nginx, *ngx_http_proxy_module*), so nothing is sent twice
   **And** Zitadel's own login CSP still passes through unchanged beside the gateway's `frame-ancestors` policy (CSP Level 3, multiple policies are all enforced), and the hosted login still shows `X-Frame-Options: deny`, checked as `smoke.sh` already does

4. **Textual responses name their charset** (R32; ASVS 5.0.0 V4.1.1)
   **Given** `charset utf-8;` with `charset_types` naming the types the image's `mime.types` gives `.html`, `.css` and `.js` (nginx, *ngx_http_charset_module*), on both gateways
   **When** `gateway.test.sh` fetches `index.html`, one `/assets/` CSS file, one `/assets/` JS file and the gateway's own 404 page from each gateway
   **Then** each `Content-Type` carries `charset=utf-8` exactly once, and the 429 stays `application/problem+json` with no charset parameter (RFC 8259 §11 defines none)

5. **CloudFront refuses plain HTTP on API paths instead of redirecting** (R38; ASVS 5.0.0 V4.1.2)
   **Given** the edge module's distributions
   **When** `edge.tftest.hcl` plans them against the mocked provider
   **Then** every distribution has an ordered cache behaviour for `/api/*` with `viewer_protocol_policy = "https-only"`, the same cache policy, origin request policy and seven allowed methods as the default behaviour, and the default behaviour keeps `redirect-to-https` for browser navigation (CloudFront, *Requiring HTTPS between viewers and CloudFront*: with HTTPS only, CloudFront answers an HTTP request with 403)
   **And** `infra/terraform/README.md` gains a post-deploy check: `http://<APP_HOST>/api/health/live` and `http://<MAILPIT_HOST>/api/v1/messages` answer 403 with no `Location`, and `http://<APP_HOST>/` still answers 301 to HTTPS

6. **Access logs record the path, never the query string or the Referer** (R31; NFR-SEC-5, NFR-OBS-2, F-F3 as disposed)
   **Given** one path-only log format per template, defined at `http` level with `$request_method $uri $server_protocol` and no `$request`, `$request_uri`, `$args`, `$query_string`, `$arg_*` or `$http_referer` (nginx, *ngx_http_log_module*), used by every `access_log` in both templates
   **When** `PlatformScriptTests` reads both templates, and `gateway.test.sh` sends `GET /api/signin-oidc?code=<marker>` and `GET /?state=<marker>` to the app host, `GET /oidc/v1/end_session?id_token_hint=<marker>` to the identity host and `GET /api/v1/messages?query=<marker>` to the Mailpit host, each with `Referer: https://example.test/?ref=<marker>`
   **Then** the template test finds every `access_log` (server or location level) on that one format in both templates, replacing the identity-only test without loosening it, and the gateway's log holds each path and none of the markers
   **And** the error log is unchanged: its request line on an upstream error stays P43 (production path), decided 2026-10-31

7. **Every `/api` response the gateway produces itself carries `no-store`** (R32; NFR-SEC-7)
   **Given** the `@rate_limited` 429 and a gateway answer for an unreachable Api (`error_page 502 504` to a named location in each `/api/` location; nginx, *ngx_http_core_module*, `error_page`; `proxy_intercept_errors` stays off, so the Api's own error responses pass through)
   **When** `gateway.test.sh` drives a 429 on `/api/` and, after stopping its echo upstream, a 502 on `/api/v1/disputes`, on both gateways, and `smoke.sh` checks its 429 and `/api/health/live`
   **Then** each gateway-produced response carries exactly one `Cache-Control: no-store` (RFC 9111 §5.2.2.5) and each security header once, while a proxied Api response still carries exactly one `Cache-Control`, the Api's own

8. **The smoke check's throttling probe and missing-header diagnostic are reliable** (R32; NFR-SEC-7, 7.10 criterion 7 smoke rows)
   **Given** `infra/smoke.sh`
   **When** the 429 probe runs, and when `/api/health/live` answers without a `Cache-Control` header
   **Then** the probe sends bounded concurrent bursts (`curl --parallel`, at most 3 bursts of 60 requests, each burst above the 10 r/s rate plus burst 20; curl manual, `--parallel`) and stops at the first 429, so its result does not depend on host speed
   **And** a missing header prints `SMOKE FAIL: /api/health/live: no cache-control` and exits non-zero, while a transport error still fails the run (the `grep` in the command substitution no longer ends the script silently under `set -e` and `pipefail`)

## Boundaries

- Must not change: the origin-verify, realip, console, Mailpit, `/otlp/` and cache-header rules of story 7.4; the coarse limit's rate and burst; the Api's own `Cache-Control` and headers (`SecurityHeaders.cs`); the Scalar script hash; the local stack's behaviour apart from the headers, charset and log format above.
- Out of scope, P43 (production path): the identity origin's `error_log` detail. Out of scope: trace correlation in gateway logs (F-F3's other remark), the client address in logs, Scalar's origin (A-F8, P36), and plain HTTP from CloudFront to the origin (story 7.5's shortcut).
- No new service, no new package, no Lua or njs, no WAF; no Terraform apply, no AWS command.
- No test is loosened: the identity access-log test is replaced by one that checks every origin, and the existing header, cache and 429 checks keep their assertions.

## Supervisor checks

Deliberate-break proofs, run by the supervisor in a throwaway copy, not by the verifier.

- Set `GATEWAY_HSTS` back to `max-age=31536000`: criterion 1's check fails.
- Put `base-uri 'self'` back in `scalar-headers.conf.template`: criterion 2's check fails.
- Remove `proxy_hide_header X-Content-Type-Options` from the identity origin's `/ui/v2/login` location, then run `smoke.sh`: criterion 3's "exactly once" check fails if Zitadel sends its own copy; record whether it does.
- Change one `/api/*` behaviour back to `redirect-to-https`: criterion 5's `terraform test` fails.
- Point the app server block's `access_log` back at the default `combined` format: criterion 6's template test and log check fail.
- Remove `Cache-Control: no-store` from `@rate_limited`: criterion 7's check fails.
- Make `/api/health/live` drop `Cache-Control` (a throwaway gateway `proxy_hide_header Cache-Control;`) and run `smoke.sh`: it prints `SMOKE FAIL: /api/health/live: no cache-control`.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes (security headers and the logging of customer-request data at the edge).
- Lane: A.
- Mechanisms and sources: HSTS (<https://www.rfc-editor.org/rfc/rfc6797>); CSP `base-uri` and multiple policies (<https://www.w3.org/TR/CSP3/>); OWASP HTTP Headers Cheat Sheet (<https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html>); OWASP ASVS 5.0.0 V3.4, V4.1 (<https://github.com/OWASP/ASVS>); nginx `add_header`, `proxy_hide_header`, `charset`, `log_format`, `error_page` (<https://nginx.org/en/docs/http/ngx_http_headers_module.html>, <https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_hide_header>, <https://nginx.org/en/docs/http/ngx_http_charset_module.html>, <https://nginx.org/en/docs/http/ngx_http_log_module.html>, <https://nginx.org/en/docs/http/ngx_http_core_module.html#error_page>); RFC 9111 `no-store`; RFC 8259 (JSON has no charset parameter); CloudFront viewer protocol policy and cache behaviours (<https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/using-https-viewers-to-cloudfront.html>, Terraform AWS provider `aws_cloudfront_distribution` `ordered_cache_behavior`); curl `--parallel` (<https://curl.se/docs/manpage.html#-Z>).
- Packages: none.
- Touches:
  - `infra/nginx/templates/security-headers.conf.template`, `scalar-headers.conf.template` (criterion 2)
  - `infra/nginx/templates/nginx.conf.template` and `infra/nginx/templates-aws/nginx.conf.template` (criteria 3, 4, 6, 7), and a new identity header include beside them (criterion 3; the Dockerfile copies it in as it copies the shared header files)
  - `infra/nginx/Dockerfile` (only if the new include needs copying into the AWS template directory)
  - `docker-compose.aws.yml` (`GATEWAY_HSTS` only)
  - `infra/terraform/modules/edge/main.tf`, `infra/terraform/envs/dev/tests/edge.tftest.hcl`, `infra/terraform/README.md` (post-deploy checks only) (criterion 5)
  - `infra/aws/gateway.test.sh` (criteria 1 to 4, 6, 7), `infra/aws/check-compose.sh` (only if it asserts the HSTS value)
  - `infra/smoke.sh` (criteria 2, 3, 7, 8)
  - `backend/tests/DisputeDesk.ArchitectureTests/PlatformScriptTests.cs` (criterion 6's template test only)

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- verification — docs/verification/7.10-prr.md, Findings A-F4, A-F5, A-F6, B-F1, B-F2, F-F3, F-F4, F-F21, F-F22, and Disposition
- story — docs/tickets/epic-7-release/tickets/story-7.4-aws-compose-override-and-opentelemetry-collector.md, criteria 3, 4 and 7
- story — docs/tickets/epic-7-release/tickets/story-7.5-terraform-dev-environment-and-oidc-deploy.md
- architecture — docs/architecture/architecture.md, AD-20, AD-23, AD-28
- prd — docs/prd/prd.md, NFR-SEC-5, NFR-SEC-7, NFR-OBS-2
- production path — docs/production-path.md, P43

## Notes

- Risk medium (proposed): a wrong header or log format can break the hosted login or the SPA in AWS, or keep customer request data in logs; nothing here serves real customers. Beyond the criteria, the CI journeys exercise the hosted login under criterion 3's headers.
- Decision: 2026-10-31 (human, 7.10 disposition) — A-F4, A-F5, A-F6, B-F1, B-F2, F-F3, F-F4, F-F21 and F-F22 are fixed in this story; for F-F3 the scope is the access logs on every origin (path without the query string), and the identity error log stays P43 in the production path.
- Decision: 2026-10-31 (Sofia, 7.12 write) — the identity origin's `Referrer-Policy` is `strict-origin-when-cross-origin`, the app's value, not `no-referrer`: with `no-referrer` a browser sends `Origin: null` on the hosted login's form posts, which the login may refuse; the identity access log already excludes the Referer.
- Decision: 2026-10-31 (Sofia, 7.12 write) — the `/api/*` HTTPS-only behaviour is set on all three distributions with one block, because it is simplest; it matters on the app (the Api) and the Mailpit host (Mailpit's API); Zitadel serves nothing under `/api/`.
- Decision: 2026-10-31 (Sofia, 7.12 write) — a gateway-produced 502 keeps nginx's default body; only its headers change here. A problem-details body for gateway failures is not in the review's finding.
- Collision: touches `PlatformScriptTests.cs` (criterion 6 only); 7.13 and 7.14 do not touch it, so all three may build in parallel. `docker-compose.aws.yml` is touched here only; 7.13 touches `docker-compose.yml`.
- Production path: the docs-only production-path change narrows P43 to the identity error log once this story merges.
