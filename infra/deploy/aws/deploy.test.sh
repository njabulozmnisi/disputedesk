#!/usr/bin/env bash
# The instance's deploy script (story 7.5, criterion 6), offline: infra/deploy/aws/deploy.sh runs in a throwaway copy with
# aws, curl, docker, iptables, iptables-save and systemctl replaced by stubs on PATH, which record every call and
# answer as each case sets. No AWS endpoint, container or firewall is touched.
#   infra/deploy/aws/deploy.test.sh
# Needs bash and jq. Every check fails the run.
set -euo pipefail
cd "$(dirname "$0")/../.."
# shellcheck source=infra/deploy/aws/input-bounds.sh
. infra/deploy/aws/input-bounds.sh

section(){ printf '\n== %s\n' "$*"; }
fail(){ echo "DEPLOY TEST FAIL: $*" >&2; exit 1; }
ok(){ echo "ok  $*"; }

ENV_FILE=infra/tests/fixtures/compose.test.env
test_value(){ grep "^$1=" "$ENV_FILE" | cut -d= -f2-; }

SSM_PATH=/disputedesk/dev
QA_PASSWORD=TestOnlyQaDbPasswordNotASecret00
# 64 letters and digits, the database module's random_password shape; the first deploy below is the passing case.
MASTER_PASSWORD=TestOnlyRdsMasterPasswordNotASecret00000000000000000000000000000
# The names the script must read from SSM: story 7.4's inputs that SSM holds, and the two the init alone uses.
SSM_NAMES=(
  APP_DB_PASSWORD ZITADEL_DB_PASSWORD LOGIN_SESSION_COOKIE_SECRET ZITADEL_MASTERKEY ZITADEL_ADMIN_PASSWORD
  LINKING_OTP_HMAC_KEY MAILPIT_UI_AUTH APP_HOST AUTH_HOST MAILPIT_HOST DB_HOST OTEL_LOG_GROUP DATAPROTECTION_SSM_PATH
  DATAPROTECTION_KMS_KEY_ID GATEWAY_ORIGIN_VERIFY QA_DB_PASSWORD RDS_MASTER_PASSWORD
)
DEFAULT_IMAGES=(
  "$(test_value API_IMAGE)" "$(test_value WORKER_IMAGE)" "$(test_value GATEWAY_IMAGE)" "$(test_value CORE_BANKING_IMAGE)"
  "$(test_value ZITADEL_IMAGE)"
)
# Every secret value, none of which may appear in any output or in any stubbed command's arguments.
SECRETS=(
  "$(test_value APP_DB_PASSWORD)" "$(test_value ZITADEL_DB_PASSWORD)" "$(test_value LOGIN_SESSION_COOKIE_SECRET)"
  "$(test_value ZITADEL_MASTERKEY)" "$(test_value ZITADEL_ADMIN_PASSWORD)" "$(test_value LINKING_OTP_HMAC_KEY)"
  "$(test_value MAILPIT_UI_AUTH)" "$(test_value GATEWAY_ORIGIN_VERIFY)" "$QA_PASSWORD" "$MASTER_PASSWORD"
)

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
repo=$work/repo

section "stubs"
mkdir "$work/bin"
cat > "$work/bin/stub" <<'STUB'
#!/usr/bin/env bash
# Records each call as one line (the tool and its arguments) and answers as the test's STUB_* variables say.
set -euo pipefail
tool=$(basename "$0")
{ printf '%s' "$tool"; printf ' %s' "$@"; echo; } >> "$STUB_CALLS"
case $tool in
  aws)
    [ -z "${STUB_AWS_FAILS:-}" ] || exit 255
    cat "$STUB_PARAMETERS" ;;
  curl)
    url=${*: -1}
    case $url in
      *truststore.pki.rds.amazonaws.com*)
        [ -z "${STUB_CA_FAILS:-}" ] || exit 22
        printf -- '-----BEGIN CERTIFICATE-----\nTestOnlyCertificate\n-----END CERTIFICATE-----\n' ;;
      *ip-ranges.amazonaws.com*)
        [ -z "${STUB_RANGES_FAIL:-}" ] || exit 22
        cat "$STUB_RANGES" ;;
    esac ;;
  docker)
    # The environment docker would hand to the container for each -e NAME.
    echo "PGHOST=${PGHOST:-} PGSSLMODE=${PGSSLMODE:-} PGSSLROOTCERT=${PGSSLROOTCERT:-} PGUSER=${PGUSER:-}" \
      "PGDATABASE=${PGDATABASE:-} POSTGRES_USER=${POSTGRES_USER:-} PGOPTIONS=${PGOPTIONS:-}" \
      "PGPASSWORD=${PGPASSWORD:-} QA_DB_PASSWORD=${QA_DB_PASSWORD:-} APP_DB_PASSWORD=${APP_DB_PASSWORD:-}" \
      "ZITADEL_DB_PASSWORD=${ZITADEL_DB_PASSWORD:-}" >> "$STUB_DOCKER_ENVIRONMENT"
    case "$*" in
      *" psql "*) echo "$STUB_ROLES_AND_DATABASES" ;;
      *10-roles-and-databases.sh) exit "${STUB_INIT_EXIT:-0}" ;;
      compose*)
        echo "COMPOSE_PROFILES=${COMPOSE_PROFILES:-}" >> "$STUB_COMPOSE_PROFILES"
        exit "${STUB_COMPOSE_EXIT:-0}" ;;
    esac ;;
  iptables)
    # -C answers whether the rule exists: 1 (absent) unless the case says otherwise.
    [ "$1" != -C ] || exit "${STUB_RULE_CHECK_EXIT:-1}" ;;
