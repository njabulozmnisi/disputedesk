# brief-es chain: architecture

Source: `docs/verification/brief-es-decisions.md` (decisions 1 to 14) and `docs/verification/brief-es-rewording-rules.md`. Spine: `docs/architecture/architecture.md` (memlog `docs/architecture/.memlog.md`). Record: `docs/decisions/ad-009-stored-events.md`. Reviewer findings: `docs/architecture/reviews/review-brief-es.md` (0 critical, 7 high, 15 medium, 8 low; each finding's resolution is listed there).

## Changed ADs

- Paradigm, AD-22 table list, ER diagram, sequence diagram, capability map: `Disputes` is the read model; `StoredEvents` is in the data model from the migration that creates `Disputes`; no separate timeline table (decisions 13, 14).
- AD-4: the instant goes to `StoredEvents` and the outbox; `Audit.Record` stamps audit rows; the seeder's back-dated instants are the one exception (F19, F28).
- AD-7: sign-out through the OIDC handler's `SignOutAsync`, story 2.8 (F20).
- AD-10: deny-by-default fallback policy with `AllowAnonymous()` on BFF and health, story 2.8, cited (F20).
- AD-11: the filter on `StoredEvent` through its dispute covers loading by replay and the timeline.
- AD-13: handlers load by replaying the stream through `Dispute.Replay` and decide from it; `Disputes` is a read model written in the same transaction; 3.8 owns the customer timeline mapping, the aggregate holds no timeline (decisions 13, 14).
- AD-14: the commit writes the read model row with the stored event and outbox row.
- AD-14a: customer refusals audited as `Rejected`; seeder actor `Automation` / `worker:seeder` [ASSUMPTION]; `DisputeRead` settled.
- AD-15: `UQ_StoredEvents_DisputeId_Version` is the only concurrency check; `Disputes` has no `xmin` token (every `Disputes` write commits with a stored event, so a row token checks the same conflict twice; EF Core *Handling concurrency conflicts*). A `DbUpdateConcurrencyException` is never a clash (F1). Retry instant is the later of the use-case instant and the stream's latest `OccurredAtUtc` (F12).
- AD-17, AD-19: 3.8 tests stored event ID = outbox ID; 4.1 tests published `id` = outbox ID (decision 9).
- AD-18: the sweep loads claimed disputes by replay; `SlaWarningRaisedAtUtc` is a read model column.
- AD-21: `TotalRequestTimeout` is the whole-call deadline from 2.1 (F7).
- AD-30: seeder actors; open seeded disputes have `SlaDueAtUtc` beyond the warning lead time (F29).
- Deferred and Open questions: resolved items taken out; production path written by 7.6; lead time settled in 5.3.
- ad-009: title number, loading by replay, read model, concurrency rule, decision 9 test split.
- Reviews and memlog: reworded to the stored-events design under rules 4 to 9; facts kept; F2, F25, F26 and reconcile F17 marked resolved by the design (ad-009).

## Skipped

- F20's `Result` values and handler events for `SignIn` and `SignOut`: story 2.8's detail, not an architecture rule.
- F24: the glossary is outside this pass's files (open question).
- F2's migration ID test, F10's SQL-built `Data` parity and F26's AD-2 exception: `StoredEvents` is filled only by the AD-14 commit, so they have nothing to check.

## Open questions

1. `Disputes` without `xmin` departs from the token settled on 2026-10-26. Decision 13 makes the unique index the concurrency rule. A human confirms. The merged `DisputeConfiguration` maps `xmin`; story 3.8 brings the code into line.
2. The raise handler's refusal audit belongs to 3.8 [ASSUMPTION].
3. The seeder's operations-side actor is `worker:seeder` [ASSUMPTION].
4. The glossary's "Timeline" covers status changes only, but operations also see `SlaWarningRaised`.
