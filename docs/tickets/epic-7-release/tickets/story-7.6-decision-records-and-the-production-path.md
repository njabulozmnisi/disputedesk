---
id: 6
type: story
title: "Decision records and the production path"
parent: epic-7-release
covers: [R39]
after: []
hitl: false
risk: low
refined: false
---

# Decision records and the production path

## Description

Writes the seven missing decision records (ad-001, ad-002, ad-003, ad-005, ad-006, ad-007, ad-008; ad-004 and ad-009 exist and stay unchanged) and `docs/production-path.md`, written for a large retail bank with tens of millions of clients. The production path opens with a source-to-entry mapping, so its completeness is checkable: every PRD §6.2 bullet and sub-bullet, every row of the architecture's Deferred table and every `Input` line in this story's Notes maps to one entry. Docs only; it designs nothing (no jobs, distributed limiters or ordering systems), it records what is deferred and why.

## Requirements

Verify: the docs agent records in `docs/verification/7.6-check.md` (1) a count of PRD §6.2 bullets and sub-bullets, Deferred rows and Notes `Input` lines, each equal to the number of mapping rows citing that source kind, and (2) for each new record, that it has the five headings of criterion 1 and cites at least one AD; `git diff` shows ad-004 and ad-009 unchanged and no file outside `docs/decisions/`, `docs/production-path.md` and `docs/verification/7.6-check.md` changed.

Inputs: none at runtime (docs only). The text sources are fixed at the commit the story builds on: PRD §6.2, the architecture's Deferred table, and the `Input` and production-path `Decision` lines in Notes below.

1. **Seven records in one short form** (R39; NFR-DOC-3)
   **Given** `docs/decisions/`
   **When** it is read
   **Then** it holds ad-001 Zitadel as the external identity provider (AD-6), ad-002 `IAppDbContext` (AD-2), ad-003 Result types (AD-3), ad-005 QA data integrity (AD-30, the Gate 2 fresh-stack reset), ad-006 actor-agnostic dispute actions (AD-9, AD-13, AD-14a), ad-007 the BFF session with no tokens in the browser (AD-7) and ad-008 linking, including the SIM-swap check before the OTP is sent (AD-12), beside the unchanged ad-004 and ad-009
   **And** each new record has the headings Status, Context, Decision, Options rejected and Consequences (Nygard, *Documenting Architecture Decisions*, plus the options-rejected section ad-004 and ad-009 use), cites the AD numbers it records, and is at most about one screen (60 lines)

2. **ad-007 states its one exception** (R39; NFR-DOC-3, AD-7)
   **Given** ad-007
   **When** it is read
   **Then** it says the browser never holds an access or refresh token, and names the one exception plainly: the ID token crosses the browser once, in the end-session redirect as `id_token_hint` (OpenID Connect RP-Initiated Logout 1.0), already spent and short-lived, because without it Zitadel's session outlives sign-out
   **And** it records that linking keeps the session key minted at sign-in (decided 2026-10-30, story 6.2) and points at the production-path entry for renewal at a privilege change

3. **ad-008 records the guess bound as it is** (R39; NFR-DOC-3, FR-4, FR-7, FR-8, AD-12)
   **Given** ad-008
   **When** it is read
   **Then** it states plainly that the controls bound OTP guesses per challenge (lockout after three wrong codes, FR-7) and per login window (the per-login and per-IP linking rate limits, FR-8, held in memory in the one Api instance and reset on restart), and that the send cap of three OTPs per customer per hour (FR-4) limits codes issued, not verification attempts
   **And** it states that this is not the account-wide limit on failed attempts that NIST SP 800-63B describes (SP 800-63B-4, *Rate Limiting (Throttling)*), because restarting linking creates a fresh challenge with 0 attempts; it does not call the existing controls an overall bound on guesses
   **And** it names the upgrade and points at its production-path entry: a persistent per-customer failure counter that survives restarts and new challenges; no control changes in v1

