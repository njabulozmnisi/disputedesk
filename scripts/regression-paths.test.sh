#!/usr/bin/env bash
# Proves the e2e-regression job (docs/backlog/story-ci-e2e-regression.md):
# - every scripts/*.sh path named under e2e/ is a line of scripts/regression-paths.list;
# - scripts/regression-paths.sh on a scratch repository: every line of the list, the near misses, renames both ways, a
#   deletion, a branch behind its base, and exit 2 for bad input (criterion 1);
# - the job's shape in ci.yml, and its decide step, read from the workflow and run as GitHub runs a bash step, on each
#   exit status of a stub (criteria 2 and 3);
# - the comments (criterion 5) and the AD-32 amendment (criterion 6).
#   scripts/regression-paths.test.sh
# Needs bash, git and yq (v4); no network, no Docker.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
paths="$root/scripts/regression-paths.sh"
list="$root/scripts/regression-paths.list"
ci="$root/.github/workflows/ci.yml"

command -v yq > /dev/null || { echo "regression-paths.test: yq v4 is required on PATH" >&2; exit 2; }
fail() { echo "REGRESSION PATHS TEST FAIL: $*" >&2; exit 1; }
# expect <exit> <case> <command...>: runs the command and checks its exit code.
expect() {
  local want=$1 name=$2 output status=0
  shift 2
  output=$("$@" 2>&1) || status=$?
  ((status == want)) || fail "$name: exit $status, want $want; output: $output"
  echo "ok  exit $want: $name"
}

scratch=$(mktemp -d); trap 'rm -rf "$scratch"' EXIT

# --- the list holds one path per line and nothing else ---
if grep -nE '^$|^#|[[:space:]]' "$list"; then fail "the list holds a blank line, a comment or a space"; fi
echo "ok  the list holds one path per line"

# --- every scripts/*.sh named under e2e/ is a line of the list, so a change to it starts the suite ---
# git grep skips the ignored node_modules and test output; --untracked includes a new file. Finding none means the search broke.
named=$(git -C "$root" grep --untracked -hoE 'scripts/[A-Za-z0-9._-]+\.sh' -- e2e | sort -u) || fail "found no scripts/*.sh path under e2e/"
while IFS= read -r script; do
  grep -Fxq -- "$script" "$list" || fail "$script is named under e2e/ but is not a line of the list, so a change to it skips the suite"
done <<< "$named"
echo "ok  every scripts/*.sh named under e2e/ is a line of the list"

# --- scripts/regression-paths.sh on a scratch repository ---
export GIT_CONFIG_GLOBAL=/dev/null GIT_AUTHOR_NAME=t GIT_AUTHOR_EMAIL=t@example.invalid \
  GIT_COMMITTER_NAME=t GIT_COMMITTER_EMAIL=t@example.invalid
repo="$scratch/repo"
git init -q -b main "$repo"
cd "$repo"
mkdir docs frontend
echo "a file that moves into a matching path" > docs/moves-in
echo "a file that moves out of a matching path" > frontend/moves-out
echo "a file that is deleted" > frontend/deleted
git add -A
git commit -q -m base
base=$(git rev-parse HEAD)

# branch: starts a commit on a detached HEAD at the base, so every case diffs only its own change.
branch() { git checkout -q --detach "$base"; }
commit() { git add -A; git commit -q -m change; }
# changed <path>: a commit that adds this one path.
changed() {
  branch
  mkdir -p "$(dirname "$1")"
  echo change > "$1"
  commit
}

while IFS= read -r line; do
  path=$line
  [[ $line != */ ]] || path="${line}deep/file"
  changed "$path"
  expect 0 "the line $line: $path" "$paths" "$base" HEAD
done < "$list"

# Built from a variable: scripts/check-infra-paths.sh reads every infra/ path this file names as one that must exist.
collector=infra/images/otel-collector
for path in frontendx/x e2e.md docs/x "$collector/x" .github/actions/install-trivy/x \
  scripts/regression-paths.test.sh docker-compose.yml.bak; do
  changed "$path"
  expect 1 "a near miss: $path" "$paths" "$base" HEAD
done

branch
git mv docs/moves-in frontend/moves-in
commit
expect 0 "a rename into a matching path" "$paths" "$base" HEAD

branch
git mv frontend/moves-out docs/moves-out
commit
expect 0 "a rename out of a matching path" "$paths" "$base" HEAD

branch
git rm -q frontend/deleted
commit
expect 0 "a deletion" "$paths" "$base" HEAD

# git quotes a name like this one in plain --name-only output, so a match on the quoted text would miss it.
changed "frontend/caf$(printf '\303\251').txt"
expect 0 "a path git would quote" "$paths" "$base" HEAD

# The base moves on with a matching change; the branch, cut before it, changes only docs/.
changed docs/on-the-branch
behind=$(git rev-parse HEAD)
changed frontend/on-the-base
expect 1 "a branch behind its base: the base's own changes do not count" "$paths" HEAD "$behind"

