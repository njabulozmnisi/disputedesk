# Build order

Every epic and story in the order it is built. A story's number is `<epic>.<story>`; it is also its file name prefix, its `docs/verification/` prefix and the name used in reports and commits. The order comes from the `tickets.toml` files (table order); ids are never renumbered, so a story a replan moved keeps its number (5.4 is built before 5.3, 3.5 moved to 5.4, 2.7 is built before 2.4, 3.8 is built before 3.3, 3.9 right after 3.3, 3.10 and 3.11 right after 3.6, 5.5 right after 5.1, 6.6 before 6.3, and 7.10 before 7.1).

Status: done (merged on main), in progress, or planned. The supervisor updates it when a story merges.

## Epic 1: [The platform baseline runs, is tested and is scanned](epic-1-platform-baseline/epic-1-platform-baseline.md)

| Story | Title | Owner | Status |
|---|---|---|---|
| 1.1 | [P0-1 backend solution skeleton, quality gates and health endpoints](epic-1-platform-baseline/tickets/story-1.1-backend-solution-skeleton-quality-gates-and-health-endp-plan.md) | backend | done |
| 1.2 | [P0-2 local Compose platform with zero-click Zitadel](epic-1-platform-baseline/tickets/story-1.2-local-compose-platform-with-zero-click-zitadel-plan.md) | backend | done |
| 1.3 | [P0-3 OpenTelemetry baseline and security headers](epic-1-platform-baseline/tickets/story-1.3-opentelemetry-baseline-and-security-headers-plan.md) | backend | done |
| 1.4 | [P0-4 CI, security scanning and floor guard](epic-1-platform-baseline/tickets/story-1.4-ci-security-scanning-and-floor-guard-plan.md) | backend | done |
| 1.5 | [P0-5 Microsoft Learn conventions, AD-3 error kinds and EF drift check](epic-1-platform-baseline/tickets/story-1.5-microsoft-learn-conventions-ad-3-error-kinds-and-ef-dri-plan.md) | backend | done |

## Epic 2: [Linked customers sign in and see their transactions](epic-2-transactions/epic-2-transactions.md)

Needs: epic 1 (the merged P0 baseline).

| Story | Title | Owner | Status |
|---|---|---|---|
| 2.1 | [Core-banking stub and the three ports with the resilience handler](epic-2-transactions/tickets/story-2.1-core-banking-stub-and-the-three-ports-with-the-resilience-ha.md) | backend | done |
| 2.2 | [Sign in and sign out through the BFF, with one ProblemDetails contract](epic-2-transactions/tickets/story-2.2-sign-in-and-sign-out-through-the-bff-with-one-problemdetails.md) | backend | done |
| 2.3 | [Linked customer lists their own transactions](epic-2-transactions/tickets/story-2.3-linked-customer-lists-their-own-transactions.md) | backend | done |
| 2.7 | [Problem types resolve to documentation pages](epic-2-transactions/tickets/story-2.7-problem-types-resolve-to-documentation-pages.md) | backend | done |
| 2.4 | [SPA shell, session routing and the transactions screen](epic-2-transactions/tickets/story-2.4-spa-shell-session-routing-and-the-transactions-screen.md) | frontend | done |
| 2.5 | [Gateway serves the SPA with the edge rules, and CI checks the frontend](epic-2-transactions/tickets/story-2.5-gateway-serves-the-spa-with-the-edge-rules-and-ci-checks-the.md) | backend | done |
| 2.8 | [Sign-in and sign-out are audited, and every endpoint is denied by default](epic-2-transactions/tickets/story-2.8-sign-in-and-sign-out-are-audited-and-every-endpoint-is-denied.md) | backend | done |
| 2.6 | [Hosted login pages themed from the design tokens](epic-2-transactions/tickets/story-2.6-hosted-login-pages-themed-from-the-design-tokens.md) | backend | planned |

## Epic 3: [Customers raise, follow and withdraw disputes](epic-3-disputes/epic-3-disputes.md)

Needs: epic 2 (CurrentCustomer, the Linked policy, the transactions handler and the SPA shell).

