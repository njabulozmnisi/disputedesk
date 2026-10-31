# e2e tooling fix: review (status: changes)

Reviewer, 2026-10-31, two passes plus standards. Tree: main commit "fix(platform): the api starts after the gateway that holds the auth.localhost alias, so CI's resolver never answers loopback; nginx finds the api at run time" (2026-10-31) with the batch staged (built on story 7.2 commit "test(e2e): 7.2 QA journeys UJ-2, UJ-5a, UJ-5b and UJ-5d on the real stack, with axe and a keyboard pass" (2026-10-31), applied cleanly). Ran `playwright test --list` per project (journeys 5; linking-rate-limits 7; linking-screens 6; linking-verify 9; regression 232 in 23 files), `telemetry-scan.spec.ts` 4/4 with no stack, `bash -n` and shellcheck on run-regression.sh (clean only through M4's suppression), `git diff --cached --check`. Not run: the stack groups on commit "fix(platform): the api starts after the gateway that holds the auth.localhost alias, so CI's resolver never answers loopback; nginx finds the api at run time" (2026-10-31) (QA's runs F and G were on story 7.2 commit "test(e2e): 7.2 QA journeys UJ-2, UJ-5a, UJ-5b and UJ-5d on the real stack, with axe and a keyboard pass" (2026-10-31); commit "fix(platform): the api starts after the gateway that holds the auth.localhost alias, so CI's resolver never answers loopback; nginx finds the api at run time" (2026-10-31) changes the Compose start order). Scope: no change to docker-compose.yml, .github/, journeys, backend or frontend; `forbidOnly` untouched; nothing in CI runs these files.

Items 1-4 met in substance (scans parse OTLP JSON and drop id and nano keys, non-hex boundary, raw 10- and 13-digit checks kept; documented run order through Playwright projects and a bash loop; Compose `-f` merge, rate-limits alone at production limits; test-side data fixes), with the findings below.

| # | Sev | File:line | Item | Issue | Fix |
|---|---|---|---|---|---|
| M1 | medium | `outbox-relay-failure-paths.spec.ts:128-136, 228` | "outbox-relay-failure-paths failed twice" | `workerErrorSpansBetween` counts any Worker span with status Error, not the producer send span, so the check that a failed send marks its producer span Error is loosened. | Filter `span.kind === 4` and `span.name === 'send disputedesk.disputes'` (OutboxRelay.cs:339-341), ideally requiring `error.type`. |
| M2 | medium | `e2e/short-budget.compose.yml:1-7`; `run-regression.sh:32` | "the raised linking limits and the small queue as committed Compose override files" | Says the production default is longer, but `Linking:Budget` defaults to 10 s (LinkingOptions.cs:36; architecture.md:157), so the file raises the regression group to 20 s; the stated reason for excluding screens and rate-limits contradicts the 10 s they run at. | Drop it and export 10 s, or name it for what it does with the real reason; recheck the full-queue timings at 10 s. |
| M3 | medium, [decision] | `support.ts:236-241` (`ageEarlierSendsTo`); used at linking-screens:485, linking-verify:161, :387 | "reset D's link and cap between them" | Scoped (one UPDATE of `LinkingChallenges.OtpSentAtUtc` for D, local container, as the owner role already used for fault injection at linking-start:568), but ad-005 says "QA only reads" and Gate 2 rejected a test-only reset of customer D. | Human decides: (a) a recorded ad-005 exception for cap ageing in e2e setup, or (b) more fresh-stack groups (the full-queue journey alone; linking-verify split so no stack exceeds 3 sends to D an hour). |
| M4 | medium | `run-regression.sh:40-41` | AGENTS.md: "No new suppressions" | New `# shellcheck disable=SC2086` for a word-splitting trick. | Four explicit `run_group` calls. |
| L1 | low | `linking-verify.spec.ts:631-632` | nothing weakens an assertion | "Linking emits no event" became "topic growth equals outbox growth". | Also assert every new outbox row is the SLA sweep's event type. |
| L2 | low | `support.ts:216` (`holdsCode`) | item 1 | Six-digit microsecond fractions (bounded by `.` and `+`/`Z`) can still match by chance. | Treat a preceding `.` as no boundary, or a `// shortcut:` comment. |
| L3 | low | `linking-screens.spec.ts:483-485` | | Fixed `pause(5000)` before ageing D's sends. | Wait for the six held jobs' stop audit rows. |
| L4 | low | `run-regression.sh:26-36` | | `set -e` is off inside `run_group` (called with `||`); no `--build`. | `up -d --wait --build \|\| return 1`. |
| Nits | | | | Stale skip text (linking-screens:443); double blank line (487-488); four new `any` (outbox-relay:131-136); two boundary helpers (`holdsCode`, linking-verify's `wholeToken`); `document` name shadows the DOM global (telemetry-scan:7). | Tidy in the fix round. |

Conflicts: story 7.1's criterion text (two projects, `npm test` = `--project=regression`) and story 7.7's README line about `npm test` are superseded by five projects and `npm test` = `bash run-regression.sh`; whichever merges second updates the README (7.7 merges first; this batch's fix round updates the README's `npm test` line).

## Fix round 1 delta

Reviewer, 2026-10-31, delta only. **Verdict: approve.** `playwright test --list` per project: 261 tests in 31 files, no spec in two groups or none (the verify privacy scan runs once per verify group by design). `telemetry-scan.spec.ts` 4 passed; shellcheck clean with no suppression. M1, M2, M3, M4, L1, L2, L4 and the nits are closed in the code; no database write remains (only `qa_readonly` reads). The privacy scan floor at ">= 1" is still a non-vacuity check: every verify group stores at least one HMAC and the code list also holds every submitted code. The linking-start "4 then 2" stagger asserts what the design guarantees.

| # | Severity | Where | Issue | Fix |
|---|---|---|---|---|
| D1 | low | `linking-screens.spec.ts:485` | L3 not closed in substance: the wait matches the request-time `LinkingStart` `Succeeded` audit row (`StartLinkingHandler.cs:41`), so it passes once the six 202s return; the D count can read before the claims commit. | Exclude `LinkingStart`/`Succeeded` and wait for `4 + queueCapacity` stop rows. |
| D2 | low | `run-regression.sh:44` | A failed `down -v` is ignored inside `run_group`, so a group could run on a stack that is not fresh. | `down -v --remove-orphans \|\| return 1`. |
| D3 | nit | `linking-start.spec.ts:387` | Header still says "a short budget, set outside the tree". | Update the comment. |
| D4 | nit | `linking-verify.spec.ts:42` | `settledTopicCount` returns silently after 30 s with rows unsent. | Throw on timeout. |
| D5 | nit | `run-regression.sh:6,31` | "fails a group that did" overstates: the app cap makes a fourth claim impossible. | Say the check confirms the cap and records the count. |

Watch item: `linking-queue` holds D's lock while seven browser sign-ins run against the 10 s default budget; it passed twice, margin unmeasured.

Supervisor ruling: lows alone do not open a fix round. The batch merges as approved once main is green; D1 to D5 go in one Small-tier test-only fix right after it (standing approval for test and tooling fixes).

D1 to D5 fixed before merge (QA lane, `e2e-fix-qa.md` "Delta fix (D1 to D5)"); the supervisor read the delta: each point closed as written, `--list` counts match the reviewer's (7, 5, 1, 6, 3, 2, 232), D1's break (target 7) timed out. The batch and the delta merge as one commit.

## Merge check (2026-10-31)

Applied on main `story 7.5 commit "feat(infra): 7.5 Terraform dev environment and OIDC deploy: one EC2 instance behind CloudFront with RDS, KMS and SSM, deployed by digest from GitHub Actions through SSM" (2026-10-31)` (7.5 merged). The README's e2e block now says what `npm test` does (`run-regression.sh`, seven fresh-stack groups, the customer D count, the stack stopped at the end) and drops the second manual `down -v`/`up`, since the script starts its own stacks; this also closes the 7.7 note on the five known failures. Supervisor checks: `telemetry-scan.spec.ts` 4 passed; `playwright test --list` journeys 5, linking-rate-limits 7, linking-screens 5, linking-queue 1, linking-verify-a 6, linking-verify-b 3, linking-verify-c 2, regression 232; shellcheck clean.

## Escapes

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
|---|---|---|---|---|
| A test reset customer D through the database owner, against ad-005 ("QA only reads") | reviewer | builder | non-standard mechanism | Human decision: more fresh-stack groups; `run-regression.sh` reads D's sends through `qa_readonly` after each group |
| The outbox failure check counted any Worker error span, not the producer send span | reviewer | builder | missing test | Filter on kind, name and `error.type`; break run shows it can fail |
| An override named "short budget" raised the budget | reviewer | builder | spec gap | Deleted; the regression group runs on the 10 s default |
| The full-queue wait matched the request-time audit row, so it passed at once | reviewer (delta) | builder | missing test | Count only job-stop rows; break with target 7 times out |
| Span scans matched OTP digits inside trace IDs and timestamps | QA | builder (5.4, 6.x) | flaky | `telemetryValues` plus `holdsCode`, tested with no stack |

## Metrics

Fix rounds: 1, plus one Small-tier delta fix (D1 to D5) before merge. Stages: build and QA on the M1, reviewer full plus delta, supervisor delta read. Full regression runs: first pass 7 (A to G), fix round 2, delta 1 group plus 1 break. Wall time: about a day elapsed. Disk: no notable change.
