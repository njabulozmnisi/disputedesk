#!/usr/bin/env bash
# Proves the committed side of main's protection (ad-012 clause 2), with no network and no GitHub call:
# - .github/rulesets/main.json is exactly the expected ruleset, and its two required checks are the
#   gate jobs of ci.yml and security.yml;
# - infra/README.md's apply commands send the ruleset file and the seven repository settings, the booleans typed with
#   -F and the strings with -f, as the owner;
# - floor-guard's Forbidden terms step, read from ci.yml and run as GitHub runs a step with no shell:, fails when the
#   FORBIDDEN_TERMS secret is empty or holds no terms (a pull request from a fork), and with terms set behaves as
#   before: it passes on no match and fails on a match, printing only file:line.
#   scripts/main-protection.test.sh
# Needs bash, git, yq (v4) and jq.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
ci="$root/.github/workflows/ci.yml"
security="$root/.github/workflows/security.yml"
ruleset="$root/.github/rulesets/main.json"
readme="$root/infra/README.md"

fail() { echo "MAIN PROTECTION TEST FAIL: $*" >&2; exit 1; }
# expect <exit> <output pattern> <case> <command...>: runs the command and checks its exit code and output.
expect() {
  local want=$1 pattern=$2 name=$3 output status=0
  shift 3
  output=$("$@" 2>&1) || status=$?
  ((status == want)) || fail "$name: exit $status, want $want; output: $output"
  grep -Eq -- "$pattern" <<< "$output" || fail "$name: no '$pattern' in: $output"
  echo "ok  exit $want: $name"
}

scratch=$(mktemp -d); trap 'rm -rf "$scratch"' EXIT

# --- the ruleset file, whole ---
expected_ruleset='{
  "name": "main",
  "target": "branch",
  "enforcement": "active",
  "bypass_actors": [],
  "conditions": {"ref_name": {"include": ["~DEFAULT_BRANCH"], "exclude": []}},
  "rules": [
    {"type": "deletion"},
    {"type": "non_fast_forward"},
    {"type": "pull_request", "parameters": {
      "required_approving_review_count": 0,
      "dismiss_stale_reviews_on_push": false,
      "require_code_owner_review": false,
      "require_last_push_approval": false,
      "required_review_thread_resolution": false}},
    {"type": "required_status_checks", "parameters": {
      "strict_required_status_checks_policy": true,
      "required_status_checks": [
        {"context": "ci-passed", "integration_id": 15368},
        {"context": "security-passed", "integration_id": 15368}]}},
    {"type": "code_scanning", "parameters": {
      "code_scanning_tools": [{"tool": "CodeQL", "security_alerts_threshold": "high_or_higher", "alerts_threshold": "errors"}]}}
  ]
}'
[[ $(jq -S . "$ruleset") == "$(jq -S . <<< "$expected_ruleset")" ]] ||
  fail "main.json differs from the expected ruleset: $(diff <(jq -S . <<< "$expected_ruleset") <(jq -S . "$ruleset") || true)"
echo "ok  main.json is exactly the expected ruleset"

# A check run is named after its job's id when the job has no name: (Workflow syntax, jobs.<job_id>.name).
gate_is_context() { # gate_is_context <workflow> <job>
  [[ $(JOB=$2 yq '.jobs | has(strenv(JOB))' "$1") == true ]] || fail "$1 has no job $2"
  [[ $(JOB=$2 yq '.jobs[strenv(JOB)] | has("name")' "$1") == false ]] || fail "$1: the job $2 has a name:"
}
gate_is_context "$ci" ci-passed
gate_is_context "$security" security-passed
echo "ok  the required contexts are the gate jobs ci-passed (ci.yml) and security-passed (security.yml)"