esac
STUB
chmod +x "$work/bin/stub"
for tool in aws curl docker iptables iptables-save systemctl; do ln -s stub "$work/bin/$tool"; done

# CloudFront's origin-facing IPv4 ranges among others the script must leave out (documentation ranges, RFC 5737).
cat > "$work/ranges.json" <<'JSON'
{
  "prefixes": [
    { "ip_prefix": "192.0.2.0/26", "service": "CLOUDFRONT_ORIGIN_FACING" },
    { "ip_prefix": "198.51.100.0/24", "service": "CLOUDFRONT_ORIGIN_FACING" },
    { "ip_prefix": "203.0.113.0/25", "service": "CLOUDFRONT" },
    { "ip_prefix": "203.0.113.128/25", "service": "EC2" }
  ],
  "ipv6_prefixes": [{ "ipv6_prefix": "2001:db8::/32", "service": "CLOUDFRONT_ORIGIN_FACING" }]
}
JSON
echo '{ "prefixes": [{ "ip_prefix": "203.0.113.0/25", "service": "CLOUDFRONT" }], "ipv6_prefixes": [] }' > "$work/no-ranges.json"
echo '{ "prefixes": [{ "ip_prefix": "192.0.2.0/26;", "service": "CLOUDFRONT_ORIGIN_FACING" }], "ipv6_prefixes": [] }' \
  > "$work/bad-range.json"
ok "stubs for aws, curl, docker, iptables, iptables-save and systemctl"

default_value(){
  case $1 in
    QA_DB_PASSWORD) echo "$QA_PASSWORD" ;;
    RDS_MASTER_PASSWORD) echo "$MASTER_PASSWORD" ;;
    *) test_value "$1" ;;
  esac
}