4. **The production path opens with a checkable mapping** (R39; NFR-DOC-2)
   **Given** `docs/production-path.md`
   **When** its mapping table (columns: source, entry, one-line reason) is read
   **Then** each PRD §6.2 bullet is one source row, and each sub-bullet of "Operations features" (dead-letter topic, outbox cleanup, topic retention tuned to POPIA, listing and search for operations tools) is its own source row
   **And** every row of the architecture's Deferred table is a source row: either it maps to an entry, or it is marked "settled in v1" with the story or decision that settled it (for example "Copy for each linking state", story 6.4; "README runbook for failed outbox rows", story 7.7)
   **And** every `Input` line and every production-path `Decision` line in this story's Notes is a source row mapped to an entry, so none is missed (for example the `IdempotencyKeys.ResponseBody` retention, trace-context trust, credential placement, the route rate limits and audit cancellation)
   **And** one entry may cite several sources where they overlap (for example §6.2 "Retention and de-identification jobs", the Deferred "Outbox cleanup, topic retention, retention and de-identification jobs" and the `ResponseBody` input); no source maps to two entries; every entry has a one-line reason

5. **Context, estimates and the named entries** (R39; NFR-DOC-2, NFR-DATA-7, ad-009)
   **Given** the production path's entries
   **When** they are read
   **Then** its context line is "a large retail bank with tens of millions of clients", and every scale figure is labelled as an estimate
   **And** it holds the stored-event entries of ad-009: order across disputes (Kafka offsets, a high-water mark with `pg_snapshot_xmin`, or change data capture), crypto-shredding of personal data in stored events, stored event schema evolution beyond additive changes, snapshots to shorten replay, and a database-level append-only grant on `AuditLogEntries` and `StoredEvents`
   **And** it holds the OTP guess bound upgrade of criterion 3 and, from the 2026-10-27 decisions, migrations as a bundle run as a one-off job and problem `type` URLs moved to an owned domain

6. **The idempotency claim is a proposal, not the current mechanism** (R39; NFR-DOC-2, AD-16)
   **Given** the idempotency entry
   **When** it is read
   **Then** it states the current mechanism first: `IdempotencyKeyClaims.ClaimAsync` claims with PostgreSQL `INSERT … ON CONFLICT`, as AD-16 specifies
   **And** it frames the replacement as a proposed future change, not built: EF Core alone, through a unique constraint plus EF Core's documented optimistic concurrency, with explicit handling of the insert conflict (`DbUpdateException` on the unique index) and of the lease and stored response, as `docs/research/ef-core-upsert/ef-core-upsert.md` recommends; it does not call it an EF Core upsert (EF Core has none) or an established guarantee; AD-16 changes only when it is built

## Boundaries

- Docs only: no code, configuration or architecture change. ad-004 and ad-009 are not edited.
- The production path records deferrals and their reasons; it does not design the deferred mechanisms beyond one or two sentences each.
- The outbox and having no schema registry stay in the architecture spine (AD-14, AD-17, AD-19), not as records (Gate 2).

## Supervisor checks

- Pick three `Input` lines at random from Notes; each has its mapping row and entry.
- Read ad-008: it contains no claim that the controls bound guesses account-wide.

## Build facts

