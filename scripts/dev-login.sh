#!/usr/bin/env bash
# Headless sign-in for builder smoke checks: signs a fixture login in to the local stack through the BFF's real
# code flow with plain HTTP requests (curl, no browser) and leaves the session cookie in a curl cookie jar.
#
# Usage: scripts/dev-login.sh <login> [cookie-jar]
#   <login>       a login key from fixtures/local.json: customer-a, customer-b or customer-c
#   [cookie-jar]  where to write the cookies (default: ${TMPDIR:-/tmp}/disputedesk-<login>.cookies)
# Then call the Api with the jar, for example:
#   curl -b <cookie-jar> http://localhost:8080/api/v1/bff/session
#
# How it works:
#   1. GET /api/v1/bff/login starts the flow; Zitadel's authorize endpoint hands it to Login v2 with an auth
#      request ID.
#   2. The script does what Login v2 does on its server: it checks the fixture password with the Session API and
#      finishes the auth request with the OIDC API. It authenticates as the login client, with the PAT that only
#      the zitadel-login container holds. It is read there for this run only, kept in a private temporary file
#      (mode 600, removed on exit) so it never appears on a command line, and never saved.
#   3. The callback goes back to the Api, which stores the session and sets the session cookie.
# Needs the stack running (docker compose up --wait), curl and jq. It holds no secret: the password comes from
# the fixture file, which holds dev-only values.
set -euo pipefail

APP="${APP_ORIGIN:-http://localhost:8080}"
AUTH="${AUTH_ORIGIN:-http://auth.localhost:8080}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

usage(){ sed -n '2,/^set -euo/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; }
die(){ printf 'dev-login: %s\n' "$*" >&2; exit 1; }

case "${1:-}" in
  -h|--help) usage; exit 0 ;;
  "") usage >&2; exit 64 ;;
esac

key=$1
jar="${2:-${TMPDIR:-/tmp}/disputedesk-$key.cookies}"
login=$(jq -ce --arg key "$key" '.logins[] | select(.key == $key)' "$ROOT/fixtures/local.json") \
  || die "no login '$key' in fixtures/local.json"

umask 077
rm -f "$jar"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# 1. The Api starts the code flow (its correlation and nonce cookies go into the jar); Zitadel hands over to Login v2.
authorize=$(curl -sS -c "$jar" -b "$jar" -o /dev/null -w '%{redirect_url}' "$APP/api/v1/bff/login")
[[ "$authorize" == "$AUTH/oauth/v2/authorize?"* ]] || die "the Api did not redirect to Zitadel (got '$authorize')"
hosted_login=$(curl -sS -o /dev/null -w '%{redirect_url}' "$authorize")
auth_request=$(sed -n 's/.*[?&]authRequest=\([^&]*\).*/\1/p' <<< "$hosted_login")
[ -n "$auth_request" ] || die "Zitadel did not hand over to Login v2 (got '$hosted_login')"

# 2. What Login v2 does on its server, as the login client.
{ printf 'Authorization: Bearer '
  docker compose -f "$ROOT/docker-compose.yml" exec -T zitadel-login cat /zitadel/login/login-client.pat | tr -d '\n'
  printf '\n'; } > "$work/auth-header"
rpc(){ curl -sS --fail-with-body -X POST "$AUTH/$1" -H @"$work/auth-header" \
  -H 'Content-Type: application/json' -H 'Connect-Protocol-Version: 1' --data-binary @-; }

session=$(jq -c '{checks: {user: {loginName: .username}, password: {password: .password}}}' <<< "$login" \
  | rpc zitadel.session.v2.SessionService/CreateSession) || die "password check failed for '$key'"
callback=$(jq -c --arg id "$auth_request" '{authRequestId: $id, session: {sessionId, sessionToken}}' <<< "$session" \
  | rpc zitadel.oidc.v2.OIDCService/CreateCallback | jq -r .callbackUrl) || die "Zitadel refused the auth request"

# 3. Back to the Api. Zitadel answers either with the callback URL itself or with an auto-submitting form_post page.
if [[ "$callback" != "$APP/"* ]]; then
  curl -sS -o "$work/page" "$callback"
  action=$(sed -n 's/.*<form[^>]*action="\([^"]*\)".*/\1/p' "$work/page" | head -1)
  [ -n "$action" ] || die "unexpected answer from Zitadel's callback (no form_post page)"
  fields=()
  for name in code state; do
    value=$(sed -n "s/.*name=\"$name\"[^>]*value=\"\([^\"]*\)\".*/\1/p" "$work/page" | head -1)
    fields+=(--data-urlencode "$name=$value")
  done
  status=$(curl -sS -c "$jar" -b "$jar" -o /dev/null -w '%{http_code}' "${fields[@]}" "$action")
else
  status=$(curl -sS -c "$jar" -b "$jar" -o /dev/null -w '%{http_code}' "$callback")
fi
[ "$status" = 302 ] || die "the Api's callback answered $status, not 302"

state=$(curl -sS -b "$jar" "$APP/api/v1/bff/session" | jq -r .state)
[ "$state" != anonymous ] || die "signed in at Zitadel, but the Api has no session"
printf 'signed in as %s (session state: %s)\ncookie jar: %s\n' "$(jq -r .username <<< "$login")" "$state" "$jar"