# parameters [NAME=value...]: writes the stub's get-parameters-by-path answer: every name at its test value unless
# given (NAME= leaves it out), plus two parameters the script must ignore.
parameters(){
  local name value assignment
  for name in "${SSM_NAMES[@]}"; do
    value=$(default_value "$name")
    for assignment in "$@"; do
      if [ "${assignment%%=*}" = "$name" ]; then value=${assignment#*=}; fi
    done
    if [ -n "$value" ]; then printf '%s\t%s\n' "$SSM_PATH/$name" "$value"; fi
  done > "$work/parameters.tsv"
  printf '%s\t%s\n' "$SSM_PATH/EXTRA_NOT_ALLOWED" ExtraValueNotAllowed0 "$SSM_PATH/old/APP_HOST" stale.disputedesk.test \
    >> "$work/parameters.tsv"
  jq -R -s '{ Parameters: [split("\n")[] | select(. != "") | split("\t") | { Name: .[0], Value: .[1], Type: "SecureString" }] }' \
    "$work/parameters.tsv" > "$work/parameters.json"
}

# has_mode FILE MODE: FILE's permission bits are exactly MODE.
has_mode(){ [ -n "$(find "$1" -perm "$2")" ]; }

# run_deploy [NAME=value...]: a fresh copy of the scripts beside a stale .env (mode 0644), run with the stubs and the
# given environment; sets status. The script's arguments are the array `arguments`.
arguments=("${DEFAULT_IMAGES[@]}")
run_deploy(){
  rm -rf "$repo"
  mkdir -p "$repo/infra/deploy/aws" "$repo/infra/images/postgres"
  cp infra/deploy/aws/deploy.sh infra/deploy/aws/input-bounds.sh "$repo/infra/deploy/aws/"
  cp infra/images/postgres/10-roles-and-databases.sh "$repo/infra/images/postgres/"
  echo STALE=1 > "$repo/.env"
  chmod 0644 "$repo/.env"
  # A second name for the old file: if .env is still the same file afterwards, it was written in place.
  rm -f "$work/stale.env"
  ln "$repo/.env" "$work/stale.env"
  : > "$work/calls"
  : > "$work/docker-environment"
  : > "$work/compose-profiles"
  status=0
  # A developer's own COMPOSE_PROFILES never reaches the script: the check below sees only what the script sets.
  env -u COMPOSE_PROFILES PATH="$work/bin:$PATH" AWS_REGION=eu-west-1 SSM_PATH="$SSM_PATH" STUB_CALLS="$work/calls" \
    STUB_DOCKER_ENVIRONMENT="$work/docker-environment" STUB_COMPOSE_PROFILES="$work/compose-profiles" \
    STUB_PARAMETERS="$work/parameters.json" STUB_RANGES="$work/ranges.json" STUB_ROLES_AND_DATABASES="0 0" "$@" \
    "$repo/infra/deploy/aws/deploy.sh" "${arguments[@]}" > "$work/output" 2>&1 || status=$?
}

called(){ grep -qxF -- "$1" "$work/calls"; }
calls_of(){ grep "^$1 " "$work/calls" || true; }

# No secret in the script's standard output or error, or in any stubbed command's arguments.
no_secret_shown(){
  local secret
  for secret in "${SECRETS[@]}"; do
    ! grep -qF -- "$secret" "$work/output" || fail "$1: the output shows a secret"
    ! grep -qF -- "$secret" "$work/calls" || fail "$1: a command's arguments hold a secret"
  done
}

# expect_failure DESCRIPTION MESSAGE STOPS_BEFORE [NAME=value...]: the deploy exits non-zero naming MESSAGE, before
# any `docker` call (STOPS_BEFORE=docker) or before Compose (STOPS_BEFORE=compose), and shows no secret.
expect_failure(){
  local description=$1 message=$2 stops_before=$3
  shift 3
  run_deploy "$@"
  [ "$status" -ne 0 ] || fail "$description: the deploy succeeded"
  grep -qF -- "$message" "$work/output" || fail "$description: the error does not say '$message': $(cat "$work/output")"
  if [ "$stops_before" = docker ]; then
    [ -z "$(calls_of docker)" ] || fail "$description: docker was called"
    if [ ! "$repo/.env" -ef "$work/stale.env" ] || [ "$(cat "$repo/.env")" != STALE=1 ]; then
      fail "$description: .env was touched"
    fi
  fi
  [ -z "$(calls_of 'docker compose')" ] || fail "$description: Compose was started"
  no_secret_shown "$description"
  ok "$description: exits $status before $stops_before, saying '$message'"
}

section "a first deploy"
parameters
run_deploy
[ "$status" -eq 0 ] || fail "the deploy failed: $(cat "$work/output")"
ok "exits 0"

called "aws ssm get-parameters-by-path --region eu-west-1 --path $SSM_PATH --recursive --with-decryption --output json" \
  || fail "the parameters are not read with get-parameters-by-path --recursive --with-decryption: $(calls_of aws)"
ok "reads the parameters under $SSM_PATH with get-parameters-by-path --recursive --with-decryption"

expected_env=$work/expected.env
for name in "${AWS_INPUTS[@]}"; do
  case $name in
    RDS_CA_BUNDLE) value=$repo/rds-ca-bundle.pem ;;
    GATEWAY_REALIP_CONF) value=$repo/cloudfront-realip.conf ;;
    AWS_REGION) value=eu-west-1 ;;
    *) value=$(test_value "$name") ;;
  esac
  echo "$name=$value"
done > "$expected_env"
diff "$expected_env" "$repo/.env" > /dev/null || fail ".env is not exactly story 7.4's inputs with their values"
ok ".env holds exactly the ${#AWS_INPUTS[@]} inputs of input-bounds.sh, from SSM, the arguments and the two fixed paths"
for absent in QA_DB_PASSWORD RDS_MASTER_PASSWORD EXTRA_NOT_ALLOWED ExtraValueNotAllowed0 stale.disputedesk.test STALE; do
  ! grep -qF -- "$absent" "$repo/.env" || fail ".env holds $absent"
