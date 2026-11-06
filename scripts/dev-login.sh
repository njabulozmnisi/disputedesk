#!/usr/bin/env bash
# Headless sign-in for builder smoke checks: signs a fixture login in to the local stack through the Api's real
# code flow with plain HTTP requests (curl, no browser) and leaves the session cookie in a curl cookie jar.
#
# Usage: scripts/dev-login.sh <login> [cookie-jar]
#   <login>       a login key from backend/fixtures/local.json: customer-a, customer-b or customer-c
#   [cookie-jar]  where to write the cookies (default: ${TMPDIR:-/tmp}/disputedesk-<login>.cookies)
# Then call the Api with the jar, for example:
#   curl -b <cookie-jar> http://localhost:8080/api/v1/authentication/session
#
# How it works:
#   1. GET /api/v1/authentication/login starts the flow; Zitadel's authorize endpoint hands it to Zitadel's hosted
#      login (/ui/login) with an auth request ID.
#   2. The script fills in the hosted login's forms as a browser would: the login name, the password, and Skip on
#      the optional two-factor setup prompt. Each form carries Zitadel's CSRF token (gorilla.csrf.Token) and its
#      cookie, which stay in a private temporary jar (removed on exit).
#   3. Zitadel's callback goes back to the Api, which stores the session and sets the session cookie.
# Needs the stack running (docker compose up --wait), curl and jq. It holds no secret: the password comes from
# the fixture file, which holds dev-only values, and reaches curl on stdin, never as an argument.
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
login=$(jq -ce --arg key "$key" '.logins[] | select(.key == $key)' "$ROOT/backend/fixtures/local.json") \
  || die "no login '$key' in backend/fixtures/local.json"

umask 077
rm -f "$jar"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
zitadel_jar="$work/zitadel.cookies"
page="$work/page"

# submit PATH FIELD...: posts the hosted login form on the current page to PATH, with its CSRF token and the auth
# request ID, as form fields read from stdin (curl -d @-, one NAME=value per line, URL-encoded); the answer replaces
# the page. Prints the status and the redirect URL.
submit(){
  local path=$1 token
  shift
  token=$(sed -n 's/.*name="gorilla.csrf.Token" value="\([^"]*\)".*/\1/p' "$page" | head -1)
  [ -n "$token" ] || die "the hosted login page has no CSRF token (before $path)"
  { printf 'gorilla.csrf.Token=%s&authRequestID=%s' "$(jq -rn --arg v "$token" '$v | @uri')" "$auth_request"
    for field in "$@"; do printf '&%s' "$field"; done; } \
    | curl -sS -c "$zitadel_jar" -b "$zitadel_jar" -o "$page" -w '%{http_code} %{redirect_url}' --data-binary @- "$AUTH$path"
}
# form_action: the action of the current page's form.
form_action(){ sed -n 's/.*<form[^>]*action="\([^"]*\)".*/\1/p' "$page" | head -1; }

# 1. The Api starts the code flow (its correlation and nonce cookies go into the jar); Zitadel hands over to its login.
authorize=$(curl -sS -c "$jar" -b "$jar" -o /dev/null -w '%{redirect_url}' "$APP/api/v1/authentication/login")
[[ "$authorize" == "$AUTH/oauth/v2/authorize?"* ]] || die "the Api did not redirect to Zitadel (got '$authorize')"
hosted_login=$(curl -sS -c "$zitadel_jar" -b "$zitadel_jar" -o /dev/null -w '%{redirect_url}' "$authorize")
auth_request=$(sed -n 's/.*[?&]authRequestID=\([0-9]*\).*/\1/p' <<< "$hosted_login")
[ -n "$auth_request" ] || die "Zitadel did not hand over to its hosted login (got '$hosted_login')"

# 2. The hosted login's forms, in order.
curl -sS -c "$zitadel_jar" -b "$zitadel_jar" -o "$page" "$hosted_login"
username=$(jq -r '.username | @uri' <<< "$login")
submit /ui/login/loginname "loginName=$username" > /dev/null
[ "$(form_action)" = /ui/login/password ] || die "Zitadel did not ask for the password of '$key'"
password=$(jq -r '.password | @uri' <<< "$login")
answer=$(submit /ui/login/password "password=$password")
if [ "$(form_action)" = /ui/login/mfa/prompt ]; then
  answer=$(submit /ui/login/mfa/prompt "skip=true")      # two-factor setup is optional (the login policy's default)
fi
callback=${answer#* }
[[ "$callback" == "$AUTH/oauth/v2/authorize/callback?"* ]] || die "Zitadel did not complete the sign-in of '$key' (got '$answer')"

# 3. Back to the Api. Zitadel answers either with a redirect to the Api's callback or with an auto-submitting
# form_post page.
answer=$(curl -sS -c "$zitadel_jar" -b "$zitadel_jar" -o "$page" -w '%{http_code} %{redirect_url}' "$callback")
if [ "${answer%% *}" = 200 ]; then
  action=$(form_action)
  [[ "$action" == "$APP/"* ]] || die "unexpected answer from Zitadel's callback (no form_post page)"
  fields=()
  for name in code state; do
    value=$(sed -n "s/.*name=\"$name\"[^>]*value=\"\([^\"]*\)\".*/\1/p" "$page" | head -1)
    fields+=(--data-urlencode "$name=$value")
  done
  status=$(curl -sS -c "$jar" -b "$jar" -o /dev/null -w '%{http_code}' "${fields[@]}" "$action")
else
  status=$(curl -sS -c "$jar" -b "$jar" -o /dev/null -w '%{http_code}' "${answer#* }")
fi
[ "$status" = 302 ] || die "the Api's callback answered $status, not 302"

state=$(curl -sS -b "$jar" "$APP/api/v1/authentication/session" | jq -r .state)
[ "$state" != anonymous ] || die "signed in at Zitadel, but the Api has no session"
printf 'signed in as %s (session state: %s)\ncookie jar: %s\n' "$(jq -r .username <<< "$login")" "$state" "$jar"
