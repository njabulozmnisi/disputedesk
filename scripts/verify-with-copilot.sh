#!/usr/bin/env bash
# Non-author verification: GPT-6 Astra (via GitHub Copilot CLI) checks a story in the
# current worktree and the verdict lands in docs/verification/<story>-verify.md.
# Usage: scripts/verify-with-copilot.sh <story-id> <story-file>
set -euo pipefail

story=${1:?story id}; story_file=${2:?story file}
models=("${VERIFIER_MODEL:-gpt-6-astra}" "${VERIFIER_FALLBACK_MODEL:-gpt-6-sol}")
root=$(git rev-parse --show-toplevel)
sha=$(git rev-parse HEAD)
# Fingerprint of the change: tracked edits plus new files (agents leave work uncommitted).
diff_hash=$({ git diff HEAD; git ls-files -o --exclude-standard -z | xargs -0 shasum -a 256; } | shasum -a 256 | cut -c1-12)
out="$root/docs/verification/$story-verify.md"
mkdir -p "$(dirname "$out")"

prompt=$(cat <<PROMPT
Act as an independent verifier that did not write this code. Check code only.
The work is deliberately uncommitted: treat every file in this worktree, including untracked ones, as the change under review.
Story file: $story_file. Read it, then:
1. Build: dotnet build backend (warnings are errors) and/or npm run build in frontend, whichever the diff touches.
2. Run the tests: unit, integration (Testcontainers) and architecture tests; Vitest, type check and lint for the frontend.
3. Check OpenAPI drift: the committed backend openapi.json matches the build output.
4. Compare the diff (git diff main...HEAD plus uncommitted changes) against each acceptance criterion. For each, name the test that proves it, or say it is unproved.
5. Standards: list any mechanism in the diff that replaces the documented framework approach or an established industry standard (Microsoft Learn, RFCs, OWASP), with the standard it should use. Report these under "Standards"; they do not change the verdict.
For .NET API questions, use: npx -y @microsoft/learn-cli search "<query>".
Do not modify any file. Reply in Markdown with a first line exactly "VERDICT: PASS" or "VERDICT: FAIL",
then a table of criterion | test | proved (yes/no), then failures with evidence, then a "Standards" section (or "Standards: none").
PROMPT
)

run() {
  "$root/scripts/test-lock.sh" run testcontainers "verify-$story" -- \
    copilot -p "$prompt" --model "$1" -s --no-ask-user --add-dir "$(cd "$(dirname "$story_file")" && pwd)" \
      --allow-tool 'shell(dotnet:*)' 'shell(npm:*)' 'shell(npx:*)' 'shell(git diff:*)' 'shell(git log:*)' 'shell(git show:*)' 'shell(ls:*)' 'shell(cat:*)' 'shell(head:*)' 'shell(grep:*)' 'shell(find:*)' 'shell(docker compose config:*)' 'shell(gitleaks:*)' 'shell(scripts/floor-guard.sh)' 'shell(actionlint:*)' 'shell(trivy:*)' 'read' \
      --deny-tool 'write' 'shell(git commit:*)' 'shell(git push:*)' 'shell(rm:*)'
}

for model in "${models[@]}"; do
  if result=$(run "$model" 2>&1) && grep -q '^VERDICT: \(PASS\|FAIL\)' <<<"$result"; then
    { echo "# Verification: $story"; echo
      echo "- Commit: \`$sha\` (uncommitted diff hash \`$diff_hash\`)"
      echo "- Verifier: $model via GitHub Copilot CLI"
      echo "- Date (UTC): $(date -u +%Y-%m-%dT%H:%M:%SZ)"; echo
      echo "$result"; } > "$out"
    echo "$out"
    grep -q '^VERDICT: PASS' <<<"$result" && exit 0 || exit 1
  fi
  echo "verify: $model unavailable or gave no verdict: $(tail -n 3 <<<"$result")" >&2
done
exit 2