done
ok ".env leaves out QA_DB_PASSWORD, RDS_MASTER_PASSWORD, a parameter not on the allow-list and one in a deeper path"

has_mode "$repo/.env" 0600 || fail ".env is not mode 0600"
[ ! "$repo/.env" -ef "$work/stale.env" ] || fail ".env was written in place, not renamed over the old one"
[ -z "$(find "$repo" -name '.env.*')" ] || fail "a temporary .env file was left behind"
ok ".env is mode 0600 and replaced the old one (0644) by a rename, leaving no temporary file"

called "curl -fsS --max-time 60 --retry 3 https://truststore.pki.rds.amazonaws.com/eu-west-1/eu-west-1-bundle.pem" \
  || fail "the CA bundle is not the region's: $(calls_of curl)"
grep -qF -- '-----BEGIN CERTIFICATE-----' "$repo/rds-ca-bundle.pem" || fail "the CA bundle was not written"
has_mode "$repo/rds-ca-bundle.pem" 0644 || fail "the CA bundle is not readable by the containers"
ok "writes the region's RDS CA bundle, mode 0644"

printf 'set_real_ip_from 192.0.2.0/26;\nset_real_ip_from 198.51.100.0/24;\n' > "$work/expected-realip.conf"
diff "$work/expected-realip.conf" "$repo/cloudfront-realip.conf" > /dev/null \
  || fail "the realip file is not the CLOUDFRONT_ORIGIN_FACING IPv4 ranges: $(cat "$repo/cloudfront-realip.conf")"
has_mode "$repo/cloudfront-realip.conf" 0644 || fail "the realip file is not readable by the gateway"
ok "writes set_real_ip_from for the CLOUDFRONT_ORIGIN_FACING IPv4 ranges only, mode 0644"

compose_postgres=$(grep -m1 'image: postgres:' docker-compose.yml | awk '{ print $2 }')
connection="-e PGHOST -e PGSSLMODE -e PGSSLROOTCERT -e PGUSER -e PGPASSWORD -e PGDATABASE -v $repo/rds-ca-bundle.pem:/run/rds/ca-bundle.pem:ro"
docker_calls=$(calls_of docker)
count_query="SELECT (SELECT count(*) FROM pg_roles WHERE rolname IN ('disputedesk', 'zitadel', 'qa_readonly')) || ' ' || (SELECT count(*) FROM pg_database WHERE datname IN ('disputedesk', 'zitadel'))"
[ "$(sed -n 1p <<< "$docker_calls")" = "docker run --rm $connection $compose_postgres psql -v ON_ERROR_STOP=1 -tA -c $count_query" ] \
  || fail "the first docker call is not psql in docker-compose.yml's postgres image ($compose_postgres) counting the roles and databases: $docker_calls"
[ "$(sed -n 1p "$work/docker-environment")" = "PGHOST=$(test_value DB_HOST) PGSSLMODE=verify-full PGSSLROOTCERT=/run/rds/ca-bundle.pem PGUSER=dbmaster PGDATABASE=postgres POSTGRES_USER= PGOPTIONS= PGPASSWORD=$MASTER_PASSWORD QA_DB_PASSWORD= APP_DB_PASSWORD= ZITADEL_DB_PASSWORD=" ] \
  || fail "the count does not connect with verify-full as dbmaster, or sees more than the master password"
init_call=$(grep -F '10-roles-and-databases.sh' <<< "$docker_calls" || true)
[ "$init_call" = "docker run --rm $connection -e POSTGRES_USER -e PGOPTIONS -e APP_DB_PASSWORD -e ZITADEL_DB_PASSWORD -e QA_DB_PASSWORD -v $repo/infra/images/postgres/10-roles-and-databases.sh:/init/10-roles-and-databases.sh:ro $compose_postgres sh /init/10-roles-and-databases.sh" ] \
  || fail "the init does not run the unchanged script in the postgres image with every value passed by name: $init_call"
