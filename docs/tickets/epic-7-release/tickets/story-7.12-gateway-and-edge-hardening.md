---
id: 12
type: story
title: "Gateway and edge hardening"
parent: epic-7-release
covers: [R32, R31, R38]
after: [4, 5]
hitl: false
risk: medium
refined: true
---

# Gateway and edge hardening

## Description

Fixes the nine gateway and edge findings of the production readiness review that the human disposed as "fix, story 7.12" on 2026-10-31 (`docs/verification/7.10-prr.md`, Disposition): A-F4, A-F5, A-F6, B-F1, B-F2, F-F3, F-F4, F-F21 and F-F22. HSTS in AWS gains `includeSubDomains`; both CSPs set `base-uri 'none'`; the identity origin sends the browser-security baseline on every response, its own 403, 404 and 302 included, and hides the login's weaker copies; the SPA's textual files name their charset; CloudFront refuses plain HTTP on the Api and Mailpit's API and on the whole identity origin instead of redirecting it; every gateway access log records the path without the query string or the Referer; every `/api` response the gateway generates itself carries `Cache-Control: no-store`; and the developer smoke check's throttling probe and missing-header diagnostic are fixed. The nginx error logs on all three origins keep the request line and Referer on a 429 or an upstream failure; that stays in the production path (P43, human decision 2026-10-31). Every check runs locally or against mocked providers; no AWS or Terraform apply runs here.

## Requirements

Verify: `infra/deploy/aws/gateway.test.sh` (criteria 1, 3, 4, 6 and 7 against both gateway images on its throwaway network, and criterion 2's CSP strings), `infra/deploy/aws/check-compose.sh`, `terraform test` in `infra/terraform/envs/dev` (criterion 5, mocked providers) and `PlatformScriptTests` (criteria 6 and 7's template checks) pass locally and in CI's `platform`, `terraform` and `backend` jobs; `infra/tests/smoke.sh` passes on a fresh local stack under `scripts/test-lock.sh run stack` (criteria 2, 3, 7 and 8); the five journeys pass in CI's `e2e` job with the e2e CSP constants updated (the SPA, Scalar and the hosted login still work under the new headers).

Inputs: none from a request. One deployment value changes: `GATEWAY_HSTS` in `docker-compose.aws.yml` is fixed in the override (not an input) as `max-age=63072000; includeSubDomains`; locally it stays empty.

