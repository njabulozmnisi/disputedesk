#!/usr/bin/env bash
# Independent challenge by GPT-6 Sol (via GitHub Copilot CLI), a different model family from the builders. Two modes:
#   story     (default) a story before it is built: it reads the story, the architecture and the code it would touch.
#   decision  a decision document (an architecture decision added or changed, a PRD change, a brief) before stories
#             are written from it. The file may sit outside the repo; its folder is added read-only.
# Every point is tagged [story] (fixes the story only: a bound, a missing criterion, a gap against the code) or
# [decision] (contradicts or changes a PRD requirement or an architecture decision). The verdict lands in
# docs/verification/<id>-challenge.md.
# Usage: scripts/challenge-with-copilot.sh <id> <file> [story|decision]
# Exit codes: 0 proceed; 1 at least one [decision] point, or any decision-mode finding (a human decides);
#             3 story mode with only [story] points (the story is fixed without a human gate); 2 tooling (no verdict).
set -euo pipefail

id=${1:?id}; file=${2:?file}; mode=${3:-story}
models=("${CHALLENGER_MODEL:-gpt-6-sol}" "${CHALLENGER_FALLBACK_MODEL:-gpt-6-astra}")
root=$(git rev-parse --show-toplevel)
out="$root/docs/verification/$id-challenge.md"
mkdir -p "$(dirname "$out")"

case "$mode" in
  story)
    subject="The story below has not been built yet. Read the story file $file, docs/architecture/architecture.md, docs/glossary.md, AGENTS.md and the code the story would touch."
    gaps="Gaps: criteria that cannot be tested as written, inputs without bounds, missing error cases." ;;
  decision)
    subject="The decision document below has not been turned into stories yet. Read $file, docs/prd/prd.md, docs/initiative-disputedesk.md, docs/architecture/architecture.md, docs/glossary.md, AGENTS.md and the code it would change."
    gaps="Conflicts and gaps: every PRD requirement, initiative requirement (R-number) or architecture decision (AD-number) the document contradicts or changes without saying so, and anything it leaves undecided." ;;
  *) echo "challenge: mode must be story or decision, got $mode" >&2; exit 2 ;;
esac

prompt=$(cat <<PROMPT
Act as an independent design challenger. Do not modify any file.
$subject
The rules: use established standards and documented framework mechanisms, never invented ones; keep every design explainable in one or two sentences; over-engineering is the standing risk.
1. Pre-mortem: assume this failed in production a month from now. List the most likely causes.
2. Simplest design: describe the plainest design that still meets every requirement, and name anything over-engineered or unnecessary.
3. Assumptions: list assumptions that could be wrong, with the evidence.
4. Standards: name any mechanism implied that is not the documented framework approach or an industry standard, and the standard it should use, with a link.
5. $gaps
Start every point with [story] if it only changes the story or document under review, or [decision] if it contradicts or changes a PRD requirement or an architecture decision.
Only raise points with a concrete reason. "Proceed, nothing to change" is an acceptable answer.
Reply in Markdown with a first line exactly "VERDICT: PROCEED", "VERDICT: SIMPLIFY" or "VERDICT: RETHINK", then the five sections.
PROMPT
)

extra_dir=()
[[ "$mode" == decision ]] && extra_dir=(--add-dir "$(cd "$(dirname "$file")" && pwd)")

for model in "${models[@]}"; do
  if result=$(copilot -p "$prompt" --model "$model" -s --no-ask-user ${extra_dir[@]+"${extra_dir[@]}"} \
      --allow-tool 'shell(ls:*)' 'shell(cat:*)' 'shell(head:*)' 'shell(grep:*)' 'shell(find:*)' 'shell(git log:*)' 'shell(git show:*)' 'read' 'web_fetch' 'web_search' \
      --deny-tool 'write' 'shell(git commit:*)' 'shell(git push:*)' 'shell(rm:*)' 2>&1) \
     && grep -q '^VERDICT: \(PROCEED\|SIMPLIFY\|RETHINK\)' <<<"$result"; then
    { echo "# Challenge: $id ($mode)"; echo
      echo "- Challenger: $model via GitHub Copilot CLI"
      echo "- Base commit: \`$(git rev-parse HEAD)\`"
      echo "- Date (UTC): $(date -u +%Y-%m-%dT%H:%M:%SZ)"; echo
      echo "$result"; } > "$out"
    echo "$out"
    grep -q '^VERDICT: PROCEED' <<<"$result" && exit 0
    [[ "$mode" == decision ]] && exit 1
    grep -q '\[decision\]' <<<"$result" && exit 1
    exit 3
  fi
  echo "challenge: $model unavailable or gave no verdict: $(tail -n 3 <<<"$result")" >&2
done
exit 2
