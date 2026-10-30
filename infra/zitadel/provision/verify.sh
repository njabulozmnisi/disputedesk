#!/usr/bin/env bash
# Read-only checks of Zitadel facts that are invisible from outside (infra/smoke.sh covers everything that
# can be seen through the gateway). Runs in the provisioner image:
#   docker compose run --rm --no-deps -T --entrypoint bash provision /verify.sh
# Prints one line per check and exits non-zero if any check fails. Never prints or passes secrets as arguments.
set -euo pipefail

Z="${ZITADEL_URL:?}"; FIXTURES="${FIXTURES_FILE:?}"
CURL_OPTS=(--max-time 30 ${CURL_CONNECT_TO:+--connect-to "$CURL_CONNECT_TO"})   # every call times out
CLIENTS=$(cat "${OUT_DIR:?}/zitadel-clients.json")
PROJECT_ID=$(jq -r .Zitadel.ProjectId <<< "$CLIENTS")
BFF_CLIENT_ID=$(jq -r .Zitadel.Bff.ClientId <<< "$CLIENTS")
FAILED=0

umask 077
AUTH_HEADER=$(mktemp)
trap 'rm -f "$AUTH_HEADER"' EXIT
{ printf 'Authorization: Bearer '; tr -d '\n' < "${PAT_FILE:?}"; printf '\n'; } > "$AUTH_HEADER"

api(){ # METHOD PATH [JSON]; the body goes over stdin
  printf '%s' "${3:-}" | curl "${CURL_OPTS[@]}" -sS --fail-with-body -X "$1" "$Z$2" -H @"$AUTH_HEADER" \
    -H 'Content-Type: application/json' -H 'Connect-Protocol-Version: 1' ${3:+--data-binary @-}; }
rpc(){ api POST "/$1" "${2:-{\}}"; }
check(){ # name jq-filter json: PASS with the filter's result, or FAIL with the input
  local name=$1 filter=$2 json=$3 out
  if out=$(jq -ec "$filter" <<< "$json" 2>/dev/null); then printf 'PASS  %-36s %s\n' "$name" "$out"
  else printf 'FAIL  %-36s %s\n' "$name" "$(jq -c . <<< "$json" 2>/dev/null | cut -c1-600)"; FAILED=1; fi; }

project=$(rpc zitadel.project.v2.ProjectService/GetProject "{\"projectId\":\"$PROJECT_ID\"}")
check "project: role assertion" '.project | select(.projectRoleAssertion == true and (.authorizationRequired // false) == false)
  | {name, projectRoleAssertion, authorizationRequired: (.authorizationRequired // false)}' "$project"

apps=$(rpc zitadel.application.v2.ApplicationService/ListApplications "{\"filters\":[{\"projectIdFilter\":{\"projectId\":\"$PROJECT_ID\"}}]}")
bff=$(jq -c --arg id "$BFF_CLIENT_ID" '.applications[].oidcConfiguration | select(.clientId == $id)' <<< "$apps")
# proto3 JSON omits zero-value enums: OIDC_APP_TYPE_WEB and OIDC_AUTH_METHOD_TYPE_BASIC are both 0.
check "BFF app: confidential web, code, JWT" '{applicationType: (.applicationType // "OIDC_APP_TYPE_WEB"),
    authMethodType: (.authMethodType // "OIDC_AUTH_METHOD_TYPE_BASIC"), responseTypes, grantTypes, developmentMode,
    accessTokenType, accessTokenRoleAssertion, idTokenRoleAssertion}
  | select(.applicationType == "OIDC_APP_TYPE_WEB" and .authMethodType == "OIDC_AUTH_METHOD_TYPE_POST"
      and .responseTypes == ["OIDC_RESPONSE_TYPE_CODE"] and .grantTypes == ["OIDC_GRANT_TYPE_AUTHORIZATION_CODE"]
      and .accessTokenType == "OIDC_TOKEN_TYPE_JWT" and .accessTokenRoleAssertion and .idTokenRoleAssertion)' "$bff"
check "BFF app: post-logout URIs (exact)" '.postLogoutRedirectUris | select(sort == ([
    "http://localhost:8080/", "http://localhost:8080/api/signout-callback-oidc",
    "http://localhost:5101/", "http://localhost:5101/api/signout-callback-oidc",
    "http://localhost:5102/", "http://localhost:5102/api/signout-callback-oidc"] | sort))' "$bff"
check "BFF app: development mode as configured" \
  "(.developmentMode // false) | select(tostring == \"${BFF_DEVELOPMENT_MODE:?}\")" "$bff"

while read -r login; do
  id=$(jq -r .userId <<< "$login"); username=$(jq -r .username <<< "$login")
  user=$(rpc zitadel.user.v2.UserService/GetUserByID "{\"userId\":\"$id\"}")
  check "login $username: verified email" '.user | select(.state == "USER_STATE_ACTIVE" and .human.email.isVerified == true)
    | {userId, email: .human.email.email, isVerified: .human.email.isVerified}' "$user"
  session=$(rpc zitadel.session.v2.SessionService/CreateSession \
    "$(jq -c '{checks: {user: {loginName: .username}, password: {password: .password}}}' <<< "$login")" || true)
  check "login $username: password works" 'select(.sessionId != null) | {session: "created"}' "$session"
  if [ -n "$(jq -r '.sessionId // empty' <<< "$session" 2>/dev/null)" ]; then
    rpc zitadel.session.v2.SessionService/DeleteSession "$(jq -c '{sessionId}' <<< "$session")" >/dev/null
  fi
done < <(jq -c '.logins[]' "$FIXTURES")

policy=$(api GET /admin/v1/policies/login)
check "login policy: self-registration" '.policy | select(.allowRegister == true and (.forceMfa // false) == false)
  | {allowRegister, forceMfa: (.forceMfa // false)}' "$policy"
factors=$(api POST /admin/v1/policies/login/second_factors/_search '{}')
check "login policy: TOTP allowed" '[.result[]] | select(index("SECOND_FACTOR_TYPE_OTP"))' "$factors"

smtp=$(api POST /admin/v1/email/_search '{}')
check "smtp: Mailpit active" '[.result[] | select(.state == "EMAIL_PROVIDER_ACTIVE") | {host: .smtp.host, state}]
  | select(length == 1 and .[0].host == "mailpit:1025")' "$smtp"

# The active policy (not the preview) has the generator's nine fields for the mounted theme file, and the files.
active=$(api GET /admin/v1/policies/label)
label=$(jq -c --argjson wanted "$(bash /label-policy.sh /brand/tokens.css)" '{wanted: $wanted, active: .policy}' <<< "$active")
check "label policy: active, from tokens.css" 'def fields: {primaryColor, backgroundColor, warnColor, fontColor,
    primaryColorDark, backgroundColorDark, warnColorDark, fontColorDark, themeMode} | map_values(. // "" | ascii_downcase);
  select((.active | fields) == (.wanted | fields)
    and ([.active.fontUrl, .active.logoUrl, .active.logoUrlDark] | all(. // "" | length > 0)))
  | .active | {primaryColor, primaryColorDark, themeMode, font: (.fontUrl | length > 0), logos: ((.logoUrl + .logoUrlDark) | length > 0)}' "$label"

exit "$FAILED"
