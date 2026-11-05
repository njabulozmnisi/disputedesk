#!/usr/bin/env bash
# Prints code=false when a push changes only docs, so ci.yml's heavy jobs can skip it; otherwise code=true.
# GitHub's paths-ignore skips a whole workflow, never single jobs, so ci.yml's changes job runs this and each heavy
# job's if: reads the output. Docs are docs/** (except docs/problems/, which ProblemCatalogueTests reads), *.md and
# _bmad/**. The files compared:
#   a story/** push  the whole story: from its fork point with origin/main to the pushed commit, so the branch head's
#                    run covers every commit of the story, and a docs-only push after a red code push still runs
#                    everything (merge-story.sh trusts the head's run);
#   a main push      the push's own commits: before..after.
# --no-renames lists a rename as its old and new paths, so a file moved out of code into docs counts as code.
# Anything in doubt counts as code: another event, a new main branch or a force-push (no usable before commit), no
# fork point, an empty diff.
# Usage: scripts/ci-changes.sh <event> <ref> <before> <after>    (ci.yml passes github.event_name, github.ref,
#        github.event.before and github.sha; the clone needs the full history and origin/main)
# Exit codes: 0 printed; 2 usage.
set -euo pipefail

[[ $# -eq 4 ]] || { echo "usage: scripts/ci-changes.sh <event> <ref> <before> <after>" >&2; exit 2; }
event=$1; ref=$2; before=$3; after=$4

is_docs() {
  case $1 in
    docs/problems/*) return 1 ;;
    docs/* | *.md | _bmad/*) return 0 ;;
    *) return 1 ;;
  esac
}

from=""
if [[ "$event" == push && "$ref" == refs/heads/story/* ]]; then
  from=$(git merge-base origin/main "$after" 2> /dev/null) || from=""
elif [[ "$event" == push && ! "$before" =~ ^0+$ ]]; then
  from=$before
fi

code=true
if [[ -n "$from" ]] && files=$(git diff --name-only --no-renames "$from" "$after" 2> /dev/null) && [[ -n "$files" ]]; then
  code=false
  while IFS= read -r file; do
    is_docs "$file" || { code=true; break; }
  done <<< "$files"
fi
echo "code=$code"
