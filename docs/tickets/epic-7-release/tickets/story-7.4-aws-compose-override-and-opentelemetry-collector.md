---
id: 4
type: story
title: "AWS Compose override and OpenTelemetry collector"
parent: epic-7-release
covers: [R32, R36, R38]
after: [3, 5.1, 11]
hitl: false
risk: medium
refined: false
---

# AWS Compose override and OpenTelemetry collector

## Description

Adds `docker-compose.aws.yml`, the AWS gateway template, the OpenTelemetry Collector config and the configuration switch that stores the Data Protection key ring in SSM, so the stack 7.5 deploys is defined and checked without an AWS account. The override takes every secret and deployment value from the environment with no default, replaces every local-only value of `docker-compose.yml` through Compose's merge rules, starts no Postgres and no Aspire Dashboard, and fits the instance. The AWS gateway trusts CloudFront's client address and scheme, refuses requests without the origin-verify header on all three hosts, serves Mailpit on its own origin behind basic auth (the app host answers 404 on `/mailpit/`), lets only the Api and the collector reach the instance metadata service, and does not route `/otlp/`: in AWS no browser telemetry is taken in, so browser spans stay a local-stack feature, while the Api's and the Worker's traces, metrics and logs reach X-Ray and CloudWatch through the collector (decided 2026-10-30). Every check here runs locally; what only a deployment can prove (instance-role access to SSM and KMS, the collector's export, CloudFront's headers) is written into 7.5's post-deploy checks.

## Requirements

Verify: `infra/aws/check-compose.sh` (the render checks of criteria 1 and 2), `infra/aws/gateway.test.sh` (criteria 3, 4, 6 and 7, against the gateway image and a Mailpit container on a throwaway Docker network), `otelcol-contrib validate` on the collector config (criterion 5) and the Api integration tests (criterion 8) pass locally and in CI's `platform` and `backend` jobs; `actionlint` passes; `docker compose config --quiet` on the local file alone still passes and the local stack is unchanged apart from criterion 7's cache headers.

Inputs (each required in the override as `${VAR:?<message>}`, no inline default; `infra/aws/compose.test.env` holds a dummy value for each, used only by the checks):
- Secrets (SSM SecureStrings at deploy, AD-26), every one generated, never chosen by hand: `APP_DB_PASSWORD`, `ZITADEL_DB_PASSWORD` and `LOGIN_SESSION_COOKIE_SECRET` (32 to 128 characters of `[A-Za-z0-9]`, because `;` would inject Npgsql connection-string keywords, `?`, `#` and `%` break Zitadel's DSN URL, and `$`, quotes and spaces break `.env` interpolation); `ZITADEL_MASTERKEY` (exactly 32 characters of `[A-Za-z0-9]`); `ZITADEL_ADMIN_PASSWORD` (32 to 128 characters of `[A-Za-z0-9-]` with at least one upper-case letter, one lower-case letter, one digit and one `-`, because Zitadel's default password complexity policy requires a symbol); `LINKING_OTP_HMAC_KEY` (base64, within `LinkingOptions`' existing bounds); `MAILPIT_UI_AUTH` (one `user:password` pair, user `[A-Za-z0-9]{1,32}`, password `[A-Za-z0-9]{32,128}`); `GATEWAY_ORIGIN_VERIFY` (the origin-verify header value, 32 to 128 characters of `[A-Za-z0-9]`). Story 7.5's deploy script checks each value against its bound before it writes `.env`.
- Deployment values (Terraform outputs through SSM, AD-28): `APP_HOST`, `AUTH_HOST` and `MAILPIT_HOST` (the three CloudFront domains, host names only, no scheme or port); `DB_HOST` (the RDS endpoint host name); `AWS_REGION`; `OTEL_LOG_GROUP` (the collector's log group); `DATAPROTECTION_SSM_PATH` (the key-ring parameter path: starts with `/`, no trailing `/`, and outside 7.5's `ssm_path`, for example `/disputedesk-dataprotection/dev`); `DATAPROTECTION_KMS_KEY_ID` (the one customer-managed KMS key 7.5 creates, named by its key ARN or its bare key ID, never an alias; 7.4 build: test values use the key ID form because the secret scan refuses a literal account ID); `RDS_CA_BUNDLE` (path on the instance of the RDS CA bundle); `GATEWAY_REALIP_CONF` (path on the instance of the `set_real_ip_from` file 7.5's deploy script writes); `API_IMAGE`, `WORKER_IMAGE`, `GATEWAY_IMAGE`, `CORE_BANKING_IMAGE`, `PROVISION_IMAGE` (each `ghcr.io/<owner>/<name>@sha256:<64 hex>`, the digests of 7.3's published images, AD-32).
- Fixed in the override, not inputs: ports, service names, `ZITADEL_EXTERNALPORT: "443"`, `ZITADEL_EXTERNALSECURE: "true"`, `BFF_DEVELOPMENT_MODE: "false"`, the memory limits, the collector image digest, the Compose network's IPv4 subnet and the pinned `api` and `otel-collector` addresses in it (7.5's `DOCKER-USER` rule names the same subnet and addresses).

