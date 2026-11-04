# Epic 8 pre-build review and security read: stories 8.3, 8.9 to 8.17 (2026-11-04)

Base `fed7f85`. The reviewer and the security agent are read-only; the supervisor recorded their findings here. Human answers to the `[decision]` points are in `epic-8-decisions.md`. Every `[story]` point goes to Sofia.

## Review (correctness and design)

### 8.10, verdict changes
- [decision, answered: remove] `AcceptsMetadata([])` on resolve.
- [story, medium] Criteria 1 and 2 must keep the handlers' generic failure return (`WithdrawDisputeHandler.cs:76-80`, `OperationsActionHandler.cs:87-91`): without it a domain refusal commits a `Disputes` row with no stored event and returns 200 (pitfall 3.8). Update only their comments; read the validated outcome as raise does (`RaiseDisputeHandler.cs:33`), with no `!`; delete `DisputeErrors.OutcomeRequired` (add to Touches).
- [story, low] Raise's expected-amount check stays 400 after the lookup. State the trailing step ("…, 422, then 400 for a rule that needs the loaded resource") in criterion 3, FR-24 (`prd.md:444`), R20 and AD-8's Rule.
- [story, low] Criterion 7's expected openapi diff includes the `WithdrawDisputeRequest` and `ResolveDisputeRequest` schema descriptions ("checked after the dispute is loaded").
- [story, low] Boundaries: built-in validation writes its own messages; say the problem type, status and named field stay (or set `ErrorMessage` to keep today's text).
- Nits: the binding tests at `WithdrawDisputeInputTests.cs:80,89` need no rewrite; criterion 5's title is for a linked cookie (an unlinked cookie with `text/plain` already gets 415 before 403).

### 8.11, verdict changes
- [story, medium] `EnumSchemaTransformer.cs:11` crefs `SingleNameEnumConverter`; deleting it breaks the build (`TreatWarningsAsErrors`, doc file). Allow the doc-comment change; add to Touches.
- [story, medium] Docs-first list misses PRD NFR-API-6 (`prd.md:507`), `conventions/backend.md:10` and the architecture *Casing* row.
- [story, low] Inputs: request bodies only (query and route binding use `TryParse`, not System.Text.Json; no enum is read from a query or header today).
- [story, low] Criterion 6: an empty openapi diff is acceptable (the enum components are already plain lists from `EnumSchemaTransformer`).

### 8.12, verdict changes
- [story, low] Criterion 4: a rewrite of an old-shape clients file also regenerates the secrets; reword "nothing else changes".
- [story, low] Criterion 2 names the two tests the Supervisor checks rely on: a `sub` differing only in case gets 403; an empty or missing list gets 403 for every caller.
- [story, low] State "no openapi change".
- Low: the AD-14a docs change gives `ActorType` one meaning, "the route's credential type".

### 8.13, verdict changes
- [decision, answered: refetch on focus] Other tabs after a sign-out.
- [decision, answered: keep data, show notice] Failed sign-out.
- [story, medium] Criterion 2's "makes no API request" needs a mechanism: `RootLayout.tsx:50` fetches the session on every document, which renews a live session on `/signed-out`; name how the root skips the session query there and what the Header shows.
- [story, medium] Docs-first list adds EXPERIENCE rows `:36`, `:141` and `:142` (142 now reversed by the human).
- [story, low] Build the session-ended URL from `window.location.pathname + search` (an absolute `href` fails the BFF's `IsLocalUrl`), keeping today's `/transactions` and `/disputes` collapse.

### 8.14, verdict changes (migration safe under AD-22's pre-release rule)
- [story, medium] Criterion 4's no-gap test must cross the old 48-hour lead window (one sweep inside it, then one after due), or it passes without the change.
- [story, medium] Docs-first list adds R18 (`initiative:48`), AD-14a's closed set, AD-18's *Prevents*, `ad-006` line 9, and a dated superseding note in `ad-009` (lines 9, 14) rather than a rewrite.
- [story, low] Name the fate of the span tag `disputedesk.sla_sweep.claim` (drop, or fix at `overdue`) and the tests reading it (`SweepRig.cs:99`, `SlaBatchTests.cs:30,50,200,205`, `SlaHeartbeatAndPrivacyTests.cs:73`); `LogBatch`'s `{Claim}` template.
- [story, low] Add `Contracts/Operations/OperationsTimelineEntryResponse.cs:7-8` to Touches (source of the `openapi.json` description).
- [story, low] Criterion 1's exact `Down`: rely on the generated `Down` (reviewed in the diff) or make the check concrete (`database update AddSlaWarning`, compare `pg_indexes`).
- Boundaries state the pre-release incompatibilities: an old Worker fails its warning claim against the dropped column; a database not recreated holds `SlaWarningRaised` rows that replay refuses.

### 8.15, verdict changes
- [story, medium] Docs-first list adds R17 (`initiative:47`), PRD NFR-SEC-6 (`prd.md:584`), `conventions/backend.md:29`, AD-11's heading and AD-30's *Prevents*.
- [story, low] The first Supervisor check cannot fail (the global unique index on `AccountRef`, `TransactionId`); keep the customer predicate and replace the check with one on the query's `ToQueryString()` filtering on `CustomerId`.
- Nit: "inside the migration's transaction" holds only when no migration was pending; reuse the wording at `Seeder.cs:160-165`.

### 8.16, verdict changes
- [decision, answered: System.Text.Json] jq or System.Text.Json.
- [story, medium] Criterion 2: allow one stated anchored pattern, `^\s*(policy|assume_role_policy|policy_document)\s*=` followed by `templatefile(`, so `block_public_policy` and `*_protocol_policy` do not match; or drop the escape check.
- [story, low] Record why string placeholders are used where HashiCorp's `templatefile` page advises `jsonencode` for JSON; values are only ARNs, IDs, the region and paths, guarded by `validation` blocks.
- [story, low] Say how the network fetch behaves in an offline local run; the connect bound is `SocketsHttpHandler.ConnectTimeout`.

### 8.17, verdict proceed
- [story, low] If the Domain enums' XML summaries do not reach `openapi.json`, add a direct `ProjectReference` or `AdditionalFiles` (criterion 5's empty diff catches it).
- Nit: the comment at `RaiseDisputeRequest.cs:32` can point at `Dispute.NoteMaxLength`.

## Security read

No high findings.

- **8.3** [story, low] Add `"/\t/evil.example"` and `"/\r\n/evil.example"` to `Callback_NonLocalReturnUrl_RedirectsToRoot`, now that the SPA's WHATWG check goes. [story, low] `/signed-out` and the not-found route stay root children outside the three layout routes; no root `beforeLoad` reads the session.
- **8.9** [story, medium] No test proves `/otlp/` blanks `Cookie` and `Authorization`: add a local `GatewayTests` case (`POST /otlp/v1/traces` with `Cookie`, `Authorization`, `Forwarded` reaches the echo upstream with none) and a break check. [story, low] One case per proxied location on both gateways (`/api/health/`, `/api/`, `= /scalar`, `/scalar/`, `= /openapi/v1.json`, `/ui/v2/login`, identity `/`, and Mailpit in AWS), each asserting the client `Forwarded` (and in AWS `X-Origin-Verify`) is dropped; one per server for the four forwarded values; Mailpit's `Upgrade`.
- **8.10** [decision, answered] `AcceptsMetadata`. [story, low] `Outcome` stays nullable with `[Required]` (`default(Outcome)` is `Upheld`); read `.Value` after validation, never a default; add `"outcome": null` to criterion 4.
- **8.12** [story, low] The provisioner's current check is exact array equality, `.Operations.AllowedClients == [$opsUserId]`. [story, low] No `Operations` section in any `appsettings*.json` (the binder merges arrays by index across providers), and the handler refuses a null or empty `sub` before the lookup; two tests.
- **8.13** [story, medium] Keep the per-tab wall-clock deadline (`Date.now()` instant) and its `visibilitychange` and `focus` re-check; one Vitest case moves `Date` past the deadline without firing the timer and expects `location.replace` once. [decision, answered] Other tabs. [story, low] Keep the e2e "no customer data after Back" checks for every way out.
- **8.14** [story, low] State what a leftover warning row does (replay refuses; the sweep skips that dispute forever); rely on the `docker compose down -v` instruction.
- **8.15** [story, low] Allow the bypass in `Seeder.cs` only (name the file in `BypassFolders`), and add `DisputeDesk.Infrastructure.Seeding` to the customer-feature namespace rule. [story, low] Same Supervisor-check point as the review.
- **8.16** [story, low] Same anchored-pattern point; also no `jsonencode(` except the SSM `content` and no `aws_iam_policy_document`. [story, low] Pin each template's top-level keys (`Version`, `Statement`) and `Version` `2012-10-17`. [story, low] Criterion 7's recorded OIDC check says when to re-run it (repository transfer or rename, subject customisation change).
- Process note: 8.13 deletes `frontend/src/app/sessionEndCollected.test.tsx`, which a security-story pitfall line (7.14) names; that line changes when 8.13 merges (a skill change, to the human then).
