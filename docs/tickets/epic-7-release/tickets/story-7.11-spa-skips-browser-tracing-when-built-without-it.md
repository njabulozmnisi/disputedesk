---
id: 11
type: story
title: "SPA skips browser tracing when built without it"
parent: epic-7-release
covers: [R36]
after: [3.11]
hitl: false
risk: low
refined: false
---

# SPA skips browser tracing when built without it

## Description

Adds one build-time switch so the SPA starts no browser tracing in AWS, where the gateway takes in no browser telemetry (decided 2026-10-30, 7.4 challenge). Built with `VITE_BROWSER_TELEMETRY=off`, the SPA never calls `startTracing`, so it registers no tracer provider, posts nothing to `/otlp/`, and sends no `traceparent`; built without it, or with any other value, it behaves exactly as story 3.11 built it. Story 7.4 sets the value through the gateway Dockerfile for the image published from main. Split from 7.4 because `frontend/` is the frontend builder's.

## Requirements

Verify: Vitest tests pass, with `vi.stubEnv` setting the variable; `npm run typecheck`, `npm run lint` and `npm test` pass in `frontend/` with no new suppression.

Inputs:
- `VITE_BROWSER_TELEMETRY`: read once at start from `import.meta.env`; exactly `off` turns tracing off; unset, empty or any other value leaves it on. Vite replaces it at build time (Vite, *Env Variables and Modes*); it is declared in `ImportMetaEnv` so TypeScript checks the name (Vite, *IntelliSense for TypeScript*).

1. **Off: no tracing, no export, no trace header** (R36; NFR-OBS-1, AD-29)
   **Given** the app started with `VITE_BROWSER_TELEMETRY` set to `off`
   **When** a screen sends a same-origin `/api` request
   **Then** `startTracing` is not called, the global tracer provider stays the no-op one, MSW records no request under `/otlp/`, and the `/api` request carries no `traceparent`

2. **On by default: unchanged** (R36; NFR-OBS-1, AD-20)
   **Given** the variable unset, empty, or `on`
   **When** the app starts
   **Then** `startTracing` runs once as before, and story 3.11's span tests still pass unchanged

## Boundaries

- Must not change: `startTracing`'s exporter, instrumentation, sanitising and resource (stories 3.11, 2.5); the CSP; the local build's behaviour.
- No runtime configuration endpoint, no meta tag, no new package.

## Supervisor checks

- In a throwaway copy, compare against `'false'` instead of `'off'`: criterion 1's test fails.

## Build facts

- Owner: frontend (frontend-builder).
- Flags: none.
- Lane: B.
- Mechanisms and sources: Vite env variables and their TypeScript declaration (<https://vite.dev/guide/env-and-mode>); Vitest `vi.stubEnv` (<https://vitest.dev/api/vi.html#vi-stubenv>).
- Packages: none.
- Touches: `frontend/src/main.tsx` (the guarded `startTracing()` call, or a one-line helper beside it if the test needs one); `frontend/src/vite-env.d.ts` (new: `ImportMetaEnv.VITE_BROWSER_TELEMETRY`; `tsconfig.json` already loads `vite/client` types); `frontend/src/telemetry/` (the test).

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- story — docs/tickets/epic-7-release/tickets/story-7.4-aws-compose-override-and-opentelemetry-collector.md, criterion 6
- architecture — docs/architecture/architecture.md, AD-20, AD-29
- verification — docs/verification/7.4-challenge.md

## Notes

- Risk low (proposed): the worst wrong build loses browser spans locally (criterion 2 guards it) or sends failing posts in AWS (the AWS gateway answers 404).
- Decision: 2026-10-30 (Sofia, refine) — split from 7.4: the backend builder cannot write `frontend/`. It can build at any time; 7.4 waits on it.