init_environment=$(sed -n 2p "$work/docker-environment")
expected_environment="PGHOST=$(test_value DB_HOST) PGSSLMODE=verify-full PGSSLROOTCERT=/run/rds/ca-bundle.pem PGUSER=dbmaster PGDATABASE=postgres POSTGRES_USER=dbmaster PGOPTIONS=-c createrole_self_grant=set,inherit PGPASSWORD=$MASTER_PASSWORD QA_DB_PASSWORD=$QA_PASSWORD APP_DB_PASSWORD=$(test_value APP_DB_PASSWORD) ZITADEL_DB_PASSWORD=$(test_value ZITADEL_DB_PASSWORD)"
[ "$init_environment" = "$expected_environment" ] \
  || fail "the init's environment is not verify-full as dbmaster with PGOPTIONS and the four passwords"
ok "the init runs infra/images/postgres/10-roles-and-databases.sh in $compose_postgres with verify-full, POSTGRES_USER=dbmaster and"
ok "  PGOPTIONS='-c createrole_self_grant=set,inherit'; the master, QA and role passwords reach it by name only"

iptables_calls=$(calls_of iptables)
expected_iptables="iptables -C DOCKER-USER -s 10.201.89.0/24 -d 169.254.169.254/32 -j DROP
iptables -I DOCKER-USER 1 -s 10.201.89.10/32 -d 169.254.169.254/32 -j RETURN
iptables -I DOCKER-USER 2 -s 10.201.89.11/32 -d 169.254.169.254/32 -j RETURN
iptables -I DOCKER-USER 3 -s 10.201.89.0/24 -d 169.254.169.254/32 -j DROP"
[ "$iptables_calls" = "$expected_iptables" ] || fail "the DOCKER-USER rules are not the api and collector exceptions before the subnet's drop: $iptables_calls"
called "iptables-save -f /etc/sysconfig/iptables" || fail "the rules are not saved to /etc/sysconfig/iptables"
called "systemctl enable iptables" || fail "iptables-services is not enabled to restore the rules at boot"
ok "inserts DOCKER-USER: RETURN for the api (.10) and collector (.11), then DROP for the Compose subnet, to 169.254.169.254"
ok "  and saves them to /etc/sysconfig/iptables with the iptables service enabled"

[ "$(tail -n 1 "$work/calls")" = "docker compose -f docker-compose.yml -f infra/deploy/aws/compose.yml up -d --wait" ] \
  || fail "the last call is not Compose up --wait with the AWS override: $(tail -n 1 "$work/calls")"
order=$(cut -d' ' -f1 "$work/calls" | uniq | tr '\n' ' ')
[ "$order" = "aws curl docker iptables iptables-save systemctl docker " ] || fail "the calls run in the wrong order: $order"
ok "reads SSM, fetches, inits, sets the rules, then runs docker compose up -d --wait with the AWS override, last"
# No --profile above, and no COMPOSE_PROFILES here: Redpanda Console (profile console) never starts in AWS.
[ "$(cat "$work/compose-profiles")" = COMPOSE_PROFILES= ] || fail "Compose runs with $(cat "$work/compose-profiles")"
ok "Compose runs with no profile enabled, so the console profile never starts"
no_secret_shown "a first deploy"
ok "no secret in standard output, standard error or any command's arguments"

section "a later deploy"
run_deploy STUB_ROLES_AND_DATABASES="3 2" STUB_RULE_CHECK_EXIT=0
[ "$status" -eq 0 ] || fail "the deploy failed: $(cat "$work/output")"
! grep -qF 10-roles-and-databases.sh "$work/calls" || fail "the init ran although the roles and databases exist"
[ "$(calls_of iptables)" = "iptables -C DOCKER-USER -s 10.201.89.0/24 -d 169.254.169.254/32 -j DROP" ] \
  || fail "the rules were inserted again: $(calls_of iptables)"
! grep -qE '^(iptables-save|systemctl) ' "$work/calls" || fail "the rules were saved again"
no_secret_shown "a later deploy"
ok "skips the init when the three roles and both databases exist, and inserts the DOCKER-USER rules only once"

STUB_EXIT=7
run_deploy STUB_ROLES_AND_DATABASES="3 2" STUB_RULE_CHECK_EXIT=0 STUB_COMPOSE_EXIT=$STUB_EXIT
[ "$status" -eq "$STUB_EXIT" ] || fail "a failed Compose up exits $status, not Compose's $STUB_EXIT"
no_secret_shown "a failed Compose up"
ok "a failed Compose up exits with Compose's exit code ($STUB_EXIT)"

