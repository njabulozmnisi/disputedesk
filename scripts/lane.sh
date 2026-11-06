#!/usr/bin/env bash
# Runs one headless builder lane on a worker machine over SSH and brings its result back, so the supervisor dispatches
# a story with one command instead of hand-written remote scripts.
#   scripts/lane.sh start <host> <story> <prompt-file> [base-ref]
#                                                                 copy the prompt and scripts/lane-runner.zsh, which
#                                                                 starts `claude -p --agent $LANE_AGENT` in a tmux
#                                                                 session (new worktree, or the existing one). Refuses
#                                                                 when the host has under 15 GB free. With base-ref
#                                                                 (for example origin/feature/7.5) the new worktree starts
#                                                                 there, so a lane can stack on an unmerged story; it
#                                                                 exits 2 when that worktree or branch already exists.
#   scripts/lane.sh wait <host> <story>                           wait for the lane's exit file; print exit code and status
#   scripts/lane.sh collect <host> <story> <base> <out-dir>       copy the report, a verified patch of the worktree, and
#                                                                 the worktree's TestResults/ and
#                                                                 docs/verification/<story>-build.json when present
#   LANE_AGENT  the builder agent to run (default backend-builder; frontend-builder for frontend stories)
# Lane files live in ~/disputedesk-lanes on the host; the session is lane-<story> with dots as dashes (tmux reads a dot
# as a pane separator). Never attach to a lane without -r (read-only): Ctrl-C there kills the builder.
# The lane's JSON output is copied back and read with jq here, so the host needs no jq.
# Exit codes: 0 done; 1 the lane failed or reported blocked, or start refused for disk; 2 usage or tooling.
set -euo pipefail

# shellcheck disable=SC2088 # the remote shell expands the ~, not this one
repo='~/Projects/Dojo/disputedesk'
# shellcheck disable=SC2088 # the remote shell expands the ~, not this one
lanes='~/disputedesk-lanes'
min_free_kb=$((15 * 1024 * 1024)) # 15 GB (AGENTS.md, Machine hygiene)

remote() { ssh -o ConnectTimeout=10 "$1" "zsh -lc $(printf '%q' "$2")"; }

command=${1:?command}; host=${2:?host}; story=${3:?story}
name="lane-${story//./-}"

case "$command" in
  start)
    prompt=${4:?prompt file}; base=${5:-}
    if [[ -n "$base" && ! "$base" =~ ^[A-Za-z0-9._/-]+$ ]]; then
      echo "lane.sh: base-ref must be a ref name, got '$base'" >&2; exit 2
    fi
    # A stacked lane needs a new worktree on its base; an existing one would be resumed on whatever it holds.
    if [[ -n "$base" ]] && remote "$host" "cd $repo && { test -e .claude/worktrees/$story || git show-ref --verify --quiet refs/heads/worktree-$story; }"; then
      echo "lane.sh: the worktree .claude/worktrees/$story or branch worktree-$story already exists on $host; remove it or start without base-ref" >&2
      exit 2
    fi
    # df -P prints one line per file system in fixed columns; the fourth is the space available, in 1024-byte blocks.
    free_kb=$(remote "$host" "df -Pk ~ | awk 'NR == 2 { print \$4 }'") || { echo "lane.sh: could not reach $host" >&2; exit 2; }
    [[ "$free_kb" =~ ^[0-9]+$ ]] || { echo "lane.sh: could not read free disk on $host" >&2; exit 2; }
    if ((free_kb < min_free_kb)); then
      echo "lane.sh: refused: $host has $((free_kb / 1024 / 1024)) GB free, under 15 GB; remove done worktrees first" >&2
      exit 1
    fi
    # One copy of the runner per story, so starting a second lane never rewrites a running one.
    remote "$host" "mkdir -p $lanes; rm -f $lanes/$story.out $lanes/$story.err $lanes/$story.exit"
    scp -q "$prompt" "$host:disputedesk-lanes/$story.prompt"
    scp -q "$(dirname "$0")/lane-runner.zsh" "$host:disputedesk-lanes/$story.zsh"
    remote "$host" "chmod +x $lanes/$story.zsh; cd $repo && git pull --ff-only -q"
    # The runner resumes an existing worktree, so a stacked lane's worktree is made here first, on the branch name
    # claude -w would give it.
    if [[ -n "$base" ]]; then
      remote "$host" "cd $repo && git worktree add -q -b worktree-$story .claude/worktrees/$story $base"
    fi
    remote "$host" "tmux new -d -s $name $lanes/$story.zsh $story ${LANE_AGENT:-backend-builder}"
    echo "lane.sh: started $name on $host"
    ;;
  wait)
    until remote "$host" "test -f $lanes/$story.exit" 2>/dev/null; do sleep 60; done
    code=$(remote "$host" "cat $lanes/$story.exit")
    output=$(mktemp)
    # A missing file leaves the copy empty; jq -e fails on that and on malformed JSON, which both read as no-result.
    scp -q "$host:disputedesk-lanes/$story.out" "$output" || true
    status=$(jq -er '"\(.subtype) \(.terminal_reason)"' "$output" 2>/dev/null) || status=no-result
    rm -f "$output"
    echo "lane.sh: $name exit $code, $status"
    [[ "$code" == 0 && "$status" == success* ]] || exit 1
    ;;
  collect)
    base=${4:?base}; out=${5:?out dir}
    mkdir -p "$out"
    remote "$host" "cd $repo && scripts/story-patch.sh .claude/worktrees/$story $base $lanes/$story.patch"
    scp -q "$host:disputedesk-lanes/$story.patch" "$out/$story.patch"
    output=$(mktemp)
    scp -q "$host:disputedesk-lanes/$story.out" "$output" || { rm -f "$output"; exit 2; }
    # jq -e exits non-zero on a missing or null result, so no empty report is written.
    report=$(jq -er '.result' "$output") || { echo "lane.sh: $story.out holds no result" >&2; rm -f "$output"; exit 1; }
    rm -f "$output"
    printf '%s\n' "$report" > "$out/$story-report.md"
    # Copied on their own: the patch leaves out TestResults/ (ignored), and fix builders apply it without docs/.
    worktree="Projects/Dojo/disputedesk/.claude/worktrees/$story"
    if remote "$host" "test -d ~/$worktree/TestResults"; then
      scp -rq "$host:$worktree/TestResults" "$out/$story-TestResults"
      echo "lane.sh: $out/$story-TestResults"
    fi
    if remote "$host" "test -f ~/$worktree/docs/verification/$story-build.json"; then
      scp -q "$host:$worktree/docs/verification/$story-build.json" "$out/$story-build.json"
      echo "lane.sh: $out/$story-build.json"
    fi
    echo "lane.sh: $out/$story.patch and $out/$story-report.md"
    ;;
  *)
    echo "lane.sh: command must be start, wait or collect" >&2; exit 2 ;;
esac
