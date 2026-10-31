# e2e tooling fix: QA record

Small-tier, test code and test configuration only (no production code). Tree: worktree `e2e-fix` from main `21040a86b07f65a56f7a568a47d51c6a3cc798ec` plus the uncommitted e2e change. Machine: one stack at a time under `scripts/test-lock.sh run stack e2e-fix -- ...`. No personal data, cookies or tokens appear below.

> Superseded in part by "Fix round 1" at the end: the run order is now seven groups, `short-budget.compose.yml` and `ageEarlierSendsTo` are gone. The sections below are the record of the first pass.

## Run order (documented in `e2e/run-regression.sh`, also `npm test` in `e2e/`)

Customer D links once per database and takes three OTPs an hour, and the linking limits differ between specs, so each group runs on its own fresh stack (`docker compose down -v`, then `up -d --wait` with the override files merged by `-f`). Each group is a Playwright project (https://playwright.dev/docs/test-projects) run with `--project`.

| # | Project | Linking limits | Overrides | Specs |
|---|---|---|---|---|
| 1 | `linking-rate-limits` | production (5 per login, 20 per address), `E2E_LINKING_RATE_LIMITS=production` | `small-queue` | linking-rate-limits |
| 2 | `linking-screens` | production | `small-queue` | linking-screens |
| 3 | `linking-verify` | raised (1000) | `small-queue`, `raised-limits` | linking-verify |
| 4 | `regression` | raised | `small-queue`, `raised-limits`, `short-budget` | every other spec, linking-start among them, plus `telemetry-scan.spec.ts` |

`journeys` is its own project on its own fresh stack (`npm run journeys`, unchanged). Override files: `e2e/raised-limits.compose.yml` (6.3 QA C1), `e2e/small-queue.compose.yml` (queue capacity 2, C3), `e2e/short-budget.compose.yml` (lock-wait budget 20 s, only for linking-start). `docker-compose.yml` is not edited.

## Item 1: span privacy scans

`support.ts`: `telemetryValues(json)` parses the OTLP JSON and keeps every string and number except `traceId`, `spanId`, `parentSpanId` and the `*UnixNano` timestamps (attribute, event and exception values, span names, status messages); `holdsCode(text, code)` requires a non-hex character on both sides. ID, account and cellphone numbers and HMACs stay raw substring checks. Applied to the spans and dashboard-log scans of linking-screens, linking-start and linking-rate-limits; in linking-start the criterion 8 table dump, API log and topic scans use the same boundary for the OTPs (5.4 QA F2: a TraceId column held six digits). linking-verify already matched OTPs as whole tokens, so it was left.

Proof:
- `telemetry-scan.spec.ts` (no stack): a code inside a trace id, span id, parent id or nanosecond timestamp is not a hit although the raw JSON contains it; a code in an attribute value or an exception event is a hit; a code inside a longer hex word or number is not.
- Throwaway break per scan on a live stack (edit reverted, diff checked clean afterwards): a fake attribute `x=424242;` injected into the fetched spans and `424242` added to the codes. linking-screens failed with "spans holds a typed code", linking-rate-limits with "spans holds an OTP", linking-start with "spans holds an OTP".
- The full runs below no longer hit the old false matches.

## Item 2: customer D

One project and one fresh stack per D spec (table above). Two spec fixes the new order exposed:
- linking-screens: the full-queue journey ran after D was linked, where the job ends before it waits on D's lock (`LinkingJobSteps.cs` checks "already linked" before the lock), so four waiters never appeared. It now runs before the real link, and afterwards waits for the held jobs and moves D's send stamps out of the hour (`ageEarlierSendsTo` in `support.ts`, through the database owner; qa_readonly cannot write).
- linking-verify: the released-challenge test expected `OtpSentAtUtc` null; since the 2026-10-30 decision (`OtpSends.ReleaseAsync`) a release keeps the stamp, so the claim counts toward the cap. The test now expects that, and `startFor` ages earlier sends (except the expiry challenge, whose send time a later test reads) because the spec sends to D more than three times.

## Item 4: data budget and outbox-relay-failure-paths

- `dispute-withdraw.spec.ts:332` failed because it asserted no Withdraw audit row at all for login B, while `dispute-detail.spec.ts` leaves a `Withdraw Rejected` row for B earlier in the run (reproduced on a stack after the first five specs). Now scoped to rows since B's raise in this spec. `:347` failed only because Playwright reruns `beforeAll` after `:332` fails, and by then the first describe had used the reserved rows (1 of 8 left). The reservations in `support.ts` hold.
- `outbox-relay-failure-paths.spec.ts:200` ("hold no customer data") needed an Error producer span on one of the spec's own rows, read from the trace page text. In a full run the failed sends (`Local_MsgTimedOut`, two in the window) belonged to the previous spec's row (TXN-A-0035, operations-read), still Pending when the broker stopped, so the spec's traces had no Error span (reproduced; the telemetry API showed the Error spans on that other trace). It now counts Worker spans with status Error (OTLP code 2) in the outage window plus the 10 s send timeout from `/api/telemetry/spans`. `:239` failed only through the `beforeAll` rerun.

Other test fixes found by the first runs (all test-side, none weakened):
- linking-screens full queue and rate-limits: the 20 s budget override ended jobs held on D's lock while more logins signed in; the budget override now applies to the regression group only.
- linking-start 6.5 scan: Mailpit is restarted by earlier 6.5 tests, so the OTPs read by the criterion 8 test are kept (`otpsSeen`) for the span scan.
- linking-verify criterion 8: the topic gains records from the SLA sweep timer during the run, so "linking emits no event" now requires the topic growth to equal the growth in outbox rows (linking writes none), after the relay has sent every row.
- linking-start 6.5 "6 logins start in parallel": six simultaneous starts with a queue of 2 could answer the sixth 429 (run E, 1 failure) because the first jobs had not left the channel; now four at once, then the two that queue once four jobs wait on the lock.

## Runs

First runs (A to E) found the failures listed above; each was fixed at its cause. Final code, two consecutive runs of `e2e/run-regression.sh`, each from `docker compose down -v`:

| Run | rate-limits | screens | verify | regression | Result |
|---|---|---|---|---|---|
| F | 7 passed | 6 passed | 9 passed | 231 passed, 1 skipped | green, exit 0 |
| G | 7 passed | 6 passed | 9 passed | 231 passed, 1 skipped | green, exit 0 |

The one skip is `scalar-disabled.spec.ts` (needs `SCALAR_OFF=1`, 5.5's own override). Run D, the same code before the linking-start fix, was also green (231 passed). Run E, with the linking-start race, failed once in that test and nothing else. `npm run journeys` on a fresh stack (before the last linking-start edit; `e2e/tests/journeys/` is untouched and shares only `support.ts`): 5 passed. The stack was stopped after each series (`docker compose stop`); no container runs now. OTLP ports 4317 and 4318 were free, so no override was needed.

Remaining flaky: none seen in F, G, or D. Candidates to watch (seen once, fixed): the linking-start parallel start race (run E). The `libgssapi_krb5.so.2` lines in the log come from the Worker's Kafka client in the container and are not test output.

## Fix round 1

Tree: main `ff24438d79edb042838580d5d03832f60f2c3972` plus the batch (applied with `git apply --3way`, clean) plus fixes 1 to 9, all uncommitted. Fixes from `e2e-fix-fixes.md`, review in `e2e-fix-review.md`. Human decision on M3: ad-005 stands, no database write resets customer D. No personal data, cookies or tokens below.

### Changes

1. **M3.** `ageEarlierSendsTo` and its three uses are removed. The over-cap tests moved to more fresh-stack groups (below). `run-regression.sh` now also reads customer D's claimed OTP sends from the read-only role after each group, prints the count and fails the run when it is over 3.
2. **M1.** `failedProducerSendSpansBetween` counts only `span.kind === 4`, `span.name === 'send disputedesk.disputes'` (OutboxRelay.SendAsync), status Error and an `error.type` attribute. The four `any`s are typed (nit).
3. **M2.** `Linking:Budget` defaults to 10 s (`LinkingOptions.Budget`). `short-budget.compose.yml` is deleted; the regression group exports `E2E_LINKING_BUDGET_SECONDS=10` so linking-start's budget test (it only needs the value, waits about the budget and allows budget+15 s) runs against the real default. No other group needs a budget override.
4. **M4.** Seven explicit `run_group <project> <limits>` calls; no shellcheck suppression (shellcheck clean).
5. **L1.** The privacy scan also asserts that every outbox row created since the baseline has `EventType = disputedesk.dispute.status-changed.v1`, the SLA sweep's type.
6. **L2.** `holdsCode` treats a preceding `.` as no boundary; `telemetry-scan.spec.ts` gained a microsecond-fraction case (4 tests, no stack).
7. **L3.** The fixed `pause(5000)` is gone with the reset it fed; the full-queue test now waits until each of the six held starts has a `Linking*` audit row.
8. **L4.** `docker compose ... up -d --wait --build || return 1` in `run_group`.
9. **Nits.** Skip text now says capacity 2; the double blank line is gone; `wholeToken` replaced by `holdsCode` in linking-verify; `document` renamed `spanDocument`; header comments and `raised-limits.compose.yml` updated.

### Group membership and customer D sends

Projects are in `e2e/playwright.config.ts`; the same table is in the header of `e2e/run-regression.sh`. Linking-verify tests are picked by tag (`@verify-a`, `@verify-b`, `@verify-c`); the full-queue journey by title (`full queue:`).

| # | Project | Limits | Holds | D sends (run 1 / run 2) |
|---|---|---|---|---|
| 1 | `linking-rate-limits` | production | linking-rate-limits | 3 / 3 |
| 2 | `linking-screens` | production | linking-screens without the full-queue journey (login 1: 1 send, login 3: 1 send) | 2 / 2 |
| 3 | `linking-queue` | production | the full-queue journey alone (six held starts, 3 claim sends) | 3 / 3 |
| 4 | `linking-verify-a` | raised | criterion 5 bounds; no-challenge; wrong-code lock (1 send; builds the reference sequence); blank challenges (0); released challenge (1); privacy scan | 2 / 2 |
| 5 | `linking-verify-b` | raised | a later start supersedes (2); the 5-minute expiry (0); privacy scan | 2 / 2 |
| 6 | `linking-verify-c` | raised | the race for D (2); privacy scan | 2 / 2 |
| 7 | `regression` | raised | every other spec, linking-start among them (its 6.5 tests send exactly 3) | 3 / 3 |

Counts are `select count(*) from "LinkingChallenges" where "CustomerId" = <D> and "OtpSentAtUtc" is not null` through `qa_readonly`, taken before each stack is removed (released claims count, as the cap does). Every stack is at or under 3.

A first attempt at the split used two verify groups (a/b with 3 sends each). The released-challenge test failed there (`referenceSequence` is built by the lock test, which was in the other group), so the group was cut to three. The privacy scan's "at least 3 HMACs and codes" floor became "at least 1", because a stack now holds one or two; the scan still runs on all three stacks.

### Throwaway breaks (edits reverted, confirmed by grep afterwards)

- **M1.** Producer span name changed to `send disputedesk.disputesX`, `outbox-relay-failure-paths` run on a fresh stack: "a send attempted during the outage has an Error producer span", expected >= 1, received 0. (A second failure, the data budget, is the `beforeAll` rerun after the first failure, as seen before.) Unbroken, the same assertion passed in runs 1 and 2.
- **L1.** Baseline replaced by `1970-01-01`, `linking-verify-c` run on a fresh stack with the run-script overrides: "a new outbox row of a type other than the SLA sweep status change", expected 0, received 8 (the seeded `raised` rows). Unbroken it passed in runs 1 and 2. (A first try started the stack without the raised limits and failed in the race test on 429; that was my command, not the break.)

### Runs (each group from `down -v`, under `scripts/test-lock.sh run stack e2e-r1`, main ff24438 start order)

| Run | rate-limits | screens | queue | verify-a | verify-b | verify-c | regression | Result |
|---|---|---|---|---|---|---|---|---|
| 1 | 7 passed | 5 passed | 1 passed | 6 passed | 3 passed | 2 passed | 231 passed, 1 skipped | exit 0 |
| 2 | 7 passed | 5 passed | 1 passed | 6 passed | 3 passed | 2 passed | 231 passed, 1 skipped | exit 0 |

The skip is `scalar-disabled.spec.ts` (needs `SCALAR_OFF=1`). The aborted first attempt of run 1 (two verify groups) is not counted. `npm run journeys` on a fresh stack: 5 passed. All six stacks came up with the api after the gateway. Stack stopped; no container runs now. OTLP ports were free. `npm ci` was run in `e2e/` (node_modules is untracked).

Not touched: `docker-compose.yml`, `.github/`, `e2e/tests/journeys/`, `forbidOnly`. The README `npm test` line is the supervisor's at merge.

## Delta fix (D1 to D5)

Base: main `ff24438d79edb042838580d5d03832f60f2c3972` plus the staged e2e batch (`e2e-batch-final.patch`); the five fixes are unstaged on top. Test-only, no database writes.

| Point | Change |
|---|---|
| D1 | `linking-screens.spec.ts` full-queue test: "the held jobs stopped" now counts rows for the six held actors with `"Action" like 'Linking%'` excluding `("Action" = 'LinkingStart' and "Result" = 'Succeeded')` and waits for `4 + queueCapacity` stop rows (mirrors linking-start's "job stops" filter); it logs the count each poll. Comment rewritten to say so. |
| D2 | `run-regression.sh`: `down -v --remove-orphans \|\| return 1`. |
| D3 | `linking-start.spec.ts` header: budget is the 10 s default Api setting, `run-regression.sh` sets both variables. |
| D4 | `linking-verify.spec.ts` `settledTopicCount`: after the 30 s loop, `expect` that no outbox row is unsent, so it fails with a message instead of returning a count. |
| D5 | `run-regression.sh` header and `check_d_sends` comment: groups are planned at most 3 D sends, a group planning more fails in its own tests first, the D check confirms the app's cap held and records the count. |

Commands and results:

- `shellcheck e2e/run-regression.sh` (no suppression): clean, exit 0.
- `npx playwright test --list --project=<p>` (no test added or removed): linking-rate-limits 7, linking-screens 5, linking-queue 1, linking-verify-a 6, linking-verify-b 3, linking-verify-c 2, regression 232 (231 run + 1 skipped, as in the earlier full run).
- Fresh stack, production limits, small queue, as `run_group linking-queue production` from `run-regression.sh` (throwaway wrapper in /tmp that copies the script's variables and function): `scripts/test-lock.sh run stack e2e-d -- /tmp/e2e-d-run/queue-group.sh`
  - Log: `held job stop rows: 3 of 6`, then `held job stop rows: 6 of 6`. The wait no longer passes at the six 202s; it waited for the job stops.
  - `1 passed (34.0s)`; `=== group linking-queue: 3 OTP sends to customer D (cap 3 an hour) ===`; exit 0.
- Throwaway break (wait target changed to 7, reverted after): same command on a fresh stack. Log reached `6 of 6` and repeated until the 120 s limit, then `Error: timed out waiting for the held jobs stopped`, `1 failed`, D sends 3, exit 1. Target restored to `4 + queueCapacity`; `git diff` shows no trace of the break.
- `docker compose stop` run afterwards (the failed break run skipped the wrapper's own stop because of `set -e`); no `disputedesk` containers running.

Only the linking-queue group was re-run; the other groups are untouched by D1 to D5 apart from the comment and the D2 and D4 guards, which only add failure paths.
