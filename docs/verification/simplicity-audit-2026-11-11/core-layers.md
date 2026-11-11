# Simplicity and standards audit: core-layers (2026-11-11)

Set: `backend/src/DisputeDesk.Domain`, `DisputeDesk.Application`, `DisputeDesk.Infrastructure` (migrations excluded), read
from the lows-2026-11-10 worktree. Rules and scale as in the 2026-11-03 baseline: AGENTS.md "How to work",
`docs/conventions/backend.md`, `review-story` pass 2 and its standards check. High = home-grown where a framework or
standard way exists, or code a new engineer cannot follow; medium = an abstraction, wrapper, option or indirection that
does not earn its place; low = local cleverness, naming, dead code. Read only; no tests were run. Paths below are
relative to `backend/src/`.

None of these findings is already in `docs/verification/lows.md` (it has no open core-layer row), so all are new.

## Findings

| ID | Severity | Tag | File:line | Finding | Simplest fix |
|---|---|---|---|---|---|
| CORE-1 | low | [code] | `DisputeDesk.Application/Disputes/GetDispute/GetDisputeHandler.cs:29-32`, `Disputes/WithdrawDispute/WithdrawDisputeHandler.cs:69-72`, `Operations/Actions/OperationsActionHandler.cs:73-76`, `Operations/ReadDispute/ReadDisputeHandler.cs:42-45`; `DisputeDesk.Infrastructure/Sla/SlaSweep.cs:213-217` | The query that loads a dispute's stream (`StoredEventRow` by `DisputeId`, ordered by `Version`, no tracking) appears five times. That breaks the "abstract on the third real case" rule; only whether the ownership filter is ignored differs between copies. | Add one query on `StoredEventRow` (for example `StreamOf(this IQueryable<StoredEventRow>, Guid disputeId)`) in `StoredEventRow.cs`, and have each caller apply `IgnoreQueryFilters(["Ownership"])` before it where it bypasses today. |
| CORE-2 | low | [code] | `DisputeDesk.Application/Disputes/RaiseDispute/RaiseDisputeHandler.cs:97-105`, `Disputes/WithdrawDispute/WithdrawDisputeHandler.cs:105-112`, `Operations/Actions/OperationsActionHandler.cs:131-139`, `Operations/ReadDispute/ReadDisputeHandler.cs:31-35` | The refusal audit (`AuditLog.Record`, then `SaveChangesAsync(CancellationToken.None)`, with the same three-line NFR-SEC-10 comment) is copied four times. | Add `AuditLog.RecordRefusalAsync(db, …)`, which records the entry and saves it with `CancellationToken.None`. Keep the comment once, there. |
| CORE-3 | low | [code] | `DisputeDesk.Infrastructure/Seeding/SeedDispute.cs:67,72`; `Seeding/Seeder.cs:60-66,104-110` | Dead parameter. Both callers `continue` when `everDisputed` is true and only then call `dispute.Write(everDisputed)`, so `Write` always gets `false`. | Remove the parameter and pass `everDisputed: false` to `Dispute.Raise` inside `Write`. |
| CORE-4 | low | [code] | `DisputeDesk.Infrastructure/Sla/SlaSweep.cs:191-195,203-205` | `EscalateIfOverdue` returns `DisputeErrors.ActionNotAllowed` (a 409 error) to mean "not due yet", and the caller reads any failure as "nothing to do". An error type is used as a sentinel for a normal outcome. | In `ChangeAsync`, write `if (dispute.SlaDueAtUtc > instant) return false;`, then call `dispute.Escalate(SweepActor, instant)` and drop `EscalateIfOverdue`. |
| CORE-5 | low | [code] | `DisputeDesk.Application/Disputes/WithdrawDispute/WithdrawDisputeHandler.cs:32-48`; `Operations/Actions/OperationsActionHandler.cs:34-50` | "Decide once more on a version clash" is written as two copied `try` blocks, each with an empty `catch`, so control flow runs through the empty catches. `SlaSweep.TryChangeAsync` (`SlaSweep.cs:157-179`) is the same rule written as a plain two-pass loop, so one rule has three shapes. | Use the `for (int attempt = 1; attempt <= 2; attempt++)` shape from `SlaSweep` in both handlers. |
| CORE-6 | low | [code] | `DisputeDesk.Application/Transactions/TransactionCursor.cs:12,23` (compare `Disputes/ListDisputes/DisputeCursor.cs:16` with `DisputeDesk.Api/Disputes/DisputeEndpoints.cs:86`) | The transactions cursor's length bound is checked by hand inside `Decode`. The disputes cursor's bound uses built-in minimal API validation (`[MaxLength]`) at the edge, which is the AGENTS.md framework-first approach. Ranked low, not high: it is a one-line comparison, not a mechanism. | Put `[MaxLength(TransactionCursor.MaxLength)]` on the transactions endpoint's `cursor` and remove the length test from `Decode`. Source: https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis#validation |
| CORE-7 | low | [code] | `DisputeDesk.Domain/Disputes/DisputeErrors.cs:24-25`; `DisputeDesk.Application/Linking/LinkingErrors.cs:12,30` | `VersionClash`, `ChallengeClash` and `LinkClash` never reach HTTP (their own comments say so) but carry the public problem slug `action-not-allowed`. That slug is shared with `DisputeErrors.ActionNotAllowed`, so the errors differ only by record equality, which `Error.cs:5-6` notes compares dictionaries by reference. A reader takes them for wire errors. | Give each internal error its own code (`version-clash`, `challenge-clash`, `link-clash`) so that code alone tells them apart. |
| CORE-8 | low | [code] | `DisputeDesk.Domain/Transactions/Disputability.cs:12-13`; `DisputeDesk.Infrastructure/Seeding/SeedDispute.cs:28-29` | The AD-4 fact "South Africa is always UTC+02:00" is written twice, comment included, as two private constants. | Make `Disputability.SouthAfricanOffset` public and use it from `SeedDispute`. |
| CORE-9 | low | [code] | `DisputeDesk.Application/Operations/Actions/OperationsActionHandler.cs:141` | `NotAllowedIn(status)` is a private one-line wrapper that only forwards to `DisputeWire.ActionNotAllowedWithDisputeStatus`. | Call `DisputeWire.ActionNotAllowedWithDisputeStatus` directly at its two call sites (lines 59 and 88). |
| CORE-10 | low | [code] | `DisputeDesk.Infrastructure/Persistence/CommitRows.cs:14,21` | The name `CommitRows` needs its summary ("the publish mapping") to explain it. The class builds only the outbox row; the commit's other row comes from `StoredEventRow.From`. | Rename it `PublishMapping` (or `OutboxMessages`) to match AD-14's term. |
| CORE-11 | low | [code] | `DisputeDesk.Application/Transactions/ListTransactions.cs:16` | `ListTransactions` is the only use-case class not named `<Feature>Handler` and not in its own feature folder; its sibling is `Disputes/ListDisputes/ListDisputesHandler.cs`. | Rename it `ListTransactionsHandler` and move it to `Transactions/ListTransactions/`. |
| CORE-12 | low | [code] | `DisputeDesk.Infrastructure/Outbox/OutboxRelay.cs:24-29,37,221` | The production constructor has an optional `Func<ProducerBuilder, IProducer>` used only by tests. It relies on DI passing a default value for a parameter it cannot resolve, which needs a summary paragraph to explain. | Keep the seam but say what it is: mark it `// shortcut: test-only seam to inject delivery failures, remove when a broker fault injector replaces it`, and shrink the paragraph to one sentence. |

