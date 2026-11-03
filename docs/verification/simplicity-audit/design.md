# Simplicity and standards audit: design

Scope: the decisions the code was made to follow. `docs/architecture/architecture.md` (AD-1 to AD-32, AD-14a, AD-19a, Deferred, Open questions), `docs/decisions/ad-001` to `ad-011`, `docs/prd/prd.md` (§5 especially), `docs/initiative-disputedesk.md`, `docs/production-path.md`, `docs/glossary.md`, `docs/conventions/backend.md` and `frontend.md`. Read-only audit of the working tree on base commit `9836438` (the architecture, PRD and production path carry uncommitted edits; this audit reads them as they stand). Code was opened only to confirm that a rule is built as written.

Rules applied: AGENTS.md *How to work*: "Use established standards; do not invent mechanisms ... Where more than one standard approach fits, choose the one that is simplest to explain. If a design cannot be explained to an engineer new to the repo in one or two sentences, it is too complex and must be simplified"; "Every abstraction earns its place"; "Over-engineering is the standing risk. Meet each quality area once and well; anything bigger goes in `docs/production-path.md`."

Justification sources searched: the PRD and the initiative R-numbers, the decision records, `docs/verification/brief-es-decisions.md` and `brief-es-challenge.md`, `ad-011-challenge.md`, `7.10-prr.md` and its dispositions, the story challenges and refines cited in each AD, and `epic-7-retrospective.md`.

Tests: **E** explainability (one or two sentences), **B** by the book (documented framework approach or industry standard, cited correctly), **P** proportion (once and well, not beyond the requirements, nothing the requirements need simplified away), **N** necessity (a stated requirement forces it, and it is the simplest standard way to meet it).

Severity: high = an invented mechanism, or a design that cannot be explained in two sentences without a forcing requirement; medium = accreted complexity or beyond-requirement scope; low = citation or wording. Effort: S (hours, docs or a small change), M (a story), L (several stories or a data-model change). Tag: [decision] changes a decision and needs a human; [doc] is a wording fix only.

## 1. Findings

### DES-1. The spine fails its own two-sentence test: rules have grown by dated amendment, with plan history kept in the rule text

