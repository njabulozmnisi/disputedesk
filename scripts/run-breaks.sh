#!/usr/bin/env bash
# Runs a builder's deliberate breaks: each break's test must pass without the break and fail with it, which proves the
# test guards the code it names (the mutation-testing baseline: a test that already fails proves nothing).
# The breaks are the `breaks` list of the builder's report (build-story skill): {"patch": "<unified diff>",
# "test_method": "...", "expect": "fails..."}. The story worktree is never touched: its uncommitted change is carried
# to a throwaway worktree at its HEAD with story-patch.sh, and each break is applied there, run and reverted.
#   test_method  a backend test name or --filter-method pattern (letters, digits, _ . * only) runs
#                `dotnet test --filter-method` over backend/DisputeDesk.slnx under the testcontainers lock; anything
#                else is a shell command run from the copy's root (for example infra/aws/deploy.test.sh).
# A break counts only when its test passes without it, its patch applies, the backend still builds, and the test then
# fails. A break whose test does not pass without it is reported and not counted. A command that is not found (exit
# 127) or a lock wait that timed out (75) is not a result of the test.
# Usage: scripts/run-breaks.sh <worktree> <build.json>
# Exit codes: 0 every break failed as expected; 1 a break did not; 2 usage or tooling.
set -euo pipefail

[[ $# -eq 2 ]] || { echo "usage: scripts/run-breaks.sh <worktree> <build.json>" >&2; exit 2; }
worktree=$1; report=$2
scripts=$(cd "$(dirname "$0")" && pwd)
[[ -d "$worktree" ]] || { echo "run-breaks: no worktree at $worktree" >&2; exit 2; }
count=$(jq -e '.breaks | arrays | length | select(. > 0)' "$report") \
  || { echo "run-breaks: $report has no breaks to run" >&2; exit 2; }
worktree=$(git -C "$worktree" rev-parse --show-toplevel)
owner="breaks-$(basename "$worktree")"

# The physical path: macOS's $TMPDIR is under the /var symlink, and the compiler then matches no .editorconfig section.
scratch=$(cd "$(mktemp -d)" && pwd -P)
copy="$scratch/copy"
cleanup() { git -C "$worktree" worktree remove --force "$copy" 2>/dev/null || true; rm -rf "$scratch"; }
trap cleanup EXIT

"$scripts/story-patch.sh" "$worktree" HEAD "$scratch/story.patch" > /dev/null || exit 2
git -C "$worktree" worktree add --quiet --detach "$copy" HEAD
git -C "$copy" apply --binary "$scratch/story.patch"

# Runs one test ($1, output appended to $2) in the copy and prints "passed", "failed", or why it could not run.
run_test() {
  local test=$1 log=$2 status=0
  if [[ "$test" =~ ^[A-Za-z0-9_.*]+$ ]]; then
    # From backend/, as in CI: dotnet reads global.json, which selects Microsoft.Testing.Platform, from the working
    # directory. xUnit v3 matches --filter-method against the fully qualified name, and ORs repeated filters, so the
    # name is passed as given and as *.<name> (a bare Method or Class.Method). --ignore-exit-code 8 after `--` goes to
    # every test module, so the modules with no matching test do not fail the run (Microsoft Learn, "dotnet test with
    # Microsoft.Testing.Platform"); a filter matching nothing passes, and is reported as such.
    (cd "$copy/backend" && dotnet build DisputeDesk.slnx) >> "$log" 2>&1 || { echo "the backend does not build"; return; }
    (cd "$copy/backend" && "$scripts/test-lock.sh" run testcontainers "$owner" -- \
      dotnet test --solution DisputeDesk.slnx --no-build --filter-method "$test" --filter-method "*.$test" \
      -- --ignore-exit-code 8) >> "$log" 2>&1 || status=$?
  else
    (cd "$copy" && bash -c "$test") >> "$log" 2>&1 || status=$?
  fi
  case $status in
    0) echo "passed" ;;
    75) echo "timed out waiting for the testcontainers lock" ;; # test-lock.sh's exit code
    127) echo "command not found" ;;
    *) echo "failed" ;;
  esac
}

failed=0
for ((i = 0; i < count; i++)); do
  test=$(jq -r ".breaks[$i].test_method // empty" "$report")
  expect=$(jq -r ".breaks[$i].expect // empty" "$report")
  jq -r ".breaks[$i].patch // empty" "$report" > "$scratch/break.patch"
  log="$scratch/break-$i.log"
  name="break $((i + 1)) ($test)"

  if [[ -z "$test" || "$expect" != fails* ]]; then
    echo "FAIL  $name: needs a test_method and an expect starting \"fails\""; failed=1; continue
  fi
  baseline=$(run_test "$test" "$log")
  if [[ "$baseline" != passed ]]; then
    echo "FAIL  $name: without the break: $baseline (not counted)"; tail -n 20 "$log" >&2; failed=1; continue
  fi
  if ! git -C "$copy" apply "$scratch/break.patch" 2>> "$log"; then
    echo "FAIL  $name: the patch does not apply: $(tail -n 1 "$log")"; failed=1; continue
  fi
  result=$(run_test "$test" "$log")
  git -C "$copy" apply -R "$scratch/break.patch" || { echo "run-breaks: could not revert $name" >&2; exit 2; }

  case $result in
    failed) echo "ok    $name: failed as expected" ;;
    passed) echo "FAIL  $name: with the break the test passed, or the filter matched no test"; failed=1 ;;
    *) echo "FAIL  $name: with the break: $result"; tail -n 20 "$log" >&2; failed=1 ;;
  esac
done
((failed == 0)) || exit 1
