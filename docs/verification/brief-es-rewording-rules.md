# Rewording rules: the final design is the design from the start (brief-es)

Decided by the human on 2026-10-29. Every file in the repo (documents, stories, requirements, records, and later the code and migrations in story 3.8) describes the stored-events design as the design from the start.

1. Source of truth: `docs/verification/brief-es-decisions.md` (decisions 1 to 14) and `docs/glossary.md`.
2. `StoredEvents` is the record of dispute changes from the first dispute story (3.1). There is no `TimelineEntries` table, `TimelineEntry` entity, timeline-entry write, backfill, or migration that creates or drops one. The timeline is built from a dispute's stored events.
3. Keep the public API name `TimelineEntryResponse` and the `timeline` array: they are the customer view of the timeline, not a table. Any other sentence about "timeline entries" means the items of that view.
4. No language that describes the design's history: "no longer", "removed", "replaced", "superseded", "amended", "backfill", "migrate from", "used to", "changed by decision N". State the rule as it is. Cite the AD number or `ad-009` rather than "decision N" where an AD exists.
5. A successful dispute change writes a stored event and no audit row; this was always the rule. A version clash reloads the stream and decides once more (AD-15).
6. Stories 7.9, 7.8a, 7.8b and 1.6 are not in the plan. Remove every mention; work they held is stated where it lives now (2.8 for sign-in and sign-out audit, the OIDC sign-out, the fallback policy and Production-environment tests; the production path for the EF-only idempotency claim; tooling debt for a session-long `stack` lock). 2.1's `TotalRequestTimeout` is the whole-call deadline.
7. The decision record is `docs/decisions/ad-009-stored-events.md` (ad-001 to ad-008 are planned in 7.6). Fix every `ad-005-stored-events` reference.
8. Keep facts that are facts: commit SHAs, dates, verdicts, test counts, metrics, findings and who caught them. Reword only how the design is described. Never invent a result.
9. Neutral voice (no person named or in the third person); the quality areas are solution requirements; glossary terms only.
10. Out of scope: git history, `backend/`, `frontend/`, vendored skill files under `.claude/skills/bmad-*` and `bmod-*`, `_bmad/`, `e2e/`, `fixtures/`, `.playwright-mcp/`. Version numbers like `1.6` are not story references.
