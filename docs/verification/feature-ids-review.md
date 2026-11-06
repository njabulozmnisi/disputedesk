# feature-ids: review

Reviewer, 2026-11-06. Post-build review of the worktree `.claude/worktrees/agent-aa1d8835659493cdd`, base `f13e521`, diff
hash `76637c49271e` (`scripts/diff-hash.sh <worktree> f13e521`). Spec: `docs/backlog/story-feature-ids.md`. Pre-build
review: `docs/verification/feature-ids-prebuild-review.md`. Checklists: review-story Pass 1 and Pass 2, the standards
check, and the security-story checklist (Feature tier, below High-stakes).

Verdict: **approve**. Every criterion is proved by a test or a check I ran. No high or medium finding; five lows.

## What I ran

| Check | Result |
|---|---|
| Staged diff against the approved rule patch (`diff` of `git diff --cached` with `feature-ids-rule-diff.patch`) | identical; `git apply --check --reverse --cached` clean |
| `scripts/rule-text.test.sh` | pass, "all assertions hold" |
| `scripts/check-ready.test.sh` | pass, all cases as expected |
| `scripts/guard.test.sh` | pass, 518 runs |
| `python3 -I .claude/skills/bmad-agent-po/scripts/tickets.py status docs/backlog` | exit 0, lists `story-feature-ids.md` (`id` 1, `next_id` 2) |
| `shellcheck` 0.11.0 on `check-ready.sh`, `check-ready.test.sh`, `rule-text.test.sh`, `guard.test.sh` | clean |
| `scripts/secret-scan.sh` (whole uncommitted change, untracked files included) | no leaks found |
| File modes of the two new test scripts (CI runs them directly) | executable |

The builder's results files cover diff hash `174871d8e989`, made before the supervisor's glossary row and README
`guard-main-session` row. I re-ran the three tests above on `76637c49271e`, and they pass. The README is an input of
`rule-text.test.sh`. The build report's AC5 "Not met" and its glossary conflict are now stale, because the glossary row
exists. Whether the hash mismatch matters is the verifier's call (verify-story step 2).

## Pass 1: correctness

