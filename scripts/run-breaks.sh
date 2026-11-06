#!/usr/bin/env bash
# Runs a builder's deliberate breaks: each break's test must pass without the break and fail with it, which proves the
# test guards the code it names (the mutation-testing baseline: a test that already fails proves nothing).
# The breaks are the `breaks` list of the builder's report (build-story skill): {"patch": "<unified diff>",
# "test_method": "...", "expect": "fails..."}. The story worktree is never touched: its uncommitted change is carried
# to a throwaway worktree at its HEAD with story-patch.sh, and each break is applied there, run and reverted.
#   test_method  a test name or --filter-method pattern (letters, digits, _ . * only) runs `dotnet test --filter-method`:
#                one in the DisputeDesk.PlatformTests namespace over infra/tests' DisputeDesk.PlatformTests project
#                under the stack lock (its GatewayTests start containers directly, outside Testcontainers); any other
#                over backend/DisputeDesk.slnx under the testcontainers lock. Anything else is a shell command run from
#                the copy's root (for example scripts/ci-gates.test.sh).
# Every patch is checked with `git apply --check` before any test runs. A break counts only when its patch applies,
# its test passes without it, the tests still build, and the test then fails. A break whose test fails without it is
# reported as FAIL and not counted. ERROR marks a break with no result of its test, also not counted: a patch that does
# not apply, a filter that matches no test, a build that fails, a command that is not found (exit 127) or a lock wait
# that timed out (75).
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
  local test=$1 log=$2 status=0 dir option target lock
  if [[ ! "$test" =~ ^[A-Za-z0-9_.*]+$ ]]; then
    (cd "$copy" && bash -c "$test") >> "$log" 2>&1 || status=$?
    case $status in
      0) echo "passed" ;;
      127) echo "command not found" ;;
      *) echo "failed" ;;
    esac
    return
  fi

  if [[ "$test" == DisputeDesk.PlatformTests.* ]]; then
    dir=infra/tests; option=--project; target=DisputeDesk.PlatformTests; lock=stack
  else
    dir=backend; option=--solution; target=DisputeDesk.slnx; lock=testcontainers
  fi
  # From $dir, as in CI: dotnet reads global.json, which selects Microsoft.Testing.Platform, from the working
  # directory. xUnit v3 matches --filter-method against the fully qualified name, and ORs repeated filters, so the
  # name is passed as given and as *.<name> (a bare Method or Class.Method). --minimum-expected-tests 1 before `--`
  # counts the whole run and exits 9 when the filters match no test; --ignore-exit-code 8 after `--` goes to every
  # test module, so a module with no matching test does not fail the run (Microsoft Learn, "dotnet test with
  # Microsoft.Testing.Platform", Whole-run and per-module minimums; "MTP troubleshooting", Exit codes).
  (cd "$copy/$dir" && dotnet build "$target") >> "$log" 2>&1 || { echo "$dir does not build"; return; }
  (cd "$copy/$dir" && "$scripts/test-lock.sh" run "$lock" "$owner" -- \
    dotnet test "$option" "$target" --no-build --minimum-expected-tests 1 \
    --filter-method "$test" --filter-method "*.$test" -- --ignore-exit-code 8) >> "$log" 2>&1 || status=$?
  case $status in
    0) echo "passed" ;;
    9) echo "the filter matched no test" ;;
    75) echo "timed out waiting for the $lock lock" ;; # test-lock.sh's exit code
    *) echo "failed" ;;
  esac
}

# Every patch is checked against the copy before any test runs, so a refused patch is reported up front.
failed=0
refused=()
for ((i = 0; i < count; i++)); do
  jq -r ".breaks[$i].patch // empty" "$report" > "$scratch/break-$i.patch"
  if ! git -C "$copy" apply --check "$scratch/break-$i.patch" 2> "$scratch/check-$i.log"; then
    refused[i]=1; failed=1
    test=$(jq -r ".breaks[$i].test_method // empty" "$report")
    echo "ERROR break $((i + 1)) ($test): the patch does not apply: $(tail -n 1 "$scratch/check-$i.log")"
  fi
done

for ((i = 0; i < count; i++)); do
  [[ -z "${refused[i]:-}" ]] || continue
  test=$(jq -r ".breaks[$i].test_method // empty" "$report")
  expect=$(jq -r ".breaks[$i].expect // empty" "$report")
  patch="$scratch/break-$i.patch"
  log="$scratch/break-$i.log"
  name="break $((i + 1)) ($test)"

  if [[ -z "$test" || "$expect" != fails* ]]; then
    echo "FAIL  $name: needs a test_method and an expect starting \"fails\""; failed=1; continue
  fi
  baseline=$(run_test "$test" "$log")
  if [[ "$baseline" == failed ]]; then
    echo "FAIL  $name: without the break: failed (not counted)"; tail -n 20 "$log" >&2; failed=1; continue
  fi
  if [[ "$baseline" != passed ]]; then
    echo "ERROR $name: without the break: $baseline (not counted)"; tail -n 20 "$log" >&2; failed=1; continue
  fi
  if ! git -C "$copy" apply "$patch" 2>> "$log"; then
    echo "ERROR $name: the patch does not apply: $(tail -n 1 "$log")"; failed=1; continue
  fi
  result=$(run_test "$test" "$log")
  git -C "$copy" apply -R "$patch" || { echo "run-breaks: could not revert $name" >&2; exit 2; }

  case $result in
    failed) echo "ok    $name: failed as expected" ;;
    passed) echo "FAIL  $name: with the break the test passed"; failed=1 ;;
    *) echo "ERROR $name: with the break: $result"; tail -n 20 "$log" >&2; failed=1 ;;
  esac
done
((failed == 0)) || exit 1
