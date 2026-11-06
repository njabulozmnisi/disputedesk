#!/usr/bin/env bash
# Proves how scripts/check-infra-paths.sh reads a backslash in a path, on a scratch repository: an escaped dot belongs
# to the path, and a JSON or C-style escape (a backslash followed by a letter) ends it.
#   scripts/check-infra-paths.test.sh
# Needs bash and git; no network, no Docker.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
check="$root/scripts/check-infra-paths.sh"

fail() { echo "CHECK INFRA PATHS TEST FAIL: $*" >&2; exit 1; }
# expect <exit> <stale line> <case> <text>: writes the text to a file in the scratch repository, runs the check, and
# checks its exit code and the stale line it prints (empty for none).
expect() {
  local want=$1 line=$2 name=$3 output status=0
  printf '%s\n' "$4" > mention.txt
  output=$("$check" 2>&1) || status=$?
  ((status == want)) || fail "$name: exit $status, want $want; output: $output"
  [[ -z $line ]] || grep -qxF -- "$line" <<< "$output" || fail "$name: no line '$line' in: $output"
  echo "ok  exit $want: $name"
}

scratch=$(mktemp -d); trap 'rm -rf "$scratch"' EXIT
cd "$scratch"
git init -q
git -c user.name=test -c user.email=test@example.com commit -q --allow-empty -m base

# Built from a variable: the check reads every infra/ path this file names as one that must exist.
images=infra/images
mkdir -p "$images/postgres"
touch "$images/main.tf"

expect 0 "" "an escaped dot names the file" "$images/main\.tf"
expect 1 "mention.txt: $images/gone.tf" "a stale path with an escaped dot keeps its name" "$images/gone\.tf"
expect 0 "" "an escaped newline ends the path" "\"$images/postgres/\n+services:\""
expect 0 "" "text after an escaped newline is not the path" "\"$images/postgres/\nnext/line\""
expect 0 "" "an escaped tab ends the path" "\"$images/main.tf\tnext\""
expect 0 "" "an escape after an escaped dot ends the path" "\"$images/main\.tf\nnext\""
expect 1 "mention.txt: $images/gone/" "a stale path before an escape is still stale" "\"$images/gone/\nnext\""