1. **Every AWS input is required, and no local default can stand in for one** (R32; NFR-SEC-9, AD-26)
   **Given** `docker compose -f docker-compose.yml -f docker-compose.aws.yml --env-file infra/aws/compose.test.env config`
   **When** `check-compose.sh` renders it once with every input and once with each input removed in turn
   **Then** the full render succeeds, and each removal fails with Compose's required-variable error naming that variable (Compose, *Interpolation*, `${VAR:?err}`)
   **And** no value in the override uses `:-` or `-` defaults, and every key whose base value is `${X:-dev-only…}` is redefined in the override, so the render's output holds no `dev-only` or `DevOnly` string (Compose, *Merge Compose files*)
   **And** every value in `compose.test.env` meets its Inputs bound, checked by `check-compose.sh` with the same patterns 7.5's deploy script uses

2. **The AWS render keeps nothing local, and fits the instance** (R38; NFR-INFRA-3, AD-24, AD-27, AD-28)
   **Given** the full render as JSON (`config --format json`)
   **When** `check-compose.sh` reads it with `jq`
   **Then** the services started without a profile are exactly `redpanda`, `redpanda-topics`, `zitadel`, `provision`, `zitadel-login`, `mailpit`, `core-banking`, `api`, `worker`, `otel-collector` and `gateway`; `postgres` and `aspire-dashboard` carry `profiles: [local]` and no started service depends on them, every replaced `depends_on`, `ports` and `environment` entry using Compose's `!override` or `!reset` tags (Compose, *Merge Compose files*, Reset and Override)
   **And** the only published port is the gateway's `80:8080`; no value holds `localhost`, `auth.localhost`, `:8080` in a public URL, `http://` in a public URL, `Development`, `sslmode=disable` or `developmentMode` true; Zitadel's DSN and the app connection string use `sslmode=verify-full` with `${RDS_CA_BUNDLE}` mounted read-only (AD-28); Zitadel has `ExternalDomain` `${AUTH_HOST}`, port 443, secure true and TLS off behind CloudFront (AD-24); the Api has `ForwardedHeaders__AllowedHosts: ${APP_HOST}` and no cookie-name or `SecurePolicy` override, so appsettings' `__Host-` names and `Always` apply (AD-24); `api`, `worker`, `gateway`, `core-banking` and `provision` use the `*_IMAGE` digests and have no `build`
   **And** the override defines the one Compose network with a fixed IPv4 subnet (`ipam`) and pins `ipv4_address` for `api` and `otel-collector` only (Compose, *Networks top-level element*, `ipam`; *services*, `networks.ipv4_address`), so 7.5's `DOCKER-USER` rule can let exactly those two reach 169.254.169.254; `otel-collector` and `mailpit` have no `ports:`; Mailpit has no `MP_WEBROOT`, because it is served at the root of its own origin
   **And** containers reach the identity origin at `https://${AUTH_HOST}` through CloudFront, so the gateway has no `auth.localhost` alias in AWS; Redpanda runs with `--memory=512M`, and the `mem_limit` values of the long-running services above sum to at most 3482 MiB (3.4 GiB), with the one-shots `redpanda-topics` and `provision` listed apart because the Api starts only after both complete (AD-28)