branch
expect 1 "no change at all" "$paths" "$base" "$base"

expect 2 "no arguments" "$paths"
expect 2 "one argument" "$paths" "$base"
expect 2 "three arguments" "$paths" "$base" HEAD HEAD
expect 2 "an unknown base" "$paths" 0000000000000000000000000000000000000001 HEAD
expect 2 "an unknown head" "$paths" "$base" 0000000000000000000000000000000000000001
expect 2 "an empty base" "$paths" "" HEAD
expect 2 "an empty head" "$paths" "$base" ""
expect 2 "an option in place of a commit" "$paths" --output=written HEAD

git checkout -q --orphan unrelated
commit
expect 2 "commits with no merge base" "$paths" "$base" HEAD

mkdir "$scratch/no-list"
cp "$paths" "$scratch/no-list/"
expect 2 "a missing list" "$scratch/no-list/regression-paths.sh" "$base" "$base"
cd "$root"

# --- the job's shape in ci.yml (criteria 2 and 3) ---
# step_if <name, run: text, or action without its @ pin>: prints the step's if.
step_if() { STEP=$1 yq '.jobs.e2e-regression.steps[] | select(.name == strenv(STEP) or .run == strenv(STEP) or ((.uses // "") | split("@") | .[0]) == strenv(STEP)) | .if' "$ci"; }
job() { yq ".jobs.e2e-regression$1" "$ci"; }

[[ $(yq '.jobs | has("e2e-regression")' "$ci") == true ]] || fail "ci.yml has no e2e-regression job"
[[ $(job ' | has("if")') == false ]] || fail "e2e-regression has a job-level if"
[[ $(job ' | has("needs")') == false ]] || fail "e2e-regression has a job-level needs"
[[ $(job ' | has("permissions")') == false ]] || fail "e2e-regression sets its own permissions"
[[ $(job '.timeout-minutes') == 60 ]] || fail "e2e-regression's timeout is not 60 minutes"
[[ $(job '.steps[0].uses') == actions/checkout@* ]] || fail "e2e-regression's first step is not the checkout"
[[ $(job '.steps[0].with.fetch-depth') == 0 ]] || fail "e2e-regression's checkout has no fetch-depth: 0"
[[ $(job '.steps[0].with.persist-credentials') == false ]] || fail "e2e-regression's checkout keeps its credentials"
[[ $(job '.steps[1].id') == decide ]] || fail "the step after the checkout is not decide"
[[ $(job '.steps[1] | has("if")') == false ]] || fail "decide has an if, so it may not write run"
[[ $(job '.steps[1].env.BASE_SHA') == "\${{ github.event.pull_request.base.sha }}" ]] || fail "BASE_SHA is not the base's SHA"
[[ $(job '.steps[1].env.HEAD_SHA') == "\${{ github.event.pull_request.head.sha }}" ]] || fail "HEAD_SHA is not the head's SHA"
[[ $(job '.steps[1].run') != *"\${{"* ]] || fail "decide's script holds \${{ }}"
[[ $(yq '[.. | select(tag == "!!map") | select(has("continue-on-error"))] | length' "$ci") == 0 ]] || fail "ci.yml has a continue-on-error"
if grep -Eq '^[[:space:]]*retries:' "$root/e2e/playwright.config.ts"; then fail "playwright.config.ts sets retries"; fi
[[ $(yq -o=json -I=0 '.jobs.ci-passed.needs | contains(["e2e-regression"])' "$ci") == true ]] || fail "ci-passed does not need e2e-regression"
[[ $(yq -o=json -I=0 '.jobs.publish.needs | contains(["e2e-regression"])' "$ci") == false ]] || fail "publish needs e2e-regression"
echo "ok  the job: no if or needs, 60 minutes, full checkout, decide second, SHAs through env, in ci-passed, not in publish"

gated="steps.decide.outputs.run == 'true'"
for step in ./.github/actions/install-compose actions/setup-node 'npm ci' \
  'npx playwright install --with-deps chromium' e2e/run-regression.sh; do
  [[ $(step_if "$step") == "$gated" ]] || fail "$step does not run only on run == 'true'"
done
[[ $(step_if 'Stack diagnostics') == "failure() && $gated" ]] || fail "Stack diagnostics is not failure() && run == 'true'"
[[ $(step_if 'Stop the stack') == "always() && $gated" ]] || fail "Stop the stack is not always() && run == 'true'"
diagnostics=$(STEP='Stack diagnostics' yq '.jobs.e2e-regression.steps[] | select(.name == strenv(STEP)) | .run' "$ci")
for command in 'docker compose ps -a' 'docker compose logs --no-color --tail 200' 'error-context.md'; do
  [[ $diagnostics == *"$command"* ]] || fail "Stack diagnostics does not run $command"
