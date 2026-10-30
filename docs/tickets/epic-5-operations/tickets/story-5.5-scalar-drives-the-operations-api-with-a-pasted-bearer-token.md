---
id: 5
type: story
title: "Scalar drives the operations API with a pasted bearer token behind a switch"
parent: epic-5-operations
covers: [R39, R21]
after: [1]
hitl: false
risk: high
---

# Scalar drives the operations API with a pasted bearer token behind a switch

## Description

Makes Scalar the stand-in operations tool (NFR-DOC-4) without giving it any credential. Scalar and the runtime OpenAPI document move behind `Scalar:Enabled` (AD-26). The document declares one `bearer` security scheme, on the operations routes only. The gateway serves the page at `/scalar/` with its own CSP, which allows Scalar's one inline script by one fixed hash. A person gets a token from story 5.1's `scripts/operations-token.sh` and pastes it into Scalar, which story 5.2 and the README (7.7) then use.

## Requirements

Verify: Integration tests cover the switch and its bounds, Scalar's served configuration, the page and the runtime document holding no configured value, the security scheme in the runtime and committed documents, and the served inline script's hash against the nginx template; `infra/smoke.sh` covers the gateway routes, the Scalar headers and a 429 on `/scalar/`; `openapi.json` changes only by the security scheme and requirements, and `frontend/src/api/generated/` matches it.

1. **Scalar and the document behind a bounded switch** (R39; NFR-DOC-4, AD-26)
   **Given** an `ApiReferenceOptions` class bound to the `Scalar` section with `ValidateOnStart` (Microsoft Learn, *Options pattern*), whose `Enabled` is `false` in `appsettings.json`, `true` in `docker-compose.yml` (`Scalar__Enabled`) and `true` in the host-run Development launch profile
   **When** `Scalar:Enabled` is `true`, `false`, `TRUE`, absent, or `yes`, in the `Production` environment each time
   **Then** `true` and `TRUE` map `/scalar/`, its files and `/openapi/v1.json`, served anonymously (`AllowAnonymous()`); `false` and absent map none of them, so each gets 404 signed in and the fallback policy's 401 anonymous, as `ApiReferenceTests` pins today (story 2.8); `yes` fails start
   **And** `Program.cs` no longer checks `IsDevelopment()` for them (AD-26: feature switches are configuration keys), and `ApiReferenceTests` and `EndpointAuthorizationTests` key the anonymous Scalar endpoints on the setting instead of the environment

2. **Scalar holds no credential and leaks no configuration** (R39, R21; NFR-DOC-4, NFR-SEC-2)
   **Given** Scalar's options (Scalar.AspNetCore 2.18.3): `DisableDefaultFonts()` (its default loads fonts from a CDN, which `font-src 'self'` blocks), `DisableTelemetry()`, `DisableAgent()`, `DisableMcp()`, `HideDeveloperTools()`, no `WithProxyUrl`, persistent authentication left off, and no client ID, client secret, token URL, token or preferred scheme set
   **When** `/scalar/` and `/openapi/v1.json` are served by two hosts whose `Zitadel:Issuer`, `Zitadel:ProjectId` and `Operations:Actors` differ
   **Then** the two pages are byte-identical, and the test parses the configuration passed to the page's `initialize` call: `withDefaultFonts` false, `telemetry` false, `agent.disabled` true, `mcp.disabled` true, developer tools never shown, `persistAuth` absent or false, and no `proxyUrl` or `authentication` key
   **And** neither the page nor the runtime document holds the issuer, `Zitadel:ProjectId`, any `Operations:Actors` sub, `clientId` or `clientSecret` (RFC 6749 §2.1, §10.1: a secret delivered to a browser is not confidential; ASVS 5.0 V13.3.1, V13.4)

3. **One bearer scheme on the operations routes only** (R39, R21; NFR-API-1, NFR-DOC-4)
   **Given** the built-in OpenAPI document with a document transformer for the scheme and an operation transformer for the requirements (Microsoft Learn, *Customize OpenAPI documents*, its bearer security scheme sample)
   **When** the runtime `/openapi/v1.json` and the committed `backend/openapi.json` are read
   **Then** each declares exactly one security scheme (OpenAPI 3.1 Security Scheme Object): `bearer`, with `type: http`, `scheme: bearer`, `bearerFormat: JWT`, which Scalar's paste field uses; no `oauth2` scheme and no `tokenUrl` (decision 2026-10-30)
   **And** every operation under `/api/v1/operations/` carries exactly `security: [{"bearer": []}]` (OpenAPI 3.1 Security Requirement Object), and no other operation carries a `security` field

