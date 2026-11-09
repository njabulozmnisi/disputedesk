# guard-allow-list: pre-build review

Reviewer, 2026-11-06. Spec: section "feature/guard-allow-list" of `~/disputedesk-rename/feature-backlog/specs.md`
(lines 172 to 323), with the file's header (ownership, the rule diff, build order). Read as a design against main at
`ef88282`: `.claude/hooks/guard.py` (v5), `scripts/guard.test.sh` (504 runs, all as expected here in 1m46s),
`.claude/settings.json`, AGENTS.md, CLAUDE.md, `docs/process/agentic-sdlc.md`, the six agent files, the build, verify,
review, qa and security skills, `scripts/verify-with-copilot.sh`, the three workflows, and the open rows of
`~/disputedesk-rename/feature-backlog/epic-9-lows.md`. Checklists: review-story Pass 1 and Pass 2, the standards check,
and the security-story checklist. The spec's `sweep-R` (`5c0e6a0`) is not in this clone; its `guard.py` citations
match `ef88282`.

Verdict: **changes**. Five high `[story]` points, ten medium `[story]` points, two `[decision]` points, and lows. The
allow-list idea is right and explainable, but as written it does not deny every unrecognised form: it is triggered by
the literal text `git` or `gh`, it skips leading assignments, and its criterion 4 drops today's subagent denies.

## The four questions in the brief

**1. Is it default-deny for every unrecognised form?** Only inside a command whose text literally names git or gh
(criterion 3). Outside that trigger, everything gets no decision as today:

- a program that runs git internally: `make`, `npm run x`, `dotnet` (an MSBuild `Exec`), `python3 script.py`, a repo
  script, the Copilot child process of `verify-with-copilot.sh` (it allows `shell(npm:*)`, `shell(npx:*)`,
  `shell(dotnet:*)`, `shell(terraform:*)`, line 37). See D1;
- a program name built without the literal text. Probed on v5 as `reviewer`, each gets no decision, and the spec's
  criterion 3 would not fire on any of them: `/usr/bin/gi? commit -m x`, `g$@it commit -m x`,
  `G=gi; ${G}t commit -m x`. See H3;
- git told by its environment to run a program: `GIT_EXTERNAL_DIFF='…' git diff` and
  `GIT_CONFIG_COUNT=1 GIT_CONFIG_KEY_0=core.fsmonitor GIT_CONFIG_VALUE_0='…' git status` (probed: no decision today).
  The spec's program position skips leading assignments, and both git calls are on the read list. See H2;
- a same-call write into git's config followed by a read (`printf … >> .git/config; git status`): only readers and a
  read. See H4;
- any tool other than Bash that runs a command. The hook matcher is `Bash|Write|Edit|MultiEdit|NotebookEdit`, and the
  builders and qa have no `tools:` list. See H5.

Criterion 4's "a subagent is judged by criteria 1 to 3 only" also removes subagent denies that exist today. See H1.

**2. Two sentences?** The subagent rule can be explained in two sentences once H2 and H3 are folded in: "A subagent may
run git and gh only in the forms its role lists, each command written as plain words with no assignment in front, beside
only a short list of text readers. Every other program, program name or option in a command that names git or gh is
denied, and every rule that denies a subagent today still applies." The main session cannot be explained in two
sentences. It keeps all of v5's reading machinery (the rough reading, the recursion into runner text, the two readings
of a comment continuation), and criterion 5 adds a second `shlex` reading and a new ask class. That is acceptable
because the human runs the main session, but the guard's header should say so (L3).

**3. Are the roles' forms complete enough?** Yes for every git or gh step the flow writes down: build-story step 5 (the
full run's known substitution, `git apply --check`, `git diff` patches), CLAUDE.md's stacked
`git checkout --detach <ref>`, verify-story step 1 (`git rev-parse HEAD`, `git status --porcelain`), review-story's
`git diff main...HEAD`. qa runs no git. The gaps are reads that agents use ad hoc. Some flip existing `none / none`
cases (M9, L1). The full run survives only if the name check runs after the known-substitution placeholder (M7).

