#!/usr/bin/env bash
# Decides whether a pull request's change can break the e2e regression suite, for CI's e2e-regression job: does any path
# changed on head since its merge base with base match a line of scripts/regression-paths.list? A line ending in / matches
# every path under that directory; any other line matches exactly that path. --no-renames lists both paths of a rename,
# and -z keeps git from quoting an unusual name, which would then match nothing. A script rather than on.paths, which
# would leave the required check ci-passed pending (ad-012, Options rejected).
# Usage: scripts/regression-paths.sh <base commit> <head commit>   (from the repository root)
# Exit codes, grep's convention: 0 a path matches; 1 none does; 2 usage, a bad commit, no merge base or no list.
# No set -e: a command failing with status 1 would read as "none matches" and skip the suite, so every failure exits 2.
set -uo pipefail

[[ $# -eq 2 ]] || { echo "usage: scripts/regression-paths.sh <base commit> <head commit>" >&2; exit 2; }
base=$1; head=$2
list="$(dirname "$0")/regression-paths.list"
[[ -s "$list" ]] || { echo "regression-paths: no list at $list" >&2; exit 2; }
for commit in "$base" "$head"; do
  git rev-parse --verify --quiet "$commit^{commit}" > /dev/null || { echo "regression-paths: '$commit' is not a commit" >&2; exit 2; }
done
changed=$(git diff --name-only --no-renames -z "$base...$head" | tr '\0' '\n') || { echo "regression-paths: git diff failed" >&2; exit 2; }

# line_matches <path>: the path matches a line of the list.
line_matches() {
  local line
  while IFS= read -r line; do
    if [[ $line == */ && $1 == "$line"* ]] || [[ $1 == "$line" ]]; then return 0; fi
  done < "$list"
  return 1
}

while IFS= read -r path; do
  if line_matches "$path"; then
    echo "regression-paths: $path starts the suite"
    exit 0
  fi
done <<< "$changed"
echo "regression-paths: no changed path starts the suite"
exit 1
