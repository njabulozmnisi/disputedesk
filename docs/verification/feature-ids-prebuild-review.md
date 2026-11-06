# feature-ids: pre-build review

Reviewer, 2026-11-06. Spec: section "feature/feature-ids" of `~/disputedesk-rename/feature-backlog/specs.md` (lines
68 to 168), with the file's header (ownership, the rule diff, build order) and its open questions. Read as a design
against main at `ef88282`: AGENTS.md, CLAUDE.md, `docs/architecture/architecture.md` (AD-32), `scripts/check-ready.sh`,
`.claude/hooks/guard.py`, `scripts/guard.test.sh`, `scripts/lane.sh`, `scripts/lane-runner.zsh`,
`docs/tickets/README.md`, `_bmad/custom/bmad-agent-po.toml`, the pipeline skills and agent files, and BMAD's
`tickets.py`. Checklists: review-story Pass 1 and Pass 2, the standards check, and the security-story checklist.

Verdict: **changes**. One `[decision]` point (the open question the spec writes into AGENTS.md) and eight medium
`[story]` points. No point contradicts the PRD or an architecture decision: AD-32's ruleset names no branch pattern,
and the human's 2026-11-06 "no more epics" decision is what the spec carries out.

## What was checked and holds

- The spec's citations are right on `ef88282`: `check-ready.sh:22` refuses anything but `^[0-9]+\.[0-9]+$`, and
  `guard.py:93` is `STACKED_BRANCH = r"feature/[0-9]+\.[0-9]+[a-z]?"`, used only by `is_stacked_push` (`guard.py:320-328`).
- The slug bound `^[a-z][a-z0-9-]{0,40}$` is 1 to 41 characters; `records-` plus 41 is 49, inside AGENTS.md's
  50-character `<slug>` bound (Commits, Branches); `lows-yyyy-mm-dd` is 15. No dot, colon, `..` or `.lock`, so every
  slug is a valid ref component (git-check-ref-format) and a valid tmux session name.
- `re.fullmatch` with the alternation denies `feature/Guard`, `feature/a.b-c`, `feature/x:main` and a 42-character slug,
  as criterion 3 expects; the subagent deny comes from `AGENT_GIT_DENY` unchanged.
- `check-ready.sh`'s three rules carry over to a slug: the review files are exact names, the front matter reader is
  path-independent, and the dependency's ref check (`git ls-remote --exit-code --heads origin refs/heads/feature/<dep>`)
  already takes any name. A slug can't traverse paths, so it is safe in `docs/verification/<slug>-*.md`.
- `lane.sh:32` (`name="lane-${story//./-}"`) and `lane-runner.zsh` need no change for a slug.
- The new test step stays inside the Boundaries: `ci.yml`'s `floor-guard` job already lists each `scripts/*.test.sh` as a
  step (`ci.yml:269-274`).
- All 52 numbered stories in `docs/tickets/README.md` are `done`.

## Findings

