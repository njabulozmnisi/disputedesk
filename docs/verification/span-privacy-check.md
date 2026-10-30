# Span privacy check (investigation, not a story QA)

Tree: throwaway copy of main HEAD 6dbae5d (`tspan`), no 7.1 e2e changes. Fresh stack, regression order (every `e2e/tests/*.spec.ts`
alphabetically, one file per run). After each spec file a scan read `/api/telemetry/spans?limit=5000` and searched every span
(all string values, every attribute, trace/span/parent ids) for customer D's ID number, account number, the no-match account, the wrong
codes (111111, 222222, 333333), 123456, 654321, 1000000050 and every OTP in the Mailpit SMS sink. Output named only service, span name
and attribute path. No personal value is written here.

## Result 1: linking-screens.spec.ts:578 "spans holds a typed value"

Reproduced on the fresh stack (1 failed, 4 passed, 1 skipped; the failing test is the :578 scan, message "spans holds a typed value").

The only match in 3,614 spans (7 resources) is the wrong code `111111` inside a span id:

| Field | Value |
|---|---|
| service | `disputedesk-api` (scope `Microsoft.AspNetCore`) |
| span name | `GET /api/v1/disputes` |
| key | `parentSpanId` (16 hex characters, random W3C trace-context id), not an attribute |
| first seen | after `dispute-list.spec.ts` (clean after `dispute-detail.spec.ts`), so a browser or request of the list spec; the id is random |

A second coincidence appeared later in the same run (after `sign-in-and-out-audit.spec.ts`): one real OTP (a six-digit run inside a 19-digit nanosecond
`startTimeUnixNano` of a `postgresql` span of the new database resource). Same class: a number field, not an attribute.

A third coincidence in the final scans (after `spa-dispute-history`/`spa-transactions`): `111111` inside the 53-character string attribute at index 5 of the
`GET /api/v1/disputes/{disputeId}` server span of the Api, which by length (17 + a 36-character GUID) and position (after http.request.method,
server.address, server.port, user_agent.original, url.scheme) is `url.path`: a dispute GUID that happens to hold six 1s. It is a dispute ID, a
resource id the design already allows in url.path, not customer data. I could not re-query the key name: the stack was handed to another QA run
right after mine (the key is inferred from the span's attribute order, so this one is unverified).

No ID number, account number, no-match account or an OTP in an attribute value appears in any span, attribute, `url.path`, `url.query`, `db.statement` or
exception field, in any spec's scan, including after `linking-screens`, `linking-start` and `linking-verify`. No span attribute at all
contained 111111, 222222 or 333333.

Verdict: test artefact (b). The scan does `text.includes(secret)` over the raw JSON of the whole span document. Six-digit codes are
substrings of the 32-hex traceId and 16-hex spanId/parentSpanId values by chance: 16^-6 per window, about 59 windows per span, so
about 3.5e-6 per span per code. With 3,600 to 15,000 spans and about 8 to 20 codes (three fixed wrong codes plus every real OTP seen) a
false match is a coin flip on a large run and rare on a small one. That is why 6.4 alone (few spans) passes and a full regression run
(many spans, plus new random OTPs each run) fails intermittently, and why the failing value differs between runs. The same applies to
dashboard logs, api logs and the topic only for numeric fields (trace ids in logs), so the log scans can flake the same way.

Fix (e2e only, QA owns it): in `linking-screens.spec.ts` (and the same scans in `linking-start.spec.ts:647`, `linking-rate-limits.spec.ts:408`,
`linking-verify.spec.ts` if it scans the same way) parse the span JSON and compare only attribute values, status messages and event/
exception fields, not `traceId`, `spanId`, `parentSpanId`, timestamps or numeric fields. Keep the 10 and 13 digit secrets (ID number,
accounts) as raw substring checks (collision chance negligible); for six-digit codes require a non-hex boundary, e.g.
`new RegExp('(?<![0-9a-f])' + code + '(?![0-9a-f])')` on the attribute values. No production change is needed.

## Result 2: linking-start.spec.ts:141 "no LinkingOtpSent audit row appeared"

Shared state, not timing. Regression order runs `linking-screens.spec.ts` before `linking-start.spec.ts`. The screens spec's third login
links customer D (header of that spec: "Run on a fresh database, alone: it takes customer D's one link"). Audit rows from this run:

- 17:33:06.23 `LinkingAlreadyLinked` (screens spec, login 4)
- 17:33:11.55 `LinkingNoMatch`, then 17:33:11.95 `LinkingStart` and 17:33:11.96 `LinkingAlreadyLinked` for the D start of linking-start
  criterion 1, which expects `LinkingOtpSent`.

The row was written within 4 ms of the start, so the job was not slow; the outcome differs because D is already linked. The two
`LinkingOtpSent` rows in the run (17:32:32, 17:32:54) both belong to the screens spec. Tests 2 to 14 of linking-start were skipped
(serial mode). `linking-rate-limits.spec.ts` skips all 7 tests unless `E2E_LINKING_RATE_LIMITS=production` (and queue capacity vars) is set.
Fix: the specs that consume customer D's single link (screens, start, verify) need their own fresh stack or a reset of D's link between
them (the 7.1 run order has to put screens last or give each its own stack); not a code defect.

Other failures seen in this run, outside the question: `dispute-withdraw.spec.ts` (2 failed), `outbox-relay-failure-paths.spec.ts`
(2 failed), `linking-verify.spec.ts` (1 failed). Not investigated here.

## Method files

Scripts and logs are in the scratchpad `tspan-out/` (scan.mjs, where.mjs, driver.sh, run-*.log); they hold no personal values.
