#!/usr/bin/env bash
# Proves the deploy wiring (ad-012 clause 4) on the workflow files, with no network and no AWS call:
# - publish's build step writes the five image references to GITHUB_OUTPUT and the job declares them as outputs; a
#   reference that is not ghcr.io/njabulozmnisi/disputedesk-<name>@sha256:<64 hex> fails the step;
# - deploy.yml runs only on workflow_dispatch, with sha; its one job is in the environment dev, in the concurrency group
#   deploy-dev, on main only; its SHA check fails on a value out of bounds, and its images step resolves the five
#   references from GHCR and fails on an image built from another commit;
# - no CI job calls deploy.yml: every deploy is dispatched by hand.
# Each run: step is read from its workflow and run as GitHub runs a step with no shell: (bash -e {0}; Workflow syntax,
# defaults.run.shell), with --noprofile --norc to keep this machine's bash start-up files out, and docker and trivy
# stubbed.
#   scripts/deploy-workflow.test.sh
# Needs bash, yq (v4) and jq.
set -euo pipefail
shopt -s nullglob # a workflow directory need not hold both *.yml and *.yaml
root=$(cd "$(dirname "$0")/.." && pwd)
ci="$root/.github/workflows/ci.yml"
deploy="$root/.github/workflows/deploy.yml"

fail() { echo "DEPLOY WORKFLOW TEST FAIL: $*" >&2; exit 1; }
# expect <exit> <output pattern> <case> <command...>: runs the command and checks its exit code and output.
expect() {
  local want=$1 pattern=$2 name=$3 output status=0
  shift 3
  output=$("$@" 2>&1) || status=$?
  ((status == want)) || fail "$name: exit $status, want $want; output: $output"
  grep -Eq -- "$pattern" <<< "$output" || fail "$name: no '$pattern' in: $output"
  echo "ok  exit $want: $name"
}
# same <actual> <expected> <case>
same() { [[ "$1" == "$2" ]] || fail "$3: got '$1', want '$2'"; echo "ok  $3"; }

scratch=$(mktemp -d); trap 'rm -rf "$scratch"' EXIT
export GITHUB_OUTPUT="$scratch/output" GITHUB_STEP_SUMMARY="$scratch/summary"

# step <workflow> <job> <step name or id> <file>: writes that step's run: script to $scratch/<file> and prints its path.
step() {
  JOB=$2 STEP=$3 yq '.jobs[strenv(JOB)].steps[] | select(.name == strenv(STEP) or .id == strenv(STEP)) | .run' "$1" \
    > "$scratch/$4"
  [[ -s "$scratch/$4" ]] || fail "no step '$3' in $2"
  echo "$scratch/$4"
}
# run_step <script>: runs it as GitHub runs a bash step, with a fresh GITHUB_OUTPUT.
run_step() { : > "$GITHUB_OUTPUT"; : > "$GITHUB_STEP_SUMMARY"; bash --noprofile --norc -e "$1"; }

names=(api worker gateway core-banking zitadel)
sha=0123456789abcdef0123456789abcdef01234567
digest() { printf 'sha256:%064d' "$1"; } # a well-formed digest, different per number
reference() { echo "ghcr.io/njabulozmnisi/disputedesk-$1@$(digest "$2")"; }
expected_outputs=$(for i in "${!names[@]}"; do echo "${names[$i]}=$(reference "${names[$i]}" "$i")"; done)