1. **HSTS covers subdomains in AWS, on every response nginx sends** (R32; ASVS 5.0.0 V3.4.1, NFR-SEC-7)
   **Given** the AWS gateway with the override's `GATEWAY_HSTS`
   **When** `gateway.test.sh` requests a page on the app, identity and Mailpit hosts, the gateway's own 403 (no origin header) on each of the three hosts, and its own 404 (`/otlp/v1/traces` on the app host, `/ui/console` on the identity host)
   **Then** each response carries exactly one `Strict-Transport-Security: max-age=63072000; includeSubDomains` (RFC 6797 §6.1, §6.1.1, §6.1.2; two years as the OWASP HTTP Headers Cheat Sheet recommends, as long as the login's own value), and the local gateway still sends none
   **And** the scope is the responses nginx produces or proxies; responses CloudFront generates itself (its 403 for plain HTTP from criterion 5, its 301 redirect) are not checked here; `preload` is not added

2. **Both CSPs forbid base elements** (R32; ASVS 5.0.0 V3.4.3, NFR-SEC-7)
   **Given** `security-headers.conf.template` and `scalar-headers.conf.template`
   **When** `/`, a client route and `/scalar/` are fetched from both gateways, and the e2e journeys run
   **Then** each `Content-Security-Policy` contains `base-uri 'none'` and no other `base-uri` source, every other directive unchanged (CSP Level 3, `base-uri`; OWASP ASVS 5.0.0 V3.4.3), and the e2e CSP constants (`e2e/tests/support.ts`, `e2e/tests/scalar-reference.spec.ts`) say the same, so the journeys that use them (`dispute-raise.spec.ts`, `spa-transactions.spec.ts`) and the Scalar spec still pass
   **And** the served `index.html` and the Scalar page contain no `<base` element, checked in `smoke.sh` beside its updated Scalar CSP string; the Scalar script hash and `ApiReferenceTests` are unchanged

3. **The identity origin sends the browser-security baseline on every response, once** (R32; ASVS 5.0.0 V3.4.1, V3.4.4, V3.4.5, V3.4.6, NFR-SEC-7)
   **Given** one new header include for the identity server block in both templates (`identity-headers.conf.template`, as `mailpit-headers.conf.template` is for Mailpit), holding HSTS (`${GATEWAY_HSTS}`), `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` and `Content-Security-Policy: frame-ancestors 'none'` with `add_header ... always`, and `proxy_hide_header` for `Strict-Transport-Security`, `X-Content-Type-Options` and `Referrer-Policy` on both proxied identity locations (server level is enough; nginx, *ngx_http_proxy_module*, `proxy_hide_header`)
   **When** `gateway.test.sh` requests the identity host's 403 (no origin header, AWS), the console 404 (AWS), the root 302, discovery and `/ui/login/loginname`, with its echo upstream sending what `zitadel:v4.20.0` sends on port 3000 (`Strict-Transport-Security: max-age=63072000; includeSubDomains; preload`, `Referrer-Policy: origin-when-cross-origin`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: deny`, `X-XSS-Protection: 1; mode=block`); and `smoke.sh` requests discovery and `/ui/login/loginname` from real Zitadel locally
   **Then** every response carries exactly one `X-Content-Type-Options: nosniff`, exactly one `Referrer-Policy: strict-origin-when-cross-origin` (stricter than the login's `origin-when-cross-origin`), in AWS exactly one HSTS with the gateway's value and locally none (the login's own HSTS is hidden, so `smoke.sh` asserts the local login sends none), and exactly one `Content-Security-Policy` whose value is exactly `frame-ancestors 'none'` beside Zitadel's own policy where it sends one (CSP Level 3, multiple policies are all enforced; OWASP HTTP Headers Cheat Sheet)
   **And** the login's `X-Frame-Options: deny` still passes through exactly once on `/ui/login/*`, checked in `gateway.test.sh` and as `smoke.sh` already does

4. **The SPA's textual files name their charset** (R32; ASVS 5.0.0 V4.1.1)
   **Given** `charset utf-8;` with `charset_types` naming the types the image's `mime.types` gives `.html`, `.css` and `.js` (`text/css` added to the default list; nginx, *ngx_http_charset_module*), set in the app server's `/` and `/assets/` locations of both templates, not at `http` or server level, so proxied Api, Mailpit and Zitadel responses are not relabelled
   **When** `gateway.test.sh` fetches `index.html`, one `/assets/` CSS file, one `/assets/` JS file and a missing `/assets/` file (the gateway's own 404 page) from each gateway, plus the Mailpit page and discovery from the AWS gateway
   **Then** each of the first four carries `charset=utf-8` exactly once in its `Content-Type`, the Mailpit page and discovery keep their upstream `Content-Type` unchanged, and the 429 stays `application/problem+json` with no charset parameter (RFC 8259 §11 defines none)

5. **CloudFront refuses plain HTTP on API paths and on the identity origin instead of redirecting** (R38; ASVS 5.0.0 V4.1.2)
   **Given** the edge module's three distributions
   **When** `edge.tftest.hcl` plans them against the mocked provider
   **Then** the app and Mailpit distributions each have an ordered cache behaviour for `/api/*` with `viewer_protocol_policy = "https-only"` and the same cache policy, origin request policy and seven allowed methods as their default behaviour, which keeps `redirect-to-https` for browser navigation; the identity distribution's default behaviour is `https-only` and it has no `/api/*` behaviour, since its token, JWKS, discovery, userinfo and end-session endpoints sit outside `/api/` (CloudFront, *Requiring HTTPS between viewers and CloudFront*: with HTTPS only, CloudFront answers an HTTP request with 403)
   **And** `infra/terraform/README.md` gains a post-deploy check: `http://<APP_HOST>/api/health/live`, `http://<MAILPIT_HOST>/api/v1/messages` and `http://<AUTH_HOST>/oauth/v2/token` answer 403 with no `Location`; `http://<APP_HOST>/` still answers 301 to HTTPS; and `http://<APP_HOST>/api` (no trailing slash) answers 301, because it is a client route the SPA serves, not an Api path (excluded on purpose)

6. **Access logs record the path, never the query string or the Referer** (R31; NFR-SEC-5, NFR-OBS-2, F-F3 as disposed)
   **Given** one path-only log format per template, defined at `http` level with `$request_method $uri $server_protocol` and no `$request`, `$request_uri`, `$args`, `$query_string`, `$arg_*` or `$http_referer` (nginx, *ngx_http_log_module*), used by every `access_log` in both templates, the `http`-level line included
   **When** `PlatformScriptTests` reads both templates; and `gateway.test.sh`, before criterion 7 stops its echo upstream and from a CloudFront viewer address no earlier check has limited, sends `GET /api/signin-oidc?code=<marker>` and `GET /?state=<marker>` to the app host, `GET /oidc/v1/end_session?id_token_hint=<marker>` to the identity host and `GET /api/v1/messages?query=<marker>` to the Mailpit host of the AWS gateway, and the first three to the local gateway, each with `Referer: https://example.test/?ref=<marker>`
   **Then** the template test finds every `access_log` (`http`, server or location level) naming that one format in both templates and fails any `access_log` without a format name, replacing the identity-only test without loosening it
   **And** each gateway's access log (its standard output, `docker logs` without standard error) holds the logged paths `/api/signin-oidc`, `/index.html` (the `$uri` after `try_files`), `/oidc/v1/end_session` and `/api/v1/messages` and none of the markers; the error log (standard error) is not checked, because its request line and Referer on a 429 or an upstream failure stay P43 (production path), decided 2026-10-31

7. **Every `/api` response the gateway generates itself carries `no-store`, once** (R32; NFR-SEC-7; AD-23 as amended 2026-10-31)
   **Given** the `@rate_limited` 429 with `Cache-Control: no-store`, and in each `/api/` and `/api/health/` location of both templates `error_page 413 500 502 504` to a named location that adds `Cache-Control: no-store` and the security headers with `add_header ... always`, keeps the status, and leaves the body to nginx or a fixed text; each such location repeats `error_page 429 @rate_limited;` because a location that sets `error_page` inherits none from its server (nginx, *ngx_http_core_module*, `error_page`; *ngx_http_headers_module*, `add_header`); `proxy_intercept_errors` stays off, so the Api's own error responses pass through untouched
   **When** `gateway.test.sh`, as its last section, drives on both gateways a 429 on `/api/`, a 413 (a 2 MB `POST` to `/api/v1/disputes`, above nginx's default 1 MB body limit), a proxied `/api/v1/disputes` answer while the echo runs, and, after stopping its echo upstream, a 502 on `/api/v1/disputes` and `/api/health/live` (retrying for up to 30 seconds, each request with `curl --max-time 5`, since the upstream is re-resolved every 10 seconds); `PlatformScriptTests` (or `gateway.test.sh` on `nginx -T`) checks that every `/api` location lists 413, 500, 502 and 504 and repeats the 429 line; and `smoke.sh` checks its 429 and `/api/health/live`
   **Then** each gateway-generated response keeps its status and carries exactly one `Cache-Control: no-store` (RFC 9111 §5.2.2.5) and each security header once, while the proxied answer (the echo sends `Cache-Control: no-store` on port 8080, standing in for the Api) and the Api's own responses in `smoke.sh` still carry exactly one `Cache-Control`, the upstream's
   **And** 500 and 504 are proved by the configuration check only (nginx produces no 500 on demand, and a 504 needs the 60-second read timeout); gateway errors outside `/api` keep today's headers

8. **The smoke check's throttling probe and missing-header diagnostic are reliable** (R32; NFR-SEC-7, 7.10 criterion 7 smoke rows)
   **Given** `infra/tests/smoke.sh`
   **When** the 429 probe runs, and when `/api/health/live` answers without a `Cache-Control` header
   **Then** the probe sends bounded concurrent bursts (`curl --parallel`, at most 3 bursts of 60 requests, each burst above the 10 r/s rate plus burst 20; curl manual, `--parallel`), stops at the first 429 and prints how many requests and bursts it took, so a failure shows what the host managed rather than a bare "no 429"
   **And** a missing header prints `SMOKE FAIL: /api/health/live: no cache-control` and exits non-zero, while a transport error still fails the run (the `grep` in the command substitution no longer ends the script silently under `set -e` and `pipefail`)

## Boundaries

- Must not change: the origin-verify, realip, console, Mailpit, `/otlp/` and cache-header rules of story 7.4; the coarse limit's rate and burst; the Api's own `Cache-Control` and headers (`SecurityHeaders.cs`); the Scalar script hash; the local stack's behaviour apart from the headers, charset, log format and gateway error pages above.
- The gateway never adds `Cache-Control` to a response the Api produced (AD-23 as amended): no `add_header Cache-Control` on proxied responses and no `proxy_hide_header Cache-Control` in an `/api` location.
- Out of scope, P43 (production path): the nginx error logs on every origin (request line and Referer on a 429 or an upstream failure). Out of scope: trace correlation in gateway logs (F-F3's other remark), the client address in logs, Scalar's origin (A-F8, P36), plain HTTP from CloudFront to the origin (story 7.5's shortcut), and the headers on responses CloudFront generates itself.
- No new service, no new package, no Lua or njs, no WAF; no Terraform apply, no AWS command.
- No test is loosened: the identity access-log test is replaced by one that checks every `access_log` in both templates, and the existing header, cache and 429 checks keep their assertions (the HSTS value check changes only to the new value).

## Supervisor checks

Deliberate-break proofs, run by the supervisor in a throwaway copy, not by the verifier. Each one fails a check that exists after the build.

- Set `GATEWAY_HSTS` back to `max-age=31536000`: criterion 1's check in `gateway.test.sh` fails.
- Put `base-uri 'self'` back in `scalar-headers.conf.template`: criterion 2's checks in `gateway.test.sh` and `smoke.sh` fail.
- Remove `proxy_hide_header Strict-Transport-Security` from the identity origin: criterion 3's "exactly one HSTS" check in `gateway.test.sh` fails, because the echo sends the login's copy on port 3000.
- Remove `text/css` from `charset_types`: criterion 4's CSS check fails.
- Change the app distribution's `/api/*` behaviour, or the identity distribution's default behaviour, back to `redirect-to-https`: criterion 5's `terraform test` fails.
- Drop the format name from the `http`-level `access_log`: criterion 6's template test fails. Point the app server block's `access_log` at a format with `$request_uri`: the template test and the marker check fail.
- Remove `502` from the `/api/` location's `error_page` list: criterion 7's 502 check fails (no `no-store`). Remove that location's `error_page 429 @rate_limited;`: `smoke.sh`'s 429 check on `/api/limit-probe.json` fails (no problem-details body).
- Remove `Cache-Control: no-store` from `@rate_limited`: criterion 7's 429 check fails.
- Make `/api/health/live` drop `Cache-Control` (a throwaway gateway `proxy_hide_header Cache-Control;`) and run `smoke.sh`: it prints `SMOKE FAIL: /api/health/live: no cache-control` and exits non-zero.

## Build facts

- Owner: backend (backend-builder); QA (owner of `e2e/`) updates the two e2e CSP constants in the same story change.
- Flags: high-stakes (security headers and the logging of customer-request data at the edge).
- Lane: A.
- Mechanisms and sources: HSTS (<https://www.rfc-editor.org/rfc/rfc6797>); CSP `base-uri` and multiple policies (<https://www.w3.org/TR/CSP3/>); OWASP HTTP Headers Cheat Sheet (<https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html>); OWASP ASVS 5.0.0 V3.4, V4.1 (<https://github.com/OWASP/ASVS>); nginx `add_header`, `proxy_hide_header`, `charset`, `log_format`, `error_page` (<https://nginx.org/en/docs/http/ngx_http_headers_module.html>, <https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_hide_header>, <https://nginx.org/en/docs/http/ngx_http_charset_module.html>, <https://nginx.org/en/docs/http/ngx_http_log_module.html>, <https://nginx.org/en/docs/http/ngx_http_core_module.html#error_page>); RFC 9111 `no-store` (<https://www.rfc-editor.org/rfc/rfc9111#section-5.2.2.5>); RFC 8259 (JSON has no charset parameter); CloudFront viewer protocol policy and cache behaviours (<https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/using-https-viewers-to-cloudfront.html>, Terraform AWS provider `aws_cloudfront_distribution` `ordered_cache_behavior`); curl `--parallel` (<https://curl.se/docs/manpage.html#-Z>).
- Packages: none.
- Touches:
  - `infra/images/gateway/templates/security-headers.conf.template`, `scalar-headers.conf.template` (criterion 2)
  - `infra/images/gateway/templates/nginx.conf.template` and `infra/images/gateway/templates-aws/nginx.conf.template` (criteria 3, 4, 6, 7)
  - new `infra/images/gateway/templates/identity-headers.conf.template` (criterion 3)
  - `infra/images/gateway/Dockerfile`, the `COPY` line that copies the shared header files into `/etc/nginx/templates-aws/`: it adds `identity-headers.conf.template` (criterion 3; required)
  - `docker-compose.aws.yml` (`GATEWAY_HSTS` only)
  - `infra/terraform/modules/edge/main.tf`, `infra/terraform/envs/dev/tests/edge.tftest.hcl`, `infra/terraform/README.md` (post-deploy checks only) (criterion 5)
  - `infra/deploy/aws/gateway.test.sh` (criteria 1 to 4, 6, 7, including its echo upstream's headers)
  - `infra/tests/smoke.sh` (criteria 2, 3, 7, 8)
  - `backend/tests/DisputeDesk.ArchitectureTests/PlatformScriptTests.cs` (criteria 6 and 7 template tests only)
  - `e2e/tests/support.ts` (the gateway CSP constant) and `e2e/tests/scalar-reference.spec.ts` (the Scalar CSP constant) (criterion 2; QA owns `e2e/`)

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- verification — docs/verification/7.10-prr.md, Findings A-F4, A-F5, A-F6, B-F1, B-F2, F-F3, F-F4, F-F21, F-F22, and Disposition
- verification — docs/verification/7.12-challenge.md, 7.12-prebuild-review.md, 7.12-prebuild-security.md, with the human decisions of 2026-10-31
- story — docs/tickets/epic-7-release/tickets/story-7.4-aws-compose-override-and-opentelemetry-collector.md, criteria 3, 4 and 7
- story — docs/tickets/epic-7-release/tickets/story-7.5-terraform-dev-environment-and-oidc-deploy.md
- architecture — docs/architecture/architecture.md, AD-20, AD-23 (amended 2026-10-31), AD-28
- prd — docs/prd/prd.md, NFR-SEC-5, NFR-SEC-7, NFR-OBS-2
- production path — docs/production-path.md, P43

## Notes

- Risk medium (proposed): a wrong header or log format can break the hosted login or the SPA in AWS, or keep customer request data in logs; nothing here serves real customers. Beyond the criteria, the CI journeys exercise the hosted login under criterion 3's headers.
- Decision: 2026-10-31 (human, 7.10 disposition) — A-F4, A-F5, A-F6, B-F1, B-F2, F-F3, F-F4, F-F21 and F-F22 are fixed in this story; for F-F3 the scope is the access logs on every origin (path without the query string).
- Decision: 2026-10-31 (human, 7.12 pre-build) — AD-23 amended: the Api still owns `Cache-Control: no-store` on every response it produces; nginx adds it only to `/api` responses it generates itself, so no response carries it twice. The location-level form that replaces the Api's header is out; criterion 7 lists the gateway-generated statuses.
- Decision: 2026-10-31 (human, 7.12 pre-build) — the nginx error logs on all three origins (request line and Referer on a 429 or an upstream failure) go to P43 and are accepted for the demo; the access logs are fixed here.
- Decision: 2026-10-31 (Sofia, 7.12 write) — the identity origin's `Referrer-Policy` is `strict-origin-when-cross-origin`, the app's value, not `no-referrer`: with `no-referrer` a browser sends `Origin: null` on the hosted login's form posts, which the login may refuse; the identity access log already excludes the Referer. It is stricter than the login's own `origin-when-cross-origin`, which it replaces.
- Decision: 2026-10-31 (Sofia, 7.12 refine) — `GATEWAY_HSTS` is two years (`max-age=63072000; includeSubDomains`), so hiding the login's HSTS never shortens what a browser is told; `preload` is not added, since the hosts are CloudFront default domains that cannot be submitted to a preload list.
- Decision: 2026-10-31 (Sofia, 7.12 refine) — `https-only` is set on `/api/*` of the app and Mailpit distributions and on the whole identity distribution, whose OAuth and OIDC endpoints are not under `/api/` and which serves no browser navigation that needs an HTTP redirect.
- Decision: 2026-10-31 (Sofia, 7.12 refine) — nginx's 400, 414 and 494 for a malformed or oversized request header, and the AWS origin-verify 403, are produced before nginx chooses a location, so a rule limited to `/api` locations cannot reach them; they carry nothing from the Api, and the 403 is never seen through CloudFront. They keep today's headers in this story.
- Decision: 2026-10-31 (Sofia, 7.12 refine) — a gateway-generated 413, 502 or 504 keeps nginx's default body or a fixed text; a problem-details body for gateway failures is not in the review's finding.
- Assumption: the three served hosts are CloudFront default domains, under which DisputeDesk serves no other name, so `includeSubDomains` covers only HTTPS hosts; a mocked plan cannot prove what is served after a deploy, so the build report states it and the README post-deploy checks keep it visible.
- Collision: touches `PlatformScriptTests.cs` (criteria 6 and 7 only); 7.13 and 7.14 do not touch it, so all three may build in parallel. `docker-compose.aws.yml` is touched here only; 7.13 touches `docker-compose.yml`.
- Production path: P43 keeps the nginx error logs on every origin (request line and Referer); this story does not narrow it.
