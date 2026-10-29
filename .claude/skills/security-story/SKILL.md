---
name: security-story
description: Procedure for a read-only security review of a story touching auth, identity, account linking or customer data. Used by the security agent. (Named to avoid clashing with the built-in security-review command.)
---

# Security review of a story

Run the scanners that apply and read the diff against this list.

- **AuthN/Z:** every endpoint tested in all three states (anonymous, authenticated-unlinked, linked); customer endpoints accept only the BFF cookie, action endpoints only bearer JWTs; one policy per action; the customer resolved from the identity link only.
- **BFF:** tokens never reach the browser; `__Host-` cookie, HttpOnly, Secure, SameSite=Strict; the antiforgery filter on every unsafe customer method; logout ends both sessions.
- **Linking:** identical response whether details match or not; OTP 6 digits, HMAC-hashed, 5-minute expiry, single use, lockout after 3; rate limits per user and IP; the phone from the bank's record only; ID numbers never stored or logged; link, unlink and failures audited.
- **Data:** ownership filter, no other customer's data, ProblemDetails without stack traces, no personal data in logs or traces.
- **Supply chain:** new packages verified and pinned; `dotnet list package --vulnerable`, `npm audit`, Semgrep, Trivy where relevant; secrets with `scripts/secret-scan.sh` run in the story worktree (the whole uncommitted change, untracked files included), never a scan of `src/` alone.
- **Headers:** CSP and security headers unchanged or tightened.
- **Standards:** each security mechanism (authentication, authorization, sessions, antiforgery, crypto, secrets, input validation) uses the framework's documented form and maps to an OWASP ASVS requirement; cite both. A home-grown security mechanism where a standard exists is at least a medium finding.

## Report

Severity per finding (high, medium, low). Any high finding is escalated for a decision before merge.

```json
{"status": "pass", "findings": [], "scanners": {}, "assumptions": [], "open_questions": [], "conflicts": []}
```
