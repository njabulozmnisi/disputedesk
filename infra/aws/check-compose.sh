#!/usr/bin/env bash
# Render checks of infra/aws/compose.yml merged with docker-compose.yml (story 7.4, criteria 1, 2 and 5), and of
# Redpanda Console staying behind its profile, offline:
# Compose renders the merged file with the dummy inputs in compose.test.env, and jq reads the JSON render. The last
# section validates the collector config with the pinned collector image.
#   infra/aws/check-compose.sh
# Needs docker (Compose v2) and jq. Every check fails the run.
set -euo pipefail
cd "$(dirname "$0")/../.."
# shellcheck source=infra/aws/input-bounds.sh
. infra/aws/input-bounds.sh

ENV_FILE=infra/aws/compose.test.env
OVERRIDE=infra/aws/compose.yml
SUBNET=10.201.89.0/24
GATEWAY_IP=10.201.89.12   # pinned outside ip_range, so it is the gateway's alone
LONG_RUNNING=(redpanda zitadel zitadel-login mailpit core-banking api worker otel-collector gateway)
ONE_SHOTS=(redpanda-topics provision)
MEMORY_BUDGET_MIB=3482

section(){ printf '\n== %s\n' "$*"; }
fail(){ echo "CHECK FAIL: $*" >&2; exit 1; }
ok(){ echo "ok  $*"; }

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# No input may come from the shell (a developer's own AWS_REGION, say): every render unsets them all first.
unset_inputs=()
for name in "${AWS_INPUTS[@]}"; do unset_inputs+=(-u "$name"); done
render(){ # env-file [config options...]
  local env_file=$1; shift
  env "${unset_inputs[@]}" docker compose -f docker-compose.yml -f "$OVERRIDE" --env-file "$env_file" config "$@"
}
test_value(){ grep "^$1=" "$ENV_FILE" | cut -d= -f2-; }
sorted(){ printf '%s\n' "$@" | sort; }

section "criterion 1: every input is required, and only the inputs are"
# The override without its comments: the ${...} references and the keys it sets.
override_values=$(grep -v '^[[:space:]]*#' "$OVERRIDE" | sed 's/[[:space:]]#.*$//')
env_names=$(grep -E '^[A-Z0-9_]+=' "$ENV_FILE" | cut -d= -f1 | sort)
referenced=$(grep -oE '\$\{[A-Z0-9_]+' <<< "$override_values" | cut -c3- | sort -u)
[ "$env_names" = "$(sorted "${AWS_INPUTS[@]}")" ] || fail "compose.test.env does not hold exactly the inputs of input-bounds.sh"
[ "$referenced" = "$(sorted "${AWS_INPUTS[@]}")" ] || fail "the override does not reference exactly the inputs of input-bounds.sh: $(tr '\n' ' ' <<< "$referenced")"
ok "the override, compose.test.env and input-bounds.sh name the same ${#AWS_INPUTS[@]} inputs"
not_required=$(grep -oE '\$\{[^}]*\}' <<< "$override_values" | grep -vE '^\$\{[A-Z0-9_]+:\?[^}]+\}$' || true)
[ -z "$not_required" ] || fail "references without :? (a default, or no message): $not_required"
unbraced=$(sed 's/\$\$//g' <<< "$override_values" | grep -oE '\$[A-Za-z_][A-Za-z0-9_]*' || true)
[ -z "$unbraced" ] || fail "unbraced references: $unbraced"
ok "every reference is \${VAR:?message}: no :- or - default, no bare \$VAR"

for name in "${AWS_INPUTS[@]}"; do
  within_bounds "$name" "$(test_value "$name")" || fail "compose.test.env: $name is outside its bound"
done
ok "every compose.test.env value meets its bound"

render "$ENV_FILE" --format json > "$work/render.json" || fail "the full render failed"
ok "the full render succeeds"

