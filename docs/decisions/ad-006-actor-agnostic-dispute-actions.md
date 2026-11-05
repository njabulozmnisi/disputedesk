# ad-006: Actor-agnostic dispute actions

**Status:** accepted (records AD-9, AD-13 and AD-14a)

**Context.** Three kinds of actor change disputes: the customer (raise, withdraw), an operations tool acting for the bank (start review, escalate, resolve) and an automated process (the SLA sweep's escalation, and the seeder's start review, escalate and resolve). If each actor had its own code path, the allowed-actions table (FR-24) would be copied and drift, and a rule could depend on who is asking rather than on the dispute's status (AD-13, Prevents). The glossary defines an **action** as a named change any authorised actor may perform, and an **actor** as recorded, never used to decide.

**Decision.** One aggregate method per action, the same for every actor; the actor is recorded, never consulted (AD-13, AD-9, AD-14a).

- `Dispute.Raise`, `StartReview`, `Escalate`, `Resolve(outcome, note)`, `Withdraw(note)` and `RecordSlaWarning` are the only ways to create or change a dispute. Each takes an `Actor` and the use-case instant, returns `Result`, and appends one stored event (AD-13, ad-009).
- The operations escalate and the SLA sweep call the same `Escalate` (AD-13, FR-26).
- Authorisation happens before the aggregate, per action: one Zitadel project role per action (`dispute.startReview`, `dispute.escalate`, `dispute.resolve`) plus `dispute.read`, one policy per role, and ownership for the customer's withdraw (AD-9, AD-11, FR-24).
- `Operations:Actors:<sub>` maps a service account's `sub` to the actor type `operations`; no mapping means 403 (superseded 2026-11-03, see the note below). The Worker acts as `automation` with identity `worker:sla-sweep`. The seeder acts as `automation` with `worker:seeder` for start review, escalate and resolve, and as the fixture customer for raise and withdraw (AD-30, AD-14a).
- Actor type and identity are written to the stored event and, for refused or forbidden actions, to the audit row, with the same values; they are never passed into a decision (AD-9, AD-14a).
- The action names, actor types and result values are closed sets owned once: Domain owns `ActorType` and `DisputeAction`, Contracts owns `AuditAction` and `AuditResult` (AD-14a).

**Options rejected.**

- *Coarse roles, OAuth scopes as permissions, or Zitadel Actions* (AD-9): AD-9 prevents roles that encode who the caller is; a role names what the caller may do.
- *Rules in handlers, or a state-machine library* (AD-13): the aggregate owns every rule in one place.
- *Free-text action names* (AD-14a): five stories would each invent their own action names, actor IDs and result values.

**Consequences.**

- Whether an action is allowed depends only on the dispute's status, through the one allowed-actions table in Domain (FR-24, AD-13); who may ask is settled earlier, by the role or ownership check.
- A new operations tool is one `Operations:Actors:<sub>` mapping and its role grants, with no change to the aggregate (AD-9) (superseded 2026-11-03, see the note below).
- Customers never see actor types or identities: the customer view maps the timeline actor to `you` or `theBank` (AD-13), and published events carry `actorType` but no actor identity (AD-19).

**Superseded in part, 2026-11-03 (human decision D4, simplicity audit; story 8.12).** The `Operations:Actors:<sub>` map is replaced by a plain allowlist, `Operations:AllowedClients`, an array of service accounts' `sub` values. Every allowed caller is actor type `operations` with its `sub` as actor ID, and roles still decide what it may do; a `sub` not in the list gets 403, and an empty or missing list refuses every caller. A `Denied` entry records actor type `Operations` and the token's `sub`, so `ActorType` keeps one meaning, the route's credential type (AD-9, AD-14a, AD-26). A new operations tool is one `Operations:AllowedClients` entry and its role grants.
