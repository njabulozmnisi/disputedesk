#!/usr/bin/env bash
# Proves the rule files name work by its feature slug (docs/backlog/story-feature-ids.md, criteria 1, 4 and 5): no
# numbered-story or per-epic placeholder is left, the lows and decisions files are named, and the index and the
# product owner's rules know feature specs. Every assertion that differs is named, then the test exits 1.
#   scripts/rule-text.test.sh
# Needs bash and git; reads the working tree's tracked files.
set -euo pipefail
cd "$(dirname "$0")/.."
rule_files=(AGENTS.md CLAUDE.md
  .claude/skills/build-story/SKILL.md .claude/skills/verify-story/SKILL.md .claude/skills/review-story/SKILL.md
  .claude/skills/security-story/SKILL.md .claude/skills/qa-story/SKILL.md
  .claude/agents/backend-builder.md .claude/agents/frontend-builder.md .claude/agents/reviewer.md .claude/agents/security.md
  docs/process/agentic-sdlc.md _bmad/custom/bmad-agent-po.toml .claude/hooks/guard.py)
failures=()

for file in "${rule_files[@]}"; do
  [[ -f "$file" ]] || failures+=("rule file $file is missing")
done

# none <description> <git grep option...>: the pattern matches no line of the rule files.
none() {
  local description=$1 hits status=0
  shift
  hits=$(git grep -n "$@" -- "${rule_files[@]}") || status=$?
  ((status <= 1)) || { failures+=("$description: git grep failed (exit $status)"); return; } # 1 is no match
  [[ -z "$hits" ]] || failures+=("$description: $hits")
}
none "a numbered-story placeholder (criterion 1)" -e 'epic-<epic>' -e '<epic>.<story>' -e '<story>'
none "a per-epic lows, decisions or retrospective rule (criterion 4)" -i \
  -e 'epic-<epic>' -e "epic's lows" -e 'per epic' -e 'every epic'

# names <file> <fixed text>: the file contains the text.
names() {
  grep -Fq -- "$2" "$1" || failures+=("$1 does not name $2")
}
for file in AGENTS.md docs/process/agentic-sdlc.md; do
  names "$file" docs/verification/lows.md
  names "$file" docs/verification/decisions.md
done
names AGENTS.md '10 open rows'
names _bmad/custom/bmad-agent-po.toml 'docs/backlog/story-<id>.md'
grep -Fqx '| Slug | Title | Owner | Tier | Status |' docs/tickets/README.md \
  || failures+=("docs/tickets/README.md has no Features header row '| Slug | Title | Owner | Tier | Status |'")

if ((${#failures[@]} > 0)); then
  echo "RULE TEXT TEST FAIL:" >&2
  printf '  %s\n' "${failures[@]}" >&2
  exit 1
fi
echo "rule-text: all assertions hold"
