# Security: session fix

Security, base `commit "docs: stories 3.6, 3.9, 4.2 and 6.1 ready to build, with their decisions and pre-build reviews" (2026-10-30)`. Verdict: pass, no high or medium. Secret scan clean, semgrep `p/csharp` 0 findings.

| Sev | Finding | Action |
|---|---|---|
| low L1 | The idle-slide `UPDATE` filters on `KeyHash` only, not on the row still being live | add `row.ExpiresAtUtc > now && row.AbsoluteExpiresAtUtc > now` to its `Where` (fix round) |
| low L2 | The idle limit is checked once on the server (the row) instead of twice | none |

Open question, routed to the linking story: a link re-issue must sign out before signing in, and must carry over the original absolute limit.

## Round 1 (delta)

Security: pass. L1 closed: the slide's `UPDATE` carries the live-row condition, checked atomically by the database. Nothing new in the delta. Secret scan clean; semgrep 0 findings.
