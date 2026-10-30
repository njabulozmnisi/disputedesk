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
