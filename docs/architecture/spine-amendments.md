# Architecture spine: amendment history

The architecture spine (`docs/architecture/architecture.md`) states each AD's current rule only. This file keeps what the rules used to carry inline: when each rule changed, in which story, and why. A rule changes by replacing its text in the spine and adding a row here, so a decision that changes supersedes the old one rather than growing by amendment (Michael Nygard, [*Documenting Architecture Decisions*](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)). The story records named in the Source column hold the full reasoning.

Restated on 2026-11-04: the dated clauses below were moved out of the rule text, with no change of meaning.

| AD | Date | What the rule now says because of it | Source |
| --- | --- | --- | --- |
| AD-2 | 2026-10-28 | The interface member is `Entities<TEntity>()`, not `Set`, because CA1716 rejects a keyword as a member name | story 3.1 |
| AD-3 | 2026-10-27 | Problem `type` values are resolvable GitHub URLs (RFC 9457 §3.1.1), replacing tag URIs, which nothing resolves and whose authority is not fully qualified (RFC 4151) | story 2.7 |
| AD-3 | 2026-10-27 | The gateway's own 429 carries no `traceId`, because nginx has no trace context | standards follow-up (initiative Notes) |
| AD-3 | 2026-10-28 | The SPA compares the full `type` URI with `===`, replacing routing on the last path segment | story 2.4 |
| AD-3 | 2026-10-30 | `ErrorKind.RateLimited` (429 `rate-limited`) for the full linking queue | story 6.5 |
| AD-4 | 2026-11-04 | The `now()` ban names application code and its SQL; AD-22's operator release is the one handwritten statement that uses `now()` (audit DES-16) | simplicity audit |
| AD-7 | 2026-10-27 | The warning dialog two minutes before the idle limit, renewing through `GET /api/v1/authentication/session` (WCAG 2.2 SC 2.2.1) | story 2.4 |
| AD-7 | 2026-10-27 | The AWS key ring in SSM Parameter Store, encrypted at rest with KMS | story 7.4 |
| AD-7 | 2026-10-30 | The cookie is written at sign-in and cleared at sign-out, never renewed, after a request in flight during logout set it again (the logout-cookie race) | the logout-cookie race fix |
| AD-7 | 2026-10-30 | A successful link keeps the session and its key. The built-in cookie handler renews the removed key when sign-out and sign-in run in one request, so re-keying at linking is not possible within one request; renewal at a privilege change is P32 | story 6.2 |
| AD-7 | 2026-10-31 | The SPA's cross-tab wall-clock deadline through `BroadcastChannel`, a privacy cutoff that clears customer data from memory | story 7.14 (7.10 PRR E-F8) |
| AD-8 | 2026-10-27 | A cookie-scheme 401 carries no `WWW-Authenticate` header, a deliberate RFC 9110 §15.5.2 deviation | standards follow-up (initiative Notes) |
| AD-8 | 2026-10-28 | Raise keeps automatic validation and runs 401, 403, 400, 404, 409, 422, 400 | story 3.1 |
| AD-8 | 2026-10-30 | On withdraw (story 3.4) and resolve (story 5.2), only a well-formed body is checked after the lookup, before the status | stories 3.4, 5.2 |
| AD-8 | 2026-10-30 | Resolve's `AcceptsMetadata` with no content types. "Last metadata wins" is documented for `EndpointMetadataCollection.GetMetadata<T>`; that an empty list means any content type is the behaviour of aspnetcore `src/Http/Routing/src/Matching/AcceptsMatcherPolicy.cs`, not documentation | story 5.2 |
| AD-8 | 2026-10-30 | JwtBearer 401s carry `WWW-Authenticate: Bearer` (RFC 6750 §3) | story 5.1 |
| AD-9 | 2026-10-26 | Role keys camelCase after the `dispute.` prefix | Gate 2 |
| AD-9 | 2026-10-30 | Only this project's role claim is read, never the generic one (ASVS 5.0 V10.3.2) | story 5.1 |
| AD-10 | 2026-10-29 | The deny-by-default fallback policy, with the authentication and health endpoints anonymous | story 2.8 |
| AD-12 | 2026-10-30 | The crash between claim and send loses the OTP, and the person restarts linking | story 6.1 challenge |
| AD-12 | 2026-10-30 | A correct code on the third attempt links; the challenge locks after three wrong codes (FR-7 governs). A login with no challenge row answers like a first wrong code, uncounted | story 6.2 challenge |
| AD-12 | 2026-10-30 | On success the session keeps its key, replacing "commit together with the session re-issue" | story 6.2 |
| AD-12 | 2026-10-30 | The per-IP then per-login chain; a request rejected by the login limiter has already taken IP permits. The count of two came from aspnetcore `release/10.0` `RateLimitingMiddleware.cs` (`AttemptAcquire`, then `AcquireAsync`); FR-8 was amended with it | story 6.3 challenge |
| AD-12 | 2026-10-30 | The full queue answers 429 to everyone; the advisory lock, cap, SIM-swap check and claim run in one transaction that commits before the send | story 6.5 |
| AD-12 | 2026-10-30 | A failed send keeps `OtpSentAtUtc`, so every claim counts toward the cap, replacing "counts nothing" | story 6.5 challenge |
| AD-12 | 2026-10-30 | The cap's hour can be short by one budget, accepted (security rated it low, ASVS 5.0 V2.4.1); `Linking:Budget` is at most Npgsql's 30-second command timeout | story 6.5, its security review and QA |
| AD-13 | 2026-11-04 | The "Ownership by story" sentences (3.8 owns the customer timeline mapping and the raise 201 body; 3.3 adds the detail read and the every-type and unknown-type tests; 3.9 adds the history list) are restated as the one mapping that serves all three | stories 3.3, 3.8, 3.9 |
| AD-14a | 2026-10-29 | Customer reads, refused ones included, write no audit row | story 3.3 |
| AD-14a | 2026-10-30 | `worker:seeder` as the seeder's automation actor ID | story 5.4 |
| AD-14a | 2026-10-30 | A `Denied` entry on an operations route records actor type `Operations` also for an unmapped `sub`; a dispute's Outcome never appears in an audit entry (no migration) | story 5.2 |
| AD-14a | 2026-10-30 | Who records `Rejected` and `Denied`: the read's 404 in story 5.1, the operations actions' in 5.2, raise and withdraw in 3.4, after 2.8 added `AuditLog.Record` | stories 2.8, 3.4, 5.1, 5.2 |
| AD-15 | 2026-10-26 | An `xmin` token on `Disputes`, superseded by ad-009's unique-index rule (the spine's open question asks a human to confirm) | ad-009 |
| AD-15 | 2026-10-30 | On a second clash the handler reads the `Disputes` row once and answers 409 with the status as last read | story 3.4 challenge |
| AD-16 | 2026-10-27 | The key is an RFC 9651 Structured Field String, so the value is quoted | standards follow-up (initiative Notes) |
| AD-16 | 2026-10-28 | `ChangeTracker.Clear()` after a lost unique-index save | story 3.1 |
| AD-17 | 2026-10-29 | At-least-once delivery with consumer deduplication; fencing with Kafka transactions goes to the production path | story 4.1 challenge |
| AD-17 | 2026-10-30 | Permanent rejections (a named list of Kafka error codes) count attempts; every other non-fatal error is transient | story 4.2 |
| AD-17 | 2026-11-04 | "3.8 tests that each outbox row's ID is its stored event's ID; 4.1 tests that the published CloudEvent `id` is the outbox row's ID" restated as the tests that exist | stories 3.8, 4.1 |
| AD-18 | 2026-10-30 | The SLA warning (ad-009) with a 48-hour lead time. Story 3.8 added the `SlaWarningRaised` record and its publish mapping; story 5.3 added `RecordSlaWarning`, its `Apply` case, `SlaWarningRaisedAtUtc`, `IX_Disputes_SlaWarning`, the batch transaction with a savepoint per dispute and the `Worker/Sla/` bypass | stories 3.8, 5.3 |
| AD-19 | 2026-10-26 | Wire enums camelCase; the event-versioning research predates this casing decision | initiative Notes |
| AD-19 | 2026-10-27 | `dataschema` is the committed schema's resolvable URL, replacing tag URIs | standards follow-up (initiative Notes) |
| AD-19 | 2026-10-30 | Seeded disputes are published at least once like any other event | story 5.4 |
| AD-21 | 2026-10-26 | One story shipped the whole port and stub before linking, transactions and raise | Gate 1 |
| AD-21 | 2026-10-29 | `TotalRequestTimeout` is the whole-call deadline | story 2.1 (initiative Notes) |
| AD-22 | 2026-10-26 | PascalCase names, `Utc` suffixes, and a surrogate `Id` on `CustomerLinks`, `IdempotencyKeys` and `Sessions`, whose former natural keys (`LoginSub`, `CustomerId` + `Key`, `KeyHash`) became unique constraints | naming decision |
| AD-22 | 2026-10-30 | The operator release of a `Failed` outbox row, the one exception to "updated only by the relay"; no release endpoint | story 7.7 challenge |
| AD-22 | 2026-11-04 | "Story 3.8 owns the dispute data model's migrations" dropped: every story is merged | story 3.8 |
| AD-23 | 2026-10-27 | nginx owns the security headers; the Api sets only `Cache-Control: no-store` | story 2.5 |
| AD-23 | 2026-10-30 | `ForwardedHeaders:TrustedNetwork` and `AllowedHosts` | story 6.3 challenge |
| AD-23 | 2026-10-31 | nginx adds `Cache-Control: no-store` to the `/api` responses it generates itself | story 7.12 (7.10 finding F-F4) |
| AD-24 | 2026-10-31 | The OIDC correlation and nonce cookie names come from configuration | story 7.13 (7.10 finding A-F3) |
| AD-25 | 2026-10-27 | The sweep heartbeat threshold is 90 seconds | standards follow-up (initiative Notes) |
| AD-25 | 2026-10-30 | At shutdown the relay finishes its one in-flight record | story 4.2 |
| AD-26 | 2026-10-30 | `DataProtection:SsmParameterPath` (story 7.4) and `DataProtection:KmsKeyId` (7.4 pre-build security) | story 7.4 |
| AD-26 | 2026-10-30 | `openapi.json` declares only a `bearer` scheme, replacing the 2026-10-27 local stand-in decision | stories 5.1, 5.5 |
| AD-27 | 2026-11-02 | `redpanda-console` as the one opt-in local exception | ad-010 |
| AD-27 | 2026-11-03 | The SPA built by `frontend/Dockerfile` and copied through a named build context, in place of the `infra/images/gateway/html` placeholder | ad-011 |
| AD-28 | 2026-10-30 | Until the account exists, Terraform is checked only with `fmt`, `validate` and mocked `test` | story 7.5 challenge |
| AD-28 | 2026-10-30 | Instance-metadata access only for `api` and `otel-collector`; the plain HTTP origin as a shortcut; Mailpit on its own origin, replacing `/mailpit/` on the app host | story 7.4 pre-build security |
| AD-28 | 2026-10-30 | The identity origin through CloudFront and the `ip-ranges.json` file (story 7.4 refine); the deploy script's atomic `.env` and the workflow's terminal status (story 7.5 refine); images published by story 7.3 | stories 7.3, 7.4, 7.5 |
| AD-29 | 2026-10-30 | No browser telemetry intake in AWS (an anonymous intake would allow unbounded ingestion); the AWS build switch is story 7.11's | story 7.4 challenge, story 7.11 |
| AD-30 | 2026-10-30 | Seeding through a second `AppDbContext` per customer with no bypass; seeded disputes published at least once. "Two instances can seed at once": when migrations were pending, EF Core 10 has committed each migration and released Npgsql's transaction-scoped lock before the seed runs, read from dotnet/efcore `release/10.0` `Migrator.cs` and `MigrationCommandExecutor.cs` and efcore.pg `v10.0.3` `NpgsqlHistoryRepository.cs`, not from documentation. The seeded-disputes story followed the operations actions (5.2 before 5.4); customer D was added by the linking story | story 5.4, epic 6 |
| AD-31 | 2026-10-27 | Each frontend package is verified and pinned when first added | standards follow-up |
| AD-31 | 2026-10-30 | The `Field` form pattern with React Hook Form's `Controller`, replacing shadcn/ui's Form component; the 401 `returnUrl` from a raise screen | story 3.6 |
| AD-32 | 2026-10-30 | Story 7.3 added `e2e` and `publish` and owns publishing to GHCR from CI on main; the frontend story added `frontend`; the AWS story (7.5) added `deploy.yml` | story 7.3 challenge |
| Consistency Conventions, Timestamps in JSON | 2026-10-28 | RFC 3339 UTC as System.Text.Json writes a UTC `DateTimeOffset`: offset `+00:00`, fractional seconds as written | story 3.1 |
| Structural Seed, Source tree additions | 2026-10-27 | One top-level folder per stack, with Microsoft's `src/` and `tests/` layout inside `backend/`; a root `src/` and `tests/` layout rejected | repository layout decision |

