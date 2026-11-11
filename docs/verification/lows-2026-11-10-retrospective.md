# lows-2026-11-10 retrospective

The retrospective beside the `feature/lows-2026-11-10` pull request (AGENTS.md, Continuous improvement). It covers the
work merged since the epic 7 retrospective (2026-11-01) up to `f6193f5`, plus this batch.

Sources:
- [lows-2026-11-10-retrospective-input.md](lows-2026-11-10-retrospective-input.md): every escape and metric, by
  feature;
- [simplicity-audit-2026-11-11/](simplicity-audit-2026-11-11/README.md): the six-area audit;
- [aws-admin-lockdown-postmortem.md](aws-admin-lockdown-postmortem.md) and
  [lows-2026-11-10-frontend-postmortem.md](lows-2026-11-10-frontend-postmortem.md): the two second fix rounds.

## What the numbers say

- **Escapes:** 59, against about 43 for epic 7. Spec gaps are the largest category (19), and the builder is the
  stage that should most often have caught them (21). Three categories are new: script errors (5), mechanisms that
  don't follow a standard (4) and auth (3).
- **Fix rounds:** 0.9 per feature, down from 1.9.
- **Audit:** high and medium findings fell from 33 to 11 against the 2026-11-03 baseline. Lows rose from 35 to 41.
- **Recurring pattern:** briefs and specs state facts nobody checked with a command (11 escapes). Every
  countermeasure so far has been a sentence.
- **Did not hold from epic 7:** the verifier still cannot run all its own checks (7 occurrences).
- **This batch:** two lanes reached a second fix round.
  - The admin lock-down's brief named one way in instead of asking for all of them.
  - A frontend low grew into a redesign of a second component.
- **Metrics:** wall time appears in 9 of 22 captures. Tokens and peak context were almost never recorded.

## Proposals for the human (one batch)

Each item is in AGENTS.md's order: a check first, then a skill line, then a template field. Items marked *decision*
need a choice, not only a yes or no. The supervisor's recommendation is in the last column.

| # | Proposal | Kind | Recommendation |
|---|---|---|---|
| 1 | The verifier can run its own checks: add `diff-hash.sh` to `verify-with-copilot.sh`'s allow list, and a test that every script its prompt names is allowed (the planned `ci-hardening` feature) | check | yes |
| 2 | *Decision:* the Copilot verifier on a Small fix with no spec. Either it grades the fix's brief or fix list, or the Small tier skips the Copilot run | decision | grade the brief or fix list |
| 3 | The builder report's `breaks` are complete (`patch`, `test_method`, `expect`) and `test_results.files` is not empty, checked by `status_contract.py`. `run-breaks.sh` runs frontend breaks from `frontend/` with packages installed. Belongs to the planned `process-checks` feature | check | yes |
| 4 | Facts in briefs come from a command: widen `check-infra-paths.sh` to every top-level folder; a spec-template field for the pasted `git grep` of every renamed name or moved path; one supervisor process line (a rule change) | check, template, rule | yes |
| 5 | *Decision:* AWS checks run after every deploy. `deploy.yml` runs the read-only post-deploy checks (database owners, check 12, the metric count) and fails the run. This changes infrastructure | decision | yes |
| 6 | One e2e regression spec asserting no sideways scroll at 320px with 200% text on each journey page; it lands with the tab-bar fix that is still open | check | yes, with that fix |
| 7 | *Decision:* a records pull request fails without a complete capture (`check-capture.sh`), and without a postmortem when fix rounds reach 2. AGENTS.md asks for tokens and peak context per agent, but no stage records them: name a source, or drop the column (an AGENTS.md change) | check, decision | the check yes; drop the tokens column |
| 8 | Collision check at dispatch, across sessions: `lane.sh start` lists files shared with any open `feature/*` branch or open pull request, whoever owns it | check | yes |
| 9 | build-story line: assert a changed control's accessible name, spoken form, error announcements and focus after an error | skill | yes |
| 10 | security-story line: a change that publishes a login or opens a public route lists every account whose sign-up or recovery mail passes through it | skill | yes |
| 11 | Brief line for an auth or identity fix: list every path by which an anonymous or low-privilege user reaches the capability, and close or test each | rule | yes |
| 12 | A lows batch fixes each row as reported; a fix that reshapes a second component is split out as its own change | rule | yes |
| 13 | build-story line: a layout change's browser check measures focus visibility (a Tab walk, WCAG 2.2 SC 2.4.11) as well as overflow and clipping | skill | yes |
| 14 | *Decision, audit DESI-1:* AD-22 still lets migrations be reshaped in place because "only disposable databases hold the schema", false since RDS on 2026-11-07. Make migrations additive and backward compatible from now on | decision | yes |
| 15 | *Decision, audit DESI-2:* P55, P71, P80 and P81 wait on "not yet deployed"; restate each reason now the environment is live (P80, the Zitadel token on an instance volume, is a live risk) | decision | yes, as a docs change |
| 16 | *Decision, audit INFR-3:* replace the hand-written IAM JSON templates with `jsonencode`, removing 18 repeated validations (reverses D9 / 8.16) | decision | yes, as its own feature |
| 17 | Mermaid diagrams render in CI with a pinned mermaid-cli (a new dependency, verified first) | check | later |

The audit's remaining high and medium `[code]` findings become feature specs, ordered by the human:
- the shell harnesses in `scripts/` (INFR-1) and the workflow-step tests (INFR-2);
- the e2e poll loops (TEST-1);
- Testcontainers for the platform tests (TEST-3);
- a sourceable `provision.sh` (TEST-4);
- the linking wrapper (API--1).

## Skill lines to prune

From the input's section 6:
- review-story's `LayerTests` line, covered by `LayerTests.cs:72`;
- both `security-local.sh` lines (build-story, security-story), covered since `security.yml` runs the scan on every
  pull request;
- security-story's `PersonalDataRecordTests` clause;
- build-story's oldest-tool-version line, narrowed to tools other than Compose.

review-story then holds 12 lines, still over the "about 10" cap. The next to review are the 2.8 and 3.8 lines, whose
escapes have not recurred.

Skill changes are rule changes, so pruning waits for the human with the proposals.

## What this batch left open

These rows stay in `lows.md`:
- the phone tab bar at 320px with 200% text;
- the dispute detail page's 10px and the empty Disputes state's sideways scroll;
- the CSP nonce for the dialog scroll lock (a gateway change);
- the rows owned by planned feature specs: `guard-allow-list`, `guard-main-session`, `process-checks`, `ci-hardening`
  and `skill-lines`.