# --- the apply commands in infra/README.md ---
owner_token="GH_TOKEN=\"\$(gh auth token --user njabulozmnisi)\" gh api"
for line in \
  "$owner_token --method POST repos/njabulozmnisi/disputedesk/rulesets --input .github/rulesets/main.json" \
  "$owner_token --method PUT repos/njabulozmnisi/disputedesk/rulesets/<id> --input .github/rulesets/main.json" \
  "$owner_token --method PATCH repos/njabulozmnisi/disputedesk \\" \
  "-F allow_squash_merge=true -F allow_merge_commit=false -F allow_rebase_merge=false \\" \
  "-F allow_auto_merge=true -F delete_branch_on_merge=true \\" \
  "-f squash_merge_commit_title=PR_TITLE -f squash_merge_commit_message=PR_BODY"; do
  grep -qF -- "$line" "$readme" || fail "infra/README.md has no line: $line"
done
echo "ok  infra/README.md applies the ruleset file and the seven settings as the owner (-F booleans, -f strings)"

# --- the Forbidden terms step ---
step=$(yq '.jobs["floor-guard"].steps[] | select(.name == "Forbidden terms")' "$ci")
[[ -n "$step" ]] || fail "floor-guard has no Forbidden terms step"
[[ $(yq '.env.FORBIDDEN_TERMS' <<< "$step") == "\${{ secrets.FORBIDDEN_TERMS }}" ]] ||
  fail "Forbidden terms: FORBIDDEN_TERMS is not \${{ secrets.FORBIDDEN_TERMS }}"
[[ $(yq 'has("if") or has("continue-on-error")' <<< "$step") == false ]] ||
  fail "Forbidden terms: the step has an if: or continue-on-error:, so its failure could be skipped or ignored"
yq '.run' <<< "$step" > "$scratch/forbidden-terms.sh"
echo "ok  Forbidden terms: reads secrets.FORBIDDEN_TERMS, with no if: and no continue-on-error:"

# A scratch repository with one tracked file. The step greps the checkout it runs in.
repository="$scratch/repository"
git init -q "$repository"
printf 'first line\nthe word bravo is here\n' > "$repository/notes.txt"
git -C "$repository" add notes.txt

# run_step [<FORBIDDEN_TERMS value>]: runs the step as GitHub runs a step with no shell: on Linux, bash -e {0}
# (Workflow syntax, defaults.run.shell), with --noprofile --norc to keep this machine's bash start-up files out; with
# no argument the variable is unset.
run_step() {
  mkdir -p "$scratch/runner-temp"
  if [[ $# -eq 0 ]]; then
    (cd "$repository" && env -u FORBIDDEN_TERMS RUNNER_TEMP="$scratch/runner-temp" \
      bash --noprofile --norc -e "$scratch/forbidden-terms.sh")
  else
    (cd "$repository" && FORBIDDEN_TERMS=$1 RUNNER_TEMP="$scratch/runner-temp" \
      bash --noprofile --norc -e "$scratch/forbidden-terms.sh")
  fi
}

not_set='^::error::The FORBIDDEN_TERMS secret is not set'
expect 1 "$not_set" "Forbidden terms: an unset secret fails" run_step
expect 1 "$not_set" "Forbidden terms: an empty secret (a pull request from a fork) fails" run_step ''
expect 1 "$not_set" "Forbidden terms: a secret of comments and blank lines fails" run_step $'# a comment\n\n   \n'
expect 0 '^No forbidden terms\.$' "Forbidden terms: terms set, no match, passes" run_step $'# a comment\ncharlie'
expect 1 '^notes\.txt:2$' "Forbidden terms: a whole-word, case-insensitive match fails, naming file:line" \
  run_step 'BRAVO'
expect 1 '^notes\.txt:2$' "Forbidden terms: a term with a Windows line ending still matches" run_step $'bravo\r\n'
expect 0 '^No forbidden terms\.$' "Forbidden terms: a part of a word is not a match" run_step 'brav'
# Captured first, then searched: under pipefail, `run_step | grep` would take run_step's exit 1 on a match whatever
# grep found, so the check could never fail.
withheld=$(run_step 'bravo' 2>&1) || true
if grep -qi 'bravo' <<< "$withheld"; then fail "Forbidden terms: the matching text was printed: $withheld"; fi
echo "ok  Forbidden terms: the matching text is withheld"
echo "main-protection: all cases pass"
