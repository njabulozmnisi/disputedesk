#!/usr/bin/env bash
# Merges a story branch into main by fast-forward, only once CI and Security have passed on the branch head, then
# pushes main and watches main's own runs (main's CI run is the one that publishes the images).
# Run it from the main checkout with main checked out.
# Usage: scripts/merge-story.sh <story>    (for example 8.7; merges origin/story/8.7)
# Exit codes: 0 merged and main's runs succeeded; 1 refused (the message says why), or main's CI or Security run
# failed after the push (push-and-watch.sh says which); 2 usage or tooling (git, gh).
set -euo pipefail

[[ $# -eq 1 ]] || { echo "usage: scripts/merge-story.sh <story>" >&2; exit 2; }
story=$1
[[ "$story" =~ ^[0-9]+\.[0-9]+[a-z]?$ ]] || { echo "merge-story: story ID must look like 8.7, got '$story'" >&2; exit 2; }
branch="story/$story"
cd "$(git rev-parse --show-toplevel)"

[[ "$(git rev-parse --abbrev-ref HEAD)" == main ]] || { echo "merge-story: check out main first" >&2; exit 2; }
git fetch --quiet origin main "$branch" || { echo "merge-story: fetch failed" >&2; exit 2; }
sha=$(git rev-parse "origin/$branch")
repo=$(git remote get-url origin | sed -E 's#(git@github.com:|https://github.com/)##; s#\.git$##')

# A fast-forward needs both the local and the remote main to be ancestors of the branch head.
for ref in main origin/main; do
  if ! git merge-base --is-ancestor "$ref" "$sha"; then
    echo "merge-story: refused: $ref does not fast-forward to $branch (${sha:0:7}); rebase the story branch" >&2
    exit 1
  fi
done

# The latest run of each workflow on the branch head: "no run", its status while it runs, else its conclusion.
latest='.[0] | if . == null then "no run" elif .status != "completed" then .status else .conclusion end'
for workflow in CI Security; do
  conclusion=$(gh run list -R "$repo" --branch "$branch" --commit "$sha" --workflow "$workflow" \
    --json status,conclusion -q "$latest") || { echo "merge-story: gh failed" >&2; exit 2; }
  if [[ "$conclusion" != success ]]; then
    echo "merge-story: refused: $workflow on $branch (${sha:0:7}) is '$conclusion', not success" >&2
    exit 1
  fi
done

git merge --ff-only --quiet "$sha" || { echo "merge-story: fast-forward failed" >&2; exit 2; }
echo "merge-story: main fast-forwarded to $branch (${sha:0:7})"
exec scripts/push-and-watch.sh origin main
