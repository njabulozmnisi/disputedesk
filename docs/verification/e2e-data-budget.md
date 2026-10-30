# e2e data budget

Test-only fix in `e2e/tests/` (QA-owned). Tracked in `flaky-fixes.md` ("e2e data budget"). Tested on main at `e172761debb5023f91e1ec1f5d468c822cbf0c59` plus the uncommitted e2e change. No personal data, cookies or tokens appear below.

## Problem

A transaction can be disputed once and the data is not reset between specs. Every spec that raised used "the first disputable row", so a full run drained customer A's 20 disputable transactions before the last specs ran. Story 5.4 seeds 7 disputes on ACC-A-1 (`TXN-A-0011, 14, 15, 18, 19, 22, 26`) and 1 on ACC-B-1 (`TXN-B-0025`), which leaves A 13 and B 19.

## Fix

`e2e/tests/support.ts` now holds `reservedTransactions`: one fixed, non-overlapping list of transaction IDs per spec and customer, and `ownDisputable(rows, spec, customer)`, which returns the spec's reserved rows that are still disputable, in reserved order. Every raising spec takes its row from that list. At import, `support.ts` throws if an ID is reserved twice or is one the 5.4 fixture disputes.

| Spec | Customer A | Customer B |
|---|---|---|
| dispute-raise (SPA clicks, so all on the first page of 20 rows) | 29, 28, 27, 13, 12 | none |
| dispute-withdraw | 3, 4, 5, 6, 7, 46, 47, 48 | 11 |
| dispute-stored-events | 8, 9 | 26 (first page, read by its browser test) |
| dispute-detail | 31 | 13 |
| dispute-list (tops A up to 3) | 32, 33, 34 | 14 |
| operations-read | 35 | 17 |
| operations-actions | none | 4, 5, 6, 7, 8, 9, 10 |
| outbox-relay-failure-paths | 36, 37, 38, 39 | 18, 19 |
| outbox-relay | 40, 41 | none |
| scalar-reference | 42 | none |
| spa-dispute-status-badges | none | 27 (first page) |
| spa-disputes-history (raises only when run alone) | 10, 43, 44, 45 | none |

Left unreserved so the read-only specs always find one: `TXN-A-0010` (also history's first pick, used only when history runs alone), B-0002, B-0021, B-0023.

Each spec's old demand was larger than the comments said: withdraw needs 8 on A (not 7); dispute-list tops A up to 3; badges and stored-events browser tests need a row on the first page of 20.

## What the fixture must add (not changed here; backend builder)

Customer A's specs reserve 31 transactions (history's alone-only picks included) and only 13 are disputable after 5.4, so the fixture must add 18 (B needs none: 15 of its 19). The additions: `TXN-A-0031` to `TXN-A-0048`, 18 settled card debits on ACC-A-1:

- dated 69 to 84 days ago (older than the 20th newest row, so the first page of 20 and the rows the SPA specs click do not move; inside the 90 day window);
- each with `kind: card`, `state: settled`, `direction: debit`, a masked card and an amount.

Without them the specs that reserve those IDs fail with an explicit message naming `reservedTransactions` in `support.ts`, not a silent miss. If the fixture prefers other IDs, change the IDs in `reservedTransactions` only.

Side effects the fixture change has on other tests (already handled in e2e): customer A grows from 30 to 48 rows, so `transactions.spec.ts` no longer pins 30 rows, and `spa-transactions.spec.ts` Load more follows the cursor to the last page instead of assuming a second page is the end. Backend tests that pin A's transaction count are the builder's to check.

Sharing and moves considered: scalar-reference and outbox-relay could move to customer B, but B has only 4 left over, so it would save 3 of the 18. History reserves its own because it raises nothing in a full run, only when run alone. No spec shares a transaction with another.

## Proof

The proof stack mounted a copy of `fixtures/local.json` with `TXN-A-0031` to `0048` added (a compose override in the QA scratch directory; `fixtures/` itself is untouched). Each run started on a fresh stack (`docker compose down -v`, then `up --wait --build`), behind `scripts/test-lock.sh run stack qa-e2e-budget`, OTLP ports moved to 14317/14318.

| Run | Commit built | Command | Result | Data-budget failures |
|---|---|---|---|---|
| 1 | e172761 | full suite, 203 tests in 20 files | 191 passed, 3 failed, 17 not run, 1 skipped | none |
| 2 | e172761 | full suite, same code | 137 passed, 6 failed, 68 not run, 1 skipped | none (3 of the 6 are `scripts/operations-token.sh` unable to read the clients file, a tooling flake; 3 as in run 1) |
| 3 | c6b9ed3 | full suite without the three linking specs | 168 passed, 3 failed, 19 not run, 1 skipped | none |

Failures in run 1 (all from other stories' uncommitted e2e work, in specs this change does not touch): `forwarded-headers.spec.ts:83` (story 6.6), `linking-start.spec.ts:486` (6.5), `linking-verify.spec.ts:237` (6.2). Not-run tests are the rest of those serial files.

Failures in run 3: `dispute-detail.spec.ts:264`, `dispute-raise.spec.ts:163` (heading after submit) and `spa-dispute-status-badges.spec.ts:76` (badge click opens the detail). All three assert the story 3.12 detail screen, which is not on main yet (the stack builds main), so the SPA answers "We can't find that dispute". They pass the raise step itself, so the data budget held. They will pass once 3.12 merges and the stack is rebuilt.

No run printed a `reservedTransactions` or "no reserved" message, which is what an exhausted list says. Before the change, run 1 of the earlier (unreserved) suite is the recorded failure: `outbox-relay-failure-paths.spec.ts:129`, `transactions.spec.ts:129` and `scalar-reference.spec.ts` ran out of customer A's rows.

A clean full green run is not shown: it needs the linking specs' stories (6.2, 6.5, 6.6) and 3.12 merged, and the fixture additions above. The fixtures were not changed, so on today's `fixtures/local.json` the specs that reserve `TXN-A-0031` and above stop with the explicit message.

## Other findings (not data budget)

- `forwarded-headers.spec.ts`, `linking-start.spec.ts` (modified) and `linking-verify.spec.ts` are another story's uncommitted work in `e2e/`. They fail on main in every run (forwarded-headers expected `172.18.0.1`, got `172.18.0.10`; linking-start 6.5 lock test timeout; linking-verify 403 vs 404). They are not touched here.
- `linking-start.spec.ts` criterion 8 failed once with "topic holds a secret" and once with "Sessions holds a secret" in earlier runs: it searches 6 digit OTPs and phone numbers as substrings in every table and topic record, so it gets more likely to match random digits as the data grows. Route to the 6.x QA.
- Run 2 lost 68 tests because `scripts/operations-token.sh` could not read the clients file from the provision volume ("is the stack up?") in `operations-actions`, `operations-read` and `scalar-reference`. A tooling flake, not data; run 3 repeats the full set.