3. **The AWS gateway trusts CloudFront only, and all three hosts need the origin header** (R32, R38; NFR-INFRA-3, AD-23)
   **Given** the gateway image started with the AWS templates (selected by `NGINX_ENVSUBST_TEMPLATE_DIR` in the override; nginx official image, *Using environment variables in nginx configuration*) and the test values
   **When** `gateway.test.sh` sends requests to the app, identity and Mailpit hosts
   **Then** `nginx -t` passes on the rendered config; each server block has `set_real_ip_from` only from the included `${GATEWAY_REALIP_CONF}` file and `real_ip_header CloudFront-Viewer-Address`, so a request with `CloudFront-Viewer-Address: 198.51.100.10:46532` from a trusted address reaches the upstream as `X-Forwarded-For: 198.51.100.10`, the realip module stripping the port itself (nginx, *ngx_http_realip_module*; IPv4 only, because every distribution has IPv6 disabled, AD-23); `X-Forwarded-Proto` is taken from `CloudFront-Forwarded-Proto`
   **And** a spoofed header is ignored (AD-23): a request carrying `CloudFront-Viewer-Address` from an address outside the realip file reaches the upstream with `X-Forwarded-For` set to the socket address; a trusted request that also carries a client-sent `X-Forwarded-For: 203.0.113.9` reaches the upstream with only the viewer address (`$remote_addr`, never `$proxy_add_x_forwarded_for`); `real_ip_recursive` stays off; a request with no `CloudFront-Forwarded-Proto` reaches the upstream with `X-Forwarded-Proto: https`, never empty
   **And** on the app, identity and Mailpit hosts, a request with no `X-Origin-Verify` header and one with a wrong value both get 403 before any upstream is called, and the right value passes (CloudFront, *Adding custom headers to origin requests*); every proxied location sets `proxy_set_header X-Origin-Verify "";`, so the upstream echo never sees the secret, and no log format includes it; the gateway's health check sends the header; `Strict-Transport-Security` is set (AD-23)
   **And** with the right origin header, the identity host answers 404 on `/ui/console` and `/ui/console/`, while `/.well-known/openid-configuration`, `/oauth/v2/*` and `/ui/v2/login` still reach Zitadel (ASVS 5.0 V13.4; provisioning uses the API, not the console)

4. **Mailpit sits on its own origin behind basic auth in AWS** (R32; NFR-SEC-11, NFR-INFRA-3, AD-28)
   **Given** Mailpit with `MP_UI_AUTH: ${MAILPIT_UI_AUTH}`, no `MP_WEBROOT` and no host port, and the AWS template's own server block for `${MAILPIT_HOST}` with the origin-verify check of criterion 3 (decided 2026-10-30)
   **When** `gateway.test.sh` calls `/` and `/api/v1/messages` on the Mailpit host, and `/mailpit/` and `/mailpit/api/v1/messages` on the app host, each with the right origin header
   **Then** on the Mailpit host both answer 401 with no credentials and with wrong ones, and 200 with the right pair (Mailpit, *Basic authentication*, web UI and API); on the app host both answer 404 and reach no upstream, so no third-party UI shares the SPA's origin (OWASP ASVS 5.0 V3, same-origin isolation)
   **And** the Mailpit server block sits under `limit_req zone=coarse`, shown by the test once the limit is exceeded (ASVS 5.0 V2.4); it sends `Strict-Transport-Security`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `X-Frame-Options: DENY` and `frame-ancestors 'none'`, but not the app CSP, which would block Mailpit's own UI, and the test checks the page loads its scripts

5. **The collector takes the dashboard's place for the Api and the Worker** (R36; NFR-OBS-4, AD-29)
   **Given** `infra/otel-collector/config.yaml` and the `otel-collector` service (`otel/opentelemetry-collector-contrib`, pinned by tag and digest, with a `mem_limit`)
   **When** `otelcol-contrib validate --config` runs on it in CI
   **Then** it passes, and the config has an `otlp` receiver with its endpoints set explicitly to `0.0.0.0:4317` (gRPC) and `0.0.0.0:4318` (HTTP), because recent collector versions bind to localhost by default, with no host port; no pipeline uses the `debug` exporter, checked by a `grep` in `check-compose.sh`; `memory_limiter` first, then `batch`, in every pipeline; a `traces` pipeline to the X-Ray OTLP endpoint and a `logs` pipeline to the CloudWatch Logs OTLP endpoint with the `x-aws-log-group: ${OTEL_LOG_GROUP}` header, both through `otlphttp` with the `sigv4auth` extension and the instance role, and a `metrics` pipeline through the `awsemf` exporter (OpenTelemetry Collector, *Configuration*; AWS, *CloudWatch OTLP endpoints*)
   **And** the Api and the Worker get `OTEL_EXPORTER_OTLP_ENDPOINT: http://otel-collector:4317`; the hosts' code is unchanged (AD-29)

