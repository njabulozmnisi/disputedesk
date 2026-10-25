#!/usr/bin/env bash
# Restore the BMAD planning skills pinned in skills-lock.json, then set up its runtime in _bmad/.
# Only needed for planning work; BMAD's files are gitignored like any other installed dependency.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

source=$(jq -r '[.skills[].source] | unique | .[0]' skills-lock.json)
ref=$(jq -r '[.skills[].ref] | unique | .[0]' skills-lock.json)
skill_args=()
for name in $(jq -r '.skills | keys[]' skills-lock.json); do skill_args+=(--skill "$name"); done

npx -y skills@1.7.4 add "$source#$ref" "${skill_args[@]}" -a claude-code -y
uv run --quiet .claude/skills/bmad/scripts/setup.py --project-root "$PWD" --skill .claude/skills/bmad >/dev/null
echo "BMAD restored at $ref"