# --- stubs: docker answers as the registry and daemon would for a pushed image; trivy finds nothing ---
mkdir "$scratch/bin"
cat > "$scratch/bin/docker" << 'EOF'
#!/usr/bin/env bash
# The image is the argument that names ghcr.io; its number is its place in api worker gateway core-banking zitadel.
for argument in "$@"; do [[ $argument == ghcr.io/* ]] && image=$argument; done
case "$image" in *disputedesk-api*) n=0 ;; *disputedesk-worker*) n=1 ;; *disputedesk-gateway*) n=2 ;;
  *disputedesk-core-banking*) n=3 ;; *disputedesk-zitadel*) n=4 ;; *) n=9 ;; esac
digest=$(printf 'sha256:%064d' "$n")
case "$1 $*" in
  "inspect "*RepoDigests*) # publish: the pushed image's repository digest, or STUB_API_DIGEST for api when set
    if [[ $n == 0 && -n ${STUB_API_DIGEST+set} ]]; then echo "$STUB_API_DIGEST"; else echo "${image%:*}@$digest"; fi ;;
  "inspect "*revision*) echo "$GITHUB_SHA" ;;
  "buildx "*.Manifest*) echo "{\"digest\":\"$digest\"}" ;; # deploy.yml's dispatch path
  "buildx "*.Image*) echo "{\"config\":{\"Labels\":{\"org.opencontainers.image.revision\":\"${STUB_REVISION:-$SHA}\"}}}" ;;
  *) ;; # build, push
esac
EOF
printf '#!/usr/bin/env bash\n' > "$scratch/bin/trivy"
chmod +x "$scratch/bin/docker" "$scratch/bin/trivy"
export PATH="$scratch/bin:$PATH"

# --- publish exposes the five references ---
# shellcheck disable=SC2016 # the expressions are GitHub's, not the shell's
same "$(yq -o=json -I=0 '.jobs.publish.outputs' "$ci")" \
  '{"api":"${{ steps.images.outputs.api }}","worker":"${{ steps.images.outputs.worker }}","gateway":"${{ steps.images.outputs.gateway }}","core-banking":"${{ steps.images.outputs.core-banking }}","zitadel":"${{ steps.images.outputs.zitadel }}"}' \
  "publish: declares the five outputs from the step images"
same "$(yq '.jobs.publish.steps[] | select(.name == "Build, scan and push the five images") | .id' "$ci")" images \
  "publish: the build step's id is images"

publish=$(step "$ci" publish images publish.sh)
export GITHUB_SHA=$sha GITHUB_REPOSITORY=njabulozmnisi/disputedesk
expect 0 '' "publish: five pushed images" run_step "$publish"
same "$(sort "$GITHUB_OUTPUT")" "$(sort <<< "$expected_outputs")" "publish: writes the five references to GITHUB_OUTPUT"
same "$(grep -c '^|' "$GITHUB_STEP_SUMMARY")" 7 "publish: the summary table has its header, rule and five rows"
grep -qF "| api | \`$(reference api 0)\` | \`$sha\` |" "$GITHUB_STEP_SUMMARY" || fail "publish: the summary's api row changed"
for bad in "ghcr.io/njabulozmnisi/disputedesk-api:sha-${sha::12}" "$(reference worker 0)" \
  "ghcr.io/njabulozmnisi/disputedesk-api@sha256:$(printf '%064d' 0 | tr 0 A)" ""; do
  export STUB_API_DIGEST=$bad
  expect 1 'api' "publish: api's reference '$bad' fails the step" run_step "$publish"
  ! grep -q '^api=' "$GITHUB_OUTPUT" || fail "publish: '$bad' was written as an output"
done
unset STUB_API_DIGEST
unset GITHUB_SHA GITHUB_REPOSITORY

# --- deploy.yml's triggers and its one job ---
same "$(yq '.on | keys | join(" ")' "$deploy")" workflow_dispatch "deploy.yml: workflow_dispatch is the only trigger"
same "$(yq -o=json -I=0 '.on.workflow_dispatch.inputs | map_values([.required, .type])' "$deploy")" \
  '{"sha":[true,"string"]}' "deploy.yml: workflow_dispatch takes sha alone"
same "$(yq 'has("concurrency")' "$deploy")" false "deploy.yml: no workflow-level concurrency"
same "$(yq '.jobs | keys | join(" ")' "$deploy")" deploy "deploy.yml: one job"
same "$(yq '.jobs.deploy.if' "$deploy")" "github.ref == 'refs/heads/main'" "deploy.yml: the job runs on main only"
same "$(yq '.jobs.deploy.environment' "$deploy")" dev "deploy.yml: the job is in the environment dev"
same "$(yq -o=json -I=0 '.jobs.deploy.concurrency' "$deploy")" '{"group":"deploy-dev","cancel-in-progress":false}' \
  "deploy.yml: the job holds deploy-dev without cancelling"
same "$(yq -o=json -I=0 '.jobs.deploy.permissions' "$deploy")" '{"id-token":"write","contents":"read"}' \
  "deploy.yml: the job's permissions"
# shellcheck disable=SC2016 # the expressions are GitHub's, not the shell's
same "$(yq '.jobs.deploy.env.SHA' "$deploy")" '${{ inputs.sha }}' "deploy.yml: SHA comes from inputs.sha"
same "$(yq '[.jobs.deploy.steps[] | .id // .uses // ""] | join(" ")' "$deploy" | cut -d' ' -f2,3)" \
  "images aws-actions/configure-aws-credentials@e1253824e5c10ff9df46874f81ed3ec929e19cfd" \
  "deploy.yml: the images step comes just before configure-aws-credentials"
# shellcheck disable=SC2016 # the expressions are GitHub's, not the shell's
same "$(yq -o=json -I=0 '.jobs.deploy.steps[] | select(.name == "Run the deploy document and wait for its result") | .env | del(.INSTANCE_ID, .DOCUMENT_NAME)' "$deploy")" \
  '{"API_IMAGE":"${{ steps.images.outputs.api }}","WORKER_IMAGE":"${{ steps.images.outputs.worker }}","GATEWAY_IMAGE":"${{ steps.images.outputs.gateway }}","CORE_BANKING_IMAGE":"${{ steps.images.outputs.core-banking }}","ZITADEL_IMAGE":"${{ steps.images.outputs.zitadel }}"}' \
  "deploy.yml: the deploy step reads the images step's outputs"

check_sha=$(step "$deploy" deploy "Check the SHA" check-sha.sh)
expect 0 '' "deploy.yml: a 40-character lower-case SHA passes" env SHA=$sha bash --noprofile --norc -e "$check_sha"
for bad in "${sha::39}" "${sha}0" "$(tr a-f A-F <<< "$sha")" ""; do
  expect 1 'sha must be' "deploy.yml: SHA '$bad' fails" env SHA="$bad" bash --noprofile --norc -e "$check_sha"
done

images=$(step "$deploy" deploy images images.sh)
# dispatched: runs the images step as a hand dispatch.
dispatched() { SHA=$sha run_step "$images"; }
expect 0 '' "deploy.yml dispatch: resolves the five digests from GHCR" dispatched
same "$(cat "$GITHUB_OUTPUT")" "$expected_outputs" "deploy.yml dispatch: writes the five outputs"
export STUB_REVISION="f${sha:1}"
expect 1 "not $sha" "deploy.yml dispatch: an image built from another commit fails" dispatched
unset STUB_REVISION

# --- CI never deploys: no job calls deploy.yml ---
same "$(yq '.jobs | to_entries | .[] | select(.value.uses // "" | test("deploy\.ya?ml")) | .key' "$ci")" "" \
  "ci.yml: no job uses deploy.yml"
same "$(yq -o=json -I=0 '.permissions' "$ci")" '{"contents":"read"}' "ci.yml: the workflow's permissions are contents: read"

# --- every workflow: only the deploy path can ask for an AWS token or run in an environment ---
# id_token_holders <directory>: prints each workflow or job there that holds id-token, or write-all, which includes it.
id_token_holders() {
  local file
  for file in "$1"/*.yml "$1"/*.yaml; do
    yq 'select(.permissions | to_yaml | test("id-token|write-all")) | filename' "$file"
    yq '.jobs | to_entries | .[] | select(.value.permissions | to_yaml | test("id-token|write-all")) | filename + ":" + .key' "$file"
  done | sed "s|^$1/||" | xargs
}
# environment_jobs <directory>: prints each job there that names an environment, in either of the key's two forms.
environment_jobs() {
  local file
  for file in "$1"/*.yml "$1"/*.yaml; do
    yq '.jobs | to_entries | .[] | select(.value | has("environment")) | filename + ":" + .key' "$file"
  done | sed "s|^$1/||" | xargs
}
workflows="$root/.github/workflows"
same "$(id_token_holders "$workflows")" "deploy.yml:deploy" "every workflow: only deploy.yml's job holds id-token: write"
same "$(environment_jobs "$workflows")" "deploy.yml:deploy" "every workflow: only deploy.yml's job names an environment"

mkdir "$scratch/other"
printf 'on: push\npermissions: write-all\njobs: {}\n' > "$scratch/other/top.yml"
printf 'on: push\njobs:\n  a:\n    permissions:\n      id-token: write\n    environment:\n      name: dev\n' > "$scratch/other/job.yml"
printf 'on: push\njobs:\n  b:\n    permissions:\n      id-token: write\n    environment: dev\n' > "$scratch/other/long.yaml"
same "$(id_token_holders "$scratch/other")" "job.yml:a top.yml long.yaml:b" "every workflow: a new holder is named, at either level, in either extension"
same "$(environment_jobs "$scratch/other")" "job.yml:a long.yaml:b" "every workflow: an environment in the name: form is named, in either extension"

echo "deploy-workflow: all cases pass"