## Decisions of 2026-11-03 (S0b)

The human's decisions changed or confirmed these rules; unlike the rows above, they are decisions, not restatements.

| AD | Date | What the rule now says because of it | Source |
| --- | --- | --- | --- |
| AD-7 | 2026-11-03 | The two key-ring mechanisms are kept: the human answered no to one Postgres key ring in both environments (audit D7, DES-9) | simplicity audit mitigation plan |
| AD-12 | 2026-11-03 | The count of IP permits a rejected request spends is no longer stated: FR-8 and AD-12 cite only Microsoft Learn's documented chained-limiter behaviour, and the test stays as a regression pin (audit D14, DES-6) | simplicity audit mitigation plan |
| AD-28 | 2026-11-03 | AWS-only mechanisms stay as built until an apply is possible (audit D11, DES-10); the seven Terraform modules stay, ready for a second environment (audit D8, INF-6) | simplicity audit mitigation plan |
| AD-8 | 2026-11-03 | Every route checks in the framework's order, 401, 403, 400, 404, 409, 422, then 400 for a rule that needs the loaded resource, raise included; 403 before 404 still stops ID probing. PRD FR-24 and the initiative's R20 state the same order; `.DisableValidation()`, the body check after the lookup and raise's second order go | human decision |
| AD-11, AD-30 | 2026-11-03 | The seeder (`Infrastructure/Seeding/Seeder.cs`, that one file) is AD-11's third named bypass: it reads fixture disputes on the migrating context with `IgnoreQueryFilters(["Ownership"])` and an explicit customer predicate, checked by the same architecture test. The second `AppDbContext` per customer and AD-30's two-instance seeding analysis go; AD-30's idempotency cites only EF Core's documented `UseAsyncSeeding` contract. PRD NFR-SEC-6, the initiative's R17 and `conventions/backend.md` name the same three places. Replaces the 2026-10-30 AD-30 row above | human decision |
| AD-8 | 2026-11-04 | Resolve's `AcceptsMetadata` with no content types is removed: a `text/plain` resolve gets the cookie fallback policy's 401 with no `WWW-Authenticate: Bearer` for a missing, refused or valid token. Replaces the 2026-10-30 row above | human decision |
| AD-14a | 2026-11-04 | A refused token on a `text/plain` resolve writes no `Denied` entry, the one unaudited operations refusal | human decision |
| AD-7 | 2026-11-03 | Every session ends in the browser by a document navigation (`location.replace` to the session-ended screen), which may need the network; each tab keeps its own wall-clock deadline and warning, and the deadline shared over `BroadcastChannel` goes to the production path (P76). P74 closes. Replaces the 2026-10-31 row above | human decision |
| AD-5 | 2026-11-03 | One enum rule on the wire: one converter factory creating `CamelCaseNameOnlyEnumConverter<TEnum>` reads every enum in a JSON request body only from its exact camelCase name and refuses a number, a list and any other casing; writing is unchanged. `SingleNameEnumConverter` goes, and the raise route stops accepting other casings, a contract change. AD-8's binding clause, the *Casing* row, PRD NFR-API-6 and `conventions/backend.md` state the same rule. Replaces the 2026-10-29 decision of stories 3.3 and 3.9 that the raise reason binds in either case | human decision |
| AD-9 | 2026-11-03 | Operations callers are a plain allowlist, `Operations:AllowedClients` (an array of subs): every allowed caller is actor type `operations`, an empty or missing list refuses every caller, and a `Denied` entry records the token's `sub`. The `Operations:Actors` map and its validator go, and AD-14a's `ActorType` keeps one meaning, the route's credential type. PRD FR-25, the initiative's R21, AD-26 and ad-006 state the same rule. Replaces the 2026-10-30 AD-14a row above for an unmapped `sub` | human decision |
| AD-7 | 2026-11-04 | Other tabs learn of an ended session when brought to the front: the session query alone refetches on window focus (TanStack Query `refetchOnWindowFocus`). A window visible beside another is an accepted risk (P77); ASVS 5.0 V14.3.1 is met for the leaving tab, and for other tabs on focus | human decision |
| AD-18 | 2026-11-03 | The SLA sweep only escalates: `SlaWarningRaised`, `RecordSlaWarning`, the warning claim, the 48-hour lead time, `SlaWarningRaisedAtUtc` and `IX_Disputes_SlaWarning` go, and every stored event is published, so `disputeVersion` has no gaps. AD-13's methods, AD-14's publish mapping, AD-14a's closed set, AD-17's order, AD-19's `disputeVersion`, AD-30's seeded values, PRD FR-17, FR-26 and NFR-DATA-2, the initiative's R18, R22 and R28, ad-006 and ad-009 state the same; the open question on the glossary's "Timeline" closes. Replaces the 2026-10-30 row above | human decision |
| AD-1 | 2026-11-03 | Contracts references Domain, and only Domain, for the nine enums identical on the wire (`Outcome`, `DisputeAction`, `Reason`, `ActorType`, `DisputeStatus`, `TransactionKind`, `NotDisputableReason`, `TransactionDirection`, `TransactionState`): each exists once, in Domain, with no Contracts copy and no mapping, and the wire is unchanged. The customer-view enums `CustomerStatus` and `TimelineActor` stay in Contracts with their one mapping. AD-14a's ownership sentence and `conventions/backend.md` (the using-alias ban stays; qualified names only where names still clash) state the same rule. Replaces the earlier answer that left AD-1's separate enum graph as is | human decision |

