#!/usr/bin/env bash
# Proves scripts/check-ready.sh on a scratch repository: the feature slug's bounds (exit 2 outside them), the three
# readiness rules (exit 1 when one fails, 0 when all hold), and the after: entries' shape. A bare repository stands in
# for origin.
#   scripts/check-ready.test.sh
# Needs bash and git; no network.
set -euo pipefail
check_ready="$(cd "$(dirname "$0")" && pwd)/check-ready.sh"

fail() { echo "CHECK READY TEST FAIL: $*" >&2; exit 1; }
# expect <exit> <output pattern> <case> [argument...]: runs check-ready in the scratch repository.
expect() {
  local want=$1 pattern=$2 name=$3 output status=0
  shift 3
  output=$(cd "$repo" && "$check_ready" "$@" 2>&1) || status=$?
  ((status == want)) || fail "$name: exit $status, want $want; output: $output"
  grep -Eq -- "$pattern" <<< "$output" || fail "$name: no '$pattern' in: $output"
  echo "ok  exit $want: $name"
}
# spec <id> [front matter line...]: writes docs/backlog/story-<id>.md with that front matter.
spec() {
  local id=$1
  shift
  { echo '---'; echo 'type: story'; printf '%s\n' "$@"; echo '---'; echo; echo "# $id"; } > "$repo/docs/backlog/story-$id.md"
}
# review <id> <line>: writes the feature's pre-build review with one line.
review() { echo "$2" > "$repo/docs/verification/$1-prebuild-review.md"; }

scratch=$(cd "$(mktemp -d)" && pwd -P); trap 'rm -rf "$scratch"' EXIT
export GIT_CONFIG_GLOBAL=/dev/null GIT_AUTHOR_NAME=t GIT_AUTHOR_EMAIL=t@example.invalid \
  GIT_COMMITTER_NAME=t GIT_COMMITTER_EMAIL=t@example.invalid
repo="$scratch/repo"
git init -q -b main "$repo"
mkdir -p "$repo/docs/backlog" "$repo/docs/verification" "$repo/docs/tickets/epic-1-x/tickets"
cat > "$repo/docs/tickets/README.md" <<'README'
| ID | Title | Owner | Status |
|---|---|---|---|
| 1.2 | [Numbered](epic-1-x/tickets/story-1.2-numbered.md) | backend | done |

## Features

| Slug | Title | Owner | Tier | Status |
|---|---|---|---|---|
| done-dep | [Done](../backlog/story-done-dep.md) | backend | Feature | done |
| planned-dep | [Planned](../backlog/story-planned-dep.md) | backend | Feature | planned |
README
touch "$repo/docs/tickets/epic-1-x/tickets/story-1.2-numbered.md" # a numbered story's file, which no longer counts
git -C "$repo" add -A; git -C "$repo" commit -q -m base
# origin holds one pushed feature branch, which a lane can stack on.
git init -q --bare "$scratch/origin.git"
git -C "$repo" remote add origin "$scratch/origin.git"
git -C "$repo" push -q origin main:refs/heads/feature/pushed-dep

slug41=$(printf '%41s' '' | tr ' ' a) # the longest feature slug
slug42=$(printf '%42s' '' | tr ' ' a)

# --- the argument: exactly one feature slug, else exit 2 ---
expect 2 'usage' "no argument"
expect 2 'usage' "two arguments" a b
expect 2 'feature slug' "an empty argument" ''
expect 2 'feature slug' "an upper-case letter" Feature-X
expect 2 'feature slug' "a 42-character slug" "$slug42"
expect 2 'feature slug' "the reserved records- prefix" records-x
expect 2 'feature slug' "the reserved lows- prefix" lows-x
expect 2 'feature slug' "a numbered story ID, with its story file present" 1.2
expect 2 'feature slug' "a three-part number" 1.2.3
expect 2 'feature slug' "a leading dash" -x
expect 2 'feature slug' "an option" --help
expect 2 'feature slug' "a leading digit" 7a
expect 2 'no spec file' "a slug with no spec file" no-such-feature

spec a
expect 0 'a ready' "a 1-character slug" a
spec "$slug41"
expect 0 "$slug41 ready" "a 41-character slug" "$slug41"
spec records
expect 0 'records ready' "records without the dash is not reserved" records
spec lows
expect 0 'lows ready' "lows without the dash is not reserved" lows

# --- rule 1: [story] points need a Sofia disposition ---
spec story-points
review story-points "| S1 | \`[story]\` | medium | a point |"
expect 1 'no Sofia disposition' "a [story] point with no disposition" story-points
echo 'Sofia, 2026-11-06: S1 fixed in the spec.' >> "$repo/docs/verification/story-points-prebuild-review.md"
expect 0 'story-points ready' "a [story] point with a disposition" story-points
review story-points "The review found no \`[story]\` point."
expect 0 'story-points ready' "a tag in prose is not a point" story-points

# --- rule 2: [decision] points need decisions: settled in the front matter ---
spec decision-points
review decision-points "| D1 | \`[decision]\` | medium | a point |"
expect 1 'not settled' "a [decision] point with no decisions line" decision-points
spec decision-points 'decisions: open'
expect 1 'not settled' "a [decision] point with decisions: open" decision-points
spec decision-points
echo 'decisions: settled' >> "$repo/docs/backlog/story-decision-points.md"
expect 1 'not settled' "decisions: settled in the body, not the front matter" decision-points
spec decision-points 'decisions: settled'
expect 0 'decision-points ready' "a [decision] point with decisions: settled" decision-points
spec guard
review guard-allow-list "| D1 | \`[decision]\` | medium | a point |"
expect 0 'guard ready' "another feature's review, whose slug starts with this one, is not read" guard

# --- rule 3: each after: entry is story-<slug>, done in the Features table or pushed as feature/<slug> ---
spec deps 'after: []'
expect 0 'deps ready' "an empty after list" deps
spec deps
echo 'after: [story-planned-dep]' >> "$repo/docs/backlog/story-deps.md"
expect 0 'deps ready' "an after line in the body, not the front matter, is not read" deps
spec deps 'after: [story-done-dep, story-pushed-dep]'
expect 0 'deps ready' "one dependency done, one pushed" deps
spec deps 'after: [story-planned-dep]'
expect 1 'dependency planned-dep is not done' "a dependency planned and not pushed" deps
spec deps 'after: [story-done]'
expect 1 'dependency done is not done' "a slug that is a prefix of a done row is not done" deps
spec deps 'after: [4]'
expect 2 "after entry '4'" "a bare number" deps
spec deps 'after: [2.3]'
expect 2 "after entry '2.3'" "a numbered story ID" deps
spec deps 'after: [1.2]'
expect 2 "after entry '1.2'" "a numbered story ID that is done" deps
spec deps 'after: [done-dep]'
expect 2 "after entry 'done-dep'" "a slug without the story- prefix" deps
spec deps 'after: [story-Done-dep]'
expect 2 "after entry 'story-Done-dep'" "an entry whose slug is out of bounds" deps
spec deps 'after: [story-records-x]'
expect 2 "after entry 'story-records-x'" "an entry with a reserved prefix" deps
touch "$repo/story-globbed"
spec deps 'after: [story-*]'
expect 2 "after entry 'story-\*'" "a glob pattern is a word, not the file names it would match" deps
spec deps 'after: [story-planned-dep]'
git -C "$repo" remote set-url origin "$scratch/no-such-origin.git"
expect 2 'git ls-remote origin failed' "origin cannot be read" deps

echo "check-ready: all cases as expected"
