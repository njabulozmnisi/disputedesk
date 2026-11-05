#!/usr/bin/env bash
# Proves scripts/ci-changes.sh on scratch repositories: a story push is judged on the whole story since main, a main
# push on its own commits, docs/problems/ counts as code, a rename counts both its paths, and a doubt counts as code.
#   scripts/ci-changes.test.sh
# Needs bash and git; no network.
set -euo pipefail
script="$(cd "$(dirname "$0")" && pwd)/ci-changes.sh"

fail() { echo "CI CHANGES TEST FAIL: $*" >&2; exit 1; }
# expect <code> <event> <ref> <before> <after>: runs the script in the clone and compares its code= line.
expect() {
  local want=$1 got; shift
  got=$(cd "$clone" && "$script" "$@") || fail "exit $? for $*"
  [[ "$got" == "code=$want" ]] || fail "$* printed '$got', want code=$want"
  echo "ok  code=$want for $1 ${2#refs/heads/}"
}
commit() { # commit <repo> <path> [message]
  mkdir -p "$(dirname "$1/$2")"; echo "$RANDOM" >> "$1/$2"
  git -C "$1" add -A; git -C "$1" commit -q -m "${3:-$2}"; git -C "$1" rev-parse HEAD
}

scratch=$(mktemp -d); trap 'rm -rf "$scratch"' EXIT
export GIT_CONFIG_GLOBAL=/dev/null GIT_AUTHOR_NAME=t GIT_AUTHOR_EMAIL=t@example.invalid \
  GIT_COMMITTER_NAME=t GIT_COMMITTER_EMAIL=t@example.invalid
origin="$scratch/origin"; work="$scratch/work"; clone="$scratch/clone"
zero=0000000000000000000000000000000000000000

git init -q -b main "$work"
base=$(commit "$work" backend/App.cs)
# main moves on after the story forks, so a plain main..story diff would count main's own code change.
git -C "$work" switch -q -c story/1.1
story_code=$(commit "$work" backend/Story.cs)
story_docs=$(commit "$work" docs/notes.md)
git -C "$work" switch -q main
main_code=$(commit "$work" backend/Main.cs)
main_docs=$(commit "$work" README.md)
main_problems=$(commit "$work" docs/problems/forbidden.md)
git -C "$work" mv backend/App.cs docs/App.md; git -C "$work" commit -q -m rename
main_rename=$(git -C "$work" rev-parse HEAD)
main_bmad=$(commit "$work" _bmad/config.yaml)
git clone -q --bare "$work" "$origin"
git clone -q "$origin" "$clone"

# Fix 1: a docs-only push after a red code push on a story branch still runs everything.
expect true push refs/heads/story/1.1 "$story_code" "$story_docs"
# The first push of a story branch has no before commit; the story since main decides.
expect true push refs/heads/story/1.1 "$zero" "$story_docs"
# main keeps the before..after diff.
expect false push refs/heads/main "$main_code" "$main_docs"
expect true push refs/heads/main "$base" "$main_code"
expect false push refs/heads/main "$main_rename" "$main_bmad"
# Fix 2: docs/problems/ is read by ProblemCatalogueTests.
expect true push refs/heads/main "$main_docs" "$main_problems"
# Fix 2: without --no-renames the rename lists only docs/App.md and would count as docs.
expect true push refs/heads/main "$main_problems" "$main_rename"
# A doubt runs everything.
expect true push refs/heads/main "$zero" "$main_docs"
expect true push refs/heads/main 1234567890123456789012345678901234567890 "$main_docs"
expect true pull_request refs/pull/1/merge "$main_code" "$main_docs"

# A story branch made only of docs commits skips the heavy jobs.
git -C "$work" switch -q -c story/1.2 "$main_bmad"
docs_story=$(commit "$work" docs/tickets/story.md)
git -C "$work" push -q "$origin" story/1.2
git -C "$clone" fetch -q
expect false push refs/heads/story/1.2 "$zero" "$docs_story"
# An empty diff (a story branch at main's head) is a doubt.
expect true push refs/heads/story/1.3 "$zero" "$main_bmad"

# A clone without origin/main cannot find the story's fork point.
git -C "$clone" update-ref -d refs/remotes/origin/main
expect true push refs/heads/story/1.2 "$zero" "$docs_story"
echo "ci-changes: all cases pass"
