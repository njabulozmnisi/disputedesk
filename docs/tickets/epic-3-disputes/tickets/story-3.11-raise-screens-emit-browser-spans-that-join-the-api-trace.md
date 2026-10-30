---
id: 11
type: story
title: "Raise screens emit browser spans that join the API trace"
parent: epic-3-disputes
covers: [R36]
after: [6]
hitl: false
risk: medium
refined: false
---

# Raise screens emit browser spans that join the API trace

## Description

Adds the SPA's OpenTelemetry tracing: same-origin `/api` fetches carry W3C `traceparent`, and the raise flow emits `dispute.raise.open` and `dispute.raise.submit`, so SM-1 (submitted ÷ opened) and SM-2 (open to 201) can be read from the traces (AD-20). Spans export as OTLP/HTTP JSON to the same origin's `/otlp/v1/traces`, which the gateway already proxies (story 2.5). Split from story 3.6 (decided 2026-10-30); it builds on 3.6's raise routes and submit.

## Requirements

Verify: Vitest tests pass with an in-memory span exporter and MSW; `npm run typecheck`, `npm run lint` and `npm test` pass in `frontend/` with no new suppression; the CSP check (criterion 7) passes in the Playwright spec at the QA stage.

Inputs: no new request input. The spans read only values the SPA already holds: the problem `type` of a failed attempt and the HTTP status.

1. **Tracing starts once, and `traceparent` goes only on same-origin requests** (R36; NFR-OBS-1, AD-20)
   **Given** `main.tsx` calls one start function in `frontend/src/telemetry/` before `createRoot(...).render`
   **When** it runs
   **Then** it registers, once: a `TracerProvider` (`@opentelemetry/sdk-trace`) whose resource is `service.name` `disputedesk-web` plus the SDK's default `telemetry.sdk.*` attributes, with no browser resource detector (no user agent or language), set with `trace.setGlobalTracerProvider`; a `StackContextManager` (`@opentelemetry/sdk-trace`) set with `context.setGlobalContextManager`; a `W3CTraceContextPropagator` (`@opentelemetry/core`) alone, with no composite and no `W3CBaggagePropagator`, set with `propagation.setGlobalPropagator`; and, through `registerInstrumentations`, only the `FetchInstrumentation` from `@opentelemetry/browser-instrumentation/experimental/fetch`, with `measureRequestSize` off, so there is no navigation, navigation-timing, resource-timing (page URLs hold the transaction ID), user-action (selectors and element text), console or errors instrumentation (OpenTelemetry JS, *Browser* and *Propagation*; the package README)
   **And** a source-scan test fails when any file under `frontend/src/` imports another `@opentelemetry/browser-instrumentation` subpath
   **And** a fetch to `/api/v1/bff/session` carries a `traceparent` matching `^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$` (W3C Trace Context, section 3.2), added by the instrumentation, never by hand, and no `baggage` header; a fetch to `https://other.example/x` carries neither, because the instrumentation injects only for the page's origin while `propagateTraceHeaderCorsUrls` stays unset (W3C Trace Context, section 8)
   **And** the exporter's own POST to `/otlp/v1/traces` records no span and carries no `traceparent`: the OTLP browser transport sends through the original `fetch` under `suppressTracing` (`@opentelemetry/otlp-exporter-base` 0.300.0), so no `ignoreUrls` entry is needed; a test proves it

2. **Context crosses the `await` by explicit propagation** (R36; NFR-OBS-1)
   **Given** `StackContextManager` keeps the active context only for synchronous code, and the ES2023 target leaves `async`/`await` native, so no context manager follows the `await` between the antiforgery fetch and the request (OpenTelemetry JS, *Context*: "Active context" and manual context; no `zone.js`)
   **When** `customFetch` in `frontend/src/api/mutator.ts` is called
   **Then** it reads `context.active()` once, before its first `await`, and makes each of its `fetch` calls (the antiforgery GET and the request) inside `context.with(thatContext, ...)`, so both are children of whatever span was active at the call; with no active span nothing changes
   **And** `customFetch`'s headers are otherwise unchanged; a `mutator.test.ts` case starts a span, calls `customFetch` for a POST inside `context.with`, and asserts that the antiforgery GET's and the POST's `traceparent` both carry that span's trace ID

