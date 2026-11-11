# lows-2026-11-10: retrospective input

Input for the retrospective beside the `feature/lows-2026-11-10` pull request (AGENTS.md, Continuous improvement). It
aggregates the escapes and metrics recorded since the epic 7 retrospective (`epic-7-retrospective.md`, commit `95ac9a5`,
2026-11-01 09:48) up to `f6193f5` (2026-11-10 16:02). Sources: every capture (Escapes and Metrics tables) in
`docs/verification/*-review.md` written in that window, the four postmortems written in it
(`postmortem-verifier-sandbox.md`, `postmortem-disk-2026-11-03.md`, `ui-tokens-icons-postmortem.md`,
`ui-raise-review-postmortem.md`), and, as a separate list, failures that reached main or AWS but got no capture. The
simplicity and standards audit over the six areas is not part of this input.

Features with a capture (22): 7.15, 7.16, 7.17, redpanda-console, tf-aws-override, tf-fixtures, tf-spa-image,
tf-gateway-spa, tf-platform-tests, feature-ids, ci-e2e-regression, reviewer-docs, cloudwatch-metric-filter,
deploy-recreate-on-config, ui-tokens-icons, ui-notices-states, ui-lists, ui-linking, ui-raise-review, ui-detail-start,
aws-admin-lockdown, readme-landing. Merged changes with no capture: rds-init-grant (#17), ec2-zone-b (#15),
gateway-musl (#14), the zlib upgrade (#8), flaky-options-start (#13), the e2e outbox fix (#16), the epic 8 and epic 9
commits of 2026-11-04 to 2026-11-06 (only `flow-tooling-review.md` has a record, with no Escapes table), and the docs
and config changes #18 to #23, #40, #41.

## 1. Escapes

"Should have" names the first stage the capture names; a second one is in brackets.

| # | Feature | Finding | Caught by | Should have been caught by | Category | Countermeasure taken |
|---|---|---|---|---|---|---|
| E1 | 7.15 | The deploy role trusted GitHub's old OIDC subject format, live since 7.5 | offline-checks research (post-merge) | pre-build review (7.5 security and review) | spec gap | CI compares the trusted subject with a real token on every push to main |
| E2 | 7.15 | An `sts.amazonaws.com` check audience would mint a deploy-capable token on every push | pre-build review and security | product owner | auth | story records the non-STS audience |
| E3 | 7.15 | jq `@base64d` rejects base64url | pre-build review and security | product owner | non-standard mechanism | story names the RFC 4648 §5 translation |
| E4 | 7.16 | A name not at the start of its line was skipped silently | reviewer | pre-build review | input boundary | a one-line-statement break |
| E5 | 7.16 | Reference names used as patterns without the no-pattern-character evidence | reviewer | product owner (pre-build security) | missing test | the shape check refuses pattern characters |
| E6 | 7.16 | The verifier's Copilot sandbox could not run the scripts (7.15, 7.16 twice, 7.17) | verifier | tooling | environment or tooling | allow list extended (`postmortem-verifier-sandbox.md`) |
| E7 | 7.17 | Cleanup silenced its own errors; the secret scan skipped failing runs | verifier | builder | missing test | build-story line proposed; never added, superseded when the shell harness moved to xUnit (`72f7d0a`) |
| E8 | 7.17 | The `qa_readonly` USAGE check passed vacuously (PUBLIC keeps USAGE since PostgreSQL 15) | reviewer | pre-build review | missing test | every privilege check asserts the negative side |
| E9 | redpanda-console | The architecture stack table still named Redpanda v26.2.2 | builder | supervisor (brief) | spec gap | none |
| E10 | tf-aws-override | The brief named a test that reads only the root Compose file | builder | supervisor (brief) | spec gap | grep the test before naming it |
| E11 | tf-aws-override | A suite failure went unnamed because output was piped through `tail` | builder | builder | environment or tooling | build-story line (results file) |
| E12 | tf-fixtures | The brief listed 3 e2e readers of the fixtures; there are 10 | builder | supervisor (brief) | spec gap | reader lists come from `git grep` |
| E13 | tf-fixtures | Three tests found the repo root through `fixtures/` and broke on the move | builder (red run) | supervisor (brief) | spec gap | grep the folder name, not only the file |
| E14 | tf-spa-image | The README did not say where the docker command runs | reviewer | builder | spec gap | none |
| E15 | tf-gateway-spa | A header comment called the SPA image "published" | reviewer | builder | spec gap | none |
| E16 | tf-gateway-spa | Reliance on the default docker driver undocumented | reviewer | builder | non-standard mechanism | comment in both workflows |
| E17 | tf-gateway-spa | The verifier skipped its Copilot run | supervisor | supervisor (dispatch) | script error | the dispatch names the Copilot run |
| E18 | tf-gateway-spa | `service:` context to a profiled service fails on CI's Compose 2.38.2; main reverted | CI on main | supervisor (brief) [builder] | environment or tooling | CI installs and checks Compose v5.6.1 (`.github/actions/install-compose/install.sh`); build-story line (oldest tool version) |
| E19 | tf-platform-tests | Copied style rules not enforced in CI for the new project | reviewer | builder | missing test | none |
| E20 | tf-platform-tests | The brief's `dotnet test infra/tests/...` from the root fails under MTP | builder | supervisor (brief) | spec gap | none |
| E21 | feature-ids | The glossary had no "feature spec" entry that criterion 5 needs | builder | pre-build review | spec gap | lows row (rule-text assertion), still open |
| E22 | feature-ids | The pre-build record had no Sofia disposition line, so `check-ready.sh` would fail on main | reviewer | supervisor (dispatch) | script error | `check-ready.sh` on the committed records before the pull request |
| E23 | ci-e2e-regression | The spec named a made-up file that the infra path check refuses | builder | product owner | spec gap | `check-infra-paths.sh` in the floor-guard job |
| E24 | ci-e2e-regression | `check-infra-paths.sh` read a JSON `\n` escape as part of a path | builder | builder (the script had no tests) | missing test | `scripts/check-infra-paths.test.sh` in CI |
| E25 | ci-e2e-regression | `scripts/operations-token.sh` missing from the trigger list | reviewer | pre-build review | spec gap | every-script check in `scripts/regression-paths.test.sh` |
| E26 | ci-e2e-regression | The fix builder's brief granted a write path the guard does not hold | builder (guard refused) | supervisor (dispatch) | script error | briefs grant only guard-owned paths (a sentence) |
| E27 | reviewer-docs | `frontend/README.md` failed Prettier | CI (pull request) | supervisor (pre-push) | environment or tooling | run each touched area's format check before pushing (a sentence) |
| E28 | reviewer-docs | Two mermaid diagrams in `agentic-sdlc.md` never rendered | reviewer (mermaid-cli) | earlier stage that added them | missing test | proposed: a floor-guard step rendering every mermaid block (not applied) |
| E29 | reviewer-docs | Backend README drafts stated six wrong facts | backend builder | supervisor (draft) | spec gap | area builders fact-check drafts |
| E30 | reviewer-docs | `RateLimitOptionsTests` start-up test flaked on main (`ObjectDisposedException`) | main CI | earlier story (6.3) | flaky | Small fix #13 (the test runs the Api as its own process) |
| E31 | cloudwatch-metric-filter | The AWS collector exported every runtime metric with rollups (363 custom metrics) | AWS Free Tier usage email (live) | earlier story's review (AWS environment, AD-29) | non-standard mechanism | collector test asserts the exact metrics and dimension sets |
| E32 | deploy-recreate-on-config | `docker compose up -d` left the collector on its old bind-mounted config after a deploy | supervisor post-deploy check (live) | earlier story's review (`deploy.sh`) | environment or tooling | `--force-recreate` with a `DeployScriptTests` case |
| E33 | ui-tokens-icons | The "no icon" test queried `role=img`, which an `<svg>` never has, so it could not fail | reviewer | builder | missing test | each criterion boundary gets a break (a sentence) |
| E34 | ui-tokens-icons | The 44px home link was mostly hidden by the focused skip link at 320px | QA | pre-build review | spec gap | review-story pre-build line (#39) |
| E35 | ui-tokens-icons | The in-flow skip link shifted the page and lost the next click | QA | reviewer (round 2 delta) [builder smoke] | input boundary | build-story headless-pass line (#39) |
| E36 | ui-tokens-icons | `run-breaks.sh` could not run Vitest breaks | verifier | builder (record format) | environment or tooling | breaks written as shell commands; lows row 45 |
| E37 | ui-notices-states | The Reference row's hairline stopped at the Copy pill | reviewer | builder (smoke at 360px only) | missing test | browser pass covers 1280px (in briefs) |
| E38 | ui-notices-states | The Copy pill kept a raw `border-[1.5px]` | verifier | builder | non-standard mechanism | build from the shared Button |
| E39 | ui-notices-states | Build record keyed breaks `test`, not `test_method` | verifier | builder | script error | briefs say `test_method` |
| E40 | ui-lists | Absence checks on the old link name would pass vacuously after the rename | reviewer | pre-build review | spec gap | QA briefs list every lookup of a renamed name |
| E41 | ui-lists | Status badges clipped at 200% text on phones | QA | builder (smoke: widths, not text) | input boundary | 200% root font in the headless pass (in briefs, then #39) |
| E42 | ui-lists | Breaks 6 and 9 stopped applying after the fix round | verifier | builder (fix round) | script error | a fix builder re-runs every break (a sentence) |
| E43 | ui-lists | The sr-only merchant duplicated the description and broke four `getByText` lookups (reviewer's draft table) | builder | pre-build review | spec gap | pre-build greps `frontend/src` and `e2e/` for every renamed or duplicated text |
| E44 | ui-lists | The spec's `exact: true` is not a Testing Library `getByRole` option (reviewer's draft table) | builder | pre-build review | spec gap | name a library option only after reading the installed types |
| E45 | ui-linking | An over-long paste with a trailing letter was sliced to six digits and accepted | security, reviewer | pre-build review | input boundary | paste rule stated as a count over the pasted text |
| E46 | ui-linking | A refused paste was not announced to screen readers | reviewer | builder | missing test | each silent error names its announcement |
| E47 | ui-linking | A paste error after a submit remounted the summary and moved focus | verifier | builder | missing test | sequence tests for errors after a submit |
| E48 | ui-linking | Disabled buttons used a transparent border against criterion 6 | verifier | builder | spec gap | none |
| E49 | ui-raise-review | The masked number lost its spoken form ("card ending 3456") | reviewer | builder | missing test | a replaced component's spoken form is asserted first |
| E50 | ui-raise-review | The 9rem label column collapsed the value column under text-only scaling | QA | pre-build review | spec gap | review-story pre-build line and build-story 200% pass (#39) |
| E51 | ui-raise-review | The 40% cap binds at default text below about 426px | reviewer (delta) | builder (1280px only) | missing test | accepted; lows row 70 |
| E52 | ui-raise-review | `RaiseScreen.test.tsx` conflicted with main after ui-lists and ui-linking merged | supervisor (update-branch) | n/a | environment or tooling | supervisor starts the merge, a builder resolves it |
| E53 | ui-detail-start | `alert-dialog.tsx` has two consumers, so the session warning also became a sheet | reviewer | pre-build review (said one consumer) | spec gap | consumers of a shared primitive listed from a grep |
| E54 | ui-detail-start | The session warning does not move focus into the dialog | QA | earlier story (the warning's) | missing test | lows row 77 (ranked medium) |
| E55 | ui-detail-start | The verifier's model could not run `scripts/diff-hash.sh` and returned FAIL | verifier | n/a | environment or tooling | the verifier confirms the hash itself |
| E56 | aws-admin-lockdown | Publishing the Mailpit login exposed the Zitadel first-instance admin's reset email | readme-landing reviewer | supervisor (plan) [7.4 pre-build security] | auth | lock-down deactivates human admins on every deploy; check 12 |
| E57 | aws-admin-lockdown | Public organization registration open on AWS | security | builder [AWS environment security review] | auth | instance restriction |
| E58 | aws-admin-lockdown | The lock-down failed open on an unexpected shape and read only the first page | security | builder | input boundary | whole list required, re-read afterwards |
| E59 | aws-admin-lockdown | Conflict with #41 in `provision.sh` and `PlatformScriptTests.cs` | supervisor (push) | supervisor (dispatch collision check) | environment or tooling | fix round 2 merged main |

Not counted: ui-linking's input-otp CSP point, caught at pre-build (no escape). The four postmortems repeat E6, E18
(with the disk trigger), E34 and E35, and E50; they add no new escape.

### Failures with no capture (not in the counts)

| # | Change | Finding | Caught by | Should have been caught by | Category |
|---|---|---|---|---|---|
| U1 | rds-init-grant (#17) | The database init failed on the first RDS deploy: it relied on `createrole_self_grant`, which the plain PostgreSQL fixture honours through an implicit ADMIN membership that RDS does not create | live deploy | 7.17 (proved on plain PostgreSQL 18) | environment or tooling |
| U2 | D2 session end (`313173b`) | Three e2e logout specs went stale on main (they raced the document navigation) | e2e baseline QA (`e2e-baseline-qa.md`) | the D2 change's checks (the e2e regression project did not run on pull requests then) | missing test |
| U3 | #16 | Two e2e specs failed when the relay sent the outbox row first; `e2e-baseline-qa.md` had left it "without a reproduction" | CI | e2e baseline QA | flaky |
| U4 | `73855eb` | The overlapping-sweeps worker test read spans by end order | CI | the test's story | flaky |
| U5 | #15 | The dev instance's zone had no c7i-flex capacity (inferred from the commit title; no record) | live deploy | n/a | environment or tooling |

The HIGH CVEs in the gateway base (libexpat `a7435f1`, zlib #8, musl #14) were advisories published after merge, not
misses; each was caught by the Trivy image scan.

### Counts by category, against epic 7

| Category | Epic 7 (about 43) | Since (59) | Change |
|---|---|---|---|
| Spec gap | 17 | 19 | flat in number, lower share (40% to 32%) |
| Missing test | 13 | 13 | flat |
| Environment or tooling | 5 | 9 | up |
| Input boundary | 5 | 5 | flat |
| Script error | 0 | 5 | new |
| Non-standard mechanism | 0 | 4 | new |
| Auth | 0 | 3 | new |
| Flaky | 3 | 1 (plus U3, U4) | flat |

### Counts by "should have been caught by"

The epic 7 retrospective did not count by stage, so there is no baseline.

| Stage | Count | Escapes |
|---|---|---|
| Builder (self-check, smoke, fix round) | 21 | E7, E11, E14, E15, E16, E19, E24, E33, E36-E39, E41, E42, E46-E49, E51, E57, E58 |
| Supervisor (brief, plan, dispatch, pre-push) | 13 | E9, E10, E12, E13, E17, E18, E20, E22, E26, E27, E29, E56, E59 |
| Pre-build review | 12 | E1, E4, E8, E21, E25, E34, E40, E43, E44, E45, E50, E53 |
| An earlier, merged story | 5 | E28, E30, E31, E32, E54 |
| Product owner (story writing) | 4 | E2, E3, E5, E23 |
| Reviewer | 1 | E35 |
| Tooling or n/a | 3 | E6, E52, E55 |

Caught by: reviewer 18, builder 13, verifier 9, QA 5, supervisor 4, security 3, CI 3, pre-build 2, outside the
pipeline after merge 2 (E1 research, E31 a billing email). Seven reached main or AWS (E1, E18, E28, E30, E31, E32, E54),
plus U1 to U5.

## 2. Recurring patterns (3 or more)

1. **A brief or spec names facts nobody pasted from a command** (spec gap, supervisor or pre-build): readers, consumers,
   tests, files and tool behaviour from memory. E9, E10, E12, E13, E18, E20, E29 (supervisor briefs and drafts) and E25,
   E40, E43, E53 (pre-build consumer lists): 11, across redpanda-console, tf-aws-override, tf-fixtures, tf-gateway-spa,
   tf-platform-tests, reviewer-docs, ci-e2e-regression, ui-lists, ui-detail-start. Every countermeasure taken was a
   sentence ("grep before naming"); tf-fixtures already called it the second time.
2. **The test environment is not the real one** (environment or tooling, plus U1): CI's Compose (E18), Compose
   recreate semantics on AWS (E32), the RDS role model (U1), jsdom without layout (E34, E35, E37, E41, E50 all needed a
   real browser), macOS against Linux (cloudwatch-metric-filter verify L3). Features: tf-gateway-spa,
   deploy-recreate-on-config, rds-init-grant, ui-tokens-icons, ui-notices-states, ui-lists, ui-raise-review.
3. **AWS live behaviour is first seen live**: E1, E31, E32, E57, U1 (5; 7.15, cloudwatch-metric-filter,
   deploy-recreate-on-config, aws-admin-lockdown, rds-init-grant). Each was found after the environment was live; three
   needed a billing email, a deploy failure or a manual check.
4. **The verifier cannot run what it must check** (environment or tooling): E6 (7.15, 7.16 twice, 7.17), E36
   (ui-tokens-icons), E55 (ui-detail-start), plus the cloudwatch-metric-filter verify (Copilot ran neither `diff-hash.sh`
   nor `floor-guard.sh`, and graded a Small fix with no spec as FAIL) and lows row 65 (deploy-recreate-on-config: the
   guard read the `deploy` path as a deploy and blocked ShellCheck). 7 occurrences over 6 features.
5. **The breaks record drifts from what `run-breaks.sh` accepts** (script error): E36, E39, E42, and lows row 25 (no
   `@@` headers). Features: ui-tokens-icons, ui-notices-states, ui-lists, plus the carried row.
6. **Text scaling and focus-state overlap found only in QA** (spec gap and input boundary): E34, E35, E41, E50, plus
   lows rows 43, 58, 59, 60, 73. Features: ui-tokens-icons, ui-lists, ui-raise-review. The brief-only countermeasure
   after ui-tokens-icons (a headless pass checking widths) did not stop E41 and E50; the skill lines followed in #39.
7. **The builder's accessibility self-check misses the spoken and sequence states** (missing test): E46, E47, E49, E54.
   Features: ui-linking, ui-raise-review, ui-detail-start.
8. **Vacuous assertions** (missing test or spec gap): E8, E33, E40 (7.17, ui-tokens-icons, ui-lists). The review-story
   absence line exists and the reviewer caught all three; the builder did not.
9. **The supervisor's dispatch or brief contradicts a tool rule** (script error): E17, E22, E26, and E59 (collision
   check). Features: tf-gateway-spa, feature-ids, ci-e2e-regression, aws-admin-lockdown. Parallel-branch conflicts also
   cost ui-raise-review (E52) and ui-notices-states (a rebase build).
10. **Auth on the AWS identity provider** (auth): E2, E56, E57 (7.15, aws-admin-lockdown). Two of the three came from
    one exposure (a public Mailpit and an open Zitadel instance).

By stage: the builder (21) and the supervisor (13) are the stages most often named. The supervisor column is new: it
did not exist in epic 7's counts.

## 3. Metrics

| Feature | Tier | Fix rounds | Full-suite runs | Stages recorded | Wall time | Tokens, peak context | Disk |
|---|---|---|---|---|---|---|---|
| 7.15 | High-stakes | 0 | 3 (Terraform: builder, verifier, reviewer) | 7 incl. throwaway CI run | - | - | - |
| 7.16 | Feature | 1 | - | 9 (two verifier runs blocked) | - | - | - |
| 7.17 | Feature | 1 | - | 8 | - | - | - |
| redpanda-console | Small | 0 | 1 (+1 Api rerun) | - | builder 23, reviewer 3 min | - | - |
| tf-aws-override | Small | 0 | 1 | - | builder 12, reviewer 3 min | - | worktree removed |
| tf-fixtures | Small | 1 | 1 | - | builder 13, QA 1, reviewer 4, fix 1 min | - | worktrees removed |
| tf-spa-image | Small | 1 | 1 (frontend) | - | builder 4, reviewer 4, fix 1 min | - | worktrees removed |
| tf-gateway-spa | Small | 2 (review, CI escape) | 1 | - | builder 20, verifier 1, reviewer 3, fix 1 min | - | 9.6 GiB free (trigger) |
| tf-platform-tests | Small | 1 | 1 | - | builder 14, reviewer 3, fix 2 min | - | worktrees removed |
| feature-ids | Feature | 0 | 1 (+ supervisor reruns) | 3 | builder 40, reviewer 6, verifier 12 min | builder 217k tokens | not derived |
| ci-e2e-regression | Feature | 1 | 3 | 3 + delta | builder about 4 h, reviewer 6+3, verifier 9+3 min | not derived | not derived |
| reviewer-docs | Small | 1 + 1 CI format fix | 0 local; 2 PR, 1 main + 1 rerun | - | audits 5, drafting 60, builders 1-6, reviewer 7, PR checks 27 min a run | - | none |
| cloudwatch-metric-filter | Small | 0 | 1 (440 tests) | 3 | - | - | - |
| deploy-recreate-on-config | Small | 0 | 1 (442 tests) | 3 | - | - | - |
| ui-tokens-icons | Feature | 3 (review 1, QA 2; third human-authorised) | 4 | 16 | - | - | - |
| ui-notices-states | Feature | 1 | 3 | 10 | - | - | - |
| ui-lists | Feature | 1 (+ break regeneration) | 2 | 12 | - | - | - |
| ui-linking | High-stakes | 1 | 3 | 11 | - | - | - |
| ui-raise-review | Feature | 2 (review, QA) | 3 | 11 + conflict | - | - | - |
| ui-detail-start | Feature | 0 | 1 | 5 | - | - | - |
| aws-admin-lockdown | High-stakes | 2 (H1 and M1, then the merge) | 3 (448, 457, 465) | 10 | - | - | - |
| readme-landing | Small | 0 (review said `changes`; H1 moved to aws-admin-lockdown) | 0 (docs) | 1 | - | - | - |

Totals: 20 fix rounds over 22 features, 0.9 a feature (epic 7: about 17 over 9 stories, 1.9). Recorded full-suite
runs: 34 over 20 features (7.16 and 7.17 unrecorded), plus flaky-options-start's 3 (two failed on the open
`RateLimitWindowTests` flake under load). Three or more fix rounds: ui-tokens-icons only (epic 7: 7.14). A second fix
round: tf-gateway-spa, ui-raise-review, aws-admin-lockdown, reviewer-docs.

**What the captures failed to record**, against AGENTS.md's metrics row:

- Fix rounds per stage: given for the ui-* features, tf-gateway-spa and aws-admin-lockdown; a bare count elsewhere.
- Full-suite runs: missing for 7.16 and 7.17.
- Wall time per stage: in 9 of 22 captures; none for the six ui-* features, 7.15 to 7.17, cloudwatch-metric-filter,
  deploy-recreate-on-config, aws-admin-lockdown or readme-landing.
- Tokens and peak context per agent: tokens once (feature-ids builder, 217k); peak context never; "not derived" in two.
- Disk change: one number (tf-gateway-spa); otherwise "worktrees removed", "not derived" or nothing.
- Shape: five different Metrics table layouts, so no column can be summed without reading each.
- Missing captures: rds-init-grant (its review asked for the escape to be recorded), and the Small fixes listed at the
  top. ci-e2e-regression's "Criterion 4 (CI runs)" section still reads "To fill in after CI".
- Postmortems: aws-admin-lockdown and reviewer-docs reached a second fix round with no postmortem (AGENTS.md trigger).

## 4. Did the epic 7 countermeasures hold?

| # | Countermeasure | Held? | Evidence |
|---|---|---|---|
| 1 | `check-ready.sh`: `[story]` points need a Sofia line, `[decision]` points `decisions: settled` | Mostly | One recurrence: E22 (feature-ids), caught by the reviewer because the check ran on uncommitted records. All six ui-* specs, feature-ids and ci-e2e-regression pass it on main today (run for this input, exit 0). |
| 2 | `check-ready.sh`: the `after` list | Held | No story built ahead of a dependency; stacked builds were used. The neighbouring collision check is not covered: E52, E59 and a ui-notices-states rebase build. |
| 3 | `scripts/security-local.sh` before reporting | Held | No Trivy config finding after merge. It now also runs on every pull request (`.github/workflows/security.yml:119`, behind `security-passed`). Red runs on main since were new image CVEs, which this filesystem scan does not cover by design. |
| 4 | Memory regression rule (`WeakRef` and forced GC) | Superseded | D2 (`313173b`) ends every session by a document navigation; `sessionEndCollected.test.tsx` is gone and the skill lines say "document navigation". No memory escape since. D2 itself broke three e2e specs on main (U2), which `ci-e2e-regression` now prevents. |
| 5 | Every route, every session state (review-story line) | Held | No recurrence. The skip-link escapes E34 and E35 are geometry, not session state. |
| 6 | CI network parity (build-story line) | The exact form held; the class did not | No stopped-container test since. The wider "test environment is not the real one" recurred: E18, E32, U1, and the jsdom cases (pattern 2). E18 got a deterministic check (`.github/actions/install-compose/install.sh:22`). |
| 7 | Pinned upstream behaviour (review-story, security-story lines) | Held for images | cloudwatch-metric-filter read the pinned collector's source (v0.164.0). A managed service's behaviour (RDS, U1) is outside the line. |
| 8 | Verifier tool access (`verify-with-copilot.sh`) | Did not hold | Pattern 4: 7 occurrences after the change, including a second postmortem (2026-11-02). The allow list (`scripts/verify-with-copilot.sh:37-38`) still lacks `scripts/diff-hash.sh`, the direct cause of E55 and the cloudwatch-metric-filter failure; its infra test scripts were dropped when they moved to xUnit (`72f7d0a`). Docker access in the sandbox is still unverified. |
| 9 | Fix-list rule: a moved header lists every response | Untested | No fix item moved a header or a behaviour's owner since. |

The lines added on 2026-11-04 (`f672263`: results file, throwaway images, `smoke.sh`, oldest tool version) show no
recurrence of their own escape. The #39 lines (2026-11-09: overlap and text scaling) are too new to judge; no frontend
story has built since.

## 5. Draft countermeasures (for the human, one batch)

Each is in AGENTS.md's order: a deterministic check first, then a skill line, then a story-template field. A change
to a skill, a script the guard bounds, CI or AGENTS.md asks.

1. **Verifier can run its own checks** (pattern 4).
   - Check: add `'shell(scripts/diff-hash.sh)'` to the allow list in `scripts/verify-with-copilot.sh:37`. Add a
     `scripts/verify-with-copilot.test.sh`, run in `ci.yml`'s floor-guard job, that fails when the prompt names a
     script the allow list does not allow. This is the planned `ci-hardening` feature's "the verifier's allow list is
     checked".
   - Decision for the human: a Small fix with no spec file fails the Copilot grade (cloudwatch-metric-filter). Either
     the script takes the fix's brief as its spec, or the Small tier skips the Copilot run.
2. **Breaks record matches `run-breaks.sh`** (pattern 5).
   - Check: `.claude/hooks/status_contract.py` refuses a builder report whose `breaks` entries lack `patch`,
     `test_method` or `expect`, or whose `test_results.files` is empty. `scripts/run-breaks.sh` gets a Vitest mode, or
     refuses a non-dotnet `test_method` that is not a command (lows row 45).
   - Skill line (build-story): "A fix-round builder re-runs every break with `scripts/run-breaks.sh`, not only its new
     ones (ui-lists)."
3. **Facts in briefs and specs come from a command** (pattern 1).
   - Check: widen `scripts/check-infra-paths.sh` from `infra/` to every top-level folder (`backend/`, `frontend/`,
     `e2e/`, `scripts/`), so a brief or spec naming a moved or made-up path fails the floor-guard job (E10, E13, E23).
   - Template field (`.claude/skills/bmad-agent-po/assets/ticket-template.md`): "Consumers: the pasted `git grep`
     output for every renamed name or text, shared component and moved path the spec touches" (E25, E40, E43, E53).
   - Process line (`docs/process/agentic-sdlc.md`, supervisor section; a rule change, asks): a brief names a test,
     reader or tool behaviour only from a command run on main, with the output pasted (E9, E10, E12, E18, E20, E29).
4. **AWS behaviour is checked after every deploy, not by chance** (pattern 3; infrastructure, so a human decision).
   - Check: `.github/workflows/deploy.yml` runs the read-only post-deploy checks after each deploy and fails the run.
     That means check 6 (database owners), check 12 (no active human admin) and the metric count, using
     `--recently-active` (lows row 62). They would live in one script under `infra/deploy/aws/`.
   - Check (lows row 66): a source assertion that neither the init script nor `deploy.sh` contains
     `createrole_self_grant` (U1).
   - Template field: "Real-environment differences: for each managed service or tool the change relies on (RDS,
     Compose, the CI runner), how it behaves there and the test, probe or post-deploy check that proves it."
5. **Text scaling and overlap before QA** (pattern 6).
   - Check: one e2e regression spec that, at 320px with the root font at 200%, asserts no horizontal overflow
     (`scrollWidth <= innerWidth`) on each journey page. It starts red until lows row 60 (the header) is fixed, so it
     lands with that fix.
   - Skill lines: already applied (#39). Judge them at the next retrospective.
6. **Captures are complete and comparable** (section 3).
   - Check: a `scripts/check-capture.sh <id>`, with its test in the floor-guard job. It fails a records pull request
     when the feature's `-review.md` has no Capture with an Escapes table and a Metrics table with AGENTS.md's columns,
     or when the fix rounds are 2 or more and there is no postmortem file.
   - Decision for the human: no stage records tokens and peak context, so the supervisor cannot derive them. Name the
     source, or drop the column (an AGENTS.md change).
7. **Collision check at dispatch** (pattern 9, E52, E59).
   - Check: `scripts/lane.sh start <id>` prints each file the spec's Touches shares with an open `feature/*` branch
     (`git diff --name-only origin/main...origin/feature/<other>`). It prints a list and does not fail, since
     different sections of a shared file are allowed.
8. **Accessibility states in the builder's self-check** (pattern 7).
   - Skill line (build-story): "A changed or replaced control: assert its accessible name and spoken form, each error's
     announcement, and focus after an error that follows a submit (ui-linking, ui-raise-review)."
9. **Identity-provider exposure on AWS** (pattern 10).
   - Check: already applied (aws-admin-lockdown's check 12 and the lock-down on every deploy). Lows rows 79 to 83 widen
     it.
   - Skill line (security-story): "A change that publishes a login or opens a public route lists every account whose
     sign-up or recovery goes through it (aws-admin-lockdown: the Zitadel admin's reset mail in Mailpit)."
10. **Mermaid diagrams render** (E28, proposed at the capture, never applied).
    - Check: the floor-guard job renders every mermaid block with a pinned mermaid-cli. This adds a pinned dependency,
      so it needs verifying first.

## 6. Stale skill lines (pruning candidates)

Only `build-story`, `review-story` and `security-story` have a "Known pitfalls" section; `verify-story` and
`qa-story` have none.

| Skill | Line (start) | Covered by | Prune? |
|---|---|---|---|
| review-story | "A named library method is checked for the layer that will call it ... (`LayerTests`; 3.9)" | `backend/tests/DisputeDesk.ArchitectureTests/LayerTests.cs:72` (`ApplicationAssembly_References_NoInfrastructureHostsOrNpgsql`): a provider-specific EF function needs the Npgsql assembly, which the test rejects | Yes |
| build-story | "A change that adds or changes infrastructure files ... runs `scripts/security-local.sh` ... (7.5)" | `.github/workflows/security.yml:119` runs `scripts/security-local.sh` on every pull request, required through `security-passed` (AD-32); the 7.5 escape (a red run after merge) cannot recur | Yes; the line only saves a round trip now |
| security-story | "A change that adds or changes infrastructure files ... gets `scripts/security-local.sh` (7.5)" | Same | Yes |
| security-story | "... sealed `ToString` on records holding personal data is enforced by `PersonalDataRecordTests`" | `backend/tests/DisputeDesk.ArchitectureTests/PersonalDataRecordTests.cs` | Cut that clause; keep "A privacy test checks every private field of the record it guards (3.8)" |
| build-story | "A change that relies on a tool behaviour (Compose, Docker, curl) checks it on the oldest version" | Compose only: `.github/actions/install-compose/install.sh:22` fails CI when its version differs from the AWS pin | Narrow it: drop the Compose example, keep the rule for other tools |
| build-story, security-story | "A change that ends a session ends it by a document navigation ..." | `e2e/tests/spa-session-end.spec.ts` (runs on every pull request) covers the existing ways out; a new way out needs a new case | No; partial cover |
| build-story | "Run the full suite with a results file (`--report-xunit-trx`), never through `tail`" | Not a check; it repeats procedure step 5 ("One full run with a results file") | Candidate as a duplicate, not as check-covered |
| build-story (procedure step 5) | "Tests that shell out ... run that test project once in the Linux SDK container" | Every check runs on Linux CI for each pull request before merge (ad-012), so the 2.6 escape (a Linux-only failure after merge) cannot reach main | Candidate; it is a procedure step, not a pitfall line |

Size: review-story's Known pitfalls holds 13 lines, over AGENTS.md's "at most about 10". Pruning the `LayerTests` line
leaves 12. Of the rest, the lines whose escapes have not recurred since epics 2 and 3 are the next ones to review:
bulk-delete on its own database (2.8), the microsecond clock (2.8; `floor-guard.sh:72` covers only exact equality
against the live clock), and the invariant enforced in code (3.8). None of them has a check yet.

```json
{"status": "approve", "record": "docs/verification/lows-2026-11-10-retrospective-input.md", "findings": [{"severity": "low", "file": "scripts/verify-with-copilot.sh", "line": 37, "issue": "The allow list lacks scripts/diff-hash.sh, so the Copilot verifier fails its hash check (ui-detail-start, cloudwatch-metric-filter); epic 7 countermeasure 8 did not hold (7 tooling occurrences since).", "fix": "Allow shell(scripts/diff-hash.sh) and add a test that every script the prompt names is allowed (proposal 1)."}, {"severity": "low", "file": "docs/verification/rds-init-grant-review.md", "line": 58, "issue": "rds-init-grant has no capture although its review asked for the escape (the fixture cannot model RDS role management) to be recorded; a live deploy failure is missing from the escape counts.", "fix": "Add the capture in the records of this batch (U1 in this input)."}, {"severity": "low", "file": "docs/verification/aws-admin-lockdown-review.md", "line": 0, "issue": "aws-admin-lockdown (and reviewer-docs) reached a second fix round with no postmortem, against AGENTS.md's immediate trigger.", "fix": "Write the postmortem, or make check-capture.sh require it (proposal 6)."}, {"severity": "low", "file": "docs/verification/ci-e2e-regression-review.md", "line": 275, "issue": "The 'Criterion 4 (CI runs)' section still reads 'To fill in after CI', and lows row 37 waits on it.", "fix": "Fill in the measured runs and wall time in the records."}, {"severity": "low", "file": ".claude/skills/review-story/SKILL.md", "line": 0, "issue": "Known pitfalls holds 13 lines, over the about-10 cap; one line is covered by LayerTests.", "fix": "Prune per section 6."}], "assumptions": [], "open_questions": [], "conflicts": []}
```
