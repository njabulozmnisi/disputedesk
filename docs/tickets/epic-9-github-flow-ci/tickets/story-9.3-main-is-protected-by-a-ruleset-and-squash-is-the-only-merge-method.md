---
id: 3
type: story
title: "Main is protected by a ruleset, and squash is the only merge method"
parent: epic-9-github-flow-ci
covers: [R33, R39]
after: [2]
hitl: true
risk: high
decisions: settled
refined: true
---

# Main is protected by a ruleset, and squash is the only merge method

## Description

ad-012 clause 2 and the middle step of its rollout order (clause 7). `main` has no protection today, and the supervisor reaches it by a command-line fast-forward (`scripts/merge-story.sh`, then `scripts/push-and-watch.sh` on `main`). After this story a ruleset on `main` requires a pull request, the two gate checks 9.1 added (`ci-passed`, `security-passed`) on an up-to-date branch and CodeQL's code-scanning results, and blocks force pushes and deletion; the repository allows only squash merges, with auto-merge on and the head branch deleted on merge. The forbidden-terms check, which becomes part of a required check, fails instead of skipping when its secret is missing. 9.3 is pushed on today's flow, its pull request shows both gates, the human applies the settings, and 9.3 is then the first squash merge under the ruleset.

The `story/**` trigger, the two scripts, the guard change and the process rules are story 9.4 (split from this story on 2026-11-05, Notes). From 9.3's merge until 9.4's, the ruleset refuses `merge-story.sh`'s fast-forward, so 9.4 is the only story in flight and merges by the pull-request steps it writes down.

Human steps (hitl): the human applies the ruleset and the repository settings with the `gh api` calls in criteria 1 and 2, once 9.3's own pull request shows both gates successful (criterion 4).

## Requirements

Verify: actionlint and shellcheck pass in `floor-guard`; the reviewer's read-backs of criteria 1 and 2 match the committed values; each Supervisor check below fails as described; after the merge, `main`'s CI and Security runs on the squash commit succeed and `gh pr view` shows the pull request merged by squash.