3. **One submit span per attempt, parent of every POST** (R36; NFR-OBS-1, SM-2)
   **Given** Submit dispute or Try again on 4b
   **When** the attempt runs: the antiforgery fetch, the POST and every `idempotency-in-progress` resend (story 3.6)
   **Then** one `dispute.raise.submit` span starts when the attempt starts and ends when the mutation settles; the raise mutation passes its own `mutationFn` to the generated hook, which calls the generated `postApiV1Disputes` inside `context.with(trace.setSpan(context.active(), submitSpan), ...)`, so each resend runs in the same span (TanStack Query, `useMutation`: `mutationFn` runs once per attempt, retries included)
   **And** with responses 409 `idempotency-in-progress`, 409 `idempotency-in-progress`, 201, one test asserts one submit span, three POST fetch spans each a child of it, and every POST's `traceparent` holding the submit span's trace ID; a second Try again makes a second submit span
   **And** the submit span ends with `http.response.status_code` set to the last response's status when there was one (none on a network error) and `dispute.raise.outcome` set to exactly one of: `submitted`, `already-disputed`, `not-disputable`, `not-found`, `validation`, `idempotency-in-progress` (resends exhausted), `idempotency-mismatch`, `core-banking-unavailable`, `unexpected` (any other response, a failed antiforgery fetch included) or `network-error`; a test drives each value, and no other value can be set (OpenTelemetry semantic conventions, HTTP spans: `http.response.status_code`, and low-cardinality attribute values as for `error.type`)

4. **One open span per admitted entry, linked from each submit** (R36; NFR-OBS-3, SM-1, SM-2)
   **Given** the raise layout's guard (story 3.6), which admits only a cached, disputable row that is not yet submitted
   **When** a customer enters 4a
   **Then** one `dispute.raise.open` span is recorded and ended at once, from the guard's admitted branch, once per mount of that branch: an effect with a once-only ref, so React StrictMode's development re-run records nothing more (React, *StrictMode* and *useEffect*); a route `onEnter` is not used, because it runs before the guard redirects
   **And** tests rendered inside `<StrictMode>`, as `main.tsx` renders, record: one open for an admitted row; none for a deep link or reload (no cached row), a not-disputable row, or Back after a submit; still one after 4a → 4b → 4a; and a second after leaving to `/transactions` and entering again
   **And** each `dispute.raise.submit` span has one span link to the open span of its flow, the open span's context being kept by the raise layout for as long as the flow is mounted (OpenTelemetry specification, Trace API, *Link*); the open span has no attributes
   **And** SM-2 is read as the end of the POST fetch span with `http.response.status_code` 201 (a child of the submit span) minus the start of the linked open span; SM-1 counts distinct open spans linked from a submit span whose outcome is `submitted`, divided by all open spans, so Try again and a replayed 201 count once (PRD SM-1, SM-2)

5. **No personal data in any span** (R36; NFR-OBS-1, AD-20)
   **Given** distinctive seeded values: `transactionId` `TXN-SEED-7Q4Z`, `accountRef` `ACC-SEED-9K2M`, description `SEEDSHOP 4417`, note `note-seed-5V8X`, expected amount `123.45`, transaction amount `987.65`, the draft's `Idempotency-Key`, a dispute ID GUID, a refusal body with `traceId` `trace-seed-3J6P`, and a transactions `cursor` `cursor-seed-8L1N`
   **When** the app signs in, lists transactions with that cursor in the query, runs the raise flow to a 201 and to a 409 `already-disputed`, and fetches `/api/v1/disputes/<that GUID>`, all into an `InMemorySpanExporter`
   **Then** no exported span name, attribute value, event, link attribute or resource attribute contains any seeded value
   **And** `url.full` is set only through the fetch instrumentation's `sanitizeUrl` option, which 0.9.0 applies when the span is created (not `applyCustomAttributesOnSpan`, which runs after the response and swallows its errors): origin and path only, with the search and hash removed and every path segment that is a GUID replaced by `{id}`; `http.url` is absent; span names are the HTTP method only, as the instrumentation sets them
   **And** an allow-list test asserts the exact attribute keys, so a package upgrade that adds one fails it: fetch spans only `http.request.method`, `http.request.method_original`, `url.full`, `server.address`, `server.port`, `http.response.status_code`, `error.type`; raise submit spans only `http.response.status_code` and `dispute.raise.outcome`; open spans none; links none; the resource only `service.name` and `telemetry.sdk.*`; no `session.id`, `user.id` or `enduser.id` anywhere (OWASP ASVS 5.0 V16.2)

