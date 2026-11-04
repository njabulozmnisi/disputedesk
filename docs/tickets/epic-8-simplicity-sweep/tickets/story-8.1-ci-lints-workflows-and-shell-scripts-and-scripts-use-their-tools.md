---
id: 1
type: story
title: "CI lints workflows and shell scripts, and the scripts use their tools' documented features"
parent: epic-8-simplicity-sweep
covers: [R33, R34]
after: []
hitl: false
risk: low
refined: true
---

# CI lints workflows and shell scripts, and the scripts use their tools' documented features

## Description

Slice S7a of the simplicity audit's mitigation plan (wave 2). Every infra story asks for `actionlint` and `shellcheck`, but CI runs neither, so builders run them by hand and nothing holds them after the merge (INF-2). This story adds both to CI as pinned binaries, and then removes the hand-made checks and loops the tools already cover: the label-policy generator's CSS comment stripper and the 11 sed mutations in CI (INF-4), the brand-file byte check that Zitadel's upload already refuses (INF-5), `push-and-watch.sh`'s polling loop (INF-11), the second copy of the security scan command (INF-12), `check-ready.sh`'s prose grep for human decisions (INF-13) and `lane.sh`'s generated remote script and inline `python3` (INF-14). It lands first in the epic, so every later infra story is linted.

## Requirements

Verify: the CI `floor-guard` job runs actionlint and shellcheck and is green; the `platform` job's label-policy exact-values step and *Infra tests* step are green; `bash -n` and shellcheck pass on every changed bash script and `zsh -n` on `scripts/lane-runner.zsh` locally; each Supervisor check below fails as described.

Inputs (no endpoint changes):
- `label-policy.sh <tokens.css>`: one path argument. The eight tokens `--primary`, `--background`, `--destructive`, `--foreground` in the first `:root {` block and in the `:root` inside `@media (prefers-color-scheme: dark)`; each declared exactly once per block, on a line whose first non-blank text is the token name, as `<name>: #rrggbb;` (a comment may follow the `;`); the value exactly `#` plus 6 hex digits.
- `push-and-watch.sh`: its arguments and exit codes are unchanged (0 every workflow succeeded, 1 a run failed or was cancelled, 2 `gh` failed or no run appeared in time). `DISPUTEDESK_WATCH_TIMEOUT` (default 1800 seconds) now bounds the time to find both runs; each watched run is bounded by its jobs' `timeout-minutes`.
- `check-ready.sh <story-id>`: unchanged usage; reads one new front-matter field `decisions:` whose only accepted value is `settled`.
- `lane.sh`: unchanged commands and arguments; `LANE_AGENT` (default `backend-builder`) still picks the agent.

