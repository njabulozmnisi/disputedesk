---
id: 1
type: epic
title: "The platform baseline runs, is tested and is scanned"
parent: initiative-disputedesk
covers: [R25, R27, R30, R33, R34, R36, R37]
sources: []
companions: []
after: []
status: done
risk: medium
---

# The platform baseline runs, is tested and is scanned

## Description

The solution skeleton, the zero-click Compose platform, the OpenTelemetry baseline and security headers, CI with security scanning, and the Microsoft Learn conventions with the AD-3 error kinds are merged on main.

## Outcome

Every later story builds on a green, scanned baseline; signal: CI green on main (initiative Done when).

## Requirements

R25 (parent R25): An architecture test proves domain and application code reference no infrastructure; external systems are reached only through `ICoreBankingClient`, `ISimSwapCheck` and `ISmsSender`, and the only single-implementation interfaces are those three and `IAppDbContext` (the persistence seam the architecture tests enforce); the built-in .NET feature is used wherever one exists, with no MediatR, AutoMapper, Swashbuckle or Duende. This epic delivers: the layer tests (NFR-ARC-1).
R27 (parent R27): The Api host exposes `/health/live` and `/health/ready` and the Worker's relay and sweep touch heartbeat files, which Compose health checks use; both shut down on SIGTERM within 30 seconds after finishing in-flight work. This epic delivers: the Api health endpoints.
R30 (parent R30): Every route is under `/api/v1`; the built-in OpenAPI document is committed as `openapi.json` and checked for drift in CI; every error is RFC 9457 ProblemDetails with a stable `type` and the `traceId`; lists use opaque keyset cursors; money travels as decimal strings and enum values as camelCase (C# members PascalCase, stored in Postgres as the member names). This epic delivers: the `openapi.json` drift check.
R33 (parent R33): GitHub Actions builds every pull request, runs every suite and the scans in NFR-SEC-8, failing on any untriaged high or critical finding, and pushes images to GHCR on main; no test is weakened to pass. This epic delivers: CI and the scans except the ZAP baseline.
R34 (parent R34): CI enforces nullable references, warnings as errors and `dotnet format` in .NET, and strict TypeScript with ESLint and Prettier; no suppression is added without a written reason; glossary terms are used in code, API and copy; the frontend is React and TypeScript with shadcn/ui on Tailwind and Orval-generated TanStack Query hooks never edited by hand, with design tokens in one theme file; every new package is verified and pinned exactly. This epic delivers: the .NET gates.
R36 (parent R36): The SPA, Api host and Worker emit OpenTelemetry traces, metrics and logs, and one trace runs browser to API to outbox to Kafka to Worker with trace context injected by hand; the business metrics in NFR-OBS-3 are emitted; the Aspire Dashboard (locally) and CloudWatch (AWS) show them with no manual setup. This epic delivers: the OTLP baseline and the Aspire Dashboard.
R37 (parent R37): `docker compose up` starts the whole stack with zero clicks, including Zitadel configuration, seed data and migrations, from multi-stage images running as non-root. This epic delivers: zero-click Compose with Zitadel provisioning.

## Done when

- P0-1 to P0-5 merged with verifier and reviewer records in docs/verification/.
- CI green on main.

## Boundaries

- Built on by every later epic, never rebuilt.
- Holds no feature endpoint, table or screen.

## References

- parent — docs/initiative-disputedesk.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-1, AD-2, AD-3, AD-27, AD-32
- experience — docs/ux/EXPERIENCE.md
- design — docs/ux/DESIGN.md
- prd — docs/prd/prd.md
- verification — docs/verification/

## Notes

- Decision: 2026-10-26 — epic Requirements are the parent's lines only, ids kept (`R<n> (parent R<n>)`); each line names the part this epic delivers where the parent line is shared.
- Waits on: none.