6. **No browser telemetry intake in AWS** (R36; NFR-OBS-1, AD-29, decided 2026-10-30)
   **Given** the AWS gateway and the gateway image published from main
   **When** the browser posts to `/otlp/v1/traces`, `/otlp/v1/metrics` or `/otlp/v1/logs`
   **Then** the AWS template's `location /otlp/ { return 404; }` answers 404 and no request reaches any upstream, tested in `gateway.test.sh`
   **And** the gateway Dockerfile's SPA stage takes `ARG BROWSER_TELEMETRY=on` as `VITE_BROWSER_TELEMETRY` (Docker, *Build variables*; Vite, *Env Variables and Modes*), and 7.3's `publish` job builds the gateway with `BROWSER_TELEMETRY=off`, so the SPA in AWS starts no tracing and posts nothing (story 7.11); the local build keeps `on`, the local template keeps its `/otlp/` proxy, and the CSP stays `connect-src 'self'`

7. **Static files carry cache headers on both gateways** (R32; AD-23)
   **Given** the local and the AWS templates
   **When** `/`, a client route and an `/assets/` file are fetched
   **Then** `index.html` (served for `/` and client routes) carries `Cache-Control: no-cache` and `/assets/` files carry `Cache-Control: public, max-age=31536000, immutable` (RFC 9111 §5.2.2.4, RFC 8246); each of these locations repeats the security headers, because a location with its own `add_header` inherits none (nginx, *add_header*); tested in `gateway.test.sh` and in the existing local platform test

8. **Data Protection keys go to SSM in AWS, chosen by configuration** (R32, R2; NFR-SEC-9, NFR-SEC-1, AD-7)
   **Given** the Api's Data Protection registration in `BffAuthentication`
   **When** `DataProtection:SsmParameterPath` is set (the override sets it from `${DATAPROTECTION_SSM_PATH}`)
   **Then** the key ring is stored through AWS's `Amazon.AspNetCore.DataProtection.SSM` package (`PersistKeysToAWSSystemsManager`) as SecureStrings under that path, encrypted with the KMS key named explicitly by `DataProtection:KmsKeyId` (the override sets it from `${DATAPROTECTION_KMS_KEY_ID}`) through the package's documented `KMSKeyId` option (checked against the pinned version), so SSM never falls back to `alias/aws/ssm`; `SetApplicationName("DisputeDesk")` is unchanged; when the path is unset, keys stay in Postgres (`PersistKeysToDbContext<AppDbContext>()`, unchanged locally); the keys are validated with `ValidateOnStart` (the path starts with `/` and has no trailing `/`; when the path is set the key ID is required)
   **And** an integration test resolves `KeyManagementOptions` with and without the path and asserts the repository type and the configured key ID, with no AWS call; the builder verifies the package (publisher, download count, repository, latest stable) and pins it exactly (AGENTS.md, Dependency verification)

## Boundaries

- Must not change: the local stack's services, ports, values and behaviour (`docker compose up --wait` and the e2e suite), apart from criterion 7's cache headers; the local `/otlp/` proxy and the browser trace locally (story 3.11); the hosts' telemetry code (AD-29).
- No AWS or Terraform command runs here; the instance role's SSM, KMS and CloudWatch permissions and the CloudFront distributions are 7.5's.
- Not proven here, written into 7.5's post-deploy checks: the instance role reaching SSM and KMS, the collector exporting to X-Ray and CloudWatch, CloudFront sending `CloudFront-Viewer-Address`, `CloudFront-Forwarded-Proto` and `X-Origin-Verify`, sign-in over CloudFront, and Redpanda's memory under load at 512M.
- No extra services beyond the collector; no credential proxy; no configuration framework.
- Not here, written into 7.5: the `DOCKER-USER` rule itself and its test, the third distribution, the HTTP origin `# shortcut:` and the post-deploy metadata check (a request from Mailpit fails while the Api gets credentials).
- Accepted for the synthetic demo, in the production path (story 7.6): CloudFront to origin is plain HTTP; the default CloudFront certificate allows TLS 1.0 and there is no WAF; the fixture logins with passwords published in `fixtures/local.json` are live in AWS (decided 2026-10-30).

