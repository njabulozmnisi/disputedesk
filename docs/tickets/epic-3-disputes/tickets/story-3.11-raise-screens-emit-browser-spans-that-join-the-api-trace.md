---
id: 11
type: story
title: "Raise screens emit browser spans that join the API trace"
parent: epic-3-disputes
covers: [R36]
after: [6]
hitl: false
risk: medium
---

# Raise screens emit browser spans that join the API trace

## Description

Adds the SPA's OpenTelemetry web SDK: same-origin `/api` fetches carry W3C `traceparent`, and the raise flow emits `dispute.raise.open` and `dispute.raise.submit`, so SM-1 (submitted ÷ opened) and SM-2 (open to 201) can be read from the traces (AD-20). Spans export over OTLP/HTTP to the same origin's `/otlp/`, which the gateway already proxies (story 2.5). Split from story 3.6 (decided 2026-10-30); it builds on 3.6's raise routes and submit.

## Requirements

Verify: Vitest tests pass with an in-memory span exporter and MSW; `npm run typecheck`, `npm run lint` and `npm test` pass in `frontend/`.

1. **`traceparent` on same-origin `/api` fetches only** (R36; NFR-OBS-1)
   **Given** the web SDK started once in `main.tsx`, before the app renders
   **When** the SPA calls `/api`
   **Then** each request carries a valid W3C `traceparent`, added by the OpenTelemetry fetch instrumentation, never by hand; no request to another origin carries one (`propagateTraceHeaderCorsUrls` unset); the only instrumentation registered is fetch, so there is no document-load or user-interaction instrumentation (the page URL holds the transaction ID)

2. **The submit span parents the POST** (R36; NFR-OBS-1)
   **Given** Submit dispute or Try again on 4b
   **When** the attempt runs, through the antiforgery fetch, the POST and any `idempotency-in-progress` resends (story 3.6)
   **Then** one `dispute.raise.submit` span covers the attempt until the mutation settles, and the POST's `traceparent` has that span's trace ID, on the first attempt and on every resend
   **And** the context survives the `await` before the POST through the context mechanism the OpenTelemetry JS browser docs give (for example `ZoneContextManager` from `@opentelemetry/context-zone`); the builder names the one chosen and its source

3. **One open per entry into the raise flow** (R36; NFR-OBS-3, SM-1, SM-2)
   **Given** a customer entering 4a from outside the raise flow
   **When** the route is entered
   **Then** one `dispute.raise.open` span is recorded and ended at once, from the raise flow's parent route `onEnter` (TanStack Router), not a `useEffect`, so React StrictMode's double effects and moving between 4a and 4b record nothing more; each `dispute.raise.submit` span has a span link to the open span of its flow (OpenTelemetry *Links*), so SM-2 is the submit span's end minus the linked open span's start, for the submit that got 201
   **And** `dispute.raise.submit` ends with `http.response.status_code` when there was a response and `dispute.raise.outcome`: `submitted`, the problem slug, or `network-error`

4. **No personal data in any span** (R36; NFR-OBS-1, AD-20)
   **Given** a seeded `transactionId`, `accountRef`, note, expected amount, transaction amount, `Idempotency-Key` and a problem body
   **When** the raise flow runs end to end, a 201 and a refusal included, into an in-memory exporter
   **Then** no attribute, span name, event or link attribute of any exported span contains any of those values; URL attributes (`url.full`, `http.url`) are recorded without their query string (the transactions `cursor` comes from core banking), through the fetch instrumentation's documented attribute hook; the raise spans carry only the attributes named in criterion 3

5. **Export to the same origin** (R36; NFR-OBS-1, AD-29)
   **Given** the SDK's batch span processor and the OTLP/HTTP trace exporter
   **When** spans are exported
   **Then** they go to `/otlp/v1/traces` on the same origin, so the CSP keeps `connect-src 'self'`; `vite.config.ts` proxies `/otlp` to the gateway in dev and preview as it does `/api`; the raise flow raises no `securitypolicyviolation` event under the gateway's CSP (`script-src 'self'; style-src 'self'`; no `eval` or `new Function` from the SDK or zone code)
   **And** the builder verifies whether the Aspire Dashboard version in `docker-compose.yml` accepts OTLP/HTTP JSON and picks the JSON exporter (`@opentelemetry/exporter-trace-otlp-http`) or the protobuf one (`@opentelemetry/exporter-trace-otlp-proto`) to match, recording the evidence

## Boundaries

- Must not change: the raise screens' behaviour and copy (story 3.6), `customFetch`'s headers apart from `traceparent` added by the instrumentation, and the gateway's `/otlp/` proxy and CSP (story 2.5).
- Not here: browser metrics or logs, the Api and Worker spans (stories 3.1 and 4.1), CloudWatch export (epic 7).

## Supervisor checks

Docker checks and deliberate-break proofs, run by the supervisor, not the verifier.

- On the running stack, raise a dispute through the gateway: the Aspire Dashboard shows one trace from `dispute.raise.submit` to the Api raise span.
- In a throwaway copy, add a span attribute holding the `transactionId`: criterion 4's test fails.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Mechanisms and sources: OpenTelemetry JS web SDK (`WebTracerProvider`, `BatchSpanProcessor`), `@opentelemetry/instrumentation-fetch`, the OTLP/HTTP trace exporter, a browser context manager from the OpenTelemetry JS docs, span links (OpenTelemetry specification, *Links between spans*); W3C Trace Context; OpenTelemetry HTTP semantic conventions for `url.full` and `http.response.status_code`.
- Packages: the OpenTelemetry web packages above, each verified (publisher, downloads, repository, latest stable, versions compatible with each other) and pinned exactly (AGENTS.md, Dependency verification); `npm audit` shows no new high finding.
- Touches:
  - `frontend/src/telemetry/` (new: the SDK set-up, the raise span helpers, their tests)
  - `frontend/src/main.tsx` (start the SDK)
  - `frontend/src/app/router.tsx` (the raise flow's `onEnter`)
  - `frontend/src/features/disputes/raise/` (the submit span around each attempt)
  - `vite.config.ts` (the `/otlp` dev and preview proxy)
  - `frontend/package.json`, `frontend/package-lock.json`
- Consumed, not edited: `frontend/src/api/generated/`, `frontend/src/api/mutator.ts`.

## References

- parent — docs/tickets/epic-3-disputes/epic-3-disputes.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-20, AD-29
- prd — docs/prd/prd.md, SM-1, SM-2
- standard — <https://opentelemetry.io/docs/languages/js/getting-started/browser/>
- standard — <https://www.w3.org/TR/trace-context/>
- verification — docs/verification/3.6-challenge.md, docs/verification/3.6-prebuild-review.md, docs/verification/3.6-prebuild-security.md

## Notes

- Risk medium: a span attribute holding a transaction ID or note leaks personal data into the telemetry store (AD-20); beyond the criteria, the supervisor's break above proves the test.
- Decision: 2026-10-30 (Sofia, pre-build review) — split from 3.6 so each story stays within about 8 criteria; it waits only on 3.6.
- Builder check (unverified, 2026-10-30): whether the Aspire Dashboard's OTLP/HTTP receiver accepts JSON as well as protobuf; the builder checks the pinned version's docs or source and records it under criterion 5. Not an open question: either answer has a named exporter.
