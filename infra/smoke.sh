#!/usr/bin/env bash
# Developer smoke check for the local Compose platform: a black-box test through the gateway, plus the
# provisioner's read-only checks (verify.sh) for facts invisible from outside.
#   scripts/test-lock.sh run stack <owner> -- infra/smoke.sh
# WARNING: starts and ends with `docker compose down -v`, wiping the shared `disputedesk` stack's data.
# KEEP_UP=1 leaves it running afterwards. Every check fails the run; secrets never reach a command line.
set -euo pipefail
cd "$(dirname "$0")/.."

GW=http://localhost:8080
AUTH=http://auth.localhost:8080          # curl resolves *.localhost to 127.0.0.1 (RFC 6761): the gateway
MAILPIT=http://localhost:${DISPUTEDESK_MAILPIT_PORT:-8025}         # the same host ports as docker-compose.yml
DASHBOARD=http://localhost:${DISPUTEDESK_DASHBOARD_PORT:-18888}
APP_PW=${APP_DB_PASSWORD:-dev-only-app-db}          # the same dev-only defaults as docker-compose.yml
QA_PW=${QA_DB_PASSWORD:-dev-only-qa-db}
ADMIN_PW=${POSTGRES_PASSWORD:-dev-only-postgres}

section(){ printf '\n== %s\n' "$*"; }
fail(){ echo "SMOKE FAIL: $*" >&2; exit 1; }
ok(){ echo "ok  $*"; }
jwt_payload(){ jq -R 'split(".")[1] | gsub("-"; "+") | gsub("_"; "/") | . + ("=" * ((4 - length % 4) % 4)) | @base64d | fromjson'; }
# psql over TCP to the service name, so the scram password check runs (the image trusts loopback and the
# socket). Passwords come from a .pgpass written over stdin inside the container.
pg(){ docker compose exec -T -e PGPASSFILE=/tmp/smoke.pgpass postgres \
        psql -h postgres -U "$1" -d "$2" -w -v ON_ERROR_STOP=1 -qAt -c "$3"; }
expect_error(){ # description pattern user database sql
  local out
  if out=$(pg "$3" "$4" "$5" 2>&1); then fail "$1 succeeded: $out"; fi
  grep -q "$2" <<< "$out" || fail "$1 failed for the wrong reason: $out"
  ok "$1 refused: $(grep -o "$2.*" <<< "$out" | head -1)"
}
in_provisioner(){ local entrypoint=$1; shift; docker compose run --rm --no-deps -T --entrypoint "$entrypoint" provision "$@" 2>/dev/null; }
clients_hash(){ in_provisioner sha256sum /out/zitadel-clients.json | cut -d' ' -f1; }

section "clean slate"
docker compose down -v --remove-orphans >/dev/null 2>&1
# Remove the locally built images too, so `up` has to build them as on a fresh machine (layer cache aside).
docker image rm -f $(docker compose config --images | grep ':local$') >/dev/null 2>&1 || true
echo "volumes and locally built images removed"

section "docker compose up --wait"
start=$SECONDS
docker compose up --wait --wait-timeout 600 >/dev/null 2>&1 || { docker compose ps -a; fail "compose up --wait"; }
echo "up and healthy in $((SECONDS - start)) s"

section "docker compose ps -a"
docker compose ps -a --format 'table {{.Service}}\t{{.Status}}'
! docker compose ps -a --format '{{.Status}}' | grep -qE 'Exited \([1-9]' || fail "a one-shot container failed"

section "memory (docker stats --no-stream)"
stats=$(docker stats --no-stream --format '{{.Name}}\t{{.MemUsage}}' $(docker compose ps -q) | sort)
echo "$stats"
awk -F'\t' '{split($2, a, " / "); v=a[1]; n=v+0; if (v ~ /GiB/) n*=1024; else if (v ~ /KiB/) n/=1024; t+=n}
            END {printf "TOTAL\t%.1f MiB (%.2f GiB)\n", t, t/1024}' <<< "$stats"

section "api readiness through the gateway"
[ "$(curl -fsS "$GW/api/health/ready")" = Healthy ] || fail "api readiness"
ok "GET $GW/api/health/ready -> Healthy"

section "gateway: the SPA and security headers, each exactly once"
curl -fsS -o /dev/null "$GW/" && ok "GET $GW/ -> 200"
for path in / /api/health/live; do
  headers=$(curl -fsSI "$GW$path")
  for h in content-security-policy x-content-type-options x-frame-options referrer-policy permissions-policy cross-origin-opener-policy; do
    [ "$(grep -ic "^$h:" <<< "$headers")" = 1 ] || fail "$path: header $h is not sent exactly once"
    ok "$path $(grep -i "^$h:" <<< "$headers" | tr -d '\r')"
  done
  # Locally GATEWAY_HSTS is empty, and nginx must then send no Strict-Transport-Security at all.
  ! grep -qi '^strict-transport-security:' <<< "$headers" || fail "$path: HSTS sent over plain HTTP"
  ok "$path no strict-transport-security"