| # | Tag | Severity | Where (spec line) | Point | Fix |
|---|---|---|---|---|---|
| D1 | `[decision]` | medium | 690-695, criterion 4 (132-140) | Open question 1 (when the lows batch and the retrospective run, now that no epic ends) is still open. The Definition of ready needs "no open questions", and criterion 4 writes the answer into AGENTS.md Continuous improvement, replacing "After every epic (retrospective)" (AGENTS.md:142). The rule diff can't be written or approved without it. | The human answers it (the proposed default, 10 open rows or five merged features, or another trigger) in `docs/verification/decisions.md` before the rule diff is written; the spec then states the trigger in criterion 4. |
| S1 | `[story]` | medium | criterion 3 (122-130), Inputs 87-89 | When the stacked branch pattern is widened to every slug, the one allowed force push also covers `feature/records-<slug>`, `feature/lows-<date>` and every Small or docs-only `feature/<slug>` branch, because they share the namespace. `guard.test.sh:217` (`check deny deny "git $stacked feature/records-7.3"`) shows a records branch is meant to be refused. It stays green only because `7.3` has a dot, so nothing would catch the regression. The Inputs reserve `records-` and `lows-`, but criterion 3 neither excludes them nor tests them. | Exclude the reserved prefixes in the pattern (Python `re` lookahead: `feature/([0-9]+\.[0-9]+[a-z]?\|(?!records-\|lows-)[a-z][a-z0-9-]{0,40})`) and add `deny deny` cases for `feature/records-guard-allow-list` and `feature/lows-2026-11-06`. State in the criterion that any other slug branch (a Small fix) does get the form, since the guard can't tell it from a stacked feature. |
| S2 | `[story]` | medium | criteria 1 (105-106) and 6 (153-154) | The two criteria contradict each other. Criterion 6 keeps build-story's story path `docs/tickets/epic-<epic>-<slug>/tickets/story-<epic>.<story>-<slug>.md` "beside" the slug path, and verify-story keeps its `<epic>.<story>` input (`build-story/SKILL.md:8`, `verify-story/SKILL.md:8`). Criterion 1's grep would then find more than "only the sentence that describes the historical story IDs", so a correct build fails criterion 1. | Since every numbered story is done, the inputs name only `docs/tickets/<slug>.md` (or the S8 path), and the one historical sentence stays in AGENTS.md Ticket numbering. The simpler design also drops check-ready's story-ID mode, which no spec will run again (an `after` entry naming a done story still resolves through the README row). If story IDs stay, criterion 1 must name each allowed line. |
| S3 | `[story]` | medium | Covers (73-74), criteria 1 and 6 | The spec renames the placeholder only in some files and never states the slug's relation to the existing bound. AGENTS.md defines `<story>` once (line 120) and uses it in Commits (`feature/<story>`, `feature/records-<story>`, the stacked push), Status contract (`<story>-build.json`), Story flow and Continuous improvement (`<story>-review.md`). Criterion 6 renames `<story>` to `<id>` only in the skills, agent files and CLAUDE.md, and the Covers list omits Commits and Status contract, so AGENTS.md would keep `<story>` while the guard allows `feature/<slug>`. AGENTS.md Branches also keeps its own `<slug>` bound `^[a-z][a-z0-9.-]{0,49}$`, so two slug definitions would sit side by side. | Use one definition: AGENTS.md line 120 says `<id>` is a story ID or a feature slug (Inputs bound), and every rule file says `<id>`. Add Commits (Branches, Stacked stories, Records) and Status contract to Covers. Say the feature slug is the narrower form of the Branches `<slug>` bound, and add `-e '<story>'` to criterion 1's grep, allowing only the definition. |
| S4 | `[story]` | medium | criteria 4, 5, 6 (132-159), Verify (161-162) | Criteria 4, 5 and the first half of 6 have no deterministic check (R35). The verifier's `cmp` proves the rule files equal the approved patch, not that the patch says what the criteria require. Criterion 1's grep misses the wording that would survive: "the epic's lows" (AGENTS.md:95, 134; `review-story/SKILL.md:39`; `security-story/SKILL.md:28`), "batched per epic" (`review-story/SKILL.md:12`), "After every epic" (AGENTS.md:142), "per epic" (`agentic-sdlc.md:95`) and the PO's `epic id` facts (`bmad-agent-po.toml:3, 9, 12`). | Add the checks to Verify, or better to `check-ready.test.sh` or a `floor-guard` line so they keep holding: an empty `git grep -n -i -e 'epic-<epic>' -e "epic's lows" -e 'per epic' -e 'every epic'` over the rule files; `git grep -l 'docs/verification/lows.md'` and `decisions.md` naming AGENTS.md and `agentic-sdlc.md`; the README's `## Features` header row; and the `persistent_facts` line. Then the builder can map each criterion to a test in `criteria`. |
| S5 | `[story]` | medium | criterion 6 (155-157) | The proof for `lane.sh` can't be run. The smoke check's "shellcheck-clean dry run of `lane.sh start` usage parsing" has nothing to run, because `lane.sh` has no dry-run mode: `start` reaches `ssh` (`lane.sh:41, 46`) before it does anything else. The criterion also says the scripts are unchanged, so there is nothing new for shellcheck to check. | Replace it with "`scripts/lane.sh` and `scripts/lane-runner.zsh` are not in the diff", which the verifier reads from the diff, and cite `lane.sh:32` for why a slug needs no dot replacement. |
| S6 | `[story]` | medium | criteria 2 (116) and 3 (127-128), Inputs 93 | The tests check only one side of each bound. They test the 42-character reject but never accept a 41-character slug, in check-ready or in the guard. check-ready never tests `lows-x`, although the Inputs reserve it. The Inputs say "anything else exits 2", but `check-ready.sh` with no argument exits 1 today, which means "not ready" (`${1:?…}` at line 21; ran it and saw exit 1). | Add accepted cases for a 41-character slug and a 1-character slug (`a`) in both tests. Add `lows-x` exit 2, and no-argument exit 2, which needs an explicit argument check in place of `${1:?}`. |
| S7 | `[story]` | medium | Inputs 92, criterion 2 | The `after:` bounds (zero to 10 entries, each a slug or a story ID) appear in no criterion and no test. Today `check-ready.sh:57-63` puts each entry into a `grep -E` pattern and a ref name unchecked, and line 58 prefixes a bare number with the epic, which a slug spec doesn't have. An entry of any other shape, such as a bare number in a slug spec, has no defined result. | Criterion 2 says an `after` entry that is neither a slug nor a story ID exits 2, and tests it, a bare `4` included. Either enforce the 10-entry bound with tests at 10 and 11, or drop it, since the spec gives no reason for it. |
| S8 | `[story]` | medium | Inputs 90-91 | The spec layout is home-grown where the adopted planning method documents one (AGENTS.md, Planning: BMAD). BMAD's form for work outside an epic is a backlog leaf: a `story-<slug>.md` file with `type: story` in `{output_folder}/backlog/` (`bmod-method/help/ticketing-and-epics.md:3, 20`; `_bmad/config.toml:3`). Sofia's `tickets.py` accepts only the types `story`, `spike` and `bug` and the names `(story\|spike\|bug)-*.md` (`bmad-agent-po/scripts/tickets.py:67-69`), so `docs/tickets/<slug>.md` with `type: feature` is invisible to `tickets.py status` and `next`. The front matter also isn't "as today": today's has `id`, `type: story`, `parent`, `covers` and `refined`, and no `type: feature`. | Use the BMAD backlog leaf (`type: story`, file `story-<slug>.md`, slug as the stem's tail) and point check-ready, build-story and criterion 1 at it. If not, state why no BMAD form fits, and list the exact front-matter keys. |
| S9 | `[story]` | low | criterion 5 (145) | `check-ready.sh:59` finds a dependency's row by `^\| <id> \|` and its last cell `\| done \|`. A slug cell written as a link would never match. | State that the Features table's slug cell is the bare slug, the title cell links the spec, and status is the last column. |
| S10 | `[story]` | low | Tier line (70-72) | The rule-diff scope "the six skills and agent files that name `<story>` or an epic file" is not a closed list. The verifier `cmp`s "each rule file", so it needs the list. | Name each file: today the files that hit are `build-story`, `verify-story`, `review-story` and `security-story` SKILL.md, and the `backend-builder`, `frontend-builder`, `reviewer` and `security` agent files. |
| S11 | `[story]` | low | criteria 1, 4 | "Feature" is now both a stakes tier (AGENTS.md, Stakes tiers: "Feature \| a story") and the unit of work, and ci-hardening is a "Small" feature. AGENTS.md's Definition of ready ("a Feature or High-stakes story has had its pre-build review") and Story flow then read two ways. | Call the unit "feature spec" everywhere, write the tier as "Feature tier", and add the term to `docs/glossary.md` if the rule text uses it. |
| S12 | `[story]` | low | Inputs 87 | One slug can be a prefix of another. AGENTS.md has the supervisor start from `docs/verification/<id>-*.md`, and for slug `guard` that pattern also matches `guard-allow-list-*.md`. A slug `x-prebuild` would also write `x-prebuild-review.md`, which is slug `x`'s pre-build record. | Add a rule for Sofia that no slug equals another slug plus a `-` suffix, or make the AGENTS.md instruction name the exact record files. |
| S13 | `[story]` | low (security) | criterion 6 | `lane.sh` puts `$story` unquoted into remote shell strings (`lane.sh:41, 53, 56, 60, 62`) and never checks its shape. A slug that meets the bound is safe, but nothing enforces the bound where the input enters. This predates the spec, and the spec asserts the script is unchanged. | Leave it for the lows list, or add one check at `lane.sh:31` with check-ready's ID regex (exit 2). |

## Pass 2: simplicity

- The smallest design is one ID word (`<id>`) and one slug bound across AGENTS.md, the skills and the scripts (S3). It
  drops the numbered-story paths and check-ready's story-ID mode, since all 52 stories are done (S2). Two vocabularies
  side by side is the main complexity risk in the spec.
- In the guard, a regex alternation inside the existing exact-argv comparison is the plainest change. The only extra
  is the reserved-prefix lookahead (S1), which is one sentence to explain.
- Two flat files (`lows.md`, `decisions.md`) in place of per-epic files are simpler than today's layout.

## Standards check

| Mechanism | Standard | Holds |
|---|---|---|
| Slug as a branch name component | git-check-ref-format, <https://git-scm.com/docs/git-check-ref-format> | yes |
| ID bound check in check-ready | Bash `[[ =~ ]]`, <https://www.gnu.org/software/bash/manual/bash.html#Conditional-Constructs> | yes |
| Dependency pushed check | `git ls-remote --exit-code`, <https://git-scm.com/docs/git-ls-remote> | yes, unchanged |
| Stacked push pattern | Python `re.fullmatch` with alternation and lookahead, <https://docs.python.org/3/library/re.html> | yes, with S1 |
| Spec file layout and front matter | BMAD backlog leaf (`tickets.py`, `ticketing-and-epics.md`) | no: home-grown `type: feature` (S8) |
| tmux session name | tmux(1), <https://man.openbsd.org/tmux.1> | yes, no change |

## Security checklist (below High-stakes)

The spec touches no auth, session, customer data, headers or packages (Packages: none). One point bears on security:
the guard's force-push exception gets wider (S1). The deployed-environment prompts and `AGENT_GIT_DENY` are unchanged,
and main stays protected by the ruleset's `non_fast_forward` rule (`.github/rulesets/main.json`). There is one
boundary-validation low (S13). No secrets are involved.

## Sofia's disposition

Sofia, 2026-11-06. Every `[story]` point S1 to S12 is folded into `docs/backlog/story-feature-ids.md`; S13 (the unquoted ID in `scripts/lane.sh`'s remote shell strings) goes to `docs/verification/lows.md`. The `[decision]` point (the lows and retrospective trigger) was answered by the human on 2026-11-06 and is recorded in `docs/verification/decisions.md`.
