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
diff_hash=$("$root/scripts/diff-hash.sh" "$root" HEAD)
out="$root/docs/verification/$story-verify.md"
mkdir -p "$(dirname "$out")"

prompt=$(cat <<PROMPT
Act as an independent verifier that did not write this code. Check code only.
The work is deliberately uncommitted: treat every file in this worktree, including untracked ones, as the change under review.
Story file: $story_file. Read it, then:
1. Build: dotnet build backend (warnings are errors) and/or npm run build in frontend, whichever the diff touches.
2. Do not re-run the test suites. Read the builder's results instead: the files listed in docs/verification/$story-build.json under test_results.files (TRX, and JUnit for Vitest). A missing results file, or a failed test in one, is a failure. Then run only the checks that need no suite: dotnet format style and whitespace --verify-no-changes on the backend solution (and on infra/tests' DisputeDesk.PlatformTests when the diff touches it), npm run typecheck, lint and format for the frontend, and shellcheck on every shell script in the diff.
3. Check OpenAPI drift: the committed backend openapi.json matches the build output.
4. Compare the diff (git diff main...HEAD plus uncommitted changes) against each acceptance criterion. For each, name the test that proves it, or say it is unproved.
5. Standards: list any mechanism in the diff that replaces the documented framework approach or an established industry standard (Microsoft Learn, RFCs, OWASP), with the standard it should use. Report these under "Standards"; they do not change the verdict.
For .NET API questions, use: npx -y @microsoft/learn-cli search "<query>".
Do not modify any file. Reply in Markdown with a first line exactly "VERDICT: PASS" or "VERDICT: FAIL",
then a table of criterion | test | proved (yes/no), then failures with evidence, then a "Standards" section (or "Standards: none").
PROMPT
)

# The whole run holds the testcontainers lock, so the verifier runs dotnet test directly. scripts/test-lock.sh is not
# allowed: a nested `test-lock.sh run testcontainers` would wait on this run's own lock until it timed out.
run() {
  "$root/scripts/test-lock.sh" run testcontainers "verify-$story" -- \
    copilot -p "$prompt" --model "$1" -s --no-ask-user --add-dir "$(cd "$(dirname "$story_file")" && pwd)" \
      --allow-tool 'shell(dotnet:*)' 'shell(npm:*)' 'shell(npx:*)' 'shell(git diff:*)' 'shell(git log:*)' 'shell(git show:*)' 'shell(ls:*)' 'shell(cat:*)' 'shell(head:*)' 'shell(grep:*)' 'shell(find:*)' 'shell(docker compose config:*)' 'shell(gitleaks:*)' 'shell(scripts/floor-guard.sh)' 'shell(actionlint:*)' 'shell(trivy:*)' 'shell(terraform:*)' \
        'shell(shellcheck:*)' 'shell(infra/deploy/aws/check-compose.sh)' 'shell(infra/deploy/aws/deploy.test.sh)' \
        'shell(infra/deploy/aws/rds-init.test.sh)' 'shell(infra/terraform/iam-names.test.sh)' 'read' \
      --deny-tool 'write' 'shell(git commit:*)' 'shell(git push:*)' 'shell(rm:*)' 'shell(terraform apply:*)' 'shell(terraform destroy:*)'
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