4. **The gateway forwards the Scalar routes to the Api** (R39; NFR-SEC-7, AD-23)
   **Given** `infra/nginx/templates/` and `Scalar:Enabled` true in Compose
   **When** `/scalar`, `/scalar/`, `/scalar/scalar.js`, `/scalar/scalar.aspnetcore.js`, `/scalar/favicon.svg` and `/openapi/v1.json` are requested through the gateway
   **Then** an exact `location = /scalar`, a prefix `location /scalar/` and an exact `location = /openapi/v1.json` proxy them to the Api (today they fall to the SPA's `try_files`), with the path unchanged (`proxy_pass http://api;` with no URI part, no rewrite, no `X-Forwarded-Prefix`), `limit_req zone=coarse burst=20 nodelay`, and the same forwarded headers as `/api/`
   **And** `/scalar` gets the Api's 302 to `scalar/`, the page and its files get 200, and `/openapi/v1.json` gets 200; no other `/openapi/` path reaches the Api
   **And** with `Scalar:Enabled` false, the same requests reach the Api and get its 401 anonymously, never the SPA's `index.html`

5. **The Scalar locations send their own CSP and every other header once** (R39, R21; NFR-SEC-7, AD-23)
   **Given** a new `scalar-headers.conf.template` included by the three locations of criterion 4
   **When** a response leaves those locations
   **Then** its CSP is exactly `default-src 'self'; script-src 'self' 'sha256-<hash>'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'` (CSP Level 3 hash-source): `script-src` holds one hash and never `'unsafe-inline'` or `'unsafe-eval'` (ASVS 5.0 V3.4.3); `style-src` holds `'unsafe-inline'` and never a hash (decision 2026-10-30; a hash beside it would make the browser ignore `'unsafe-inline'`); `connect-src` is `'self'` only, because Scalar fetches no token itself
   **And** the file repeats the other headers of `security-headers.conf` with the same values (nginx `add_header` inheritance), and `infra/smoke.sh` reads `/scalar/` with GET (`curl -fsS -D - -o /dev/null`, because the Api answers HEAD on it with 405), checks each header exactly once, and checks that each non-CSP header's value equals the one on `/` (OWASP HTTP Headers Cheat Sheet)
   **And** a 429 on the Scalar locations is the unchanged `@rate_limited` response, with the SPA's CSP and each header once; no second named location is added, and `infra/smoke.sh` provokes and checks one 429 on `/scalar/` as it does on `/api/` (RFC 6585)

6. **A test pins the inline script's hash to the nginx template** (R39; NFR-SEC-7, AD-26)
   **Given** the Api serving the page at exactly `/scalar/` (the inline script embeds the request path, so each page path has its own hash)
   **When** the test reads the served HTML and `infra/nginx/templates/scalar-headers.conf.template`
   **Then** the HTML holds exactly one inline `<script>` element, the template's `script-src` holds exactly one `sha256-` source, and the base64 SHA-256 of that script's content equals it (CSP Level 3 hash-source), so a Scalar upgrade or option change that alters the script fails CI
   **And** the same test asserts that the template's `script-src` holds no `'unsafe-inline'` and `style-src` holds no `sha256-` source

## Boundaries

- The SPA's CSP and every other location stay as merged; only the three Scalar locations get the Scalar CSP.
- `/scalar/` is the one supported page path. Another page path under `/scalar/` (for example `/scalar/v1`, Scalar's `{documentName}` route) renders a script the CSP refuses, so it shows a blank page; it fails closed.
- Customer routes declare no security requirement in the document.
- Scalar sends requests to the operations routes on the app origin; the session cookie it may also send is ignored there (story 5.1: the operations group accepts only JwtBearer).
- nginx keeps owning the security headers; the Api sets no CSP and no nonce (AD-23 unchanged).
- No pre-filled credential of any kind, locally or in AWS (decision 2026-10-30).
- The runtime document's `servers` URL comes from the request. Locally the gateway's `Host` makes it same-origin; in AWS it depends on AD-23's forwarded-header handling (story 7.4), without which an `http://` URL is blocked by `connect-src 'self'` on the https page.

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- On the running stack, open `/scalar/`, paste a token from `scripts/operations-token.sh`, and read a dispute customer A raised: 200. Record every CSP violation the browser console shows; the only one expected is the refused `'unsafe-eval'` from the bundled zod capability probe (caught by Scalar; `script-src` stays without `'unsafe-eval'`).
- After pasting the token and sending that request, the browser's `localStorage` for the app origin holds no value containing the token.
- In a throwaway copy, change one Scalar option that alters the inline script: criterion 6's hash test fails.
- In a throwaway copy, set `Scalar:Enabled` false in Compose: `/scalar/` through the gateway gets 401 anonymously.

## Build facts

- Owner: backend (backend-builder).
- Flags: high-stakes (a CSP on the customers' origin).
- Lane: M2, after 5.1 and before 5.2, whose supervisor check uses Scalar.
- Touches:
  - `backend/src/DisputeDesk.Api/Program.cs`, `backend/src/DisputeDesk.Api/ApiReference/` (new: `ApiReferenceOptions`, the Scalar setup, the two transformers), `backend/src/DisputeDesk.Api/appsettings.json`, `backend/src/DisputeDesk.Api/Properties/launchSettings.json` (`Scalar__Enabled: "true"`)
  - `docker-compose.yml` (`Scalar__Enabled: "true"` on the api)
  - `infra/nginx/templates/nginx.conf.template` and a new `scalar-headers.conf.template`, `infra/smoke.sh`
  - `backend/openapi.json`, `frontend/src/api/generated/` (regenerated with `npm run generate`)
  - `backend/tests/DisputeDesk.Api.IntegrationTests/ApiReferenceTests.cs`, `OpenApiDocumentTests.cs`, `Authorization/EndpointAuthorizationTests.cs`, and a repo-root lookup for the hash test, copied from `DisputeDesk.ArchitectureTests`' `SourceRoots`
- Not touched: `infra/nginx/templates/security-headers.conf.template` (the SPA's CSP), `backend/src/DisputeDesk.Api/BuildTimeOpenApi.cs`, the operations endpoints and policies (story 5.1).

## References

- parent — docs/tickets/epic-5-operations/epic-5-operations.md
- architecture — docs/architecture/architecture.md, AD-5, AD-23, AD-26
- prd — docs/prd/prd.md, NFR-API-1, NFR-DOC-4, NFR-SEC-7
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0>, Options pattern, `ValidateOnStart`
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/openapi/customize-openapi?view=aspnetcore-10.0>, document and operation transformers, bearer security scheme sample
- standard — <https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0>, build-time document generation
- standard — <https://github.com/scalar/scalar/tree/main/integrations/dotnet/aspnetcore>, Scalar ASP.NET Core integration and its options
- standard — <https://spec.openapis.org/oas/v3.1.0#security-scheme-object>, Security Scheme Object
- standard — <https://spec.openapis.org/oas/v3.1.0#security-requirement-object>, Security Requirement Object
- standard — <https://www.rfc-editor.org/rfc/rfc6749#section-10.1>, client authentication (a secret in a browser is not confidential)
- standard — <https://www.w3.org/TR/CSP3/#grammardef-hash-source>, CSP Level 3 hash-source
- standard — <https://nginx.org/en/docs/http/ngx_http_headers_module.html#add_header>, `add_header` inheritance
- standard — <https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html>, OWASP HTTP Headers Cheat Sheet
- standard — <https://github.com/OWASP/ASVS>, ASVS 5.0 V3.4.3, V13.3.1, V13.4
- challenge — docs/verification/5.5-challenge.md, docs/verification/5.1-challenge.md
- review — docs/verification/5.5-prebuild-review.md, docs/verification/5.5-prebuild-security.md, docs/verification/5.1-prebuild-review.md, docs/verification/5.1-prebuild-security.md

## Notes

- Risk high: an anonymous page with an inline script on the customers' origin; beyond the criteria, the security agent checks the served page, the runtime document and the gateway response for any credential or configured value and any widened `script-src`.
- Decision: 2026-10-30 (Sofia, 5.1 pre-build review) — split from 5.1 to keep each story within about 8 criteria; its criteria are 5.1's former criteria 6 and 7 plus the pre-build points on Scalar, the document and the gateway.
- Decision: 2026-10-30 (human, 5.1 `[decision]` point 1) — Scalar never holds or pre-fills a client secret; a person pastes a token from `scripts/operations-token.sh`.
- Decision: 2026-10-30 (human, 5.5 pre-build security S2) — the document declares only the `bearer` scheme on the operations routes; no `clientCredentials` scheme. This drops the runtime `tokenUrl` document transformer and the build-time stand-in issuer. Real tools find the token endpoint through OIDC discovery and the README (7.7). Replaces the part of the 5.1 `[decision]` point 2 decision that kept the client-credentials scheme and built its `tokenUrl`.
- Decision: 2026-10-30 (human, 5.1 `[decision]` point 2, kept) — one fixed CSP hash: Scalar's options are identical in every environment, nginx keeps owning the headers (AD-23 unchanged), and a test hashes the served inline script against the nginx template.
- Decision: 2026-10-30 (human) — `style-src 'self' 'unsafe-inline'` on the Scalar locations only: Scalar 2.18.3 adds its stylesheet and `style` attributes at runtime, a nonce is ruled out (AD-23), and a test cannot hash the runtime stylesheet without pulling it out of `scalar.js`. `style-src` runs no script (CSP Level 3); `img-src`, `font-src` and `connect-src` stay `'self'`, which closes the usual ways injected CSS sends data out. Never mixed with a style hash. Recorded in AD-26.
- Decision: 2026-10-30 (Sofia) — NFR-SEC-7's "no inline script" holds for the SPA; `/scalar/` admits Scalar's one inline script by hash only, as AD-26 decided.
- A `Scalar.AspNetCore` upgrade reruns the security stage as well as the hash test: a script on `/scalar/` can do what the SPA can with the session cookie and can read a pasted token from the page's memory, so the controls are the hash-only `script-src`, the bundle served from `'self'` and pinned by the package version, and a server-generated document with no user content rendered as HTML.
- For the builder: an operation's `Description.RelativePath` has no leading slash (`api/v1/operations/...`); match it that way or by endpoint metadata.
- Production path: lines for `docs/production-path.md` are in story 7.6's Notes (Input from 5.1, 2026-10-30).