6. **Export to the same origin as OTLP/HTTP JSON** (R36; NFR-OBS-1, AD-29)
   **Given** a `BatchSpanProcessor` (`@opentelemetry/sdk-trace`) with the OTLP/HTTP JSON trace exporter (`@opentelemetry/exporter-trace-otlp-http`), its URL built at start as `new URL('/otlp/v1/traces', window.location.origin).href` (the transport parses the URL with no base, so a relative URL throws), never from a `VITE_*` variable or a fixed host, and with no exporter `headers`
   **When** spans are flushed (`forceFlush` in the test)
   **Then** one POST goes to `/otlp/v1/traces` on the same origin with `content-type` `application/json`, recorded by MSW, so the CSP keeps `connect-src 'self'`; JSON is the encoding because the pinned Aspire Dashboard accepted an OTLP/HTTP JSON trace through the gateway (story 2.5's smoke check, `docs/verification/2.5-review.md`; OTLP specification, *OTLP/HTTP*)
   **And** `frontend/vite.config.ts` proxies `/otlp` to the gateway (`GATEWAY_ORIGIN`) in `server.proxy` and `preview.proxy`, as it does `/api`, never straight to the Dashboard, so the gateway still strips `Cookie` and `Authorization` from the same-origin export (Vite, *server.proxy*, *preview.proxy*; story 2.5)

7. **No CSP violation under the gateway's policy** (R36; NFR-OBS-1, AD-29)
   **Given** `e2e/tests/dispute-raise.spec.ts` with the gateway's CSP (`withGatewayCsp`, as `spa-transactions.spec.ts` uses it)
   **When** the raise flow runs to a 201
   **Then** the page records no `securitypolicyviolation` event (a listener added before navigation) and no console error, and the browser's POST to `/otlp/v1/traces` gets 200; the flow flushes at least one export during the journey (MDN, *securitypolicyviolation event*; Playwright, `page.addInitScript`)
   **And** the builder greps the built `frontend/dist/assets/` for `eval(` and `new Function` and finds neither, because the app's source scans do not read `node_modules` (OWASP ASVS 5.0 V3.4.3)

## Boundaries

- Must not change: the raise screens' behaviour and copy (story 3.6), `customFetch`'s headers apart from `traceparent` added by the instrumentation, and the gateway's `/otlp/` proxy and CSP (story 2.5).
- Not here: browser metrics or logs, the Api and Worker spans (stories 3.1 and 4.1), CloudWatch export (epic 7), spans on the disputes screens (stories 3.7 and 3.12).

## Supervisor checks

Docker checks and deliberate-break proofs, run by the supervisor, not the verifier.

- On the running stack, raise a dispute through the gateway: the Aspire Dashboard shows one trace from `dispute.raise.submit` to the Api raise span.
- In a throwaway copy, add a span attribute holding the `transactionId`: criterion 5's test fails.
- In a throwaway copy, drop the `context.with` around the request `fetch` in `customFetch`: criteria 2 and 3 fail.
- In a throwaway copy, record the open span in the raise route's `onEnter`: criterion 4's deep-link case fails.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Mechanisms and sources: `TracerProvider`, `BatchSpanProcessor`, `StackContextManager` and `InMemorySpanExporter` from `@opentelemetry/sdk-trace` (its README); `W3CTraceContextPropagator` from `@opentelemetry/core`; `context.with`, `trace.setSpan`, `propagation.setGlobalPropagator` from `@opentelemetry/api` (<https://opentelemetry.io/docs/languages/js/context/>, <https://opentelemetry.io/docs/languages/js/propagation/>); `FetchInstrumentation` with `sanitizeUrl` (<https://www.npmjs.com/package/@opentelemetry/browser-instrumentation>, *Fetch*); `registerInstrumentations` (`@opentelemetry/instrumentation`); OTLP/HTTP JSON (<https://opentelemetry.io/docs/specs/otlp/#otlphttp>); span links (<https://opentelemetry.io/docs/specs/otel/trace/api/#link>); W3C Trace Context (<https://www.w3.org/TR/trace-context/>); HTTP span conventions (<https://opentelemetry.io/docs/specs/semconv/http/http-spans/>); `service.name` (<https://opentelemetry.io/docs/specs/semconv/resource/#service>); React StrictMode (<https://react.dev/reference/react/StrictMode>).
- Packages (human decision 2026-10-30; each still verified per AGENTS.md, Dependency verification, and pinned exactly): `@opentelemetry/browser-instrumentation` 0.9.0, `@opentelemetry/api` 1.10.0, `@opentelemetry/sdk-trace` 3.0.0, `@opentelemetry/core` 3.0.0, `@opentelemetry/exporter-trace-otlp-http` 0.300.0 (it depends on `@opentelemetry/sdk-trace` 3.0.0 exactly), `@opentelemetry/resources` 3.0.0 (for `resourceFromAttributes`; already a dependency of `sdk-trace` 3.0.0) and `@opentelemetry/instrumentation` 0.223.0 (for `registerInstrumentations`; the version `browser-instrumentation` 0.9.0 depends on, `^0.223.0`, rather than the latest 0.300.0, so one copy is installed). Every direct dependency is pinned exactly, with no `^` or `~`; `@opentelemetry/api` is 1.10.0 because `sdk-trace` and `core` 3.0.0 require `>=1.10.0 <1.11.0`. No `zone.js` and no `@opentelemetry/context-zone`. The 0.223 instrumentation line brings `import-in-the-middle`, `require-in-the-middle` and `web-vitals` into the lockfile; the builder greps `frontend/dist/` to confirm the Node hook packages stay out of the bundle and records it. `npm audit` is 0 at every level on `b009f12`; the report lists any new finding.
- The import of `@opentelemetry/browser-instrumentation/experimental/fetch` carries `// shortcut: experimental package pinned at 0.9.0, a minor release may break its API; re-check the fetch options and span attributes on every upgrade, and move to a stable export when one ships`.
- Touches:
  - `frontend/src/telemetry/` (new: the start function, the raise span helpers, `sanitizeUrl`, their tests, the import source scan)
  - `frontend/src/main.tsx` (start tracing before render)
  - `frontend/src/api/mutator.ts` and `frontend/src/api/mutator.test.ts` (criterion 2)
  - `frontend/src/features/disputes/raise/RaiseLayout.tsx` (the open span and its kept context)
  - `frontend/src/features/disputes/raise/ReviewScreen.tsx` and `ReviewScreen.test.tsx` (the submit span and the raise `mutationFn`)
  - `frontend/src/test/support.tsx` (a `<StrictMode>` render option for criterion 4; an in-memory exporter helper) and `frontend/src/test/setup.ts` if the exporter is reset per test
  - `frontend/vite.config.ts` (the `/otlp` dev and preview proxy)
  - `frontend/package.json`, `frontend/package-lock.json`
  - `e2e/tests/dispute-raise.spec.ts` (criterion 7, QA stage)
- Consumed, not edited: `frontend/src/api/generated/`, `frontend/src/app/router.tsx`.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-20, AD-29
- prd — docs/prd/prd.md, SM-1, SM-2, NFR-OBS-1, NFR-OBS-3
- standard — <https://opentelemetry.io/docs/languages/js/getting-started/browser/>
- standard — <https://www.w3.org/TR/trace-context/>
- verification — docs/verification/3.11-challenge.md, docs/verification/3.11-prebuild-review.md, docs/verification/3.11-prebuild-security.md
- verification — docs/verification/2.5-review.md (OTLP/HTTP JSON accepted through the gateway)

## Notes

- Risk medium: a span attribute holding a transaction ID or note leaks personal data into the telemetry store (AD-20); beyond the criteria, the supervisor's break above proves the test.
- Decision: 2026-10-30 (Sofia, pre-build review) — split from 3.6 so each story stays within about 8 criteria; it waits only on 3.6.
- Decision: 2026-10-30 (human) — the experimental successor `@opentelemetry/browser-instrumentation` 0.9.0 (`./experimental/fetch`), pinned exactly and marked as a shortcut, with `@opentelemetry/api` 1.10.0, `@opentelemetry/sdk-trace` 3.0.0 (`TracerProvider`, `BatchSpanProcessor`, `StackContextManager`), `@opentelemetry/core` 3.0.0 and the OTLP exporter; no `zone.js`.
- Decision: 2026-10-30 (Sofia, refine) — the exporter is OTLP/HTTP JSON, from story 2.5's smoke evidence; the builder check on the Aspire receiver is closed.
- Decision: 2026-10-30 (Sofia, refine) — the open span is recorded where the guard admits the row, not in the route's `onEnter` (challenger and pre-build review); `router.tsx` is no longer touched, so story 3.7 no longer waits on this story for that file.
- Carried to story 7.6 (pre-build security T7, T8; 2026-10-30): trust in an inbound `traceparent` (sampler choice) and `/otlp/` as an anonymous write path (collector-side filtering by `service.name`). Not in this story.
- Assumption: `@opentelemetry/resources` 3.0.0 and `@opentelemetry/instrumentation` 0.223.0 are direct dependencies beside the human-decided list, because the documented set-up imports `resourceFromAttributes` and `registerInstrumentations` from them; both are already in the installed tree through the decided packages.
