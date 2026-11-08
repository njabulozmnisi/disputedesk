#!/usr/bin/env bash
# Checks one workflow's gate job (ad-012 clauses 1 and 3): the gate's needs must be every check job, and every check
# job must run unconditionally (no job-level if: or needs:). The check jobs are all the workflow's jobs except the gate
# and the jobs named after it, which run only on main after the checks. A check job left out of the gate would never
# block a merge once the gate is the required check, so this fails, naming each job that is wrong.
# Usage: scripts/ci-gates.sh <workflow file> <gate job> [<main-only job>...]
#        (CI's floor-guard job: ci.yml ci-passed publish; security.yml security-passed)
# Needs yq v4 (preinstalled on ubuntu-24.04). Exit codes: 0 ok; 1 a job is wrong; 2 usage, or no yq.
set -euo pipefail

[[ $# -ge 2 ]] || { echo "usage: scripts/ci-gates.sh <workflow file> <gate job> [<main-only job>...]" >&2; exit 2; }
command -v yq >/dev/null || { echo "ci-gates: yq v4 is required on PATH" >&2; exit 2; }
file=$1; export GATE=$2; shift 2
skip=("$GATE" "$@")
# Prints its input lines except the gate and the main-only jobs.
drop_skipped() { grep -vxF -f <(printf '%s\n' "${skip[@]}") || true; }

expected=$(yq '.jobs | keys | .[]' "$file" | drop_skipped | sort)
needs=$(yq '.jobs[strenv(GATE)].needs | ([] + .) | .[]' "$file" | sort)
conditional=$(yq '.jobs | to_entries | .[] | select(.value | (has("if") or has("needs"))) | .key' "$file" | drop_skipped)

failed=0
[[ $(yq '.jobs | has(strenv(GATE))' "$file") == true ]] || { echo "ci-gates: $file has no job $GATE"; failed=1; }
while IFS= read -r job; do
  [[ -n "$job" ]] && { echo "ci-gates: $file: a check job not in $GATE's needs: $job"; failed=1; }
done < <(comm -23 <(echo "$expected") <(echo "$needs"))
while IFS= read -r job; do
  [[ -n "$job" ]] && { echo "ci-gates: $file: in $GATE's needs but not a check job: $job"; failed=1; }
done < <(comm -13 <(echo "$expected") <(echo "$needs"))
while IFS= read -r job; do
  [[ -n "$job" ]] && { echo "ci-gates: $file: $job has a job-level if: or needs:, so it may not run"; failed=1; }
done <<< "$conditional"

((failed == 0)) || exit 1
echo "ci-gates: $file: $GATE needs every check job, and each runs unconditionally"
