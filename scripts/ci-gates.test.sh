#!/usr/bin/env bash
# Proves the two gate jobs (ad-012 clauses 1 and 3):
# - scripts/ci-gates.sh, on scratch workflows: it names a check job missing from the gate's needs, a job in the needs
#   that is not a check job, and a check job with its own if: or needs:;
# - ci.yml and security.yml pass it with the arguments CI's floor-guard job passes;
# - each gate's own settings (needs, if: always(), timeout, no permissions, NEEDS from toJSON(needs));
# - each gate's run: script, read from the workflow and run as GitHub runs a bash step, on sample needs contexts: it
#   succeeds only when every result is success, and names each job that is not;
# - publish keeps its if: and needs, and each local install action (ad-012 clause 5) runs in the jobs that need it,
#   after their checkout (a misplaced step in publish would otherwise fail only on main).
#   scripts/ci-gates.test.sh
# Needs bash, yq (v4) and jq; no network.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
gates="$root/scripts/ci-gates.sh"

fail() { echo "CI GATES TEST FAIL: $*" >&2; exit 1; }
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

# --- scripts/ci-gates.sh on scratch workflows ---
workflow() { # workflow <name> <jobs YAML>: writes a workflow whose jobs block is the given YAML.
  printf 'name: t\non: push\njobs:\n%s\n' "$2" > "$scratch/$1.yml"
  echo "$scratch/$1.yml"
}
job='    runs-on: ubuntu-24.04
    steps:
      - run: "true"'