section "every failure exits non-zero, naming its cause"
parameters APP_DB_PASSWORD=
expect_failure "a missing parameter" "APP_DB_PASSWORD is missing" docker
parameters MAILPIT_UI_AUTH= APP_HOST=
expect_failure "two missing parameters" "MAILPIT_UI_AUTH is missing" docker
parameters "APP_DB_PASSWORD=$(test_value APP_DB_PASSWORD);"
expect_failure "a value with ;" "APP_DB_PASSWORD is outside its bound" docker
parameters "ZITADEL_ADMIN_PASSWORD=\$$(test_value ZITADEL_ADMIN_PASSWORD)"
expect_failure "a value with \$" "ZITADEL_ADMIN_PASSWORD is outside its bound" docker
parameters "APP_HOST=https://app.disputedesk.test"
expect_failure "a host with a scheme" "APP_HOST is outside its bound" docker
parameters QA_DB_PASSWORD=
expect_failure "a missing QA_DB_PASSWORD" "QA_DB_PASSWORD is missing" docker
parameters "QA_DB_PASSWORD=${QA_PASSWORD:1}"
expect_failure "a 31-character QA_DB_PASSWORD" "QA_DB_PASSWORD is outside its bound" docker
parameters "QA_DB_PASSWORD=${QA_PASSWORD}-"
expect_failure "a QA_DB_PASSWORD with -" "QA_DB_PASSWORD is outside its bound" docker
parameters RDS_MASTER_PASSWORD=
expect_failure "a missing RDS_MASTER_PASSWORD" "RDS_MASTER_PASSWORD is missing" docker
parameters "RDS_MASTER_PASSWORD=${MASTER_PASSWORD:1}"
expect_failure "a 63-character RDS_MASTER_PASSWORD" "RDS_MASTER_PASSWORD is outside its bound" docker
parameters "RDS_MASTER_PASSWORD=${MASTER_PASSWORD}0"
expect_failure "a 65-character RDS_MASTER_PASSWORD" "RDS_MASTER_PASSWORD is outside its bound" docker
parameters "RDS_MASTER_PASSWORD=${MASTER_PASSWORD:1}/"
expect_failure "an RDS_MASTER_PASSWORD with /" "RDS_MASTER_PASSWORD is outside its bound" docker

parameters
arguments=("ghcr.io/disputedesk-test/disputedesk-api:sha-000000000000" "${DEFAULT_IMAGES[@]:1}")
expect_failure "an image by tag, not digest" "API_IMAGE is outside its bound" docker
arguments=("${DEFAULT_IMAGES[@]:0:4}")
expect_failure "four image arguments" "usage:" docker
arguments=("${DEFAULT_IMAGES[@]}")
expect_failure "a region with no number" "AWS_REGION is missing or outside its bound" docker AWS_REGION=eu-west
expect_failure "an SSM path with a trailing /" "SSM_PATH is missing" docker SSM_PATH=/disputedesk/dev/
expect_failure "a failed SSM read" "reading the parameters under $SSM_PATH failed" docker STUB_AWS_FAILS=1
expect_failure "a failed CA bundle fetch" "fetching the RDS CA bundle failed" docker STUB_CA_FAILS=1
expect_failure "a failed ip-ranges.json fetch" "fetching ip-ranges.json failed" docker STUB_RANGES_FAIL=1
expect_failure "no origin-facing IPv4 range" "lists no CLOUDFRONT_ORIGIN_FACING IPv4 range" docker \
  STUB_RANGES="$work/no-ranges.json"
expect_failure "a range that is not IPv4" "holds a range that is not IPv4" docker STUB_RANGES="$work/bad-range.json"
expect_failure "a role present with a database missing" "A half-done database init" compose \
  STUB_ROLES_AND_DATABASES="1 1"
expect_failure "all three roles with one database" "A half-done database init" compose STUB_ROLES_AND_DATABASES="3 1"
expect_failure "a failed init" "the database init failed" compose STUB_INIT_EXIT=1

section "the script never prints what it reads"
code=$(grep -v '^[[:space:]]*#' infra/deploy/aws/deploy.sh)
for pattern in 'set -x' 'set -o xtrace' 'cat .env' 'compose config' 'compose logs' 'compose -f docker-compose.yml -f infra/deploy/aws/compose.yml config'; do
  ! grep -qF -- "$pattern" <<< "$code" || fail "deploy.sh has $pattern"
done
grep -qx "POSTGRES_IMAGE=$compose_postgres" infra/deploy/aws/deploy.sh || fail "deploy.sh's postgres image is not docker-compose.yml's"
ok "no set -x, no cat .env, no compose config or logs; psql's image is docker-compose.yml's pinned postgres"

echo
echo "DEPLOY TEST OK"