done
# The Api owns Cache-Control on its responses; the gateway adds none of its own.
cache_control=$(curl -fsSI "$GW/api/health/live" | grep -i '^cache-control:' | tr -d '\r')
[ "$(grep -c . <<< "$cache_control")" = 1 ] || fail "/api/health/live: not exactly one cache-control: $cache_control"
grep -qi 'no-store' <<< "$cache_control" || fail "/api/health/live: cache-control without no-store: $cache_control"
ok "/api/health/live $cache_control"

section "gateway: the coarse limit's 429"
# Rapid requests exceed 10 r/s with burst 20. The path ends in .json, so the 429 proves its Content-Type does not
# come from the extension.
limited=""
for _ in $(seq 40); do
  response=$(curl -sS -D - -o /dev/null "$GW/api/limit-probe.json" | tr -d '\r')
  if head -1 <<< "$response" | grep -q ' 429'; then limited=$response; break; fi
done
[ -n "$limited" ] || fail "40 rapid requests to /api/ got no 429"
grep -qi '^content-type: application/problem+json$' <<< "$limited" || fail "429 content type: $limited"
grep -qi '^retry-after: 1$' <<< "$limited" || fail "429 has no Retry-After: 1: $limited"
for h in content-security-policy x-content-type-options x-frame-options referrer-policy permissions-policy cross-origin-opener-policy; do
  [ "$(grep -ic "^$h:" <<< "$limited")" = 1 ] || fail "429: header $h is not sent exactly once"
done
ok "429 with application/problem+json, Retry-After: 1 and each security header once"

section "Zitadel discovery and Login v2 at auth.localhost"
discovery=$(curl -fsS "$AUTH/.well-known/openid-configuration")
jq '{issuer, authorization_endpoint, token_endpoint, code_challenge_methods_supported}' <<< "$discovery"
[ "$(jq -r .issuer <<< "$discovery")" = "$AUTH" ] || fail "issuer is not $AUTH"
curl -fsS -o /dev/null "$AUTH/ui/v2/login/loginname" && ok "GET $AUTH/ui/v2/login/loginname -> 200"

clients=$(in_provisioner cat /out/zitadel-clients.json)
project=$(jq -r .Zitadel.ProjectId <<< "$clients")
bff=$(jq -r .Zitadel.Bff.ClientId <<< "$clients")

section "operations machine user: client-credentials token, decoded"
# Basic credentials go to curl as a config on stdin, not as an argument.
token=$(jq -r '.Zitadel.OperationsTools | "user = \"\(.ClientId):\(.ClientSecret)\""' <<< "$clients" \
  | curl -fsS -K - "$AUTH/oauth/v2/token" --data-urlencode grant_type=client_credentials \
      --data-urlencode "scope=openid urn:zitadel:iam:org:project:id:$project:aud urn:zitadel:iam:org:projects:roles" \
  | jq -r .access_token)
claims=$(jwt_payload <<< "$token")
jq --arg p "$project" '{iss, client_id, aud, roles: (.["urn:zitadel:iam:org:project:\($p):roles"] | keys)}' <<< "$claims"
[ "$(jq -c --arg p "$project" '.["urn:zitadel:iam:org:project:\($p):roles"] | keys' <<< "$claims")" \
  = '["dispute.escalate","dispute.read","dispute.resolve","dispute.startReview"]' ] || fail "operations token roles"
ok "JWT access token carries exactly the three action roles and dispute.read"

section "BFF app: authorize (code + PKCE S256), exact redirect URIs"
authorize_lands_on_login(){ # redirect_uri -> 0 when Zitadel accepts it and hands over to Login v2
  local location
  location=$(curl -sS -o /dev/null -w '%{redirect_url}' -G "$AUTH/oauth/v2/authorize" \
    --data-urlencode "client_id=$bff" --data-urlencode "redirect_uri=$1" \
    --data-urlencode response_type=code --data-urlencode scope=openid \
    --data-urlencode code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM --data-urlencode code_challenge_method=S256)
  [[ "$location" == "$AUTH/ui/v2/login/login?authRequest="* ]]; }
for uri in http://localhost:8080/api/signin-oidc http://localhost:5101/api/signin-oidc http://localhost:5102/api/signin-oidc; do
  authorize_lands_on_login "$uri" || fail "registered redirect URI rejected: $uri"
  ok "accepted  $uri"