1. **actionlint runs on every workflow in CI** (INF-2; R33, NFR-INFRA-2)
   **Given** the `floor-guard` job
   **When** CI runs on a pull request or on `main`
   **Then** a step installs actionlint v1.7.12 from its GitHub release with a pinned SHA-256 checked by `sha256sum --check --strict` (the same shape as Security's *Install Trivy*), and runs `actionlint` on `.github/workflows/*.yml`, with shellcheck on `PATH` so actionlint also checks each `run:` block; any finding fails the job

2. **shellcheck runs on every tracked shell script** (INF-2; R33, R34)
   **Given** the same job
   **When** it runs
   **Then** shellcheck v0.11.0, installed the same way, checks every file `git ls-files ':(glob)infra/**/*.sh' 'scripts/*.sh'` lists (the `:(glob)` magic makes `**` match `infra/smoke.sh` at the top of `infra/` too), and any finding fails the job
   **And** each existing finding is fixed in this story; no `# shellcheck disable` is added without a comment on the same line saying why (R34); `scripts/lane-runner.zsh` is not linted, because shellcheck does not support zsh, and is checked with `zsh -n`

3. **The label policy is a plain lookup of eight declarations** (INF-4; R34)
   **Given** `infra/zitadel/provision/label-policy.sh`
   **When** it runs on `frontend/src/theme/tokens.css`
   **Then** it prints the same JSON body as today, reading each token from the lines of its block whose first non-blank text is `<name>:` (so `--primary:` never matches `--primary-hover:`, and a comment line whose first non-blank text is `/*` or `*` is never read), and exits non-zero naming the token when the block has no such line, more than one, or a value that is not `#rrggbb`
   **And** the awk comment stripper goes; a declaration that does not start its own line is not supported (the theme file never writes one), which the script header says
   **And** the CI step *Label policy generator rejects tokens outside its bounds* (`ci.yml:172-197`, 11 sed mutations) is removed; the exact-values step above it (`ci.yml:160-170`) stays unchanged; three cases in `infra/tests/DisputeDesk.PlatformTests` run the script on a temporary copy of `tokens.css` and prove a missing token, a token declared twice in one block and a value that is not `#rrggbb` each exit non-zero naming the token

4. **The brand byte checks are left to the upload** (INF-5)
   **Given** the CI step *Hosted-login brand files* (`ci.yml:199-216`)
   **When** this story lands
   **Then** its size and file-signature checks go, because `provision.sh`'s `upload` (`infra/zitadel/provision/provision.sh:63-69`, failing on HTTP 400 or above) is checked by Zitadel, which detects the type from the bytes and refuses an asset over 512 KiB (story 2.6)
   **And** the step keeps its last part, renamed *tokens.css names both wordmarks and loads neither*: the two `grep` lines (`ci.yml:212-216`) stay unchanged, because no upload checks them

5. **push-and-watch waits with `gh run watch`** (INF-11)
   **Given** `scripts/push-and-watch.sh` after its push
   **When** it waits for CI and Security on the pushed commit
   **Then** for each workflow it finds the run ID with `gh run list --commit "$sha" --workflow <name> --json databaseId`, retrying every 10 seconds until the run appears or `DISPUTEDESK_WATCH_TIMEOUT` passes (exit 2), then runs `gh run watch <id>` (without `--exit-status`, so a non-zero exit means `gh` itself failed: exit 2), then reads `gh run view <id> --json conclusion`: `success` continues, any other conclusion exits 1 naming the workflow and the run URL
   **And** a run that failed before the watch started returns at once and exits 1; a run that never finishes is ended by GitHub at its jobs' `timeout-minutes` (every job in `ci.yml` and `security.yml` sets one) and exits 1 as `cancelled`; the 30-second polling loop (`push-and-watch.sh:20-41`) goes; the header's comment on `DISPUTEDESK_WATCH_TIMEOUT` says what it bounds

6. **One filesystem scan command** (INF-12)
   **Given** Security's *Filesystem and config scan* step (`security.yml:125-126`)
   **When** this story lands
   **Then** the step runs `scripts/security-local.sh`, so the local and CI scans are one command; the script's header says CI runs it, and its two `--skip-dirs` stay (CI's checkout has neither directory, so the scan is the same)

7. **check-ready reads an exact front-matter field for human decisions** (INF-13; approved by the human 2026-11-04)
   **Given** a story whose challenge or pre-build review files hold a `[decision]` point
   **When** `scripts/check-ready.sh <story-id>` runs
   **Then** the points count as decided only when the first `decisions:` line inside the story's front matter (between its first two `---` lines) is exactly `decisions: settled`, as the script already reads the first `after:` line; one field settles every `[decision]` point of the story; a missing field, any other value, or the line only in the story's body leaves the story not ready
   **And** the prose match on "human" with "decision", "decided" or "settled" (`check-ready.sh:45-48`) and its header item 2 go; the header says the supervisor sets the field after the human has answered every `[decision]` point, citing the decisions record, and that the field records that step, not who decided (the record does)
   **And** the `[story]` disposition rule (`check-ready.sh:40-43`) and the `after` rule are unchanged

