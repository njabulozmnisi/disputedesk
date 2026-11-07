#!/usr/bin/env bash
# Deploys DisputeDesk on the AWS dev instance (AD-28): run by SSM Run Command as root, through the deploy document
# Terraform creates, from the repository clone checked out at the commit being deployed.
#   AWS_REGION=<region> SSM_PATH=</ssm/path> infra/deploy/aws/deploy.sh API_IMAGE WORKER_IMAGE GATEWAY_IMAGE CORE_BANKING_IMAGE ZITADEL_IMAGE
#
# 1. Reads an allow-list of names under SSM_PATH (SecureStrings decrypted) and checks every value against story 7.4's
#    bounds (infra/deploy/aws/input-bounds.sh), so a missing or malformed value stops the deploy before any docker call.
# 2. Writes the RDS CA bundle and the gateway's set_real_ip_from file for CloudFront's origin-facing ranges.
# 3. Writes .env, mode 0600, to a temporary file renamed over the old one, so it is never half-written.
# 4. Creates the database roles and databases on RDS on the first deploy
#    (infra/images/postgres/10-roles-and-databases.sh).
# 5. Lets only the api and the otel-collector containers reach the instance metadata service.
# 6. Starts the stack and exits with Compose's exit code.
#
# It never prints a value it reads: no set -x, no .env, no `docker compose config` or `logs`. DeployScriptTests
# (infra/tests) runs it with stubs.
set -euo pipefail
cd "$(dirname "$0")/../../.."
# shellcheck source=infra/deploy/aws/input-bounds.sh
. infra/deploy/aws/input-bounds.sh

fail(){ echo "deploy: $*" >&2; exit 1; }
step(){ echo "deploy: $*"; }

# The names read from SSM, and only these: story 7.4's inputs that SSM holds (six a person creates, eight Terraform
# writes), and the two that only the database init uses. Any other parameter under SSM_PATH is ignored.
FROM_SSM=(
  APP_DB_PASSWORD ZITADEL_DB_PASSWORD ZITADEL_MASTERKEY ZITADEL_ADMIN_PASSWORD
  LINKING_OTP_HMAC_KEY MAILPIT_UI_AUTH
  APP_HOST AUTH_HOST MAILPIT_HOST DB_HOST OTEL_LOG_GROUP DATAPROTECTION_SSM_PATH DATAPROTECTION_KMS_KEY_ID
  GATEWAY_ORIGIN_VERIFY
  QA_DB_PASSWORD RDS_MASTER_PASSWORD
)

# RDS's master user; infra/terraform/modules/database uses the same constant.
RDS_MASTER_USER=dbmaster
# The pinned image of docker-compose.yml's postgres service, for psql (DeployScriptTests checks they match).
POSTGRES_IMAGE=postgres:18.6-alpine@sha256:898c60cb390d1085bc97bdd6a1ae968757421df394b071848b52b1c09c89d6a5
# infra/deploy/aws/compose.yml's fixed subnet and the two addresses allowed to reach the instance metadata service.
COMPOSE_SUBNET=10.201.89.0/24
API_ADDRESS=10.201.89.10/32
COLLECTOR_ADDRESS=10.201.89.11/32
INSTANCE_METADATA=169.254.169.254/32