done
for uri in http://localhost:9999/api/signin-oidc http://localhost:8080/signin-oidc http://localhost:8080/api/signin-oidc/x; do
  ! authorize_lands_on_login "$uri" || fail "unregistered redirect URI accepted: $uri"
  ok "rejected  $uri"
done

section "Zitadel facts invisible from outside (verify.sh, admin API, read-only)"
in_provisioner bash /verify.sh | cut -c1-200 || fail "verify.sh"

section "provisioning is idempotent"
before=$(clients_hash)
log=$(docker compose run --rm --no-deps -T provision 2>&1 | grep '^\[provision\]') || fail "second provisioning run"
echo "$log"
! grep -qE 'created|wrote' <<< "$log" || fail "the second run changed something"
grep -q 'secrets not rotated' <<< "$log" || fail "the second run did not keep the client file"
[ "$(clients_hash)" = "$before" ] || fail "zitadel-clients.json changed"
ok "second run created nothing; zitadel-clients.json unchanged (sha256 ${before:0:12}...)"

section "Postgres roles"
docker compose exec -T postgres sh -c 'umask 077 && cat > /tmp/smoke.pgpass' <<EOF
*:5432:*:disputedesk:$APP_PW
*:5432:*:qa_readonly:$QA_PW
*:5432:*:postgres:$ADMIN_PW
EOF
trap 'docker compose exec -T postgres rm -f /tmp/smoke.pgpass >/dev/null 2>&1 || true' EXIT
# Proves the TCP path really checks passwords (a deliberately wrong one, so not a secret on the command line).
if out=$(docker compose exec -T -e PGPASSWORD=not-the-password postgres \
           psql -h postgres -U qa_readonly -d disputedesk -w -c 'SELECT 1' 2>&1); then
  fail "a wrong password was accepted: $out"
fi
grep -q 'password authentication failed' <<< "$out" || fail "wrong password refused for the wrong reason: $out"
ok "a wrong password for qa_readonly over TCP is refused (scram)"
pg disputedesk disputedesk "CREATE TABLE smoke_probe (id int); INSERT INTO smoke_probe VALUES (1);"
[ "$(pg qa_readonly disputedesk 'SELECT count(*) FROM smoke_probe')" = 1 ] || fail "qa_readonly cannot read app tables"
ok "qa_readonly SELECTs a table the app role created later (default privileges)"
[ "$(pg qa_readonly disputedesk 'SHOW default_transaction_read_only')" = on ] || fail "qa_readonly sessions are not read-only"
ok "qa_readonly sessions default to read-only"
# The read-only default can be switched off per session, so the privilege itself must refuse writes.
expect_error "qa_readonly INSERT in a READ WRITE transaction" "permission denied for table" \
  qa_readonly disputedesk "BEGIN READ WRITE; INSERT INTO smoke_probe VALUES (2); COMMIT;"
expect_error "qa_readonly connecting to the zitadel database" "permission denied for database" \
  qa_readonly zitadel "SELECT 1"
flags=$(pg postgres postgres "SELECT rolsuper, rolcreatedb, rolcreaterole FROM pg_roles WHERE rolname = 'zitadel'")
[ "$flags" = "f|f|f" ] || fail "zitadel role flags (super, createdb, createrole) are $flags"
ok "zitadel role is not superuser, cannot create databases or roles"
pg disputedesk disputedesk "DROP TABLE smoke_probe"

section "Redpanda topic"
topics=$(docker compose exec -T redpanda rpk topic list)
echo "$topics"
grep -qE '^disputedesk\.disputes[[:space:]]+3[[:space:]]+1$' <<< "$topics" || fail "topic disputedesk.disputes (3 partitions, 1 replica)"

section "Mailpit and Aspire dashboard"
curl -fsS "$MAILPIT/api/v1/info" | jq -ec '{mailpit: .Version}' || fail "mailpit"
curl -fsS -o /dev/null "$DASHBOARD/health" && ok "Aspire dashboard /health -> 200"
[ "$(curl -sS -o /dev/null -w '%{redirect_url}' "$DASHBOARD/")" = "$DASHBOARD/structuredlogs" ] \
  || fail "Aspire dashboard asks for sign-in"
ok "Aspire dashboard / -> /structuredlogs (anonymous, no sign-in)"

if [ "${KEEP_UP:-0}" != "1" ]; then
  section "docker compose down -v"
  trap - EXIT
  docker compose down -v --remove-orphans >/dev/null 2>&1 && echo "stopped and volumes removed"
fi
echo; echo "SMOKE OK"
