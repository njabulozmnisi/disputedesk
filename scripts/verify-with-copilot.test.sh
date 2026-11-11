#!/usr/bin/env bash
# Proves scripts/verify-with-copilot.sh on a scratch repository with a story worktree, a stub copilot on PATH standing in
# for GPT-6 Astra: a spec the worktree lacks is refused before the model runs (exit 2); the spec reaches the model by
# its absolute path; the record lands in the main checkout's docs/verification/, never in the worktree; a second run
# adds a section instead of replacing the record.
#   scripts/verify-with-copilot.test.sh
# Needs bash and git; no network.
set -euo pipefail
scripts=$(cd "$(dirname "$0")" && pwd)

fail() { echo "VERIFY WITH COPILOT TEST FAIL: $*" >&2; exit 1; }
# expect <exit> <output pattern> <case> <directory> <argument...>: runs the script from the directory.
expect() {
  local want=$1 pattern=$2 name=$3 directory=$4 output status=0
  shift 4
  output=$(cd "$directory" && scripts/verify-with-copilot.sh "$@" 2>&1) || status=$?
  ((status == want)) || fail "$name: exit $status, want $want; output: $output"
  grep -Eq -- "$pattern" <<< "$output" || fail "$name: no '$pattern' in: $output"
  echo "ok  exit $want: $name"
}

scratch=$(cd "$(mktemp -d)" && pwd -P); trap 'git -C "$main" worktree prune 2> /dev/null; rm -rf "$scratch"' EXIT
export GIT_CONFIG_GLOBAL=/dev/null GIT_AUTHOR_NAME=t GIT_AUTHOR_EMAIL=t@example.invalid \
  GIT_COMMITTER_NAME=t GIT_COMMITTER_EMAIL=t@example.invalid DISPUTEDESK_LOCK_DIR="$scratch/locks"
main="$scratch/main"
git init -q -b main "$main"
mkdir -p "$main/scripts" "$main/docs/backlog"
cp "$scripts/verify-with-copilot.sh" "$scripts/diff-hash.sh" "$scripts/test-lock.sh" "$main/scripts/"
git -C "$main" add -A; git -C "$main" commit -q -m base
# The spec is written in the main checkout after the worktree was cut, so the worktree lacks it.
worktree="$scratch/worktree"
git -C "$main" worktree add -q --detach "$worktree" HEAD
echo "# a spec" > "$main/docs/backlog/story-x.md"
echo change > "$worktree/change.txt"

# The stub answers with a verdict and keeps the prompt it was given.
mkdir "$scratch/bin"
cat > "$scratch/bin/copilot" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$2" > "$STUB_PROMPT"
echo "VERDICT: PASS"
STUB
chmod +x "$scratch/bin/copilot"
export PATH="$scratch/bin:$PATH" STUB_PROMPT="$scratch/prompt"
record="$main/docs/verification/x-verify.md"

expect 2 'no spec at docs/backlog/story-x.md' "a spec the worktree lacks" "$worktree" x docs/backlog/story-x.md
[[ ! -e "$STUB_PROMPT" ]] || fail "the model ran without a spec"
[[ ! -e "$record" && ! -e "$worktree/docs/verification" ]] || fail "a record was written without a spec"
echo "ok  without a spec the model does not run and no record is written"

expect 0 "^$record\$" "the main checkout's spec, from the worktree" "$worktree" x "$main/docs/backlog/story-x.md"
grep -Fq "Story file: $main/docs/backlog/story-x.md." "$STUB_PROMPT" || fail "the prompt does not name the spec's absolute path"
[[ ! -e "$worktree/docs/verification" ]] || fail "a record was written into the worktree"
[[ $(grep -c '^VERDICT: PASS$' "$record") == 1 && $(head -n 1 "$record") == "# Verification: x" ]] ||
  fail "the record does not hold the verdict under its title: $(cat "$record")"
echo "ok  the record lands in the main checkout, with the verdict"

expect 0 "^$record\$" "a relative spec path, from the main checkout" "$main" x docs/backlog/story-x.md
grep -Fq "Story file: $main/docs/backlog/story-x.md." "$STUB_PROMPT" || fail "a relative spec path reached the model as given"
[[ $(grep -c '^VERDICT: PASS$' "$record") == 2 && $(grep -c '^# Verification: x$' "$record") == 1 ]] ||
  fail "the second run replaced the record instead of adding to it: $(cat "$record")"
grep -q '^## Copilot run$' "$record" || fail "the second run's section has no heading"
echo "ok  a second run adds its own section"
echo "verify-with-copilot: all cases pass"