for name in "${AWS_INPUTS[@]}"; do
  grep -v "^$name=" "$ENV_FILE" > "$work/without.env"
  if errors=$(render "$work/without.env" --quiet 2>&1); then fail "the render succeeds without $name"; fi
  grep -qF "required variable $name is missing a value" <<< "$errors" || fail "without $name, the error does not name it: $errors"
done
ok "removing any one input fails the render with Compose's required-variable error naming it"

if grep -qiE 'dev-only|devonly' "$work/render.json"; then fail "the render holds a dev-only value: $(grep -oiE '[^"]*(dev-only|devonly)[^"]*' "$work/render.json" | head -3)"; fi
ok "the render holds no dev-only or DevOnly value"

section "criterion 1: the bounds accept their edges and reject just past them"
repeat(){ printf "%${2}s" '' | tr ' ' "$1"; }   # character count
accepts(){ within_bounds "$1" "$2" || fail "$1 rejects $3"; }
rejects(){ ! within_bounds "$1" "$2" || fail "$1 accepts $3"; }
for name in APP_DB_PASSWORD ZITADEL_DB_PASSWORD LOGIN_SESSION_COOKIE_SECRET GATEWAY_ORIGIN_VERIFY; do
  accepts "$name" "$(repeat a 32)" "32 characters"
  accepts "$name" "$(repeat Z 128)" "128 characters"
  rejects "$name" "$(repeat a 31)" "31 characters"
  rejects "$name" "$(repeat a 129)" "129 characters"
  rejects "$name" "$(repeat a 31);" "a ;"
  rejects "$name" "$(repeat a 31)\$" "a \$"
  rejects "$name" "$(repeat a 31)-" "a -"
done
accepts ZITADEL_MASTERKEY "$(repeat a 32)" "32 characters"
rejects ZITADEL_MASTERKEY "$(repeat a 31)" "31 characters"
rejects ZITADEL_MASTERKEY "$(repeat a 33)" "33 characters"
accepts ZITADEL_ADMIN_PASSWORD "Aa1-$(repeat a 28)" "32 characters with each class"
accepts ZITADEL_ADMIN_PASSWORD "Aa1-$(repeat a 124)" "128 characters with each class"
rejects ZITADEL_ADMIN_PASSWORD "Aa1-$(repeat a 27)" "31 characters"
rejects ZITADEL_ADMIN_PASSWORD "Aa1-$(repeat a 125)" "129 characters"
rejects ZITADEL_ADMIN_PASSWORD "Aa1$(repeat a 29)" "no -"
rejects ZITADEL_ADMIN_PASSWORD "aa1-$(repeat a 28)" "no upper-case letter"
rejects ZITADEL_ADMIN_PASSWORD "AA1-$(repeat A 28)" "no lower-case letter"
rejects ZITADEL_ADMIN_PASSWORD "Aaa-$(repeat a 28)" "no digit"
rejects ZITADEL_ADMIN_PASSWORD "Aa1-$(repeat a 27);" "a ;"
accepts LINKING_OTP_HMAC_KEY "$(repeat a 32 | base64)" "32 bytes"
rejects LINKING_OTP_HMAC_KEY "$(repeat a 31 | base64)" "31 bytes"
rejects LINKING_OTP_HMAC_KEY "not base64!" "a value that is not base64"
accepts MAILPIT_UI_AUTH "$(repeat u 32):$(repeat p 32)" "a 32-character user and password"
accepts MAILPIT_UI_AUTH "u:$(repeat p 128)" "a 128-character password"
rejects MAILPIT_UI_AUTH "u:$(repeat p 31)" "a 31-character password"
rejects MAILPIT_UI_AUTH "u:$(repeat p 129)" "a 129-character password"
rejects MAILPIT_UI_AUTH "$(repeat u 33):$(repeat p 32)" "a 33-character user"
rejects MAILPIT_UI_AUTH "$(repeat p 32)" "no user"
rejects MAILPIT_UI_AUTH "u:$(repeat p 32) v:$(repeat p 32)" "two pairs"
for name in APP_HOST AUTH_HOST MAILPIT_HOST DB_HOST; do
  accepts "$name" d111111abcdef8.cloudfront.net "a host name"
  rejects "$name" https://d111111abcdef8.cloudfront.net "a scheme"
  rejects "$name" d111111abcdef8.cloudfront.net:443 "a port"
  rejects "$name" localhost "a single label"
done
accepts AWS_REGION af-south-1 "a region"
rejects AWS_REGION af-south "a name without its number"
accepts OTEL_LOG_GROUP "/$(repeat a 511)" "512 characters"
rejects OTEL_LOG_GROUP "/$(repeat a 512)" "513 characters"
rejects OTEL_LOG_GROUP "/disputedesk otel" "a space"
accepts DATAPROTECTION_SSM_PATH /disputedesk-dataprotection/dev "a path"
rejects DATAPROTECTION_SSM_PATH disputedesk-dataprotection/dev "no leading /"
rejects DATAPROTECTION_SSM_PATH /disputedesk-dataprotection/dev/ "a trailing /"
rejects DATAPROTECTION_SSM_PATH / "only /"
kms_key=00000000-0000-0000-0000-00000000000a
accepts DATAPROTECTION_KMS_KEY_ID "arn:aws:kms:af-south-1:$(repeat 0 12):key/$kms_key" "a key ARN"
accepts DATAPROTECTION_KMS_KEY_ID "$kms_key" "a key ID"
rejects DATAPROTECTION_KMS_KEY_ID alias/aws/ssm "the SSM default key's alias"
rejects DATAPROTECTION_KMS_KEY_ID "arn:aws:kms:af-south-1:$(repeat 0 12):alias/aws/ssm" "an alias ARN"
rejects DATAPROTECTION_KMS_KEY_ID "arn:aws:kms:af-south-1:$(repeat 0 11):key/$kms_key" "an 11-digit account"
rejects RDS_CA_BUNDLE rds-ca-bundle.pem "a relative path"
rejects API_IMAGE ghcr.io/owner/disputedesk-api:sha-aaaaaaaaaaaa "a tag instead of a digest"
rejects API_IMAGE "ghcr.io/owner/disputedesk-api@sha256:$(repeat 0 63)" "a 63-digit digest"
rejects API_IMAGE "docker.io/owner/disputedesk-api@sha256:$(repeat 0 64)" "another registry"
ok "each bound accepts its edges and rejects the value just past them, ; and \$ included"

section "criterion 2: only the AWS services start, and nothing local is left"
q(){ jq -r "$@" "$work/render.json"; }
started=$(q '.services | keys[]')
expected=$(sorted redpanda redpanda-topics zitadel provision zitadel-login mailpit core-banking api worker otel-collector gateway)
[ "$started" = "$expected" ] || fail "the services started without a profile are: $(tr '\n' ' ' <<< "$started")"
ok "started without a profile: $(tr '\n' ' ' <<< "$started")"
COMPOSE_PROFILES=local render "$ENV_FILE" --format json > "$work/local.json"
for name in postgres aspire-dashboard; do
  [ "$(jq -c --arg s "$name" '.services[$s].profiles' "$work/local.json")" = '["local"]' ] || fail "$name is not in the local profile"
done
dependents=$(q '.services | to_entries[] | select((.value.depends_on // {}) | has("postgres") or has("aspire-dashboard")) | .key')
[ -z "$dependents" ] || fail "started services depend on a local-only one: $dependents"
ok "postgres and aspire-dashboard carry profiles: [local], and no started service depends on them"

# Redpanda Console is a local tool, started only on request (docker compose --profile console ...).
[ "$(q '.services | has("redpanda-console")')" = false ] || fail "the AWS render starts redpanda-console without a profile"
[ "$(jq '.services | has("redpanda-console")' "$work/local.json")" = false ] \
  || fail "the AWS render starts redpanda-console under the local profile"
ok "the AWS render starts no redpanda-console, without a profile or under the local profile"
local_services=$(env "${unset_inputs[@]}" docker compose -f docker-compose.yml config --services)
! grep -qx redpanda-console <<< "$local_services" || fail "docker compose up starts redpanda-console without its profile"
console=$(env "${unset_inputs[@]}" docker compose -f docker-compose.yml --profile console config --format json \
  | jq -c '.services["redpanda-console"] // {} | [.profiles, ([(.ports // [])[].host_ip] | unique), has("user")]')
[ "$console" = '[["console"],["127.0.0.1"],false]' ] || fail "redpanda-console's profiles, host addresses and user: $console"
ok "locally, redpanda-console starts only under profiles: [console], publishes on 127.0.0.1 only and keeps the image's user"

# Compose merges maps and appends sequences, so a service the base file defines must replace these whole.
base_services=$(env "${unset_inputs[@]}" docker compose -f docker-compose.yml config --services | tr '\n' ' ')
merged_not_replaced=$(awk -v base=" $base_services " '
  /^services:/ { in_services = 1; next }
  /^[^ #]/ { in_services = 0 }
  in_services && /^  [a-z][a-z0-9-]*:/ { service = $1; sub(":", "", service) }
  in_services && /^    (depends_on|ports|environment):/ && !/!(override|reset)/ && index(base, " " service " ") { print service ": " $1 }
' "$OVERRIDE")
[ -z "$merged_not_replaced" ] || fail "keys merged into the base instead of replaced: $merged_not_replaced"
ok "every depends_on, ports and environment of a base service uses !override or !reset"

published=$(q '[.services | to_entries[] | .key as $s | (.value.ports // [])[] | "\($s) \(.published):\(.target)"] | join(",")')
[ "$published" = "gateway 80:8080" ] || fail "published ports: $published"
ok "the only published port is the gateway's 80:8080"

for pattern in localhost Development sslmode=disable '"BFF_DEVELOPMENT_MODE": "true"'; do
  ! grep -qF -- "$pattern" "$work/render.json" || fail "the render holds $pattern"
done
for name in APP_HOST AUTH_HOST MAILPIT_HOST; do
  host=$(test_value "$name")
  ! grep -qE "http://$host|$host:8080" "$work/render.json" || fail "a public URL for $name uses http:// or :8080"
done
ok "no localhost, Development, sslmode=disable or development mode; no public URL with http:// or :8080"

ca=/run/rds/ca-bundle.pem
[ "$(q '.services.zitadel.environment.ZITADEL_DATABASE_POSTGRES_DSN')" \
  = "postgresql://zitadel:$(test_value ZITADEL_DB_PASSWORD)@$(test_value DB_HOST):5432/zitadel?sslmode=verify-full&sslrootcert=$ca" ] \
  || fail "Zitadel's DSN does not verify RDS with the CA bundle"
for service in api worker; do
  [ "$(q --arg s "$service" '.services[$s].environment.ConnectionStrings__DisputeDesk')" \
    = "Host=$(test_value DB_HOST);Port=5432;Database=disputedesk;Username=disputedesk;Password=$(test_value APP_DB_PASSWORD);SSL Mode=VerifyFull;Root Certificate=$ca" ] \
    || fail "the $service connection string does not verify RDS with the CA bundle"
done
for service in zitadel api worker; do
  mounted=$(q --arg s "$service" --arg ca "$ca" '[.services[$s].volumes[] | select(.target == $ca) | "\(.source) \(.read_only)"] | join(",")')
  [ "$mounted" = "$(test_value RDS_CA_BUNDLE) true" ] || fail "$service does not mount RDS_CA_BUNDLE read-only at $ca: $mounted"
done
ok "Zitadel's DSN (sslmode=verify-full) and the app connection string (SSL Mode=VerifyFull) verify RDS with the CA bundle, mounted read-only"

zitadel=$(q '.services.zitadel.environment | [.ZITADEL_EXTERNALDOMAIN, .ZITADEL_EXTERNALPORT, .ZITADEL_EXTERNALSECURE, .ZITADEL_TLS_ENABLED] | join(" ")')
[ "$zitadel" = "$(test_value AUTH_HOST) 443 true false" ] || fail "Zitadel's external domain, port, secure and TLS: $zitadel"
ok "Zitadel: ExternalDomain \${AUTH_HOST}, port 443, secure, TLS off behind CloudFront"
[ "$(q '.services.api.environment.ForwardedHeaders__AllowedHosts')" = "$(test_value APP_HOST)" ] || fail "the api's AllowedHosts is not APP_HOST"
# Only the gateway may set X-Forwarded-*: any wider network lets another container on the subnet spoof them.
trusted=$(q '.services.api.environment.ForwardedHeaders__TrustedNetwork')
[ "$trusted" = "$GATEWAY_IP/32" ] || fail "the api trusts forwarded headers from $trusted, not only the gateway's $GATEWAY_IP/32"
[ -z "$(q '.services.api.environment | keys[] | select(startswith("Bff__"))')" ] || fail "the api overrides a cookie setting"
ok "the api: AllowedHosts \${APP_HOST}, forwarded headers trusted only from the gateway ($GATEWAY_IP/32), no cookie-name or SecurePolicy override"

for pair in api:API_IMAGE worker:WORKER_IMAGE gateway:GATEWAY_IMAGE core-banking:CORE_BANKING_IMAGE provision:PROVISION_IMAGE; do
  service=${pair%%:*}
  [ "$(q --arg s "$service" '.services[$s].image')" = "$(test_value "${pair#*:}")" ] || fail "$service does not use ${pair#*:}"
  [ "$(q --arg s "$service" '.services[$s] | has("build")')" = false ] || fail "$service still builds"
done
ok "api, worker, gateway, core-banking and provision run the *_IMAGE digests and build nothing"

[ "$(q -c '.networks.default.ipam.config')" = "[{\"subnet\":\"$SUBNET\",\"ip_range\":\"10.201.89.128/25\"}]" ] || fail "the network is not the one fixed subnet"
pinned=$(q '[.services | to_entries[] | select(.value.networks.default.ipv4_address) | "\(.key)=\(.value.networks.default.ipv4_address)"] | join(" ")')
[ "$pinned" = "api=10.201.89.10 gateway=$GATEWAY_IP otel-collector=10.201.89.11" ] || fail "pinned addresses: $pinned"
ok "one network on $SUBNET, dynamic addresses from 10.201.89.128/25, and only api (.10), otel-collector (.11) and gateway (.12) pinned"

for service in otel-collector mailpit; do
  [ "$(q --arg s "$service" '.services[$s] | has("ports")')" = false ] || fail "$service has ports"
done
[ "$(q '.services.mailpit.environment | has("MP_WEBROOT")')" = false ] || fail "Mailpit has MP_WEBROOT"
[ "$(q '.services.mailpit.environment.MP_UI_AUTH')" = "$(test_value MAILPIT_UI_AUTH)" ] || fail "Mailpit's UI auth is not MAILPIT_UI_AUTH"
[ "$(q '.services.gateway.networks.default.aliases // [] | length')" = 0 ] || fail "the gateway has a network alias"
ok "otel-collector and Mailpit publish no port; Mailpit has basic auth and no webroot; the gateway has no alias"

gateway=$(q '.services.gateway | [.environment.NGINX_ENVSUBST_TEMPLATE_DIR, (.volumes[] | select(.target == "/etc/nginx/cloudfront-realip.conf") | "\(.source) \(.read_only)")] | join(" ")')
[ "$gateway" = "/etc/nginx/templates-aws $(test_value GATEWAY_REALIP_CONF) true" ] || fail "the gateway's templates and realip file: $gateway"
ok "the gateway renders the AWS templates and mounts GATEWAY_REALIP_CONF read-only"

for service in api worker; do
  [ "$(q --arg s "$service" '.services[$s].environment.OTEL_EXPORTER_OTLP_ENDPOINT')" = http://otel-collector:4317 ] || fail "$service does not export to the collector"
done
keyring=$(q '.services.api.environment | [.DataProtection__SsmParameterPath, .DataProtection__KmsKeyId, .AWS_REGION] | join(" ")')
[ "$keyring" = "$(test_value DATAPROTECTION_SSM_PATH) $(test_value DATAPROTECTION_KMS_KEY_ID) $(test_value AWS_REGION)" ] || fail "the api's key ring settings: $keyring"
ok "the api and the worker export to the collector; the api keeps its key ring in SSM with the named KMS key"

grep -qx -- --memory=512M <<< "$(q '.services.redpanda.command[]')" || fail "Redpanda does not run with --memory=512M"
total=0
for service in "${LONG_RUNNING[@]}"; do
  bytes=$(q --arg s "$service" '.services[$s].mem_limit // empty')
  [ -n "$bytes" ] || fail "$service has no mem_limit"
  mib=$((bytes / 1048576))
  total=$((total + mib))
  printf '    %-16s %5d MiB\n' "$service" "$mib"
done
printf '    %-16s %5d MiB (budget %d)\n' total "$total" "$MEMORY_BUDGET_MIB"
for service in "${ONE_SHOTS[@]}"; do
  printf '    %-16s %5d MiB (one-shot, done before the api starts)\n' "$service" "$(( $(q --arg s "$service" '.services[$s].mem_limit') / 1048576 ))"
done
(( total <= MEMORY_BUDGET_MIB )) || fail "the long-running services' mem_limit total $total MiB, over $MEMORY_BUDGET_MIB"
ok "Redpanda at --memory=512M; the long-running services' limits total $total MiB, within $MEMORY_BUDGET_MIB"

section "criterion 5: the collector config"
COLLECTOR=infra/otel-collector/config.yaml
! grep -qw debug "$COLLECTOR" || fail "the collector config mentions the debug exporter"
[ "$(grep -c '^      processors: ' "$COLLECTOR")" = 3 ] || fail "the collector config does not have three pipelines"
[ "$(grep -c '^      processors: \[memory_limiter, batch\]$' "$COLLECTOR")" = 3 ] || fail "a pipeline does not run memory_limiter, then batch"
grep -qx '        endpoint: 0.0.0.0:4317' "$COLLECTOR" && grep -qx '        endpoint: 0.0.0.0:4318' "$COLLECTOR" \
  || fail "the OTLP receiver does not listen on 0.0.0.0:4317 and 0.0.0.0:4318"
ok "no debug exporter; memory_limiter then batch in all three pipelines; OTLP on 0.0.0.0:4317 and :4318"
# validate builds the sigv4auth extensions, which look up credentials then (never sending anything): placeholders in the
# environment stand in for the instance role, so it never tries the instance metadata service.
docker run --rm -e "AWS_REGION=$(test_value AWS_REGION)" -e "OTEL_LOG_GROUP=$(test_value OTEL_LOG_GROUP)" \
  -e AWS_ACCESS_KEY_ID=placeholder -e AWS_SECRET_ACCESS_KEY=placeholder \
  -v "$PWD/$COLLECTOR:/etc/otelcol-contrib/config.yaml:ro" "$(q '.services["otel-collector"].image')" \
  validate --config=/etc/otelcol-contrib/config.yaml || fail "otelcol-contrib validate"
ok "otelcol-contrib validate passes with the pinned image"

echo; echo "CHECK OK"
