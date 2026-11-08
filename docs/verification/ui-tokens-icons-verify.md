# Verification: ui-tokens-icons

- Commit: `2f897c37b3b58b11119c6126123213f98defeb43` (uncommitted diff hash `8f40abae6693`, matches `test_results.diff_hash`)
- Results file: `TestResults/frontend-junit.xml`, 676 tests, 0 failures, 0 errors; not re-run.
- Verifier: gpt-6-astra (Copilot) for the criteria table; breaks and floor guard by the verifier.
- Date (UTC): 2026-11-08

## Criteria (Astra, confirmed against the build.json mapping)

| Criterion | Test | Proved |
|---|---|---|
| AC1 list-column tokens | theme.test.ts > declares the list columns | yes |
| AC2 exact lucide-react pin | packagePins.test.ts | yes |
| AC3 status-to-icon mapping | customerStatus.test.ts | yes |
| AC4 decorative icons on every surface | routing, TransactionsScreen, StartPage tests | yes |
| AC5 wordmark dot, 44px targets | routing.test.tsx | yes |
| AC6 no text-glyph icon | theme.test.ts > has no text-glyph icon | yes |

Astra's VERDICT: FAIL rested on two e2e selectors (below); the spec assigns both to QA, so they are not findings.

## Breaks (8 of 8 fail as expected)

`scripts/run-breaks.sh` could not run them: it treats a non-dotnet `test_method` as a shell command, and the builder
wrote Vitest test names ("theme.test.ts > ..."), so every break returned ERROR (command not found). That is a tooling
mismatch, not a break result. The verifier instead ran each break by hand in a throwaway worktree (story diff applied,
node_modules linked). The five affected Vitest files passed (154 tests) without a break; each patch then failed its
named test and was reverted.

1. declares the list columns: fails
2. pins every dependency to an exact version: fails (the lucide-react runtime check fails too)
3. maps submitted to its label, tone, icon and group: fails
4. maps underReviewPrioritised to the custom SearchPriority icon: fails
5. draws the phone tab bar with decorative Lucide icons...: fails
6. hides the icon from the accessible name... (badge): fails
7. puts the aria-hidden dot after the name...: fails
8. has no text-glyph icon: fails

## Floor guard

`scripts/floor-guard.sh`: clean.

## Not failed, named for QA

- `e2e/tests/spa-disputes-history.spec.ts:282-285` still filters on the glyphs for check and cross.
- `e2e/tests/spa-transactions.spec.ts:331` still expects the glyph-prefixed `Transactions` and `Disputes` names.
  The spec (story lines 132-136) gives both updates to QA. The mockup comparison and the browser-drawn size of the
  icons and the wordmark dot are also QA's.

## Non-standard mechanisms

None reported.

## Findings

None high or medium.

- low: `scripts/run-breaks.sh` has no Vitest form, so frontend breaks need a shell-command `test_method` (or the script a Vitest mode).

## Fix round 1 (2026-11-08)

- Commit: `2f897c37b3b58b11119c6126123213f98defeb43`, uncommitted diff hash `86921d070fc6`; equals `test_results.diff_hash` in the new build.json.
- Results file: `TestResults/frontend-junit.xml`, 676 tests, 0 failures, 0 errors; not re-run.
- Fix list: one medium (review): the top-navigation check used `queryByRole('img')`, which can never fail on an `<svg>`. The fix asserts `getByRole('navigation', { name: 'Main' }).querySelector('svg')` is null and adds break 9.

### Breaks (9 of 9 fail as expected)

`scripts/run-breaks.sh` ran every break with the new shell-command `test_method` form (no hand run needed): each passed without
its patch and failed with it. Break 9 (a Lucide icon in a `TopNav` link) fails the repaired routing test, so the
"top navigation keeps text only" boundary of criterion 4 is now proved.

### Floor guard

`scripts/floor-guard.sh`: clean.

### Criteria table

