#!/usr/bin/env bash
# Pushes the current branch and waits for the CI and Security workflows on the pushed commit, so main is never left red
# unnoticed. Exits non-zero unless both succeed.
# Usage: scripts/push-and-watch.sh [remote]            (default remote: origin)
#   DISPUTEDESK_WATCH_TIMEOUT  seconds to wait for both runs (default 1800)
# Exit codes: 0 both workflows succeeded; 1 a workflow failed or was cancelled; 2 timeout or tooling (push, gh).
set -euo pipefail

remote=${1:-origin}
timeout=${DISPUTEDESK_WATCH_TIMEOUT:-1800}
workflows=(CI Security)

branch=$(git rev-parse --abbrev-ref HEAD)
sha=$(git rev-parse HEAD)
repo=$(git remote get-url "$remote" | sed -E 's#(git@github.com:|https://github.com/)##; s#\.git$##')

git push "$remote" "$branch" || { echo "push-and-watch: push failed" >&2; exit 2; }
echo "push-and-watch: pushed ${sha:0:7} to $remote/$branch; waiting for ${workflows[*]} on $repo"

deadline=$((SECONDS + timeout))
while (( SECONDS < deadline )); do
  pending=0
  for workflow in "${workflows[@]}"; do
    result=$(gh run list -R "$repo" --commit "$sha" --workflow "$workflow" --json status,conclusion,url \
      -q '.[0] | "\(.status) \(.conclusion) \(.url)"' 2>/dev/null) || { echo "push-and-watch: gh failed" >&2; exit 2; }
    read -r status conclusion url <<< "${result:-missing - -}"
    if [[ "$status" != "completed" ]]; then pending=1; continue; fi
    if [[ "$conclusion" != "success" ]]; then
      echo "push-and-watch: $workflow $conclusion on ${sha:0:7}: $url" >&2
      exit 1
    fi
  done
  if (( pending == 0 )); then
    echo "push-and-watch: ${workflows[*]} succeeded on ${sha:0:7}"
    exit 0
  fi
  sleep 30
done

echo "push-and-watch: timed out after ${timeout}s waiting for ${workflows[*]} on ${sha:0:7}" >&2
exit 2
