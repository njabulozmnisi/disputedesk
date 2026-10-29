#!/usr/bin/env bash
# Carries a story worktree's uncommitted change to a fix-round builder, whose own worktree starts at the base commit.
# Writes one binary patch of every tracked and untracked (not ignored) change against <base>, then applies it to a
# throwaway worktree at <base> and fails unless the result matches the story worktree file for file.
# The story worktree's own index is never touched (a temporary index is used).
# Usage: scripts/story-patch.sh <story-worktree> <base> <patch-file>
# The fix builder then runs, from its worktree root: git apply --binary --exclude='docs/*' <patch-file>
# Exit codes: 0 patch written and verified; 1 the patch does not reproduce the worktree; 2 usage.
set -euo pipefail

worktree=${1:?story worktree}; base=${2:?base commit}; patch=${3:?patch file}
[[ -d "$worktree" ]] || { echo "story-patch: no worktree at $worktree" >&2; exit 2; }

scratch=$(mktemp -d)
check="$scratch/check"
cleanup() { git -C "$worktree" worktree remove --force "$check" 2>/dev/null || true; rm -rf "$scratch"; }
trap cleanup EXIT

index="$scratch/index"
GIT_INDEX_FILE="$index" git -C "$worktree" read-tree "$base"
GIT_INDEX_FILE="$index" git -C "$worktree" add -A
GIT_INDEX_FILE="$index" git -C "$worktree" diff --cached --binary "$base" > "$patch"

git -C "$worktree" worktree add --quiet --detach "$check" "$base"
git -C "$check" apply --binary "$patch"

# Compare every file git would track in each tree (ignored build output such as bin/, obj/ and TestResults/ is skipped).
list() { (cd "$1" && git ls-files --cached --others --exclude-standard | sort | while read -r file; do [[ -f "$file" ]] && echo "$file"; done); }
if ! diff <(list "$worktree") <(list "$check") > "$scratch/names"; then
  echo "story-patch: file lists differ:" >&2; cat "$scratch/names" >&2; exit 1
fi
while read -r file; do
  cmp -s "$worktree/$file" "$check/$file" || { echo "story-patch: $file differs" >&2; exit 1; }
done < <(list "$worktree")

echo "story-patch: $(grep -c '^diff --git' "$patch") files, verified against $base -> $patch"
