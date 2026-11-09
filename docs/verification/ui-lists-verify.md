# Verification: ui-lists

- Commit: `2f897c37b3b58b11119c6126123213f98defeb43` (uncommitted diff hash `3c8a8b8405b1`)
- Verifier: gpt-6-astra via GitHub Copilot CLI
- Date (UTC): 2026-11-08T15:20:59Z

I’ll check the full uncommitted tree, read the saved test results, and run the applicable build and static checks. I won’t rerun tests or edit source files.


The frontend checks and saved Vitest results pass. The e2e code still has selectors for the old link name and amount element. Backend style checking also reported errors before restore; I’ll recheck it now that the backend build has passed, and compare the generated OpenAPI document.


VERDICT: FAIL

| Criterion | Test | Proved (yes/no) |
|---|---|---|
| AC1: lede and hidden header | `TransactionsScreen.test.tsx`: “shows the lede and lists a row older than 90 days”; “hides the column header from assistive technology” | yes |
| AC2: day bands | `TransactionsScreen.test.tsx`: “draws each day heading as a band inside the one panel, above its rows” | yes |
| AC3: responsive columns and badge target | `TransactionsScreen.test.tsx`: three “the row columns” tests; “shows resolved as the only link, with no Dispute control and no hint”; `customerStatus.test.ts`: resolved-to-Flag mapping | yes |
| AC4: merchant-named Dispute link | `TransactionsScreen.test.tsx`: “reads ‘Dispute’, is named after the merchant, is 44px tall and underlines on hover only”; exact-name disambiguation test | yes |
| AC5: single-link dispute row | `DisputesScreen.test.tsx`: single-link accessible-name, responsive chevron, and merchant-only hover tests | yes |
| AC6: loading skeleton | `TransactionsScreen.test.tsx`: “matches the columns: a plain panel, the hidden header, 2 hidden bands and 5 rows of four bars”; loading-list and loading axe tests | yes |

These establish unit-level coverage; computed viewport geometry remains QA’s check.

## Failures

- **Medium — renamed links break existing e2e journeys.** `TransactionRow.tsx:99` now names links `Dispute {merchant}`, but `e2e/tests/journeys/uj-1.spec.ts:69,75` still requires and clicks `Dispute this transaction`. The same stale selector remains in `dispute-raise.spec.ts:98,331,628,632`, `spa-transactions.spec.ts:230`, and `spa-dispute-status-badges.spec.ts:100,201`. These positive assertions/clicks cannot match the renamed links.
- **Medium — inherited icon changes break outcome assertions.** `DisputeStatusBadge.tsx:19` renders Lucide SVGs, but `e2e/tests/spa-disputes-history.spec.ts:282–285` still requires visible spans containing check/cross text glyphs. Those elements no longer exist.

These are code-inspection findings; no suites were rerun.

**Checks:** frontend build, typecheck, lint and formatting passed. Backend build passed with zero warnings/errors; style and whitespace passed after restore. Generated OpenAPI matched the tracked document. Floor guard passed; no shell scripts changed. The listed JUnit file contains 727 tests with zero failures/errors/skips. Builder fingerprint verification was blocked by command permissions.

## Standards

`TransactionsScreen.tsx:56` places decorative `<div>` elements directly inside `<ul>`, contrary to the [HTML list content model](https://html.spec.whatwg.org/multipage/grouping-content.html#the-ul-element). Use hidden `<li>` placeholders while retaining five exposed list items. This advisory does not affect the verdict.

## Verifier's reading (2026-11-08)

- Diff hash: scripts/diff-hash.sh on the builder's worktree against 2f897c3 gives 3c8a8b8405b1, equal to test_results.diff_hash. The JUnit file holds 727 tests, 0 failures (builder's run, not re-run).
- Copilot's two "medium" e2e findings are reclassified: the spec (Tests and commits, lines 147 to 157) assigns every e2e selector update (the Dispute link named after the merchant with exact: true, the badge glyph assertions) to QA's second commit, and the builder's base excludes e2e. They are QA's checks, named here, not failed. No high or medium finding remains.
- Standards (advisory, low): TransactionsScreen.tsx:56 puts decorative div placeholders directly in a ul (HTML content model); hidden li would conform.
- lucide-react counted as the base's, as instructed.

## Breaks (scripts/run-breaks.sh)

All 9 breaks passed without the break and failed with it ("failed as expected"): AC1 lede, AC1 header aria-hidden, AC2 band fill, AC3 three columns, AC3 badge link 44px, AC4 Dispute link name, AC5 merchant-only hover underline, AC5 decorative chevron, AC6 skeleton columns.

## Floor guard

scripts/floor-guard.sh in the builder's worktree: clean.
