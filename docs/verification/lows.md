# Lows

Low findings left after a merge (AGENTS.md, Story flow, Fix rounds). They are fixed in the lows batch (AGENTS.md,
Continuous improvement). The feature column names the feature spec that closes the row; "carried" in the record
column marks a finding with no feature record of its own.

| Feature | Record | Finding | Closed |
|---|---|---|---|
| guard-allow-list | carried | `.claude/hooks/guard.py`: indirection is not seen (variables, aliases, scripts); an accepted limit. The default-deny allow-list for subagents closes the class. | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py`: `gh workflow run <numeric id>` gets no deploy ask; `gh run rerun` of a main run is neither asked nor denied. | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py` deploy ask: `gh workflow run -R <repo> deploy.yml` did not ask, because the pattern `gh\s+workflow\s+run\s+\S*deploy` misses options between `run` and the workflow name; the closed list must allow options between the command words. | Closed: `gh workflow run -R <repo> deploy.yml` asks (`scripts/guard.test.sh:300`); the numeric workflow ID is the row above. |
| guard-allow-list | carried | `.claude/hooks/guard.py`: a main-session commit message or PR body naming a force form is denied (fails closed); use `git commit -F` and `gh pr create --body-file`. | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py`: subagent false positives: graphql reads with `-f`, `git merge-base`. | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py`: `gh api -X POST -X GET` is denied to a subagent although gh sends GET; fails closed. | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py`: variable, `${X:-...}`, alias and `env -S` indirection in front of git or gh are not read. | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py`: the main session is asked on a quoted lone separator before a plain push (fails closed). | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py`: main session: a force push whose program name a substitution builds gets no decision. | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py`: a heredoc commit message with an apostrophe asks the main session (fails closed); `git commit -F` avoids it. | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py`: `echo '...' \| ssh <host>` (ssh with no remote command runs its stdin remotely) is not read. | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py`: a word naming a shell in a command that also names git or gh fails closed (`which bash && git status` asks). | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py` (ranked medium, deferred to this feature by the human, 2026-11-06): a quoted redirection operator (`-o '>'`) is dropped with the next word, so a force option after it is lost: the main session's deny becomes an ask. | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py` (ranked medium, deferred to this feature by the human, 2026-11-06): shells and interpreters outside the stdin-shell list (tcsh, csh, fish, ash, mksh, pwsh, python3) read piped text unseen, force flag included. | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py`: static indirection is not read: `read` plus `eval`, `$BASH`, `$0`, a script file then `sh`, `. /dev/fd/3`. | Open |
| guard-allow-list | carried | `.claude/hooks/guard.py`: the stdin-shell check falsely asks or denies read-only git when a word `sh`, `bash` or `ksh` is followed only by options (`git log --grep bash`, `git status \| grep sh`). | Open |
| process-checks | carried | Builder `breaks` format: break patches had no `@@` hunk headers, so `scripts/run-breaks.sh` refused them; the build-story procedure should require patches made with `git diff` (a check in `run-breaks.sh` or the status hook). | Open |
| process-checks | carried | `scripts/lane.sh collect`: the builder staged its base instead of committing it as briefed, so the diff hash reproduced only against a commit built from its index; `lane.sh collect` should refuse a worktree whose index differs from HEAD. | Open |
| process-checks | carried | `scripts/story-patch.sh`: leaves untracked files out of the patch unless they are marked intent-to-add; marking them changes the worktree index and so `diff-hash.sh`. Make story-patch include untracked files itself without touching the index. | Open |
| skill-lines | carried | Spec template: a spec's literal Boundaries forbade the `floor-guard` line that runs the spec's own test; the spec template should exempt a spec's own tests and their `floor-guard` line. | Open |
| process-checks | feature-ids-prebuild-review.md S13 | `scripts/lane.sh:31`: the work ID goes unquoted into remote shell strings with no shape check; check it against the slug pattern and exit 2 otherwise. | Open |
| skill-lines | feature-ids-review.md L1 | Slug uniqueness covers only other feature slugs; a slug must also never equal, or dash-prefix, an existing `docs/verification/` file stem (`session-fix-*`, `e2e-fix-*`, `tf-gateway-spa-*`, `flow-tooling-*`). | Open |
| feature-ids | feature-ids-review.md L2, feature-ids-verify.md L2 | The planned rows of the Features table link no spec yet; link each title when its spec file is written. | Open |
| process-checks | feature-ids-review.md L3 | Criterion 5's glossary entry for "feature spec" has no assertion; add one to `scripts/rule-text.test.sh`. | Open |
| process-checks | feature-ids-review.md L4 | `scripts/verify-with-copilot.sh` usage text and `scripts/diff-hash.sh:5` still say `<story>`; say `<id>` and `<spec-file>`. | Open |
| process-checks | feature-ids-verify.md L1 | `scripts/check-ready.sh:57` deletes commas, so `after: [story-a,4]` merges into `story-a4` and skips the numeric-entry rejection; turn commas into spaces and test the no-space form. | Open |
