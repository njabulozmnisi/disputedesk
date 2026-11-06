---
id: 1
type: story
title: "New work is named by a feature slug"
sources: [AGENTS.md, docs/tickets/README.md, scripts/check-ready.sh, .claude/hooks/guard.py, scripts/guard.test.sh, _bmad/custom/bmad-agent-po.toml]
companions: [docs/verification/feature-ids-prebuild-review.md, docs/verification/decisions.md, docs/verification/lows.md]
after: []
refined: true
hitl: true
risk: medium
decisions: settled
---

# New work is named by a feature slug

## Description

The 52 numbered stories in `docs/tickets/README.md` are done and keep their files, IDs and records. New work is a
**feature spec**: a BMAD backlog story named by its feature slug, and the slug is the only work ID. Today
`scripts/check-ready.sh:22` refuses any ID but `<epic>.<story>`, the guard's one allowed force push
(`STACKED_BRANCH = r"feature/[0-9]+\.[0-9]+[a-z]?"`, `guard.py:93`) accepts only a numbered branch, and lows and
decisions go to numbered files. This feature replaces those forms with the slug rather than keeping them beside it.

- Tier: Feature tier, with a human gate: a rule change (AGENTS.md, Human gates). Owner: backend (scripts and tests);
  the supervisor writes the rule diff (`guard.py:93` included) and this docs change.
- Covers: AGENTS.md (Ticket numbering, Definition of ready, Commits: Branches, Stacked stories and Records, Status
  contract, Story flow, Continuous improvement), R39.
- Bootstrap: this spec is built before `check-ready.sh` accepts a slug. Its readiness is checked once by hand against
  the Definition of ready, and its records use the slug (`docs/verification/feature-ids-*.md`). Its one `[decision]`
  point is answered in [docs/verification/decisions.md](../verification/decisions.md).

## Inputs

- **Rule files**, exactly: AGENTS.md, CLAUDE.md, `.claude/skills/build-story/SKILL.md`, `verify-story/SKILL.md`,
  `review-story/SKILL.md`, `security-story/SKILL.md` and `qa-story/SKILL.md`, `.claude/agents/backend-builder.md`,
  `frontend-builder.md`, `reviewer.md` and `security.md`, `docs/process/agentic-sdlc.md`,
  `_bmad/custom/bmad-agent-po.toml` and `.claude/hooks/guard.py`. The rule diff changes only these files, and the
  verifier `cmp`s each of them. BMAD's own files under `.claude/skills/bmod-*` and `bmad-*` are not rule files and are
  not edited.
- Feature slug `<id>`: `^[a-z][a-z0-9-]{0,40}$`, 1 to 41 characters, no dot, and not starting with `records-` or
  `lows-` (reserved for the records and lows pull requests). It is the narrower form of AGENTS.md's Branches `<slug>`
  bound (`^[a-z][a-z0-9.-]{0,49}$`), so `feature/records-<id>` (at most 49 characters) stays inside that bound. No
  slug equals another slug followed by `-` and more characters, so `docs/verification/<id>-*.md` and BMAD's
  `story-<id>-plan.md` never match a second spec.
