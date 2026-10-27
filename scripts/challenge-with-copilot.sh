#!/usr/bin/env bash
# Independent challenge before a story is built: GPT-6 Sol (via GitHub Copilot CLI), a different
# model family from the builders, reads only the story and the architecture and says whether the
# design should go ahead. The verdict lands in docs/verification/<story>-challenge.md.
# Usage: scripts/challenge-with-copilot.sh <story-id> <story-file>
# Exit codes: 0 proceed, 1 simplify or rethink (a human decides), 2 tooling (no verdict).
set -euo pipefail

story=${1:?story id}; story_file=${2:?story file}
models=("${CHALLENGER_MODEL:-gpt-6-sol}" "${CHALLENGER_FALLBACK_MODEL:-gpt-6-astra}")
root=$(git rev-parse --show-toplevel)
out="$root/docs/verification/$story-challenge.md"
mkdir -p "$(dirname "$out")"

prompt=$(cat <<PROMPT
Act as an independent design challenger. The story below has not been built yet. Do not modify any file.
Read the story file $story_file, docs/architecture/architecture.md, docs/glossary.md, AGENTS.md and the code the story would touch.
The rules: use established standards and documented framework mechanisms, never invented ones; keep every design explainable in one or two sentences; over-engineering is the standing risk.
1. Pre-mortem: assume this story failed in production a month from now. List the most likely causes.
2. Simplest design: describe the plainest design that still meets every criterion, and name anything in the story that is over-engineered or unnecessary.
3. Assumptions: list assumptions in the story or architecture that could be wrong, with the evidence.
4. Standards: name any mechanism the story implies that is not the documented framework approach or an industry standard, and the standard it should use, with a link.
5. Gaps: criteria that cannot be tested as written, inputs without bounds, missing error cases.
Only raise points with a concrete reason. "Proceed, nothing to change" is an acceptable answer.
Reply in Markdown with a first line exactly "VERDICT: PROCEED", "VERDICT: SIMPLIFY" or "VERDICT: RETHINK", then the five sections.
PROMPT
)

for model in "${models[@]}"; do
  if result=$(copilot -p "$prompt" --model "$model" -s --no-ask-user \
      --allow-tool 'shell(ls:*)' 'shell(cat:*)' 'shell(head:*)' 'shell(grep:*)' 'shell(find:*)' 'shell(git log:*)' 'shell(git show:*)' 'read' 'web_fetch' 'web_search' \
      --deny-tool 'write' 'shell(git commit:*)' 'shell(git push:*)' 'shell(rm:*)' 2>&1) \
     && grep -q '^VERDICT: \(PROCEED\|SIMPLIFY\|RETHINK\)' <<<"$result"; then
    { echo "# Challenge: $story"; echo
      echo "- Challenger: $model via GitHub Copilot CLI"
      echo "- Base commit: \`$(git rev-parse HEAD)\`"
      echo "- Date (UTC): $(date -u +%Y-%m-%dT%H:%M:%SZ)"; echo
      echo "$result"; } > "$out"
    echo "$out"
    grep -q '^VERDICT: PROCEED' <<<"$result" && exit 0 || exit 1
  fi
  echo "challenge: $model unavailable or gave no verdict: $(tail -n 3 <<<"$result")" >&2
done
exit 2