good=$(workflow good "  a:
$job
  b:
$job
  publish:
    needs: [a, b]
    if: github.ref == 'refs/heads/main'
$job
  gate:
    needs: [a, b]
    if: always()
$job")
expect 0 'needs every check job' "the gate needs every check job" "$gates" "$good" gate publish

missing=$(workflow missing "  a:
$job
  b:
$job
  gate:
    needs: [a]
    if: always()
$job")
expect 1 'not in gate.s needs: b' "a check job left out of the gate is named" "$gates" "$missing" gate

extra=$(workflow extra "  a:
$job
  publish:
$job
  gate:
    needs: [a, publish]
    if: always()
$job")
expect 1 'not a check job: publish' "a main-only job in the gate's needs is named" "$gates" "$extra" gate publish

conditional=$(workflow conditional "  a:
    if: github.event_name == 'push'
$job
  b:
    needs: a
$job
  gate:
    needs: [a, b]
    if: always()
$job")
expect 1 'a has a job-level if: or needs:' "a check job with its own if: is named" "$gates" "$conditional" gate
expect 1 'b has a job-level if: or needs:' "a check job with its own needs: is named" "$gates" "$conditional" gate

expect 1 'no job gate' "a missing gate fails" "$gates" "$(workflow nogate "  a:
$job")" gate
expect 2 'usage' "no arguments is a usage error" "$gates"

# --- the real workflows, with floor-guard's arguments ---
ci="$root/.github/workflows/ci.yml"
security="$root/.github/workflows/security.yml"
expect 0 'needs every check job' "ci.yml: ci-passed needs every check job" "$gates" "$ci" ci-passed publish
expect 0 'needs every check job' "security.yml: security-passed needs every check job" \
  "$gates" "$security" security-passed

# --- each gate's own settings ---
settings() { # settings <workflow> <gate> <needs as a JSON array>
  local file=$1
  export GATE=$2
  [[ $(yq -o=json -I=0 '.jobs[strenv(GATE)].needs' "$file") == "$3" ]] || fail "$GATE: needs is not $3"
  [[ $(yq '.jobs[strenv(GATE)].if' "$file") == 'always()' ]] || fail "$GATE: if is not always()"
  [[ $(yq '.jobs[strenv(GATE)].timeout-minutes' "$file") == 5 ]] || fail "$GATE: timeout-minutes is not 5"
  [[ $(yq '.jobs[strenv(GATE)] | has("permissions")' "$file") == false ]] || fail "$GATE: sets permissions"
  [[ $(yq '.jobs[strenv(GATE)].steps | length' "$file") == 1 ]] || fail "$GATE: has more than one step"
  # shellcheck disable=SC2016 # the expression is GitHub's, not the shell's
  [[ $(yq '.jobs[strenv(GATE)].steps[0].env.NEEDS' "$file") == '${{ toJSON(needs) }}' ]] ||
    fail "$GATE: NEEDS is not \${{ toJSON(needs) }}"
  [[ $(yq '.jobs[strenv(GATE)].steps[0].run' "$file") != *"\${{"* ]] || fail "$GATE: its script holds \${{ }}"
  echo "ok  $GATE: needs $3, if: always(), timeout 5, no permissions, NEEDS from toJSON(needs)"
}
settings "$ci" ci-passed '["backend","frontend","platform","terraform","floor-guard","e2e"]'
settings "$security" security-passed '["gitleaks","codeql","vulnerable-packages","trivy"]'

# --- publish keeps its if: and needs (criterion 6) ---
[[ $(yq '.jobs.publish.if' "$ci") == "github.event_name == 'push' && github.ref == 'refs/heads/main'" ]] ||
  fail "publish: its if: changed"
[[ $(yq -o=json -I=0 '.jobs.publish.needs' "$ci") == '["backend","frontend","platform","floor-guard","e2e"]' ]] ||
  fail "publish: its needs changed"
echo "ok  publish: if and needs unchanged"

# --- each local install action runs in the jobs that need it, after their checkout ---
uses_after_checkout() { # uses_after_checkout <workflow> <action path> <the jobs that must use it, sorted, space-separated>
  local file=$1 action=$2 want=$3 users job uses checkout step
  users=$(ACTION=$action yq '.jobs | to_entries | .[] | select(.value.steps[].uses == strenv(ACTION)) | .key' "$file" |
    sort -u | xargs)
  [[ "$users" == "$want" ]] || fail "$action is used by '$users', want '$want'"
  for job in $want; do
    uses=$(JOB=$job yq '.jobs[strenv(JOB)].steps[] | .uses // ""' "$file") # one line per step, empty for a run: step
    checkout=$(grep -n '^actions/checkout@' <<< "$uses" | head -1 | cut -d: -f1)
    step=$(grep -nxF "$action" <<< "$uses" | head -1 | cut -d: -f1)
    [[ -n "$checkout" && "$checkout" -lt "$step" ]] || fail "$job: $action is not after its checkout"
  done
  echo "ok  $action runs after the checkout in: $want"
}
uses_after_checkout "$ci" ./.github/actions/install-compose "backend e2e platform"
uses_after_checkout "$ci" ./.github/actions/install-trivy "publish"
uses_after_checkout "$security" ./.github/actions/install-trivy "trivy"

# --- each gate's run: script on sample needs contexts ---
# GitHub runs a bash step as bash --noprofile --norc -eo pipefail {0} (Workflow syntax, defaults.run.shell).
script() { # script <workflow> <gate>: writes the gate's run: script to a file and prints its path.
  GATE=$2 yq '.jobs[strenv(GATE)].steps[0].run' "$1" > "$scratch/$2.sh"
  echo "$scratch/$2.sh"
}
run_gate() { # run_gate <script> <needs JSON>
  NEEDS=$2 bash --noprofile --norc -eo pipefail "$1"
}
result() { printf '"%s":{"result":"%s","outputs":{}}' "$1" "$2"; }
with_result() { jq -c --arg job "$2" --arg result "$3" '.[$job].result = $result' <<< "$1"; } # <needs> <job> <result>

ci_gate=$(script "$ci" ci-passed)
all_success="{$(result backend success),$(result frontend success),$(result platform success),$(result terraform success),$(result floor-guard success),$(result e2e success)}"
expect 0 'Every check job succeeded' "ci-passed: every job succeeded" run_gate "$ci_gate" "$all_success"
for bad in failure cancelled skipped; do
  needs=$(with_result "$all_success" e2e "$bad")
  expect 1 "e2e: $bad" "ci-passed: e2e $bad fails, naming it" run_gate "$ci_gate" "$needs"
done
two_bad="{$(result backend failure),$(result frontend success),$(result platform cancelled),$(result e2e success)}"
expect 1 'backend: failure' "ci-passed: two bad jobs, the first named" run_gate "$ci_gate" "$two_bad"
expect 1 'platform: cancelled' "ci-passed: two bad jobs, the second named" run_gate "$ci_gate" "$two_bad"
# jq exits non-zero on input that is not JSON, and the step's -e stops it there: the gate fails closed.
if run_gate "$ci_gate" 'not json' > /dev/null 2>&1; then fail "ci-passed: a NEEDS that is not JSON passed"; fi
echo "ok  exit non-zero: ci-passed: a NEEDS that is not JSON fails"

security_gate=$(script "$security" security-passed)
security_ok="{$(result gitleaks success),$(result codeql success),$(result vulnerable-packages success),$(result trivy success)}"
expect 0 'Every check job succeeded' "security-passed: every job succeeded" run_gate "$security_gate" "$security_ok"
# A codeql leg that fails while the other succeeds: the matrix's combined result is failure (observed, not documented).
expect 1 'codeql: failure' "security-passed: codeql failure fails, naming it" \
  run_gate "$security_gate" "$(with_result "$security_ok" codeql failure)"
expect 1 'trivy: skipped' "security-passed: trivy skipped fails, naming it" \
  run_gate "$security_gate" "$(with_result "$security_ok" trivy skipped)"
echo "ci-gates: all cases pass"
