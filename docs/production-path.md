# Production path

Context: a large retail bank with tens of millions of clients. That count is an estimate, and every other scale figure in this document is an estimate labelled as one (architecture, Deferred; story 7.6 Notes, decision 2026-10-29).

This document records what DisputeDesk v1 defers and why (NFR-DOC-2). It designs none of the deferred mechanisms: each entry says what v1 does, what production does instead, and why it can wait, in a few sentences. Decision records are in `docs/decisions/`; the architecture spine is `docs/architecture/architecture.md`.

Entries are in two sections. *Deferred scope* holds what v1 leaves out: the PRD §6.2 items, the architecture's Deferred rows and the production steps v1 does not take. *Accepted risks* holds known limits of mechanisms v1 built, each with the control in place and the upgrade. Entry numbers are stable and appear in number order within each section. The source-to-entry mapping that checked this document's completeness when it was written is in [the 7.6 check](verification/7.6-check.md#source-to-entry-mapping).

## Deferred scope

### P1. Provisional credit

v1 never credits a customer while a dispute is open. Provisional credit is a US Regulation E norm with no documented South African equivalent (PRD §6.2; dispute-lifecycle research). *Why it waits:* no local rule asks for it.

### P2. Attachments

A dispute carries a reason, an optional note and, for wrong amount, the expected amount (FR-13, FR-14); no file can be attached (PRD §6.2). *Why it waits:* out of scope for v1 (PRD §6.2).

### P3. Customer notifications on status changes

Mailpit carries only email verification, password reset and the linking OTP; a customer sees a status change in the app (PRD §6.2). Production notifies customers as part of the bank-scale end state (P13). *Why it waits:* it is part of the bank-scale end state (PRD §6.2).

### P4. Customer self-unlink and lost-login recovery

A linked login stays linked in v1, and a lost login has no self-service recovery (PRD §6.2). *Why it waits:* both are bank-assisted in production (PRD §6.2).

### P5. Partial disputes other than wrong amount

A dispute is raised for the full amount, or for the difference when the reason is wrong amount (glossary: Dispute; FR-14; PRD §6.2). *Why it waits:* out of scope for v1 (PRD §6.2).

### P6. Scheme-specific windows per reason

v1 uses one window: a settled card debit within 90 days of the transaction date (glossary: Disputable transaction). Card schemes have longer windows for some reasons, for example the 540-day not-received extension counted from the expected delivery date (PRD §6.2; NFR-DOC-5). *Why it waits:* the scheme rule texts were not read (NFR-DOC-5).

### P7. A separate fraud route for unrecognised transactions

"Unrecognised" is one reason among six in v1 (glossary: Reason). South African practice commonly sends it down a separate fraud route (PRD §6.2; dispute-lifecycle research, section 2). *Why it waits:* out of scope for v1 (PRD §6.2); downstream fraud services are part of the bank-scale end state (P13).

### P8. A general support platform for several departments, and a UI for AI agents

DisputeDesk is not a case-management system; operations work happens in another team's tool, which calls the API (PRD §6.2, §7). *Why it waits:* outside the product's job (PRD §7, Non-goals).

### P9. Follow-up messages from the customer after raising a dispute

The customer writes a note at raise and at withdraw only (FR-13, FR-18; PRD §6.2). *Why it waits:* out of scope for v1 (PRD §6.2).

### P10. Filters and search on transactions

The transactions list is keyset-paged, newest first, with no filter (AD-5; PRD §6.2). *Why it waits:* out of scope for v1 (PRD §6.2).

### P11. Languages other than English, and currencies other than ZAR

v1 is English only and ZAR only (PRD §6.2). Core banking is assumed to return the ZAR billed amount (AD-4, FR-14), and the dispute snapshot stores the currency as received, with no check (a `// shortcut:` in `Dispute.cs`). Before a non-ZAR source is connected, raise must reject, or convert to the ZAR billed amount, any transaction whose currency is not ZAR (story 3.1). The 7.10 PRR (A-F1) adds that the core-banking client also checks each amount against the documented range and precision before a transaction snapshot is accepted, with tests for malformed upstream responses (ASVS 5.0 V2.2.1, V2.2.3). *Why it waits:* the only source in v1 is the stub, which returns ZAR.

### P12. Joint accounts

An `accountRef` is assumed to belong to one customer. With joint accounts, a co-owner's raise gets 409 and learns another owner's dispute exists (story 3.1 security S3; its mapping row is P41's). Before joint accounts are connected, this assumption needs a decision. *Why it waits:* v1's core-banking source assumes one owner per `accountRef` (story 3.1 security S3).

### P13. The bank-scale end state

Linking moves into the bank's identity platform, downstream services consume the dispute events for chargebacks, fraud and complaints reporting, and customers are notified when a status changes (PRD §6.2; ad-001). *Why it waits:* it needs systems outside this repo.

### P14. Hosted login branding

The hosted login shows Zitadel's default copy ("Create your Zitadel account.", "Welcome back!") instead of the mockups' "Sign in to DisputeDesk" (story 2.6 QA). *Why it waits:* the hosted login text customisation is a later polish item (story 2.6 QA).

### P15. A schema registry

There is no schema registry: the two event types have committed JSON Schemas under `contracts/events/` and a contract test (AD-19). Redpanda's built-in registry is the upgrade path (PRD §6.2). *Why it waits:* AD-19 rejects a registry for v1; the committed schemas and the contract test check both types.

### P16. An event consumer and its inbox table

No consumer lives in this repo. A consumer would dedupe with an inbox table, keyed on `source` + `id`, and apply an event only if its `disputeVersion` is newer than the last it applied (PRD §6.2; AD-17, AD-19; Enterprise Integration Patterns, *Idempotent Receiver*). *Why it waits:* out of scope; the contract already requires dedupe.

### P17. Order across disputes

Order is defined only within one dispute, by `Version`; there is no global position and no index by event type (NFR-DATA-5; AD-17; ad-009). A PostgreSQL sequence is not a commit-order checkpoint, because sequence values are not rolled back and transactions commit out of allocation order (ad-009). If a reader across streams appears, the options are Kafka offsets, a high-water mark with `pg_snapshot_xmin`, or change data capture (PRD §6.2; architecture, Deferred). *Why it waits:* nothing reads across streams in v1.

### P19. Relay tuning and error handling

The exact relay batch size and backoff curve are tuning; AD-17 fixes the behaviour (architecture, Deferred). librdkafka error events are logged per occurrence, about 62 warnings per 30-second outage (story 4.2 review); production logs on state change or rate-limits per code. An unlisted error code on one dispute's head row stalls the whole relay; production defers per row and alerts on the deferred-warning rate (story 4.2 review). *Why it waits:* tuning; AD-17 fixes the behaviour (architecture, Deferred).

### P20. Failed outbox rows and a dead-letter topic

After five permanent rejections a row is `Failed`, and later events for that dispute wait (AD-17). An operator releases a `Failed` row with a targeted `UPDATE` (the runbook, AD-22's one operator exception). A row whose payload itself is refused cannot be released, because the payload is never edited; production adds a reviewed repair path or a dead-letter topic with an audited replay (story 7.7 refine; PRD §6.2, Operations features). *Why it waits:* AD-17 rejects a dead-letter topic for v1, and an operator release covers a fixed cause (AD-22).

### P22. Error-rate alerts on expected unique violations

EF Core logs a unique violation (SQL state 23505) at Error even when the application expects and answers it. Alerting excludes 23505 on `UQ_Disputes_AccountRef_TransactionId` (a lost raise race, 409 `already-disputed`) and `UQ_StoredEvents_DisputeId_Version` (a version clash, AD-15). The logs are not filtered, because an EF Core filter would hide every failed save (story 3.4 pre-build review). *Why it waits:* it is an alerting rule for production, recorded by the story 3.4 pre-build review; v1 changes no logging.

### P23. Retention, cleanup and de-identification

Nothing is deleted from `Disputes`, `StoredEvents` or `AuditLogEntries` in v1 (AD-22). Retention follows transaction records: five years under the FIC Act, then de-identification under POPIA s14 (NFR-DATA-7). Production adds the jobs that enforce this, outbox cleanup and topic retention tuned to POPIA (PRD §6.2). It also adds a retention job deleting expired idempotency keys: `IdempotencyKeys.ResponseBody` keeps a copy of the customer's dispute view, expiry applies only when a key is reused, so expired rows are never deleted and an erasure on the dispute would miss them (story 3.2 security S2; ASVS 4.0.3 V8.3.8). The 7.10 PRR (E-F7) adds that the purge runs on a schedule, not only on key reuse, and that the policy covers stored events, outbox rows and backups without deleting financial records the FIC Act requires (ASVS 5.0 V14.2.2). *Why it waits:* the five-year retention period (NFR-DATA-7) has not run for any v1 data.

### P24. Crypto-shredding of personal data in stored events

`DisputeRaised` holds the customer ID, the account reference, the transaction ID and the snapshot; stored events are insert only (ad-009, AD-22). Production encrypts that personal data under a per-customer key and destroys the key on erasure (architecture, Deferred; NFR-SEC-5). *Why it waits:* in v1 stored events are kept under NFR-DATA-7 with the same access as `Disputes`, never logged or traced (AD-22).

### P25. Stored event schema evolution beyond additive changes

Changes within a stored event type are additive; a new shape is a new type name (AD-14a). Upcasting old shapes on read is the production option (architecture, Deferred). *Why it waits:* AD-14a's rule (additive changes only, a new shape is a new type) covers v1.

### P26. Snapshots to shorten replay

A handler loads a dispute by replaying its whole stream (AD-13). Snapshots at intervals would shorten replay (architecture, Deferred). *Why it waits:* streams hold a handful of stored events in v1 (estimate: 15 to 100 per stream once internal work is recorded).

### P28. Migrations as a bundle run as a one-off job

The Api applies EF Core migrations at start-up, under EF Core's migration lock, before it reports ready (AD-22). Production builds a migration bundle and runs it once as a one-off job before the new release starts, instead of every replica migrating at start-up ([EF Core, *Applying migrations*](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying); standards audit, 2026-10-27). *Why it waits:* v1 runs one Api instance.

### P29. Idempotency claim: a proposed EF Core-only replacement

**Current mechanism.** `IdempotencyKeyClaims.ClaimAsync` claims the key with one PostgreSQL `INSERT … ON CONFLICT` statement, as AD-16 specifies: an expired row is replaced in the same statement.

**Proposed future change, not built.** Claim with EF Core alone, through the unique constraint `UQ_IdempotencyKeys_CustomerId_Key` plus EF Core's documented optimistic concurrency on `LeaseId` ([EF Core, *Handling concurrency conflicts*](https://learn.microsoft.com/ef/core/saving/concurrency)), with explicit handling of the insert conflict (a `DbUpdateException` on the unique index) and of the lease and the stored response, as `docs/research/ef-core-upsert/ef-core-upsert.md` recommends. This is not an EF Core upsert, which EF Core does not have, and it is not an established guarantee until it is built and tested. AD-16 changes only when it is built. *Why it waits:* the current claim is correct as built (the research), and switching costs a contained rework with another fix round (the research, *Cost of switching now*); the initiative decision of 2026-10-29 makes it a production-path item.

### P31. Linking send cap and delivery

Every claimed OTP send counts toward the three-per-hour cap, delivered or not, so a customer hit by SMS delivery failures waits for the window. Production reads the SMS provider's delivery receipts to tell a failed delivery from an uncertain one. The cap count has no index on (`CustomerId`, `OtpSentAtUtc`); production adds one once `LinkingChallenges` outgrows a sequential scan (story 6.5 refine; AD-12). *Why it waits:* `ISmsSender` writes to Mailpit in v1 (AD-21), not to an SMS provider.

### P33. Scale-out and shared limit state

v1 runs one Api instance and, in AWS, one Worker instance (NFR-ARC-3). Production runs several of each, with shared rate-limit state and latency targets under load (PRD §6.2). Linking rate limits are held in memory on the single Api instance, so they reset on restart and do not add up across instances; scale-out needs a shared limiter store. The Api trusts forwarded headers from every container on the Compose network; production trusts only the load balancer's or gateway's own addresses (story 6.3 refine; see also P43). One over-limit login rotating IPv6 /64 prefixes writes one audit row per rejection, and the gateway's `limit_req` keys on the full address; production keeps limits in a shared store and keys the gateway on the /64 (story 6.3 security). *Why it waits:* sessions, idempotency keys and the audit log are already in PostgreSQL; in-memory limits are the one exception (NFR-ARC-3).

### P34. Operations features: listing and search

Operations tools read one dispute by ID (FR-30) and act on it; there is no listing or search for them (PRD §6.2, Operations features). The other operations features are P20 (dead-letter topic) and P23 (outbox cleanup, topic retention). No operations story is deferred: 5.3 and 5.4 are in v1's scope (`docs/tickets/README.md`; initiative scope decision, 2026-10-27). *Why it waits:* operations work happens in another team's tool, which calls the API (PRD §7).

### P42. Browser telemetry

Locally, `/otlp/` is an anonymous write path: any client can post forged spans under any `service.name`, up to 1 MB per request within the coarse rate-limit zone, to the Dashboard (story 3.11 security T8). In a deployed environment the AWS gateway routes no `/otlp/` and the SPA built for AWS exports nothing, so browser spans are a local-stack feature (AD-29; story 7.4 refine). Before switching it on, add an authenticated or sampled intake (for example behind the browser session with per-session limits) and filter browser spans at the collector by `service.name` (`disputedesk-web`) and an attribute allow-list. The 7.10 PRR (F-F10) adds a scope decision before production: either privacy-safe browser signals with the trace continuation the PRD specifies (P16 records the missing event consumer), or NFR-OBS-1 and NFR-OBS-3 revised to the narrower architecture. *Why it waits:* no deployed environment takes browser spans in v1.

### P45. Init process as PID 1

Containers run an init process as PID 1 (Compose `init: true`), because the kernel drops signals PID 1 has no handler for, so a .NET process that fails to start would spin instead of exiting (pid_namespaces(7)). Any deployment outside Compose needs the same: ECS `linuxParameters.initProcessEnabled`, or an init in the image (start-up exit fix, 2026-10-30). *Why it waits:* v1 deploys only through Compose (AD-27, AD-28).

### P46. AWS dev environment, a demo deployment

The AWS dev environment is checked with `terraform fmt`, `validate` and plans against mocked providers (AD-28; story 7.5 refine). It was first deployed on 2026-11-07, and `infra/terraform/README.md`'s post-deploy checks pass. It holds only synthetic data. Before any real use, move the origin-verify value, which Terraform state holds, to a write-only argument or ephemeral resource; the RDS master password already uses write-only arguments. *Why it waits:* the environment carries only synthetic data, on a dedicated account.

### P47. CloudFront caching for static assets

All three distributions (app, auth and mail) use `CachingDisabled` (AD-28; story 7.4 pre-build security S7.4-1, human decision 2026-10-30). Caching static assets is a performance change (architecture, Deferred). *Why it waits:* performance only; caching stays disabled until then.

### P48. Node 26 LTS move

`.nvmrc` stays on Node 24 until a story bumps it (architecture, Deferred). *Why it waits:* the architecture revisits the move after 2026-10-28 (architecture, Deferred).

### P57. Probes in the published images

The published api and worker images are the `api-local` and `worker-local` targets: the chiseled .NET images plus a static busybox (named `wget` in the api, full busybox in the worker), because AWS runs Compose on one instance and Compose health checks run inside the container (human decision 2026-10-30, story 7.4 build; AD-22, AD-25). Production runs the shell-free `api` and `worker` targets under an orchestrator whose probes call `/health/live` and `/health/ready` over the network (Kubernetes liveness and readiness probes, or an ECS target group's health check). *Why it waits:* v1 has no orchestrator or load balancer to probe, and the health checks keep AD-22's "the Worker never runs on an old schema" and AD-25's restarts.

### P76. A deadline shared across tabs

Each tab keeps its own idle deadline, 15 minutes after the last API response in that tab, and its own warning; no deadline or message passes between tabs (AD-7). Because the deadline is per tab, an idle tab can show the session-ended screen while another tab keeps the server session live; from that tab the wordmark or Back reaches the app again without signing in. Production can share the deadline across the tabs of one session, for example through `BroadcastChannel` (HTML, *Broadcasting to other browsing contexts*), so activity in one tab moves the warning in all of them and every tab ends together. *Why it waits:* the server's 15-minute idle limit is the authority, so no tab keeps a session the server has ended, and other tabs learn of an end on focus (P77; human decision 2026-11-03).

## Accepted risks

### P18. Fencing the relay leader with Kafka transactions

The relay's advisory lock cannot fence a Kafka send already in flight when a leader loses its connection, so across a handover an earlier event of a dispute can arrive late or twice (AD-17). Fencing with Kafka transactions (a fixed `transactional.id`, KIP-98) is the stronger option. The 7.10 PRR (F-F7) adds that the PRD's ordering promise (NFR-DATA-5) needs either that fencing before strict arrival order is promised, or a recorded human decision that reconciles the PRD with per-dispute order by `disputeVersion`. *Why it waits:* v1 is at-least-once and consumers keep per-dispute order by `disputeVersion` (AD-17, decided 2026-10-29).

### P21. Kafka transport security

The broker runs plaintext with no client authentication on the private Compose network in v1 (a `// shortcut:` in the relay's producer configuration; AD-26). Before a shared network, enable TLS plus SASL/SCRAM or mTLS on the broker, with topic ACLs so only the relay principal writes `disputedesk.disputes` (story 4.1 security S4, human decision 2026-10-29). The 7.10 PRR (C-F6) adds negative tests that an unauthorised principal can neither publish, consume nor administer topics (ASVS 5.0 V8.2.1, V8.2.2). *Why it waits:* the broker is on the private Compose network only (AD-26).

### P27. Database-level append-only grant on `AuditLogEntries` and `StoredEvents`

The AD-14 guard refuses a Modified or Deleted audit row or stored event, and a source-rules test fails on bulk updates or raw SQL against either table (AD-14). Production also grants the application role insert and select only on both tables, with a separate migration role (architecture, Deferred; OWASP Logging Cheat Sheet). The 7.10 PRR (E-F2) adds that the runtime account owns the database today (`infra/images/postgres/10-roles-and-databases.sh`), so production gives migrations their own owner role, grants the Api and the Worker only the operations each uses, and keeps maintenance access separate (ASVS 5.0 V13.2.2, V16.4.2; see P28). *Why it waits:* the application enforces insert only in v1.

### P30. OTP guess bound

The controls bound OTP guesses per challenge (lockout after three wrong codes, FR-7) and per login window (the in-memory per-login and per-IP limits, FR-8); the send cap (FR-4) limits codes issued, not verification attempts (ad-008). This is not the account-wide limit on failed attempts that [NIST SP 800-63B-4, *Rate Limiting (Throttling)*](https://pages.nist.gov/800-63-4/sp800-63b.html) describes, because restarting linking creates a fresh challenge with 0 attempts. The upgrade is a persistent per-customer failure counter that survives restarts and new challenges (human decision, 2026-10-30, 7.6 challenge). *Why it waits:* no control changes in v1, by that decision.

### P32. Session key renewal at a privilege change

Linking keeps the session key minted at sign-in, since the built-in cookie handler with a server-side ticket store cannot change the key within one request (ad-007; story 6.2 decision, 2026-10-30). Production renews the key at every privilege change ([OWASP Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html)) once the handler supports it, or moves to a session store that re-keys. The 7.10 PRR (C-F2) adds a test that the pre-link session cookie, replayed after linking, reaches no customer resource once the key is renewed (ASVS 5.0 V7.2.4). *Why it waits:* fixation is already blocked, because the store rejects any key it did not mint (AD-7).

### P35. Operations tool credentials and secret placement

In v1 `scripts/operations-token.sh` runs the client-credentials grant with the machine user's shared secret, and a person pastes the token into Scalar, which holds no credential (AD-26). The machine user's secret sits in `zitadel-clients.json`, which the Api container mounts and loads in full, though the Api never uses it. Real operations tools keep their own credentials in a secret store and authenticate with `private_key_jwt` (RFC 7523) instead of a shared secret; the access-token lifetime is set deliberately on the machine user (Zitadel's default in v1). Production gives each operations tool its own credential store, which the Api cannot read (least privilege, ASVS 5.0 V13.3; stories 5.1 and 5.5). *Why it waits:* Scalar stands in for another team's tool in v1 (NFR-DOC-4).

### P36. API viewer origin

Scalar is served on the customers' app origin under its own CSP (AD-26). Production serves any API viewer from a separate origin, so no script on it can reach the session cookie's routes (story 5.1 security S3). The 7.10 PRR (A-F8) adds bounding the API access the separate viewer origin has, or disabling the viewer on a customer-facing deployment (ASVS 5.0 V3.5.4). *Why it waits:* Scalar is a dev and demo tool (NFR-DOC-4).

### P37. Rate limits on customer and operations routes

Each refused withdraw (404) writes an audit row, and customer routes have no rate limiter, so a linked customer looping over random IDs adds one row per request (story 3.4 security S9). Each refused operations action writes a `Denied` or `Rejected` audit row, and the operations routes have no rate limiter either (story 5.2 pre-build review). Production adds a per-login limiter on unsafe customer routes and a per-client limiter on the operations routes, with the built-in rate limiter, as AD-12 does for linking ([Microsoft Learn, *Rate limiting middleware*](https://learn.microsoft.com/aspnet/core/performance/rate-limit); ASVS 5.0 V2.4.1). The 7.10 PRR (A-F2) adds operations reads, whose refusals also write audit rows, and storage and capacity thresholds proved by abuse tests. *Why it waits:* the gateway's coarse per-address limit already caps the request rate on `/api/` in v1 (AD-23).

### P38. Operations route fallback and audit cancellation

A wrong method or an unmatched path under `/api/v1/operations` reaches the framework's 405 or 404 endpoint, which carries no policy, so the cookie-only fallback answers bearer callers with 401 and no `Bearer` challenge (RFC 6750 §3); it fails closed and runs no handler. Production gives the operations group its own fallback answer. Audit saves for `Denied` and `Rejected` use the request's cancellation token, so a client that disconnects mid-request can cancel its own audit row; production saves audit rows with `CancellationToken.None` (OWASP Logging Cheat Sheet; story 5.2 review and security). A sign-out audit lost to a database failure is only logged; story 7.13 makes audit saves immune to client cancellation, and production gives audit a durable path (P71; accepted for the demo, human decision 2026-10-31). *Why it waits:* both fail closed: no handler runs and no dispute changes.

### P39. Problem `type` URLs on an owned domain

Problem `type` URLs live under the GitHub namespace (AD-3), an accepted risk: a rename, deletion or repo-jacking would break or hijack them. Production moves them to an owned domain (story 2.7 security review, 2026-10-27). *Why it waits:* the project owns no domain (AD-19).

### P40. Seeding in a real deployment

`Seed:Enabled` defaults to off and is set only in Compose and the AWS demo (AD-26), but nothing refuses it at start-up. A real deployment refuses `Seed:Enabled` at start-up, so seeded events are never published for fixture customer IDs (story 5.4 security S4; ASVS 5.0 V13, configuration). Seeded snapshots on a stack kept across a day boundary without `docker compose down -v` are a demo limitation; the README says to reset (architecture, Deferred; AD-30). *Why it waits:* v1 has only local and demo environments.

### P41. Trace context trust

The browser's `traceparent` and `tracestate` are accepted as the request's parent, so the audit `TraceId` and the outbox trace columns are caller-chosen (story 3.1 security S3). The Api trusts any caller's `traceparent` under OpenTelemetry .NET's default ParentBased(AlwaysOn) sampler (no sampler is set; unverified on the stack), so a caller can send flags `00` to suppress the Api's spans for its own requests and choose the trace ID stored in `StoredEvents.TraceId` and the outbox (story 3.11 security T7). Production restarts or validates trace context at the gateway, the trust boundary, or sets a sampler that does not follow an untrusted parent's sampled flag at the edge ([W3C Trace Context](https://www.w3.org/TR/trace-context/), security considerations). *Why it waits:* one trace from browser to the relay is a v1 requirement (AD-20).

### P43. Gateway edge: request logs and trusted proxies

Since story 7.12 every origin's access log records the method, path and protocol only (no query string or Referer), but the identity origin's `error_log` still writes the request line with `id_token_hint` on an upstream error. Production lowers the error-log detail. The Api trusts forwarded headers from the whole Compose network, which on a Linux Docker host also includes the host's bridge address; production trusts only the load balancer's or gateway's own addresses (story 6.6 security; ASVS 5.0 V4.1.3, V16.2.5; AD-23). The 7.10 PRR (E-F6) adds sending `id_token_hint` in a POST body rather than the URL where the identity provider supports that form of logout (OpenID Connect RP-Initiated Logout 1.0), and proving the log behaviour during upstream failures, not only on a successful logout (ASVS 5.0 V14.2.1). The nginx error logs on every origin (app, identity and Mailpit) keep the request line and Referer on 429 rejections and upstream failures; story 7.12 fixes the access logs, and production lowers error-log detail on all three origins (accepted for the demo, human decision 2026-10-31). *Why it waits:* v1 runs only the local stack and the AWS dev environment (PRD §6.1).

### P44. Gateway container hardening

`/etc/nginx` is writable by the nginx user, because `nginx-unprivileged` renders its templates there at start; production runs the gateway with a read-only root filesystem and a tmpfs render directory (story 2.5 security F4; CIS Docker 5.12). `infra/images/gateway/Dockerfile` runs an unpinned `apk upgrade` of brotli-libs, curl, libcurl, libexpat, musl, musl-utils and zlib (a `# shortcut:`), because the pinned base lags the Alpine fixes; drop it when a newer pinned base ships them, and consider removing curl (unused at runtime) or the `-alpine-slim` variant (story 2.5 review L1). *Why it waits:* the gateway already runs non-root from a pinned image (AD-27).

### P49. SLA sweep failures

The SLA sweep claims the 50 earliest-due disputes each loop (AD-18); if 50 or more keep failing (for example a stream that will not replay), later disputes are never escalated (FR-26), although each failure is logged at Error (story 5.3 security L4). Production alerts on repeated sweep failures and parks a dispute that fails a set number of times. *Why it waits:* every failure is already logged at Error, so a stall shows in the logs; alerting rules are a production concern, as in P22.

### P50. Demo logins in AWS

The fixture logins of customers A, B and C, whose passwords are published in `backend/fixtures/local.json`, are live on the AWS demo, and self-registration lets anyone link to customer D (story 7.4 pre-build security S7.4-11, human decision 2026-10-30). A real environment has no fixture logins and refuses the seeder at start-up (P40). The 7.10 PRR (B-F8) adds a lifecycle for the bootstrap administrator: short-lived activation, a required change of the initial password, and removing or disabling bootstrap access (ASVS 5.0 V6.3.2, V6.4.1); in AWS the provisioner already deactivates every human administrator on each deploy (aws-admin-lockdown). It also adds (E-F11) a production configuration that refuses fixture provisioning and seeding, replaces the core-banking and SMS stand-ins, and leaves out demo tooling such as Scalar (ASVS 5.0 V15.2.3). *Why it waits:* the demo holds only synthetic data, so a fixture login reaches no real customer.

### P51. Outbox release role

The runbook's release of a `Failed` row runs as the owner role `disputedesk`, which has full DML, and writes no audit record (story 7.7 pre-build security S7.7-2, human decision 2026-10-30). Production gives operators a dedicated role with only column-level `GRANT UPDATE ("Status", "Attempts", "NextAttemptAtUtc") ON "OutboxMessages"` and records each release in the audit log (ASVS 5.0 V8, V16.3; see also P20). *Why it waits:* a release is a manual step from the runbook (AD-22's one operator exception), and v1 environments hold only synthetic data.

### P52. Edge transport

CloudFront reaches the AWS demo's origin over plain HTTP on port 80 (a `# shortcut:` in Terraform); the default `*.cloudfront.net` certificate keeps the minimum viewer protocol at TLS 1.0, and there is no WAF (AD-28; story 7.4 pre-build security S7.4-9 and story 7.5 pre-build security S7.5-11, human decision 2026-10-30). Production uses an HTTPS origin or CloudFront VPC origins, its own domain with an ACM certificate and a TLS 1.2 minimum viewer protocol, and AWS WAF in front of every distribution (ASVS 5.0 V12.1.1, V12.3.1). The 7.10 PRR restates the TLS 1.2 minimum (D-F5) and adds Mailpit's WebSocket path, which crosses the same plain HTTP origin, with a check of the deployed handshake (B-F3; ASVS 5.0 V4.4.1); encryption of the hops behind the origin (D-F6) is P67. *Why it waits:* accepted for the free-plan demo, which holds only synthetic data, and the project owns no domain (AD-19, AD-28).

### P53. Build once, promote by digest

CI's `publish` job rebuilds the images on main rather than pushing the bytes the e2e job tested (it scans each one with Trivy before pushing), and the gateway's `apk upgrade` is unpinned (P44), so the pushed gateway can differ from the tested one (story 7.3 pre-build review 3; AD-32). Production builds each image once, tests and scans that digest, and promotes the same digest. *Why it waits:* each pushed image is still scanned before it is pushed.

### P54. Build provenance

Published images carry the OCI `source` and `revision` labels only (story 7.3 pre-build security S7.3-2). Production adds SLSA build provenance attestations, verified at deploy. *Why it waits:* it needs a new action, which v1 does not add (story 7.3 pre-build security S7.3-2).

### P55. Deploy role subject

The OIDC trust pins the environment `dev`, which only `main` may use, not the workflow (AD-28, ad-012; story 7.5 pre-build security S7.5-7). Production customises the subject claim to include `job_workflow_ref`, so only `deploy.yml` on main can assume the role (GitHub, *Customizing the token claims*). The one CI job that holds `id-token: write` is `deploy`, which calls `deploy.yml` (the OIDC token and subject are a one-time check recorded in `infra/terraform/README.md`); a job that is given it later could mint a token the deploy role accepts, and the `job_workflow_ref` subject closes that too. *Why it waits:* the trust already limits the role to the `dev` environment on main, and the environment has never been applied (P46).

### P56. Instance role

`AmazonSSMManagedInstanceCore` can read every parameter in the account and `CloudWatchAgentServerPolicy` can write to any log group (AD-28, AD-29; story 7.5 pre-build security S7.5-9). Production replaces them with AWS's documented minimal Session Manager instance policy and a policy scoped to the one log group and X-Ray (AWS IAM, least privilege). *Why it waits:* the demo account is dedicated, so the account holds nothing else to read or write.

### P58. Third-party image scanning

The pinned collector image (`otel/opentelemetry-collector-contrib:0.164.0`) carries HIGH CVE-2026-116587 in go-jose v4.1.5, and CI's Trivy scans only the images the repo builds (story 7.4 security F1). Production scans every pinned third-party image on a schedule and upgrades when a fix ships. *Why it waits:* the vulnerable code (JWK parsing) is not reachable, because the collector has no JWT or OIDC extension and listens only on the private network, and no release with go-jose 4.1.6 or later exists yet.

### P59. Container network capabilities

In AWS the api trusts forwarded headers only from the gateway's pinned address, but containers keep Docker's default `NET_RAW` capability, so a compromised container on the Compose network could try ARP spoofing of that address (story 7.4 security re-run I1). Production drops `NET_RAW` on the internet-facing services (`cap_drop`), or runs each service in its own network namespace under an orchestrator. *Why it waits:* the only gain would be getting past the per-IP linking limit, and the per-login limit and the OTP lockout still hold.

### P60. Cross-origin headers checked in CI

ZAP's baseline keys its rules by plugin ID, so the triaged `90004 IGNORE` line (COEP and CORP) also hides a dropped `Cross-Origin-Opener-Policy`, which the gateway sets today; only `infra/tests/smoke.sh` and the regression spec `e2e/tests/scalar-reference.spec.ts` check it; the smoke check does not run in CI, and the spec runs in CI's `e2e-regression` job only on a pull request that touches a path in `scripts/regression-paths.list` (story 7.3 security F1). Production adds a header assertion to the CI journeys, or uses ZAP's Automation Framework, whose alert filters match alert references. *Why it waits:* the header is set by one template the smoke check covers, and a change to it shows in review.

### P61. Deploy secrets apart from the containers' credentials

On the AWS instance the internet-facing Api and the collector reach instance metadata (S7.4-8), so they hold the instance role, and that role reads and decrypts every deploy secret under the SSM path, the RDS master password and Zitadel's master key included (story 7.5 security S7.5-L1). Production gives the containers their own credentials and keeps the deploy secrets from them: an RDS-managed master password in Secrets Manager that the instance role cannot read, a key-ring delivery that needs no metadata access, or deleting the master password parameter after the first init. The same instance's `DOCKER-USER` exceptions are by source address, which a container with `NET_RAW` can spoof (S7.5-L2; see P59). The 7.10 PRR (E-F3) adds keeping the operations credentials out of the API's configuration (see P35) and scoping each component's SSM, KMS and telemetry permissions to its own resources (see P56; ASVS 5.0 V13.3.2). *Why it waits:* the demo account is dedicated and holds only synthetic data, and the README says plainly what a compromise of the Api or the collector exposes.

### P62. Instance egress

The AWS instance's security group allows HTTPS out to any address (Trivy AWS-0104), because the instance sits in a public subnet with no NAT gateway or proxy and pulls from GHCR, reaches SSM, CloudWatch, X-Ray and KMS, and calls CloudFront for the identity origin; PostgreSQL out goes only to the database's group. Production sends AWS traffic through interface VPC endpoints and the rest through an egress proxy with an allow-list of host names (GHCR, the release downloads), and drops the open rule. The 7.10 PRR (E-F4) adds a documented destination inventory that the proxy enforces, with named exceptions for the identity provider, AWS and the image registry (ASVS 5.0 V13.2.4, V13.2.5). *Why it waits:* each interface endpoint costs about USD 7 a month, GHCR has no fixed addresses, and the demo account is dedicated and holds only synthetic data (human decision, 2026-10-31). The OIDC handler's token backchannel (it posts `client_secret` and `code_verifier`) and the JWT bearer handler's metadata backchannel still follow redirects (ASVS 5.0 V15.3.2); production sets each `BackchannelHttpHandler` to a `SocketsHttpHandler` with `AllowAutoRedirect = false` (Microsoft Learn, `OpenIdConnectOptions.BackchannelHttpHandler`). *Why it waits:* their only peer is the configured Zitadel issuer over HTTPS, and the integration tests replace the backchannel, so no behaviour test proves it without a new test seam (story 7.13 refine, 2026-10-31).

### P63. State bucket encryption key

The Terraform state bucket uses S3-managed encryption (SSE-S3; Trivy AWS-0132), which meets story 7.5's "default encryption" but gives no key policy and no CloudTrail record of each decrypt. The state holds the origin-verify secret and the values Terraform writes to SSM. Production uses SSE-KMS with a customer-managed key whose policy admits only the deploy and operator roles. *Why it waits:* access to the bucket is already limited by IAM, public access is blocked and TLS is required, on a dedicated demo account (human decision, 2026-10-31).

### P64. Identity-provider password, MFA and upload policy

The hosted login runs the policies Zitadel is provisioned with: multi-factor authentication is optional (`infra/images/zitadel/verify.sh`), so a password alone regains a linked customer's access; the password policy keeps the provider's default symbol requirement; no context-specific password blocklist is defined; and no document lists every authentication and recovery path with its strength, throttling and lockout-abuse controls. The provisioning script's asset uploads have no documented formats, decompressed limits or malicious-file handling. Production requires multi-factor authentication for customer access on every sign-in and recovery path, replaces composition rules with length plus breached and context-specific password screening through the provider's supported password policy, keeps one authentication-path inventory checked against the pinned provider, and documents each upload surface (ASVS 5.0 V5.1.1, V6.1.1 to V6.1.3, V6.2.5, V6.2.11, V6.3.3; NIST SP 800-63B-4; 7.10 PRR B-F4, B-F5, B-F6, B-F7, B-F9). *Why it waits:* the demo holds only synthetic data, on a dedicated account, and the application is not yet deployed to AWS (P46; human decision 2026-10-31).

### P65. Identity-provider sessions

The Api's own session deadlines are explicit (`SessionTicketStore.cs`), but the joint browser and identity-provider session policy is not. After local expiry the Api sends an ordinary OIDC challenge, which an active identity-provider session can satisfy without new credentials; disabling a login or changing its factors does not end its browser sessions; and there is no session list for a customer and no per-login termination for an administrator. Production documents the full session policy (concurrency, lifetimes, re-authentication bounds), enforces authentication freshness with the OIDC `max_age` parameter and a checked `auth_time` claim (OpenID Connect Core 1.0), ends dependent browser sessions on the provider's lifecycle and revocation events, and offers audited session lists and termination (ASVS 5.0 V7.1.1 to V7.1.3, V7.3.1, V7.3.2, V7.4.2, V7.4.3, V7.4.5, V7.5.2, V7.6.1; 7.10 PRR C-F1, C-F3, C-F4, C-F5). *Why it waits:* the demo holds only synthetic data, on a dedicated account, and the application is not yet deployed to AWS (P46; human decision 2026-10-31).

### P66. Access-token purpose

The operations bearer validation accepts RS256 signatures only and restricts subjects, and project roles add a further check, but nothing establishes that the token is an access token rather than an ID token or another token from the same issuer (`OperationsAuthentication.cs`). Production validates the token's purpose with explicit typing (`typ` `at+jwt`, RFC 9068; RFC 8725, *Use Explicit Typing*) where the issuer supports it, or with mutually exclusive validation rules, and tests that ID tokens and other same-issuer tokens are refused (ASVS 5.0 V9.2.2; 7.10 PRR C-F9). *Why it waits:* no customer-to-operations escalation was shown, the demo holds only synthetic data, on a dedicated account, and the application is not yet deployed to AWS (P46; human decision 2026-10-31).

### P67. Service identity and internal transport

Behind the gateway, services trust the private Compose network. The core-banking stub has no authentication and accepts `X-Customer-Id` and resource IDs from any caller that reaches it; the Api's connections to the stub, to Mailpit's SMTP and to the collector are plaintext with long-lived shared credentials or none; and the broker has no client authentication (P21). Production gives each service its own identity with short-lived or certificate-based credentials (for example mutual TLS), encrypts every service-to-service hop, and passes the customer context to core banking in a form the bank's service can verify, with explicit permissions per caller (ASVS 5.0 V8.2.1, V8.2.2, V8.3.1, V12.2.1, V12.3.1, V12.3.3, V13.2.1; 7.10 PRR C-F7, D-F6, E-F1). The CloudFront-to-origin hop is P52. *Why it waits:* the stub serves only fixture customers, the demo holds only synthetic data, on a dedicated account, and the application is not yet deployed to AWS (P46; human decision 2026-10-31).

### P68. Key inventory and rotation

No inventory lists the keys and secrets v1 uses (the Data Protection key ring, the OTP HMAC key, the deploy-time secrets) with owner, permitted use, rotation and retirement, and the OTP HMAC key is a single unversioned value, so rotating it breaks live linking challenges (`LinkingOptions.cs`, `Otp.cs`). Production keeps that inventory and lifecycle policy (NIST SP 800-57 Part 1) and versions the OTP HMAC key so a rotation does not break live challenges (ASVS 5.0 V11.1.1, V11.1.2, V11.2.2; 7.10 PRR D-F1, D-F2). *Why it waits:* an OTP lives five minutes, so a rotation costs at most a restarted linking, the demo holds only synthetic data, on a dedicated account, and the application is not yet deployed to AWS (P46; human decision 2026-10-31).

### P69. Sensitive-data classification

The PRD and the architecture give individual rules (dispute events carry no personal data; stored events are never logged or traced), but no one matrix classifies each sensitive field and each copy of it: token claims, sessions, linking challenges, audit records, idempotency responses, telemetry and backups. Production keeps that matrix, with encryption, integrity, retention, access and privacy requirements for each protection level (ASVS 5.0 V14.1.1, V14.1.2, V14.2.4; POPIA; 7.10 PRR E-F5). *Why it waits:* the demo holds only synthetic data, on a dedicated account, and the application is not yet deployed to AWS (P46; human decision 2026-10-31).

### P70. Dependency inventory and remediation deadlines

CI scans dependencies and the images the repo builds on a schedule (`.github/workflows/security.yml`), but no policy sets remediation deadlines by severity and exploitability, a routine update cadence or exception expiry, and the inventory leaves out transitive packages, native libraries and the third-party images that are deployed (see P58). Production sets those deadlines and keeps a release-associated software bill of materials covering every deployed component, monitored for advisories (ASVS 5.0 V15.1.1, V15.1.2, V15.2.1; 7.10 PRR E-F9, E-F10). *Why it waits:* no specific overdue vulnerability is known, the demo holds only synthetic data, on a dedicated account, and the application is not yet deployed to AWS (P46; human decision 2026-10-31).

### P71. Security logging and audit collection

Audit records go only to the application database; the collector receives the Api's and the Worker's telemetry over plaintext OTLP but collects neither those records nor the proxy, identity, database and container logs; and no inventory lists each layer's events, destination, readers and retention. Authentication outcomes, authorisation denials, CSRF and validation failures and unexpected core-banking responses are not all recorded as security events. Production keeps a logging inventory per layer, records those events with sanitised actor and context details, and exports security audit events and stack logs over authenticated TLS to a separately administered store connected to alerting (ASVS 5.0 V16.1.1, V16.2.3, V16.3.1 to V16.3.4, V16.4.3; OWASP Logging Cheat Sheet; 7.10 PRR E-F13, E-F14, E-F15). *Why it waits:* the demo holds only synthetic data, on a dedicated account, and the application is not yet deployed to AWS (P46; human decision 2026-10-31).

### P72. Stack lock scope

`scripts/test-lock.sh run` releases the lock when its wrapped command ends, so separately locked bring-up and request commands let another lane take the stack between them; the documented workaround runs the whole check in one locked script. The upgrade adds separate acquire and release commands, so a lane holds the stack for its whole session (7.10 PRR F-F23). *Why it waits:* the one-locked-script workaround holds, and no contamination of recorded evidence was found (human decision 2026-10-31).

### P73. `no-store` on gateway errors raised before a location

Story 7.12 adds `Cache-Control: no-store` to the `/api` responses nginx generates in a location (429, 413, 500, 502, 504; AD-23 as amended 2026-10-31). nginx produces 400, 414 and 494, and the AWS template its origin-verify 403, before it selects a location, so a rule scoped to `/api` cannot reach them (7.10 finding F-F4). Production adds the header to those responses with a server-level rule keyed on the request path (nginx `map`), or confirms each body holds no customer data. *Why it waits:* these bodies are nginx's fixed error pages with no customer data, and the demo holds only synthetic data (story 7.12 refine, 2026-10-31).

### P74. Browser memory after the session ends

Closed (human decision 2026-11-03). Every session ends in the browser by a document navigation (`location.replace` to the session-ended screen, or the logout form post), which drops the old document's memory, the router's match cache and URL map included, so nothing stays to clear. Another tab that stays open is covered by P77.

### P75. Broker durability

The broker runs `--mode=dev-container` with one replica (AD-27; ad-010). That mode bypasses fsync, enables write caching and lets producers create topics, so an `acks=all` acknowledgement is not yet on disk; the relay marks a row `Sent` on that acknowledgement, so a broker crash before the flush can lose an event the outbox never resends ([`rpk redpanda start`](https://docs.redpanda.com/current/reference/rpk/rpk-redpanda/rpk-redpanda-start/); [Manage topics, write caching](https://docs.redpanda.com/current/develop/config-topics/)). Production runs Redpanda in production mode (fsync before each `acks=all` acknowledgement, write caching off) on at least three brokers with replication factor 3, and disables topic auto-creation (`auto_create_topics_enabled`), so only the topic the relay's host creates at start-up exists (AD-27). *Why it waits:* v1 holds only synthetic data on one node, and the memory budget of the AWS dev instance has no room for a second broker (ad-010).

### P77. Other tabs learn of a session end on focus

A tab learns that the session ended in another tab when it is brought to the front: its session query refetches when the tab becomes visible or its window gains focus (TanStack Query `refetchOnWindowFocus` with `focusManager.setEventListener` listening to both, human decision 2026-11-05), only while someone is signed in, and, on a 401 or `anonymous` answer, leaves by a document navigation. A window that stays visible beside another without gaining focus is not covered: it keeps its data on screen until its next request's 401 or its own deadline. Each visibility change or focus also renews a live session, because the session request is authenticated. ASVS 5.0 V14.3.1 (clear data at session end) is met for the leaving tab, and for other tabs on focus. Production can add a cross-tab message (P76). *Why it waits:* the uncovered window shows only what it showed while the session was live, the server refuses any request from it, and the demo holds only synthetic data (human decision 2026-11-04).

### P78. Go-jose and x/net in the Zitadel image

The `disputedesk-zitadel` image carries Zitadel's own binary byte for byte, and that binary is built with `github.com/go-jose/go-jose/v4` v4.1.4, which has HIGH CVE-2026-116587 (denial of service, fixed in 4.1.6); Zitadel v4.20.1, its latest release, still pins v4.1.4. Both image scans triage it with a dated entry, with an expiry, in `.github/trivyignore.yaml` (Trivy, *Filtering*: ignore file). Unlike the collector's copy (P58), it is likely reachable: Zitadel is the identity provider, so it parses JOSE objects from callers as part of its own OIDC and token endpoints. The same binary is built with `golang.org/x/net` v0.61.0, which has HIGH CVE-2026-78661 (denial of service in `x/net/html`, fixed in 0.62.0); Zitadel v4.20.1 and its main branch still pin v0.61.0, so it is triaged the same way (human decision 2026-11-07). Production upgrades Zitadel as soon as a release with go-jose 4.1.6 and x/net 0.62.0 or later ships, and removes both ignore entries with it. *Why it waits:* no fixed Zitadel release exists, building Zitadel from source is a fork to maintain, the impact is denial of service rather than disclosure, and the demo holds only synthetic data.

### P79. Relay and sweep inside the Api locally

Locally the outbox relay and the SLA sweep run inside the Api host (`Worker:InProcess` is `true`), and in AWS the Worker host runs them as a separate unit, an accepted difference between the two environments (NFR-ARC-2, AD-27). Both hosts register them through the same registration in Infrastructure, so the code that runs is the same; only the process differs, and locally an Api restart also restarts the relay and the sweep. Production runs them only in their own process, so the Api scales and restarts without them. *Why it waits:* the local stack fits in less memory with one .NET process, and the AWS environment, the one that mirrors production, already runs the Worker host on its own.

### P80. Zitadel's administrator token on an instance volume

The provisioning script authenticates with the personal access token Zitadel writes once, when its first instance is created, onto the `zitadel-bootstrap` volume. In AWS Zitadel's database lives on RDS but that volume lives on the EC2 instance, so replacing the instance while RDS survives loses the token for good: Zitadel never writes it again, provisioning fails with 401, and recovery is manual (locally, a new token for the provisioning user issued in the console by the administrator; in AWS, where every human administrator is deactivated (aws-admin-lockdown) and only the lost token could reactivate one, a fresh Zitadel database). Production keeps the provisioning credential in a secret store that outlives the instance, or provisions from a pipeline with its own credential. *Why it waits:* the application is not yet deployed to AWS (P46), and a local `docker compose down -v` removes both together.

### P81. Personal information outside South Africa

With `aws_region` set to `af-south-1` (Cape Town), everything that stores customer data stays in South Africa: RDS and its automated backups (one day, no cross-region copy), the instance and its volumes, and the CloudWatch logs and X-Ray traces (`infra/terraform/`). Two paths still leave the country. CloudFront answers from its global edge locations, so requests and responses, with the cookies and personal information in them, can pass through locations outside South Africa; it caches nothing (the `CachingDisabled` policy) and writes no access logs. And Terraform also accepts four regions outside South Africa (`us-east-1`, `us-west-2`, `eu-west-1`, `eu-central-1`), where all of it would be hosted abroad, a transfer under POPIA section 72. Production fixes the region to `af-south-1` and records the section 72 basis for the edge transit before any real data. *Why it waits:* the demo holds only synthetic data, and the environment has never been applied (P46).
