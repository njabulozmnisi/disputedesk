# Verification: ui-raise-review

- Commit: `2f897c37b3b58b11119c6126123213f98defeb43` (uncommitted diff hash `7979389b8ed0`; includes the uncommitted bases ui-tokens-icons and ui-notices-states)
- Verifier: gpt-6-astra via GitHub Copilot CLI, reconciled by the Claude verifier
- Date (UTC): 2026-11-08

## Copilot (GPT-6 Astra) result, condensed

Its first line was `VERDICT: FAIL`, but the only failure it lists is "verification blocked, not a confirmed code defect": its sandbox denied `scripts/diff-hash.sh` and `scripts/floor-guard.sh`. It names no code defect. It reports: frontend build, typecheck, lint and format pass; backend build and style checks pass; OpenAPI matches the committed contract; the JUnit file has 738 tests, 0 failures, 0 errors; Standards: none.

| Criterion | Test | Proved |
|---|---|---|
| AC1 step visibility | RaiseScreen: step above h1; ReviewScreen: refusal and retryable-failure cases | yes |
| AC2 form surface, quiet transaction block | RaiseScreen: "is one flat surface", "draws the transaction as a quiet block" | yes |
| AC3 styled, accessible live difference | RaiseScreen: "draws the difference as 14/700 heading text" | yes |
| AC4 action layout, disabled Cancel | Raise and review action-layout tests; "makes Change and Cancel aria-disabled and inert" | yes |
| AC5 review surface, read-back rows | ReviewScreen: named section, 9rem column, Card and Account tests | yes |
| AC6 accessible Change control | ReviewScreen: "names Change from its content"; draft kept; pending state | yes |
| AC7 static only-once notice | ReviewScreen: static notice; "leaves exactly one alert after a 503" | yes |

## Verifier's reconciliation

- Diff hash: `scripts/diff-hash.sh` on the builder's worktree against `2f897c3` gives `7979389b8ed0`, equal to `test_results.diff_hash` in the build record. (The Copilot copy's own hash differed only because the throwaway copy held the spec, build.json and results file as extra untracked files.)
- Results file `TestResults/ui-raise-review-frontend.junit.xml`: 738 tests, 0 failures, 0 errors. The builder notes a first run timed out 8 tests under load (average 18); a second run on the same diff passed.
- `scripts/floor-guard.sh` in the builder's worktree: clean.
- `lucide-react` 1.70.0 in `frontend/package.json` is counted as the base's (ui-tokens-icons), as briefed.

## Breaks (`scripts/run-breaks.sh`, exit 0)

Each passed without the break and failed with it ("failed as expected"):

1. ReviewScreen "shows no step after 404 not-found, which ends the draft" (AC1)
2. RaiseScreen "draws the transaction as a quiet block" (AC2)
3. RaiseScreen "draws the difference as 14/700 heading text" (AC3)
4. RaiseScreen "puts a hairline above the buttons" (AC4)
5. ReviewScreen "reads the dl in a 9rem label column at every width" (AC5)
6. ReviewScreen "names Change from its content" (AC6)
7. ReviewScreen "draws the only-once sentence as a static neutral notice" (AC7)

## Result

No high or medium findings. Status: pass. The builder's headless-browser smoke (axe, widths 320 to 1280) was not re-run; checks needing the running stack belong to QA or CI.

## Fix round 2 (2026-11-08)

Base 2f897c37b3b58b11119c6126123213f98defeb43, uncommitted; diff hash cd3e0b7f9cca.

- Diff hash: scripts/diff-hash.sh on the worktree against 2f897c3 gives cd3e0b7f9cca, equal to test_results.diff_hash in the build record.
- Results file TestResults/ui-raise-review-frontend.junit.xml: 740 tests, 0 failures, 0 errors.
- Copilot step skipped as briefed: against the first-round worktree only ReviewScreen.tsx, ReviewScreen.test.tsx, theme.test.ts and tokens.css differ under frontend/src (no other story file changed; e2e/ ignored, QA's).
- scripts/floor-guard.sh: clean. lucide-react 1.70.0 is the base's, the same in both trees.
- Fix 1 (masked number, round 1): aria-hidden printed form plus sr-only "card ending" / "account ending" present in ReviewScreen.tsx; break 8 covers it.
- Fix 2 (QA, text scaling): the review dl grid is minmax(0,min(var(--label-col),40%)) / minmax(0,1fr), so the label column keeps 9rem where it fits and cannot take more than 40%; class test plus breaks 5 and 9.

### Breaks (scripts/run-breaks.sh, 9 breaks, all "failed as expected")

1. ReviewScreen "shows no step after 404 not-found, which ends the draft"
2. RaiseScreen "draws the transaction as a quiet block"
3. RaiseScreen "draws the difference as 14/700 heading text"
4. RaiseScreen "puts a hairline above the buttons"
5. ReviewScreen "reads the dl in a label column of 9rem, capped at 40% ..."
6. ReviewScreen "names Change from its content"
7. ReviewScreen "draws the only-once sentence as a static neutral notice"
8. ReviewScreen "reads the masked number to a screen reader as its short form, hiding the printed one"
9. ReviewScreen "reads the dl in a label column of 9rem, capped at 40% ..." (second break on the same test)

### Result

No high or medium findings. Status: pass. The real-browser text-scaling check (320 and 360px at 150% and 200%, 1280px at 9rem) needs a browser and is QA's, in spa-raise-review-axe.spec.ts; not run here.