**4. How are the main session's asks kept?** By leaving the main path as it is and changing it only through criteria 4
to 7, with the existing 504 main-session expectations as the regression net. Two gaps. The spec does not require each
existing main expectation to stay unchanged except the listed flips, which leaves the net loosenable (M9). Criterion 6
misses deploy paths through `gh api` (M1). Criterion 5 adds asks that are not actions on a deployed environment, which
CLAUDE.md's guard sentence does not allow (D2).

## What holds

- A default-deny allow-list is the documented approach for running OS commands: OWASP Input Validation Cheat Sheet
  (allow-list), and more precisely the OWASP OS Command Injection Defense Cheat Sheet, which lists allowed commands and
  their allowed arguments
  (<https://cheatsheetseries.owasp.org/cheatsheets/OS_Command_Injection_Defense_Cheat_Sheet.html>). Citing it would be
  a better source for criteria 1 and 3.
- Unknown `agent_type` values get the read list only (Inputs), which is default-deny on the role.
- Keeping `-c`, `--config-env`, `--exec-path`, `-O`, `--output` and `--ext-diff` away from subagents is right. So is
  leaving out `sed` (GNU `e` command), `awk` (`system()`), `find -exec`, `xargs`, `tee` and `watch`.
- Criterion 5's second `shlex` reading without `punctuation_chars` fixes the quoted-operator escape (lows row 32). In
  punctuation mode a quoted `>` and an unquoted one give the same token, which is why `without_redirections`
  (`guard.py:194-208`) drops the next word.
- The ruleset is a real server-side backstop for force pushes and deletion of `main` (AD-32).
- Stdlib only. No new package. The criteria count is 8, each with a mechanism and a source (Definition of ready).

## Findings

### High

**H1 `[story]` Criterion 4 removes today's subagent denies** (specs.md:261, "a subagent is judged by criteria 1 to 3
only"). Read literally, a subagent then gets no decision for anything that does not name git or gh. Today these are
denied to subagents: a command substitution (`guard.py:398`; cases `guard.test.sh:160-161`), `terraform apply` and
`aws` (`guard.py:50-54, 413-416`; cases 316-317), `deploy.sh`, `docker system prune`, `dotnet add package MediatR` and
`--no-verify` (`BASH_DENY`, `guard.py:42-47`). The Description (line 187) says the main session keeps its rules, but it
says nothing on these subagent rules.
Fix: state that every v5 deny, the subagent substitution deny and the ask-to-deny conversion still apply to subagents,
before the allow-list, and keep their cases in the subagent columns.

**H2 `[story]` Program position is not exact: leading assignments and wrapper options** (specs.md:215-216). The
program is taken "after leading assignments and the wrappers", so neither is judged.
- Assignments. git's environment makes it run a program or take configuration. `GIT_EXTERNAL_DIFF` runs a program for
  `git diff`. `GIT_CONFIG_COUNT`/`GIT_CONFIG_KEY_<n>`/`GIT_CONFIG_VALUE_<n>` and `GIT_CONFIG_PARAMETERS` are the env form
  of the denied `-c`. `GIT_SSH_COMMAND` applies to the builders' `git fetch`. For gh, `GH_BROWSER`/`BROWSER` apply to
  `--web` (git(1), Environment Variables, <https://git-scm.com/docs/git#_environment_variables>; gh environment,
  <https://cli.github.com/manual/gh_help_environment>). Probed on v5: `GIT_EXTERNAL_DIFF='…' git diff` and the
  `GIT_CONFIG_*` form get no decision as `reviewer`.
- Wrapper options. If criterion 1 reads only the program-position word, an unmodelled wrapper option puts a reader in
  program position and makes git its argument: `env -u cat git push …` has the "program" `cat`, so criterion 3 passes
  it. v5 denies it only because `programs()` (`guard.py:244-253`) treats every git or gh word as a call.
  "Read as today" (line 222) leaves open which reading holds.
Fix: in a subagent command that names git or gh, allow no leading assignment except the owner-token form
(`GH_TOKEN=` the known substitution). Allow a wrapper only bare, except `timeout <duration>`; a wrapper with any other
option counts as a non-reader program. Add a case for each env variable above and for `env -u cat git push`.

**H3 `[story]` The trigger is still a name match** (specs.md:245, "whose text … names git or gh"). A program word built
by expansion never contains the literal text: a glob (`/usr/bin/gi?`), an empty expansion (`g$@it`), a split variable
(`G=gi; ${G}t`), ANSI-C quoting (`$'\x67it'`) or brace expansion (`{g,}it`). Probed: the first three get no decision on
v5, and the spec's criterion 3 would not fire. The variable rule in criterion 3 applies only once the text names git.
Fix, simple and default-deny: for every subagent command, whether or not it names git or gh, a program-position word
that is not a plain literal is denied. A plain literal has no `$`, `*`, `?`, `[`, `{`, backslash, quote or `$'…'`. The
builder's full run is still a literal after the known-substitution placeholder (M7). POSIX 2.9.1 (simple commands) and
2.6 (word expansions) are the sources. Add the five forms as Closes cases.

**H4 `[story]` A same-call write into git's files runs a program** (specs.md:189-192 excludes only the cross-call
case). Readers keep their redirections, so `printf '[core]\n\tfsmonitor = …\n' >> .git/config; git status` has only a
reader, a redirection and a read-list call. `core.fsmonitor`, `diff.external`, `core.sshCommand` and an `include.path`
then run on the next git call in the same command (git-config, <https://git-scm.com/docs/git-config>). A write to
`.githooks/post-checkout` before `git checkout --detach` works the same way for a builder. The Write and Edit tools
cannot do this, because ownership denies `.git/` and `.githooks/`; a Bash redirection skips ownership.
Fix: in a subagent command that names git or gh, a write redirection whose target names git (`.git/`, `.githooks/`,
`.gitattributes`, `.gitmodules`, a `gitconfig`, `.config/git/`, `.config/gh/`) or holds an expansion is denied. Keep
`git diff > x.patch` working. Add cases on both sides.

**H5 `[story]` Tools other than Bash are not read** (`.claude/settings.json` PreToolUse matcher
`Bash|Write|Edit|MultiEdit|NotebookEdit`). `backend-builder`, `frontend-builder` and `qa` have no `tools:` field, only
`disallowedTools: Agent`, so they get every tool. In this build (Claude Code 2.1.310) the Bash tool's own description
sends waits to a Monitor tool. I did not inspect that tool's input, so whether it runs a shell command is unverified.
Any tool that runs a command is outside the allow-list.
Fix: give the builders and qa a `tools:` allow-list, as reviewer, verifier and security already have (Claude Code
subagents, `tools` field, <https://code.claude.com/docs/en/sub-agents>). That is default-deny at the tool layer. Or add
every command-running tool to the matcher with the Bash rules. The agent files then join the rule diff (M8).

### Medium

**M1 `[story]` Criterion 6 misses deploy paths through `gh api`** (specs.md:276-287).
- `BASH_ASK` (`guard.py:53`) asks a dispatch only when its path contains `deploy`, so
  `gh api -X POST repos/<r>/actions/workflows/<numeric id>/dispatches` gets no ask. That is the same numeric-ID gap
  criterion 6 closes for `gh workflow run`.
- `…/actions/jobs/<id>/rerun` is not in the `…/actions/runs/<id>/rerun*` pattern.
- `…/actions/runs/<id>/pending_deployments` (approve a waiting deployment) and `…/deployments` are not covered.
- Unverified: a check-suite or check-run `rerequest` may re-run an Actions run.
Simplify too: only `deploy.yml` has `workflow_dispatch` (`ci.yml` and `security.yml` have none), so the closed list
`ci.yml, security.yml, CI, Security` lets through only calls that fail. "Every `gh workflow run` asks the main
session" is simpler and correct. For `gh api`, a main-session write whose path contains `/actions/` (outside the
`actions/variables` rule) or `/deployments` asks. Sources: GitHub REST, workflows (create a dispatch event),
workflow jobs (re-run a job), workflow runs (review pending deployments).

**M2 `[story]` The GraphQL read test can be bypassed** (specs.md:211-212). "Starts with `query` or `{`" checks only
the first operation. A document holding `query … mutation M …` with an `operationName` field picks the mutation
(GraphQL spec, GetOperation, <https://spec.graphql.org/October2021/#GetOperation()>). The spec also leaves open:
`-F query=@file` (unread), a repeated `query` field, and the long forms `--field` and `--raw-field`.
Fix: exactly one `query` field, through `-f` or `--raw-field`; no `operationName` field; no `-F` value starting with
`@`; the text contains neither `mutation` nor `subscription` anywhere (fails closed). Add cases for each.

**M3 `[story]` Option bounds for the denied and the builder options** (specs.md:199-200, 207-208).
- git's parse-options accepts glued values and unambiguous long prefixes (`--out=x`, `--ext`, `--open`, `-Ovim`).
  `is_force_argument` already handles prefixes (`guard.py:303-309`); say the same here.
- `-c`, `--config-env` and `--exec-path` are global options (before the subcommand). Denied anywhere, they would deny
  `git grep -c`, `git log -c` and `git diff -c`.
- `git fetch` also needs `--upload-pack`/`--exec` denied.
- `git apply` also needs `-3`/`--3way` (implies `--index`), `-N`/`--intent-to-add` (writes the index, the diff-hash
  breakage of lows row 25), `--unsafe-paths` and `--build-fake-ancestor` denied.
- `git checkout --detach <ref>` must be the exact argv, with no `-f` and no `--`.
Source: gitcli, "Options" (<https://git-scm.com/docs/gitcli>); git-apply; git-fetch.

**M4 `[story]` Read subcommands that also write** (specs.md:205-206).
- `git branch` with no positional word still writes: `--unset-upstream`, `--set-upstream-to=x`, `--edit-description`
  (runs the editor).
- `git config --get|--get-all|--list` should be exactly one read action, with no `--edit`, `--add`, `--unset` or
  `--replace-all`.
Fix: a closed option set for `branch`, `remote` and `config`. For `branch`: `--list`/`-l` with a pattern,
`--show-current`, `-a`, `-r`, `-v`, `--contains`, `--merged`, `--no-merged`, `--format`, `--sort`.

**M5 `[story]` Two readers write or run a program** (specs.md:213-214). On this machine `sort` (2.3-Apple) has
`-o outfile` and `--compress-program program` (seen in `sort --help`). GNU sort has the same. `uniq in out` writes its
second operand. Drop `sort` and `uniq`, or deny those options and the second operand. Add a case.

**M6 `[story]` The breaks prove only the permissive side** (specs.md:306-307). Removing an allow entry or a reader
makes a `none` case fail, which proves the entry is needed. It does not prove the default-deny. Require widening breaks
too: add `push` to the builder list, add `python3` to the readers, drop the literal-program check (H3), accept
`--cached` for `git apply`. Each must make a deny case fail.

**M7 `[story]` Name the order: the known substitutions come out before the name check** (specs.md:217, 245). The
builder's mandated full run, `scripts/test-lock.sh run testcontainers <id> -- dotnet test … "$(git rev-parse
--show-toplevel)/TestResults"` (build-story step 5; case `guard.test.sh:423`), has `dotnet` and `test-lock.sh`, neither
a reader. It survives only if criterion 3 reads the text after `KNOWN_SUBSTITUTION` is replaced (`guard.py:390`).
State it, and keep 423 as `none` for builders.

**M8 `[story]` The rule diff is incomplete** (specs.md:177, "Rule diff: `.claude/hooks/guard.py`"). Criterion 7
changes `docs/process/agentic-sdlc.md` section 2 (line 107 still reads `--body <body>`), which is outside every
builder's paths and compared by the verifier's `cmp`. H5's fix touches `.claude/agents/backend-builder.md`,
`frontend-builder.md` and `qa.md`. List them in the rule diff.

**M9 `[story]` Moved coverage: list the flips and pin the main column** (specs.md:301-305). Splitting the subagent
column into roles changes existing expectations. These `none / none` cases become deny for some or all roles:
- `git rebase` (392);
- `gh help` (391);
- `gh variable list`/`get` and `gh secret ls` (390, 411, 427);
- the `ls` aliases (426, 428);
- `gh repo deploy-key list` (419);
- `gh api -X GET … -f` (382, 399, 400);
- `git -c 'a;b' status` (430);
- `sh -c 'git status'` (453);
- `git apply` and `git fetch` (393, 394) for the read-only roles.

The spec should carry a table of each flip, and state that every main-session expectation stays as on `ef88282` except
the cases criteria 4 to 7 name. Without that, the 504-run net does not hold the main session's asks.

**M10 `[story]` Criterion 5's fed-text ask has no bound** (specs.md:268-270). "Text naming git or gh is piped …
into a program that is not on the reader list" does not say whether `gh pr view 5 --json x | python3 -m json.tool`
(gh's output, not text naming gh) asks. Define it. One option: a word naming git or gh that is an argument, not a
program, in an earlier stage of the pipeline, a here-string or a heredoc body. Put a case on each side.

### Decisions

**D1 `[decision]` medium. Programs that run git internally stay unread.** The spec's aim, and the human's approved
countermeasure (lows rows 20, 33: "the default-deny allow-list for subagents closes the class"), is to close the class.
A per-call hook cannot do that for `make`, npm scripts, `dotnet` targets, `python3 file.py`, repo scripts or the
Copilot child process. A per-role program allow-list would not close it either: builders run `npm` and `dotnet` on
scripts they write. Options:
- (a) Name the limit in the guard's header beside the cross-call one, with the same upgrade path (the Claude Code
  sandbox or an OS sandbox).
- (b) Add the server-side backstop that matters most. A required reviewer on the `dev` environment (GitHub, deployment
  protection rules,
  <https://docs.github.com/en/actions/managing-workflow-runs-and-deployments/managing-deployments/managing-environments-for-deployment>)
  makes any deploy, from any unread path, wait for the human. That is an infra change and needs its own approval.
- (c) Sandbox the subagents now.

Recommended: (a) now, (b) before `DEPLOY_ENABLED` is turned on.

**D2 `[decision]` medium. New asks against CLAUDE.md's guard sentence.** CLAUDE.md says the guard turns only actions
on a deployed environment into a permission prompt (`guard.py:48` cites the 2026-11-05 decision). Criterion 5 adds
asks for text fed to a non-reader, and criterion 7 keeps the fail-closed asks. Both are confirmations, not deployed
environment actions. v5 already asks on unreadable commands, so the sentence and the code already differ. Either
CLAUDE.md's sentence is amended in the rule diff (a rule change, human gate), or these forms deny in the main session
too.

### Low

- **L1 `[story]` Read forms worth adding.** git: `version`/`--version`, `help`, `check-ignore`, `show-ref`, `diff-tree`,
  `name-rev`. gh: `pr status`, `variable list|get`, `secret list`, the `ls` aliases, `auth status`, and `api -X GET`
  with `-f` (query parameters). Also: the build-story Breaks step should say a builder reverts a break with
  `git apply -R`, since `restore`, `checkout --` and `stash` are denied, and that a DIRTY fix round resolves with
  `git apply --reject`, since `--3way` needs the index (M3). `build-story` is a rule file.
- **L2 `[story]` Test runtime.** 504 runs took 1m46s here. Eight decisions per case is about four times the guard calls
  (roughly 7 minutes) in `floor-guard`, CI and the verifier's step 5. Run three classes (main session, builder,
  read-only) with one member each, plus one loop that proves each of the eight `agent_type` values maps to its class.
- **L3 `[story]` Header.** Add the main-session reading's complexity (question 2), the Copilot child process (D1), and
  that `which bash && git status` is still denied to a subagent (`which` is no reader). Criterion 4's "false positive
  closed" holds for the main session only.
- **L4 `[story]` Fix rounds on `guard.py`.** The supervisor writes the guard and a builder cannot change it. A
  finding in it needs a new human-approved rule diff and a new `cmp` target. Say so, given four rounds on v5.

## Security checklist

Supply chain: stdlib only, no package. Standards: allow-list per OWASP (cite the OS Command Injection Defense Cheat
Sheet); PreToolUse `agent_type` and `permissionDecision` per Claude Code hooks; tool restriction per the subagent
`tools` field (H5); upgrade path per the Claude Code sandbox. AuthN/Z, session, linking, data, audit and headers are not
touched by this spec.

## Escapes

To fill after merge.

```json
{"status": "changes", "record": "docs/verification/guard-allow-list-prebuild-review.md", "findings": [{"severity": "high", "tag": "story", "id": "H1"}, {"severity": "high", "tag": "story", "id": "H2"}, {"severity": "high", "tag": "story", "id": "H3"}, {"severity": "high", "tag": "story", "id": "H4"}, {"severity": "high", "tag": "story", "id": "H5"}, {"severity": "medium", "tag": "story", "id": "M1-M10"}, {"severity": "medium", "tag": "decision", "id": "D1, D2"}, {"severity": "low", "tag": "story", "id": "L1-L4"}]}
```
