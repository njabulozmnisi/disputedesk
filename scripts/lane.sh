#!/usr/bin/env bash
# Runs one headless builder lane on a worker machine over SSH and brings its result back, so the supervisor dispatches
# a story with one command instead of hand-written remote scripts.
#   scripts/lane.sh start <host> <story> <prompt-file> [<base>]  copy the prompt, start `claude -p --agent backend-builder`
#                                                                 in a tmux session (new worktree, or the existing one)
#   scripts/lane.sh wait <host> <story>                           wait for the lane's exit file; print exit code and status
#   scripts/lane.sh collect <host> <story> <base> <out-dir>       copy the report and a verified patch of the worktree
# Lane files live in ~/disputedesk-lanes on the host; the session is lane-<story> with dots as dashes (tmux reads a dot
# as a pane separator). Never attach to a lane without -r (read-only): Ctrl-C there kills the builder.
# Exit codes: 0 done; 1 the lane failed or reported blocked; 2 usage or tooling.
set -euo pipefail

repo='~/Projects/Dojo/disputedesk'
lanes='~/disputedesk-lanes'

remote() { ssh -o ConnectTimeout=10 "$1" "zsh -lc $(printf '%q' "$2")"; }

command=${1:?command}; host=${2:?host}; story=${3:?story}
name="lane-${story//./-}"

case "$command" in
  start)
    prompt=${4:?prompt file}
    run=$(mktemp)
    # An existing worktree is resumed in place; otherwise claude -w creates it from the host's HEAD.
    cat > "$run" <<RUN
#!/bin/zsh -l
lanes=~/disputedesk-lanes
worktree=~/Projects/Dojo/disputedesk/.claude/worktrees/$story
if [ -d "\$worktree" ]; then cd "\$worktree"; flag=(); else cd ~/Projects/Dojo/disputedesk; flag=(-w $story); fi
claude -p --agent backend-builder "\${flag[@]}" --permission-mode dontAsk --output-format json "\$(cat \$lanes/$story.prompt)" > \$lanes/$story.out 2> \$lanes/$story.err
echo \$? > \$lanes/$story.exit
RUN
    remote "$host" "mkdir -p $lanes; rm -f $lanes/$story.out $lanes/$story.err $lanes/$story.exit"
    scp -q "$prompt" "$host:disputedesk-lanes/$story.prompt"
    scp -q "$run" "$host:disputedesk-lanes/$story.sh"
    rm -f "$run"
    remote "$host" "chmod +x $lanes/$story.sh; cd $repo && git pull --ff-only -q; tmux new -d -s $name $lanes/$story.sh"
    echo "lane.sh: started $name on $host"
    ;;
  wait)
    until remote "$host" "test -f $lanes/$story.exit" 2>/dev/null; do sleep 60; done
    code=$(remote "$host" "cat $lanes/$story.exit")
    status=$(remote "$host" "python3 -c 'import json,os;d=json.load(open(os.path.expanduser(\"$lanes/$story.out\")));print(d.get(\"subtype\"),d.get(\"terminal_reason\"))'" || echo "no-result")
    echo "lane.sh: $name exit $code, $status"
    [[ "$code" == 0 && "$status" == success* ]] || exit 1
    ;;
  collect)
    base=${4:?base}; out=${5:?out dir}
    mkdir -p "$out"
    remote "$host" "cd $repo && scripts/story-patch.sh .claude/worktrees/$story $base $lanes/$story.patch"
    scp -q "$host:disputedesk-lanes/$story.patch" "$out/$story.patch"
    remote "$host" "python3 -c 'import json,os;print(json.load(open(os.path.expanduser(\"$lanes/$story.out\")))[\"result\"])'" > "$out/$story-report.md"
    echo "lane.sh: $out/$story.patch and $out/$story-report.md"
    ;;
  *)
    echo "lane.sh: command must be start, wait or collect" >&2; exit 2 ;;
esac
