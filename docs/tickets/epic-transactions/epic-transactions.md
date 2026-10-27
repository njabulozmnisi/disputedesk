---
id: 2
type: epic
title: "Linked customers sign in and see their transactions"
parent: initiative-disputedesk
covers: [R1, R2, R3, R4, R5, R9, R11, R12, R21, R24, R25, R26, R27, R29, R30, R31, R32, R34, R36, R37, R40]
sources: []
companions: []
after: []
risk: high
---

# Linked customers sign in and see their transactions

## Description

A seeded linked customer signs in through the BFF on the themed hosted login and lists their own transactions from the core-banking stub in the SPA, served by the gateway, with disputability shown on each row and a retry when core banking is down.

## Outcome

Linked customers see what they can dispute without phoning the bank; signal: the tracer demo runs on a fresh `docker compose up`.

## Requirements

R1 (parent R1): A person creates a login and verifies their email on the identity provider's hosted pages, themed with DisputeDesk's design tokens; the emails land in Mailpit and an unverified login cannot sign in.
R2 (parent R2): A verified login signs in through the BFF (code flow with PKCE, Api host as confidential client); the browser holds only an HttpOnly, SameSite=Strict session cookie, Secure with the `__Host-` prefix in AWS (local `http://localhost` is exempt and uses `SameAsRequest` in Development), no token is ever browser-visible, unsafe methods need the antiforgery header, and the session ends after 15 minutes idle or 12 hours.
R3 (parent R3): Signing out ends both the DisputeDesk and the identity provider session; a replayed old cookie gets 401.
R4 (parent R4): An unlinked login starts linking with an ID number and account number and gets the same status and body whether the details match, match nothing, or match a customer with a linked login; the OTP goes off the request path, only on a match with no linked login, to the cellphone on the bank's record; malformed input gets 400 naming the field without echoing it; starting again invalidates the earlier challenge. This epic delivers: the matching port and the SMS sender to Mailpit (story 1).
R5 (parent R5): The SIM-swap check runs before any OTP is sent and fails closed on failure, error or timeout, with an unchanged response and an audit entry; codes entered afterwards behave as wrong codes, ending in "We can't link you online. Please contact the bank." (seeded customer C). This epic delivers: the SIM-swap port and customer C failing it (story 1).
R9 (parent R9): Every endpoint enforces one authorisation state: anonymous gets 401 on customer and linking endpoints, an unlinked login gets 403 "linking required" on customer endpoints, a linked customer gets 403 "already linked" on linking endpoints; the customer is resolved on every request from the link table by login, never from the request or the token subject. This epic delivers: the customer-endpoint states (anonymous 401, unlinked 403 `linking-required`, linked) and customer resolution from the link table.
R11 (parent R11): A linked customer lists their own transactions newest first with keyset pagination (default 20, maximum 50, bad cursor 400), each row showing the fields in FR-11; when the core-banking source is still down after the resilience policies, the list returns 503 and the SPA offers a retry while dispute history stays available.
R12 (parent R12): Each transaction row shows either that it is disputable or its dispute's customer-facing status; pending transactions, credits, transfers between own accounts, transactions dated more than 90 South African calendar days ago (boundary at the end of D + 90), and any transaction ever disputed are not disputable. This epic delivers: every rule except "ever disputed", which epic-disputes adds.
R21 (parent R21): Operations endpoints accept only client-credentials bearer JWTs validated for issuer, audience, lifetime and signature, and customer and linking endpoints accept only the cookie, so the wrong credential gets 401; a token without the role, or a credential not mapping to exactly one actor type, gets 403; `dispute.read` returns one dispute with no personal details beyond the snapshot; each action's audit entry carries the fields listed in FR-25. This epic delivers: the camelCase role keys in provisioning (story 2).
R24 (parent R24): A fresh `docker compose up` seeds customers A to D with about 90 days of start-up-relative transactions as FR-29 describes: A linked with a dispute in every status and outcome (one passing the SLA minutes after start-up), B linked and owning the dispute A gets 404 for, C unlinked and failing the SIM-swap check, D unlinked and able to complete linking; seeded disputes publish no events. This epic delivers: transactions for customers A to D, links for A and B.
R25 (parent R25): An architecture test proves domain and application code reference no infrastructure; external systems are reached only through `ICoreBankingClient`, `ISimSwapCheck` and `ISmsSender`, and the only single-implementation interfaces are those three and `IAppDbContext` (the persistence seam the architecture tests enforce); the built-in .NET feature is used wherever one exists, with no MediatR, AutoMapper, Swashbuckle or Duende. This epic delivers: the three ports and the stub isolation rule.
R26 (parent R26): The runtime units are exactly those in NFR-ARC-2, with one Api and one Worker; sessions, idempotency keys and the audit log live in PostgreSQL so the Api could scale out without session loss (rate-limit state is the stated exception); a second Worker would be harmless. This epic delivers: the core-banking stub as the one added runtime unit.
R27 (parent R27): The Api host exposes `/health/live` and `/health/ready` and the Worker's relay and sweep touch heartbeat files, which Compose health checks use; both shut down on SIGTERM within 30 seconds after finishing in-flight work. This epic delivers: readiness checking Zitadel discovery (story 2).
R29 (parent R29): PostgreSQL holds every record NFR-DATA-1 lists and transactions are not copied apart from the snapshot; the allowed actions, disputability and amount rules live in the domain model; money is `numeric(19,2)` with ISO code ZAR and every timestamp is stored in UTC; each dispute carries a version and uniqueness is enforced by the indexes in NFR-DATA-5; schema changes are EF Core migrations applied on start-up; disputes and their audit entries are never deleted in v1. This epic delivers: migrations applied on start-up and the first tables (`Sessions`, `DataProtectionKeys`, `CustomerLinks`).
R30 (parent R30): Every route is under `/api/v1`; the built-in OpenAPI document is committed as `openapi.json` and checked for drift in CI; every error is RFC 9457 ProblemDetails with a stable `type` and the `traceId`; lists use opaque keyset cursors; money travels as decimal strings and enum values as camelCase (C# members PascalCase, stored in Postgres as the member names).
R31 (parent R31): ID numbers are never stored or logged, card numbers are kept and shown only as first 6 and last 4 digits, account numbers only as last 4; logs are structured, carry the trace ID, and logs, traces, metrics and events carry no personal data. This epic delivers: masked card and account fields and no customer identifier in telemetry.
R32 (parent R32): The nginx gateway sets the security headers and the coarse per-IP limit in NFR-SEC-7; secrets come from the environment, never the repo; in AWS, Mailpit's web UI sits behind basic auth. This epic delivers: the gateway headers and coarse limit.
R34 (parent R34): CI enforces nullable references, warnings as errors and `dotnet format` in .NET, and strict TypeScript with ESLint and Prettier; no suppression is added without a written reason; glossary terms are used in code, API and copy; the frontend is React and TypeScript with shadcn/ui on Tailwind and Orval-generated TanStack Query hooks never edited by hand, with design tokens in one theme file; every new package is verified and pinned exactly. This epic delivers: the frontend gates and the CI frontend job.
R36 (parent R36): The SPA, Api host and Worker emit OpenTelemetry traces, metrics and logs, and one trace runs browser to API to outbox to Kafka to Worker with trace context injected by hand; the business metrics in NFR-OBS-3 are emitted; the Aspire Dashboard (locally) and CloudWatch (AWS) show them with no manual setup. This epic delivers: the same-origin `/otlp/` proxy.
R37 (parent R37): `docker compose up` starts the whole stack with zero clicks, including Zitadel configuration, seed data and migrations, from multi-stage images running as non-root. This epic delivers: the core-banking service and the gateway image with the SPA.
R40 (parent R40): The customer portal meets WCAG 2.2 AA (contrast, keyboard access, visible focus, labelled errors, reflow at 320px), proved by an automated accessibility scan in the E2E tests. This epic delivers: the shell and transactions screens.

## Done when

- Seeded customer A signs in on the themed hosted login and sees transactions newest first in the SPA served by the gateway.
- Anonymous, unlinked and linked callers get 401, 403 `linking-required` and 200 on `GET /api/v1/transactions`.
- Stopping the core-banking service gives 503 `core-banking-unavailable` and a retry in the SPA.
- CI runs the frontend job; every story passed verifier, QA and reviewer (integrated verification).

## Boundaries

- Linking screens and endpoints belong to epic-linking; disputes to epic-disputes.
- Touch point: Zitadel provisioning (`infra/zitadel/provision/`) is configured here and in epic-operations, never called at runtime.

## References

- parent — docs/initiative-disputedesk.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-3, AD-4, AD-5, AD-6, AD-7, AD-8, AD-10, AD-21, AD-22, AD-23, AD-24, AD-26, AD-27, AD-29, AD-30, AD-31, AD-32
- experience — docs/ux/EXPERIENCE.md
- design — docs/ux/DESIGN.md
- prd — docs/prd/prd.md
- verification — docs/verification/

## Notes

- Decision: 2026-10-26 — epic Requirements are the parent's lines only, ids kept (`R<n> (parent R<n>)`); each line names the part this epic delivers where the parent line is shared.
- Decision: 2026-10-26 — the tracer bullet is the chain of stories 1 to 5 (stub, BFF, transactions endpoint, SPA screen, gateway): a seeded linked customer signs in and lists transactions from the stub in the SPA. One story through every layer would exceed one agent session and cross the backend and frontend builder areas; story 2 (BFF and Zitadel) is the least certain and runs first in lane A.
- Waits on 1.2 because: 1 needs what those stories build (see its References and Description).
- Waits on 1.2, 1.5 because: 2 needs what those stories build (see its References and Description).
- Waits on: epic 1 (the merged P0 baseline).
- Decision: 2026-10-26 — Gate 2: code in every builder-owned story of this epic is stupid simple and logical, per AGENTS.md (smallest change that meets the criteria; every abstraction earns its place).