Unchanged from the first run (all six criteria proved by the mapped tests). Astra was re-run but its output is not used: the
story file is not in the builder's worktree and it could not read the main checkout, so it marked every criterion "no" for lack of the spec;
its only other evidence repeats the two e2e items below. It wrote its copy of the record into the builder's worktree; that copy is deleted.

### Not failed, named for QA

- `e2e/tests/spa-disputes-history.spec.ts:282-283` still filters on the check and cross glyphs.
- `e2e/tests/spa-transactions.spec.ts:331` still expects the glyph-prefixed `Transactions` and `Disputes` names.
  The spec assigns both to QA.

### Findings

None high or medium. Fix finding 1 is closed.

- low: `scripts/verify-with-copilot.sh` run from a worktree writes the record there, and Astra cannot read the story from a worktree that lacks it (the story is in the DisputeDesk-ux-docs checkout).

## Fix round 2 (2026-11-08)

- Commit `2f897c37b3b58b11119c6126123213f98defeb43`, uncommitted diff hash `0a1bf425eec8`, equal to `test_results.diff_hash` in the build record.
- Results file: `TestResults/frontend-junit.xml`, 677 tests, 0 failures, 0 errors; not re-run.
- Fix list: one medium (QA, finding 2): the focused skip link overlapped the 44px home link at 320px (WCAG 2.5.8). The fix gives the focused skip link its own 44px row above the header, in theme tokens, with break 10.

### Breaks (10 of 10 fail as expected)

`scripts/run-breaks.sh`: every break passed without its patch and failed with it. Break 10 ("takes its own 44px row above the header when focused, without floating over it") proves the skip-link geometry.

### Floor guard

`scripts/floor-guard.sh`: clean.

### Copilot step

Astra ran in a throwaway copy with the spec from origin/main. It marked all six criteria proved and found no code defect. Its VERDICT: FAIL rests on two tooling denials only: it could not generate OpenAPI to compare (backend/openapi.json is unchanged against main and the diff is frontend-only) and it could not run scripts/diff-hash.sh (the hash match is confirmed above). Frontend build, typecheck, lint and format passed. Standards: none. The copy and its record are deleted; nothing was written to the builder worktree.

### Not failed, named for QA

- The 320px axe target-size result in linking-screens and linking-queue needs the running stack: QA re-runs the two linking projects.
- The two e2e glyph items from the first run stay with QA.

### Findings

None high or medium. Fix finding 2 is closed at unit level; QA confirms in the browser.

## Fix round 3 (2026-11-08)

- Commit 2f897c37b3b58b11119c6126123213f98defeb43, uncommitted diff hash d4ad4fb4eb25: equals test_results.diff_hash in ui-tokens-icons-build.json (checked with scripts/diff-hash.sh in the builder's worktree).
- Results: TestResults/frontend-junit.xml, 677 tests, 0 failures, 0 errors. The suite was not re-run.
- scripts/run-breaks.sh: exit 0, all 10 breaks failed as expected (theme columns, package pins, status mapping x2, tab bar icons (breaks 5 and 9 share a test), transactions icon name, wordmark dot, no text-glyph icon, skip link float). Break 10 proves AC7.
- scripts/floor-guard.sh in the builder's worktree: clean.
- Copilot step (gpt-6-astra, throwaway copy with the spec from origin/main): VERDICT: PASS. AC1 to AC6 proved; build, typecheck, lint, format and OpenAPI drift clean. The spec on main lists AC1 to AC6 only, so Astra did not grade AC7 (skip link); the builder's routing.test.tsx "the skip link" describe block and break 10 cover it. The fingerprint check was permission-blocked inside Astra and was done here instead. The throwaway copy's own hash differs (c1ef95810f61) only because the build record, results and spec were copied in as untracked files.
- Non-standard mechanisms: none.
- Named for QA (running stack): the 320px axe target-size check in the linking-screens and linking-queue projects, and the e2e glyph items.
- Findings: none high or medium.
