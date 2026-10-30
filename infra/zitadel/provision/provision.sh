#!/usr/bin/env bash
# DisputeDesk Zitadel provisioner: one-shot container, runs on every `docker compose up`.
# It creates only what Zitadel's FirstInstance / DefaultInstance environment variables cannot: the project
# and its roles, the BFF web app, the operations machine user and the fixture logins. Instance settings
# (SMTP, login policy, Login v2) live in docker-compose.yml.
# Auth:  the IAM_OWNER PAT that FirstInstance writes into the bootstrap volume before Zitadel reports ready.
# Idempotent: every object is looked up before it is created. Client secrets are shown only once,
#        so they are (re)generated only when the output file is missing or belongs to another instance.
# Secrets never appear on a command line: the PAT goes to curl as a header file, bodies go over stdin.
# Runs as root on purpose: it hands the client file to the .NET images' non-root user (chown).
set -euo pipefail

Z="${ZITADEL_URL:?}"
OUT_FILE="${OUT_DIR:?}/zitadel-clients.json"
OUT_UID="${OUT_UID:?}"                 # the .NET images' non-root app user, which reads the file later
FIXTURES="${FIXTURES_FILE:?}"
APP_ORIGINS="${APP_ORIGINS:?}"         # space-separated origins allowed to complete the BFF sign-in
BFF_DEVELOPMENT_MODE="${BFF_DEVELOPMENT_MODE:?}"   # true only where the app origins are plain http (local)
PROJECT_NAME=DisputeDesk
BFF_APP_NAME=disputedesk-bff
OPS_USERNAME=operations-tools

# curl pins *.localhost to 127.0.0.1 (RFC 6761) and ignores the Docker alias, so connect straight to
# Zitadel while the Host header stays auth.localhost:8080 (Zitadel resolves the instance from it).
# --max-time bounds every call, so a hung Zitadel fails the one-shot instead of blocking `docker compose up`.
CURL_OPTS=(--max-time 30 ${CURL_CONNECT_TO:+--connect-to "$CURL_CONNECT_TO"})
log(){ printf '[provision] %s\n' "$*" >&2; }

# It is written straight into the app's JSON, so anything but a JSON boolean is refused up front.
case "$BFF_DEVELOPMENT_MODE" in
  true|false) ;;
  *) log "BFF_DEVELOPMENT_MODE must be true or false, not '$BFF_DEVELOPMENT_MODE'"; exit 1 ;;
esac

umask 077
AUTH_HEADER=$(mktemp)
trap 'rm -f "$AUTH_HEADER"' EXIT
{ printf 'Authorization: Bearer '; tr -d '\n' < "${PAT_FILE:?}"; printf '\n'; } > "$AUTH_HEADER"

# call METHOD PATH [JSON]: prints the body; fails on HTTP >= 400, except 409 when ALLOW_CONFLICT=1.
call(){
  local m=$1 p=$2 d=${3:-} tmp code
  tmp=$(mktemp)
  code=$(printf '%s' "$d" | curl "${CURL_OPTS[@]}" -sS -o "$tmp" -w '%{http_code}' -X "$m" "$Z$p" \
    -H @"$AUTH_HEADER" -H 'Content-Type: application/json' -H 'Connect-Protocol-Version: 1' \
    ${d:+--data-binary @-})
  if [ "$code" -ge 400 ]; then
    if [ "${ALLOW_CONFLICT:-0}" = 1 ] && [ "$code" = 409 ]; then rm -f "$tmp"; return 0; fi
    log "FAIL $m $p -> $code $(cat "$tmp")"; rm -f "$tmp"; return 1
  fi
  cat "$tmp"; rm -f "$tmp"
}
rpc(){ call POST "/$1" "${2:-{\}}"; }   # Connect protocol, JSON body

ORG_ID=$(call GET /auth/v1/users/me | jq -r '.user.details.resourceOwner')
log "organization $ORG_ID"

# ------------------------------------------------------------------------------------ project and roles
PROJECT_ID=$(rpc zitadel.project.v2.ProjectService/ListProjects \
  "{\"filters\":[{\"projectNameFilter\":{\"projectName\":\"$PROJECT_NAME\",\"method\":\"TEXT_FILTER_METHOD_EQUALS\"}}]}" \
  | jq -r '.projects[0].projectId // empty')