- **Where:** `architecture.md` AD-7 (l.108-115), AD-8 (l.121), AD-12 (l.154-163), AD-13 (l.169), AD-14a (l.190-192), AD-15 (l.199), AD-30 (l.372); frontmatter `updated: '2026-10-29'` (l.10); *PRD and code divergences* (l.706-715); *Source tree* note on columns "This run adds..." (l.630-639).
- **Tests failed:** E.
- **Evidence:** AD-8's rule is one paragraph of about 380 words that mixes the scheme rule, the check order, two different validation orders, a deliberate RFC 9110 deviation, the RFC 6750 header, and a routing workaround justified from framework source. AD-13 holds the aggregate rule, replay, the read model, the timeline allowlist, disputability order, the customer wire view and "**Ownership by story.** 3.8 owns the customer timeline mapping ... 3.3 adds the detail read ... 3.9 adds the history list". All stories are merged, so that text is history. AD-1 still says "The story that first needs each missing rule adds it". The divergences section lists six items each "fixed in the named story", all of them merged since. The frontmatter says `updated: '2026-10-29'` while the body carries amendments dated 2026-10-30 to 2026-11-02. Sixty-five dated "decided" or "amended" clauses sit inside the spine's text.
- **Essential vs accreted:** most ADs have an essential rule that fits two sentences (given in the table in §3). The accretion is edge-case clarification found during stories (which belongs in the story's tests and the challenge record), plan ownership, and the reason for each amendment.
- **Simpler standard alternative:** keep each AD as Rule (two to five sentences), Prevents, Rejected; move dated rationale to the decision record or the story's verification file, and supersede rather than amend (Michael Nygard, [*Documenting Architecture Decisions*](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions): a decision that changes is a new record that supersedes the old one). Delete the divergences section and the story-ownership sentences now that every story is merged; fix the frontmatter date.
- **Severity:** medium. **Effort:** M. **Tag:** [doc].

### DES-2. Event sourcing (ad-009, AD-13 to AD-15) is the standard pattern, but no requirement forced it, and Microsoft's own guidance warns against it for a domain this small

- **Where:** `ad-009-stored-events.md` l.5; `architecture.md` AD-13 l.169 ("**Loading by replay.** ... **Read model.**"), AD-14, AD-15; PRD NFR-DATA-2 (l.561), R29; `brief-es-decisions.md` decisions 1, 13, 15.
- **Tests failed:** P, N.
- **Evidence:** the business requirement is "view the history of their disputed transactions, including the current status of each dispute" (PRD §1). Before 2026-10-29 the design met it with a `Disputes` row plus an `xmin` token (AD-15 records "The `xmin` token was settled on 2026-10-26"). The stored-events brief was a human decision; the decision-time challenger answered "VERDICT: RETHINK" (`brief-es-challenge.md`) and its "Simplest design" kept the event table but asked that every extra have "a named present-day use". The requirement text that now demands replay (R29: "a handler loads a dispute by replaying its stored events and decides from that state ... a test proves replaying each stream reproduces its row") was written from that decision, not before it. The [Event Sourcing pattern](https://learn.microsoft.com/azure/architecture/patterns/event-sourcing) that ad-009 cites lists, under when not to use it, "small or simple domains, systems that have little or no business logic". The dispute has five statuses, one aggregate and a 5 x 4 allowed-actions table (FR-24).
- **What it costs:** a read model that must equal the fold (and a replay test to prove it), a `Version` column that leaks gaps into `disputeVersion` (AD-19), a per-type `Apply` fold, a version-clash retry with a second-clash read (AD-15), a seeder that must reach every status through aggregate methods with back-dated instants (AD-30), the AD-14 `SaveChanges` override that writes three tables, and the production-path entries P24 (crypto-shredding of stored events), P25 (upcasting) and P26 (snapshots).
- **What is essential either way:** an append-only history row per change, written in the same transaction as the change and the outbox row (R10, R16, R28), and the rules in the aggregate (NFR-DATA-3). Neither needs replay.
- **One-to-two-sentence version (exists):** "Each dispute change appends one stored event to the dispute's stream in the same transaction as its `Disputes` read-model row and, when published, its outbox row; handlers rebuild the dispute by replaying the stream and decide from that." It is explainable, so the finding is proportion, not explainability.
- **Simpler standard alternative:** the `Disputes` row is the state, guarded by EF Core optimistic concurrency ([Handling concurrency conflicts](https://learn.microsoft.com/ef/core/saving/concurrency), `xmin` on Npgsql), and an append-only history table (the [Audit Log pattern](https://martinfowler.com/eaaDev/AuditLog.html)) written by the same `SaveChanges` feeds the timeline and the outbox. Same guarantees for R10, R16, R23 and R28; no replay test, no fold, no snapshot or upcasting entries.
- **Severity:** medium (standard and explainable, but beyond the requirement that existed). **Effort:** L (data model, migrations, seeder, AD-13 to AD-15, R29). **Tag:** [decision]. Given that the design is built, tested and merged, keeping it and recording the trade-off honestly in ad-009 (cite the "when not to use" line, state that the choice demonstrates the pattern rather than being forced) is the low-cost option.

### DES-3. `SlaWarningRaised` is a feature added to exercise a pattern; a requirement was then written around it

- **Where:** `architecture.md` AD-18 l.234 ("**SLA warning** (ad-009, story 5.3)"); AD-13 allowlist; AD-19 l.249 (`disputeVersion` gaps); AD-30 l.373 (seeded `SlaDueAtUtc` beyond the lead time); PRD FR-26 l.483; R22; `brief-es-decisions.md` 4a.
- **Tests failed:** N, P.
- **Evidence:** no FR, journey or success metric before 2026-10-29 asks for an SLA warning; the glossary has no warning term; no consumer reads it (it is unpublished, and FR-30's read is its only reader). The challenger said: "adding `AgentNoteAdded` or `SlaWarningRaised` solely to exercise that mapping adds a product feature without a use case" (`brief-es-challenge.md` §2). Its "present use" in ad-009 is the sweep that was built to produce it (story 5.3). Its cost reaches six ADs: a second claim query and partial index `IX_Disputes_SlaWarning`, the read-model column `SlaWarningRaisedAtUtc`, a 48-hour lead time, a seeding constraint, a publish mapping "to nothing", version gaps that every consumer must be told to tolerate, and the glossary open question on "Timeline" (architecture l.746).
- **Simpler alternative:** drop the type. The deny-by-default timeline allowlist (AD-13) is still proven by the test-only unknown type that 3.3 already has ([OWASP Authorization Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html): deny by default), and the "never publish a private type" rule is still proven by the exhaustive mapping's test. `disputeVersion` then has no gaps.
- **Severity:** medium. **Effort:** M (one migration to drop the column and index, sweep, seeder, R22, FR-26). **Tag:** [decision].

### DES-4. The check order 401, 403, 404, 400, 409 has no security basis for its 404-before-400 step, and it forces two validation orders and a routing workaround

- **Where:** PRD FR-24 l.444 ("Checks run in this order: 401, 403, 404, 400, 409. A caller without the role therefore cannot probe which dispute IDs exist."); `architecture.md` AD-8 l.121; code `Api/Disputes/DisputeEndpoints.cs:63` and `Api/Operations/OperationsEndpoints.cs:52-60` (`.DisableValidation()`, `new AcceptsMetadata([], typeof(ResolveDisputeRequest))`).
- **Tests failed:** E, B.
- **Evidence:** the stated reason ("a caller without the role cannot probe") is met by 403 before 404 alone; a caller who reaches the 400-versus-404 step already holds the role or owns the resource. Yet the order forces: `.DisableValidation()` on withdraw and resolve with a second, hand-written body check after the lookup; a *different* order for raise ("401, 403, 400 for the body's shape, then 404, 409, 422, and last 400"); and on resolve, an `AcceptsMetadata` with an empty content-type list, justified from aspnetcore source `AcceptsMatcherPolicy.cs` and "last metadata wins", so that a text/plain body is not answered by the cookie fallback. AD-8 cannot be stated in two sentences, and part of its correctness rests on framework internals (see DES-6).
- **One-to-two-sentence version of what is essential:** "Customer and linking routes take only the cookie, operations routes only a bearer token, each policy pins its scheme, and a wrong or missing credential gets 401 with a body, never a redirect. Authentication and authorisation run before anything reads the request."
- **Simpler standard alternative:** one order everywhere, the framework's: 401, 403, then the built-in minimal API validation (400, [Microsoft Learn, *Minimal APIs: validation*](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis)), then 404, 409, 422. Delete `DisableValidation`, the post-lookup body check and the `AcceptsMetadata` workaround (re-check the 415 case: with validation on, a non-JSON body is rejected by the endpoint's own binding after its policy). RFC 9110 sets no precedence between 400 and 404.
- **Severity:** high (the forcing requirement's own reason does not require it). **Effort:** M (PRD FR-24, R20, AD-8, tests in 3.4 and 5.2). **Tag:** [decision].

### DES-5. The actor-type map has one possible value, and AD-14a then redefines what `ActorType` means

- **Where:** `architecture.md` AD-9 l.128 ("`Operations:Actors:<sub>` maps a service account's `sub` to exactly one actor type (`operations`); no mapping means 403"); AD-26 l.331; AD-14a l.190 ("A `Denied` entry on an operations route records actor type `Operations` ... also when that `sub` maps to no actor: there `Operations` means the request was presented to an operations route"); `ad-006` l.12, l.25; PRD FR-25 l.454 ("does not map to exactly one known actor type returns 403 ... one Zitadel service account per actor type [ASSUMPTION]").
- **Tests failed:** N, E.
- **Evidence (extends the code auditors' finding):** of the three actor types, `customer` arrives only by cookie and `automation` only in-process, so a bearer token can only ever map to `operations`. "Exactly one known actor type" is therefore a sub allowlist under another name, and it already needs a second meaning of `ActorType` in AD-14a for the unmapped case. The roles (AD-9) already decide what a caller may do; the map adds a second authorisation input that the glossary says actors never are ("Recorded, never used to decide").
- **Simpler standard alternative:** every caller that passes JwtBearer validation and holds the action's role is actor type `operations`, actor ID its `sub` (role-based authorisation, [Microsoft Learn, *Role-based authorization*](https://learn.microsoft.com/aspnet/core/security/authorization/roles)). If a sub allowlist is wanted, name it as such (`Operations:AllowedClients`), and record `Denied` with the token's `sub` without redefining `ActorType`.
- **Severity:** medium. **Effort:** S. **Tag:** [decision] (FR-25 consequence 3, AD-9, AD-14a, AD-26, ad-006).

### DES-6. Three ADs rest on framework source internals rather than documented behaviour

- **Where:** AD-8 l.121 ("the empty list meaning any content type is the behaviour of aspnetcore `src/Http/Routing/src/Matching/AcceptsMatcherPolicy.cs`"); AD-12 l.159 ("two: the middleware calls the chained limiter's `AttemptAcquire` and then `AcquireAsync` before rejecting, dotnet/aspnetcore `release/10.0` `RateLimitingMiddleware.cs`"), repeated in PRD FR-8 l.244; AD-30 l.372 ("when migrations were pending, EF Core 10 has committed each migration and released Npgsql's transaction-scoped lock before the seed runs, so two instances can seed at once ... from dotnet/efcore `release/10.0` `Migrator.cs` and `MigrationCommandExecutor.cs` and efcore.pg `v10.0.3` `NpgsqlHistoryRepository.cs`").
- **Tests failed:** B.
- **Evidence:** "framework first" means the documented contract. Source-level behaviour is not a contract and can change in a patch (the stack pins 10.0.14 and bumps patches together). Each is a symptom of a rule pushed past the documented path: AD-8 by the check order (DES-4), AD-12 by promising a per-IP count in the PRD, AD-30 by a concurrency case (two Api instances seeding at once) that NFR-ARC-3 says v1 never runs.
- **Simpler standard alternative:** AD-8, see DES-4. AD-12: state only the documented behaviour of chained limiters ([Microsoft Learn, *Rate limiting middleware*](https://learn.microsoft.com/aspnet/core/performance/rate-limit)) and drop "two permits" from FR-8; keep the test as a regression pin, not a requirement. AD-30: drop the two-instance seeding analysis (one Api instance, NFR-ARC-3; seeding is idempotent through the unique index) and use the documented [`UseAsyncSeeding`](https://learn.microsoft.com/ef/core/modeling/data-seeding#use-seeding-method) contract only.
- **Severity:** medium. **Effort:** S (docs; tests stay). **Tag:** [doc] for AD-30 and AD-12; [decision] for FR-8's wording.

### DES-7. The seeder's "no third bypass" rule buys a second `AppDbContext` per customer over the migrating connection

- **Where:** `architecture.md` AD-30 l.372 ("per customer it builds a second `AppDbContext` over the migrating context's connection with that customer's `CurrentCustomer` ... reads each fixture dispute by `accountRef` + `transactionId` through the ownership filter"); AD-11 l.145 (bypasses only in `Operations/` and the sweep).
- **Tests failed:** E, P.
- **Evidence:** the seeder is start-up code behind `Seed:Enabled`, not a request path; the ownership filter protects customers from each other at request time (R17). Avoiding a named bypass here costs a context-per-customer construction inside EF Core's seeding hook and the source-level concurrency analysis in DES-6.
- **Simpler standard alternative:** the seeder is the third named bypass (`IgnoreQueryFilters(["Ownership"])`, [EF Core *Global query filters*: disabling filters](https://learn.microsoft.com/ef/core/querying/filters)), checked by the same architecture test; or it creates a DI scope per customer and sets `CurrentCustomer` there, the standard way a scoped value is set. Either is one sentence.
- **Severity:** medium. **Effort:** S. **Tag:** [decision] (AD-11's two-bypass rule, NFR-SEC-6 "each in one named place" still holds).

### DES-8. The SPA's cross-tab session deadline duplicates the server's idle limit

- **Where:** `architecture.md` AD-7 *Expiry* l.112 ("The SPA also ends the session in the browser 15 minutes after the last API response ... and shares that deadline across the tabs of the origin through `BroadcastChannel` (`disputedesk-session`)"); story 7.14; code `frontend/src/app/useSessionTabs.ts`.
- **Tests failed:** P.
- **Evidence:** R2 requires that "the session ends after 15 minutes idle", which the ticket store enforces (AD-7: "the server's session stays the authority"). WCAG 2.2 SC 2.2.1 needs a warning before the limit, which a per-tab timer met. The cross-tab deadline, the `activity` / `expired` / `signed-out` message protocol, the visibility and focus re-checks and the bfcache reload came from a production readiness review disposition (7.10 PRR E-F8, "fix, story 7.14"), not from a requirement, and the story took five verifier rounds and seven fix files (`7.14-verify-r0` to `r4`, `7.14-fixes*.md`). Clearing the query cache on a 401 or `anonymous` (criterion 1 of 7.14) is the proportionate part.
- **Simpler standard alternative:** keep the one clean-up on 401 or `anonymous` (TanStack Query `removeQueries`), the per-tab warning dialog that renews through `GET /api/v1/bff/session` ([WCAG 2.2 SC 2.2.1](https://www.w3.org/WAI/WCAG22/Understanding/timing-adjustable.html)), and let the server's idle limit end every tab on its next request ([OWASP Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html): the server enforces timeouts). The shared deadline goes to the production path beside P74.
- **Severity:** medium. **Effort:** M. **Tag:** [decision] (a human disposition).

### DES-9. Two Data Protection key-ring mechanisms, one per environment, for an environment never applied

- **Where:** `architecture.md` AD-7 *Keys and tokens* l.111 ("In AWS the key ring is stored with AWS's documented `Amazon.AspNetCore.DataProtection.SSM` package ... encrypted at rest with KMS"); AD-26 l.331 (`DataProtection:SsmParameterPath`, `DataProtection:KmsKeyId`); code `Api/Bff/BffAuthentication.cs:63-67`.
- **Tests failed:** P, N.
- **Evidence:** locally the key ring is in Postgres (`PersistKeysToDbContext`); in AWS RDS replaces that Postgres (AD-28), so the same documented mechanism works unchanged there. The AWS branch adds a package, two configuration keys, a KMS key in Terraform, and an environment branch in `BffAuthentication`, which AD-24 calls out as something to prevent ("environment branches in code"). The decision (2026-10-27, story 7.4) cites encryption at rest; RDS storage encryption gives that for the Postgres ring.
- **Simpler standard alternative:** `PersistKeysToDbContext` in both environments, with RDS encryption at rest ([Microsoft Learn, *Key storage providers*](https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-storage-providers), [*Key encryption at rest*](https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-encryption-at-rest)); SSM or KMS-wrapped keys become a production-path entry beside P68.
- **Severity:** medium. **Effort:** S. **Tag:** [decision].

### DES-10. The AWS environment carries production-grade hardening, yet it has never been applied, and the done-when still promises a deploy

- **Where:** `architecture.md` AD-28 l.350-356; PRD NFR-INFRA-3 to -5; initiative R38 and *Done when* l.87 ("The dev environment deploys to AWS through Terraform and the GitHub OIDC role, and destroys cleanly"); `production-path.md` P46 ("AWS dev environment, never applied").
- **Tests failed:** P (both directions).
- **Evidence:** R38 requires Terraform modules for one dev environment (a human decision that reversed the kickoff amendment's "no live deploy"), so the Terraform itself is in scope. On top sit three CloudFront distributions with an origin-verify secret, a Mailpit origin behind basic auth, an iptables `DOCKER-USER` rule for IMDS, a collector with SigV4, the SSM key ring (DES-9) and a deploy script with atomic `.env` writes, all checked only by `terraform validate` and mocked `terraform test` because the account "does not exist" (AD-28 l.350). The 2026-10-27 scope decision already said the AWS deploy "falls back to a documented Terraform plan" (initiative l.127); the R38 text and the done-when were never brought in line, so the requirement says more than was delivered.
- **Simpler standard alternative:** restate R38 and the done-when to the delivered fallback ("Terraform modules and a dev root that pass `fmt`, `validate` and mocked `test`; applied when the account exists"), and freeze the AWS hardening at what is built: no further AWS-only mechanisms until an apply is possible.
- **Severity:** medium. **Effort:** S. **Tag:** [decision] (R38 wording) and [doc] (done-when).

### DES-11. ad-011 team folders: a rule with no requirement behind it, an exception on its first day, and new build machinery

- **Where:** `ad-011-team-folders-in-one-repo.md` l.7 ("A team folder reads another team folder only through that folder's published contract"), l.12 and l.43 (the four branding files, "the one accepted exception to the rule"), l.29 (the build-only profiled SPA service with `additional_contexts`), l.30 (a new .NET test project under `infra/tests/`).
- **Tests failed:** N, P.
- **Evidence:** no PRD, NFR or R-number asks for team boundaries; the context is that "the folder a file sits in does not say which team owns it". The decision-time challenge answered "SIMPLIFY". The changes themselves follow documented Docker mechanisms ([Merge Compose files](https://docs.docker.com/compose/how-tos/multiple-compose-files/merge/), [`additional_contexts`](https://docs.docker.com/reference/compose-file/build/#additional_contexts)), so the issue is proportion, not standards: a never-started profiled service exists only to feed the gateway build, `infra/` gains a .NET SDK dependency, and the rule needed an exception before it was finished. The rule is explainable in one sentence, which is its strength.
- **Simpler standard alternative:** a `CODEOWNERS` file ([GitHub docs, *About code owners*](https://docs.github.com/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners)) states who owns each path in one line per folder, with no moves; or, if the moves stay, stop at changes 1, 2 and 5 (paths only) and leave the SPA build in the gateway image and the platform tests in backend.
- **Severity:** medium. **Effort:** S to undo changes 3 and 4 (changes 1, 2, 3 and 5 are in the tree; change 4, the infra test project, is not yet). **Tag:** [decision].

### DES-12. The PRD's NFR-DOC-3 says "exactly these" records, and the spine's map stops at ad-009; ad-010 and ad-011 exist

- **Where:** PRD NFR-DOC-3 l.645 ("key decisions are short decision records in `docs/decisions/`, exactly these"); `architecture.md` Capability map, Documentation row l.679 ("ad-001 to ad-008 ... and ad-009"); ad-004 and ad-005 are delivery-process records (model choice per agent; QA resets) inside the solution's decision set.
- **Tests failed:** E (the record set no longer matches its own rule).
- **Simpler alternative:** "Key decisions are short decision records in `docs/decisions/`" with no closed list; add ad-010 and ad-011 to the map; move ad-004 to `docs/process/` (it decides tooling, not the solution).
- **Severity:** low. **Effort:** S. **Tag:** [doc].

### DES-13. AD-5 names the built-in enum converter; the API uses a custom one that AD-8 made necessary, and the two enum rules disagree on case

- **Where:** `architecture.md` AD-5 l.94 ("through one shared `JsonSerializerOptions` with `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`"); AD-8 l.121 ("an enum value that is a number, a list or an unknown name" gets 400); code `Contracts/Json/SingleNameEnumConverter.cs:12`.
- **Tests failed:** B (an undocumented custom mechanism at the design level).
- **Evidence (extends the code auditors' finding):** the built-in converter reads `"a, b"` as combined flags, so AD-8's "a list" clause forced `SingleNameEnumConverter`, which no AD records; AD-5 still reads as if the built-in converter is used. The resolve outcome's exact-camelCase rule (story 5.2 AC3) and the case-insensitive converter are therefore two binding rules with no AD deciding between them.
- **Simpler alternative:** either record the converter in AD-5 with its reason and one case rule for every enum, or drop the "list" clause and check `Enum.IsDefined` after binding (one line, [`Enum.IsDefined`](https://learn.microsoft.com/dotnet/api/system.enum.isdefined)) for the enums where a combined value could bind.
- **Severity:** low. **Effort:** S. **Tag:** [decision].

### DES-14. AD-21's port ships a member no requirement reads

- **Where:** `architecture.md` AD-21 l.270-272 ("**One story ships the whole port and stub before linking, transactions and raise:** ... `GetAccountsAsync(customerId)`").
- **Tests failed:** N.
- **Evidence (confirms the code auditors' finding at design level):** no FR reads accounts (FR-11 lists transactions merged across accounts; `accountLast4` arrives on each transaction). The AD's "ship the whole port at once" rule created the member; its only callers are resilience tests.
- **Simpler alternative:** remove `GetAccountsAsync` from AD-21's list ("nothing speculative").
- **Severity:** low. **Effort:** S. **Tag:** [decision].

### DES-15. In AWS the primary success metrics cannot be measured

- **Where:** PRD §8 l.716 ("measured from the NFR-OBS-3 metrics"), NFR-OBS-3 l.626 ("Raise-screen opens and time to submit come from the SPA's browser spans"), NFR-OBS-4; `architecture.md` AD-29 l.363 ("In AWS there is no browser telemetry intake ... browser spans are a local-stack feature"); `production-path.md` P42.
- **Tests failed:** P (a requirement simplified away).
- **Evidence:** SM-1 and SM-2 are the two primary metrics and both come only from browser spans, which the AWS build does not export. The reason (an anonymous intake would allow unbounded ingestion) is sound, and P42 records it, but the PRD still reads as if SM-1 and SM-2 are measurable in every environment.
- **Simpler alternative:** either state in NFR-OBS-4 and §8 that SM-1 and SM-2 are measured locally only in v1, or emit two server-side counters (raise-screen opens are already a request: the transaction read that opens the raise screen) so the primary metrics exist in CloudWatch.
- **Severity:** low. **Effort:** S. **Tag:** [decision].

### DES-16. Small contradictions between the spine, the conventions and the glossary

- **Where and evidence:**
  - AD-4 l.87 "SQL never calls `now()` or `CURRENT_TIMESTAMP`", while AD-22 l.297's operator release runs `"NextAttemptAtUtc" = now()`. True for code, but the one handwritten statement in the spine breaks it; say "application SQL" or pass the instant.
  - `conventions/frontend.md` l.9 "Forms with shadcn/ui's Form component", while AD-31 l.382 replaced it with "`Field` with React Hook Form's `Controller`" (decided 2026-10-30, 3.6).
  - `conventions/backend.md` l.27 "Migrations follow expand, migrate, contract", while AD-22 l.295 allows reshaping history in place before the first release.
  - `glossary.md` "Timeline: The ordered history of a dispute's status changes", while operations see `SlaWarningRaised` on it (architecture Open questions l.746, still open; goes away with DES-3).
  - AD-7 l.109 cites "RFC 10017 §6.1, OAuth 2.0 for Browser-Based Applications"; this audit could not verify that RFC number (the work was published as `draft-ietf-oauth-browser-based-apps`); confirm it, or cite the draft.
  - AD-1 binds Domain and Contracts to share enum names, which forces the `using`-alias ban and fully qualified names (`conventions/backend.md` l.14). Contracts could reference Domain for the internal enums that are identical on the wire; the customer enums stay separate. Recorded here as a wording-level cost; changing AD-1's graph is a [decision].
- **Tests failed:** E, B (citation). **Severity:** low. **Effort:** S. **Tag:** [doc], except the last bullet [decision].

### DES-17. `docs/production-path.md` mixes deferred scope with accepted risks of what v1 built

- **Where:** `production-path.md` (444 lines, 75 entries numbered out of order, l.1-130 a 120-row source-to-entry table); entries P30, P32, P52, P61, P62, P65, P66, P68, P70 (from the 7.10 PRR and pre-build security reviews).
- **Tests failed:** E, P.
- **Evidence:** the document is used correctly in its main purpose: each entry says what v1 does, what production does and why it waits, and it designs nothing (l.5), which is what "anything bigger goes in `docs/production-path.md`" asks. But NFR-DOC-2 asks for "everything deferred in §6.2, with a one-line reason", and about half the entries are not deferred scope: they are known weaknesses of mechanisms v1 built (P30 "OTP guess bound", P32 "the pre-link session key stays valid after linking", P52 "plain HTTP origin, TLS 1.0 minimum and no WAF", P66 "bearer validation does not check the token type"). Mixed with feature deferrals such as P2 "Attachments", they are hard to find, though security is the primary quality area. The 120-row mapping table is a completeness check that is longer than most entries. P29 is a design proposal ("Proposed future change, not built"), which the opening line says the document does not hold.
- **Simpler alternative:** two sections, *Deferred scope* (PRD §6.2, one line each) and *Accepted risks* (one line each: the risk, the control in place, the upgrade), and move the mapping table to the 7.6 verification record. The register keeps the same content in fewer lines.
- **Severity:** low. **Effort:** S. **Tag:** [doc].

Counts: high 1 (DES-4); medium 10 (DES-1, DES-2, DES-3, DES-5, DES-6, DES-7, DES-8, DES-9, DES-10, DES-11); low 6 (DES-12 to DES-17).

## 2. Complexity examined and judged justified

| Decision | Why it is complex | Forcing requirement | Standard it follows |
| --- | --- | --- | --- |
| BFF with a server-side ticket store (AD-7, ad-007) | A ticket store, hashed 256-bit keys, idle and absolute limits, an hourly clean-up | R2 (no token in the browser), R3 ("a replayed old cookie gets 401", which a cookie-held ticket cannot give), NFR-ARC-3 (sessions in Postgres) | [OAuth 2.0 for Browser-Based Applications](https://datatracker.ietf.org/doc/draft-ietf-oauth-browser-based-apps/) BFF; [`CookieAuthenticationOptions.SessionStore`](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.cookies.cookieauthenticationoptions.sessionstore); OWASP Session Management (store a hash, server-side timeouts). Keeping the key at linking (P32) is a recorded, low-risk trade-off |
| Zitadel self-hosted (AD-6, ad-001) | Three containers and a provisioning script | Fixed by the kickoff amendment; R1 needs real sign-up and email verification; PRD §7 "not an identity platform" | Standard OIDC code flow and JwtBearer only; no runtime management API. The label-policy theming is a human choice, cheap once built |
| Linking with an off-request job, SIM-swap check and OTP (AD-12, ad-008) | A bounded channel, a hosted service, an advisory lock per customer, claim-send-release | R4 (identical response and timing), R5 (SIM-swap before OTP, fail closed), R6 (3 per hour across logins), R7 (atomic attempt count) | [OWASP Forgot Password Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html) (uniform response and time, asynchronous work), OWASP Authentication Cheat Sheet (generic responses), conditional updates per EF Core. Essential rule in two sentences: see §3. The accretions (cap window short by one budget, budget tied to Npgsql's 30 s timeout, two-key lock namespace) belong in the story's tests (DES-1), not in a simpler design |
| Transactional outbox, Kafka via Redpanda, the Worker relay (AD-14, AD-17, AD-19, ad-010) | A second host, an advisory-lock leader, per-dispute head-of-line order, at-least-once | R23 and R28 (atomic publish, per-dispute order, Kafka headers named in FR-27 and FR-28), NFR-RES-3 (works while Kafka is down), NFR-ARC-4 (a second Worker harmless); the broker is named by the kickoff | [Transactional outbox](https://microservices.io/patterns/data/transactional-outbox.html), [CloudEvents Kafka binding](https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/bindings/kafka-protocol-binding.md), W3C Trace Context, [PostgreSQL advisory locks](https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS), Idempotent Receiver. ad-010 is a model record: options compared with sources, memory as the binding constraint. One caveat: the relay leadership exists only for NFR-ARC-4 while v1 runs one Worker (NFR-ARC-3); it is cheap enough to keep |
| SLA sweep with `FOR UPDATE SKIP LOCKED` (AD-18, escalation part) | Raw SQL claim in batches, savepoint per dispute | R22 (two Workers cannot double-claim, catch up after downtime) | [PostgreSQL `SKIP LOCKED`](https://www.postgresql.org/docs/current/sql-select.html#SQL-FOR-UPDATE-SHARE), a hosted `BackgroundService`. The warning half is DES-3 |
| Actor-agnostic actions (ad-006, AD-13) | One method per action for every actor | PRD §4.7 and the human's decision that a caller may be a person, an LLM agent or automation | One aggregate owns the FR-24 table ([DDD aggregate](https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-domain-model)). Simple and standard; only the actor-type map is over-built (DES-5) |
| `Result` and `Error` (AD-3, ad-003) | A hand-written type and eight error kinds | R30 (ProblemDetails with a stable `type`), "framework responses without a body" prevented | RFC 9457 through `AddProblemDetails` and `CustomizeProblemDetails`; the Result pattern is a common, small, package-free choice. Resolvable `type` URLs follow RFC 9457 §3.1.1 |
| `IAppDbContext` (AD-2, ad-002) | One interface whose member forwards to `Set<T>()` | NFR-ARC-1 and NFR-ARC-5 (Application binds no provider, enforced by a test) | The Clean Architecture persistence seam; two members long and never edited. `Entities` instead of `Set` is forced by CA1716 with no suppression, a fair trade |
| Ownership by named query filter (AD-11) | A per-context instance member, two named bypasses, an architecture test | R17, NFR-SEC-6 | [EF Core 10 named query filters](https://learn.microsoft.com/ef/core/querying/filters); OWASP deny by default |
| Idempotent raise (AD-16) | A lease, a takeover after 60 s, 4xx stored, 5xx deleted | R15 | [IETF Idempotency-Key draft](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/) for the semantics; EF Core concurrency token for the lease. The takeover path is the one accretion; it is small and tested |
| Gateway edge and forwarded headers (AD-23, AD-24) | Templated nginx, trusted network, header ownership split | R8, R32, NFR-SEC-7 | [`ForwardedHeadersOptions`](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer), OWASP HTTP Headers Cheat Sheet, cookie prefixes per RFC 6265bis |
| Terraform modules plus an env root (AD-28, the Terraform itself) | Seven modules and a bootstrap root for one environment | R38 and the human's decision to show multi-environment structure | HashiCorp module and root layout, S3 native locking, GitHub OIDC. The hardening on top is DES-10 |
| Release of a `Failed` outbox row by hand (AD-22) | A documented operator SQL statement | NFR-RES-4 ("wait for an operator per the README runbook") | The simplest possible mechanism: no endpoint, no command. A model of proportion |

## 3. Every AD with a one-line verdict

| AD | Verdict | One-to-two-sentence rule (where one exists) or the reason |
| --- | --- | --- |
| AD-1 Clean Architecture, feature folders | simple and standard | Six projects with inward references enforced by `LayerTests`; one endpoint calls one plain handler, grouped by feature. (Stale story text: DES-1; enum duplication: DES-16) |
| AD-2 EF Core behind `IAppDbContext` | simple and standard | Handlers use EF Core through a two-member interface; raw SQL only where EF cannot express it, in one named Infrastructure method |
| AD-3 Errors | justified complex (wording long) | Expected failures are `Result` values with one of eight kinds, mapped to RFC 9457 ProblemDetails in one place, and the framework's own 401, 403 and 429 get the same body |
| AD-4 Time, money, identifiers | simple and standard | Read `TimeProvider` once per use case and pass the instant down; money is `decimal` / `numeric(19,2)` and IDs are UUIDv7. (`now()` wording: DES-16) |
| AD-5 HTTP API shape | simple and standard | Minimal APIs under `/api/v1`, one endpoint per action, keyset paging and camelCase JSON, with the built-in OpenAPI document committed and drift-checked. (Converter: DES-13) |
| AD-6 Zitadel | justified complex | Zitadel owns sign-up, verification and passwords; DisputeDesk reaches it only through standard OIDC and JwtBearer |
| AD-7 BFF session | justified complex, over-engineered at the SPA edge | The Api is the BFF: the cookie holds a server-side session key, tokens never reach the browser, and the store enforces 15-minute idle and 12-hour absolute limits. (DES-8, DES-9) |
| AD-8 Credentials and check order | over-engineered | The scheme-pinning half is two sentences (DES-4); the check-order half is not |
| AD-9 Operations roles and actors | simple and standard, over-built map | One role per action plus `dispute.read`, read only from this project's role claim. (DES-5) |
| AD-10 Authorisation states | simple and standard | A deny-by-default fallback plus `Unlinked` and `Linked` policies; the `Linked` handler resolves the customer from `CustomerLinks` on every request |
| AD-11 Ownership filter | simple and standard | A named EF Core query filter scopes disputes and their stored events to the current customer, bypassed only in named places checked by a test. (DES-7) |
| AD-12 Linking | justified complex (wording accreted) | Every well-formed start returns the same 202 and queues an in-memory job that matches, checks the cap and the SIM swap, then claims and sends the OTP; verify counts each attempt atomically and links on a correct code within three attempts. (DES-1, DES-6) |
| AD-13 Aggregate, replay, customer view | over-engineered (by scope), under-specified as one rule | Aggregate methods own every rule and append one stored event each; customers see a mapped view through an allowlist. Replay and the read model: DES-2 |
| AD-14 One commit path | justified complex | One `SaveChanges` override writes the stored event, the read-model row and, for published types, the outbox row in one transaction, and refuses updates to insert-only tables |
| AD-14a Audit vocabulary | justified complex (wording accreted) | Closed sets of actions, results and actor values owned once; the audit log holds linking, refusals, operations reads and sign-in and sign-out, never a successful dispute change. (Meaning drift: DES-5) |
| AD-15 Concurrency | justified complex (follows from DES-2) | The unique `(DisputeId, Version)` index catches every concurrent change; the loser reloads once and decides again, else 409 |
| AD-16 Idempotent raise | justified complex | The IETF Idempotency-Key semantics, stored per customer for 24 hours, completed in the raise's own transaction under a lease |
| AD-17 Outbox relay | justified complex | One Worker relay, leader by advisory lock, publishes each dispute's outbox rows in version order at least once |
| AD-18 SLA sweep | justified (escalation), over-engineered (warning) | Every 30 seconds the Worker claims due disputes with `SKIP LOCKED` and escalates them through the aggregate. (DES-3) |
| AD-19 Event contract | simple and standard | Two CloudEvents 1.0 types built from stored events by one mapping, no personal data, JSON Schemas under `contracts/events/` |
| AD-19a `raised.v1` fields | simple and standard | Thin event; consumers read detail through `dispute.read` |
| AD-20 Trace and signals | simple and standard | One W3C trace from browser to Worker through the outbox row, and named OpenTelemetry business metrics with no personal data |
| AD-21 Ports and stub | simple and standard, one speculative member | Three ports to external systems, a separate stub service, and the standard resilience handler tuned so its behaviour can be shown. (DES-14) |
| AD-22 Data model | simple and standard (wording accreted) | PascalCase EF defaults, one writer per table, migrations at start-up; a `Failed` outbox row is released by one documented SQL statement |
| AD-23 Gateway edge | simple and standard | nginx owns the security headers and a coarse per-IP limit; the Api trusts forwarded headers only from the configured network |
| AD-24 Cookies per environment | simple and standard | HttpOnly, SameSite=Strict cookies everywhere, `__Host-` and Secure in AWS, all from configuration |
| AD-25 Health and shutdown | simple and standard | Api health endpoints; Worker heartbeat files read by Compose; 30-second graceful shutdown |
| AD-26 Configuration and secrets | simple and standard | One validated options class per section, secrets only from the environment, feature switches as configuration; Scalar holds no credential |
| AD-27 Compose platform | simple and standard | The fixed set of runtime units, nginx the only entry, images non-root and pinned by digest |
| AD-28 AWS dev environment | justified (Terraform), over-engineered (hardening on an unapplied environment) | One EC2 instance runs the same Compose stack with RDS and CloudFront, deployed through a GitHub OIDC role. (DES-10) |
| AD-29 Telemetry export | simple and standard, gap in AWS | Hosts export OTLP only; the Aspire Dashboard locally, a collector to CloudWatch in AWS. (DES-15) |
| AD-30 Seed data | over-engineered | The seeder raises and moves each fixture dispute through the aggregate and the normal commit, idempotently. (DES-6, DES-7) |
| AD-31 Frontend contract | simple and standard | React with Orval-generated hooks from `openapi.json`, auth state from `/bff/session`, problems routed by full `type` URI |
| AD-32 CI and release | simple and standard | One `ci.yml` with fixed jobs plus `security.yml`; `publish` on main pushes SHA-tagged images to GHCR |

| Record | Verdict |
| --- | --- |
| ad-001 Zitadel | simple and standard; accurate restatement of AD-6 |
| ad-002 `IAppDbContext` | simple and standard |
| ad-003 Result types | simple and standard |
| ad-004 Model roles | out of place: a delivery-process record in the solution's set (DES-12) |
| ad-005 QA data integrity | simple; a process record, but it explains the seeded-state guarantee, so it earns its place |
| ad-006 Actor-agnostic actions | simple and standard, except the actor map (DES-5) |
| ad-007 BFF session | simple and standard; honest about the key at linking |
| ad-008 Linking | justified complex; the "What bounds OTP guesses, stated as it is" section is a model of honest scoping |
| ad-009 Stored events | standard and well cited, beyond the requirement that existed (DES-2, DES-3) |
| ad-010 Redpanda | justified; the best-evidenced record in the set |
| ad-011 Team folders | over-engineered for its purpose (DES-11) |

## 4. Verdict and top five changes

The design is by the book almost everywhere: every mechanism of substance cites Microsoft Learn, an RFC, OWASP or a named pattern, and no AD invents a protocol. Its weakness is accretion rather than invention. Rules grew by dated amendment until several (AD-7, AD-8, AD-12, AD-13, AD-30) cannot be said in two sentences, and three of them now rest on framework source instead of documented behaviour. Two scope additions arrived after the requirements and had requirements written around them: event sourcing (`SlaWarningRaised` most of all) and the team-folder model. Security, the primary area, is met once and well: the BFF, scheme pinning, the ownership filter and the linking design are each justified by a requirement and a standard. The one place where requirements outran the delivery is AWS (R38's deploy, SM-1 and SM-2 in CloudWatch), and the done-when text has not been updated to match.

Top five changes, by value for effort:

1. **DES-4:** use one framework check order (401, 403, 400, 404, 409) and delete `DisableValidation`, the post-lookup body checks and the `AcceptsMetadata` workaround. [decision], M.
2. **DES-1:** cut every AD to Rule, Prevents and Rejected; move dated rationale and story ownership out; delete the divergences section. [doc], M.
3. **DES-3:** drop `SlaWarningRaised` and the warning half of the sweep; deny-by-default stays proven by the test-only type. [decision], M.
4. **DES-5 with DES-9:** every bearer caller holding the role is `operations` (no actor map, no second meaning of `ActorType`), and one Data Protection key ring in Postgres in both environments. [decision], S each.
5. **DES-10 with DES-15:** restate R38, the done-when and NFR-OBS-4 to what v1 delivers (a validated Terraform plan; browser metrics local only), and freeze AWS-only mechanisms until an apply is possible. [decision], S.

DES-2 (event sourcing itself) is the largest simplification available and the most expensive. Given it is built and tested, the proportionate step is to record in ad-009 that the pattern was chosen to demonstrate it, not because a requirement forced it, and to quote Microsoft's own "when not to use" line.