done
[[ $(STEP='Stop the stack' yq '.jobs.e2e-regression.steps[] | select(.name == strenv(STEP)) | .run' "$ci") == 'docker compose down -v' ]] ||
  fail "Stop the stack does not run docker compose down -v"
echo "ok  the suite's steps run only on run == 'true'; diagnostics on failure; the stack always stops"

[[ $(yq -o=json -I=0 '.jobs.floor-guard.steps[].run' "$ci" | grep -n '^"scripts/guard.test.sh"$' | cut -d: -f1) -lt \
  $(yq -o=json -I=0 '.jobs.floor-guard.steps[].run' "$ci" | grep -n '^"scripts/regression-paths.test.sh"$' | cut -d: -f1) ]] ||
  fail "floor-guard does not run this test after scripts/guard.test.sh"
echo "ok  floor-guard runs this test after scripts/guard.test.sh"

# --- the decide step, run as GitHub runs a bash step (ci-gates.test.sh explains the flags) ---
decide_dir="$scratch/decide"
mkdir -p "$decide_dir/scripts"
yq '.jobs.e2e-regression.steps[1].run' "$ci" > "$decide_dir/decide.sh"
# The stub records its arguments and exits with the status the case asks for.
cat > "$decide_dir/scripts/regression-paths.sh" <<'STUB'
#!/usr/bin/env bash
echo "$*" > called
exit "$STUB_STATUS"
STUB
chmod +x "$decide_dir/scripts/regression-paths.sh"

# decide <event> <stub status>: runs the step; the output file and the log are left for the checks.
decide() {
  rm -f "$decide_dir/output" "$decide_dir/called"
  (cd "$decide_dir" && GITHUB_EVENT_NAME=$1 STUB_STATUS=$2 GITHUB_OUTPUT="$decide_dir/output" BASE_SHA=base-sha HEAD_SHA=head-sha \
    bash --noprofile --norc -e decide.sh) > "$decide_dir/log" 2>&1
}
wrote() { [[ $(cat "$decide_dir/output" 2>/dev/null) == "$1" ]] || fail "$2: GITHUB_OUTPUT is not '$1'"; }

decide pull_request 0 || fail "pull request, status 0: the step failed"
wrote run=true "pull request, status 0"
[[ $(cat "$decide_dir/called") == "base-sha head-sha" ]] || fail "the script did not get BASE_SHA and HEAD_SHA"
echo "ok  decide: status 0 writes run=true, with both SHAs passed"

decide pull_request 1 || fail "pull request, status 1: the step failed"
wrote run=false "pull request, status 1"
grep -q '^::notice::' "$decide_dir/log" || fail "pull request, status 1: no notice"
echo "ok  decide: status 1 writes run=false with a notice"

for status in 2 3 127 128; do
  if decide pull_request "$status"; then fail "pull request, status $status: the step passed"; fi
  wrote "" "pull request, status $status"
  grep -q '^::error::' "$decide_dir/log" || fail "pull request, status $status: no error"
  echo "ok  decide: status $status fails the step and writes no run"
done

decide push 2 || fail "push: the step failed"
wrote run=false "push"
[[ ! -e "$decide_dir/called" ]] || fail "push: the script ran"
grep -q '^::notice::.*squash commit' "$decide_dir/log" || fail "push: the notice does not name the squash commit"
echo "ok  decide: a push skips the script and writes run=false with the reason"

# --- the comments say what runs (criterion 5) ---
if grep -Fq 'the regression project is not run in CI' "$ci"; then fail "the e2e comment still says the regression project is not run"; fi
comment=$(sed -n '/^  # .*scripts\/regression-paths.list/,/^  e2e-regression:/p' "$ci")
for text in scripts/regression-paths.list 'retries stay 0' 'D budget' "publish's needs" scalar-disabled.spec.ts SCALAR_OFF=1 'wall time'; do
  grep -Fq -- "$text" <<< "$comment" || fail "the e2e-regression comment does not name: $text"
done
echo "ok  the comments say what runs"

# --- AD-32 names e2e-regression as the one path-scoped suite (criterion 6) ---
docs_failures=()
ad32=$(sed -n '/^### AD-32/,/^### /p' "$root/docs/architecture/architecture.md")
for text in e2e-regression path-scoped scripts/regression-paths.list A3; do
  grep -Fq -- "$text" <<< "$ad32" || docs_failures+=("AD-32's section does not name $text")
done
grep -E '^\| AD-32 \| 2026-11-06 \|' "$root/docs/architecture/spine-amendments.md" | grep -Fq e2e-regression ||
  docs_failures+=("spine-amendments.md has no AD-32 row of 2026-11-06 naming e2e-regression")
if ((${#docs_failures[@]} > 0)); then fail "$(printf '%s; ' "${docs_failures[@]}")"; fi
echo "ok  AD-32 and its amendment row name the path-scoped suite"
echo "regression-paths: all cases pass"