Inputs:
- The `gh` account: every `gh` write and every read-back in this story runs as the repository owner, `GH_TOKEN="$(gh auth token --user <owner>)" gh …`, `<owner>` being the account the repository-local credential helper in `.git/config` names. The active `gh` account has pull access only, and it stays active for the verifier, so there is no `gh auth switch`; the token is never printed, logged or written to a file.
- The ruleset, `.github/rulesets/main.json`, whole: `name` `main`; `target` `branch`; `enforcement` `active`; `bypass_actors` `[]`; `conditions.ref_name` `{include: ["~DEFAULT_BRANCH"], exclude: []}`; `rules`: `deletion`; `non_fast_forward`; `pull_request` with its five required parameters, `required_approving_review_count: 0` and `dismiss_stale_reviews_on_push`, `require_code_owner_review`, `require_last_push_approval` and `required_review_thread_resolution` all `false`; `required_status_checks` with `strict_required_status_checks_policy: true` and `required_status_checks` `[{context: ci-passed, integration_id: 15368}, {context: security-passed, integration_id: 15368}]` (15368 is the GitHub Actions app, `gh api apps/github-actions`); `code_scanning` with `code_scanning_tools` `[{tool: CodeQL, security_alerts_threshold: high_or_higher, alerts_threshold: errors}]`. No `allowed_merge_methods` (epic Notes, declined).
- The repository settings: `allow_squash_merge` `true`, `allow_merge_commit` `false`, `allow_rebase_merge` `false`, `allow_auto_merge` `true`, `delete_branch_on_merge` `true` (five booleans, sent with `-F`); `squash_merge_commit_title` `PR_TITLE`, `squash_merge_commit_message` `PR_BODY` (two strings, sent with `-f`).
- 9.3's pull request: head `story/9.3`, base `main`; title the commit-message convention's subject in one short line of at most 72 characters, the detail in the body, which `PR_BODY` makes the squash commit's message.
- Head commit for `--match-head-commit`: the full 40-character SHA of the commit whose tree the stages checked (`docs/verification/9.3-build.json`'s diff hash covers that tree), or the head `gh pr update-branch` made from it once its checks pass.

1. **A ruleset protects `main`** (ad-012 clause 2, the human's CodeQL answer; R33, NFR-SEC-8)
   **Given** `.github/rulesets/main.json` with exactly the content in Inputs
   **When** the human applies it with `GH_TOKEN=… gh api --method POST repos/<owner>/<name>/rulesets --input .github/rulesets/main.json`
   **Then** the reviewer reads it back as the owner, GET only, with `gh api repos/<owner>/<name>/rulesets/<id> --jq '{name,target,enforcement,bypass_actors,conditions,rules}'`, compares it with the file, and records the read-back and any parameter GitHub adds (for example `allowed_merge_methods` or `do_not_enforce_on_create`) in a dated section of `docs/verification/9.3-review.md` before criterion 5
   **And** a second application uses `--method PUT repos/<owner>/<name>/rulesets/<id>` with the same `--input`, so there is one ruleset

2. **Squash is the only merge method** (ad-012 clause 2; R33)
   **Given** the human runs `GH_TOKEN=… gh api --method PATCH repos/<owner>/<name>` with the five booleans in Inputs as `-F` fields and the two strings as `-f` fields
   **When** the reviewer reads back `gh api repos/<owner>/<name>` as the owner, GET only, with those seven fields
   **Then** each has its value in Inputs, recorded beside criterion 1's read-back, so the squash commit on `main` carries the pull request's title and body
   **And** `infra/README.md` gains a *Repository settings* section, written by the builder, with the two commands of criteria 1 and 2 (the owner-token prefix included), the expected values (the committed file and the seven fields), and why there is no linear-history rule and no merge queue (ad-012 clause 2)

3. **The forbidden-terms check fails closed** (NFR-SEC-8; R33)
   **Given** `floor-guard`'s *Forbidden terms* step, which today skips with a notice when `secrets.FORBIDDEN_TERMS` is empty, as it is on a pull request from a fork
   **When** it runs with no terms
   **Then** it fails with an error saying the `FORBIDDEN_TERMS` secret is not set, so `ci-passed`, now required, never passes without the check
   **And** with the secret set, it behaves exactly as today

4. **The settings are applied only once both gates report** (ad-012 clause 7; R33)
   **Given** 9.3's change on `story/9.3`, pushed on today's flow with `scripts/push-and-watch.sh origin story/9.3` (the `story/**` trigger is still there until 9.4), then its pull request opened with `GH_TOKEN=… gh pr create --head story/9.3 --base main` and watched with `gh pr checks <n> --watch`
   **When** its `ci-passed` and `security-passed` checks have both succeeded on its head, reported by the GitHub Actions app
   **Then** the human applies criteria 1 and 2, and from then until 9.4 merges nothing is merged by `merge-story.sh` (the ruleset refuses its push to `main`)
   **And** a later push to `story/9.3` (a fix round) runs the main checkout's `scripts/push-and-watch.sh origin story/9.3`, since this story deletes no script

5. **9.3 is the first squash merge under the ruleset** (ad-012 clauses 6 and 7; R33)
   **Given** criteria 1 to 4 on 9.3's pull request, with every stage passed and the reviewer's read-backs recorded
   **When** the supervisor confirms `gh pr view <n> --json baseRefName` reads `main` and arms `GH_TOKEN=… gh pr merge <n> --squash --auto --match-head-commit <sha>` with the SHA in Inputs, and before any later push to `story/9.3` first runs `gh pr merge <n> --disable-auto`, re-arming with the new head once its checks pass
   **Then** GitHub merges it once both required checks and the code-scanning rule pass on the up-to-date branch, and deletes `story/9.3`; the supervisor waits with `gh pr checks <n> --watch --required`, reads the squash SHA with `gh pr view <n> --json state,mergeCommit`, and finds `main`'s runs with `gh run list --branch main --commit <squash sha> --workflow CI` (and `Security`), watched with `gh run watch <id> --exit-status`
   **And** `main`'s CI run on the squash commit succeeds with `publish` and the deploy notice (9.2), and Security's run succeeds

## Boundaries

- Must not change: what any check job runs, except criterion 3's failing step; the gate jobs; `publish`; `deploy.yml`; the triggers (the `story/**` trigger goes in 9.4); `push-and-watch.sh`, `merge-story.sh`, `check-ready.sh`; the guard; AGENTS.md, CLAUDE.md and the skills (9.4).
- No merge queue, no required reviewer, no linear-history rule, no `allowed_merge_methods`, no Gitflow branches (ad-012, Options rejected and clause 2).
- Auto-merge is armed only after every stage has passed; it never replaces a stage (challenge, section 2).
- No pull request from a fork is merged in this flow; criterion 3 makes its checks fail.

## Supervisor checks

After criteria 1 and 2 are applied, every `gh` write as the owner (Inputs). Each throwaway commit is empty (`git commit --allow-empty`), so a rule that wrongly lets one through leaves `main`'s tree unchanged. Each must be refused as described:
- A direct push of an empty throwaway commit to `main` (the guard gives the main session no prompt, the human's answer on 9.4's P1): refused by the ruleset (pull request required).
- A throwaway pull request with a failing backend test, armed with `gh pr merge <n> --squash --auto`: it does not merge. On the same pull request, `gh pr merge <n> --merge` and `--rebase` are refused with an error naming the merge method, and `gh pr merge <n> --squash --match-head-commit <an older SHA>` is refused with an error naming the moved head, not the failing check. Close it and delete its branch.
- A throwaway pull request of one empty commit, armed with `--squash --auto --match-head-commit <its head>` while its checks are pending; one more empty commit pushed to it; `gh pr view <n> --json state,headRefOid,autoMergeRequest` shows it did not merge on the new head. The result is recorded either way; 9.4's process disables auto-merge before any push in both cases.
- A throwaway pull request of one empty commit opened before criterion 5 and read after it, when 9.3's squash merge has put it behind `main`: it is not mergeable until `gh pr update-branch <n>`.
- On a scratch branch, the *Forbidden terms* step reading an unset secret: `floor-guard` fails with criterion 3's error.
- The reviewer confirms from a throwaway pull request's merge box that `ci-passed` and `security-passed` show as *Required*.

## Build facts

- Owner: backend (backend-builder) for `.github/` and `infra/README.md`.
- Tier: Feature, with a human gate: repository settings (hitl). The reviewer runs the security checklist on the ruleset, the settings and the fail-closed step.
- Flags: hitl, security.
- Lane: A, after 9.2; nothing else merges between criterion 4 and 9.4's merge.
- Before the build (supervisor, docs-only change): AD-32's rule gains the ruleset (pull request, the two gates, up to date, CodeQL results, squash only) and the invariant of 9.4's criterion 7, with a row in `docs/architecture/spine-amendments.md` (AD-32, 2026-11-05, ad-012 clauses 2 and 6). The builder does not edit them.
- Mechanisms and sources:
  - Rulesets and their rules (pull request, required status checks with *require branches to be up to date* and the app pin, block force pushes, restrict deletions, code scanning results): <https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets>; REST: <https://docs.github.com/en/rest/repos/rules>; import: <https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/managing-rulesets-for-a-repository>.
  - Code scanning merge protection at the repository level: <https://docs.github.com/en/code-security/code-scanning/managing-your-code-scanning-configuration/set-code-scanning-merge-protection>.
  - Merge methods and squash settings: <https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/about-merge-methods-on-github>; REST *Update a repository*: <https://docs.github.com/en/rest/repos/repos#update-a-repository>.
  - Auto-merge: <https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/automatically-merging-a-pull-request>.
  - `gh api` (`--input`, `-F` typed and `-f` string fields; a field flag implies `POST`), `gh pr create`, `gh pr checks`, `gh pr merge` (`--squash`, `--auto`, `--match-head-commit`, `--disable-auto`), `gh pr view`, `gh pr update-branch`, `gh run list`, `gh run watch --exit-status`: <https://cli.github.com/manual/>; the builder confirms each flag with `gh help <command>` (the guard denies `gh pr merge --help` to a subagent).
  - Personal access with `GH_TOKEN` and `gh auth token --user`: <https://cli.github.com/manual/gh_help_environment>, <https://cli.github.com/manual/gh_auth_token>.
- Packages: none.
- Touches: `.github/rulesets/main.json` (new), `.github/workflows/ci.yml` (`floor-guard`'s *Forbidden terms* step), `infra/README.md` (new *Repository settings* section).

## Pre-build

- ad-012's challenge `[story]` points for clause 2 are folded in: exact check names and squash enforced by repository settings (criteria 1, 2), auto-merge enabled (criterion 2), the settings applied only once the gates report (criterion 4), auto-merge armed only after the stages (Boundaries).
- Sofia, 2026-11-05: folded the `[story]` points of `docs/verification/9.3-prebuild-review.md` that belong to the ruleset and settings here, and the rest into 9.4 (split, Notes). Here: P1 (the owner token, Inputs), P2 (9.3's push and pull-request sequence, criterion 4), P6 (the auto-merge-after-push Supervisor check), P7 (armed only when the base is `main`, criterion 5), P8 (empty throwaway commits; the refusals on the failing-test pull request, each naming its reason; the behind-`main` pull request read after criterion 5), P9 (the whole ruleset in Inputs; the named comparison, criterion 1), P11 (the builder writes commands and expected values; the reviewer records the read-backs, criteria 1, 2), P12 (`-F` for booleans, `-f` for strings, criterion 2), P13 (`gh help`, Build facts), P16 (the armed SHA, Inputs), P18 (a short title, the detail in the body, Inputs), P19 (the wait steps, criterion 5), P20 (the forbidden-terms step fails closed, criterion 3). In 9.4: P1 again, P3, P4, P5, P6's rule, P7's rule, P10, P13's guard cases, P14, P15, P16's rule, P17, P19's rule, and the human's answer on P21. Criteria: 5.
- Decisions: settled by the human, 2026-11-05 (`docs/verification/epic-9-decisions.md`: the CodeQL rule; P21, allow the exact push and merge forms, carried out in 9.4).
- Sofia, 2026-11-05: the human's answer on 9.4's P1 (`docs/verification/epic-9-decisions.md`) supersedes P21: the guard asks only for actions on a deployed environment, the main session pushes and merges with no prompt, and pipeline agents are denied them; Supervisor checks' direct push to `main` now runs with no prompt and the ruleset alone refuses it.

## References

- parent — docs/tickets/epic-9-github-flow-ci/epic-9-github-flow-ci.md, section Requirements
- decision — docs/decisions/ad-012-github-flow-ci.md, clauses 2, 6, 7 and the human decisions on merge method and CodeQL findings
- decisions — docs/verification/epic-9-decisions.md
- challenge — docs/verification/ad-012-challenge.md, sections 1 to 5
- review — docs/verification/9.3-prebuild-review.md
- research — ~/disputedesk-research/research_notes/ci-simplification/external-research.md, sections 1, 2 and 5 (outside the repository)
- architecture — docs/architecture/architecture.md, AD-32
- next story — docs/tickets/epic-9-github-flow-ci/tickets/story-9.4-stories-merge-through-their-pull-request-and-the-guard-bounds-the-flow.md

## Notes

- Risk high (proposed): a ruleset that does not require the gates, or a bypass actor, would let unchecked code reach `main`; beyond the criteria, the reviewer confirms from a throwaway pull request's merge box that both checks show as *Required*.
- Human decision, 2026-11-05: the ruleset also carries the `code_scanning` rule for CodeQL (high or higher security alerts, error alerts). The pre-build review confirmed the rule is available on this user-owned public repository at the repository level (*Code scanning merge protection*), so no `docs/production-path.md` fallback is needed.
- Decision: 2026-11-05 (Sofia) — split from the original 9.3 into 9.3 (the ruleset and settings) and 9.4 (the trigger, the scripts, the guard and the process rules), because folding the pre-build review's twenty `[story]` points and the human's P21 answer took the story past about 8 criteria (AGENTS.md, Definition of ready). Each slice is one outcome a check proves: here, `main` refuses anything but a squash merge of a checked pull request; in 9.4, the flow and the guard describe and allow exactly that. The ruleset goes first, as ad-012 clause 7 orders.
- Decision: 2026-11-05 (Sofia) — `--match-head-commit` on every armed merge, so auto-merge merges only the head the stages checked; because GitHub's schema does not say what `expectedHeadOid` does after a later push (pre-build review, P6), auto-merge is disabled before any push and re-armed with the new head, whatever the Supervisor check shows.
- Decision: 2026-11-05 (Sofia) — the ruleset is a committed JSON file applied with `gh api`, so the settings are reviewable and re-appliable; GitHub's import takes the same shape.
- Decision: 2026-11-05 (Sofia) — squash commit title and message come from the pull request, so the supervisor writes the commit-message convention once, as the pull request's title and body; the title is one short line (at most 72 characters) with the detail in the body (pre-build review, P18, second fix), because GitHub Docs give no title limit and a short subject stays well below any.
- Decision: 2026-11-05 (Sofia) — a pull request from a fork fails its checks (criterion 3) rather than relying on a rule that the flow merges none (pre-build review, P20): a deterministic check over a sentence.
- Decision: 2026-11-05 (Sofia) — the reviewer, not the supervisor, records the read-backs, because stage agents write their own records (AGENTS.md, Dispatch rules; pre-build review, P11); the record reaches `main` in 9.4's pull request or the first records pull request after it.