- Owner: docs (docs agent, not a builder).
- Flags: none.
- Lane: docs.
- Mechanisms and sources: architecture decision records (Michael Nygard, <https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions>); NIST SP 800-63B-4, rate limiting (<https://pages.nist.gov/800-63-4/sp800-63b.html>); OpenID Connect RP-Initiated Logout 1.0 (<https://openid.net/specs/openid-connect-rpinitiated-1_0.html>); EF Core concurrency conflicts (<https://learn.microsoft.com/ef/core/saving/concurrency>).
- Packages: none.
- Touches: `docs/decisions/ad-001` to `ad-008` (except `ad-004`); `docs/production-path.md` (new); `docs/verification/7.6-check.md` (new).

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- prd — docs/prd/prd.md, §6.2, NFR-DOC-2, NFR-DOC-3, FR-4, FR-7, FR-8
- architecture — docs/architecture/architecture.md, AD-2, AD-3, AD-6, AD-7, AD-9, AD-12, AD-13, AD-14a, AD-16, AD-30, Deferred
- decisions — docs/decisions/ad-004-model-roles.md, docs/decisions/ad-009-stored-events.md
- research — docs/research/ef-core-upsert/ef-core-upsert.md
- verification — docs/verification/7.6-challenge.md

## Notes

- Decision: 2026-10-26 — Gate 2: the records follow kickoff amendment 1 (ad-001 to ad-008 as listed in criterion 1); the substitute topics chosen at slicing are dropped. The outbox and having no schema registry are recorded in the architecture spine (AD-14, AD-17, AD-19), not as separate records.
- Decision: 2026-10-26 — Gate 2: owner changes to `docs`, a docs agent rather than a builder.
- Decision: 2026-10-27 — standards audit: the production path records migrations as a migration bundle run as a one-off job instead of every replica migrating at startup, and the deferred operations stories (see the initiative's scope decision).
- Decision: 2026-10-27 — ad-008 records the NIST SP 800-63B deviation on restarted challenges and the bound that replaces it (criterion 1).
- Decision: 2026-10-27 — 2.7 security review: the production path records that problem `type` URLs live under the GitHub namespace, an accepted risk (rename, deletion or repo-jacking would break or hijack them); production moves them to an owned domain.
- Input (from 3.1, 2026-10-28): add to `docs/production-path.md`: "Currency: core banking is assumed to return the ZAR billed amount (AD-4, FR-14), and the dispute snapshot stores the currency as received, with no check (`// shortcut:` in `Dispute.cs`). Before a non-ZAR source is connected, raise must reject, or convert to the ZAR billed amount, any transaction whose currency is not ZAR."
- Input (from 3.1 security S3, 2026-10-28): add to `docs/production-path.md`: "Trace context: the browser's `traceparent` and `tracestate` are accepted as the request's parent, so the audit `TraceId` and the outbox trace columns are caller-chosen. Production restarts or validates trace context at the gateway, the trust boundary (W3C Trace Context, security considerations)." Also: "Joint accounts: an `accountRef` is assumed to belong to one customer; with joint accounts a co-owner's raise gets 409 and learns another owner's dispute exists."
- Input (from 2.5 security F4, 2026-10-28): add to `docs/production-path.md`: "Gateway filesystem: `/etc/nginx` is writable by the nginx user, because `nginx-unprivileged` renders its templates there at start. Production runs the gateway with a read-only root filesystem and a tmpfs render directory (CIS Docker 5.12)."
- Input (from 2.5 review L1, 2026-10-28): add to `docs/production-path.md`: "Gateway base image: `infra/nginx/Dockerfile` runs an unpinned `apk upgrade` of brotli-libs, curl and libcurl (`// shortcut:`) because the pinned `nginx-unprivileged` base lags the Alpine fixes. Drop it when a newer pinned base ships them; consider removing curl (unused at runtime) or the `-alpine-slim` variant."
- Input (from 3.2 security S2, 2026-10-28): `IdempotencyKeys.ResponseBody` keeps a copy of the customer's dispute view; expiry applies only when a key is reused, so expired rows are never deleted and an erasure on the dispute would miss them (ASVS 4.0.3 V8.3.8). Production path: a retention job deleting expired keys.
- Input (from 4.1 security S4, human decision 2026-10-29): add to `docs/production-path.md`: "Kafka transport: the broker runs plaintext with no client authentication on the private Compose network in v1 (a `// shortcut:` in the relay's producer configuration). Before a shared network, enable TLS plus SASL/SCRAM or mTLS on the broker, with topic ACLs so only the relay principal writes `disputedesk.disputes`."
- Input (from 3.4 pre-build review, 2026-10-30): add to `docs/production-path.md`: "Error-rate alerts: EF Core logs a unique violation (SQL state 23505) at Error even when the application expects and answers it. Alerting excludes 23505 on `UQ_Disputes_AccountRef_TransactionId` (a lost raise race, 409 `already-disputed`) and `UQ_StoredEvents_DisputeId_Version` (a version clash, AD-15); the logs are not filtered, because an EF Core filter would hide every failed save."
- Input (from 3.4 security S9, 2026-10-30): add to `docs/production-path.md`: "Customer route rate limit: each refused withdraw (404) writes an audit row, and customer routes have no rate limiter, so a linked customer looping over random IDs adds one row per request. Production adds a per-login limiter on unsafe customer routes with the built-in rate limiter, as AD-12 does for linking (Microsoft Learn, *Rate limiting middleware*; ASVS 5.0 V2.4.1)."
- Decision: 2026-10-29 — the production path is written for a large retail bank with tens of millions of clients, with scale figures labelled as estimates (architecture Deferred); the EF-only idempotency claim is a production-path item.
- Input (from 5.1 pre-build review and security, 2026-10-30): add to `docs/production-path.md`: "Operations tool credentials: in v1 `scripts/operations-token.sh` runs the client-credentials grant with the machine user's shared secret and a person pastes the token into Scalar, which holds no credential. Real operations tools keep their own credentials in a secret store and authenticate with `private_key_jwt` (RFC 7523) instead of a shared secret; the access-token lifetime is set deliberately on the machine user (Zitadel's default in v1)."
- Input (from 5.1 security S3, 2026-10-30): add to `docs/production-path.md`: "API viewer origin: Scalar is served on the customers' app origin under its own CSP; production serves any API viewer from a separate origin, so no script on it can reach the session cookie's routes."
- Input (from 5.1 decision, 2026-10-30): add to `docs/production-path.md`: "Operations secret placement: the machine user's secret sits in `zitadel-clients.json`, which the Api container mounts, though the Api never reads it. Production gives each operations tool its own credential store and the Api none."
- Input (5.1 security F1, 2026-10-30): the Api loads all of `zitadel-clients.json`, which also holds the operations tools' client secret it never uses; production keeps tool credentials in a store or file the Api cannot read (least privilege, ASVS 5.0 V13.3).
- Input (from 5.2 pre-build review, 2026-10-30): add to `docs/production-path.md`: "Operations route rate limit: each refused operations action writes a `Denied` or `Rejected` audit row, and the operations routes have no rate limiter, so a token holder looping over random IDs adds one row per request. Production adds a per-client limiter on the operations routes with the built-in rate limiter (Microsoft Learn, *Rate limiting middleware*; ASVS 5.0 V2.4.1)."
- Input (from 5.4 security S4, 2026-10-30): add to `docs/production-path.md`: "Seeding in a real deployment: `Seed:Enabled` defaults to off and is set only in Compose and the AWS demo (AD-26), but nothing refuses it at start-up. A real deployment refuses `Seed:Enabled` at start-up, so seeded events are never published for fixture customer IDs (ASVS 5.0 V13, configuration)."
- Input (4.2 review, 2026-10-30): librdkafka error events are logged per occurrence (about 62 warnings per 30 s outage); production logs on state change or rate-limits per code. An unlisted error code on one dispute's head row stalls the whole relay; production defers per row and alerts on the deferred-warning rate.
- Input (from 3.11 security T7, 2026-10-30): add to `docs/production-path.md`: "Inbound trace context: the Api trusts any caller's `traceparent` under OpenTelemetry .NET's default ParentBased(AlwaysOn) sampler (no sampler is set; unverified on the stack), so a caller can send flags `00` to suppress the Api's spans for its own requests and choose the trace ID stored in `StoredEvents.TraceId` and the outbox. Production sets a sampler that does not follow an untrusted parent's sampled flag at the edge, or restarts the trace there (W3C Trace Context, section 8)."
- Input (from 3.11 security T8, 2026-10-30): add to `docs/production-path.md`: "Browser telemetry intake: `/otlp/` is an anonymous write path; any client can post forged spans under any `service.name`, up to 1 MB per request within the coarse rate-limit zone, to the Dashboard and, in AWS, to X-Ray. Production filters browser spans at the collector by `service.name` (`disputedesk-web`) and an attribute allow-list."
- Input (2.6 QA, 2026-10-30): the hosted login has no favicon (404) and shows Zitadel's default copy ("Create your Zitadel account.", "Welcome back!") instead of the mockups' "Sign in to DisputeDesk"; Login v2 text customisation is a later polish item.
- Input (from 6.5 refine, 2026-10-30): add to `docs/production-path.md`: "Send cap and delivery: every claimed OTP send counts toward the three-per-hour cap, delivered or not, so a customer hit by SMS delivery failures waits for the window. Production reads the SMS provider's delivery receipts to tell a failed delivery from an uncertain one. The cap count has no index on (`CustomerId`, `OtpSentAtUtc`); production adds one once `LinkingChallenges` outgrows a sequential scan."
- Input (from 6.3 refine, 2026-10-30): add to `docs/production-path.md`: "Linking rate limits are held in memory on the single Api instance, so they reset on restart and do not add up across instances; scale-out needs a shared limiter store. The Api trusts forwarded headers from every container on the Compose network; production trusts only the load balancer's or gateway's own addresses."
- Input (from 6.2 decision, 2026-10-30): add to `docs/production-path.md`: "Session key at linking: linking keeps the session key minted at sign-in, since the built-in cookie handler with a server-side ticket store cannot change the key within one request. Production renews the key at every privilege change (OWASP Session Management Cheat Sheet) once the handler supports it, or moves to a session store that re-keys."
- Input (from 5.2 review and security, 2026-10-30): add to `docs/production-path.md`: "A wrong method or an unmatched path under `/api/v1/operations` reaches the framework's 405 or 404 endpoint, which carries no policy, so the cookie-only fallback answers bearer callers with 401 and no `Bearer` challenge (RFC 6750 §3); it fails closed and runs no handler. Production gives the operations group its own fallback answer. Audit saves for `Denied` and `Rejected` use the request's cancellation token, so a client that disconnects mid-request can cancel its own audit row; production saves audit rows with `CancellationToken.None` (OWASP Logging Cheat Sheet)."
- Input (from 6.6 security, 2026-10-30): add to `docs/production-path.md`: "Request lines in nginx logs: the identity origin's access log records only the path, but its `error_log` still writes the request line with `id_token_hint` on an upstream error, and the app origin's combined access log keeps query strings (the dev-only GET sign-in callback's single-use code). Production logs paths only on both origins and lowers the identity origin's error-log detail. The Api trusts forwarded headers from the whole Compose network, which on a Linux Docker host also includes the host's bridge address; production trusts only the load balancer's or gateway's own addresses (ASVS 5.0 V4.1.3, V16.2.5)."
- Input (from the start-up exit fix, 2026-10-30): add to `docs/production-path.md`: "Containers run an init process as PID 1 (Compose `init: true`), because the kernel drops signals PID 1 has no handler for, so a .NET process that fails to start would spin instead of exiting (pid_namespaces(7)). Any deployment outside Compose needs the same: ECS `linuxParameters.initProcessEnabled`, or an init in the image."
- Risk low (proposed): docs only; an overstated security claim misleads a bank reading the records, so the supervisor checks ad-008's wording beyond the criteria.
- Decision: 2026-10-30 (human, 7.6 challenge) — ad-008 records plainly that the controls bound OTP guesses per challenge and per login window, not account-wide as NIST SP 800-63B describes, and the production path names the upgrade: a persistent per-customer failure counter. No control change (criteria 3 and 5). This replaces the 2026-10-27 wording "the real bound on guesses is the per-login linking rate limit plus the send cap".
- Decision: 2026-10-30 (Sofia, refine) — the production path's completeness is checked through a source-to-entry mapping (criterion 4); every `Input` line in these Notes stays here, one line each, as the inventory the mapping counts.
- Input (from 7.4 refine, 2026-10-30): add to `docs/production-path.md`: "Browser telemetry in a deployed environment: the AWS gateway routes no `/otlp/` and the SPA built for AWS exports nothing, so browser spans are a local-stack feature. Before switching it on, add an authenticated or sampled intake (for example behind the BFF session with per-session limits) and filter browser spans at the collector."
- Input (from 7.5 refine, 2026-10-30): add to `docs/production-path.md`: "The AWS dev environment is written and checked with `terraform fmt`, `validate` and plans against mocked providers only, and has never been applied, because the account does not exist. Before any real use, run `infra/terraform/README.md`'s apply, deploy, check and destroy procedure, and move Terraform's two state-held secrets (the origin-verify value and the RDS master password) to write-only arguments or ephemeral resources."
- Input (from 7.7 refine, 2026-10-30): add to `docs/production-path.md`: "A `Failed` outbox row is released by an operator's targeted `UPDATE` (the runbook, AD-22's one operator exception). A row whose payload itself is refused cannot be released, because the payload is never edited; production adds a reviewed repair path or a dead-letter topic with an audited replay."
- Input (from 6.3 security, 2026-10-30): add to `docs/production-path.md`: "One over-limit login rotating IPv6 /64 prefixes writes one audit row per rejection, and the gateway's `limit_req` keys on the full address; production keeps limits in a shared store (with the in-memory limits entry) and keys the gateway on the /64."
- Input (from 5.3 security L4, 2026-10-30): add to `docs/production-path.md`: "The SLA sweep claims the 50 earliest-due disputes each loop; if 50 or more keep failing (for example a stream that will not replay), later disputes are never escalated (FR-26) although each failure is logged at Error. Production alerts on repeated sweep failures and parks a dispute that fails N times."
- Input (from 7.4 pre-build security S7.4-9 and 7.5 pre-build security S7.5-11, human decision 2026-10-30): add to `docs/production-path.md`: "Edge transport: CloudFront reaches the AWS demo's origin over plain HTTP on port 80 (a `# shortcut:` in Terraform), accepted for the free-plan demo with synthetic data; the default `*.cloudfront.net` certificate keeps the minimum viewer protocol at TLS 1.0, and there is no WAF. Production uses an HTTPS origin or CloudFront VPC origins, its own domain with an ACM certificate and a TLS 1.2 minimum viewer protocol, and AWS WAF in front of every distribution (ASVS 5.0 V12.1.1, V12.3.1)."
- Input (from 7.4 pre-build security S7.4-11, human decision 2026-10-30): add to `docs/production-path.md`: "Demo logins in AWS: the fixture logins of customers A, B and C, whose passwords are published in `fixtures/local.json`, are live on the AWS demo, and self-registration lets anyone link to customer D; accepted because the demo holds only synthetic data. A real environment has no fixture logins and refuses the seeder at start-up (see the seeding entry)."
- Input (from 7.7 pre-build security S7.7-2, human decision 2026-10-30): add to `docs/production-path.md`: "Outbox release role: the runbook's release of a `Failed` row runs as the owner role `disputedesk`, which has full DML, and writes no audit record. Production gives operators a dedicated role with only column-level `GRANT UPDATE ("Status", "Attempts", "NextAttemptAtUtc") ON "OutboxMessages"` and records each release in the audit log (ASVS 5.0 V8, V16.3)."
- Input (from 7.3 pre-build review 3, 2026-10-30): add to `docs/production-path.md`: "Build once, promote by digest: CI's `publish` job rebuilds the images on main rather than pushing the bytes the e2e job tested (it scans each one with Trivy before pushing), and the gateway's `apk upgrade` is unpinned, so the pushed gateway can differ from the tested one. Production builds each image once, tests and scans that digest, and promotes the same digest."
- Input (from 7.3 pre-build security S7.3-2, 2026-10-30): add to `docs/production-path.md`: "Build provenance: published images carry the OCI `source` and `revision` labels only; production adds SLSA build provenance attestations, verified at deploy (it needs a new action, so it is out of v1)."
- Input (from 7.5 pre-build security S7.5-7, 2026-10-30): add to `docs/production-path.md`: "Deploy role subject: the OIDC trust pins the repository's main branch, not the workflow; production customises the subject claim to include `job_workflow_ref`, so only `deploy.yml` on main can assume the role (GitHub, *Customizing the token claims*)."
- Input (from 7.5 pre-build security S7.5-9, 2026-10-30): add to `docs/production-path.md`: "Instance role: `AmazonSSMManagedInstanceCore` can read every parameter in the account and `CloudWatchAgentServerPolicy` can write to any log group, accepted because the demo account is dedicated. Production replaces them with AWS's documented minimal Session Manager instance policy and a policy scoped to the one log group and X-Ray (AWS IAM, least privilege)."
- Input (from 7.4 pre-build security S7.4-1, human decision 2026-10-30): Mailpit in AWS now has its own origin, a third CloudFront distribution (AD-28 amended), so `docs/production-path.md`'s edge-caching entry reads "All three distributions use `CachingDisabled`" instead of "Both distributions".
