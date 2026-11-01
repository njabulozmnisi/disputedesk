#!/usr/bin/env bash
# Readiness check before a story is dispatched (AGENTS.md, Definition of ready). Fails when:
#   1. the story's challenge or pre-build review files (docs/verification/<id>-challenge.md, -prebuild-review.md,
#      -prebuild-security.md) hold a `[story]` point, and none of them nor the story file has a Sofia disposition:
#      a line starting "Sofia, " (optionally as a list item) or a heading "Sofia's disposition" or "Sofia's refine";
#   2. they hold a `[decision]` point, and none of them nor the story file records a human decision: a line naming
#      the human together with "decision", "decided" or "settled" (any case), for example a "## Human decisions"
#      heading, "S2 [decision], human: ..." or "The human settled ...";
#   3. a story in the front matter's `after` list is not `done` in docs/tickets/README.md
#      (a bare number names a story of the same epic: `after: [4, 5.1]` in story 7.12 means 7.4 and 5.1).
# A point is a line where the tag opens a list item or a table cell, so prose such as "no `[decision]` point" is
# not counted. Missing review files are skipped: whether a story needs them is the supervisor's call.
# Usage: scripts/check-ready.sh <story-id>    (for example 7.12)
# Exit codes: 0 ready; 1 not ready; 2 usage or no story file.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

story=${1:?usage: scripts/check-ready.sh <story-id>}
[[ "$story" =~ ^[0-9]+\.[0-9]+$ ]] || { echo "check-ready: story ID must look like 7.12, got '$story'" >&2; exit 2; }
epic=${story%%.*}

# The file name prefix is the story ID (docs/tickets/README.md); the dash after it keeps 7.1 from matching 7.12.
story_files=(docs/tickets/epic-"$epic"-*/tickets/story-"$story"-*.md)
[[ -f "${story_files[0]}" ]] || { echo "check-ready: no story file for $story under docs/tickets/" >&2; exit 2; }
story_file=${story_files[0]}

review_files=()
for kind in challenge prebuild-review prebuild-security; do
  [[ -f "docs/verification/$story-$kind.md" ]] && review_files+=("docs/verification/$story-$kind.md")
done

problems=()

has_point() { # $1 = story or decision
  local tag="\\[$1\\]"
  ((${#review_files[@]} > 0)) || return 1
  grep -Eq "^[[:space:]]*(([-*]|[0-9]+\.)[[:space:]]+)?\`?$tag|\|[[:space:]]*\`?$tag" "${review_files[@]}"
}

if has_point story \
  && ! grep -Eq "^[[:space:]]*([-*][[:space:]]+)?Sofia, |^#+ Sofia's (disposition|refine)" "${review_files[@]}" "$story_file"; then
  problems+=("[story] points in ${review_files[*]} have no Sofia disposition line (\"Sofia, <date>\" or a \"Sofia's disposition\" heading)")
fi

if has_point decision \
  && ! grep -hi human "${review_files[@]}" "$story_file" | grep -Ei "decision|decided|settled" >/dev/null; then
  problems+=("[decision] points in ${review_files[*]} have no recorded human decision")
fi

# `after: [4, 5.1]` in the front matter; epic 1's plan files have no `after` line.
after_line=$(grep -m1 '^after:' "$story_file" || true)
for dependency in $(tr -d '[],' <<<"${after_line#after:}"); do
  [[ "$dependency" == *.* ]] || dependency="$epic.$dependency"
  if ! grep -Eq "^\| ${dependency//./\\.} \|.*\| done \|$" docs/tickets/README.md; then
    problems+=("dependency $dependency is not done in docs/tickets/README.md")
  fi
done

if ((${#problems[@]} > 0)); then
  echo "check-ready: $story is not ready ($story_file):" >&2
  printf '  - %s\n' "${problems[@]}" >&2
  exit 1
fi
echo "check-ready: $story ready"
