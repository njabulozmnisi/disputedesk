# brief-es chain: pipeline records

Source: `docs/verification/brief-es-rewording-rules.md` and `brief-es-decisions.md`. Scope: the `2.*` and `3.*` records in `docs/verification/` (there are no `1.*` records). Commit SHAs, dates, verdicts, statuses, test names and counts, metrics and escapes rows are unchanged.

## Files changed

- `2.2-qa.md`: the re-run section states finding 1 as closed and the sign-out test as it is ("superseded", "no longer", "now" removed).
- `2.3-review.md`: the note on break rows that do not apply to the merged code, without "superseded" or "no longer".
- `2.3-security.md`: the deny-by-default fallback policy is filed for story 2.8.
- `2.4-qa.md`: the stack counts read 10 dispute records, each with its outbox row (lines 89 and 124), and name no table the check could not have read.
- `2.4-review.md`: the flaky Data Protection key race and the `<h1>` focus and skip link are open follow-ups with no story assigned (in the narrative and the escapes row).
- `2.4-verify.md`: the known flake points to `2.4-review.md`.
- `2.5-review.md`: L2, N1 and N2 are open follow-ups with no story assigned.
- `2.7-review.md`: the constant problem `title` per type is an open follow-up with no story assigned (decision row and finding 3).
- `3.1-challenge.md`: both rounds describe the raise as the dispute, its `DisputeRaised` stored event and the outbox row in one save; the row-count asks drop "four"; the round 1 heading drops "superseded".
- `3.1-fixes.md`: item 9 lists the losing `Dispute`, its stored event and its `OutboxMessage`.
- `3.1-security.md`: the navigation filter is on the dispute's stored events; the stored event's contents and the append-only override; S3 names the stored event's `TraceId`.
- `3.1-qa.md`: lines 50, 61, 64, 67, 70 and 83 state the observations neutrally: one dispute record and one outbox row per transaction, counts "9 dispute records and 9 outbox rows" then 10 and 10, the outbox trace id is the one the dispute record was committed under, and foreign keys, one instant and the ASVS V7.3.3 line name the dispute record.
- `3.1-review.md`: the ownership-filter break row (a code break) names the dispute's stored events; the Docker `qa_readonly` line (74) reads "one dispute record and its outbox row".
- `3.1-verify.md`: criterion 3 is "row counts"; criterion 9 names the direct-query test on the dispute's stored events.
- `3.2-challenge.md`: the pre-mortem and the simplest design name the dispute's stored event and outbox row.
- `3.2-fixes.md`: R2 is an open follow-up with no story assigned.
- `3.2-qa.md`: count columns are D|R|O|K (disputes, the record committed with each dispute, `OutboxMessages`, `IdempotencyKeys`); every tuple drops the audit column and per-transaction rows are `1|1|1`; lines 9, 13, 53, 55 and 58 name no table the check could not have read, and the trace id is the one the dispute record was committed under.
- `3.2-review.md`: R2 and QA finding 1 are open follow-ups with no story assigned; the EF-only claim is a production-path item; the session-long `stack` lock is tooling debt.

## Sentences reworded with care (check before accepting)

1. `2.4-qa.md`, `3.1-qa.md`, `3.1-review.md` and `3.2-qa.md` (human decision, 2026-10-29): the observed rows were a timeline row and an audit row per raise. The observations are restated neutrally as one dispute record with its outbox row under one trace ID, naming no table the check could not have read. The 3.2 QA count columns for those two rows are merged into one (the observed values were equal in every tuple). Design descriptions elsewhere stay in stored-event language.
2. `3.1-review.md` and `3.1-verify.md`: the test `TimelineEntries_QueriedDirectly_AreFilteredThroughTheirDispute` is described as the direct-query test on the dispute's stored events, and only the name suffix is kept. Restating it under a new full name would invent a test name.
3. Items routed to 7.8a, 7.8b, 7.9 or 1.6 that rule 6 gives no home (problem `title` per type, the `smoke.sh` and `isApiResponse` items, shared JSON options R2, the `<h1>` and skip link, the Data Protection key-race flake, the lost-race `fail:` log lines) are written as "open follow-up, no story assigned". A home is needed: the production path, tooling debt or a planned story.

## Left as facts

- "AD-2, AD-8, AD-16, AD-21 ... amended" in the 2.x and 3.x decision tables records what each story changed in the documents. It is not about the stored-events design.
- `2.1-review.md`: the 5-second deadline as merged (a linked `CancellationTokenSource`) is a fact of the 2.1 commit. Rewording it to `TotalRequestTimeout` would contradict the commit.
- Audit test names and fix 11 (`ProductionSource_FileNamingAuditEntries_...`, `SaveChanges_AuditEntryModified_...`) and `DELETE FROM "AuditLogEntries"` refused: the audit log exists and shares the insert-only guard.

## Remaining grep hits

`grep -nE 'TimelineEntr|timeline entr|DisputeEvents|backfill|7\.9|7\.8a|7\.8b|1\.6|superseded|no longer'` over `2.*` and `3.*`:

- `2.2-qa.md:61` "7.9 s": a test timing.
- `2.4-qa.md:7`, `2.5-qa.md:7` "1.64.1": the Playwright version.
- `3.1-qa.md:12` "one timeline entry `action` `raise`": an item of the API response's `timeline` array (rule 3).
- `3.1-verify.md:51` `DisputeEvents.cs:9`: a source file in the 3.1 commit holding the published integration event type (`disputedesk.dispute.raised.v1`), a dispute event in the glossary sense.