| Story | Title | Owner | Status |
|---|---|---|---|
| 3.1 | [Linked customer raises a dispute that commits with its stored event and outbox row](epic-3-disputes/tickets/story-3.1-linked-customer-raises-a-dispute-that-commits-with-its-store.md) | backend | done |
| 3.2 | [Raising is idempotent under an Idempotency-Key](epic-3-disputes/tickets/story-3.2-raising-is-idempotent-under-an-idempotency-key.md) | backend | done |
| 3.8 | [Dispute changes are recorded as stored events, and disputes load by replay](epic-3-disputes/tickets/story-3.8-dispute-changes-are-recorded-as-stored-events-and-disputes-lo.md) | backend | done |
| 3.3 | [Customer opens a dispute and follows its timeline](epic-3-disputes/tickets/story-3.3-customer-opens-a-dispute-and-follows-its-timeline.md) | backend | done |
| 3.9 | [Customer lists their disputes newest first, a page at a time](epic-3-disputes/tickets/story-3.9-customer-lists-their-disputes-newest-first-a-page-at-a-time.md) | backend | planned |
| 3.4 | [Customer withdraws an open dispute](epic-3-disputes/tickets/story-3.4-customer-withdraws-an-open-dispute.md) | backend | planned |
| 3.6 | [Raise-a-dispute screens with idempotent submit](epic-3-disputes/tickets/story-3.6-raise-a-dispute-screens-with-idempotent-submit.md) | frontend | planned |
| 3.10 | [Dispute status badges on transaction rows](epic-3-disputes/tickets/story-3.10-dispute-status-badges-on-transaction-rows.md) | frontend | planned |
| 3.11 | [Raise screens emit browser spans that join the API trace](epic-3-disputes/tickets/story-3.11-raise-screens-emit-browser-spans-that-join-the-api-trace.md) | frontend | planned |
| 3.7 | [Disputes history screen and the shell's skip link](epic-3-disputes/tickets/story-3.7-disputes-history-screen-and-the-shells-skip-link.md) | frontend | planned |
| 3.12 | [Dispute detail, timeline and withdraw screen](epic-3-disputes/tickets/story-3.12-dispute-detail-timeline-and-withdraw-screen.md) | frontend | planned |

## Epic 4: [Dispute events reach Kafka in per-dispute order](epic-4-events/epic-4-events.md)

Needs: epic 3 (outbox rows written by the one commit path (AD-14), with outbox ID = stored event ID).

| Story | Title | Owner | Status |
|---|---|---|---|
| 4.1 | [Outbox relay publishes dispute events to Kafka with a Worker heartbeat](epic-4-events/tickets/story-4.1-outbox-relay-publishes-dispute-events-to-kafka-with-a-worker.md) | backend | done |
| 4.2 | [Relay rides out broker outages, failed records, leader handover and shutdown](epic-4-events/tickets/story-4.2-relay-rides-out-broker-outages-failed-records-leader-handove.md) | backend | planned |

## Epic 5: [Operations tools and the SLA sweep act on disputes](epic-5-operations/epic-5-operations.md)

Needs: epic 2 (the gateway templates and provisioning from stories 2.5 and 2.6); epic 3 (the Dispute aggregate, loading by replay and the one commit path); epic 4 (the Worker heartbeat health check).

| Story | Title | Owner | Status |
|---|---|---|---|
| 5.1 | [Operations tools authenticate with bearer tokens and read a dispute](epic-5-operations/tickets/story-5.1-operations-tools-authenticate-with-bearer-tokens-and-read-a.md) | backend | planned |
| 5.5 | [Scalar drives the operations API with a pasted bearer token behind a switch](epic-5-operations/tickets/story-5.5-scalar-drives-the-operations-api-with-a-pasted-bearer-token.md) | backend | planned |
| 5.2 | [Operations actions start review, escalate and resolve under the allowed-actions table](epic-5-operations/tickets/story-5.2-operations-actions-start-review-escalate-and-resolve-under-t.md) | backend | planned |
| 5.4 | [Seeded disputes for customers A and B in every status](epic-5-operations/tickets/story-5.4-seeded-disputes-for-customers-a-and-b-in-every-status.md) | backend | planned |
| 5.3 | [SLA sweep escalates overdue disputes](epic-5-operations/tickets/story-5.3-sla-sweep-escalates-overdue-disputes.md) | backend | planned |

Build-order constraint: 5.3 builds after 5.2 and 5.4, and its migration never builds beside 6.1's (both change `AppDbContextModelSnapshot`), so 5.3 starts once 6.1 has merged. From then it may build beside 6.5, 6.2, 6.6 and 6.3, none of which adds a migration.

## Epic 6: [Logins link to customers without revealing a match](epic-6-linking/epic-6-linking.md)

Needs: epic 2 (`CustomerLinks`, the core-banking port and `AuditLog.Record`); epic 3 (`AuditLogEntries`); epic 4 (story 4.1's `Telemetry.ActivitySourceName`, for the linking job's span).

| Story | Title | Owner | Status |
|---|---|---|---|
| 6.1 | [Unlinked login starts linking with a uniform response and an OTP sent after the SIM-swap check](epic-6-linking/tickets/story-6.1-unlinked-login-starts-linking-with-a-uniform-response-and-an.md) | backend | planned |
| 6.5 | [Linking start holds the send cap and a full queue](epic-6-linking/tickets/story-6.5-linking-start-holds-the-send-cap-and-a-full-queue.md) | backend | planned |
| 6.2 | [OTP verification links the login, with lockout and race safety](epic-6-linking/tickets/story-6.2-otp-verification-links-the-login-with-lockout-and-race-safet.md) | backend | planned |
| 6.6 | [Trusted forwarded headers and an identity log without id_token_hint](epic-6-linking/tickets/story-6.6-trusted-forwarded-headers-and-an-identity-log-without-id-tok.md) | backend | planned |
| 6.3 | [Linking rate limits per login and per IP](epic-6-linking/tickets/story-6.3-linking-rate-limits-per-login-and-per-ip.md) | backend | planned |
| 6.4 | [Linking screens for details, OTP, lockout and rate limits](epic-6-linking/tickets/story-6.4-linking-screens-for-details-otp-lockout-and-rate-limits.md) | frontend | planned |

Build-order constraint: once 6.1 merges, 6.5, 6.2 and 6.6 build in parallel (5.3 may run beside them); 6.3 starts once both 6.5 and 6.2 have merged (it needs 6.5's `ErrorKind.RateLimited` and 6.2's verify route); 6.4 follows 6.3. 6.5 and 6.2 share `LinkingEndpoints.cs`, `Infrastructure/Linking/` and the generated contract files in different sections, so the later of the two rebases and regenerates `openapi.json` and `frontend/src/api/generated/`.

## Epic 7: [Journeys proven on the stack, deployed and documented](epic-7-release/epic-7-release.md)

Needs: epic 2 (the themed hosted login for UJ-1); epic 3 (dispute screens for UJ-1, UJ-2 and UJ-5); epic 4 (events on the topic for the four-hop trace); epic 5 (Scalar and the sweep for the README and the sweep story); epic 6 (linking screens for UJ-1).

| Story | Title | Owner | Status |
|---|---|---|---|
| 7.10 | [Production readiness review](epic-7-release/tickets/story-7.10-production-readiness-review.md) | supervisor | planned |
| 7.1 | [QA journey UJ-1 on the real stack with axe and ZAP](epic-7-release/tickets/story-7.1-qa-journey-uj-1-on-the-real-stack-with-axe-and-zap.md) | qa | planned |
| 7.2 | [QA journeys UJ-2, UJ-5a, UJ-5b and UJ-5d](epic-7-release/tickets/story-7.2-qa-journeys-uj-2-uj-5a-uj-5b-and-uj-5d.md) | qa | planned |
| 7.3 | [CI runs the journeys with ZAP and publishes images to GHCR](epic-7-release/tickets/story-7.3-ci-runs-the-journeys-with-zap-and-publishes-images-to-ghcr.md) | backend | planned |
| 7.6 | [Decision records and the production path](epic-7-release/tickets/story-7.6-decision-records-and-the-production-path.md) | docs | planned |
| 7.7 | [README, runbook and onboarding](epic-7-release/tickets/story-7.7-readme-runbook-and-onboarding.md) | docs | planned |
| 7.4 | [AWS Compose override and OpenTelemetry collector](epic-7-release/tickets/story-7.4-aws-compose-override-and-opentelemetry-collector.md) | backend | planned |
| 7.5 | [Terraform dev environment and OIDC deploy](epic-7-release/tickets/story-7.5-terraform-dev-environment-and-oidc-deploy.md) | backend | planned |