| Criterion | Proof | Holds |
|---|---|---|
| 1. The work ID is the slug | `rule-text.test.sh:28` (no `epic-<epic>`, `<epic>.<story>` or `<story>` in the 14 rule files); AGENTS.md Ticket numbering, Story flow, Commits, Status contract and Continuous improvement read `<id>` (staged diff); I grepped the rule files for "Feature" written as a tier without "tier" and found none | yes |
| 2. `check-ready.sh` takes only a slug | `check-ready.test.sh:58-79` (both sides of the bounds: `a` and 41 characters accepted; 42 characters, `records-x`, `lows-x`, `1.2` with its story file present, `1.2.3`, no argument and two arguments exit 2); `:105-134` (`after` shapes `4`, `2.3`, `1.2`, a bare slug and `story-*` exit 2; done, pushed and planned dependencies; an unreadable origin exits 2); `${1:?}` replaced by `(($# == 1))` (`check-ready.sh:26`); the numbered mode and the folder lookup are gone | yes |
| 3. A stacked feature can push its rebase | `guard.test.sh:52-60` (none/deny for the slug forms, 1 and 41 characters, `records` and `lows` with no dash); `:226-236` (deny/deny for `Guard`, `7a`, 42 characters, `a.b-c`, `x:main`, `-guard`, empty, `records-…`, `lows-…`, `7.4`); `guard.py:328` uses `re.fullmatch`; I probed the pattern myself (`feature/records-` and `feature/a/b` are refused, `feature/records` is allowed) | yes |
| 4. One lows file and one decisions file | `rule-text.test.sh:29-40`; AGENTS.md Continuous improvement has the human's trigger from `decisions.md` (D1, 2026-11-06), word for word | yes |
| 5. The index and the product owner know feature specs | `rule-text.test.sh:41-43` (header row, po.toml path); `tickets.py status` exit 0; glossary row present (supervisor's docs, read, not asserted: L3) | yes |
| 6. Briefs and skills take the slug | `rule-text.test.sh:28` over the five skills and four agent files; build-story names only `docs/backlog/story-<id>.md`, verify-story takes `<id>`; `scripts/lane.sh` and `lane-runner.zsh` are not in the diff | yes |

Also checked:
- Every script a skill now calls with a slug accepts one. `verify-with-copilot.sh` (`${1:?}`, used in a path and a
  lock tag) and `test-lock.sh` (the owner tag) never check the ID's shape. No script outside `check-ready.sh` and the
  guard matches `[0-9]+\.[0-9]+`.
- `check-ready.sh` validates the slug before it builds any path (`:28`, `:30`), so `docs/backlog/story-<id>.md` and
  `docs/verification/<id>-*.md` cannot traverse paths. `set -o noglob` keeps an `after` entry from expanding to file
  names, and a test covers it (`check-ready.test.sh:129-131`).
- Reading only the inline `after:` line is safe: `tickets.py` refuses block-style lists ("frontmatter lists must be
  inline"). It also resolves a sibling's stem `story-<id>` (`tickets.py`, `sibling`), so the Inputs' form is BMAD's.
  Reading `after` from the front matter only, not the whole file, is a correct tightening with a test (`:108-110`).
- The README dependency match `^\| <id> \|.*\| done \|$` cannot match a numbered row, since a slug has no dot. A slug
  that is a prefix of a done row is not done (`check-ready.test.sh:115-116`).
- The pre-build points S1 to S12 are fixed in the spec. S13 (`lane.sh` puts the ID unquoted into remote shell strings)
  is a low the spec leaves alone. It is not yet in `lows.md` (L5).

The .NET-specific Pass 1 items (Location headers, timestamps, ProblemDetails, ownership, stored events, `!`) do not
apply: the diff has no backend code.

## Pass 2: simplicity

- `check-ready.sh` got smaller. One `is_slug` function serves both places that need it (the argument and each
  `after` entry), and the numbered mode, the epic prefixing and the folder glob are gone. Each step reads on first
  pass.
- `rule-text.test.sh` is two small helpers (`none`, `names`) over a closed list of files. `check-ready.test.sh` builds
  one scratch repository with a bare `origin`, with one `expect` helper and one line per case.
- The guard change is one constant. The lookahead is the one extra part, and it takes one sentence to explain.
- Nothing speculative was added. No new abstraction, option or package.

## Standards check

| Mechanism | Standard | Holds |
|---|---|---|
| Slug and argument check | Bash `[[ =~ ]]` and `$#`, <https://www.gnu.org/software/bash/manual/bash.html#Conditional-Constructs> | yes |
| `after` entries as words | `set -o noglob`, <https://www.gnu.org/software/bash/manual/bash.html#The-Set-Builtin> | yes |
| Dependency pushed | `git ls-remote --exit-code`, <https://git-scm.com/docs/git-ls-remote> | yes, unchanged |
| Slug as a ref component | <https://git-scm.com/docs/git-check-ref-format> | yes (no dot, colon or slash; a trailing `-` is valid) |
| Stacked branch pattern | Python `re.fullmatch` with a negative lookahead, <https://docs.python.org/3/library/re.html> | yes |
| Rule-text assertions | `git grep` exit 1 meaning no match, <https://git-scm.com/docs/git-grep> | yes |
| Spec file | BMAD backlog leaf, read by `tickets.py status` | yes |

## Security checklist (below High-stakes)

The diff has no auth, session, linking, customer data, headers or packages, so those items do not apply.
- Boundary validation: the slug is checked where it enters `check-ready.sh`, before any path or ref is built from it.
  `lane.sh` still does not check it (L5, pre-existing).
- Guard: the one allowed force push now covers every slug branch, which the spec states (criterion 3), but no longer
  any `feature/records-*` or `feature/lows-*` branch. Subagents are still denied it (`AGENT_GIT_DENY` unchanged), and
  main's ruleset still refuses force pushes. No exception is wider than the spec requires.
- Secrets: the scan is clean. Packages: none.

## Findings

| # | Severity | File:line | Issue | Fix |
|---|---|---|---|---|
| L1 | low | `AGENTS.md` Ticket numbering (staged, the uniqueness bullet); `_bmad/custom/bmad-agent-po.toml:3` | The uniqueness rule covers only other feature slugs, but `docs/verification/` already holds slug-named records from earlier work (`session-fix-*`, `e2e-fix-*`, `e2e-baseline-qa.md`, `tf-gateway-spa-*`, `flow-tooling-*`). A new slug such as `session-fix` would overwrite `session-fix-review.md`, and `e2e` would match `e2e-*.md` in "start from `docs/verification/<id>-*.md`". No planned slug collides today. | Extend Sofia's rule: a slug never equals, or prefixes with a `-`, any existing `docs/verification/` file stem. This is a rule change, so it goes in the lows batch. |
| L2 | low | `docs/tickets/README.md:118-124` | Criterion 5 and the product owner's fact say the title cell links the spec. The seven planned rows have plain titles, because their specs are not yet in `docs/backlog/`. | Link each title when its spec file is written. Sofia's "update it in the same change" rule already covers this. |
| L3 | low | `scripts/rule-text.test.sh:40-43` | Criterion 5's glossary definition is proved only by reading. The spec's list of assertions leaves it out, which is why the builder reported it unmet before the supervisor added the row. | Add `names docs/glossary.md '**Feature spec**'`. |
| L4 | low | `scripts/diff-hash.sh:5`, `scripts/verify-with-copilot.sh:3-4,7` | Comments and usage text outside the rule files still say `<story>`, `<story-id>` and `<story-file>`, while the skills now pass `<id>` and `docs/backlog/story-<id>.md`. The behaviour is correct. | Say `<id>` and `<spec-file>` in the lows batch. |
| L5 | low | `docs/verification/feature-ids-prebuild-review.md` (S13 and its `[story]` points); `docs/verification/lows.md` | Pre-build S13 (`lane.sh:41,53,56,60,62` puts the ID unquoted into remote shell strings) is open and not in `lows.md`. The pre-build record also has no "Sofia, <date>" disposition line, so `scripts/check-ready.sh feature-ids` on main would exit 1 once this record set is merged (the spec's bootstrap checked readiness by hand). | At merge, add S13 to `lows.md` and append Sofia's disposition line to the pre-build review. |

## Escapes

To be added at merge (capture).

## Capture (supervisor, 2026-11-06)

### Escapes

| Finding | Caught by | Should have been caught by | Category | Countermeasure |
|---|---|---|---|---|
| The glossary had no "feature spec" entry, which criterion 5 needs | builder (reported as unmet) | pre-build review | spec gap | lows.md: an assertion in `scripts/rule-text.test.sh` |
| The pre-build record had no Sofia disposition line, so `check-ready.sh feature-ids` would fail on main | reviewer | supervisor at dispatch | script error | the supervisor runs `check-ready.sh <id>` on the committed records before opening the pull request |

### Metrics

| Fix rounds | Full test runs | Breaks | Wall time | Tokens and peak context | Disk |
|---|---|---|---|---|---|
| 0 | 1 builder run (diff hash 174871d8e989); the supervisor re-ran rule-text, check-ready and guard tests after two docs edits (76637c49271e) | 17, all fail as expected | builder about 40 min, reviewer about 6 min, verifier about 12 min | builder 217k tokens | not derived |
