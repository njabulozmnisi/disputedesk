#!/usr/bin/env bash
# Readiness check before a feature spec is dispatched (AGENTS.md, Definition of ready). Fails when:
#   1. the feature's challenge or pre-build review files (docs/verification/<id>-challenge.md, -prebuild-review.md,
#      -prebuild-security.md) hold a `[story]` point, and none of them nor the spec file has a Sofia disposition:
#      a line starting "Sofia, " (optionally as a list item) or a heading "Sofia's disposition" or "Sofia's refine";
#   2. they hold a `[decision]` point, and the first `decisions:` line in the spec's front matter (between its first
#      two `---` lines) is not exactly `decisions: settled`. The supervisor sets that field once the human has answered
#      every `[decision]` point of the feature in docs/verification/decisions.md; one field settles them all. The
#      field records that step, not who decided: the decisions record does;
#   3. a feature in the front matter's `after` list (`after: [story-<id>, ...]`, the sibling spec's file stem) is
#      neither `done` in the Features table of docs/tickets/README.md nor pushed as feature/<id> on origin, which a
#      lane can stack on.
# A point is a line where the tag opens a list item or a table cell, so prose such as "no `[decision]` point" is
# not counted. No review file is required: each one present is read, and a missing one is skipped, since the
# challenge and pre-build security files are not written for every feature.
# Usage: scripts/check-ready.sh <id>    (a feature slug, for example guard-allow-list)
# Exit codes: 0 ready; 1 not ready; 2 usage, no spec file, an after entry of another shape, or git ls-remote failed.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

# The feature slug's bounds (AGENTS.md, Ticket numbering).
is_slug() {
  [[ "$1" =~ ^[a-z][a-z0-9-]{0,40}$ && "$1" != records-* && "$1" != lows-* ]]
}

(($# == 1)) || { echo "check-ready: usage: scripts/check-ready.sh <id>" >&2; exit 2; }
id=$1
is_slug "$id" || { echo "check-ready: the ID must be a feature slug, for example guard-allow-list, got '$id'" >&2; exit 2; }

spec_file=docs/backlog/story-$id.md
[[ -f "$spec_file" ]] || { echo "check-ready: no spec file $spec_file" >&2; exit 2; }

review_files=()
for kind in challenge prebuild-review prebuild-security; do
  [[ -f "docs/verification/$id-$kind.md" ]] && review_files+=("docs/verification/$id-$kind.md")
done

problems=()

has_point() { # $1 = story or decision
  local tag="\\[$1\\]"
  ((${#review_files[@]} > 0)) || return 1
  grep -Eq "^[[:space:]]*(([-*]|[0-9]+\.)[[:space:]]+)?\`?$tag|\|[[:space:]]*\`?$tag" "${review_files[@]}"
}

if has_point story \
  && ! grep -Eq "^[[:space:]]*([-*][[:space:]]+)?Sofia, |^#+ Sofia's (disposition|refine)" "${review_files[@]}" "$spec_file"; then
  problems+=("[story] points in ${review_files[*]} have no Sofia disposition line (\"Sofia, <date>\" or a \"Sofia's disposition\" heading)")
fi

# The spec's front matter: the lines between its first two `---` lines.
front_matter=$(awk '/^---$/ { if (++fences == 2) exit; next } fences == 1' "$spec_file")
decisions_line=$(grep -m1 '^decisions:' <<< "$front_matter" || true)
if has_point decision && [[ "$decisions_line" != "decisions: settled" ]]; then
  problems+=("[decision] points in ${review_files[*]} are not settled: the spec's front matter has no \`decisions: settled\` line")
fi

after_line=$(grep -m1 '^after:' <<< "$front_matter" || true)
set -o noglob # each after entry is a word, never a file name pattern
for entry in $(tr -d '[],' <<< "${after_line#after:}"); do
  dependency=${entry#story-}
  if [[ "$entry" != story-* ]] || ! is_slug "$dependency"; then
    echo "check-ready: after entry '$entry' in $spec_file must be story-<id>, <id> a feature slug" >&2
    exit 2
  fi
  grep -Eq "^\| $dependency \|.*\| done \|$" docs/tickets/README.md && continue
  # --exit-code: 2 when no such branch exists on origin (git ls-remote); any other failure is tooling. The full ref
  # name, because a pattern matches any ref ending in it: feature/x would match old/feature/x.
  status=0
  git ls-remote --exit-code --heads origin "refs/heads/feature/$dependency" > /dev/null || status=$?
  ((status == 0)) && continue
  ((status == 2)) || { echo "check-ready: git ls-remote origin failed (exit $status)" >&2; exit 2; }
  problems+=("dependency $dependency is not done in docs/tickets/README.md and feature/$dependency is not on origin")
done

if ((${#problems[@]} > 0)); then
  echo "check-ready: $id is not ready ($spec_file):" >&2
  printf '  - %s\n' "${problems[@]}" >&2
  exit 1
fi
echo "check-ready: $id ready"
