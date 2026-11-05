#!/usr/bin/env bash
# Proves scripts/run-breaks.sh on a scratch story worktree whose uncommitted change sets value.txt to 1: each break's
# test must pass without the break (the baseline) and fail with it. A stub dotnet on PATH stands in for the backend.
#   scripts/run-breaks.test.sh
# Needs bash, git and jq; no network, no Docker.
set -euo pipefail
scripts=$(cd "$(dirname "$0")" && pwd)

fail() { echo "RUN BREAKS TEST FAIL: $*" >&2; exit 1; }
# expect <exit> <output pattern> <case> <breaks JSON>: runs the script on a report holding the breaks.
expect() {
  local want=$1 pattern=$2 name=$3 output status=0
  jq -n --argjson breaks "$4" '{breaks: $breaks}' > "$scratch/build.json"
  output=$("$scripts/run-breaks.sh" "$story" "$scratch/build.json" 2> "$scratch/stderr") || status=$?
  ((status == want)) || fail "$name: exit $status, want $want; output: $output $(cat "$scratch/stderr")"
  grep -Eq "$pattern" <<< "$output$(cat "$scratch/stderr")" || fail "$name: no '$pattern' in: $output"
  echo "ok  exit $want: $name"
}
# A break: the patch that sets value.txt from 1 to $1.
break_to() { printf -- '--- a/value.txt\n+++ b/value.txt\n@@ -1 +1 @@\n-1\n+%s\n' "$1"; }

scratch=$(cd "$(mktemp -d)" && pwd -P); trap 'git -C "$story" worktree prune; rm -rf "$scratch"' EXIT
export GIT_CONFIG_GLOBAL=/dev/null GIT_AUTHOR_NAME=t GIT_AUTHOR_EMAIL=t@example.invalid \
  GIT_COMMITTER_NAME=t GIT_COMMITTER_EMAIL=t@example.invalid DISPUTEDESK_LOCK_DIR="$scratch/locks"
story="$scratch/story"
git init -q -b main "$story"
mkdir "$story/backend"; echo 0 > "$story/value.txt"; touch "$story/backend/DisputeDesk.slnx"
git -C "$story" add -A; git -C "$story" commit -q -m base
echo 1 > "$story/value.txt" # the story's uncommitted change, which the baseline needs

# The stub: build succeeds; test passes when value.txt holds 1, and records its arguments.
mkdir "$scratch/bin"
cat > "$scratch/bin/dotnet" <<'STUB'
#!/usr/bin/env bash
[[ "$1" == build ]] && exit 0
echo "$*" >> "$STUB_LOG"
[[ "$(cat ../value.txt)" == 1 ]]
STUB
chmod +x "$scratch/bin/dotnet"
export PATH="$scratch/bin:$PATH" STUB_LOG="$scratch/dotnet.log"

pass='grep -qx 1 value.txt'
good=$(jq -n --arg p "$(break_to 2)" --arg t "$pass" '[{patch: $p, test_method: $t, expect: "fails"}]')
expect 0 'ok    break 1 .*failed as expected' "a shell test that passes without the break and fails with it" "$good"

: > "$STUB_LOG"
dotnet=$(jq -n --arg p "$(break_to 2)" '[{patch: $p, test_method: "Linking.Start_rejects_x", expect: "fails"}]')
expect 0 'ok    break 1' "a dotnet filter that passes without the break and fails with it" "$dotnet"
[[ $(grep -c -- '--filter-method Linking.Start_rejects_x --filter-method \*.Linking.Start_rejects_x' "$STUB_LOG") == 2 ]] \
  || fail "the dotnet test did not run twice (baseline, break) with both filters: $(cat "$STUB_LOG")"
echo "ok  the dotnet test ran once without and once with the break"

red=$(jq -n --arg p "$(break_to 2)" '[{patch: $p, test_method: "grep -qx 9 value.txt", expect: "fails"}]')
expect 1 'FAIL  break 1 .*without the break: failed \(not counted\)' "a test that fails without the break is not counted" "$red"

both=$(jq -n --argjson a "$red" --argjson b "$good" '$a + $b')
expect 1 'ok    break 2' "a red baseline does not stop the other breaks" "$both"

weak=$(jq -n --arg p "$(break_to 1.0)" --arg t "grep -q 1 value.txt" '[{patch: $p, test_method: $t, expect: "fails"}]')
expect 1 'FAIL  break 1 .*with the break the test passed' "a test the break does not make fail" "$weak"

stale=$(jq -n --arg p "$(break_to 2 | sed 's/^-1$/-7/')" --arg t "$pass" '[{patch: $p, test_method: $t, expect: "fails"}]')
expect 1 'FAIL  break 1 .*does not apply' "a patch that does not apply" "$stale"

missing=$(jq -n --arg p "$(break_to 2)" '[{patch: $p, test_method: "no-such-command-here x", expect: "fails"}]')
expect 1 'FAIL  break 1 .*command not found' "a command that is not found" "$missing"

expect 2 'has no breaks' "a report with no breaks" '[]'
[[ "$(cat "$story/value.txt")" == 1 ]] && [[ -z "$(git -C "$story" worktree list | sed 1d)" ]] \
  || fail "the story worktree was changed or a copy was left behind"
echo "ok  the story worktree is untouched and no copy is left"
echo "run-breaks: all cases pass"
