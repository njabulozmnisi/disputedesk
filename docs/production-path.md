# Production path

Context: a large retail bank with tens of millions of clients. That count is an estimate, and every other scale figure in this document is an estimate labelled as one (architecture, Deferred; story 7.6 Notes, decision 2026-10-29).

This document records what DisputeDesk v1 defers and why (NFR-DOC-2). It designs none of the deferred mechanisms: each entry says what v1 does, what production does instead, and why it can wait, in a few sentences. Entries are grouped by theme; numbers stay stable, so they are not in order. Decision records are in `docs/decisions/`; the architecture spine is `docs/architecture/architecture.md`.

## Source-to-entry mapping

Every PRD §6.2 bullet, every "Operations features" sub-bullet, every row of the architecture's Deferred table, and every `Input` line and production-path `Decision` line in the Notes of story 7.6 is one row below. One entry may cite several sources; no source maps to two entries. A Deferred row that v1 already settles is marked "settled in v1" with the story that settles it.

| Source | Entry | One-line reason |
| --- | --- | --- |
| PRD §6.2: Provisional credit | P1 | A US Regulation E norm with no documented South African equivalent |
| PRD §6.2: Attachments | P2 | Out of scope for v1 |
| PRD §6.2: Customer notifications on status changes | P3 | Mailpit carries only verification, password reset and the OTP in v1 |
| PRD §6.2: Customer self-unlink, and lost-login recovery | P4 | Both are bank-assisted in production |
| PRD §6.2: Partial disputes other than wrong amount | P5 | Out of scope for v1; wrong amount is the one partial case |
| PRD §6.2: Scheme-specific windows per reason | P6 | v1 uses one 90-day window from the transaction date |
| PRD §6.2: A separate fraud route for unrecognised transactions | P7 | Out of scope for v1, though common in South African practice |
| PRD §6.2: A schema registry | P15 | Committed JSON Schemas and a contract test cover v1's two types |
| PRD §6.2: An event consumer in this repo | P16 | The contract already requires consumers to dedupe |
| PRD §6.2: Operations features | P34 | Operations work happens in another team's tool |
| PRD §6.2, Operations features: a dead-letter topic | P20 | A `Failed` row stops its dispute until an operator releases it |
| PRD §6.2, Operations features: outbox cleanup | P23 | Nothing is deleted in v1 |
| PRD §6.2, Operations features: topic retention tuned to POPIA | P23 | Retention is one decision across tables and the topic |
| PRD §6.2, Operations features: listing and search for operations tools | P34 | v1 offers read by ID only (FR-30) |
| PRD §6.2: A general support platform for several departments, and a UI for AI agents | P8 | DisputeDesk is not a case-management system |
| PRD §6.2: Follow-up messages from the customer after raising a dispute | P9 | Out of scope for v1 |
| PRD §6.2: Retention and de-identification jobs | P23 | v1 deletes nothing; the five-year FIC Act period has not run |
| PRD §6.2: Crypto-shredding of personal data in stored events | P24 | Stored events are kept under NFR-DATA-7 with the same access as `Disputes` |
| PRD §6.2: Event order across disputes | P17 | Nothing reads across streams in v1 |
| PRD §6.2: Scale-out | P33 | v1 runs one Api and one Worker instance |
| PRD §6.2: Filters and search on transactions | P10 | Out of scope for v1; the list is keyset-paged |
| PRD §6.2: Languages other than English, and currencies other than ZAR | P11 | v1 serves one language and one currency |
| PRD §6.2: The bank-scale end state | P13 | Depends on the bank's identity platform and downstream systems |
| Deferred: Copy for each linking state | settled in v1 | Story 6.4 writes the copy; AD-3 and AD-12 fix the wire contract |
| Deferred: Exact relay batch size and backoff curve | P19 | Tuning; AD-17 fixes the behaviour |
| Deferred: CloudFront caching for static assets | P47 | Performance only; caching stays disabled |
| Deferred: Database-level append-only `AuditLogEntries` and `StoredEvents` | P27 | AD-14 enforces insert only in the application |
| Deferred: Order across disputes | P17 | Order is per dispute by `Version` (AD-17, ad-009) |
| Deferred: Crypto-shredding of personal data in stored events | P24 | ad-009 defers it |
| Deferred: Stored event schema evolution beyond additive changes | P25 | AD-14a allows only additive changes; a new shape is a new type |
| Deferred: Snapshots at intervals to shorten replay | P26 | Streams are short in v1 |
| Deferred: Operations tool credentials (stories 5.1, 5.5) | P35 | Scalar holds no credential; a person pastes the token |
| Deferred: Shared rate-limit state, several Api and Worker instances | P33 | Out of scope (PRD §6.2) |
| Deferred: An event consumer and its inbox table | P16 | Out of scope; the contract requires dedupe |
| Deferred: Fencing a relay leader with Kafka transactions | P18 | v1 is at-least-once with per-dispute order by `disputeVersion` |
| Deferred: Kafka broker authentication and TLS | P21 | Plaintext on the private Compose network, a `// shortcut:` |
| Deferred: Outbox cleanup, topic retention, retention and de-identification jobs | P23 | Nothing is deleted in v1 |
| Deferred: Seeded snapshots after a day boundary without `down -v` | P40 | Demo limitation; the README says to reset |
| Deferred: CloudFront prefix-list weight against security-group quotas | P46 | Checked once the AWS account exists |
| Deferred: Node 26 LTS move | P48 | `.nvmrc` stays on 24 until a story bumps it |
| Deferred: README runbook for failed outbox rows; the decision records | settled in v1 | Stories 7.7 (runbook) and 7.6 (decision records); the release is AD-22's operator exception |
| Deferred: Browser telemetry intake in AWS | P42 | Browser spans are local only (AD-29) |
| Input (3.1): currency | P11 | Core banking is assumed to return ZAR |
| Input (3.1 security S3): trace context; joint accounts | P41 | Trace context is caller-chosen; its joint-accounts sentence is written in P12 |
| Input (2.5 security F4): gateway filesystem | P44 | The base image renders templates into `/etc/nginx` |
| Input (2.5 review L1): gateway base image | P44 | The pinned base lags the Alpine fixes |
| Input (3.2 security S2): `IdempotencyKeys.ResponseBody` retention | P23 | Expired keys are never deleted in v1 |
| Input (4.1 security S4): Kafka transport | P21 | Private Compose network in v1 |
| Input (3.4 pre-build review): error-rate alerts | P22 | EF Core logs expected unique violations at Error |
| Input (3.4 security S9): customer route rate limit | P37 | Only linking routes have a limiter in v1 |
| Input (5.1 pre-build review and security): operations tool credentials | P35 | v1 uses a shared secret through a script |
| Input (5.1 security S3): API viewer origin | P36 | Scalar shares the app origin in v1 |
| Input (5.1 decision): operations secret placement | P35 | The Api container mounts a file it never reads |
| Input (5.1 security F1): the Api loads all of `zitadel-clients.json` | P35 | Least privilege for tool credentials |
| Input (5.2 pre-build review): operations route rate limit | P37 | Only linking routes have a limiter in v1 |
| Input (5.4 security S4): seeding in a real deployment | P40 | Nothing refuses `Seed:Enabled` at start-up |
| Input (4.2 review): librdkafka error logging and head-row stalls | P19 | Relay tuning and alerting |
| Input (3.11 security T7): inbound trace context | P41 | The default sampler follows an untrusted parent |
| Input (3.11 security T8): browser telemetry intake | P42 | `/otlp/` is an anonymous write path locally |
| Input (2.6 QA): hosted login branding | P14 | Polish item |
| Input (6.5 refine): send cap and delivery | P31 | Every claimed send counts, delivered or not |
| Input (6.3 refine): in-memory linking rate limits; forwarded-header trust | P33 | One Api instance in v1 |
| Input (6.2 decision): session key at linking | P32 | The built-in handler cannot re-key within one request |
| Input (5.2 review and security): operations fallback; audit cancellation | P38 | Fails closed today; production tidies both edges |
| Input (6.6 security): request lines in nginx logs; forwarded-header trust | P43 | Local and dev edge only in v1 |
| Input (start-up exit fix): init process as PID 1 | P45 | Compose sets it; other runtimes need the same |
| Input (7.4 refine): browser telemetry in a deployed environment | P42 | No intake in AWS in v1 |
| Input (7.5 refine): the AWS dev environment never applied | P46 | The account does not exist |
| Input (7.7 refine): releasing a `Failed` outbox row | P20 | A refused payload cannot be released |
| Input (6.3 security): IPv6 /64 rotation | P33 | Limits are in memory and the gateway keys on the full address |
| Input (5.3 security L4): SLA sweep head-of-line failures | P49 | 50 or more failing disputes stall later escalations |
| Input (7.4 and 7.5 pre-build security, human decision): edge transport | P52 | Plain HTTP origin, TLS 1.0 minimum and no WAF, accepted for the demo |
| Input (7.4 pre-build security S7.4-11, human decision): demo logins in AWS | P50 | Fixture logins are live on a demo holding only synthetic data |
| Input (7.7 pre-build security S7.7-2, human decision): outbox release role | P51 | The release runs as the owner role and writes no audit record |
| Input (7.3 pre-build review 3): build once, promote by digest | P53 | `publish` rebuilds the images on main |
| Input (7.3 pre-build security S7.3-2): build provenance | P54 | Images carry OCI labels only; attestations need a new action |
| Input (7.5 pre-build security S7.5-7): deploy role subject | P55 | The OIDC trust pins the branch, not the workflow |
| Input (7.5 pre-build security S7.5-9): instance role | P56 | Broad AWS managed policies on a dedicated demo account |
| Decision 2026-10-30 (human, 7.4 build): published api and worker images | P57 | They carry a busybox probe for the Compose health checks |
| Input (7.4 security F1): collector image CVE | P58 | Not reachable in this configuration; no fixed release yet |
| Input (7.4 security re-run I1): layer-2 spoofing on the Compose network | P59 | Only the per-IP linking limit is at stake; the per-login limit holds |
| Input (7.4 pre-build security S7.4-1, human decision): three distributions | P47 | Mailpit has its own distribution; all three disable caching |
| Decision 2026-10-27 (standards audit): migrations; deferred operations stories | P28 | No operations story is deferred: 5.3 and 5.4 are in v1's scope (initiative scope decision 2026-10-27) |
| Decision 2026-10-27 (2.7 security review): problem `type` URLs | P39 | Accepted risk under the GitHub namespace |
| Decision 2026-10-29: bank context; EF-only idempotency claim | P29 | Its context half is this document's opening line |
| Decision 2026-10-30 (human, 7.6 challenge): OTP guess bound | P30 | No control change in v1 |
| Decision 2026-10-30 (Sofia, refine): completeness through a mapping | this table | The mapping is the completeness check |