- Spec file: the BMAD backlog leaf `docs/backlog/story-<id>.md` (`{output_folder}/backlog/`, with `output_folder =
  docs` in `_bmad/config.toml`). Front matter keys, exactly: `id` (the backlog's `next_id` from `tickets.py status
  docs/backlog`), `type: story`, `title`, `sources`, `companions`, `after`, `refined`, `hitl`, `risk`, and
  `decisions` (only when the pre-build review has a `[decision]` point). `parent`, `covers`, `tracker_id` and
  `assignee` are cut, as BMAD's template does for a backlog story on the repo store.
- `after:` list: each entry is a sibling leaf's stem, `story-<id>`, the form `tickets.py` resolves in a backlog
  folder. No count bound: no reason for one exists.
- `scripts/check-ready.sh <id>`: exactly one argument, a slug as bounded above. No argument, more than one, or any
  other shape exits 2; so does a missing spec file or an `after` entry that is not `story-<id>`.
- Stacked force form: `git [-C <path>] push --force-with-lease --force-if-includes origin feature/<id>`, `<id>` a
  slug as bounded above.

## Criteria

1. **The work ID is the slug** (AGENTS.md Ticket numbering, Commits, Status contract; R39)
   **Given** the rule diff applied
   **When** AGENTS.md is read
   **Then** Ticket numbering says new work is a feature spec `docs/backlog/story-<id>.md` (Inputs), and that the
   numbered stories in `docs/tickets/README.md` are done and keep their IDs and records, in one sentence that names
   no placeholder; the `<story>` definition in Story flow becomes "`<id>` is the feature slug (Ticket numbering)",
   and Commits (Branches, Stacked stories, Records), Status contract (`<id>-build.json`), Story flow and Continuous
   improvement say `<id>`; the tier is written "Feature tier" wherever it means the tier
   **And** `scripts/rule-text.test.sh` (new, run in `floor-guard`) asserts that `git grep -n -e 'epic-<epic>'
   -e '<epic>.<story>' -e '<story>'` over the rule files is empty.
   Mechanism: the existing rule text, one ID word. Source: AGENTS.md; the slug is a valid ref component
   (git-check-ref-format, <https://git-scm.com/docs/git-check-ref-format>).

2. **`check-ready.sh` takes a slug, and only a slug** (AGENTS.md Definition of ready; R35)
   **Given** a scratch repository with `docs/backlog/story-<id>.md` and review files `docs/verification/<id>-*.md`
   **When** `scripts/check-ready.sh <id>` runs
   **Then** it applies the three existing rules (Sofia disposition for `[story]` points, `decisions: settled` for
   `[decision]` points, every `after` entry `story-<dep>` done or pushed as `refs/heads/feature/<dep>`); a dependency
   is done when its Features row (criterion 5) reads `done`
   **And** `a` and a 41-character slug are accepted; no argument, `Feature-X`, a 42-character slug, `records-x`,
   `lows-x`, `1.2` and `1.2.3` exit 2, as do an `after` entry `4` or `2.3` and a slug with no spec file. The
   numbered-ID mode and its folder lookup are removed, and an explicit argument check replaces `${1:?}` (which
   exits 1).
   Test: new `scripts/check-ready.test.sh` (scratch repositories, a stub `origin`, no network), run in `floor-guard`.
   Mechanism: bash regex match and `git ls-remote --exit-code`. Source: Bash manual, Conditional Constructs
   (<https://www.gnu.org/software/bash/manual/bash.html#Conditional-Constructs>);
   <https://git-scm.com/docs/git-ls-remote>.

3. **A stacked feature can push its rebase** (AGENTS.md Commits, Stacked stories; R33)
   **Given** `guard.py` with `STACKED_BRANCH = r"feature/(?!records-|lows-)[a-z][a-z0-9-]{0,40}"`
   **When** `scripts/guard.test.sh` runs
   **Then** the stacked force form with `feature/guard-allow-list`, `feature/a` and a 41-character slug gets no
   decision in the main session and is denied to a subagent
   **And** the same form with `feature/Guard`, a 42-character slug, `feature/a.b-c`, `feature/x:main`,
   `feature/records-guard-allow-list`, `feature/lows-2026-11-06` and `feature/7.4` is denied to both. The existing
   `feature/7.4` and `feature/7.5` allowed cases (`guard.test.sh:50-52, 98, 148-149, 155-156`) take a slug branch
   instead, and `feature/7.4` moves to the denied cases: the rule changes, the coverage does not shrink. Any other
   slug branch (a Small fix) also gets the form, since the guard cannot tell it from a stacked feature.
   Mechanism: the existing exact-argv comparison, with `re.fullmatch` and a negative lookahead. Source: Python `re`
   (<https://docs.python.org/3/library/re.html>); <https://git-scm.com/docs/git-push> (`--force-with-lease`).

4. **Lows and decisions are filed in one place each** (AGENTS.md Story flow, Fix rounds, Continuous improvement; R39)
   **Given** the rule diff applied
   **When** a merged feature leaves low findings, or a pre-build review raises a `[decision]` point
   **Then** the low goes to `docs/verification/lows.md` (columns: feature, record, finding, closed) and the decision
   to `docs/verification/decisions.md` (columns: feature, point, answer, date)
   **And** the lows batch and the retrospective run when `lows.md` holds 10 open rows or five features have merged
   since the last batch, whichever comes first, as one `feature/lows-<yyyy-mm-dd>` pull request with the
   retrospective beside it (decided by the human, 2026-11-06, in `docs/verification/decisions.md`); this replaces
   the retrospective bullet of AGENTS.md's Continuous improvement, and the retrospective aggregates the escapes and
   metrics since the last batch.
   `scripts/rule-text.test.sh` asserts that `git grep -n -i -e 'epic-<epic>' -e "epic's lows" -e 'per epic'
   -e 'every epic'` over the rule files is empty, that AGENTS.md and `agentic-sdlc.md` both name
   `docs/verification/lows.md` and `docs/verification/decisions.md`, and that AGENTS.md contains `10 open rows`.
   Mechanism: one lows file and one decisions file. Source: AGENTS.md, Continuous improvement.

5. **The index and the product owner's rules know feature specs** (R39)
   **Given** the rule diff and the docs change
   **When** `docs/tickets/README.md`, `_bmad/custom/bmad-agent-po.toml` and `docs/glossary.md` are read
   **Then** the index ends with a `## Features` table whose header row is exactly `| Slug | Title | Owner | Tier |
   Status |`, in build order: the slug cell is the bare slug, the title cell links the spec, and status is the last
   column (`check-ready.sh` matches `^\| <id> \|.*\| done \|$`); the `persistent_facts` say new work is a feature
   spec, a BMAD backlog story `docs/backlog/story-<id>.md` with the Inputs' front matter keys, no epic folder and no
   number in the name, and that no slug equals another slug plus a `-` suffix; the glossary defines "feature spec"
   **And** `scripts/rule-text.test.sh` asserts the header row and that `bmad-agent-po.toml` names
   `docs/backlog/story-<id>.md`; `python3 .claude/skills/bmad-agent-po/scripts/tickets.py status docs/backlog`
   exits 0 and lists `story-feature-ids.md`.
   Mechanism: the BMAD backlog leaf and the existing Markdown table index. Source: BMAD,
   `.claude/skills/bmod-method/help/ticketing-and-epics.md` (backlog leaves) and
   `bmad-agent-po/assets/ticket-template.md`; AGENTS.md, Ticket numbering.

6. **Every agent brief and skill takes the slug** (AGENTS.md Status contract; R39)
   **Given** the rule diff applied
   **When** the skills and the agent files among the rule files name a work item
   **Then** they say `<id>` where they said `<story>`; the build-story input names only `docs/backlog/story-<id>.md`,
   and the verify-story input is `<id>`
   **And** `scripts/lane.sh` and `scripts/lane-runner.zsh` are not in the diff: `lane.sh:32` only replaces dots in
   the tmux session name, and a slug has none. The verifier reads this from the diff.
   Mechanism: text placeholders; tmux session naming. Source: tmux(1), session names
   (<https://man.openbsd.org/tmux.1>).

Verify: `floor-guard` passes `check-ready.test.sh`, `guard.test.sh` and `rule-text.test.sh`; `tickets.py status
docs/backlog` exits 0; the verifier's `cmp` of the rule files matches the approved patch.

## Boundaries

- No change to any check job beyond the two new test steps.
- No renaming or moving of existing story files or records.
- `scripts/lane.sh` and `scripts/lane-runner.zsh` unchanged.
- Packages: none.