## Supervisor checks

Deliberate-break proofs, run by the supervisor, not the verifier.

- In a throwaway copy, change one input to `${APP_HOST:-localhost}`: criterion 1's check fails.
- In a throwaway copy, drop the `!override` on the api's `depends_on`: criterion 2's check fails (the api still depends on `postgres`).
- In a throwaway copy, remove the origin-verify check from the identity server block: criterion 3's test fails.
- In a throwaway copy, put a `/mailpit/` proxy location back on the app host: criterion 4's test fails.
- In a throwaway copy, remove `proxy_set_header X-Origin-Verify "";` from one location: criterion 3's upstream-echo test fails.
- In a throwaway copy, put the local `/otlp/` proxy location into the AWS template: criterion 6's test fails.
- Locally, run the stack with Redpanda at `--memory=512M` through the full e2e suite and record `docker stats` peaks per service in `docs/verification/7.4-review.md`; any service over its AWS `mem_limit` is a finding.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes (the Data Protection key ring in AWS, the edge's trust rules).
- Lane: B.
- Mechanisms and sources: Compose interpolation and merge rules, `!reset` and `!override`, profiles (<https://docs.docker.com/reference/compose-file/interpolation/>, <https://docs.docker.com/compose/how-tos/multiple-compose-files/merge/>, <https://docs.docker.com/compose/how-tos/profiles/>); nginx official image template rendering (<https://hub.docker.com/_/nginx>, *Using environment variables in nginx configuration*); nginx realip and `add_header` (<https://nginx.org/en/docs/http/ngx_http_realip_module.html>, <https://nginx.org/en/docs/http/ngx_http_headers_module.html>); CloudFront origin custom headers (<https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/add-origin-custom-headers.html>); Compose networks, `ipam` and `ipv4_address` (<https://docs.docker.com/reference/compose-file/networks/>, <https://docs.docker.com/reference/compose-file/services/#ipv4_address-ipv6_address>); nginx `limit_req` (<https://nginx.org/en/docs/http/ngx_http_limit_req_module.html>); Zitadel's default password complexity policy (<https://github.com/zitadel/zitadel/blob/main/cmd/defaults.yaml>, `PasswordComplexityPolicy`); OWASP ASVS 5.0 V2.4, V3, V4, V6.2, V13.3, V13.4 (<https://github.com/OWASP/ASVS>); Mailpit basic authentication and webroot (<https://mailpit.axllent.org/docs/configuration/http/>; `MP_UI_AUTH` and `MP_WEBROOT` checked against v1.32.0 on 2026-10-30: 401 on the UI and API without credentials, 200 with); OpenTelemetry Collector configuration, `memory_limiter`, `sigv4auth`, `awsemf` (<https://opentelemetry.io/docs/collector/configuration/>, opentelemetry-collector-contrib READMEs for the pinned version); CloudWatch OTLP endpoints (<https://docs.aws.amazon.com/AmazonCloudWatch/latest/monitoring/CloudWatch-OTLPEndpoint.html>); RFC 9111 and RFC 8246 cache directives; Docker build arguments (<https://docs.docker.com/build/building/variables/>); Vite env variables (<https://vite.dev/guide/env-and-mode>); AWS SSM Data Protection provider (<https://github.com/aws/aws-ssm-data-protection-provider-for-aspnet>); ASP.NET Core key storage providers (<https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-storage-providers>).
- Packages: `Amazon.AspNetCore.DataProtection.SSM` (verified and pinned by the builder); the collector image pinned by tag and digest.
- Touches:
  - `docker-compose.aws.yml` (new)
  - `infra/aws/compose.test.env` and `infra/aws/check-compose.sh` (new: the render checks)
  - `infra/aws/gateway.test.sh` and `infra/aws/test-realip.conf` (new: the gateway checks, with documentation ranges)
  - `infra/nginx/templates-aws/` (new: the AWS `nginx.conf.template` with the app, identity and Mailpit server blocks, and a Mailpit header set; the shared security and Scalar header templates are copied in by the Dockerfile from one source)
  - `infra/nginx/templates/nginx.conf.template` (criterion 7's cache headers only)
  - `infra/nginx/Dockerfile` (the AWS template directory, `ARG BROWSER_TELEMETRY`)
  - `infra/otel-collector/config.yaml` (new)
  - `.github/workflows/ci.yml` (`platform` job: the two scripts and `otelcol-contrib validate`; `publish` job: the gateway's `BROWSER_TELEMETRY=off` build argument)
  - `backend/src/DisputeDesk.Api/Bff/BffAuthentication.cs` (key storage by configuration), its options class, `backend/Directory.Packages.props`, `backend/src/DisputeDesk.Api/DisputeDesk.Api.csproj`
  - `backend/tests/DisputeDesk.Api.IntegrationTests/` (the key-storage test)
  - the existing local platform test for criterion 7 (`backend/tests/DisputeDesk.ArchitectureTests/PlatformScriptTests.cs` or `infra/smoke.sh`, whichever already checks gateway headers)

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-7, AD-23, AD-24, AD-26, AD-27, AD-28, AD-29, AD-32
- prd — docs/prd/prd.md, NFR-SEC-1, NFR-SEC-9, NFR-SEC-11, NFR-OBS-1, NFR-OBS-4, NFR-INFRA-3
- story — docs/tickets/epic-7-release/tickets/story-7.11-spa-skips-browser-tracing-when-built-without-it.md (the SPA side of criterion 6)
- verification — docs/verification/7.4-challenge.md, docs/verification/7.4-prebuild-security.md

## Notes

- Risk medium (proposed): a wrong edge or override exposes the AWS demo or breaks sign-in there, but nothing here runs against real customers; beyond the criteria, the supervisor's memory measurement under the e2e suite checks the instance budget.
- Gold-plating watch: no extra services beyond the collector.
- Decision: 2026-10-27 — Data Protection keys are encrypted at rest in AWS through `Amazon.AspNetCore.DataProtection.SSM` (AD-7); this story owns it.
- The instance role's SSM and KMS permissions for the key-ring parameter path belong to the Terraform compute module (7.5).
- Input (from 2.5 challenge, 2026-10-28): the AD-23 AWS edge rules are unowned until here. The AWS nginx template sets `set_real_ip_from` to the CloudFront origin-facing ranges with `real_ip_header CloudFront-Viewer-Address` (port stripped), takes `X-Forwarded-Proto` from `CloudFront-Forwarded-Proto`, and the rendered AWS config is checked with `nginx -t` (nginx realip module; AD-23). Add these as criteria when 7.4 is made ready. Done: criterion 3.
- Input (from 2.5 security F2, 2026-10-28): `/otlp/v1/*` accepts anonymous POSTs, so in AWS anyone can write spans, logs and metrics into X-Ray and CloudWatch, bounded only by the coarse limit and the 1 MB cap (AD-29 assumption). Decide here whether that is accepted, and cap it in the collector (`memory_limiter`) either way (OWASP ASVS 5.0 V16). Settled: the human decision below removes the intake; `memory_limiter` stays (criterion 5).
- Input (from 2.5 QA Q1, 2026-10-28): gateway static responses (`/`, `/assets/*`) carry no `Cache-Control`, so browsers cache heuristically and can keep a stale `index.html` after a release. Set `Cache-Control: no-cache` on `index.html` and `public, max-age=31536000, immutable` on `/assets/` (RFC 9111 §4.2.2, RFC 8246); any location with its own `add_header` repeats the security headers. Done: criterion 7.
- Decision: 2026-10-30 (human, 7.4 challenge) — no browser telemetry intake in AWS: the AWS gateway does not route `/otlp/` and the SPA exports nothing there; browser spans stay a local-stack feature; the Api's and the Worker's telemetry still reaches CloudWatch and X-Ray through the collector. AD-29 is amended. Criteria 5 and 6.
- Decision: 2026-10-30 (Sofia, refine) — the SPA side of "exports nothing" needs a frontend change the backend builder cannot write (`frontend/` is the frontend builder's), so it is split out as story 7.11 (owner frontend), which 7.4 waits on. The switch is a Vite build-time variable set through a gateway Dockerfile argument: the image published from main is built with it off, the local build and the e2e stack keep it on.
- Assumption: the published gateway image differs from the e2e-tested local build only in `BROWSER_TELEMETRY`, which removes browser tracing and nothing else (story 7.11's tests prove the off path). Accepted as the simplest switch; a runtime switch would need the gateway to rewrite `index.html`.
- Decision: 2026-10-30 (Sofia, refine; superseded the same day by the human decision on S7.4-1 below for the host) — Mailpit is protected by Mailpit's own web UI and API basic auth (`MP_UI_AUTH`), which covers its API as well as its pages; nginx's `auth_basic` would need a generated password file and protect the same thing.
- Decision: 2026-10-30 (Sofia, refine) — in AWS, containers reach the identity origin at its public HTTPS URL through CloudFront (which adds the origin-verify header), not through a network alias, because the issuer is `https://${AUTH_HOST}` and the gateway serves plain HTTP behind CloudFront. Proposed as an addition to AD-28's Edge line.
- Verified 2026-10-30 against `nginxinc/nginx-unprivileged:1.31.7-alpine`: with `real_ip_header CloudFront-Viewer-Address`, `198.51.100.10:46532` becomes `$remote_addr` `198.51.100.10` with no extra parsing; an unbracketed IPv6 value is not parsed, which IPv6 being disabled on both distributions makes moot (AD-23).
- Assumption: the CloudFront origin-facing ranges reach nginx as an included file that 7.5's deploy script writes from AWS's published `ip-ranges.json` (service `CLOUDFRONT_ORIGIN_FACING`); this story checks the include with documentation ranges.
- Production path (for 7.6): browser telemetry in a deployed environment needs an authenticated or sampled intake (for example a collector behind the BFF session with per-session limits) before it is switched on.
- Decision: 2026-10-30 (human, pre-build security S7.4-1) — Mailpit in AWS lives on its own origin: a third CloudFront distribution and its own nginx server block with basic auth and the origin-verify check; the app host answers 404 on `/mailpit/`. AD-28's Images line is amended. Criterion 4.
- Decision: 2026-10-30 (human, pre-build security S7.4-8, shared with 7.5) — only the Api and the collector may reach the instance metadata service: this override pins their container addresses (criterion 2), and 7.5's `deploy.sh` adds a `DOCKER-USER` iptables rule dropping traffic to 169.254.169.254 from every other container (the Amazon ECS Developer Guide's method), with a test. AD-28's Compute line is amended.
- Decision: 2026-10-30 (human, pre-build security S7.4-9) — CloudFront to origin stays plain HTTP for the free-plan demo with synthetic data, marked `# shortcut:` in 7.5's Terraform; an HTTPS or VPC origin, a domain, an ACM certificate with a TLS 1.2 minimum viewer protocol, and WAF are a production-path entry (story 7.6). AD-28's Edge line is amended.
- Decision: 2026-10-30 (human, pre-build security S7.4-11) — accepted: the fixture logins with passwords published in the repo are live on the AWS demo, and self-registration can link customer D; 7.5's README says so, and the production path (story 7.6) records it.
- Decision: 2026-10-30 (Sofia, pre-build security S7.4-1) — "basic auth" on the Mailpit origin stays Mailpit's own `MP_UI_AUTH`, as the refine decision above chose; the new server block adds the origin-verify check and the coarse limit.
- Decision: 2026-10-30 (Sofia, pre-build security S7.4-2, S7.4-6) — every secret is generated from `[A-Za-z0-9]`, except the Zitadel admin password, which also needs `-` because Zitadel's default complexity policy requires a symbol (`cmd/defaults.yaml`, `HasSymbol: true`, read 2026-10-30 on the main branch; the builder checks the pinned version); `-` is safe in `.env` and never sits in a DSN.
- Decision: 2026-10-30 (Sofia, pre-build security S7.4-4) — a request with no `CloudFront-Forwarded-Proto` gets `X-Forwarded-Proto: https`, not 403: the edge only ever serves HTTPS viewers (redirect), so `https` is the true scheme, and a 403 would break the health check.
- Decision: 2026-10-30 (Sofia, pre-build security S7.4-7) — the KMS key is named by a new configuration key `DataProtection:KmsKeyId` (AD-26's key list amended), and the key-ring path is a separate root outside 7.5's `ssm_path`.