## Product scope

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

v1 is English only and ZAR only (PRD §6.2). Core banking is assumed to return the ZAR billed amount (AD-4, FR-14), and the dispute snapshot stores the currency as received, with no check (a `// shortcut:` in `Dispute.cs`). Before a non-ZAR source is connected, raise must reject, or convert to the ZAR billed amount, any transaction whose currency is not ZAR (story 3.1). *Why it waits:* the only source in v1 is the stub, which returns ZAR.

### P12. Joint accounts

An `accountRef` is assumed to belong to one customer. With joint accounts, a co-owner's raise gets 409 and learns another owner's dispute exists (story 3.1 security S3; its mapping row is P41's). Before joint accounts are connected, this assumption needs a decision. *Why it waits:* v1's core-banking source assumes one owner per `accountRef` (story 3.1 security S3).

### P13. The bank-scale end state

Linking moves into the bank's identity platform, downstream services consume the dispute events for chargebacks, fraud and complaints reporting, and customers are notified when a status changes (PRD §6.2; ad-001). *Why it waits:* it needs systems outside this repo.

### P14. Hosted login branding

The hosted login has no favicon (404) and shows Zitadel's default copy ("Create your Zitadel account.", "Welcome back!") instead of the mockups' "Sign in to DisputeDesk" (story 2.6 QA). *Why it waits:* Login v2 text customisation is a later polish item (story 2.6 QA).

## Events and the outbox

### P15. A schema registry

There is no schema registry: the two event types have committed JSON Schemas under `contracts/events/` and a contract test (AD-19). Redpanda's built-in registry is the upgrade path (PRD §6.2). *Why it waits:* AD-19 rejects a registry for v1; the committed schemas and the contract test check both types.

### P16. An event consumer and its inbox table

No consumer lives in this repo. A consumer would dedupe with an inbox table, keyed on `source` + `id`, and apply an event only if its `disputeVersion` is newer than the last it applied (PRD §6.2; AD-17, AD-19; Enterprise Integration Patterns, *Idempotent Receiver*). *Why it waits:* out of scope; the contract already requires dedupe.

### P17. Order across disputes

Order is defined only within one dispute, by `Version`; there is no global position and no index by event type (NFR-DATA-5; AD-17; ad-009). A PostgreSQL sequence is not a commit-order checkpoint, because sequence values are not rolled back and transactions commit out of allocation order (ad-009). If a reader across streams appears, the options are Kafka offsets, a high-water mark with `pg_snapshot_xmin`, or change data capture (PRD §6.2; architecture, Deferred). *Why it waits:* nothing reads across streams in v1.

### P18. Fencing the relay leader with Kafka transactions

The relay's advisory lock cannot fence a Kafka send already in flight when a leader loses its connection, so across a handover an earlier event of a dispute can arrive late or twice (AD-17). Fencing with Kafka transactions (a fixed `transactional.id`, KIP-98) is the stronger option. *Why it waits:* v1 is at-least-once and consumers keep per-dispute order by `disputeVersion` (AD-17, decided 2026-10-29).

### P19. Relay tuning and error handling

The exact relay batch size and backoff curve are tuning; AD-17 fixes the behaviour (architecture, Deferred). librdkafka error events are logged per occurrence, about 62 warnings per 30-second outage (story 4.2 review); production logs on state change or rate-limits per code. An unlisted error code on one dispute's head row stalls the whole relay; production defers per row and alerts on the deferred-warning rate (story 4.2 review). *Why it waits:* tuning; AD-17 fixes the behaviour (architecture, Deferred).

### P20. Failed outbox rows and a dead-letter topic

After five permanent rejections a row is `Failed`, and later events for that dispute wait (AD-17). An operator releases a `Failed` row with a targeted `UPDATE` (the runbook, AD-22's one operator exception). A row whose payload itself is refused cannot be released, because the payload is never edited; production adds a reviewed repair path or a dead-letter topic with an audited replay (story 7.7 refine; PRD §6.2, Operations features). *Why it waits:* AD-17 rejects a dead-letter topic for v1, and an operator release covers a fixed cause (AD-22).

### P21. Kafka transport security

The broker runs plaintext with no client authentication on the private Compose network in v1 (a `// shortcut:` in the relay's producer configuration; AD-26). Before a shared network, enable TLS plus SASL/SCRAM or mTLS on the broker, with topic ACLs so only the relay principal writes `disputedesk.disputes` (story 4.1 security S4, human decision 2026-10-29). *Why it waits:* the broker is on the private Compose network only (AD-26).

### P22. Error-rate alerts on expected unique violations

EF Core logs a unique violation (SQL state 23505) at Error even when the application expects and answers it. Alerting excludes 23505 on `UQ_Disputes_AccountRef_TransactionId` (a lost raise race, 409 `already-disputed`) and `UQ_StoredEvents_DisputeId_Version` (a version clash, AD-15). The logs are not filtered, because an EF Core filter would hide every failed save (story 3.4 pre-build review). *Why it waits:* it is an alerting rule for production, recorded by the story 3.4 pre-build review; v1 changes no logging.

### P51. Outbox release role

The runbook's release of a `Failed` row runs as the owner role `disputedesk`, which has full DML, and writes no audit record (story 7.7 pre-build security S7.7-2, human decision 2026-10-30). Production gives operators a dedicated role with only column-level `GRANT UPDATE ("Status", "Attempts", "NextAttemptAtUtc") ON "OutboxMessages"` and records each release in the audit log (ASVS 5.0 V8, V16.3; see also P20). *Why it waits:* a release is a manual step from the runbook (AD-22's one operator exception), and v1 environments hold only synthetic data.

## Data

### P23. Retention, cleanup and de-identification

Nothing is deleted from `Disputes`, `StoredEvents` or `AuditLogEntries` in v1 (AD-22). Retention follows transaction records: five years under the FIC Act, then de-identification under POPIA s14 (NFR-DATA-7). Production adds the jobs that enforce this, outbox cleanup and topic retention tuned to POPIA (PRD §6.2). It also adds a retention job deleting expired idempotency keys: `IdempotencyKeys.ResponseBody` keeps a copy of the customer's dispute view, expiry applies only when a key is reused, so expired rows are never deleted and an erasure on the dispute would miss them (story 3.2 security S2; ASVS 4.0.3 V8.3.8). *Why it waits:* the five-year retention period (NFR-DATA-7) has not run for any v1 data.

### P24. Crypto-shredding of personal data in stored events

`DisputeRaised` holds the customer ID, the account reference, the transaction ID and the snapshot; stored events are insert only (ad-009, AD-22). Production encrypts that personal data under a per-customer key and destroys the key on erasure (architecture, Deferred; NFR-SEC-5). *Why it waits:* in v1 stored events are kept under NFR-DATA-7 with the same access as `Disputes`, never logged or traced (AD-22).

### P25. Stored event schema evolution beyond additive changes

Changes within a stored event type are additive; a new shape is a new type name (AD-14a). Upcasting old shapes on read is the production option (architecture, Deferred). *Why it waits:* AD-14a's rule (additive changes only, a new shape is a new type) covers v1.

### P26. Snapshots to shorten replay

A handler loads a dispute by replaying its whole stream (AD-13). Snapshots at intervals would shorten replay (architecture, Deferred). *Why it waits:* streams hold a handful of stored events in v1 (estimate: 15 to 100 per stream once internal work is recorded).

### P27. Database-level append-only grant on `AuditLogEntries` and `StoredEvents`

The AD-14 guard refuses a Modified or Deleted audit row or stored event, and a source-rules test fails on bulk updates or raw SQL against either table (AD-14). Production also grants the application role insert and select only on both tables, with a separate migration role (architecture, Deferred; OWASP Logging Cheat Sheet). *Why it waits:* the application enforces insert only in v1.

### P28. Migrations as a bundle run as a one-off job

The Api applies EF Core migrations at start-up, under EF Core's migration lock, before it reports ready (AD-22). Production builds a migration bundle and runs it once as a one-off job before the new release starts, instead of every replica migrating at start-up ([EF Core, *Applying migrations*](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying); standards audit, 2026-10-27). *Why it waits:* v1 runs one Api instance.

### P29. Idempotency claim: a proposed EF Core-only replacement

**Current mechanism.** `IdempotencyKeyClaims.ClaimAsync` claims the key with one PostgreSQL `INSERT … ON CONFLICT` statement, as AD-16 specifies: an expired row is replaced in the same statement.

**Proposed future change, not built.** Claim with EF Core alone, through the unique constraint `UQ_IdempotencyKeys_CustomerId_Key` plus EF Core's documented optimistic concurrency on `LeaseId` ([EF Core, *Handling concurrency conflicts*](https://learn.microsoft.com/ef/core/saving/concurrency)), with explicit handling of the insert conflict (a `DbUpdateException` on the unique index) and of the lease and the stored response, as `docs/research/ef-core-upsert/ef-core-upsert.md` recommends. This is not an EF Core upsert, which EF Core does not have, and it is not an established guarantee until it is built and tested. AD-16 changes only when it is built. *Why it waits:* the current claim is correct as built (the research), and switching costs a contained rework with another fix round (the research, *Cost of switching now*); the initiative decision of 2026-10-29 makes it a production-path item.

## Linking and sessions

### P30. OTP guess bound

The controls bound OTP guesses per challenge (lockout after three wrong codes, FR-7) and per login window (the in-memory per-login and per-IP limits, FR-8); the send cap (FR-4) limits codes issued, not verification attempts (ad-008). This is not the account-wide limit on failed attempts that [NIST SP 800-63B-4, *Rate Limiting (Throttling)*](https://pages.nist.gov/800-63-4/sp800-63b.html) describes, because restarting linking creates a fresh challenge with 0 attempts. The upgrade is a persistent per-customer failure counter that survives restarts and new challenges (human decision, 2026-10-30, 7.6 challenge). *Why it waits:* no control changes in v1, by that decision.

### P31. Linking send cap and delivery

Every claimed OTP send counts toward the three-per-hour cap, delivered or not, so a customer hit by SMS delivery failures waits for the window. Production reads the SMS provider's delivery receipts to tell a failed delivery from an uncertain one. The cap count has no index on (`CustomerId`, `OtpSentAtUtc`); production adds one once `LinkingChallenges` outgrows a sequential scan (story 6.5 refine; AD-12). *Why it waits:* `ISmsSender` writes to Mailpit in v1 (AD-21), not to an SMS provider.

### P32. Session key renewal at a privilege change

Linking keeps the session key minted at sign-in, since the built-in cookie handler with a server-side ticket store cannot change the key within one request (ad-007; story 6.2 decision, 2026-10-30). Production renews the key at every privilege change ([OWASP Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html)) once the handler supports it, or moves to a session store that re-keys. *Why it waits:* fixation is already blocked, because the store rejects any key it did not mint (AD-7).

### P33. Scale-out and shared limit state

v1 runs one Api and one Worker instance (NFR-ARC-3). Production runs several of each, with shared rate-limit state and latency targets under load (PRD §6.2). Linking rate limits are held in memory on the single Api instance, so they reset on restart and do not add up across instances; scale-out needs a shared limiter store. The Api trusts forwarded headers from every container on the Compose network; production trusts only the load balancer's or gateway's own addresses (story 6.3 refine; see also P43). One over-limit login rotating IPv6 /64 prefixes writes one audit row per rejection, and the gateway's `limit_req` keys on the full address; production keeps limits in a shared store and keys the gateway on the /64 (story 6.3 security). *Why it waits:* sessions, idempotency keys and the audit log are already in PostgreSQL; in-memory limits are the one exception (NFR-ARC-3).

## Operations and the API surface

### P34. Operations features: listing and search

Operations tools read one dispute by ID (FR-30) and act on it; there is no listing or search for them (PRD §6.2, Operations features). The other operations features are P20 (dead-letter topic) and P23 (outbox cleanup, topic retention). No operations story is deferred: 5.3 and 5.4 are in v1's scope (`docs/tickets/README.md`; initiative scope decision, 2026-10-27). *Why it waits:* operations work happens in another team's tool, which calls the API (PRD §7).

### P35. Operations tool credentials and secret placement

In v1 `scripts/operations-token.sh` runs the client-credentials grant with the machine user's shared secret, and a person pastes the token into Scalar, which holds no credential (AD-26). The machine user's secret sits in `zitadel-clients.json`, which the Api container mounts and loads in full, though the Api never uses it. Real operations tools keep their own credentials in a secret store and authenticate with `private_key_jwt` (RFC 7523) instead of a shared secret; the access-token lifetime is set deliberately on the machine user (Zitadel's default in v1). Production gives each operations tool its own credential store, which the Api cannot read (least privilege, ASVS 5.0 V13.3; stories 5.1 and 5.5). *Why it waits:* Scalar stands in for another team's tool in v1 (NFR-DOC-4).

### P36. API viewer origin

Scalar is served on the customers' app origin under its own CSP (AD-26). Production serves any API viewer from a separate origin, so no script on it can reach the session cookie's routes (story 5.1 security S3). *Why it waits:* Scalar is a dev and demo tool (NFR-DOC-4).

### P37. Rate limits on customer and operations routes

Each refused withdraw (404) writes an audit row, and customer routes have no rate limiter, so a linked customer looping over random IDs adds one row per request (story 3.4 security S9). Each refused operations action writes a `Denied` or `Rejected` audit row, and the operations routes have no rate limiter either (story 5.2 pre-build review). Production adds a per-login limiter on unsafe customer routes and a per-client limiter on the operations routes, with the built-in rate limiter, as AD-12 does for linking ([Microsoft Learn, *Rate limiting middleware*](https://learn.microsoft.com/aspnet/core/performance/rate-limit); ASVS 5.0 V2.4.1). *Why it waits:* the gateway's coarse per-address limit already caps the request rate on `/api/` in v1 (AD-23).

### P38. Operations route fallback and audit cancellation

A wrong method or an unmatched path under `/api/v1/operations` reaches the framework's 405 or 404 endpoint, which carries no policy, so the cookie-only fallback answers bearer callers with 401 and no `Bearer` challenge (RFC 6750 §3); it fails closed and runs no handler. Production gives the operations group its own fallback answer. Audit saves for `Denied` and `Rejected` use the request's cancellation token, so a client that disconnects mid-request can cancel its own audit row; production saves audit rows with `CancellationToken.None` (OWASP Logging Cheat Sheet; story 5.2 review and security). *Why it waits:* both fail closed: no handler runs and no dispute changes.

### P39. Problem `type` URLs on an owned domain

Problem `type` URLs live under the GitHub namespace (AD-3), an accepted risk: a rename, deletion or repo-jacking would break or hijack them. Production moves them to an owned domain (story 2.7 security review, 2026-10-27). *Why it waits:* the project owns no domain (AD-19).

### P40. Seeding in a real deployment

`Seed:Enabled` defaults to off and is set only in Compose and the AWS demo (AD-26), but nothing refuses it at start-up. A real deployment refuses `Seed:Enabled` at start-up, so seeded events are never published for fixture customer IDs (story 5.4 security S4; ASVS 5.0 V13, configuration). Seeded snapshots on a stack kept across a day boundary without `docker compose down -v` are a demo limitation; the README says to reset (architecture, Deferred; AD-30). *Why it waits:* v1 has only local and demo environments.

### P49. SLA sweep failures

The SLA sweep claims the 50 earliest-due disputes each loop (AD-18); if 50 or more keep failing (for example a stream that will not replay), later disputes are never escalated (FR-26), although each failure is logged at Error (story 5.3 security L4). Production alerts on repeated sweep failures and parks a dispute that fails a set number of times. *Why it waits:* every failure is already logged at Error, so a stall shows in the logs; alerting rules are a production concern, as in P22.

### P50. Demo logins in AWS

The fixture logins of customers A, B and C, whose passwords are published in `fixtures/local.json`, are live on the AWS demo, and self-registration lets anyone link to customer D (story 7.4 pre-build security S7.4-11, human decision 2026-10-30). A real environment has no fixture logins and refuses the seeder at start-up (P40). *Why it waits:* the demo holds only synthetic data, so a fixture login reaches no real customer.

## Edge, telemetry and platform

### P41. Trace context trust

The browser's `traceparent` and `tracestate` are accepted as the request's parent, so the audit `TraceId` and the outbox trace columns are caller-chosen (story 3.1 security S3). The Api trusts any caller's `traceparent` under OpenTelemetry .NET's default ParentBased(AlwaysOn) sampler (no sampler is set; unverified on the stack), so a caller can send flags `00` to suppress the Api's spans for its own requests and choose the trace ID stored in `StoredEvents.TraceId` and the outbox (story 3.11 security T7). Production restarts or validates trace context at the gateway, the trust boundary, or sets a sampler that does not follow an untrusted parent's sampled flag at the edge ([W3C Trace Context](https://www.w3.org/TR/trace-context/), security considerations). *Why it waits:* one trace from browser to Worker is a v1 requirement (AD-20).

### P42. Browser telemetry

Locally, `/otlp/` is an anonymous write path: any client can post forged spans under any `service.name`, up to 1 MB per request within the coarse rate-limit zone, to the Dashboard (story 3.11 security T8). In a deployed environment the AWS gateway routes no `/otlp/` and the SPA built for AWS exports nothing, so browser spans are a local-stack feature (AD-29; story 7.4 refine). Before switching it on, add an authenticated or sampled intake (for example behind the BFF session with per-session limits) and filter browser spans at the collector by `service.name` (`disputedesk-web`) and an attribute allow-list. *Why it waits:* no deployed environment takes browser spans in v1.

### P43. Gateway edge: request logs and trusted proxies

The identity origin's access log records only the path, but its `error_log` still writes the request line with `id_token_hint` on an upstream error, and the app origin's combined access log keeps query strings (the dev-only GET sign-in callback's single-use code). Production logs paths only on both origins and lowers the identity origin's error-log detail. The Api trusts forwarded headers from the whole Compose network, which on a Linux Docker host also includes the host's bridge address; production trusts only the load balancer's or gateway's own addresses (story 6.6 security; ASVS 5.0 V4.1.3, V16.2.5; AD-23). *Why it waits:* v1 runs only the local stack and the AWS dev environment (PRD §6.1).

### P44. Gateway container hardening

`/etc/nginx` is writable by the nginx user, because `nginx-unprivileged` renders its templates there at start; production runs the gateway with a read-only root filesystem and a tmpfs render directory (story 2.5 security F4; CIS Docker 5.12). `infra/nginx/Dockerfile` runs an unpinned `apk upgrade` of brotli-libs, curl and libcurl (a `// shortcut:`), because the pinned base lags the Alpine fixes; drop it when a newer pinned base ships them, and consider removing curl (unused at runtime) or the `-alpine-slim` variant (story 2.5 review L1). *Why it waits:* the gateway already runs non-root from a pinned image (AD-27).

### P45. Init process as PID 1

Containers run an init process as PID 1 (Compose `init: true`), because the kernel drops signals PID 1 has no handler for, so a .NET process that fails to start would spin instead of exiting (pid_namespaces(7)). Any deployment outside Compose needs the same: ECS `linuxParameters.initProcessEnabled`, or an init in the image (start-up exit fix, 2026-10-30). *Why it waits:* v1 deploys only through Compose (AD-27, AD-28).

### P46. AWS dev environment, never applied

The AWS dev environment is written and checked with `terraform fmt`, `validate` and plans against mocked providers only, and has never been applied, because the account does not exist (AD-28; story 7.5 refine). Before any real use, run `infra/terraform/README.md`'s apply, deploy, check and destroy procedure, check the CloudFront prefix-list weight against security-group quotas (architecture, Deferred), and move Terraform's two state-held secrets (the origin-verify value and the RDS master password) to write-only arguments or ephemeral resources. *Why it waits:* the account does not exist.

### P47. CloudFront caching for static assets

All three distributions (app, auth and mail) use `CachingDisabled` (AD-28; story 7.4 pre-build security S7.4-1, human decision 2026-10-30). Caching static assets is a performance change (architecture, Deferred). *Why it waits:* performance only; caching stays disabled until then.

### P48. Node 26 LTS move

`.nvmrc` stays on Node 24 until a story bumps it (architecture, Deferred). *Why it waits:* the architecture revisits the move after 2026-10-28 (architecture, Deferred).

### P52. Edge transport

CloudFront reaches the AWS demo's origin over plain HTTP on port 80 (a `# shortcut:` in Terraform); the default `*.cloudfront.net` certificate keeps the minimum viewer protocol at TLS 1.0, and there is no WAF (AD-28; story 7.4 pre-build security S7.4-9 and story 7.5 pre-build security S7.5-11, human decision 2026-10-30). Production uses an HTTPS origin or CloudFront VPC origins, its own domain with an ACM certificate and a TLS 1.2 minimum viewer protocol, and AWS WAF in front of every distribution (ASVS 5.0 V12.1.1, V12.3.1). *Why it waits:* accepted for the free-plan demo, which holds only synthetic data, and the project owns no domain (AD-19, AD-28).

### P53. Build once, promote by digest

CI's `publish` job rebuilds the images on main rather than pushing the bytes the e2e job tested (it scans each one with Trivy before pushing), and the gateway's `apk upgrade` is unpinned (P44), so the pushed gateway can differ from the tested one (story 7.3 pre-build review 3; AD-32). Production builds each image once, tests and scans that digest, and promotes the same digest. *Why it waits:* each pushed image is still scanned before it is pushed.

### P54. Build provenance

Published images carry the OCI `source` and `revision` labels only (story 7.3 pre-build security S7.3-2). Production adds SLSA build provenance attestations, verified at deploy. *Why it waits:* it needs a new action, which v1 does not add (story 7.3 pre-build security S7.3-2).

### P55. Deploy role subject

The OIDC trust pins the repository's main branch, not the workflow (AD-28; story 7.5 pre-build security S7.5-7). Production customises the subject claim to include `job_workflow_ref`, so only `deploy.yml` on main can assume the role (GitHub, *Customizing the token claims*). *Why it waits:* the trust already limits the role to main, and the environment has never been applied (P46).

### P56. Instance role

`AmazonSSMManagedInstanceCore` can read every parameter in the account and `CloudWatchAgentServerPolicy` can write to any log group (AD-28, AD-29; story 7.5 pre-build security S7.5-9). Production replaces them with AWS's documented minimal Session Manager instance policy and a policy scoped to the one log group and X-Ray (AWS IAM, least privilege). *Why it waits:* the demo account is dedicated, so the account holds nothing else to read or write.

### P57. Probes in the published images

The published api and worker images are the `api-local` and `worker-local` targets: the chiseled .NET images plus a static busybox (named `wget` in the api, full busybox in the worker), because AWS runs Compose on one instance and Compose health checks run inside the container (human decision 2026-10-30, story 7.4 build; AD-22, AD-25). Production runs the shell-free `api` and `worker` targets under an orchestrator whose probes call `/health/live` and `/health/ready` over the network (Kubernetes liveness and readiness probes, or an ECS target group's health check). *Why it waits:* v1 has no orchestrator or load balancer to probe, and the health checks keep AD-22's "the Worker never runs on an old schema" and AD-25's restarts.

### P58. Third-party image scanning

The pinned collector image (`otel/opentelemetry-collector-contrib:0.164.0`) carries HIGH CVE-2026-116587 in go-jose v4.1.5, and CI's Trivy scans only the images the repo builds (story 7.4 security F1). Production scans every pinned third-party image on a schedule and upgrades when a fix ships. *Why it waits:* the vulnerable code (JWK parsing) is not reachable, because the collector has no JWT or OIDC extension and listens only on the private network, and no release with go-jose 4.1.6 or later exists yet.

### P59. Container network capabilities

In AWS the api trusts forwarded headers only from the gateway's pinned address, but containers keep Docker's default `NET_RAW` capability, so a compromised container on the Compose network could try ARP spoofing of that address (story 7.4 security re-run I1). Production drops `NET_RAW` on the internet-facing services (`cap_drop`), or runs each service in its own network namespace under an orchestrator. *Why it waits:* the only gain would be getting past the per-IP linking limit, and the per-login limit and the OTP lockout still hold.
