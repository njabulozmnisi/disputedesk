# brief-es chain: PRD and product documents

Sources: `brief-es-decisions.md` (decisions 1 to 14) and `brief-es-rewording-rules.md`. Changes are logged in `docs/prd/.memlog.md` and `docs/product-brief/.memlog.md`.

## Changed items

- PRD §2.1: the actor's job is every change recorded as a stored event and every status change published (decision 1).
- PRD §3: "Dispute event", "Stored event" and "Timeline" carry the `docs/glossary.md` wording (Naming).
- PRD FR-13 (§4.4 description): raising builds the dispute row from the `DisputeRaised` stored event (rule 2).
- PRD FR-26: the sweep reloads the stored events on a version clash; the `SlaWarningRaised` lead time is settled in story 5.3 (decisions 4a, 4b).
- PRD NFR-DATA-1: the dispute row is a read model built from the stored events (decision 13).
- PRD NFR-DATA-2: a handler loads a dispute by replay; the `Disputes` row is a read model in the same transaction, proved by a replay test (decision 13).
- PRD NFR-DATA-6: `StoredEvents` is part of the data model from the migration that creates the dispute table; no migration creates a table a later one drops (decision 14).
- PRD NFR-DATA-7: stored events are never deleted (decision 11).
- PRD NFR-SEC-10: sign-in and sign-out entries use the same audit-entry fields as every other entry (AD-14a).
- PRD NFR-DOC-3: lists ad-009 stored events.
- PRD citations: "brief-es decision N" cites ad-009, with AD-14a, AD-15 or AD-30 where they hold the rule (rule 4).
- review-adversarial #11, review-rubric (Done-ness fix), review-triage, reconcile-inputs B2: design wording changed from audit entries to records and stored events; findings and verdicts kept (rule 8).
- product-brief "A history that can be trusted": every status change is a stored event with its dispute event in the same transaction.
- `brief-es-challenge.md`: points reworded to the current design and stated as settled by ad-009 (and AD-14a, AD-30 where they apply); verdict, tags and sources kept (rule 8).
- `docs/conventions/backend.md`: stored-event rules (append through aggregate methods, one exhaustive publish mapping, insert only, load by replay, reload once on a clash); `StoredEvents` in the table list; audit scope (ad-009, AD-13, AD-14, AD-14a, AD-15).

## Open questions

1. Decision 13 (load by replay) contradicts the current architecture AD-13 and `ad-009-stored-events.md`, which reject replaying on every load and read the snapshot. The PRD and backend conventions follow decision 13; the architecture owner has to align.
2. `ad-009-stored-events.md` still has the heading "ad-005" and a "3.8 backfill" sentence (not owned here).
3. The `.memlog.md` files are append-only (memlog convention) and gitignored, so earlier entries keep words like "superseded" and "replacing"; new entries follow the rules.
4. PRD FR-25 and NFR-SEC-10 audit operations reads; AD-14a lists `DisputeRead`, so this looks settled, pending the architecture pass.
