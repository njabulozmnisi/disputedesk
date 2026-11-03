#!/usr/bin/env bash
# Developer smoke check for the local Compose platform: a black-box test through the gateway, plus the
# provisioner's read-only checks (verify.sh) for facts invisible from outside.
#   scripts/test-lock.sh run stack <owner> -- infra/tests/smoke.sh
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
# Remove the locally built images too, so `up` has to build them as on a fresh machine (layer cache aside). The
# build profile adds the build-only spa image, which the gateway's build takes its SPA files from.
docker image rm -f $(docker compose --profile build config --images | grep ':local$') >/dev/null 2>&1 || true
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
# index.html (for / and client routes) is revalidated on every load; the hashed build files are cached for a year
# (RFC 9111 5.2.2.4, RFC 8246). Each of these locations repeats the security headers, checked once each above for /.
index=$(curl -fsS "$GW/")
# grep finds nothing in a broken build; || true lets the line below say so instead of set -e ending the run silently.
asset=$(grep -oE '/assets/[^"]+\.js' <<< "$index" | sed -n 1p) || true
[ -n "$asset" ] || fail "index.html names no /assets/ script"
# The CSP's base-uri 'none' would block a base element, so the page must hold none (CSP Level 3, base-uri).
! grep -qi '<base' <<< "$index" || fail "index.html holds a <base> element"
ok "index.html holds no <base> element"
for path_cache in "/|no-cache" "/disputes|no-cache" "$asset|public, max-age=31536000, immutable"; do
  path=${path_cache%%|*} expected=${path_cache#*|}
  headers=$(curl -fsSI "$GW$path" | tr -d '\r')
  [ "$(grep -ic '^cache-control:' <<< "$headers")" = 1 ] || fail "$path: not exactly one cache-control"
  grep -qix "cache-control: $expected" <<< "$headers" || fail "$path: cache-control is not $expected"
  for h in content-security-policy x-content-type-options x-frame-options referrer-policy permissions-policy cross-origin-opener-policy; do
    [ "$(grep -ic "^$h:" <<< "$headers")" = 1 ] || fail "$path: header $h is not sent exactly once"
  done
  ok "$path cache-control: $expected, with each security header once"
done
# The Api owns Cache-Control on its responses; the gateway adds none of its own. A transport error and a missing header
# each fail with their own message (a grep inside the request's pipeline would end the run silently under set -e).
live_headers=$(curl -fsSI "$GW/api/health/live" | tr -d '\r') || fail "/api/health/live: request failed"
cache_control=$(grep -i '^cache-control:' <<< "$live_headers") || fail "/api/health/live: no cache-control"
[ "$(grep -c . <<< "$cache_control")" = 1 ] || fail "/api/health/live: not exactly one cache-control: $cache_control"
grep -qi 'no-store' <<< "$cache_control" || fail "/api/health/live: cache-control without no-store: $cache_control"
ok "/api/health/live $cache_control"

section "gateway: Scalar and the runtime document (AD-26)"
header_value(){ grep -i "^$1:" | cut -d' ' -f2-; }   # header name, response headers on stdin (CR stripped)
spa_headers=$(curl -fsSI "$GW/" | tr -d '\r')
scalar_hash=$(grep -o "'sha256-[^']*'" infra/images/gateway/templates/scalar-headers.conf.template)
scalar_csp="default-src 'self'; script-src 'self' $scalar_hash; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'"
redirect=$(curl -sS -D - -o /dev/null "$GW/scalar" | tr -d '\r')
head -1 <<< "$redirect" | grep -q ' 302' || fail "/scalar is not a 302: $redirect"
[ "$(header_value location <<< "$redirect")" = scalar/ ] || fail "/scalar does not redirect to scalar/: $redirect"
ok "GET $GW/scalar -> 302 scalar/"
for path in /scalar/ /scalar/scalar.js /scalar/scalar.aspnetcore.js /scalar/favicon.svg /openapi/v1.json; do
  curl -fsS -o /dev/null "$GW$path" || fail "GET $path"
  ok "GET $GW$path -> 200"
done
# The document's server URL comes from the gateway's Host, so Scalar's requests stay same-origin (connect-src 'self').
[ "$(curl -fsS "$GW/openapi/v1.json" | jq -r '.servers[0].url')" = "$GW/" ] || fail "/openapi/v1.json server URL is not $GW/"
ok "/openapi/v1.json servers[0].url = $GW/"
# Only v1.json reaches the api; any other /openapi/ path is the SPA's index.html.
[ "$(curl -fsS "$GW/openapi/v2.json")" = "$(curl -fsS "$GW/")" ] || fail "/openapi/v2.json is not the SPA"
ok "GET $GW/openapi/v2.json -> the SPA"
# GET, not HEAD: the api maps the page as GET only and answers HEAD with 405.
headers=$(curl -fsS -D - -o /dev/null "$GW/scalar/" | tr -d '\r')
for h in content-security-policy x-content-type-options x-frame-options referrer-policy permissions-policy cross-origin-opener-policy; do
  [ "$(grep -ic "^$h:" <<< "$headers")" = 1 ] || fail "/scalar/: header $h is not sent exactly once"
  if [ "$h" = content-security-policy ]; then expected=$scalar_csp; else expected=$(header_value "$h" <<< "$spa_headers") || fail "/ has no $h"; fi
  [ "$(header_value "$h" <<< "$headers")" = "$expected" ] || fail "/scalar/: $h is not: $expected"
  ok "/scalar/ $(grep -i "^$h:" <<< "$headers")"
done
! grep -qi '^strict-transport-security:' <<< "$headers" || fail "/scalar/: HSTS sent over plain HTTP"
ok "/scalar/ no strict-transport-security"
! grep -qi '<base' <<< "$(curl -fsS "$GW/scalar/")" || fail "the Scalar page holds a <base> element"
ok "the Scalar page holds no <base> element"

section "gateway: the coarse limit's 429"
# The limit is 10 r/s with burst 20. Each burst sends 60 requests at once (curl --parallel), at most 3 bursts, and the
# probe stops at the first burst that gets a 429 and says what it took, so a failure shows what this host managed.
# The 429 is @rate_limited's on every location, with the SPA's CSP and Cache-Control: no-store (AD-23).
expect_429(){ # path
  local burst results tally limited=""
  for burst in 1 2 3; do
    # One JSON object per request, its status and its headers (curl --write-out %{header_json}: lower-case names, each
    # value in an array).
    results=$(curl -sS --parallel --parallel-max 60 -o /dev/null -w '{"status":%{http_code},"headers":%{header_json}}\n' \
      "$GW$1?burst=$burst&request=[1-60]") || fail "$1: a request in burst $burst failed"
    limited=$(jq -rn 'first(inputs | select(.status == 429)) | .headers | to_entries[] | "\(.key): \(.value[])"' <<< "$results")
    [ -z "$limited" ] || break
  done
  if [ -z "$limited" ]; then
    # The statuses of the last burst, as "200 x60", so the failure shows what this host managed.
    tally=$(jq -rn '[inputs.status] | group_by(.) | map("\(.[0]) x\(length)") | join(", ")' <<< "$results")
    fail "$1: no 429 in 3 bursts of 60 concurrent requests (180 requests); the last burst got $tally"
  fi
  echo "$1: a 429 after $burst burst(s) of 60 concurrent requests ($((burst * 60)) requests; $(jq -n '[inputs | select(.status == 429)] | length' <<< "$results") limited in the last burst)"
  if ! { [ "$(grep -ic '^content-type:' <<< "$limited")" = 1 ] && grep -qix 'content-type: application/problem+json' <<< "$limited"; }; then
    fail "$1 429 content type: $limited"
  fi
  grep -qix 'retry-after: 1' <<< "$limited" || fail "$1 429 has no Retry-After: 1: $limited"
  if ! { [ "$(grep -ic '^cache-control:' <<< "$limited")" = 1 ] && grep -qix 'cache-control: no-store' <<< "$limited"; }; then
    fail "$1 429: not exactly one Cache-Control: no-store: $limited"
  fi
  for h in content-security-policy x-content-type-options x-frame-options referrer-policy permissions-policy cross-origin-opener-policy; do
    [ "$(grep -ic "^$h:" <<< "$limited")" = 1 ] || fail "$1 429: header $h is not sent exactly once"
  done
  [ "$(header_value content-security-policy <<< "$limited")" = "$(header_value content-security-policy <<< "$spa_headers")" ] \
    || fail "$1 429 does not carry the SPA's CSP: $limited"
  ok "$1 429 with application/problem+json, Retry-After: 1, Cache-Control: no-store, the SPA's CSP and each security header once"
}
# The path ends in .json, so the 429 proves its Content-Type does not come from the extension.
expect_429 /api/limit-probe.json
expect_429 /scalar/

section "Zitadel discovery and the hosted login at auth.localhost"
discovery=$(curl -fsS "$AUTH/.well-known/openid-configuration")
jq '{issuer, authorization_endpoint, token_endpoint, code_challenge_methods_supported}' <<< "$discovery"
[ "$(jq -r .issuer <<< "$discovery")" = "$AUTH" ] || fail "issuer is not $AUTH"
curl -fsS -o /dev/null "$AUTH/ui/login/loginname" && ok "GET $AUTH/ui/login/loginname -> 200"
# The branding changes no framing setting: the hosted login still refuses to be framed (the hosted login sends both headers).
login_headers=$(curl -fsS -D - -o /dev/null "$AUTH/ui/login/loginname" | tr -d '\r')
grep -i '^content-security-policy:' <<< "$login_headers" | grep -q "frame-ancestors 'none'" \
  || fail "hosted login: no frame-ancestors 'none' in its Content-Security-Policy"
if ! { [ "$(grep -ic '^x-frame-options:' <<< "$login_headers")" = 1 ] && grep -qix 'x-frame-options: deny' <<< "$login_headers"; }; then
  fail "hosted login: not exactly one X-Frame-Options: deny"
fi
ok "hosted login: frame-ancestors 'none' and X-Frame-Options: deny once"
# The identity origin's baseline from the gateway, once each: the upstreams' own HSTS and nosniff are hidden, and locally
# GATEWAY_HSTS is empty, so no HSTS at all; frame-ancestors 'none' is a policy of its own beside Zitadel's (CSP Level 3,
# multiple policies). Referrer-Policy once: the gateway's replaces the login's weaker origin-when-cross-origin on Login
# v2 and fills in for discovery, which sends none; Zitadel's own same-origin passes through alone on its v1 login.
for path_referrer in "/.well-known/openid-configuration strict-origin-when-cross-origin" "/ui/login/loginname same-origin" \
                     "/ui/login/loginname strict-origin-when-cross-origin"; do
  read -r path referrer <<< "$path_referrer"
  headers=$(curl -fsS -D - -o /dev/null "$AUTH$path" | tr -d '\r')
  [ "$(header_value x-content-type-options <<< "$headers")" = nosniff ] || fail "$AUTH$path: not exactly one X-Content-Type-Options: nosniff"
  # Two headers give two lines, so this also proves the policy is sent at most once.
  policy=$(header_value referrer-policy <<< "$headers") || policy=none
  [ "$policy" = "$referrer" ] || fail "$AUTH$path: Referrer-Policy is not exactly one $referrer: $policy"
  [ "$(grep -icxF "content-security-policy: frame-ancestors 'none'" <<< "$headers")" = 1 ] \
    || fail "$AUTH$path: not exactly one Content-Security-Policy: frame-ancestors 'none'"
  ! grep -qi '^strict-transport-security:' <<< "$headers" || fail "$AUTH$path: HSTS sent over plain HTTP"
  ok "$AUTH$path: nosniff and frame-ancestors 'none' once each, Referrer-Policy $referrer; no HSTS"
done

clients=$(in_provisioner cat /out/zitadel-clients.json)
project=$(jq -r .Zitadel.ProjectId <<< "$clients")
web_app=$(jq -r .Zitadel.WebApp.ClientId <<< "$clients")

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

section "web app: authorize (code + PKCE S256), exact redirect URIs"
authorize_lands_on_login(){ # redirect_uri -> 0 when Zitadel accepts it and hands over to the hosted login
  local location
  location=$(curl -sS -o /dev/null -w '%{redirect_url}' -G "$AUTH/oauth/v2/authorize" \
    --data-urlencode "client_id=$web_app" --data-urlencode "redirect_uri=$1" \
    --data-urlencode response_type=code --data-urlencode scope=openid \
    --data-urlencode code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM --data-urlencode code_challenge_method=S256)
  [[ "$location" == "$AUTH/ui/login/login?authRequest="* ]]; }
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
! grep -qE 'created|wrote|colours updated|uploaded' <<< "$log" || fail "the second run changed something"
grep -q 'label policy colours are current' <<< "$log" || fail "the second run did not find the label policy current"
grep -q 'secrets not rotated' <<< "$log" || fail "the second run did not keep the client file"
[ "$(clients_hash)" = "$before" ] || fail "zitadel-clients.json changed"
ok "second run created nothing; zitadel-clients.json unchanged (sha256 ${before:0:12}...)"
in_provisioner bash /verify.sh | grep -q '^PASS  label policy: active, from tokens.css' \
  || fail "the active label policy changed after the second run"
ok "the active label policy still matches tokens.css after the second run"

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