## What else left the spine on 2026-11-04

- **PRD and code divergences.** Six differences between the PRD and the code merged before epic 2 (the `ErrorKind` set, `provision.sh`'s roles and keys, Scalar behind `IsDevelopment()`, the Worker's health check, the fixtures, the gateway's `limit_req`, HSTS and `form-action`), each fixed in its story. Every story is merged, so the section was deleted.
- **"This run adds…"** The note listing the columns and tables the spine added to the recommended data model (`LinkingChallenges.OtpSentAtUtc`, `SupersededAtUtc` and a nullable `CustomerId`; `Disputes.Version`; the outbox's `DisputeId`, `DisputeVersion`, `TraceState`, `LastError` and `text` `Payload`; `IdempotencyKeys.LeaseId` and `ResponseLocation`; the audit actor, dispute and trace columns, with `outcome` renamed to `Result`; `Sessions` and `DataProtectionKeys`; `StoredEvents` as the history table; the naming decision above). The ER diagram and AD-22 state the current model.
- **Story ownership.** AD-1's "the story that first needs each missing rule adds it" (every rule now exists, in `LayerTests` and `OwnershipRulesTests`), AD-25's "once the relay exists" and "once the API exists", and the closing line naming the stories that wrote the production path and settled the SLA lead time.
| AD-21 | 2026-11-04 | `GetAccountsAsync` removed from the port and the stub | human decision |
