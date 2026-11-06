#!/usr/bin/env bash
# Proves the guard's Bash rules (story 9.4, criteria 4 to 6): each case is fed to .claude/hooks/guard.py as
# PreToolUse hook JSON, once as the main session (no agent_type) and once as a subagent (agent_type backend-builder),
# and both decisions are compared with the expected ones: none (the guard prints nothing), ask or deny. Every `gh`
# case runs again with the owner-token prefix the flow puts before each `gh` write. Every case that differs is named,
# then the test exits 1.
#   scripts/guard.test.sh
# Needs bash, jq and python3 (3.10 or later); no network, no GitHub call.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
guard="$root/.claude/hooks/guard.py"
owner_token="GH_TOKEN=\"\$(gh auth token --user njabulozmnisi)\" "
repo=njabulozmnisi/disputedesk
sha=0123456789abcdef0123456789abcdef01234567 # a 40-character SHA
stacked='push --force-with-lease --force-if-includes origin' # the stacked force form, before its branch
failures=()
runs=0

# decision <agent_type or empty> <command>: prints the guard's decision, or "error" when the guard fails.
decision() {
  local event output
  event=$(jq -cn --arg agent "$1" --arg command "$2" \
    '{tool_name: "Bash", tool_input: {command: $command}} + (if $agent == "" then {} else {agent_type: $agent} end)')
  output=$(python3 -I "$guard" <<< "$event") || { echo error; return; }
  if [[ -z "$output" ]]; then echo none; return; fi
  jq -r '.hookSpecificOutput.permissionDecision' <<< "$output"
}