[ $# -eq 5 ] || fail "usage: AWS_REGION=<region> SSM_PATH=<path> infra/deploy/aws/deploy.sh API_IMAGE WORKER_IMAGE GATEWAY_IMAGE CORE_BANKING_IMAGE ZITADEL_IMAGE"
within_bounds AWS_REGION "${AWS_REGION:-}" || fail "AWS_REGION is missing or outside its bound"
[[ ${SSM_PATH:-} =~ ^(/[A-Za-z0-9_.-]+)+$ ]] || fail "SSM_PATH is missing, or does not start with / or ends with /"

# The five arguments, in order, under their input names (every input is read by name below).
for name in API_IMAGE WORKER_IMAGE GATEWAY_IMAGE CORE_BANKING_IMAGE ZITADEL_IMAGE; do
  printf -v "$name" '%s' "$1"
  shift
done
# Beside .env in the clone; the containers mount both read-only.
RDS_CA_BUNDLE=$PWD/rds-ca-bundle.pem
GATEWAY_REALIP_CONF=$PWD/cloudfront-realip.conf

step "reading the parameters under $SSM_PATH"
parameters=$(aws ssm get-parameters-by-path --region "$AWS_REGION" --path "$SSM_PATH" --recursive --with-decryption \
  --output json) || fail "reading the parameters under $SSM_PATH failed"
for name in "${FROM_SSM[@]}"; do
  # The exact name only, so a parameter in a deeper path never stands in for one of these.
  value=$(jq -r --arg name "$SSM_PATH/$name" '.Parameters[] | select(.Name == $name) | .Value' <<< "$parameters")
  printf -v "$name" '%s' "$value"
done

# Every value is checked, in input-bounds.sh's order, before anything is written or started.
for name in "${AWS_INPUTS[@]}"; do
  [ -n "${!name}" ] || fail "$name is missing"
  within_bounds "$name" "${!name}" || fail "$name is outside its bound"
done
[ -n "$QA_DB_PASSWORD" ] || fail "QA_DB_PASSWORD is missing"
[[ $QA_DB_PASSWORD =~ ^[A-Za-z0-9]{32,128}$ ]] || fail "QA_DB_PASSWORD is outside its bound"
[ -n "$RDS_MASTER_PASSWORD" ] || fail "RDS_MASTER_PASSWORD is missing"
# The shape of the database module's ephemeral random_password (length 64, special false), which a Terraform test
# cannot assert; RDS rejects /, @, " and spaces in a master password.
[[ $RDS_MASTER_PASSWORD =~ ^[A-Za-z0-9]{64}$ ]] || fail "RDS_MASTER_PASSWORD is outside its bound"
step "all ${#AWS_INPUTS[@]} inputs, QA_DB_PASSWORD and RDS_MASTER_PASSWORD are present and within their bounds"

# replace_file DESTINATION MODE: writes standard input to a temporary file beside DESTINATION, then renames it over
# DESTINATION, so a failure leaves the old file whole.
replace_file(){
  local temporary
  temporary=$(mktemp "$1.XXXXXX")
  cat > "$temporary"
  chmod "$2" "$temporary"
  mv -f "$temporary" "$1"
}

# The region's CA bundle (Amazon RDS, "Using SSL/TLS to encrypt a connection to a DB instance", certificate bundles by
# AWS Region), for verify-full.
step "fetching the RDS CA bundle for $AWS_REGION"
bundle=$(curl -fsS --max-time 60 --retry 3 "https://truststore.pki.rds.amazonaws.com/$AWS_REGION/$AWS_REGION-bundle.pem") \
  || fail "fetching the RDS CA bundle failed"
grep -q -- '-----BEGIN CERTIFICATE-----' <<< "$bundle" || fail "the RDS CA bundle holds no certificate"
replace_file "$RDS_CA_BUNDLE" 0644 <<< "$bundle"

# CloudFront's origin-facing IPv4 ranges from AWS's published list, as nginx set_real_ip_from lines (story 7.4). The
# list can drift until the next deploy (infra/terraform/README.md).
step "fetching CloudFront's origin-facing ranges"
ranges=$(curl -fsS --max-time 60 --retry 3 https://ip-ranges.amazonaws.com/ip-ranges.json \
  | jq -r '.prefixes[] | select(.service == "CLOUDFRONT_ORIGIN_FACING") | .ip_prefix') \
  || fail "fetching ip-ranges.json failed"
[ -n "$ranges" ] || fail "ip-ranges.json lists no CLOUDFRONT_ORIGIN_FACING IPv4 range"
realip=''
while read -r range; do
  [[ $range =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}/[0-9]{1,2}$ ]] || fail "ip-ranges.json holds a range that is not IPv4: $range"
  realip+="set_real_ip_from $range;"$'\n'
