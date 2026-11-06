# Verification: feature-ids

- Commit: `f13e5211173f953d681c615c090906a38866a9df` (base; the work is uncommitted), diff hash `76637c49271e`
- Worktree: `.claude/worktrees/agent-aa1d8835659493cdd`
- Verifier: gpt-6-astra via GitHub Copilot CLI (`scripts/verify-with-copilot.sh`); breaks and cmp by the verifier agent
- Date (UTC): 2026-11-06

## Diff hash

The builder's `test_results.diff_hash` is `174871d8e989`; the tree now hashes to `76637c49271e`. The supervisor's two docs
edits after the build (glossary row, README Features row) explain the change; the brief names the new hash. The
shell-script results were not re-run for the docs edits (the supervisor re-ran `rule-text.test.sh`, `check-ready.test.sh`
and `guard.test.sh`, all passing, per the brief). Not a finding.

## 1. Rule files against the approved patch (cmp)

`git archive f13e521` into a scratch tree, `feature-ids-rule-diff.patch` applied (13 files; CLAUDE.md is not in the
patch), then `cmp` of all 14 rule files in the worktree against that tree.

Result: all 14 identical (AGENTS.md, CLAUDE.md, the five skills, the four agent files, `agentic-sdlc.md`,
`bmad-agent-po.toml`, `guard.py`). No high finding.

## 2. Breaks (`scripts/run-breaks.sh`, throwaway copy)

All 17 breaks: each test passed without the break and failed with it. Exit 0.

| Breaks | Test | Result |
|---|---|---|
| 1, 11-16 | `scripts/rule-text.test.sh` | failed as expected |
| 2-7, 17 | `scripts/check-ready.test.sh` | failed as expected |
| 8-10 | `scripts/guard.test.sh` | failed as expected |

## 3. Floor guard

`scripts/floor-guard.sh`: clean.

## 4. Criteria (Astra's table, checked by the verifier)

| Criterion | Test | Proved |
|---|---|---|
| AC1 slug work ID | `rule-text.test.sh`; cmp matches the approved patch | yes |
| AC2 `check-ready.sh` takes a slug | `check-ready.test.sh` (macOS and Linux runs) | yes, except the comma case in finding 1 |
| AC3 stacked force form | `guard.test.sh` (518 runs) | yes |
| AC4 lows and decisions | `rule-text.test.sh` | yes |
| AC5 index, PO rules, glossary | `rule-text.test.sh`, `tickets-status.txt` | yes (finding 2 is a note) |
| AC6 skills and agents take `<id>` | `rule-text.test.sh`; `lane.sh` and `lane-runner.zsh` not in the diff | yes |

Astra's verdict was FAIL on three points. The verifier assessed each:

- The `yq` failures (`ci-gates`, `deploy-workflow`, `main-protection` test results) are environmental: yq v4 is not
  installed on the build machine, and the builder's summary says so. CI's floor-guard runs them with the runner's yq.
  Named for CI, not failed.
- AC2 comma handling: confirmed, see finding 1.
- AC5 unlinked titles: see finding 2.

## Findings

1. **low, AC2.** `scripts/check-ready.sh:57` runs `tr -d '[],'`, which deletes commas, so a flow list without a space
   after the comma merges its entries: `after: [story-a,4]` yields the single word `story-a4`, a valid slug, instead of
   rejecting the numeric entry `4`; `[story-a,story-b]` yields `story-astory-b`. Reproduced with the same `tr`
   expression. The line is unchanged from f13e521, and a merged name fails closed (the dependency is reported as not
   done) unless a feature with the merged name exists. `check-ready.test.sh` covers only comma-plus-space. Fix by
   `tr ',' ' '` then `tr -d '[]'`, and add a no-space case.
2. **low, AC5.** In `docs/tickets/README.md` Features table, the seven `planned` rows have an unlinked title, where the
   criterion says the title cell links the spec. No spec file exists for them yet, and `rule-text.test.sh` checks only
   the header row, so it neither requires nor forbids the link. Acceptable until each spec is written.

No high or medium finding.

## Non-standard mechanisms

None reported (Astra: none identified). The `cmp` and `tr` use is plain shell.

## Not run

`ci-gates.test.sh`, `deploy-workflow.test.sh`, `main-protection.test.sh`: need yq v4, which is CI's. No suite re-run, no
stack. `scripts/verify-with-copilot.sh` writes its record into the worktree's `docs/verification/`; the verifier moved
that file out, so the worktree is as it was (hash `76637c49271e` after).
