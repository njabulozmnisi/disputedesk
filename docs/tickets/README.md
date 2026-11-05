# Build order

Every epic and story in the order it is built. A story's number is `<epic>.<story>`; it is also its file name prefix, its `docs/verification/` prefix and the name used in reports and commits. The order comes from the `tickets.toml` files (table order); ids are never renumbered, so a story a replan moved keeps its number (5.4 is built before 5.3, 3.5 moved to 5.4, 2.7 is built before 2.4, 3.8 is built before 3.3, 3.9 right after 3.3, 3.10 and 3.11 right after 3.6, 5.5 right after 5.1, 6.6 before 6.3, 7.6 and 7.7 right after 7.3, 7.11 before 7.4, 7.12, 7.13 and 7.14 before 7.10, and 7.10 last).

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
| 2.6 | [Hosted login pages themed from the design tokens](epic-2-transactions/tickets/story-2.6-hosted-login-pages-themed-from-the-design-tokens.md) | backend | done |

## Epic 3: [Customers raise, follow and withdraw disputes](epic-3-disputes/epic-3-disputes.md)

Needs: epic 2 (CurrentCustomer, the Linked policy, the transactions handler and the SPA shell).

| Story | Title | Owner | Status |
|---|---|---|---|
| 3.1 | [Linked customer raises a dispute that commits with its stored event and outbox row](epic-3-disputes/tickets/story-3.1-linked-customer-raises-a-dispute-that-commits-with-its-store.md) | backend | done |
| 3.2 | [Raising is idempotent under an Idempotency-Key](epic-3-disputes/tickets/story-3.2-raising-is-idempotent-under-an-idempotency-key.md) | backend | done |
| 3.8 | [Dispute changes are recorded as stored events, and disputes load by replay](epic-3-disputes/tickets/story-3.8-dispute-changes-are-recorded-as-stored-events-and-disputes-lo.md) | backend | done |
| 3.3 | [Customer opens a dispute and follows its timeline](epic-3-disputes/tickets/story-3.3-customer-opens-a-dispute-and-follows-its-timeline.md) | backend | done |
| 3.9 | [Customer lists their disputes newest first, a page at a time](epic-3-disputes/tickets/story-3.9-customer-lists-their-disputes-newest-first-a-page-at-a-time.md) | backend | done |
| 3.4 | [Customer withdraws an open dispute](epic-3-disputes/tickets/story-3.4-customer-withdraws-an-open-dispute.md) | backend | done |
| 3.6 | [Raise-a-dispute screens with idempotent submit](epic-3-disputes/tickets/story-3.6-raise-a-dispute-screens-with-idempotent-submit.md) | frontend | done |
| 3.10 | [Dispute status badges on transaction rows](epic-3-disputes/tickets/story-3.10-dispute-status-badges-on-transaction-rows.md) | frontend | done |
| 3.11 | [Raise screens emit browser spans that join the API trace](epic-3-disputes/tickets/story-3.11-raise-screens-emit-browser-spans-that-join-the-api-trace.md) | frontend | done |
| 3.7 | [Disputes history screen and the shell's skip link](epic-3-disputes/tickets/story-3.7-disputes-history-screen-and-the-shells-skip-link.md) | frontend | done |
| 3.12 | [Dispute detail, timeline and withdraw screen](epic-3-disputes/tickets/story-3.12-dispute-detail-timeline-and-withdraw-screen.md) | frontend | done |

## Epic 4: [Dispute events reach Kafka in per-dispute order](epic-4-events/epic-4-events.md)

Needs: epic 3 (outbox rows written by the one commit path (AD-14), with outbox ID = stored event ID).

| Story | Title | Owner | Status |
|---|---|---|---|
| 4.1 | [Outbox relay publishes dispute events to Kafka with a Worker heartbeat](epic-4-events/tickets/story-4.1-outbox-relay-publishes-dispute-events-to-kafka-with-a-worker.md) | backend | done |
| 4.2 | [Relay rides out broker outages, failed records, leader handover and shutdown](epic-4-events/tickets/story-4.2-relay-rides-out-broker-outages-failed-records-leader-handove.md) | backend | done |

## Epic 5: [Operations tools and the SLA sweep act on disputes](epic-5-operations/epic-5-operations.md)

Needs: epic 2 (the gateway templates and provisioning from stories 2.5 and 2.6); epic 3 (the Dispute aggregate, loading by replay and the one commit path); epic 4 (the Worker heartbeat health check).

| Story | Title | Owner | Status |
|---|---|---|---|
| 5.1 | [Operations tools authenticate with bearer tokens and read a dispute](epic-5-operations/tickets/story-5.1-operations-tools-authenticate-with-bearer-tokens-and-read-a.md) | backend | done |
| 5.5 | [Scalar drives the operations API with a pasted bearer token behind a switch](epic-5-operations/tickets/story-5.5-scalar-drives-the-operations-api-with-a-pasted-bearer-token.md) | backend | done |
| 5.2 | [Operations actions start review, escalate and resolve under the allowed-actions table](epic-5-operations/tickets/story-5.2-operations-actions-start-review-escalate-and-resolve-under-t.md) | backend | done |
| 5.4 | [Seeded disputes for customers A and B in every status](epic-5-operations/tickets/story-5.4-seeded-disputes-for-customers-a-and-b-in-every-status.md) | backend | done |
| 5.3 | [SLA sweep escalates overdue disputes](epic-5-operations/tickets/story-5.3-sla-sweep-escalates-overdue-disputes.md) | backend | done |

Build-order constraint: 5.3 builds after 5.2 and 5.4, and its migration never builds beside 6.1's (both change `AppDbContextModelSnapshot`), so 5.3 starts once 6.1 has merged. From then it may build beside 6.5, 6.2, 6.6 and 6.3, none of which adds a migration.

## Epic 6: [Logins link to customers without revealing a match](epic-6-linking/epic-6-linking.md)

Needs: epic 2 (`CustomerLinks`, the core-banking port and `AuditLog.Record`); epic 3 (`AuditLogEntries`); epic 4 (story 4.1's `Telemetry.ActivitySourceName`, for the linking job's span).

| Story | Title | Owner | Status |
|---|---|---|---|
| 6.1 | [Unlinked login starts linking with a uniform response and an OTP sent after the SIM-swap check](epic-6-linking/tickets/story-6.1-unlinked-login-starts-linking-with-a-uniform-response-and-an.md) | backend | done |
| 6.5 | [Linking start holds the send cap and a full queue](epic-6-linking/tickets/story-6.5-linking-start-holds-the-send-cap-and-a-full-queue.md) | backend | done |
| 6.2 | [OTP verification links the login, with lockout and race safety](epic-6-linking/tickets/story-6.2-otp-verification-links-the-login-with-lockout-and-race-safet.md) | backend | done |
| 6.6 | [Trusted forwarded headers and an identity log without id_token_hint](epic-6-linking/tickets/story-6.6-trusted-forwarded-headers-and-an-identity-log-without-id-tok.md) | backend | done |
| 6.3 | [Linking rate limits per login and per IP](epic-6-linking/tickets/story-6.3-linking-rate-limits-per-login-and-per-ip.md) | backend | done |
| 6.4 | [Linking screens for details, OTP, lockout and rate limits](epic-6-linking/tickets/story-6.4-linking-screens-for-details-otp-lockout-and-rate-limits.md) | frontend | done |

Build-order constraint: once 6.1 merges, 6.5, 6.2 and 6.6 build in parallel (5.3 may run beside them); 6.3 starts once both 6.5 and 6.2 have merged (it needs 6.5's `ErrorKind.RateLimited` and 6.2's verify route); 6.4 follows 6.3. 6.5 and 6.2 share `LinkingEndpoints.cs`, `Infrastructure/Linking/` and the generated contract files in different sections, so the later of the two rebases and regenerates `openapi.json` and `frontend/src/api/generated/`.

## Epic 7: [Journeys proven on the stack, deployed and documented](epic-7-release/epic-7-release.md)

Needs: epic 2 (the themed hosted login for UJ-1); epic 3 (dispute screens for UJ-1, UJ-2 and UJ-5); epic 4 (events on the topic for the four-hop trace); epic 5 (Scalar and the sweep for the README and the sweep story); epic 6 (linking screens for UJ-1).

| Story | Title | Owner | Status |
|---|---|---|---|
| 7.1 | [QA journey UJ-1 on the real stack with axe and ZAP](epic-7-release/tickets/story-7.1-qa-journey-uj-1-on-the-real-stack-with-axe-and-zap.md) | qa | done |
| 7.2 | [QA journeys UJ-2, UJ-5a, UJ-5b and UJ-5d](epic-7-release/tickets/story-7.2-qa-journeys-uj-2-uj-5a-uj-5b-and-uj-5d.md) | qa | done |
| 7.3 | [CI runs the journeys with ZAP and publishes images to GHCR](epic-7-release/tickets/story-7.3-ci-runs-the-journeys-with-zap-and-publishes-images-to-ghcr.md) | backend | done |
| 7.6 | [Decision records and the production path](epic-7-release/tickets/story-7.6-decision-records-and-the-production-path.md) | docs | done |
| 7.7 | [README, runbook and onboarding](epic-7-release/tickets/story-7.7-readme-runbook-and-onboarding.md) | docs | done |
| 7.11 | [SPA skips browser tracing when built without it](epic-7-release/tickets/story-7.11-spa-skips-browser-tracing-when-built-without-it.md) | frontend | done |
| 7.4 | [AWS Compose override and OpenTelemetry collector](epic-7-release/tickets/story-7.4-aws-compose-override-and-opentelemetry-collector.md) | backend | done |
| 7.5 | [Terraform dev environment and OIDC deploy](epic-7-release/tickets/story-7.5-terraform-dev-environment-and-oidc-deploy.md) | backend | done |
| 7.12 | [Gateway and edge hardening](epic-7-release/tickets/story-7.12-gateway-and-edge-hardening.md) | backend | done |
| 7.13 | [Backend hardening from the readiness review](epic-7-release/tickets/story-7.13-backend-hardening-from-the-readiness-review.md) | backend | done |
| 7.14 | [Customer data cleared at session end, copy and operations docs](epic-7-release/tickets/story-7.14-customer-data-cleared-at-session-end-copy-and-operations-docs.md) | frontend | done |
| 7.10 | [Production readiness review](epic-7-release/tickets/story-7.10-production-readiness-review.md) | supervisor | done |
| 7.15 | [Deploy role trusts GitHub's immutable OIDC subject](epic-7-release/tickets/story-7.15-deploy-role-trusts-the-immutable-oidc-subject.md) | backend | done |
| 7.16 | [IAM action and condition-key names checked against AWS's service reference](epic-7-release/tickets/story-7.16-iam-names-checked-against-the-aws-service-reference.md) | backend | done |
| 7.17 | [Deploy database setup proved against PostgreSQL 18 with TLS](epic-7-release/tickets/story-7.17-deploy-database-setup-proved-against-postgresql-18-with-tls.md) | backend | done |

Build-order constraint (decided 2026-10-30): 7.1 starts once 6.4 and 3.12 have merged; 7.2 follows 7.1 and 7.3 follows 7.2 (they share `e2e/playwright.config.ts`, and CI runs the journeys 7.1 and 7.2 add). 7.6 waits on nothing and 7.11 waits only on 3.11, so both may build beside the QA stories; 7.7 follows 7.3 and 5.3. 7.4 follows 7.3 and 7.11, and 7.5 follows 7.4 (one lane, B). 7.10 runs last, after every other epic 7 story. Decided 2026-10-31 (7.10 disposition): the review's fixes run as 7.12 (gateway and edge), 7.13 (backend) and 7.14 (frontend and docs), which touch different files and may build in parallel (only 7.12 touches `PlatformScriptTests`); 7.10 closes once all three have merged. Decided 2026-11-01: three offline checks of the AWS deploy path follow the closed 7.10; 7.15 builds first, because the deploy role's trust subject is a live defect; 7.17 may build beside it in the second lane, and 7.16 follows 7.15 in its lane; each adds a different `ci.yml` job or step, so the story that merges second rebases, and none runs an AWS command. No AWS command is run in this epic: the account does not exist, so 7.5 closes on fmt, validate and mocked-provider plans.

## Epic 8: [Simplicity sweep](epic-8-simplicity-sweep/epic-8-simplicity-sweep.md)

Source: waves 2 and 3 of [the simplicity audit's mitigation plan](../verification/simplicity-audit/mitigation-plan.md), with the human's decisions D1 to D6, D9 and D14b of 2026-11-03 (S15 dropped by D7).

| Story | Title | Owner | Status |
|---|---|---|---|
| 8.1 | [CI lints workflows and shell scripts, and the scripts use their tools' documented features](epic-8-simplicity-sweep/tickets/story-8.1-ci-lints-workflows-and-shell-scripts-and-scripts-use-their-tools.md) | backend | done |
| 8.2 | [Backend tidy: one copy of each helper, and no unused members](epic-8-simplicity-sweep/tickets/story-8.2-backend-tidy-one-copy-of-each-helper-and-no-unused-members.md) | backend | done |
| 8.3 | [Frontend tidy: route guards in beforeLoad, and one form pattern](epic-8-simplicity-sweep/tickets/story-8.3-frontend-tidy-route-guards-in-beforeload-and-one-form-pattern.md) | frontend | done |
| 8.4 | [Backend tests use the standard metric, log and clock fakes](epic-8-simplicity-sweep/tickets/story-8.4-backend-tests-use-the-standard-metric-log-and-clock-fakes.md) | backend | done |
| 8.5 | [Architecture source rules read Roslyn syntax trees, and banned APIs fail the build](epic-8-simplicity-sweep/tickets/story-8.5-architecture-source-rules-read-roslyn-syntax-trees-and-banned-apis.md) | backend | done |
| 8.6 | [E2E specs share their helpers and wait on signals, not time](epic-8-simplicity-sweep/tickets/story-8.6-e2e-specs-share-their-helpers-and-wait-on-signals.md) | qa | done |
| 8.7 | [Gateway and Compose checks move to the infra test project, edge headers checked once](epic-8-simplicity-sweep/tickets/story-8.7-gateway-and-compose-checks-move-to-the-infra-test-project.md) | backend | done |
| 8.8 | [Deploy, database-init and Compose-render harnesses move to the infra test project](epic-8-simplicity-sweep/tickets/story-8.8-deploy-database-init-and-compose-render-harnesses-move-to-xunit.md) | backend | planned |
| 8.9 | [Gateway sets the forwarded-header block once per server](epic-8-simplicity-sweep/tickets/story-8.9-gateway-sets-the-forwarded-header-block-once-per-server.md) | backend | planned |
| 8.10 | [Every route checks in the framework's order: 401, 403, 400, 404, 409, 422](epic-8-simplicity-sweep/tickets/story-8.10-every-route-checks-in-the-framework-order.md) | backend | done |
| 8.11 | [One enum rule on the wire: exact camelCase names only](epic-8-simplicity-sweep/tickets/story-8.11-one-enum-rule-on-the-wire-exact-camelcase-names-only.md) | backend | done |
| 8.12 | [Operations callers are a plain allowlist of client subs](epic-8-simplicity-sweep/tickets/story-8.12-operations-callers-are-a-plain-allowlist-of-client-subs.md) | backend | planned |
| 8.13 | [Every session ends in the browser by a document navigation](epic-8-simplicity-sweep/tickets/story-8.13-every-session-ends-in-the-browser-by-a-document-navigation.md) | frontend | done |
| 8.14 | [The SLA sweep only escalates: SlaWarningRaised is dropped](epic-8-simplicity-sweep/tickets/story-8.14-the-sla-sweep-only-escalates-slawarningraised-is-dropped.md) | backend | planned |
| 8.15 | [The seeder is the third named ownership-filter bypass](epic-8-simplicity-sweep/tickets/story-8.15-the-seeder-is-the-third-named-ownership-filter-bypass.md) | backend | planned |
| 8.16 | [IAM policies are JSON templates checked in the infra tests, and the OIDC jobs become a recorded check](epic-8-simplicity-sweep/tickets/story-8.16-iam-policies-are-json-templates-checked-in-the-infra-tests.md) | backend | planned |
| 8.17 | [Contracts references Domain for the enums that are identical on the wire](epic-8-simplicity-sweep/tickets/story-8.17-contracts-references-domain-for-the-enums-identical-on-the-wire.md) | backend | planned |

Build-order constraint (decided 2026-11-04): wave 2 first. 8.1 (CI linters) lands before the infra stories 8.7 and 8.8; 8.2, 8.3, 8.4 and 8.6 wait on nothing in this epic and may build in parallel within the lane limits; 8.5 follows 8.4 (it bans what 8.4 removes); 8.8 follows 8.7 (shared helpers) and 8.9 follows 8.7 (its header tests prove the nginx change). Wave 3 runs one at a time in the order 8.10, 8.11, 8.12, 8.14, 8.15, 8.17, because they touch the same endpoints and types; 8.10 also waits on 8.2 and 8.4, and 8.15 on 8.5. 8.13 (frontend) waits only on 8.3 and may build beside 8.12; 8.16 (infra) waits only on 8.8. 8.14 is the epic's one migration, so no other migration runs beside it.

## Epic 9: [Production CI on GitHub flow](epic-9-github-flow-ci/epic-9-github-flow-ci.md)

Source: [ad-012, CI and deploy follow GitHub flow](../decisions/ad-012-github-flow-ci.md) (accepted 2026-11-05) and its [challenge](../verification/ad-012-challenge.md). Needs: epic 8 (every story merged on today's flow).

| Story | Title | Owner | Status |
|---|---|---|---|
| 9.1 | [Every check runs on pull requests and reports through two gate jobs](epic-9-github-flow-ci/tickets/story-9.1-every-check-runs-on-pull-requests-and-reports-through-two-gate-jobs.md) | backend | planned |
| 9.2 | [Deploy follows publish on main, behind a switch, and the deploy role trusts the dev environment](epic-9-github-flow-ci/tickets/story-9.2-deploy-follows-publish-on-main-behind-a-switch.md) | backend | planned |
| 9.3 | [Main is protected by a ruleset, and stories merge by squash through their pull request](epic-9-github-flow-ci/tickets/story-9.3-main-is-protected-and-stories-merge-by-squash-through-their-pull-request.md) | backend | planned |

Build-order constraint (decided 2026-11-05): ad-012's rollout order, one lane, one story at a time. 9.1 starts once every epic 8 story has merged; 9.2 follows 9.1 (both change `ci.yml`'s `publish` job); 9.3 follows 9.2. 9.1 and 9.2 merge on today's flow; the human applies 9.3's ruleset once its pull request shows both gates, nothing else merges until 9.3 does, and 9.3 is the first squash merge. The epic 8 lows batch and retrospective run after epic 9, on the new flow.
