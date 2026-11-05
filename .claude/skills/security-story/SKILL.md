---
name: security-story
description: Procedure for the security review of a story touching auth, identity, account linking or customer data, written to the security agent's own record. Used by the security agent and, on stories below High-stakes, as a checklist by the reviewer. (Named to avoid clashing with the built-in security-review command.)
---

# Security review of a story

Run the scanners that apply and read the diff against this list. The security agent changes no code; it writes only `<story>-security.md` in the main checkout's `docs/verification/` (the path is in the brief), and appends a re-run to it as a dated section.

- **AuthN/Z:** every endpoint tested in all three states (anonymous, authenticated-unlinked, linked); customer endpoints accept only the BFF cookie, action endpoints only bearer JWTs; one policy per action; the customer resolved from the identity link only.
- **BFF:** tokens never reach the browser; `__Host-` cookie, HttpOnly, Secure, SameSite=Strict; the antiforgery filter on every unsafe customer method; logout ends both sessions.
- **Linking:** identical response whether details match or not; OTP 6 digits, HMAC-hashed, 5-minute expiry, single use, lockout after 3; rate limits per user and IP; the phone from the bank's record only; ID numbers never stored or logged; link, unlink and failures audited.
- **Data:** ownership filter (stored events through their dispute), no other customer's data, ProblemDetails without stack traces, no personal data in logs, traces or published events; stored events never logged or traced; `StoredEvents` and `AuditLogEntries` insert-only (ad-009, AD-14).
- **Audit:** refused and forbidden actions, linking, operations reads, sign-in and sign-out each audited through `AuditLog.Record` (AD-14a).
- **Supply chain:** new packages verified and pinned; `dotnet list package --vulnerable`, `npm audit`, Semgrep, Trivy where relevant; secrets with `scripts/secret-scan.sh` run in the story worktree (the whole uncommitted change, untracked files included), never a scan of `src/` alone.
- **Headers:** CSP and security headers unchanged or tightened.
- **Standards:** each security mechanism (authentication, authorization, sessions, antiforgery, crypto, secrets, input validation) uses the framework's documented form and maps to an OWASP ASVS requirement; cite both. A home-grown security mechanism where a standard exists is at least a medium finding.

## Known pitfalls

- A privacy test checks every private field of the record it guards (3.8); sealed `ToString` on records holding personal data is enforced by `PersonalDataRecordTests`.
- A change that adds or changes infrastructure files (Terraform, Dockerfiles, Compose) gets `scripts/security-local.sh`, the Security workflow's Trivy filesystem and config scan (7.5).
- A cleared cache is not freed memory: a change that clears customer data in the SPA has a `WeakRef` plus forced-GC test that proves the data is collected (`frontend/src/app/sessionEndCollected.test.tsx`; 7.14).
- A test stand-in for a pinned upstream image sends that image's real headers and behaviour, read from the image itself (for example a Next.js `routes-manifest.json`), never assumed (7.12).

## Report

Severity per finding (high, medium, low). High and medium findings open a fix round; lows go to the epic's lows list. Any high finding is escalated for a decision before merge.

```json
{"status": "pass", "record": "docs/verification/<id>-security.md", "findings": [], "scanners": {}, "assumptions": [], "open_questions": [], "conflicts": []}
```