if [ -z "$PROJECT_ID" ]; then
  # Role assertion on: granted roles are put in tokens. Authorisation not required: customers hold no role.
  PROJECT_ID=$(rpc zitadel.project.v2.ProjectService/CreateProject \
    "{\"organizationId\":\"$ORG_ID\",\"name\":\"$PROJECT_NAME\",\"projectRoleAssertion\":true,\"authorizationRequired\":false,\"projectAccessRequired\":false}" \
    | jq -r .projectId)
  log "project created $PROJECT_ID"
else log "project exists $PROJECT_ID"; fi

# One role per action, plus reading one dispute (FR-30). Withdraw is the customer's own action and needs no role.
# The part after the dot is camelCase, like the action names on the wire.
ROLES='dispute.startReview|Start review
dispute.escalate|Escalate
dispute.resolve|Resolve
dispute.read|Read a dispute'
while IFS='|' read -r key name; do
  ALLOW_CONFLICT=1 rpc zitadel.project.v2.ProjectService/AddProjectRole \
    "{\"projectId\":\"$PROJECT_ID\",\"roleKey\":\"$key\",\"displayName\":\"$name\",\"group\":\"action\"}" >/dev/null
done <<< "$ROLES"
ROLE_KEYS=$(cut -d'|' -f1 <<< "$ROLES" | jq -R . | jq -sc .)
log "roles ensured: $ROLE_KEYS"

# ------------------------------------------------------------------------------- the Api's BFF web app
redirects=() logouts=()
for origin in $APP_ORIGINS; do
  # Sign-out returns to the app's root (AD-7); the handler's own signed-out callback stays registered as well.
  redirects+=("$origin/api/signin-oidc"); logouts+=("$origin/" "$origin/api/signout-callback-oidc")
done
REDIRECTS=$(printf '%s\n' "${redirects[@]}" | jq -R . | jq -sc .)
LOGOUTS=$(printf '%s\n' "${logouts[@]}" | jq -R . | jq -sc .)

find_bff(){ rpc zitadel.application.v2.ApplicationService/ListApplications \
  "{\"filters\":[{\"projectIdFilter\":{\"projectId\":\"$PROJECT_ID\"}},{\"nameFilter\":{\"name\":\"$BFF_APP_NAME\",\"method\":\"TEXT_FILTER_METHOD_EQUALS\"}}]}" \
  | jq -c '.applications[0] // empty'; }

BFF_SECRET=""
BFF=$(find_bff)
if [ -z "$BFF" ]; then
  # Confidential web app: authorization code only (the Api adds PKCE and keeps no refresh token),
  # client_secret_post (the ASP.NET Core handler sends the secret in the form body), JWT access tokens, roles
  # asserted in ID and access tokens. Development mode allows plain-http redirect URIs, so only the local stack
  # turns it on (BFF_DEVELOPMENT_MODE).
  created=$(rpc zitadel.application.v2.ApplicationService/CreateApplication "{
    \"projectId\":\"$PROJECT_ID\",\"name\":\"$BFF_APP_NAME\",
    \"oidcConfiguration\":{
      \"redirectUris\":$REDIRECTS,
      \"postLogoutRedirectUris\":$LOGOUTS,
      \"responseTypes\":[\"OIDC_RESPONSE_TYPE_CODE\"],
      \"grantTypes\":[\"OIDC_GRANT_TYPE_AUTHORIZATION_CODE\"],
      \"applicationType\":\"OIDC_APP_TYPE_WEB\",
      \"authMethodType\":\"OIDC_AUTH_METHOD_TYPE_POST\",
      \"version\":\"OIDC_VERSION_1_0\",
      \"developmentMode\":$BFF_DEVELOPMENT_MODE,
      \"accessTokenType\":\"OIDC_TOKEN_TYPE_JWT\",
      \"accessTokenRoleAssertion\":true,
      \"idTokenRoleAssertion\":true,
      \"idTokenUserinfoAssertion\":true
    }}")
  BFF_SECRET=$(jq -r '.oidcConfiguration.clientSecret' <<< "$created")
  # Taken from the create response: listing again straight away can miss the new app (the list lags behind).
  BFF=$(jq -c '{applicationId, oidcConfiguration: {clientId: .oidcConfiguration.clientId}}' <<< "$created")
  log "BFF web app created"
else log "BFF web app exists (its settings are not updated)"; fi
BFF_APP_ID=$(jq -r '.applicationId' <<< "$BFF")
BFF_CLIENT_ID=$(jq -r '.oidcConfiguration.clientId' <<< "$BFF")

# ---------------------------------------------------------------- machine user for operations tools
user_id(){ rpc zitadel.user.v2.UserService/ListUsers \
  "{\"queries\":[{\"userNameQuery\":{\"userName\":\"$1\",\"method\":\"TEXT_QUERY_METHOD_EQUALS\"}}]}" \
  | jq -r '.result[0].userId // empty'; }

