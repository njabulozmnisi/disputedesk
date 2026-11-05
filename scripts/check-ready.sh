#!/usr/bin/env bash
# Readiness check before a story is dispatched (AGENTS.md, Definition of ready). Fails when:
#   1. the story's challenge or pre-build review files (docs/verification/<id>-challenge.md, -prebuild-review.md,
#      -prebuild-security.md) hold a `[story]` point, and none of them nor the story file has a Sofia disposition:
#      a line starting "Sofia, " (optionally as a list item) or a heading "Sofia's disposition" or "Sofia's refine";
#   2. they hold a `[decision]` point, and the first `decisions:` line in the story's front matter (between its first
#      two `---` lines) is not exactly `decisions: settled`. The supervisor sets that field once the human has answered
#      every `[decision]` point of the story, citing the decisions record; one field settles them all. The field
#      records that step, not who decided: the decisions record does;
#   3. a story in the front matter's `after` list is neither `done` in docs/tickets/README.md nor pushed as
#      story/<id> on origin, which a lane can stack on (a bare number names a story of the same epic:
#      `after: [4, 5.1]` in story 7.12 means 7.4 and 5.1).
# A point is a line where the tag opens a list item or a table cell, so prose such as "no `[decision]` point" is
# not counted. No review file is required: each one present is read, and a missing one is skipped, since the
# challenge and pre-build security files are no longer written for every story.
# Usage: scripts/check-ready.sh <story-id>    (for example 7.12)
# Exit codes: 0 ready; 1 not ready; 2 usage, no story file, or git ls-remote failed.
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

# The story's front matter: the lines between its first two `---` lines.
front_matter=$(awk '/^---$/ { if (++fences == 2) exit; next } fences == 1' "$story_file")
decisions_line=$(grep -m1 '^decisions:' <<< "$front_matter" || true)
if has_point decision && [[ "$decisions_line" != "decisions: settled" ]]; then
  problems+=("[decision] points in ${review_files[*]} are not settled: the story's front matter has no \`decisions: settled\` line")
fi

# `after: [4, 5.1]` in the front matter; epic 1's plan files have no `after` line.
after_line=$(grep -m1 '^after:' "$story_file" || true)
for dependency in $(tr -d '[],' <<<"${after_line#after:}"); do
  [[ "$dependency" == *.* ]] || dependency="$epic.$dependency"
  grep -Eq "^\| ${dependency//./\\.} \|.*\| done \|$" docs/tickets/README.md && continue
  # --exit-code: 2 when no such branch exists on origin (git ls-remote); any other failure is tooling. The full ref
  # name, because a pattern matches any ref ending in it: story/7.1 would match feature/story/7.1.
  status=0
  git ls-remote --exit-code --heads origin "refs/heads/story/$dependency" > /dev/null || status=$?
  ((status == 0)) && continue
  ((status == 2)) || { echo "check-ready: git ls-remote origin failed (exit $status)" >&2; exit 2; }
  problems+=("dependency $dependency is not done in docs/tickets/README.md and story/$dependency is not on origin")
done

if ((${#problems[@]} > 0)); then
  echo "check-ready: $story is not ready ($story_file):" >&2
  printf '  - %s\n' "${problems[@]}" >&2
  exit 1
fi
echo "check-ready: $story ready"
