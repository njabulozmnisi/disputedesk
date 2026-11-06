# Epic 9 lows

Low findings from the epic's stages, fixed in one batch per epic (AGENTS.md, Story flow).

| Story | Record | Where | Finding |
|---|---|---|---|
| 9.1 | verify L1 | `scripts/ci-gates.sh` | Needs `yq` v4 on PATH and gives no clear "yq is required" message; `run-breaks.sh` reports every break as FAIL/ERROR without it. |
| 9.1 | review L1 | `scripts/ci-gates.sh:18` | A scalar `needs:` (valid workflow syntax) yields no entries from `.needs[]`, so the step names jobs as missing (fails closed); normalise with `.needs \| ([] + .) \| .[]` or fail with "needs must be a list", and add a test case. |
| 9.2 | security L1 | `scripts/deploy-workflow.test.sh:923`, `.github/workflows/` | The one-holder pin counts `id-token` only in `ci.yml` and nothing limits `environment: dev` to `deploy.yml`'s job; a later `pull_request_target` or `workflow_run` workflow naming `dev` would get the trusted subject. Extend the pin to every workflow file (P55's `job_workflow_ref` is the full fix). |
| 9.2 | verify L1 | `9.2-build.json` `test_results.files` | Lists two deliberate failing negative runs (test-before, old floor-guard arguments) among the results; keep them apart from `test_results.files`. |
| 9.2 | verify L2 | AC3, AC5, AC7 | No automated test: GitHub-side run on main, the human environment step, README text. |
| 9.2 | review L1 | `scripts/deploy-workflow.test.sh:110,115,119,163,175` | Five new `# shellcheck disable=SC2016` lines carry no written reason; add the reason (or one file-wide directive with it). |
| 9.2 | review L2 | `scripts/deploy-workflow.test.sh:43` | Extracted steps run with `bash --noprofile --norc -eo pipefail`, but GitHub runs them as `bash -e {0}`; run them with `bash -e` or set `shell: bash` on them. |
| 9.3 | review L1 | `infra/README.md:84`, story 9.3 | `FORBIDDEN_TERMS` was never set on the repository, so the forbidden-terms check had never run in CI; criterion 4's push needs it set first, and the story did not name the step (capture as a pre-build escape). |
| 9.3 | review L3 | story 9.3 Boundaries | The literal Boundary forbade the `floor-guard` line that runs the story's own test (amended); the story template should exempt a story's own tests and their `floor-guard` line. |
| 9.3 | review L4 | `.github/workflows/ci.yml:301` | A fork pull request that edits `ci.yml` runs its own workflow and can make `ci-passed` pass; not exploitable (only the owner merges, forks are not merged); record the limit and keep fork-workflow approval on. |
| 9.2 | supervisor check C | story 9.2 Supervisor checks | "a 39-character sha fails the images step": the SHA is checked by the step before `images` ("Check the SHA"); reword the check. |
| 9.2 (supervisor) | guard | `.claude/hooks/guard.py` deploy ask | `gh workflow run -R <repo> deploy.yml` did not ask: the pattern `gh\s+workflow\s+run\s+\S*deploy` misses options between `run` and the workflow name; 9.4's closed list must allow options between the command words (evidence: the 9.2 A1/A2 dispatches). |
| 9.1 (CI) | flaky | `.github/actions/install-compose/install.sh`, `install-trivy/install.sh` | GitHub release downloads returned 504 for longer than `curl --retry 3 --retry-all-errors` covers (9.2 branch CI, 2026-11-05): add `--retry-delay`/`--retry-max-time` so retries span a minute or two, and the same on the Trivy and Gitleaks installs. |
| 9.4 | review L1 | `.claude/hooks/guard.py` | Indirection is not seen (variables, aliases, scripts); accepted limit; the retro-8 default-deny allow-list for subagents closes the class. |
| 9.4 | review L2 | `.claude/hooks/guard.py` | `gh workflow run <numeric id>` gets no deploy ask; `gh run rerun` of a main run is neither asked nor denied (both predate the story). |
| 9.4 | review L3 | `.claude/hooks/guard.py` | A main-session commit message or PR body naming a force form is denied (fails closed); use `git commit -F` and `gh pr create --body-file`. |
| 9.4 | review L4 | `.claude/hooks/guard.py` | Subagent false positives: graphql reads with `-f`, `git merge-base`. |
| 9.4 | verify R1-L1 | `.claude/hooks/guard.py` | `gh api -X POST -X GET` is denied to a subagent although gh sends GET; fails closed, consistent with criterion 5. |
| 9.4 | supervisor (tooling) | `scripts/story-patch.sh` | Leaves untracked files out of the patch unless they are marked intent-to-add; marking them changes the worktree index and so `diff-hash.sh` (seen in 9.4 round 2). Make story-patch include untracked files itself without touching the index (retro-8 batch story). |
| 9.4 | verify round 3 L1 | `.claude/hooks/guard.py` | Variable, `${X:-...}`, alias and `env -S` indirection in front of git or gh are not read (as in v3). |
| 9.4 | verify round 3 L2 | `.claude/hooks/guard.py` | The main session is asked on a quoted lone separator before a plain push (fails closed). |
| 9.4 | review R3-L2 | `.claude/hooks/guard.py` | Main session: a force push whose program name a substitution builds gets no decision. |
| 9.4 | review R3-L4 | `.claude/hooks/guard.py` | A heredoc commit message with an apostrophe asks the main session (fails closed); `git commit -F` avoids it. |
| 9.4 | build round 4 | `.claude/hooks/guard.py` | `echo '...' \| ssh <host>` (ssh with no remote command runs its stdin remotely) is not read. |
| 9.4 | build round 4 | `.claude/hooks/guard.py` | A word naming a shell in a command that also names git or gh fails closed (`which bash && git status` asks). |
| 9.4 | verify round 4 M1 (deferred, human decision 2026-11-06) | `.claude/hooks/guard.py` | A quoted redirection operator (`-o '>'`) is dropped with the next word, so a force option after it is lost: the main session's deny becomes an ask. Closed by the retro-8 allow-list story (shlex punctuation mode keeps a quoted operator a word). |
| 9.4 | verify round 4 M2 (deferred, human decision 2026-11-06) | `.claude/hooks/guard.py` | Shells and interpreters outside the stdin-shell list (tcsh, csh, fish, ash, mksh, pwsh, python3) read piped text unseen, force flag included; v3 denied these. Closed by the retro-8 default-deny allow-list for subagents. |
| 9.4 | verify round 4 L1 | `.claude/hooks/guard.py` | Static indirection is not read: `read` plus `eval`, `$BASH`, `$0`, a script file then `sh`, `. /dev/fd/3`. |
| 9.4 | verify round 4 L2 | `.claude/hooks/guard.py` | The stdin-shell check falsely asks or denies read-only git when a word `sh`, `bash` or `ksh` is followed only by options (`git log --grep bash`, `git status \| grep sh`). |