8. **lane.sh copies a committed runner and reads JSON with jq** (INF-14)
   **Given** `scripts/lane.sh start`, `wait` and `collect`
   **When** they run
   **Then** `start` copies a committed `scripts/lane-runner.zsh` (the body of today's generated heredoc) and starts it as `lane-runner.zsh <story> <agent>`, the agent being `${LANE_AGENT:-backend-builder}` read on the local machine as today, and keeps the remote `chmod +x`
   **And** `wait` and `collect` copy the lane's JSON output back with `scp` and read it with `jq` on the local machine: `wait` reads `subtype` and `terminal_reason` and treats missing or malformed JSON as `no-result` (exit 1, as today); `collect` reads `result` with `jq -er`, so a missing or null `result` exits non-zero instead of writing a report; no `python3` and no escaped heredoc remain, and the three commands otherwise behave as today

## Boundaries

- Must not change: the label policy's output; any other CI job or step; `provision.sh`; `check-ready.sh`'s exit codes; what `lane.sh` runs on the remote host.
- No new linter beyond actionlint and shellcheck; no pre-commit hook change.

## Supervisor checks

- Throwaway branch: add an unquoted `$var` in a `run:` block; the `floor-guard` job fails at actionlint.
- Throwaway branch: add `cd $dir` to `infra/smoke.sh`; the job fails at shellcheck (SC2086), which proves the `:(glob)` pathspec reaches it.
- Throwaway copy: remove `--primary` from the dark block of a copy of `tokens.css`; `label-policy.sh` exits non-zero naming `--primary`.
- Throwaway copy: a story with a `[decision]` point and each of: no field and the line "the human decided"; `decisions: pending`; `decisions: settled` in the body only. `check-ready.sh` reports each not ready, and ready once `decisions: settled` is in the front matter.
- Throwaway copy: `push-and-watch.sh` with `gh` stubbed on `PATH` to answer a `failure` conclusion, then a failing `gh run watch`; it exits 1, then 2.
- Throwaway copy: `lane.sh wait` against an output file holding `not json`; it prints `no-result` and exits 1.

## Build facts

- Owner: backend (backend-builder).
- Tier: Small (CI and tooling; no production code).
- Flags: none.
- Lane: A.
- Mechanisms and sources:
  - actionlint, including shellcheck on `run:` blocks: <https://github.com/rhysd/actionlint> (docs/checks.md, *shellcheck integration for run:*), release v1.7.12 (2026-03-30).
  - shellcheck: <https://www.shellcheck.net/>, release v0.11.0 (<https://github.com/koalaman/shellcheck/releases>); it supports sh, bash, dash and ksh, not zsh.
  - Git pathspec `:(glob)` magic: <https://git-scm.com/docs/gitglossary#Documentation/gitglossary.txt-aiddefpathspecapathspec>.
  - `gh run watch`: <https://cli.github.com/manual/gh_run_watch>; `gh run list --json`: <https://cli.github.com/manual/gh_run_list>; `gh run view --json`: <https://cli.github.com/manual/gh_run_view>; job `timeout-minutes`: <https://docs.github.com/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idtimeout-minutes>.
  - jq, `-e` exit status: <https://jqlang.org/manual/>.
- Packages: none. The two binaries are pinned by version and SHA-256 in `ci.yml`: actionlint from the release's `actionlint_1.7.12_checksums.txt` (the builder copies the line for `actionlint_1.7.12_linux_amd64.tar.gz`); shellcheck v0.11.0 publishes no checksum file, so its pin is GitHub's asset digest `sha256:8c3be12b05d5c177a04c29e3c78ce89ac86f1595681cab149b65b97c4e227198` for `shellcheck-v0.11.0.linux.x86_64.tar.xz` (pre-build review, 2026-11-04), which the builder re-reads from the release page. Both are the latest stable on 2026-11-04.
- Guard: `scripts/` is in backend-builder's paths (human approval, 2026-11-04, `docs/verification/epic-8-decisions.md`).
- When 8.1 lands (supervisor, docs-only, approved 2026-11-04): add the `decisions: settled` line to `docs/process/agentic-sdlc.md` and to the product owner's ticket template, and set `decisions: settled` on every story not yet done whose challenge or pre-build files hold a `[decision]` point the human has answered (today 8.7, 8.10 and 8.15), citing `docs/verification/epic-8-decisions.md`; otherwise the new rule reports them not ready.
- Touches:
  - `.github/workflows/ci.yml` (`floor-guard`: two install steps and two lint steps; `platform`: the mutation step removed, the brand step cut to its `grep` lines)
  - `.github/workflows/security.yml` (the filesystem scan step)
  - `infra/zitadel/provision/label-policy.sh`; `infra/tests/DisputeDesk.PlatformTests/` (the three label-policy cases)
  - `scripts/push-and-watch.sh`, `scripts/security-local.sh`, `scripts/check-ready.sh`, `scripts/lane.sh`, `scripts/lane-runner.zsh` (new)
  - any `infra/**/*.sh` or `scripts/*.sh` shellcheck flags
- Collision: 8.7 and 8.8 add to `infra/tests/DisputeDesk.PlatformTests` in other files; this story lands first.

## Pre-build

- Challenger: `docs/verification/8.1-challenge.md` (simplify).
- Review: `docs/verification/epic-8-prebuild-wave2.md`, section 8.1. No security read.
- Decisions: none raised as `[decision]`; the human approved the `decisions: settled` field on 2026-11-04 (`docs/verification/epic-8-decisions.md`).
- Sofia, 2026-11-04: every `[story]` point folded. Review: the `:(glob)` pathspec (criterion 2); the runner's agent argument and `wait`'s two fields (criterion 8); `gh run view` after the watch and what the timeout bounds (criterion 5, Inputs); the rejection cases as infra tests, a duplicate refused and comment lines never read (criterion 3); the wordmark `grep` kept (criterion 4); the two pins (Build facts); line numbers 45-48 and the field's writer (criterion 7, Build facts); zsh not linted (criterion 2). Challenger: brand checks the upload does not make kept (criterion 4); the overall bound restated with the job timeouts (criterion 5); `LANE_AGENT` carried, malformed JSON fails, `zsh -n` (criterion 8); the accepted line shape instead of a CSS parser (criterion 3, Inputs); front matter only, missing and other values, one field for all points, its limit stated (criterion 7, Supervisor checks); the 11 mutations go and a duplicate test stays (criterion 3).

## References

- parent — docs/tickets/epic-8-simplicity-sweep/epic-8-simplicity-sweep.md, section Requirements
- audit — docs/verification/simplicity-audit/infra-ci-scripts.md, INF-2, INF-4, INF-5, INF-11 to INF-14
- plan — docs/verification/simplicity-audit/mitigation-plan.md, wave 2, S7; wave 4, item 1
- story — docs/tickets/epic-2-transactions/tickets/story-2.6-hosted-login-pages-themed-from-the-design-tokens.md, Boundaries (the upload's refusal)
- decisions — docs/verification/epic-8-decisions.md (the `decisions: settled` field)

## Notes

- Risk low (proposed): the lint steps can only add failures; the removed checks are covered by the exact-values step, the three infra cases and the upload's refusal.
- Decision: 2026-11-04 (Sofia) — the linters go in `floor-guard`, the job with no services, so a lint failure shows within its 5-minute budget.
- Decision: 2026-11-04 (Sofia) — INF-12 takes the plan's second option (CI calls the script), because it leaves one command and changes no document.
- Decision: 2026-11-04 (Sofia) — INF-13 takes the plan's front-matter field; `decisions: settled` is the one value, so a typo fails closed. The human approved it on 2026-11-04.
- Decision: 2026-11-04 (Sofia) — `lane.sh` reads the JSON on the local machine after `scp`, so the remote host needs no `jq`.
- Decision: 2026-11-04 (Sofia) — the label lookup reads one declaration per line rather than parsing CSS, the simpler of the challenger's two options; the theme file writes every token that way, and a duplicate still fails, so a commented-out copy cannot be picked silently.
- Decision: 2026-11-04 (Sofia) — `push-and-watch.sh` bounds the run search with `DISPUTEDESK_WATCH_TIMEOUT` and leaves each run's length to its jobs' `timeout-minutes`, because `gh run watch` has no timeout and macOS has no `timeout` command; a wrapper that kills the watch would bring back the loop INF-11 removes. A run left queued for a runner is not bounded: the script says so in a `shortcut:` comment.
- Decision: 2026-11-04 (Sofia) — the brand step keeps its wordmark `grep`, because `provision.sh` uploads only when Zitadel has no asset, so the upload does not check a changed theme file on a provisioned stack.
