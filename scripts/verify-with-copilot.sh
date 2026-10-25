#!/usr/bin/env bash
# Non-author verification: GPT-6 Astra (via GitHub Copilot CLI) checks a story in the
# current worktree and the verdict lands in docs/verification/<story>-verify.md.
# Usage: scripts/verify-with-copilot.sh <story-id> <story-file>
set -euo pipefail

story=${1:?story id}; story_file=${2:?story file}
models=("${VERIFIER_MODEL:-gpt-6-astra}" "${VERIFIER_FALLBACK_MODEL:-gpt-6-sol}")
root=$(git rev-parse --show-toplevel)
sha=$(git rev-parse HEAD)
diff_hash=$(git diff HEAD | shasum -a 256 | cut -c1-12)
out="$root/docs/verification/$story-verify.md"
mkdir -p "$(dirname "$out")"

prompt=$(cat <<PROMPT
You are an independent verifier. You did not write this code. Check code only.
Story file: $story_file. Read it, then:
1. Build: dotnet build backend (warnings are errors) and/or npm run build in frontend, whichever the diff touches.
2. Run the tests: unit, integration (Testcontainers) and architecture tests; Vitest, type check and lint for the frontend.
3. Check OpenAPI drift: the committed backend openapi.json matches the build output.
4. Compare the diff (git diff main...HEAD plus uncommitted changes) against each acceptance criterion. For each, name the test that proves it, or say it is unproved.
For .NET API questions, use: npx -y @microsoft/learn-cli search "<query>".
Do not modify any file. Reply in Markdown with a first line exactly "VERDICT: PASS" or "VERDICT: FAIL",
then a table of criterion | test | proved (yes/no), then failures with evidence.
PROMPT
)

run() {
  "$root/scripts/test-lock.sh" run testcontainers "verify-$story" -- \
    copilot -p "$prompt" --model "$1" -s --no-ask-user --add-dir "$(cd "$(dirname "$story_file")" && pwd)" \
      --allow-tool 'shell(dotnet:*)' 'shell(npm:*)' 'shell(npx:*)' 'shell(git diff:*)' 'shell(git log:*)' 'shell(git show:*)' 'shell(ls:*)' 'shell(cat:*)' 'shell(head:*)' 'shell(grep:*)' 'shell(find:*)' 'read' \
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