No high or medium findings. These were checked and are not findings: the AD-14 commit path in `AppDbContext`,
`UniqueViolations` with `UniqueViolationException` (Application may not reference Npgsql, so mapping the constraint name
in Infrastructure is the layering rule at work), the raw SQL in `OtpAttempts`, `OtpSends`, `IdempotencyKeyClaims` and
`SlaQueries` (each is one named method with its justification, as `backend.md` requires), the sync/async seeding pair
(EF Core's documented `UseSeeding`/`UseAsyncSeeding` pattern), the standard resilience handler, options validation, the
bounded-channel queued background task, and the OpenTelemetry propagator use in the relay.

## Counts

| Severity | 2026-11-03 baseline | 2026-11-11 |
|---|---|---|
| high | 0 | 0 |
| medium | 1 | 0 |
| low | 8 | 12 |

The baseline report is no longer in the tree, so findings are compared by kind, not ID. The kind the baseline ranked
medium (an indirection that does not earn its place) no longer appears at medium. All 12 lows are new: none matches an
open row in `lows.md`.

## Trend

High stays at 0 and medium drops from 1 to 0, so the epic-8 simplicity stories removed the abstraction-level findings in
this set. Lows rise from 8 to 12. Most are copied code from later stories: the stream load, the refusal audit and the
clash retry, each past its third real case. The rest are small leftovers: a dead parameter, a sentinel error, a
pass-through wrapper and two names. No new abstraction or home-grown mechanism appeared.
