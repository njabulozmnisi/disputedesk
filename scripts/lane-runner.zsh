#!/bin/zsh -l
# One headless builder lane on the worker host. scripts/lane.sh start copies this file there and runs it in a tmux
# session:   lane-runner.zsh <story> <agent>
# An existing worktree is resumed in place; otherwise claude -w creates it from the host's HEAD. claude's JSON output,
# its errors and its exit code go to ~/disputedesk-lanes/<story>.out, .err and .exit.
story=$1
agent=$2
lanes=~/disputedesk-lanes
worktree=~/Projects/Dojo/disputedesk/.claude/worktrees/$story
if [ -d "$worktree" ]; then cd "$worktree"; flag=(); else cd ~/Projects/Dojo/disputedesk; flag=(-w $story); fi
claude -p --agent $agent "${flag[@]}" --permission-mode dontAsk --output-format json "$(cat $lanes/$story.prompt)" > $lanes/$story.out 2> $lanes/$story.err
echo $? > $lanes/$story.exit
