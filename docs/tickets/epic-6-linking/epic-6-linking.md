---
id: 6
type: epic
title: "Logins link to customers without revealing a match"
parent: initiative-disputedesk
covers: [R4, R5, R6, R7, R8, R9, R10, R31, R36, R40]
sources: []
companions: []
after: []
risk: high
---

# Logins link to customers without revealing a match

## Description

An unlinked login links to its customer with an ID number, an account number and an OTP sent off the request path after the SIM-swap check, under the send cap, lockout and rate limits, with every step audited and nothing revealing whether the details matched.

## Outcome

New logins link safely without disclosure; signal: SM-3 from the linking metrics, SM-C2 as its counter-metric.

## Requirements

R4 (parent R4): An unlinked login starts linking with an ID number and account number and gets the same status and body whether the details match, match nothing, or match a customer with a linked login; the OTP goes off the request path, only on a match with no linked login, to the cellphone on the bank's record; malformed input gets 400 naming the field without echoing it; starting again invalidates the earlier challenge.
R5 (parent R5): The SIM-swap check runs before any OTP is sent and fails closed on failure, error or timeout, with an unchanged response and an audit entry; codes entered afterwards behave as wrong codes, ending in "We can't link you online. Please contact the bank." (seeded customer C).
R6 (parent R6): At most 3 OTPs go to one customer per hour across all logins and IP addresses; further requests get the same response, send nothing and are audited.
R7 (parent R7): An OTP is 6 digits, stored only as an HMAC, expires at issue plus 5 minutes and works once; a correct code makes the login a linked customer with no new sign-in; with no active challenge, verification answers as a wrong code; the 4th attempt after 3 wrong codes fails even when correct; parallel verifications never exceed 3 attempts or 1 success, and of two logins racing for one customer the loser gets the generic failure.
R8 (parent R8): Linking start and verify together are limited to 5 per login and 20 per client IP in any 15 minutes (429 with `Retry-After`, rejected requests not counted); forwarded headers are trusted only from the known proxies.
R9 (parent R9): Every endpoint enforces one authorisation state: anonymous gets 401 on customer and linking endpoints, an unlinked login gets 403 "linking required" on customer endpoints, a linked customer gets 403 "already linked" on linking endpoints; the customer is resolved on every request from the link table by login, never from the request or the token subject. This epic delivers: the linking-route states (linked login gets 403 `already-linked`).
R10 (parent R10): Every link and every linking failure, every refused or forbidden dispute action, every operations read, and every sign-in and sign-out (R41) is written to an audit log the application only appends to; linking entries carry time, login, outcome and client IP, never the ID number. A successful dispute change (raise, withdraw, operations actions, SLA escalation) is recorded as a stored event in `StoredEvents`, which has the same insert-only guard as the audit log, and writes no audit entry. v1 has no unlink endpoint. This epic delivers: linking audit entries.
R31 (parent R31): ID numbers are never stored or logged, card numbers are kept and shown only as first 6 and last 4 digits, account numbers only as last 4, both masks checked where data enters from core banking; logs are structured, carry the trace ID, and logs, traces, metrics and published events carry no personal data; stored events hold `CustomerId`, `AccountRef`, `TransactionId` and the snapshot as private records with the same access as the `Disputes` table, are never logged or traced, and are kept under NFR-DATA-7. This epic delivers: the ID number never stored or logged.
R36 (parent R36): The SPA, Api host and Worker emit OpenTelemetry traces, metrics and logs, and one trace runs browser to API to outbox to Kafka to Worker with trace context injected by hand; the business metrics in NFR-OBS-3 are emitted; the Aspire Dashboard (locally) and CloudWatch (AWS) show them with no manual setup. This epic delivers: linking metrics.
R40 (parent R40): The customer portal meets WCAG 2.2 AA (contrast, keyboard access, visible focus, labelled errors, reflow at 320px), checked by axe in the E2E tests, plus a recorded manual keyboard pass. This epic delivers: the linking screens.

## Done when

- A new login links to customer D with the OTP from Mailpit and lands on Transactions.
- Customer C's codes fail and end in the locked message; the cap, lockout and rate limits hold under tests.
- Responses are identical for match, no match and already-linked; integrated verification passed.

## Boundaries

- No unlink endpoint in v1 (R10).
- Lost-login recovery is bank-assisted (prd §6.2).

## References

- parent — docs/initiative-disputedesk.md, section Requirements
- architecture — docs/architecture/architecture.md, AD-7, AD-12, AD-14a, AD-15, AD-23, AD-31
- experience — docs/ux/EXPERIENCE.md
- design — docs/ux/DESIGN.md
- prd — docs/prd/prd.md
- verification — docs/verification/

## Notes

- Decision: 2026-10-26 — epic Requirements are the parent's lines only, ids kept (`R<n> (parent R<n>)`); each line names the part this epic delivers where the parent line is shared.
- Decision: 2026-10-26 — tracer bullet: story 1, Unlinked login starts linking with a uniform response and an OTP sent after the SIM-swap check; it is the thinnest path through every layer this epic touches.
- Waits on 2.1, 2.3, 2.8, 3.2 because: 1 needs what those stories build (see its References and Description).
- Waits on 2.4, 3.7 because: 4 needs what those stories build (see its References and Description).
- Waits on: epic 2 (the Unlinked policy, `CustomerLinks`, the core-banking port and `AuditLog.Record` from story 2.8), epic 3 (`AuditLogEntries`).
- Decision: 2026-10-26 — Gate 2: code in every builder-owned story of this epic is stupid simple and logical, per AGENTS.md (smallest change that meets the criteria; every abstraction earns its place).
- Open question (from 3.1 review, 2026-10-28): this epic needs the `LoginSub`, `ClientIp` and `Details` audit columns; `AuditLogEntries` has only the columns `AuditLog.Record` (story 2.8) writes. Decide which story adds them and their migration (likely 6.1) before epic 6 starts.
