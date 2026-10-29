---
name: qa-story
description: Procedure for data-integrity and data-movement QA of one story on the running stack, tracing each criterion through browser, network, database and events, plus the ZAP baseline. Used by the qa agent.
---

# QA a story

1. **Lock.** Everything below runs under `scripts/test-lock.sh run stack qa-<story> -- ...`. One QA run at a time; the stack starts once.
2. **Stack.** `docker compose up --wait` in the builder's worktree.
3. **Walk every criterion,** happy and unhappy paths, as a seeded customer signed in through Zitadel (Playwright MCP). Always include:
   - another customer's dispute (expect 404, no data leaked);
   - a resolved dispute that can't be withdrawn (expect 409);
   - for identity stories: sign up a new user, verify through Mailpit, link it; a wrong or expired OTP; an unlinked user calling a customer endpoint.
4. **Trace four hops per criterion, with concrete expected values:**
   - Browser: what the user sees.
   - Network: the right endpoint and status, no extra fields, no other customer's data.
   - Database, via the read-only role in `infra/db/`: the dispute's stored event, its `Disputes` row and (for a published type) the outbox row with the same ID, written in one transaction; audit rows only for refused or forbidden actions, linking, operations reads, sign-in and sign-out.
   - Events: the right shape and key on the topic, trace context in the headers.
5. **Standards.** Check observed behaviour against the governing standard: HTTP semantics and status codes (RFC 9110), problem details (RFC 9457), cookie attributes (RFC 6265bis), caching (RFC 9111), OWASP guidance for the area. A deviation is a finding even when the story text is silent.
6. **ZAP baseline scan** against the running stack; record new alerts.
7. **Evidence** to `docs/verification/<story>-qa.md`, keyed by commit SHA. Scrub `Authorization` headers, cookies, ID numbers and other personal data first (POPIA).
8. **Passing walkthroughs become committed Playwright tests** in `e2e/`. A failing one blocks the merge.

## Report

```json
{"status": "pass", "sha": "...", "evidence": "docs/verification/<id>-qa.md", "e2e_tests": [], "zap_new_alerts": [], "assumptions": [], "open_questions": [], "conflicts": []}
```