# run_case <main session> <subagent> <command>
run_case() {
  local main subagent shown=${3//$'\n'/\\n} # a newline in the command is shown as \n, so each run is one line
  main=$(decision "" "$3")
  subagent=$(decision backend-builder "$3")
  runs=$((runs + 1))
  if [[ "$main" == "$1" && "$subagent" == "$2" ]]; then
    echo "ok    $main / $subagent: $shown"
  else
    echo "FAIL  $main / $subagent, want $1 / $2: $shown"
    failures+=("$shown")
  fi
}

# check <main session> <subagent> <command>: a `gh` command is checked bare and with the owner-token prefix.
check() {
  run_case "$@"
  if [[ "$3" == "gh "* ]]; then run_case "$1" "$2" "$owner_token$3"; fi
}

# --- no decision in the main session, denied to a subagent (criteria 4 and 5) ---
check none deny "git $stacked feature/9.4"
check none deny "git -C /abs/path $stacked feature/9.4"
check none deny "git -C /private/tmp/claude-501/-Users-x/scratchpad/wt.9_4 $stacked feature/9.4b"
check none deny "git push origin feature/9.4"
check none deny "git push origin feature/records-9.3"
check none deny "git commit -m x"
check none deny "git merge --ff-only origin/main"
check none deny "gh pr merge 5 --squash --auto --match-head-commit $sha"
check none deny "gh pr merge 5 --squash --auto"
check none deny "gh pr merge 5 --disable-auto"
check none deny "gh pr -R $repo merge 5 --squash"
check none deny "gh pr create --head feature/9.4 --base main --title t --body b"
check none deny "gh pr edit 5 --base main"
check none deny "gh pr update-branch 5"
check none deny "gh pr close 5"
check none deny "gh pr reopen 5"
check none deny "gh pr ready 5"
check none deny "gh api -XPOST repos/$repo/rulesets"
check none deny "gh api --method=PATCH repos/$repo"
check none deny "gh api -X patch repos/$repo"
check none deny "gh api -X=PUT repos/$repo/topics"
check none deny "gh api --method DELETE repos/$repo/rulesets/1"
check none deny "gh api repos/$repo/rulesets --input main.json"
check none deny "gh api -X DELETE repos/$repo/rulesets/1"
check none deny "gh api repos/$repo/issues -f title=x"
check none deny "gh api repos/$repo/issues -F count=1"
check none deny "gh api repos/$repo/issues --raw-field title=x"
check none deny "gh api repos/$repo/issues --field title=x"
check none deny "gh repo edit --enable-merge-commit"
check none deny "gh repo delete $repo --yes"
check none deny "gh repo rename other"
check none deny "gh repo archive"
check none deny "gh repo unarchive"
check none deny "gh repo sync"
check none deny "gh repo deploy-key add key.pub --allow-write"
check none deny "gh repo deploy-key delete 12"
check none deny "gh secret set FORBIDDEN_TERMS --body x"
check none deny "gh secret delete FORBIDDEN_TERMS"
check none deny "gh variable set OTHER_VARIABLE --body x"
check none deny "gh alias set mm 'pr merge'"
check none deny "gh alias import aliases.yml"
check none deny "gh workflow disable Security"
check none deny "gh workflow enable Security"
check none deny "gh workflow run ci.yml"
# a backslash-newline pair is a line continuation, which bash removes before it reads words (review H1)
check none deny "gh api \\"$'\n'"  -X PUT repos/$repo/rulesets/1 \\"$'\n'"  --input main.json"
check none deny "gh pr \\"$'\n'"  merge 5 --squash"
check none deny "git \\"$'\n'"  push origin feature/9.4"
check none deny "git push --force-with-lease \\"$'\n'"--force-if-includes origin feature/9.4" # joined: the stacked form
# pushes that look like a force and are not (review M1)
check none deny "git push -4 origin feature/9.4"
check none deny "git push -u origin feature/fix-9.4f"
check none deny "git push --follow-tags origin feature/9.4"
check none deny "git push --no-force-with-lease origin feature/9.4"
check none deny "git push --dry-run origin feature/9.4"
check none deny "git push origin feature/9.4+x"
check none deny "git push origin \"feature/9.4\""
check none deny "git push \"origin\" 'feature/9.4'"
# gh api: gh keeps the last method, and a method may sit in a short-option cluster (review M2, verify finding 2)
check none deny "gh api -X GET -X DELETE repos/$repo/rulesets/1"
check none deny "gh api -X GET -X POST repos/$repo/issues -f title=x"
check none deny "gh api -XPOST repos/$repo/issues"
check none deny "gh api -iXPOST repos/$repo/issues"
check none deny "gh api -iX POST repos/$repo/issues"
check none deny "gh api --method=post repos/$repo/issues"
check none deny "gh api --method GET --method PATCH repos/$repo"
check none deny "gh api -X get -X Put repos/$repo/topics"
check none deny "gh api -X \"DELETE\" repos/$repo/rulesets/1"
check none deny "gh api -X \"\$METHOD\" repos/$repo/rulesets/1" # a method the guard cannot read is not GET
check none deny "gh api repos/$repo/issues -ftitle=x"
# gh options before the group, or between the group and its subcommand (review M3)
check none deny "gh -R $repo pr merge 5 --squash"
check none deny "gh --repo $repo pr create --head feature/9.4 --base main"
check none deny "gh --repo=$repo pr merge 5"
check none deny "gh -R$repo pr close 5"
check none deny "gh -R $repo secret set X --body y"
check none deny "gh -R $repo repo deploy-key add k.pub"
check none deny "gh pr --repo $repo ready 5"
# the closed list reads the subcommand by position, not any later word (verify finding 3)
check none deny "gh pr merge 5"
check none deny "gh pr close 5 --comment list"
# a write to a variable other than DEPLOY_ENABLED is on the closed list only (review M4)
check none deny "gh api -X PATCH repos/$repo/actions/variables/OTHER_VARIABLE -f value=x"
# the two known substitutions are taken out first, so the rest of the command is still read (rule diff v3)
check none deny "${owner_token}gh pr merge 5 --squash"
check none deny "${owner_token}gh api repos/$repo/rulesets/1 -X PUT --input main.json" # review S10
check none deny "cd \"\$(git rev-parse --show-toplevel)\" && git push origin feature/9.4"
# a ) glued to a word still ends it; a ( in a jq value does not hide the call (rule diff v3)
check none deny "(cd /tmp && gh pr merge)"
check none deny "gh api repos/$repo/rulesets/1 --jq '(.id)' -X PUT --input main.json"
# gh's own aliases for the closed list (gh help <command>, ALIASES; review R1-M1)
check none deny "gh pr new --head feature/9.4 --base main --title t --body-file b.md"
check none deny "gh secret remove AWS_ROLE_ARN"
check none deny "gh variable remove OTHER"
# an escaped backslash before a newline joins nothing: the second line is read (review R1-L1)
check none deny "echo x\\\\"$'\n'"gh pr merge 5"
check none deny "echo x\\\\\\\\"$'\n'"git push origin feature/9.4"
# the flow's own steps (criteria 2 and 3)
check none deny "git $stacked feature/9.5"
check none deny "git -C /private/tmp/wt $stacked feature/9.5"
check none deny "${owner_token}gh pr create --head feature/9.4 --base main --title \"9.4 stories merge through their pull request\" --body-file /tmp/body.md"
check none deny "gh pr create --head feature/9.4 --base main --title t --body-file /tmp/body.md"
check none deny "git commit -F /tmp/msg.txt"
# Under the human decision of 2026-11-05 (rule diff v4) the guard reads each simple command as the words bash passes
# on (Python's shlex). These four were deny / deny under v3: bash passes git the stacked form's own words, or a word
# that keeps a literal backslash, which is no force option or + refspec.
check none deny "git  $stacked feature/9.4" # two spaces: the stacked form's words
check none deny "git $stacked \"feature/9.4\"" # a quoted branch: the stacked form's words
check none deny "git push origin \\\\+feature/9.4" # bash passes \+feature/9.4
check none deny "git push \\\\\\"$'\n'"--force origin feature/9.4" # bash passes \--force, as the repository
# Under the same decision a subagent is denied every command substitution but the two exemptions; these two were
# none / none under v3.
check none deny "echo \$(date)" # X1
check none deny "echo \`date\` && ls \"\$(pwd)/docs\""
# guard-cases-v4: quoted separators, nested quotes, subshells, pipes and lists, wrappers, redirections, comments and
# substitutions (rule diff v4). Its C5, F1 to F6, F8 to F10 and X6 are cases this test already has.
check none deny "git -c 'test.note=a;b' push origin feature/9.4" # V1c
check none deny "git -c 'a;b' push origin feature/9.4" # V2
check none deny "git -c \"a|b\" push origin feature/9.4" # V3c
check none deny "gh api --jq '.[] | .id' -X DELETE repos/$repo/rulesets/1" # V4
check none deny "gh api --jq '.a;.b' --method PUT repos/$repo/rulesets/1 --input main.json" # V5
check none deny "git commit -m \"it's \\\"quoted\\\"\"" # N1
check none deny "gh pr create --head feature/9.4 --base main --title \"a 'b' c\" --body-file b.md" # N2
check none deny "git push \"'--force'\" origin feature/9.4" # N3: the word keeps its quotes, a refspec to git
check none deny "gh pr merge 5 --squash --subject \"a \\\"b\\\" ; c\"" # N5
check none deny "( cd /tmp && gh pr merge 5 )" # S2
check none deny "cat x |& gh pr merge 5" # P4
check none deny "gh pr view 5 || gh pr close 5" # P6
check none deny "gh pr view 5 &"$'\n'"gh pr merge 5" # P7
check none deny "timeout 60 gh pr merge 5 --squash" # W2
check none deny "ssh m1 'cd /x && gh pr merge 5'" # W4
check none deny "git push origin feature/9.4 2>&1" # R1
check none deny "true # note\\"$'\n'"git push origin feature/9.4" # C3
check none deny "git push origin feature/9.4 # done" # C4
check none deny "ls \"\$(pwd)/docs\"" # X2
check none deny "diff <(cat a) <(cat b)" # X3
check none deny "${owner_token}gh pr merge 5 --squash --auto --match-head-commit $sha" # X7
check none deny "gh pr -R 'https://github.com/o/r?x=)y' merge 5 --squash" # X10
check none deny "gh pr create --head feature/9.4 --base main --title t --body 'Adds \`guard.test.sh\`.'" # X11
# guard-cases-v5: every word named git or gh is read as a possible program, git's --attr-source takes a value, and a
# redirection and its target are dropped before the words are read (POSIX Shell Command Language 2.7) (rule diff v5,
# review R3-M1 and R3-L1)
check none deny "env -u git gh pr merge 5 --squash" # E1
check none deny "env -u gh git commit -m x" # E3
check none deny "git --attr-source HEAD commit -m x" # G2
check none deny "gh pr > /tmp/out merge 5" # D7
check none deny "gh pr 2>/dev/null merge 5" # D8
check none deny "git 2>/dev/null commit -m x" # D10
check none deny "> /tmp/out gh pr merge 5" # D11
check none deny "gh pr >| /tmp/out merge 5 --squash" # D1: >| is a redirection, not a pipe
# a shell's -c text is still read (rule diff v4), $SHELL included (verify round 3)
check none deny "\$SHELL -c 'git push origin main'"
check none deny "\${SHELL} -c 'gh pr merge 5'"
# a commit message is one word, so a force push named in it is not read as one (rule diff v4, kept)
check none deny "git commit -m 'Explains why git push --force origin main is refused'"

# --- denied to both: every other force push (criterion 4) ---
check deny deny "git push --force-with-lease"
check deny deny "git push --force-with-lease origin feature/9.4"
check deny deny "git push --force-if-includes origin feature/9.4"
check deny deny "git push --force origin feature/9.4"
check deny deny "git push -f origin feature/9.4"
check deny deny "git push -fu origin feature/9.4"
check deny deny "git push -uf origin feature/9.4"
check deny deny "git push --mirror origin"
check deny deny "git push origin +feature/9.4"
check deny deny "git push origin '+feature/9.4'"
check deny deny "git $stacked feature/9.4:main"
check deny deny "git $stacked HEAD:feature/9.5"
check deny deny "git $stacked feature/9.4 feature/9.5"
check deny deny "git $stacked feature/9.4xy"
check deny deny "git $stacked feature/records-9.3"
check deny deny "git $stacked main"
check deny deny "git -C /tmp/\$(id) $stacked feature/9.4"
check deny deny "git -C relative/path $stacked feature/9.4"
check deny deny "git $stacked feature/9.4; git push -f origin feature/9.5"
check deny deny "git $stacked feature/9.4 &"
check deny deny "git $stacked feature/9.4 || true"
check deny deny "git $stacked feature/9.4"$'\n'"echo second"
check deny deny "git $stacked feature/9.4"$'\n'
check deny deny "git push --force-with-lease"$'\n'"--force-if-includes origin feature/9.4"
check deny deny "git push origin feature/9.4 && git push -f origin feature/9.5"
check deny deny "git -C /abs/dir push --force origin feature/9.4"
check deny deny "git -c x=y push --force origin feature/9.4"
check deny deny "git --git-dir=/abs/dir/.git push --force origin feature/9.4"
# line continuations (review H1)
check deny deny "git push \\"$'\n'"  --force origin feature/9.4"
check deny deny "git \\"$'\n'"  push -f origin feature/9.4"
check deny deny "git $stacked \\"$'\n'"feature/9.4"$'\n'"echo second"
check deny deny "git $stacked feature/9.4 \\"$'\n'"  feature/9.5"
# short-option clusters, abbreviations of --mirror and --force*, escaped or quoted + refspecs (review M1)
check deny deny "git push -4f origin feature/9.4"
check deny deny "git push -6f origin feature/9.4"
check deny deny "git push -vqf origin feature/9.4"
check deny deny "git push --mirr origin"
check deny deny "git push --m origin"
check deny deny "git push --force-w origin feature/9.4"
check deny deny "git push --force-i origin feature/9.4"
check deny deny "git push origin \\+feature/9.4"
check deny deny "git push origin \"\"+feature/9.4"
check deny deny "git push origin \"+feature/9.4\""
check deny deny "git push origin '+'feature/9.4"
# quoted or escaped force options (verify finding 1)
check deny deny "git push origin \"--force\" feature/9.4"
check deny deny "git push origin '--force' feature/9.4"
check deny deny "git push origin --\"force\" feature/9.4"
check deny deny "git push '-f' origin feature/9.4"
check deny deny "git push \"--force-with-lease\" origin feature/9.4"
check deny deny "git push \"--mirror\" origin"
check deny deny "git push \\-\\-force origin feature/9.4"
check deny deny "git \"push\" --force origin feature/9.4"
# only an odd run of backslashes before a newline joins the lines (review R1-L1)
check deny deny "echo x\\\\"$'\n'"git push --force origin feature/9.4"
check deny deny "git push \\"$'\n'"--force origin feature/9.4"
# guard-cases-v4 (rule diff v4)
check deny deny "git -c 'test.note=a;b' push --force origin feature/9.4" # V1
check deny deny "git -c \"a|b\" push -f origin feature/9.4" # V3
check deny deny "git push origin \"fe\"'at'ure/9.4 \"--\"'force'" # N6
check deny deny "git push --force origin 'feature/9.4" # B4: unreadable, the rough reading still sees the force
check deny deny "(git push --force origin feature/9.4)" # S1
check deny deny "{ git push -f origin feature/9.4; }" # S3
check deny deny "if true; then git push origin +feature/9.4; fi" # S5
check deny deny "(cd /tmp; (git push --mirror origin))" # S6
check deny deny "echo y | git push --force origin feature/9.4" # P1
check deny deny "env GIT_TRACE=1 git push --force origin feature/9.4" # W1
check deny deny "bash -c 'git push --force origin feature/9.4'" # W3
check deny deny "GIT_DIR=/x/.git /usr/bin/git push -f origin feature/9.4" # W7
check deny deny "git $stacked feature/9.4 2>&1" # R2: the stacked form plus a redirection is not the whole command
check deny deny "git push origin feature/9.4 2>&1 -f" # R4
check deny deny "true # note\\"$'\n'"git push --force origin feature/9.4" # C2: the comment ends at the newline
check deny deny "gh pr view 5 <(git push --force origin feature/9.4)" # X5
check deny deny "git -C /\$(git rev-parse --show-toplevel) $stacked feature/9.5" # X8: read before the placeholder
check deny deny "git -C \"\$(git rev-parse --show-toplevel)\" $stacked feature/9.5" # X9
check deny deny "git push \$'--force' origin feature/9.4" # Q1: ANSI-C quoting, the rough reading sees --force
# guard-cases-v5 (rule diff v5, review R3-M1)
check deny deny "env -u gh git push --force origin feature/9.4" # E2
check deny deny "sudo -u git git push -f origin feature/9.4" # E4
check deny deny "xargs -a git git push -f origin feature/9.4" # E9
check deny deny "git --attr-source HEAD push --force origin feature/9.4" # G1
check deny deny "git > /dev/null push --force origin feature/9.4" # D9
check deny deny "git push >| /tmp/out --force origin feature/9.4" # D2
# Text fed to a shell fails closed, and every word of the command is read again, so a force push in the text is still
# denied (rule diff v5, review R3-H1, verify round 3)
check deny deny "echo 'git push --force origin feature/9.4' | sh" # Y1
check deny deny ". /dev/stdin <<< 'git push -f origin feature/9.4'" # Y6
check deny deny "bash <<< 'git push -f origin feature/9.4'" # Y4
check deny deny "bash <<'EOF'"$'\n'"git push --force origin feature/9.4"$'\n'"EOF" # Y5
check deny deny "echo 'git push --force origin main' | sh"
check deny deny "echo 'git push --force origin main' | bash"
check deny deny "echo \"git push -f origin main\" | bash -s"
check deny deny "\$SHELL -c 'git push --force origin main'"
check deny deny ". /dev/stdin <<< 'git push --force origin main'"
check deny deny "echo 'git push -f origin feature/9.4' | source /dev/stdin"

# --- ask in the main session, denied to a subagent: actions on a deployed environment (criterion 5) ---
check ask deny "gh workflow run deploy.yml"
check ask deny "gh workflow run -R $repo deploy.yml"
check ask deny "gh workflow -R $repo run deploy.yml -f sha=abc"
check ask deny "gh workflow run 'Deploy (dev)'"
check ask deny "gh api -X POST repos/$repo/actions/workflows/deploy.yml/dispatches -f ref=main"
check ask deny "gh variable set DEPLOY_ENABLED --body true"
check ask deny "gh variable delete DEPLOY_ENABLED"
check ask deny "gh variable -R $repo set DEPLOY_ENABLED --body false"
# DEPLOY_ENABLED in any case (GitHub variable names are case-insensitive), after -R, and through the REST API (review M4)
check ask deny "gh variable set deploy_enabled --body true"
check ask deny "gh variable set Deploy_Enabled --body true"
check ask deny "gh variable delete deploy_enabled"
check ask deny "gh -R $repo variable set DEPLOY_ENABLED --body true"
check ask deny "gh api -X PATCH repos/$repo/actions/variables/DEPLOY_ENABLED -f value=true"
check ask deny "gh api repos/$repo/actions/variables -f name=deploy_enabled -f value=true"
check ask deny "gh api --method DELETE repos/$repo/actions/variables/deploy_enabled"
check ask deny "${owner_token}gh api -X PATCH repos/$repo/actions/variables/DEPLOY_ENABLED -f value=false"
check ask deny "terraform apply"
check ask deny "aws sts get-caller-identity"
# gh variable remove is gh variable delete's alias (review R1-M1)
check ask deny "gh variable remove DEPLOY_ENABLED"
check ask deny "gh variable remove -R $repo deploy_enabled"
check ask deny "${owner_token}gh variable set DEPLOY_ENABLED --body true"
# guard-cases-v4 (rule diff v4)
check ask deny "gh api --jq '.[] | .id' -X PATCH repos/$repo/actions/variables/DEPLOY_ENABLED -f value=true" # V6
check ask deny "gh pr view 5 && gh variable set DEPLOY_ENABLED --body true" # P5
check ask deny "eval \"gh variable set DEPLOY_ENABLED --body true\"" # W6

# --- ask in the main session, denied to a subagent: a substitution, or a command the guard cannot read, naming git
# or gh ---
# A substitution can hide the words the rules read, so it fails closed; only the owner-token prefix and
# $(git rev-parse --show-toplevel) are known (rule diff v3, review R1-H1). The reviewer's S1 to S4, S8, S9 and S11
# wanted no decision in the main session; under the human decision of 2026-11-05 (rule diff v3) they ask.
check ask deny "gh api \"repos/\$(gh repo view --json nameWithOwner -q .nameWithOwner)/rulesets/1\" -X PUT --input main.json" # S1
check ask deny "gh api repos/$repo/rulesets/\`cat id.txt\` -X DELETE" # S2
check ask deny "gh -R \"\$(cat repo.txt)\" pr merge 5 --squash" # S3
check ask deny "gh pr --repo \"\$(gh repo view --json nameWithOwner -q .nameWithOwner)\" merge 5 --squash" # S4
check ask deny "gh api -X PUT \"repos/\$(gh repo view --json nameWithOwner -q .nameWithOwner)/rulesets/1\" --input main.json" # S8
check ask deny "gh pr merge \"\$(gh pr list --head feature/9.4 --json number -q '.[0].number')\" --squash" # S9
check ask deny "gh api \"repos/\$(gh repo view --json nameWithOwner -q .nameWithOwner)/rulesets\"" # S11
# the deploy asks are no longer lost behind a substitution (S5 to S7)
check ask deny "gh variable set --repo \"\$(gh repo view --json nameWithOwner -q .nameWithOwner)\" DEPLOY_ENABLED --body true" # S5
check ask deny "gh api \"repos/\$(gh repo view --json nameWithOwner -q .nameWithOwner)/actions/variables/DEPLOY_ENABLED\" -X PATCH -f value=true" # S6
check ask deny "gh workflow run --repo \"\$(cat repo.txt)\" deploy.yml" # S7
check ask deny "git push origin \"\$(git branch --show-current)\""
check ask deny "echo \$(git rev-parse HEAD)" # a read inside a substitution fails closed too
check ask deny "gh pr view \"\$(cat n.txt)\""
check ask deny "echo \$(echo \$(gh auth token --user njabulozmnisi); gh pr merge 5)" # a known form nested in another
check ask deny "x=\$(g\"h\" pr merge 5)" # gh hidden by quotes: the rule reads the unquoted text
check ask deny "GH_TOKEN=\"\$(gh auth token)\" gh pr merge 5 --squash" # not the known form: no --user
check ask deny "GH_TOKEN=\"\$(gh auth token --user njabulozmnisi; gh pr merge 5)\" gh pr view 5"
check ask deny "cd \"\$(git rev-parse --show-toplevel)\" && echo \$(git log -1)" # a known one beside an unknown one
# Under the human decision of 2026-11-05 (rule diff v4) a process substitution is a substitution; none / deny under v3.
check ask deny "gh api repos/$repo/rulesets/1 --input <(cat main.json) -X PUT"
# guard-cases-v4: substitutions, and forms shlex does not model (rule diff v4)
check ask deny "gh pr view 5 <(gh pr merge 5 --squash)" # X4
check ask deny "gh pr create --head feature/9.4 --base main --title t --body \"Adds \`guard.test.sh\`.\"" # X13
check ask deny "git log -n \$((2+3)) --oneline" # X14: arithmetic expansion
check ask deny "git commit -m \"unterminated" # B1: an unbalanced quote
check ask deny "gh pr view 5 'x" # B3
check ask deny "git push {--force,} origin feature/9.4" # Q2: brace expansion
check ask deny "find . -name x -exec git rm {} \\;" # Q4: an escaped separator
check ask deny "git push origin ';' --force" # Q5: a quoted separator
# guard-cases-v5: a shell that reads its commands from standard input (a pipe, a here-string, a heredoc, -s, or no
# operand after its options), and `.` or `source` of /dev/stdin or a process substitution, cannot be read (POSIX sh;
# bash manual, Invoking Bash, Pipelines, Here Strings, Bourne Shell Builtins) (rule diff v5, review R3-H1)
check ask deny "echo 'git push origin feature/9.4' | bash" # Y2
check ask deny "echo 'gh pr merge 5 --squash' | bash -s" # Y3
check ask deny "echo 'git push origin main' | sh"
check ask deny ". /dev/stdin <<< 'git push origin main'"
check ask deny "echo 'gh pr merge 5' | zsh"
check ask deny "echo 'gh pr merge 5' | dash"
check ask deny "echo 'gh pr merge 5' | ksh"
check ask deny "echo 'gh pr merge 5' | \$SHELL"
check ask deny "echo 'gh pr merge 5' | \${SHELL}"
check ask deny "echo 'gh pr merge 5' | /bin/sh -x"
check ask deny "echo 'gh pr merge 5' | bash -o pipefail" # -o takes the next word as its value
check ask deny "bash -s -- x <<< 'gh pr merge 5'" # -s reads standard input even with operands
check ask deny "source /dev/stdin <<< 'gh pr merge 5'"
check ask deny "echo 'gh pr merge 5' | . /dev/fd/0"
check ask deny "command . /dev/stdin <<< 'gh pr merge 5'"
check ask deny "source <(echo 'gh pr merge 5')"

# --- no decision for either: reads and local steps (criterion 5) ---
check none none "gh api repos/$repo/rulesets/1"
check none none "gh api -X GET search/issues -f q=x"
check none none "gh api --method get repos/$repo"
check none none "gh pr view 5 --json state"
check none none "gh pr view 5 --json baseRefName,mergeStateStatus,headRefOid"
check none none "gh pr checks 5 --watch --required"
check none none "gh pr list"
check none none "gh run list --branch main --commit $sha --workflow CI"
check none none "gh run watch 123 --exit-status"
check none none "gh variable list"
check none none "gh help pr merge"
check none none "git rebase --onto origin/main $sha feature/9.4"
check none none "git apply --binary /abs/9.4-rule-diff.patch"
check none none "git fetch origin"
# a read across a continuation stays a read, and a plain newline still separates two reads (review H1)
check none none "gh pr \\"$'\n'"  view 5 --json state"
check none none "gh pr view 5"$'\n'"gh pr list"
# fields with every method GET are query parameters; -i alone is no method (review M2)
check none none "gh api --method=get search/issues -F per_page=1"
check none none "gh api -X GET -X get repos/$repo -f x=1"
check none none "gh api -i repos/$repo/pulls/5"
check none none "gh api --paginate repos/$repo/pulls"
# reads after gh options; gh takes the word after `--state` as its value (review M3)
check none none "gh -R $repo pr view 5"
check none none "gh --repo $repo pr list --search merge"
check none none "gh -R $repo run list"
check none none "gh pr --state closed list"
# reads of DEPLOY_ENABLED (review M4)
check none none "gh api repos/$repo/actions/variables/DEPLOY_ENABLED"
check none none "gh api -X GET repos/$repo/actions/variables/deploy_enabled"
check none none "gh variable get DEPLOY_ENABLED"
# reads whose arguments are write words (verify finding 3, review L4)
check none none "gh pr list --search ready"
check none none "gh pr list --search close"
check none none "gh pr list --label edit --search merge"
check none none "gh pr view 5 --json title -q .title"
check none none "gh workflow view deploy.yml"
check none none "gh run list --workflow deploy"
check none none "gh repo deploy-key list"
# the two known substitutions (rule diff v3), and a $( in single quotes, which bash reads literally
check none none "git status && gh pr view 5"
check none none "${owner_token}gh pr view 5 --json state,mergeCommit"
check none none "scripts/test-lock.sh run testcontainers 9.4 -- dotnet test --solution DisputeDesk.slnx --report-xunit-trx --results-directory \"\$(git rev-parse --show-toplevel)/TestResults\""
check none none "echo '\$(date)' && git status" # X12: ask / deny under v3 (human decision of 2026-11-05, rule diff v4)
# reads through gh's aliases (gh help <command>, ALIASES)
check none none "gh pr ls --state open"
check none none "gh secret ls && gh variable ls && gh variable get DEPLOY_ENABLED"
check none none "gh workflow ls && gh alias ls"
check none none "gh run watch 123456789 --exit-status"
# guard-cases-v4 (rule diff v4)
check none none "git -c 'a;b' status" # V2c
check none none "gh api --jq '.[] | .id' repos/$repo/rulesets" # V4c
check none none "gh api --jq '.a;.b' --method GET repos/$repo" # V5c
check none none "echo \"a 'b; git push --force origin feature/9.4' c\"" # N4: one double-quoted word
check none none "echo 'it" # B2: unreadable, but names neither git nor gh
check none none "(gh pr view 5)" # S4
check none none "gh pr list | grep merge" # P2
check none none "git log --oneline | head -1" # P3
check none none "sh -c \"echo hi\"" # W5
check none none "git status >/tmp/x 2>&1 && gh pr view 5" # R3
check none none "git status # push --force" # C1
check none none "ls /x/.git/hooks" # X15
check none none "gh pr view 5 --json number,title --jq '{number,title}'" # Q3
check none none "gh pr view 5 --json state,mergeCommit" # F7
# guard-cases-v5: only a shell's standard input fails closed, a redirection target is not read as a word, and the
# false denies v4 removed stay removed (rule diff v5)
check none none "git log --grep merge"
check none none "git log --grep \"merge\" --oneline"
check none none "echo 'git push --force origin main' | grep force"
check none none "cat <<< 'git push --force origin main'"
check none none "git status 2>/dev/null"
check none none "gh pr view 5 --json state > /tmp/pr.json 2>&1"
check none none "git log --oneline > /tmp/log.txt && gh pr view 5"
check none none "bash scripts/guard.test.sh"
check none none "sh -c 'git status'"

if ((${#failures[@]} > 0)); then
  echo "GUARD TEST FAIL: ${#failures[@]} of $runs runs differ:" >&2
  printf '  %s\n' "${failures[@]}" >&2
  exit 1
fi
echo "guard: all $runs runs as expected"