done <<< "$ranges"
printf '%s' "$realip" | replace_file "$GATEWAY_REALIP_CONF" 0644
step "wrote $(wc -l <<< "$ranges" | tr -d ' ') ranges"

for name in "${AWS_INPUTS[@]}"; do
  printf '%s=%s\n' "$name" "${!name}"
done | replace_file .env 0600
step "wrote .env"

# psql runs in the pinned postgres image against RDS, verifying its certificate. Every value reaches the container by
# name (-e NAME, read from this subshell's environment), never in docker's arguments.
export_rds_connection(){
  export PGHOST=$DB_HOST PGSSLMODE=verify-full PGSSLROOTCERT=/run/rds/ca-bundle.pem PGUSER=$RDS_MASTER_USER \
    PGPASSWORD=$RDS_MASTER_PASSWORD PGDATABASE=postgres
}
rds_connection=(-e PGHOST -e PGSSLMODE -e PGSSLROOTCERT -e PGUSER -e PGPASSWORD -e PGDATABASE
  -v "$RDS_CA_BUNDLE:/run/rds/ca-bundle.pem:ro")

# "<roles> <databases>": how many of the init's three roles and two databases exist.
count_query="SELECT (SELECT count(*) FROM pg_roles WHERE rolname IN ('disputedesk', 'zitadel', 'qa_readonly'))"
count_query+=" || ' ' || (SELECT count(*) FROM pg_database WHERE datname IN ('disputedesk', 'zitadel'))"
roles_and_databases=$(
  export_rds_connection
  docker run --rm "${rds_connection[@]}" "$POSTGRES_IMAGE" psql -v ON_ERROR_STOP=1 -tA -c "$count_query"
) || fail "reading the roles and databases on RDS failed"

case $roles_and_databases in
  "0 0")
    # First deploy. RDS's master is not a superuser and gets no membership in the roles it creates, so the init grants
    # it each owner role WITH SET before CREATE DATABASE ... OWNER, which needs it (PostgreSQL, CREATE DATABASE).
    step "creating the database roles and databases"
    (
      export_rds_connection
      export POSTGRES_USER=$RDS_MASTER_USER APP_DB_PASSWORD ZITADEL_DB_PASSWORD QA_DB_PASSWORD
      docker run --rm "${rds_connection[@]}" -e POSTGRES_USER -e APP_DB_PASSWORD -e ZITADEL_DB_PASSWORD \
        -e QA_DB_PASSWORD -v "$PWD/infra/images/postgres/10-roles-and-databases.sh:/init/10-roles-and-databases.sh:ro" \
        "$POSTGRES_IMAGE" sh /init/10-roles-and-databases.sh
    ) || fail "the database init failed; see 'A half-done database init' in infra/terraform/README.md"
    ;;
  "3 2")
    step "the database roles and databases exist"
    ;;
  *)
    fail "RDS holds only some of the roles and databases; see 'A half-done database init' in infra/terraform/README.md"
    ;;
esac

# Only the api and the otel-collector may reach the instance metadata service (AD-28): Docker's DOCKER-USER chain runs
# before Docker's own rules (Docker, "Packet filtering and firewalls"). Saved for iptables-services to restore at boot,
# as the Amazon ECS Developer Guide persists its metadata block.
if iptables -C DOCKER-USER -s "$COMPOSE_SUBNET" -d "$INSTANCE_METADATA" -j DROP 2>/dev/null; then
  step "the instance metadata rule is in place"
else
  step "blocking the instance metadata service for every container but the api and the otel-collector"
  iptables -I DOCKER-USER 1 -s "$API_ADDRESS" -d "$INSTANCE_METADATA" -j RETURN
  iptables -I DOCKER-USER 2 -s "$COLLECTOR_ADDRESS" -d "$INSTANCE_METADATA" -j RETURN
  iptables -I DOCKER-USER 3 -s "$COMPOSE_SUBNET" -d "$INSTANCE_METADATA" -j DROP
  iptables-save -f /etc/sysconfig/iptables
  systemctl enable iptables
fi

step "starting the stack"
docker compose -f docker-compose.yml -f infra/deploy/aws/compose.yml up -d --wait
