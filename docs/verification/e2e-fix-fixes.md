# e2e tooling fix: fix round 1

From the review (e2e-fix-review.md). Human decision on M3 (2026-10-31): keep ad-005 ("QA only reads"); no database write resets customer D.

1. **M3:** remove `ageEarlierSendsTo` and its three uses; instead split the over-cap tests onto more fresh-stack groups (the linking-screens full-queue journey in its own project; linking-verify split so no stack sends customer D more than 3 OTPs in an hour), in the documented run order. Assert in the run script or config comment which group holds which spec.
2. **M1:** count only the producer send span: `span.kind === 4` and `span.name === 'send disputedesk.disputes'` (OutboxRelay.cs:339-341), and require its `error.type` attribute.
3. **M2:** find what each group actually needs against the 10 s `Linking:Budget` default; drop `short-budget.compose.yml` if nothing needs it, otherwise name it for what it does with the real, observed reason.
4. **M4:** four (or however many) explicit `run_group` calls with literal arguments; no shellcheck suppression.
5. **L1:** also assert every outbox row created since the baseline is the SLA sweep's event type.
6. **L2:** `holdsCode` treats a preceding `.` as no boundary (microsecond fractions).
7. **L3:** wait for the six held jobs' stop audit rows instead of a fixed pause (if that test survives item 1's split).
8. **L4:** `docker compose … up -d --wait --build || return 1` inside `run_group`.
9. **Nits:** stale skip text; double blank line; the four new `any` typed; one boundary helper instead of two where they mean the same; rename `document` in telemetry-scan.

The README's `npm test` line (story 7.7) is updated by the supervisor when this batch merges.
