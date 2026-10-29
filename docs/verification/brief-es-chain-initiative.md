# brief-es chain: initiative

Sources: `brief-es-decisions.md` (decisions 1 to 14) and `brief-es-rewording-rules.md`. File changed: `docs/initiative-disputedesk.md`; entries appended to `docs/.memlog.md`.

## Changed items

- Description: every dispute change is a stored event, and every raise and status change is published as a dispute event (decisions 1, 4a).
- R20: when two actions race, the losing handler reloads the stored events and decides once more, succeeding only if the new status still allows the action, otherwise 409 (decision 4b, AD-15).
- R29: load by replay; the `Disputes` row is a read model in the same transaction, proved by a replay test; `StoredEvents` exists from the migration that creates `Disputes`, and no migration creates a table a later one drops (decisions 13, 14).
- Notes: the 7.8a/7.8b split line is gone; the standards-audit and deadline-scope lines name no 7.8a; the 7.9 line now places the work in 2.8, 6.3 (`AllowedHosts`), 2.1's `TotalRequestTimeout`, the production path (EF-only idempotency claim) and tooling debt (session-long `stack` lock) (decisions 5, 10, rule 6).
- Notes: the decision-records line names ad-009 beside ad-001 to ad-008; "AD-10 and AD-30 amended" reads "(AD-10, AD-30)".
- R22: the SLA warning lead time is settled in story 5.3 (memlog; the R line needs no number).
- 4.1 criterion split (decision 9): the initiative and PRD name no story criteria for it, so nothing changed.

## Open questions

1. Notes line "AD-13 governs seeding ... under a fixed seeding clock" stands beside decision 12's "back-dated instants"; they look compatible (instants back-dated from one seeding instant), but the architecture pass should confirm the wording.
2. `docs/tickets/README.md` and the epic-7 `tickets.toml` still list 1.6, 7.8a, 7.8b and 7.9 (not owned here).
