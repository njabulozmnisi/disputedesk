---
id: 6
type: story
title: "Decision records and the production path"
parent: epic-7-release
covers: [R39]
after: []
hitl: false
risk: low
---

# Decision records and the production path

## Description

Writes decision records ad-001 to ad-008 (ad-004 exists) and `docs/production-path.md` with every prd §6.2 deferral and a reason; docs only.

## Requirements

Verify: Each new record named below follows the Nygard ADR form (status, context, decision, consequences) plus options rejected; every prd §6.2 bullet appears in `docs/production-path.md` with a one-line reason.

1. **Decision records** (R39; NFR-DOC-3)
   **Given** `docs/decisions/`
   **When** it is read
   **Then** it holds ad-001 Zitadel as the external identity provider, ad-002 `IAppDbContext`, ad-003 Result types, ad-004 model roles (exists, unchanged), ad-005 QA data integrity, ad-006 actor-agnostic dispute actions, ad-007 the BFF session (no tokens in the browser) and ad-008 linking (including the SIM-swap check before the OTP), each citing the AD numbers it records
   **And** ad-008 records the deviation from NIST SP 800-63B's limit on failed attempts: restarting linking creates a fresh challenge with 0 attempts, so the real bound on guesses is the per-login linking rate limit (FR-8) plus the send cap of 3 OTPs per customer per hour (FR-4)

2. **Production path** (R39; NFR-DOC-2, NFR-DATA-7)
   **Given** `docs/production-path.md`
   **When** it is read
   **Then** every prd §6.2 item and every spine Deferred item appears once with a one-line reason, including retention and de-identification jobs

## Supervisor checks

Deliberate-break proofs and Docker checks, run by the supervisor, not the verifier.

- None beyond the story flow.

## Build facts

- Owner: docs (docs agent, not a builder).
- Flags: none.
- Lane: docs.
- Touches: docs/decisions/ad-001 to ad-008 (except ad-004); `docs/production-path.md`.

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md

## Notes

- Decision: 2026-10-26 — Gate 2: the records follow kickoff amendment 1 (ad-001 to ad-008 as listed in criterion 1); the substitute topics chosen at slicing are dropped. The outbox and having no schema registry are recorded in the architecture spine (AD-14, AD-17, AD-19), not as separate records.
- Decision: 2026-10-26 — Gate 2: owner changes to `docs`, a docs agent rather than a builder.
- Decision: 2026-10-27 — standards audit: the production path records migrations as a migration bundle run as a one-off job instead of every replica migrating at startup, and the deferred operations stories (see the initiative's scope decision).
- Decision: 2026-10-27 — ad-008 records the NIST SP 800-63B deviation on restarted challenges and the bound that replaces it (criterion 1).