OPS_ID=$(user_id "$OPS_USERNAME")
if [ -z "$OPS_ID" ]; then
  OPS_ID=$(rpc zitadel.user.v2.UserService/CreateUser "{\"organizationId\":\"$ORG_ID\",\"username\":\"$OPS_USERNAME\",
    \"machine\":{\"name\":\"Operations tools\",\"description\":\"Client credentials for operations tools\",\"accessTokenType\":\"ACCESS_TOKEN_TYPE_JWT\"}}" \
    | jq -r .id)
  log "machine user $OPS_USERNAME created"
else log "machine user $OPS_USERNAME exists"; fi
# Creates the authorization once. An existing one is left as it is (409): changing ROLES later does not
# update its role keys, so recreate the stack (down -v) or update it by hand.
ALLOW_CONFLICT=1 rpc zitadel.authorization.v2.AuthorizationService/CreateAuthorization \
  "{\"userId\":\"$OPS_ID\",\"projectId\":\"$PROJECT_ID\",\"organizationId\":\"$ORG_ID\",\"roleKeys\":$ROLE_KEYS}" >/dev/null
log "machine user $OPS_USERNAME authorized for $ROLE_KEYS"

# ------------------------------------------------------------------------- human logins from fixtures
# Fixed user IDs from the fixtures, so the `sub` claim is known before Zitadel runs and later stories
# can seed identity links from the same file. Customers hold no role.
jq -c '.logins[]' "$FIXTURES" | while read -r login; do
  username=$(jq -r .username <<< "$login")
  if [ -n "$(user_id "$username")" ]; then log "login $username exists"; continue; fi
  body=$(jq -c --arg org "$ORG_ID" '{
    organizationId: $org, userId: .userId, username: .username,
    human: {
      profile: { givenName: .givenName, familyName: .familyName, preferredLanguage: "en" },
      email: { email: .email, isVerified: true },
      password: { password: .password, changeRequired: false } } }' <<< "$login")
  rpc zitadel.user.v2.UserService/CreateUser "$body" >/dev/null
  log "login $username created ($(jq -r .userId <<< "$login")), email verified"
done

# --------------------------------------------------------------------- client settings for the Api
# The Zitadel:* keys of AD-26, plus the operations tools' client credentials (infra/smoke.sh uses them).
# Kept when the file already describes this instance in this shape; otherwise both secrets are (re)generated.
if [ -s "$OUT_FILE" ] \
  && [ "$(jq -r '.Zitadel.Issuer' "$OUT_FILE")" = "$Z" ] \
  && [ "$(jq -r '.Zitadel.ProjectId' "$OUT_FILE")" = "$PROJECT_ID" ] \
  && [ "$(jq -r '.Zitadel.Bff.ClientId' "$OUT_FILE")" = "$BFF_CLIENT_ID" ]; then
  log "$OUT_FILE is current; secrets not rotated"
else
  if [ -z "$BFF_SECRET" ]; then
    BFF_SECRET=$(call POST "/management/v1/projects/$PROJECT_ID/apps/$BFF_APP_ID/oidc_config/_generate_client_secret" '{}' \
      | jq -r .clientSecret)
  fi
  OPS_SECRET=$(rpc zitadel.user.v2.UserService/AddSecret "{\"userId\":\"$OPS_ID\"}" | jq -r .clientSecret)
  # The secrets reach jq over stdin (printf is a shell builtin), never as arguments.
  { printf '%s' "$BFF_SECRET" | jq -Rs .; printf '%s' "$OPS_SECRET" | jq -Rs .; } \
    | jq -s --arg issuer "$Z" --arg project "$PROJECT_ID" \
        --arg bffId "$BFF_CLIENT_ID" --arg opsId "$OPS_USERNAME" '{
      Zitadel: {
        Issuer: $issuer, MetadataAddress: "\($issuer)/.well-known/openid-configuration", ProjectId: $project,
        Bff: { ClientId: $bffId, ClientSecret: .[0] },
        OperationsTools: { ClientId: $opsId, ClientSecret: .[1] } } }' > "$OUT_FILE.tmp"
  chown "$OUT_UID:$OUT_UID" "$OUT_FILE.tmp"
  chmod 0400 "$OUT_FILE.tmp"
  mv "$OUT_FILE.tmp" "$OUT_FILE"
  log "wrote $OUT_FILE (owner $OUT_UID, mode 0400)"
fi
log "done"
