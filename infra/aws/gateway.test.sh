#!/usr/bin/env bash
# The AWS gateway's edge rules (story 7.4, criteria 3, 4, 6 and 7), offline: the gateway image as CI publishes it, with
# the environment infra/aws/compose.yml gives it, on a throwaway Docker network with a real Mailpit and an echo server
# that stands in for the api, Zitadel and the login and answers with the headers it received. Clients run on the same
# network at fixed addresses, one inside test-realip.conf's range (as CloudFront would be) and one outside it.
# It also starts the image with the local templates, for their cache headers (criterion 7).
# Story 7.12's edge hardening (criteria 1 to 4, 6 and 7) runs on both gateways.
#   scripts/test-lock.sh run stack <owner> -- infra/aws/gateway.test.sh
# Needs docker (with Compose v2) and jq; binds no host port. Every check fails the run; everything it starts is removed.
set -euo pipefail
cd "$(dirname "$0")/../.."
# shellcheck source=infra/aws/input-bounds.sh
. infra/aws/input-bounds.sh

ENV_FILE=infra/aws/compose.test.env
SPA_IMAGE=disputedesk-spa:aws-test            # built with BROWSER_TELEMETRY=off, as the publish job builds it
LOCAL_SPA_IMAGE=disputedesk-spa:local-test    # the default build, as docker-compose.yml builds it
IMAGE=disputedesk-gateway:aws-test            # with the SPA from SPA_IMAGE
LOCAL_IMAGE=disputedesk-gateway:local-test    # with the SPA from LOCAL_SPA_IMAGE
run=disputedesk-gateway-test-$$
SUBNET=192.0.2.0/24                           # a documentation range (RFC 5737), so it clashes with no real network
TRUSTED_IP=192.0.2.20                         # inside test-realip.conf's 192.0.2.16/28
UNTRUSTED_IP=192.0.2.100

section(){ printf '\n== %s\n' "$*"; }
fail(){ echo "GATEWAY TEST FAIL: $*" >&2; exit 1; }
ok(){ echo "ok  $*"; }
test_value(){ grep "^$1=" "$ENV_FILE" | cut -d= -f2-; }

APP=$(test_value APP_HOST)
AUTH=$(test_value AUTH_HOST)
MAIL=$(test_value MAILPIT_HOST)
VERIFY=$(test_value GATEWAY_ORIGIN_VERIFY)
MAILPIT_CREDENTIALS=$(test_value MAILPIT_UI_AUTH)

