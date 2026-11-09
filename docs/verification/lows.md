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
| lows batch | ci-e2e-regression-review.md L1, ci-e2e-regression-verify.md L1 | The `Stack diagnostics` step shows only the last group's logs and `error-context.md`, because `e2e/run-regression.sh` removes each group's stack and Playwright clears its results; print `docker compose logs` in `run_group` when a group fails, before the next `down -v`, and give each project its own `--output`. | Open |
| lows batch | ci-e2e-regression-verify.md L2 | `e2e/run-regression.sh:47`: no `--wait-timeout 600`, so a stuck start runs to the job's 60-minute timeout. | Open |
| lows batch | ci-e2e-regression-review.md L2, ci-e2e-regression-verify.md L3 | `.github/workflows/ci.yml`: the e2e-regression comment calls the wall time an estimate; replace it with the measured time from the review record once CI has run it. | Open |
| lows batch | ci-e2e-regression-review.md L3 | `ComposeRenderTests.StackSources` covers build contexts, additional contexts and bind sources with the profiles hard-coded; a future `dockerfile` outside its context, `env_file`, configs or secrets file, `include` or `extends` would be missed. | Open |
| lows batch | ci-e2e-regression-review.md L4 | Step `decide`: after a stacked base branch is force-pushed, the event's `base.sha` may not be fetchable and the job fails with exit 2 (fails safe); diffing the merge commit's parents avoids it. | Open |
| ui-tokens-icons | ui-tokens-icons-review.md | `TransactionRow.tsx:65`: the hint's `gap-1.5` is outside DESIGN's spacing subset; `mt-0.5` needs a comment naming it the 14px icon's optical offset. | |
| ui-tokens-icons | ui-tokens-icons-review.md | `customerStatus.test.ts:33`: the SearchPriority test does not assert Search's handle path `m21 21-4.34-4.34`. | |
| ui-tokens-icons | ui-tokens-icons-review.md | `customerStatus.test.ts:23`: the `?? 'span'` placeholder is an indirect way to avoid a non-null assertion. | |
| ui-tokens-icons | ui-tokens-icons-review.md | `Header.tsx`: from 768px, signed in, the focused skip link covers 24.6px of the TopNav "Transactions" link (axe target-size passes; QA measured 80% visible). Note it in the comment as accepted under SC 2.5.8, or narrow the overlap. | |
| ui-tokens-icons | ui-tokens-icons-review.md | `Header.tsx:33`: the comment says the skip link sits at the padding edge; under CSS Flexbox 4.1 it sits at the content edge. | |
| ui-tokens-icons | ui-tokens-icons-verify.md | `scripts/run-breaks.sh` has no Vitest mode; a non-dotnet `test_method` must be a runnable shell command (queued by the guard owner for the next scripts/ change). | |
| ui-tokens-icons | ui-tokens-icons-verify.md | `scripts/verify-with-copilot.sh` run from a worktree that lacks the spec cannot grade criteria and writes its record into the worktree. | |
| ui-tokens-icons | ui-tokens-icons-qa.md | `linking-start.spec.ts` criterion 2 flakes when a random traceId contains "abc". | |
| ui-notices-states | ui-detail-start-review.md | `ErrorSummary.tsx:48`: the summary's field links are 22px tall at 767px and wider, against EXPERIENCE's 44px minimum (seen inside the withdraw dialog). | |
| ui-notices-states | ui-notices-states-review.md | One theme outline width token for the 1.5px border shared by Button, Input, Textarea and RadioGroup (raw `border-[1.5px]` in each). | |
| ui-linking | ui-linking-review.md | `input-otp.tsx:59`: `-m-px` does not keep the cells equal (the active cell is 43px against 41px at 320px, QA L2); draw the second pixel with `inset-ring` and drop `-m-px`. | |
| ui-linking | ui-linking-review.md | The "field holding 481" paste test pastes `4815 1678`, not the `4815167` criterion 3 names. | |
| ui-linking | ui-linking-review.md, ui-linking-qa.md | The inline "Start again" link is 81x22px: WCAG 2.5.8's inline exception applies, but DESIGN's 44px rule says otherwise; reconcile DESIGN with the inline-link pattern. | |
| ui-linking | ui-linking-review.md | A disabled field's label is not dimmed as the mockup draws it (`field.tsx`, outside the story's Touches). | |
| ui-linking | ui-linking-review.md | `docs/backlog/story-ui-linking.md:52`: the Inputs bullet still describes the old paste condition; the code refuses any paste holding more than 6 digits (add `481-5167` and `4815167\n` as refused rows). | |
| ui-linking | ui-linking-review.md | `OtpScreen.tsx:241`: after a wrong code the field error keeps role="alert", so a refused paste may be announced twice. | |
| ui-linking | ui-linking-qa.md | After a refused paste, "Enter the 6-digit code." stays beside six valid digits until the next Verify (validate-on-submit). | |
| carried | ui-linking-qa.md | A 429 from the gateway limiter on `/api/v1/antiforgery` leaves Verify with no visible notice. | |
