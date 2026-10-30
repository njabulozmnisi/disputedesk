---
id: 6
type: story
title: "Decision records and the production path"
parent: epic-7-release
covers: [R39]
after: []
hitl: false
risk: low
---

# Decision records and the production path

## Description

Writes decision records ad-001 to ad-008 (ad-004 exists; ad-009 stored events exists) and `docs/production-path.md` with every prd §6.2 deferral and every spine Deferred item, each with a reason, written for a large retail bank with tens of millions of clients; docs only.

## Requirements

Verify: Each new record named below follows the Nygard ADR form (status, context, decision, consequences) plus options rejected; every prd §6.2 bullet appears in `docs/production-path.md` with a one-line reason.

1. **Decision records** (R39; NFR-DOC-3)
   **Given** `docs/decisions/`
   **When** it is read
   **Then** it holds ad-001 Zitadel as the external identity provider, ad-002 `IAppDbContext`, ad-003 Result types, ad-004 model roles (exists, unchanged), ad-005 QA data integrity, ad-006 actor-agnostic dispute actions, ad-007 the BFF session (no tokens in the browser), ad-008 linking (including the SIM-swap check before the OTP) and ad-009 stored events (exists, unchanged), each citing the AD numbers it records
   **And** ad-008 records the deviation from NIST SP 800-63B's limit on failed attempts: restarting linking creates a fresh challenge with 0 attempts, so the real bound on guesses is the per-login linking rate limit (FR-8) plus the send cap of 3 OTPs per customer per hour (FR-4)

2. **Production path** (R39; NFR-DOC-2, NFR-DATA-7)
   **Given** `docs/production-path.md`
   **When** it is read
   **Then** every prd §6.2 item and every spine Deferred item appears once with a one-line reason, including retention and de-identification jobs
   **And** its context is "a large retail bank with tens of millions of clients", and every scale figure in it is labelled as an estimate
   **And** it holds the stored-event items of ad-009 and the spine's Deferred table: order across disputes (Kafka offsets, a high-water mark with `pg_snapshot_xmin`, or change data capture), crypto-shredding of personal data in stored events, stored event schema evolution beyond additive changes, snapshots to shorten replay, and a database-level append-only grant on `AuditLogEntries` and `StoredEvents`
   **And** it holds the EF-only idempotency claim: replacing the raw-SQL claim (`IdempotencyKeyClaims.ClaimAsync`, `INSERT … ON CONFLICT`) with EF Core alone, as `docs/research/ef-core-upsert/ef-core-upsert.md` recommends, with AD-16's statement updated when it is built

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- None beyond the story flow.

## Build facts

- Owner: docs (docs agent, not a builder).
- Flags: none.
- Lane: docs.
- Touches: docs/decisions/ad-001 to ad-008 (except ad-004); `docs/production-path.md`.

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md

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