work=$(mktemp -d)
cleanup(){
  docker rm -f "$run-gateway" "$run-local" "$run-echo" "$run-mailpit" "$run-trusted" "$run-untrusted" >/dev/null 2>&1 || true
  docker network rm "$run" >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

section "the gateway image, and the AWS render"
# Each gateway takes its SPA files from the SPA image through the named context spa, as CI and Compose pass it.
docker build -q --build-arg BROWSER_TELEMETRY=off -t "$SPA_IMAGE" frontend >/dev/null
docker build -q -t "$LOCAL_SPA_IMAGE" frontend >/dev/null
docker build -q --build-context "spa=docker-image://$SPA_IMAGE" -t "$IMAGE" infra/nginx >/dev/null
docker build -q --build-context "spa=docker-image://$LOCAL_SPA_IMAGE" -t "$LOCAL_IMAGE" infra/nginx >/dev/null
ok "built $IMAGE (its SPA built with BROWSER_TELEMETRY=off) and $LOCAL_IMAGE (the default)"
unset_inputs=()
for name in "${AWS_INPUTS[@]}"; do unset_inputs+=(-u "$name"); done
env "${unset_inputs[@]}" docker compose -f docker-compose.yml -f infra/aws/compose.yml --env-file "$ENV_FILE" config --format json > "$work/aws.json"
env "${unset_inputs[@]}" docker compose -f docker-compose.yml config --format json > "$work/local.json"
environment_of(){ jq -r --arg s "$2" '.services[$s].environment | to_entries[] | "\(.key)=\(.value)"' "$1"; }
environment_of "$work/aws.json" gateway > "$work/gateway.env"
environment_of "$work/local.json" gateway > "$work/local-gateway.env"
environment_of "$work/aws.json" mailpit > "$work/mailpit.env"
# Compose turns $$ into $ when it runs the health check.
healthcheck=$(jq -r '.services.gateway.healthcheck.test[1]' "$work/aws.json" | sed 's/\$\$/$/g')

section "start: echo upstream, Mailpit, both gateways, two clients"
cat > "$work/echo.conf" <<'EOF'
pid /tmp/echo.pid;
events {}
http {
  client_body_temp_path /tmp/echo_client;
  proxy_temp_path /tmp/echo_proxy;
  fastcgi_temp_path /tmp/echo_fastcgi;
  uwsgi_temp_path /tmp/echo_uwsgi;
  scgi_temp_path /tmp/echo_scgi;
  access_log /dev/stdout;
  # The api and Zitadel (8080) and the dashboard (18890): Cache-Control: no-store, as the Api sends on every response.
  server {
    listen 8080;
    listen 18890;
    location / {
      default_type text/plain;
      add_header Cache-Control "no-store";
      return 200 "port=$server_port host=$host fhost=$http_x_forwarded_host xff=$http_x_forwarded_for proto=$http_x_forwarded_proto verify=$http_x_origin_verify forwarded=$http_forwarded\n";
    }
    # A failing api for the gateway's own 502 (7.12 criterion 7): the connection is closed without a response (nginx's
    # non-standard code 444, ngx_http_rewrite_module), which the gateway reports at once as 502. /api/health/* arrives
    # here as /health/*.
    location = /api/upstream-closes    { return 444; }
    location = /health/upstream-closes { return 444; }
    # Zitadel's v1 login sends its own Referrer-Policy, which the identity origin passes through.
    location /ui/login/ {
      default_type text/plain;
      add_header Referrer-Policy "same-origin";
      return 200 "port=$server_port\n";
    }
  }
  # The login (3000): the headers zitadel-login:v4.20.0 sends on /ui/v2/login/* (its routes-manifest.json).
  server {
    listen 3000;
    location / {
      default_type text/plain;
      add_header Strict-Transport-Security "max-age=63072000; includeSubDomains; preload";
      add_header Referrer-Policy "origin-when-cross-origin";
      add_header X-Content-Type-Options "nosniff";
      add_header X-Frame-Options "deny";
      add_header X-XSS-Protection "1; mode=block";
      return 200 "port=$server_port host=$host fhost=$http_x_forwarded_host xff=$http_x_forwarded_for proto=$http_x_forwarded_proto verify=$http_x_origin_verify forwarded=$http_forwarded\n";
    }
  }
}
EOF
# The echo server runs as the image's non-root user, which must be able to read its config through the mount.
chmod 755 "$work" && chmod 644 "$work/echo.conf"
docker network create --subnet "$SUBNET" "$run" >/dev/null
docker run -d --name "$run-echo" --network "$run" \
  --network-alias api --network-alias zitadel --network-alias zitadel-login --network-alias aspire-dashboard \
  -v "$work/echo.conf:/echo.conf:ro" --entrypoint nginx "$IMAGE" -c /echo.conf -g 'daemon off;' >/dev/null
docker run -d --name "$run-mailpit" --network "$run" --network-alias mailpit --env-file "$work/mailpit.env" \
  "$(jq -r .services.mailpit.image "$work/aws.json")" >/dev/null
docker run -d --name "$run-gateway" --network "$run" --env-file "$work/gateway.env" \
  -v "$PWD/infra/aws/test-realip.conf:/etc/nginx/cloudfront-realip.conf:ro" "$IMAGE" >/dev/null
docker run -d --name "$run-local" --network "$run" --env-file "$work/local-gateway.env" "$LOCAL_IMAGE" >/dev/null
for client in trusted untrusted; do
  ip=$TRUSTED_IP; [ "$client" = trusted ] || ip=$UNTRUSTED_IP
  docker run -d --name "$run-$client" --network "$run" --ip "$ip" --entrypoint sleep "$IMAGE" 1800 >/dev/null
done

GW=http://$run-gateway:8080
LOCAL=http://$run-local:8080
from(){ local client=$1; shift; docker exec "$run-$client" curl -sS --max-time 5 "$@"; }
# status CLIENT HOST PATH [curl options...]: the status code of one request to the AWS gateway
status(){ local client=$1 host=$2 path=$3; shift 3; from "$client" -o /dev/null -w '%{http_code}' -H "Host: $host" "$@" "$GW$path"; }
# echoed CLIENT HOST PATH [curl options...]: the echo server's answer to a request with the right origin header
echoed(){ local client=$1 host=$2 path=$3; shift 3; from "$client" -H "Host: $host" -H "X-Origin-Verify: $VERIFY" "$@" "$GW$path"; }
# headers HOST PATH [curl options...]: the response headers (CR stripped) of a GET with the right origin header
headers(){ local host=$1 path=$2; shift 2; from trusted -o /dev/null -D - -H "Host: $host" -H "X-Origin-Verify: $VERIFY" "$@" "$GW$path" | tr -d '\r'; }
# Read whole before grep -q, which exits at the first match and would otherwise end docker logs with SIGPIPE.
upstream_saw(){ grep -qF "$1" <<< "$(docker logs "$run-echo" 2>&1)"; }
once(){ [ "$(grep -ic "^$1:" <<< "$2")" = 1 ] || fail "$3: $1 is not sent exactly once"; }
# exactly NAME VALUE RESPONSE LABEL: the header is sent once, with exactly this value
exactly(){ once "$1" "$3" "$4"; grep -qixF "$1: $2" <<< "$3" || fail "$4: $1 is not: $2"; }
# status_is STATUS RESPONSE LABEL
status_is(){ head -1 <<< "$2" | grep -q " $1 " || fail "$3: not $1: $(head -1 <<< "$2")"; }
# response_headers BASE HOST PATH [curl options...]: the status line and headers (CR stripped) of one request, sent
# with no origin-verify header unless the options add it
response_headers(){ local base=$1 host=$2 path=$3; shift 3; from trusted -o /dev/null -D - -H "Host: $host" "$@" "$base$path" | tr -d '\r'; }
# app_headers_once LABEL HSTS-COUNT RESPONSE: each of the app's security headers once, and HSTS the given number of times
app_headers_once(){
  local label=$1 hsts=$2 response=$3 header
  for header in content-security-policy x-content-type-options x-frame-options referrer-policy permissions-policy cross-origin-opener-policy; do
    once "$header" "$response" "$label"
  done
  [ "$(grep -ic '^strict-transport-security:' <<< "$response")" = "$hsts" ] || fail "$label: Strict-Transport-Security not sent $hsts time(s)"
}

for _ in $(seq 60); do
  [ "$(status trusted "$APP" / -H "X-Origin-Verify: $VERIFY")" = 200 ] \
    && [ "$(from trusted -o /dev/null -w '%{http_code}' "$LOCAL/")" = 200 ] \
    && docker exec "$run-mailpit" /mailpit readyz >/dev/null 2>&1 && break
  sleep 1
done
[ "$(status trusted "$APP" / -H "X-Origin-Verify: $VERIFY")" = 200 ] || { docker logs "$run-gateway"; fail "the AWS gateway did not start"; }
ok "both gateways and Mailpit are up"

section "criterion 3: the rendered config, CloudFront's client address and scheme"
docker exec "$run-gateway" nginx -t 2>&1 | grep -q 'test is successful' || fail "nginx -t on the rendered AWS config"
ok "nginx -t passes on the rendered AWS config"
rendered=$(docker exec "$run-gateway" nginx -T 2>/dev/null)
! grep -q set_real_ip_from <<< "$(grep -v '^[[:space:]]*#' infra/nginx/templates-aws/nginx.conf.template)" \
  || fail "the AWS template sets a trusted range itself"
grep -qx '  include /etc/nginx/cloudfront-realip.conf;' <<< "$rendered" || fail "the realip file is not included"
grep -qx '  real_ip_header CloudFront-Viewer-Address;' <<< "$rendered" || fail "real_ip_header is not CloudFront-Viewer-Address"
grep -qx '  real_ip_recursive off;' <<< "$rendered" || fail "real_ip_recursive is not off"
! grep -q 'proxy_add_x_forwarded_for' <<< "$rendered" || fail "a location appends to a client-sent X-Forwarded-For"
ok "trusted ranges only from the included file, at http level for all three server blocks; real_ip_header CloudFront-Viewer-Address; recursive off"

viewer=(-H "CloudFront-Viewer-Address: 198.51.100.10:46532" -H "CloudFront-Forwarded-Proto: https")
for host_path in "$APP /api/realip-app" "$AUTH /oauth/v2/realip-auth"; do
  read -r host path <<< "$host_path"
  answer=$(echoed trusted "$host" "$path" "${viewer[@]}")
  grep -qF "host=$host fhost=$host xff=198.51.100.10 proto=https verify=" <<< "$answer" || fail "$host from CloudFront: $answer"
done
ok "from a trusted address, 198.51.100.10:46532 reaches the upstream as X-Forwarded-For: 198.51.100.10, with Host and X-Forwarded-Host fixed"
answer=$(echoed untrusted "$APP" /api/realip-untrusted "${viewer[@]}")
grep -qF "xff=$UNTRUSTED_IP proto" <<< "$answer" || fail "a spoofed CloudFront-Viewer-Address from outside the ranges was trusted: $answer"
ok "from outside the ranges, CloudFront-Viewer-Address is ignored: X-Forwarded-For is the socket address"
answer=$(echoed trusted "$APP" /api/realip-chain "${viewer[@]}" -H "X-Forwarded-For: 203.0.113.9")
grep -qF "xff=198.51.100.10 proto" <<< "$answer" || fail "a client-sent X-Forwarded-For reached the upstream: $answer"
ok "a client-sent X-Forwarded-For: 203.0.113.9 is dropped; the upstream sees only the viewer address"
for case in "https:https" "http:http" "none:https" "gopher:https"; do
  sent=${case%%:*} expected=${case#*:}
  proto=(); [ "$sent" = none ] || proto=(-H "CloudFront-Forwarded-Proto: $sent")
  answer=$(echoed trusted "$APP" "/api/proto-$sent" ${proto[@]+"${proto[@]}"})
  grep -qF "proto=$expected verify=" <<< "$answer" || fail "CloudFront-Forwarded-Proto $sent: $answer"
done
ok "X-Forwarded-Proto comes from CloudFront-Forwarded-Proto; none (or anything but http) is https, never empty"

section "criterion 3: the origin-verify header"
for host_path in "$APP /api/verify-app" "$AUTH /oauth/v2/verify-auth" "$MAIL /api/v1/verify-mail"; do
  read -r host path <<< "$host_path"
  [ "$(status trusted "$host" "$path-missing")" = 403 ] || fail "$host without X-Origin-Verify"
  [ "$(status trusted "$host" "$path-wrong" -H "X-Origin-Verify: wrong-$VERIFY")" = 403 ] || fail "$host with a wrong X-Origin-Verify"
  { ! upstream_saw "$path-missing" && ! upstream_saw "$path-wrong"; } || fail "a refused request to $host reached an upstream"
done
{ [ "$(status trusted "$APP" /api/verify-app-right -H "X-Origin-Verify: $VERIFY")" = 200 ] && upstream_saw /api/verify-app-right; } || fail "the app host refused the right header"
{ [ "$(status trusted "$AUTH" /oauth/v2/verify-auth-right -H "X-Origin-Verify: $VERIFY")" = 200 ] && upstream_saw /oauth/v2/verify-auth-right; } || fail "the identity host refused the right header"
[ "$(status trusted "$MAIL" /api/v1/messages -H "X-Origin-Verify: $VERIFY")" = 401 ] || fail "the right header did not reach Mailpit (its own 401)"
ok "on all three hosts, no X-Origin-Verify and a wrong one get 403 before any upstream; the right one passes"

for host_path in "$APP /api/strip" "$APP /api/health/strip" "$APP /scalar" "$APP /scalar/strip" "$APP /openapi/v1.json" \
                 "$AUTH /ui/v2/login/strip" "$AUTH /oauth/v2/strip" "$AUTH /.well-known/openid-configuration"; do
  read -r host path <<< "$host_path"
  answer=$(echoed trusted "$host" "$path" -H "Forwarded: for=203.0.113.9;host=evil.example;proto=http")
  grep -q 'verify= ' <<< "$answer" || fail "$host$path passed X-Origin-Verify upstream: $answer"
  grep -q 'forwarded=$' <<< "$answer" || fail "$host$path passed the client's Forwarded header upstream: $answer"
done
# Mailpit echoes nothing, so its location (like every other) is also checked in the template: X-Origin-Verify below,
# and the client's Forwarded in PlatformScriptTests.GatewayTemplate_ProxiedLocations_DropTheClientsForwardedHeader.
unstripped=$(awk '
  /^    location / { block = ""; in_location = 1 }
  in_location { block = block $0 "\n" }
  in_location && /^    }$/ { if (block ~ /proxy_pass/ && block !~ /proxy_set_header X-Origin-Verify "";/) print block; in_location = 0 }
' infra/nginx/templates-aws/nginx.conf.template)
[ -z "$unstripped" ] || fail "a proxied location keeps X-Origin-Verify: $unstripped"
! grep -qi 'origin_verify' <<< "$(grep -E '^[[:space:]]*log_format' -A2 <<< "$rendered")" || fail "a log format includes the origin-verify header"
! grep -qF "$VERIFY" <<< "$(docker logs "$run-gateway" 2>&1)" || fail "the gateway's log holds the origin-verify value"
ok "every proxied location clears X-Origin-Verify and the client's Forwarded (RFC 7239), so the upstream sees neither; no log format or log line holds the origin-verify value"

# shellcheck disable=SC2016 # the health check's own text: the gateway container's shell expands it, not this one
grep -qF 'X-Origin-Verify: $GATEWAY_ORIGIN_VERIFY' <<< "$healthcheck" || fail "the health check does not send the origin-verify header"
docker exec "$run-gateway" sh -c "$healthcheck" || fail "the gateway's health check fails: $healthcheck"
ok "the gateway's health check sends the origin-verify header and passes"

section "7.12 criterion 1: HSTS with includeSubDomains on every response nginx sends in AWS, none locally"
HSTS='max-age=63072000; includeSubDomains'   # two years, as long as the login's own value (RFC 6797 6.1.1, 6.1.2)
LOCAL_APP=$(grep '^GATEWAY_APP_HOST=' "$work/local-gateway.env" | cut -d= -f2-)
LOCAL_AUTH=$(grep '^GATEWAY_AUTH_HOST=' "$work/local-gateway.env" | cut -d= -f2-)
for host_path in "$APP /" "$AUTH /.well-known/openid-configuration" "$MAIL /"; do
  read -r host path <<< "$host_path"
  exactly strict-transport-security "$HSTS" "$(headers "$host" "$path")" "$host$path"
  response=$(response_headers "$GW" "$host" "$path")
  status_is 403 "$response" "$host$path without X-Origin-Verify"
  exactly strict-transport-security "$HSTS" "$response" "$host$path 403"
done
for host_path in "$APP /otlp/v1/traces" "$AUTH /ui/console"; do
  read -r host path <<< "$host_path"
  response=$(headers "$host" "$path")
  status_is 404 "$response" "$host$path"
  exactly strict-transport-security "$HSTS" "$response" "$host$path 404"
done
for host_path in "$LOCAL_APP /" "$LOCAL_AUTH /.well-known/openid-configuration"; do
  read -r host path <<< "$host_path"
  ! grep -qi '^strict-transport-security:' <<< "$(response_headers "$LOCAL" "$host" "$path")" || fail "local $host$path: HSTS sent over plain HTTP"
done
ok "AWS: Strict-Transport-Security: $HSTS once on a page, the gateway's own 403 on all three hosts and its own 404 on the app and identity hosts; local: none"

section "criterion 3: the identity host keeps the console closed"
for path in /ui/console /ui/console/ /ui/console/assets/probe.js; do
  [ "$(status trusted "$AUTH" "$path" -H "X-Origin-Verify: $VERIFY")" = 404 ] || fail "$path is not 404"
  ! upstream_saw "GET $path " || fail "$path reached Zitadel"
done
for path_port in "/.well-known/openid-configuration 8080" "/oauth/v2/authorize 8080" "/ui/v2/login/loginname 3000"; do
  read -r path port <<< "$path_port"
  grep -qF "port=$port " <<< "$(echoed trusted "$AUTH" "$path")" || fail "$path does not reach the upstream on $port"
done
ok "/ui/console and /ui/console/ answer 404 without reaching Zitadel; discovery, /oauth/v2/* and /ui/v2/login still do"

section "7.12 criterion 3: the identity origin's browser-security baseline, each header once"
# identity_check LABEL STATUS HSTS-COUNT REFERRER-POLICY RESPONSE. The echo on port 3000 sends the login's own HSTS,
# Referrer-Policy and nosniff, which the gateway hides, so each appears once with the gateway's value; on port 8080 it
# sends Zitadel's v1 login's same-origin, which passes through alone, and no Referrer-Policy on discovery, where the
# gateway adds its own, as on the responses it makes itself.
identity_check(){
  local label=$1 status=$2 hsts=$3 referrer=$4 response=$5
  status_is "$status" "$response" "$label"
  exactly x-content-type-options nosniff "$response" "$label"
  exactly referrer-policy "$referrer" "$response" "$label"
  [ "$(grep -icxF "content-security-policy: frame-ancestors 'none'" <<< "$response")" = 1 ] || fail "$label: not exactly one frame-ancestors 'none' policy"
  [ "$(grep -ic '^strict-transport-security:' <<< "$response")" = "$hsts" ] || fail "$label: Strict-Transport-Security not sent $hsts time(s)"
  [ "$hsts" = 0 ] || exactly strict-transport-security "$HSTS" "$response" "$label"
}
identity_check "AWS $AUTH/ without X-Origin-Verify" 403 1 strict-origin-when-cross-origin "$(response_headers "$GW" "$AUTH" /)"
identity_check "AWS $AUTH/ui/console" 404 1 strict-origin-when-cross-origin "$(headers "$AUTH" /ui/console)"
for label_base in "AWS $GW $AUTH 1" "local $LOCAL $LOCAL_AUTH 0"; do
  read -r label base host hsts <<< "$label_base"
  identity_check "$label $host/" 302 "$hsts" strict-origin-when-cross-origin "$(response_headers "$base" "$host" / -H "X-Origin-Verify: $VERIFY")"
  identity_check "$label $host discovery" 200 "$hsts" strict-origin-when-cross-origin "$(response_headers "$base" "$host" /.well-known/openid-configuration -H "X-Origin-Verify: $VERIFY")"
  identity_check "$label $host/ui/login/loginname" 200 "$hsts" same-origin "$(response_headers "$base" "$host" /ui/login/loginname -H "X-Origin-Verify: $VERIFY")"
  response=$(response_headers "$base" "$host" /ui/v2/login/loginname -H "X-Origin-Verify: $VERIFY")
  identity_check "$label $host/ui/v2/login/loginname" 200 "$hsts" strict-origin-when-cross-origin "$response"
  exactly x-frame-options deny "$response" "$label $host/ui/v2/login/loginname"
done
ok "identity origin (AWS 403, console 404, both gateways' 302, discovery, v1 and v2 login): nosniff and frame-ancestors 'none' once each, the gateway's HSTS once in AWS and none locally; Referrer-Policy once: the gateway's strict-origin-when-cross-origin on the 403, 404, 302, discovery and Login v2, Zitadel's same-origin alone on its v1 login; the login's X-Frame-Options: deny passes through once"

section "criterion 4: Mailpit on its own origin, behind basic auth"
for path in / /api/v1/messages; do
  [ "$(status trusted "$MAIL" "$path" -H "X-Origin-Verify: $VERIFY")" = 401 ] || fail "Mailpit $path without credentials"
  [ "$(status trusted "$MAIL" "$path" -H "X-Origin-Verify: $VERIFY" -u "${MAILPIT_CREDENTIALS%%:*}:wrong-password")" = 401 ] || fail "Mailpit $path with a wrong password"
  [ "$(status trusted "$MAIL" "$path" -H "X-Origin-Verify: $VERIFY" -u "$MAILPIT_CREDENTIALS")" = 200 ] || fail "Mailpit $path with the right credentials"
done
ok "the Mailpit host's / and /api/v1/messages: 401 without and with wrong credentials, 200 with the right pair"
for path in /mailpit/ /mailpit/api/v1/messages; do
  [ "$(status trusted "$APP" "$path" -H "X-Origin-Verify: $VERIFY" -u "$MAILPIT_CREDENTIALS")" = 404 ] || fail "the app host's $path is not 404"
  ! upstream_saw "$path " || fail "the app host's $path reached an upstream"
done
ok "the app host answers 404 on /mailpit/ and /mailpit/api/v1/messages and reaches no upstream"

response=$(headers "$MAIL" / -u "$MAILPIT_CREDENTIALS")
for header in strict-transport-security x-content-type-options referrer-policy x-frame-options; do
  once "$header" "$response" "Mailpit /"
done
grep -qix 'x-content-type-options: nosniff' <<< "$response" || fail "Mailpit: no nosniff"
grep -qix 'x-frame-options: deny' <<< "$response" || fail "Mailpit: no X-Frame-Options: DENY"
grep -qix 'referrer-policy: no-referrer' <<< "$response" || fail "Mailpit: no Referrer-Policy: no-referrer"
# Two policies, both enforced: the gateway's frame-ancestors 'none' and Mailpit's own (its scripts by nonce).
policies=$(grep -i '^content-security-policy:' <<< "$response")
[ "$(grep -c . <<< "$policies")" = 2 ] || fail "Mailpit: not exactly two CSP headers: $policies"
grep -qix "content-security-policy: frame-ancestors 'none'" <<< "$policies" || fail "Mailpit: no frame-ancestors 'none' policy"
grep -qi "script-src 'nonce-" <<< "$policies" || fail "Mailpit: its own CSP did not pass through"
app_csp=$(grep -i '^content-security-policy:' <<< "$(headers "$APP" /)")
! grep -qixF "$app_csp" <<< "$policies" || fail "Mailpit gets the app CSP"
page=$(from trusted -H "Host: $MAIL" -H "X-Origin-Verify: $VERIFY" -u "$MAILPIT_CREDENTIALS" "$GW/")
scripts=$(grep -oE '<script[^>]* src="[^"]+"' <<< "$page" | sed -E 's/.* src="([^"]+)"/\1/')
[ -n "$scripts" ] || fail "Mailpit's page names no script"
for script in $scripts; do
  [[ $script == /* ]] || script=/$script
  [ "$(status trusted "$MAIL" "$script" -H "X-Origin-Verify: $VERIFY" -u "$MAILPIT_CREDENTIALS")" = 200 ] || fail "Mailpit's $script does not load"
done
ok "the Mailpit host sends HSTS, nosniff, Referrer-Policy: no-referrer and X-Frame-Options: DENY once each, frame-ancestors 'none' beside Mailpit's own CSP, not the app CSP, and its page's scripts load ($(tr '\n' ' ' <<< "$scripts"))"

limited=$(docker exec "$run-trusted" sh -c "for i in \$(seq 40); do curl -s -o /dev/null -w '%{http_code}\n' --max-time 5 \
  -H 'Host: $MAIL' -H 'X-Origin-Verify: $VERIFY' -H 'CloudFront-Viewer-Address: 198.51.100.77:1' $GW/api/v1/messages; done" | grep -c '^429$' || true)
(( limited > 0 )) || fail "40 rapid requests to the Mailpit host got no 429"
ok "the Mailpit host is under the coarse limit: $limited of 40 rapid requests got 429"

section "criterion 6: no browser telemetry intake in AWS"
for signal in traces metrics logs; do
  [ "$(status trusted "$APP" "/otlp/v1/$signal" -H "X-Origin-Verify: $VERIFY" -X POST -H 'Content-Type: application/json' --data '{}')" = 404 ] \
    || fail "POST /otlp/v1/$signal is not 404"
  ! upstream_saw "/otlp/v1/$signal" || fail "/otlp/v1/$signal reached an upstream"
done
ok "POST /otlp/v1/traces, /metrics and /logs answer 404 and reach no upstream"
! docker exec "$run-gateway" grep -rqF /otlp/v1/traces /usr/share/nginx/html || fail "the image built with BROWSER_TELEMETRY=off still exports traces"
docker exec "$run-local" grep -rqF /otlp/v1/traces /usr/share/nginx/html || fail "the default build does not export traces"
ok "the SPA built with BROWSER_TELEMETRY=off holds no trace export; the default build does"

section "criterion 7: cache headers, with the security headers repeated, on both gateways"
check_cache(){ # label base-url host expected-hsts-count path expected-cache-control
  local label=$1 base=$2 host=$3 hsts=$4 path=$5 expected=$6 response
  response=$(from trusted -o /dev/null -D - -H "Host: $host" -H "X-Origin-Verify: $VERIFY" "$base$path" | tr -d '\r')
  head -1 <<< "$response" | grep -q ' 200' || fail "$label $path: $(head -1 <<< "$response")"
  once cache-control "$response" "$label $path"
  grep -qix "cache-control: $expected" <<< "$response" || fail "$label $path: Cache-Control is not $expected"
  app_headers_once "$label $path" "$hsts" "$response"
}
for label_base in "AWS $GW $APP 1" "local $LOCAL localhost:8080 0"; do
  read -r label base host hsts <<< "$label_base"
  # Each build has its own hashed file names, so each gateway's index.html names its own script.
  asset=$(grep -oE '/assets/[^"]+\.js' <<< "$(from trusted -H "Host: $host" -H "X-Origin-Verify: $VERIFY" "$base/")" | sed -n 1p)
  [ -n "$asset" ] || fail "$label: index.html names no /assets/ script"
  check_cache "$label" "$base" "$host" "$hsts" / "no-cache"
  check_cache "$label" "$base" "$host" "$hsts" /disputes "no-cache"
  check_cache "$label" "$base" "$host" "$hsts" "$asset" "public, max-age=31536000, immutable"
  missing=$(from trusted -o /dev/null -D - -H "Host: $host" -H "X-Origin-Verify: $VERIFY" "$base/assets/missing-probe.js" | tr -d '\r')
  head -1 <<< "$missing" | grep -q ' 404' || fail "$label: a missing asset is not 404"
  ! grep -qi '^cache-control:' <<< "$missing" || fail "$label: a missing asset's 404 is cacheable"
  ok "$label: / and a client route no-cache, $asset immutable for a year, each with the security headers once; a missing asset's 404 carries no Cache-Control"
done

section "7.12 criteria 2 and 4: base-uri 'none' in both CSPs, and the SPA's charset, on both gateways"
scalar_hash=$(grep -o "'sha256-[^']*'" infra/nginx/templates/scalar-headers.conf.template)
scalar_csp="default-src 'self'; script-src 'self' $scalar_hash; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'"
for label_base in "AWS $GW $APP $work/gateway.env" "local $LOCAL $LOCAL_APP $work/local-gateway.env"; do
  read -r label base host env_file <<< "$label_base"
  auth_origin=$(grep '^GATEWAY_AUTH_ORIGIN=' "$env_file" | cut -d= -f2-)
  app_csp="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self' $auth_origin"
  for path in / /disputes; do
    exactly content-security-policy "$app_csp" "$(response_headers "$base" "$host" "$path" -H "X-Origin-Verify: $VERIFY")" "$label $path"
  done
  exactly content-security-policy "$scalar_csp" "$(response_headers "$base" "$host" /scalar/ -H "X-Origin-Verify: $VERIFY")" "$label /scalar/"
  ok "$label: / and a client route send the app CSP, /scalar/ the Scalar CSP, each with base-uri 'none' and every other directive unchanged"

  index=$(from trusted -H "Host: $host" -H "X-Origin-Verify: $VERIFY" "$base/")
  css=$(grep -oE '/assets/[^"]+\.css' <<< "$index" | sed -n 1p)
  js=$(grep -oE '/assets/[^"]+\.js' <<< "$index" | sed -n 1p)
  [ -n "$css" ] && [ -n "$js" ] || fail "$label: index.html names no /assets/ stylesheet or script"
  for path_type in "/ text/html" "$css text/css" "$js application/javascript" "/assets/missing-probe.css text/html"; do
    read -r path type <<< "$path_type"
    exactly content-type "$type; charset=utf-8" "$(response_headers "$base" "$host" "$path" -H "X-Origin-Verify: $VERIFY")" "$label $path"
  done
  ok "$label: index.html, $css, $js and a missing asset's 404 name charset=utf-8 once"
done
# Proxied responses keep their upstream's Content-Type: the charset is set only in the SPA's own locations.
content_type(){ grep -i '^content-type:' <<< "$1" || echo "(no content-type)"; }
direct=$(content_type "$(from trusted -o /dev/null -D - -u "$MAILPIT_CREDENTIALS" "http://$run-mailpit:8025/" | tr -d '\r')")
[ "$(content_type "$(headers "$MAIL" / -u "$MAILPIT_CREDENTIALS")")" = "$direct" ] || fail "the Mailpit page's Content-Type is not Mailpit's own: $direct"
direct=$(content_type "$(from trusted -o /dev/null -D - "http://$run-echo:8080/.well-known/openid-configuration" | tr -d '\r')")
[ "$(content_type "$(headers "$AUTH" /.well-known/openid-configuration)")" = "$direct" ] || fail "discovery's Content-Type is not the upstream's: $direct"
ok "AWS: the Mailpit page and discovery keep their upstream's Content-Type"

section "7.12 criterion 6: the access logs record the path, never the query string or the Referer"
marker=LogMarker712x$$
referer=(-H "Referer: https://example.test/?ref=$marker")
# A viewer address no earlier check has limited, so no request here is refused (AWS); locally the client's own address.
log_viewer=(-H "CloudFront-Viewer-Address: 198.51.100.66:1")
# The access log is the container's standard output; standard error (the error log) stays P43. Earlier checks logged
# some of the same paths, so the paths must be in the lines after these counts.
access_log_of(){ docker logs "$1" 2>/dev/null; }
gateway_log_start=$(( $(access_log_of "$run-gateway" | wc -l) ))
local_log_start=$(( $(access_log_of "$run-local" | wc -l) ))
for host_path in "$APP /api/signin-oidc?code=$marker" "$APP /?state=$marker" "$AUTH /oidc/v1/end_session?id_token_hint=$marker" \
                 "$MAIL /api/v1/messages?query=$marker"; do
  read -r host path <<< "$host_path"
  response_headers "$GW" "$host" "$path" -H "X-Origin-Verify: $VERIFY" "${log_viewer[@]}" "${referer[@]}" >/dev/null
done
for host_path in "$LOCAL_APP /api/signin-oidc?code=$marker" "$LOCAL_APP /?state=$marker" "$LOCAL_AUTH /oidc/v1/end_session?id_token_hint=$marker"; do
  read -r host path <<< "$host_path"
  response_headers "$LOCAL" "$host" "$path" "${referer[@]}" >/dev/null
done
# new_log_lines CONTAINER START: the access log's lines after its first START
new_log_lines(){ access_log_of "$1" | tail -n "+$(( $2 + 1 ))"; }
for container_paths in "$run-gateway $gateway_log_start /api/signin-oidc /index.html /oidc/v1/end_session /api/v1/messages" \
                       "$run-local $local_log_start /api/signin-oidc /index.html /oidc/v1/end_session"; do
  read -r container start paths <<< "$container_paths"
  for path in $paths; do
    for _ in $(seq 5); do grep -qF "\"GET $path HTTP/1.1\"" <<< "$(new_log_lines "$container" "$start")" && break; sleep 1; done
    grep -qF "\"GET $path HTTP/1.1\"" <<< "$(new_log_lines "$container" "$start")" || fail "$container's access log has no new GET $path"
  done
  ! grep -qF "$marker" <<< "$(access_log_of "$container")" || fail "$container's access log holds a query string or a Referer: $(grep -F "$marker" <<< "$(access_log_of "$container")")"
  ok "$container's access log: $paths, with no query string and no Referer"
done

section "7.12 criterion 7: Cache-Control: no-store on the /api responses each gateway generates itself"
# generated_check LABEL STATUS HSTS-COUNT RESPONSE: the gateway's own response, with its status, no-store and the app's
# security headers once each
generated_check(){
  local label=$1 status=$2 hsts=$3 response=$4
  status_is "$status" "$response" "$label"
  exactly cache-control no-store "$response" "$label"
  app_headers_once "$label" "$hsts" "$response"
}
# gateway_error_check LABEL STATUS HSTS-COUNT RESPONSE-WITH-BODY: as generated_check, and the body is @gateway_error's
# fixed text, so nothing from the request or the upstream is echoed back
gateway_error_check(){
  generated_check "$@"
  [ "$(tail -1 <<< "$4")" = "The gateway could not complete the request." ] || fail "$1: not the fixed body: $(tail -1 <<< "$4")"
}
# first_429 CLIENT [curl options...] URL: the status line and headers of the first 429 in up to 60 rapid requests
first_429(){
  local client=$1; shift
  docker exec "$run-$client" sh -c 'for _ in $(seq 60); do
      response=$(curl -sS -o /dev/null -D - --max-time 5 "$@" | tr -d "\r")
      case "$response" in "HTTP/1.1 429 "*) printf "%s\n" "$response"; exit 0 ;; esac
    done
    exit 1' sh "$@" || fail "60 rapid requests from $client got no 429: $*"
}
# Each gateway is limited per client: in AWS per viewer address (one per check), locally per container address.
for label_base in "AWS $GW $APP 1 trusted" "local $LOCAL $LOCAL_APP 0 untrusted"; do
  read -r label base host hsts limited_client <<< "$label_base"
  response=$(first_429 "$limited_client" -H "Host: $host" -H "X-Origin-Verify: $VERIFY" -H "CloudFront-Viewer-Address: 198.51.100.70:1" "$base/api/limit-probe")
  generated_check "$label /api/ 429" 429 "$hsts" "$response"
  # RFC 8259 11: application/json-based types have no charset parameter. Retry-After in seconds (RFC 9110 10.2.3).
  exactly content-type application/problem+json "$response" "$label /api/ 429"
  exactly retry-after 1 "$response" "$label /api/ 429"
  # 2 MB, above nginx's default client_max_body_size of 1 MB. With its body, for @gateway_error's fixed text.
  response=$(docker exec "$run-trusted" sh -c 'head -c 2097152 /dev/zero | curl -sS -D - --max-time 5 -X POST --data-binary @- "$@"' \
    sh -H "Host: $host" -H "X-Origin-Verify: $VERIFY" -H "CloudFront-Viewer-Address: 198.51.100.71:1" "$base/api/v1/disputes" | tr -d '\r')
  gateway_error_check "$label /api/v1/disputes 413" 413 "$hsts" "$response"
  # The upstream's answer passes through with its own Cache-Control only (the echo sends no-store, as the Api does).
  response=$(response_headers "$base" "$host" /api/v1/disputes -H "X-Origin-Verify: $VERIFY" -H "CloudFront-Viewer-Address: 198.51.100.72:1")
  status_is 200 "$response" "$label /api/v1/disputes from the upstream"
  exactly cache-control no-store "$response" "$label /api/v1/disputes from the upstream"
  ok "$label: the gateway's 429 (application/problem+json, Retry-After: 1) and 413 (the fixed body) carry Cache-Control: no-store and the security headers once; a proxied answer keeps one Cache-Control, the upstream's"
done
# The echo closes the connection on these paths, one through each /api location, so the gateway answers 502 itself.
for label_base in "AWS $GW $APP 1" "local $LOCAL $LOCAL_APP 0"; do
  read -r label base host hsts <<< "$label_base"
  for path in /api/upstream-closes /api/health/upstream-closes; do
    # With the body, for @gateway_error's fixed text.
    response=$(from trusted -D - -H "Host: $host" -H "X-Origin-Verify: $VERIFY" -H "CloudFront-Viewer-Address: 198.51.100.73:1" "$base$path" | tr -d '\r')
    gateway_error_check "$label $path 502" 502 "$hsts" "$response"
  done
  ok "$label: when the upstream closes the connection without a response, /api/ and /api/health/ answer 502 with the fixed body, Cache-Control: no-store and the security headers once"
done

echo; echo "GATEWAY TEST OK"
