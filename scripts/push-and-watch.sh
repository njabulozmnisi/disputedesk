#!/usr/bin/env bash
# Pushes a branch and waits for the CI and Security workflows on the pushed commit, so main is never left red
# unnoticed. Exits non-zero unless both succeed.
# Usage: scripts/push-and-watch.sh [remote] [branch]   (default remote: origin; default branch: the current one)
#   A story/* branch is pushed with --force-with-lease --force-if-includes, so it can be pushed again after a rebase
#   but never over a remote commit this clone has not integrated (git-push); every other branch, main included, is
#   pushed without force and a rejected push exits 2.
#   DISPUTEDESK_WATCH_TIMEOUT  seconds to find both workflows' runs on the pushed commit (default 1800). It does not
#                              bound a run once found: GitHub cancels a job at its timeout-minutes, which every job
#                              in ci.yml and security.yml sets, and a cancelled run exits 1.
# Exit codes: 0 both workflows succeeded; 1 a workflow failed or was cancelled; 2 timeout or tooling (push, gh).
set -euo pipefail

remote=${1:-origin}
branch=${2:-$(git rev-parse --abbrev-ref HEAD)}
timeout=${DISPUTEDESK_WATCH_TIMEOUT:-1800}
workflows=(CI Security)

sha=$(git rev-parse --verify --quiet "refs/heads/$branch") \
  || { echo "push-and-watch: no local branch $branch" >&2; exit 2; }
repo=$(git remote get-url "$remote" | sed -E 's#(git@github.com:|https://github.com/)##; s#\.git$##')

if [[ "$branch" == story/* ]]; then
  git push --force-with-lease --force-if-includes "$remote" "$branch" || { echo "push-and-watch: push failed" >&2; exit 2; }
else
  git push "$remote" "$branch" || { echo "push-and-watch: push failed" >&2; exit 2; }
fi
echo "push-and-watch: pushed ${sha:0:7} to $remote/$branch; waiting for ${workflows[*]} on $repo"

# A run appears a few seconds after the push. --branch keeps a fast-forwarded main from finding the story branch's
# earlier run on the same commit.
deadline=$((SECONDS + timeout))
run_ids=()
for workflow in "${workflows[@]}"; do
  while true; do
    id=$(gh run list -R "$repo" --branch "$branch" --commit "$sha" --workflow "$workflow" --json databaseId \
      -q '.[0].databaseId // empty') || { echo "push-and-watch: gh failed" >&2; exit 2; }
    [[ -z "$id" ]] || break
    if (( SECONDS >= deadline )); then
      echo "push-and-watch: no $workflow run on ${sha:0:7} after ${timeout}s" >&2
      exit 2
    fi
    sleep 10
  done
  run_ids+=("$id")
done

# shortcut: a run left queued for a runner is not bounded, because timeout-minutes counts from a job's start; bound the
# watch if a push ever hangs on one.
for i in "${!workflows[@]}"; do
  workflow=${workflows[$i]}
  id=${run_ids[$i]}
  # Without --exit-status, gh run watch exits non-zero only when gh itself fails; the run's result is read after it.
  # Without a TTY gh run watch reprints the whole run view on each refresh, so its output is dropped.
  gh run watch "$id" -R "$repo" --interval 30 > /dev/null || { echo "push-and-watch: gh run watch failed" >&2; exit 2; }
  result=$(gh run view "$id" -R "$repo" --json conclusion,url -q '"\(.conclusion) \(.url)"') \
    || { echo "push-and-watch: gh failed" >&2; exit 2; }
  read -r conclusion url <<< "$result"
  if [[ "$conclusion" != "success" ]]; then
    echo "push-and-watch: $workflow $conclusion on ${sha:0:7}: $url" >&2
    exit 1
  fi
done

echo "push-and-watch: ${workflows[*]} succeeded on ${sha:0:7}"
