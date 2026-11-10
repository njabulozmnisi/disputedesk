#!/usr/bin/env bash
# DisputeDesk Zitadel provisioner. Compose runs it inside the zitadel container, as Zitadel's own non-root user, from
# the service's post_start hook (Compose, "Lifecycle hooks"), so it runs whenever Compose creates, recreates or
# restarts the container.
# It creates only what Zitadel's FirstInstance / DefaultInstance environment variables cannot: the project
# and its roles, the web app, the operations machine user, the fixture logins and the hosted login's
# label policy (branding). Instance settings (SMTP, login policy) live in docker-compose.yml. It closes public
# organization registration on an existing instance, and in AWS it also deactivates every human administrator of
# DisputeDesk or the instance (PROVISION_DEACTIVATE_HUMAN_ADMINS).
# Auth:  the IAM_OWNER PAT that FirstInstance writes into the bootstrap volume before Zitadel reports ready.
# Idempotent: every object is looked up before it is created. Client secrets are shown only once,
#        so they are (re)generated only when the output file is missing or belongs to another instance.
# Secrets never appear on a command line: the PAT goes to curl as a header file, bodies go over stdin.
# The container is healthy only while the marker exists: it is removed first and written only when every step passed.
set -euo pipefail

MARKER="${PROVISION_MARKER_FILE:?}"
rm -f "$MARKER"

Z="${PROVISION_CALL_URL:?}"            # Zitadel's public URL as this container calls it (see CURL_OPTS)
OUT_FILE="${PROVISION_OUT_DIR:?}/zitadel-clients.json"
FIXTURES="${PROVISION_FIXTURES_FILE:?}"
PAT_FILE="${PROVISION_PAT_FILE:?}"
APP_ORIGINS="${PROVISION_APP_ORIGINS:?}"   # space-separated origins allowed to complete the sign-in
AUTH_DEVELOPMENT_MODE="${PROVISION_AUTH_DEVELOPMENT_MODE:?}"   # true only where the app origins are plain http (local)
DEACTIVATE_HUMAN_ADMINS="${PROVISION_DEACTIVATE_HUMAN_ADMINS:?}"   # true only where the Mailpit inbox may be public (AWS)
PROJECT_NAME=DisputeDesk
WEB_APP_NAME=disputedesk-web
OPS_USERNAME=operations-tools
READY_DEADLINE=300                     # seconds since the script started (bash's SECONDS)

# Every call goes to Zitadel in this container (127.0.0.1:8080, PROVISION_CONNECT_TO) with the public host in the Host
# header, which is how Zitadel finds its instance; the gateway, which starts only after this hook, is never on the path.
# --max-time bounds every call, so a hung Zitadel fails the hook instead of blocking `docker compose up`.
CURL_OPTS=(--max-time 30 --connect-to "${PROVISION_CONNECT_TO:?}")
# Zitadel can refuse connections or answer 503 for a moment after it reports ready, so the first call retries
# with curl's --retry: on a refused connection or a transient status (408, 429, 500, 502, 503, 504), waiting 1 s and
# doubling, starting no retry after 30 s. Any other status fails at once, and the last status still fails the run.
RETRY_OPTS=(--retry 5 --retry-connrefused --retry-max-time 30)
log(){ printf '[provision] %s\n' "$*" >&2; }

# It is written straight into the app's JSON, so anything but a JSON boolean is refused up front.
case "$AUTH_DEVELOPMENT_MODE" in
  true|false) ;;
  *) log "PROVISION_AUTH_DEVELOPMENT_MODE must be true or false, not '$AUTH_DEVELOPMENT_MODE'"; exit 1 ;;
esac
# Refused up front too, so a mistyped value never quietly leaves a human administrator active.
case "$DEACTIVATE_HUMAN_ADMINS" in
  true|false) ;;
  *) log "PROVISION_DEACTIVATE_HUMAN_ADMINS must be true or false, not '$DEACTIVATE_HUMAN_ADMINS'"; exit 1 ;;
esac

# The hook starts right after the container does, before Zitadel listens (Compose runs post_start after the start, not
# after the health check). Zitadel writes the PAT during its setup, before it reports ready, so the PAT is read after.
until zitadel ready >/dev/null 2>&1; do
  if (( SECONDS >= READY_DEADLINE )); then log "Zitadel was not ready within $READY_DEADLINE s"; exit 1; fi
  sleep 2
done
[ -s "$PAT_FILE" ] || { log "Zitadel is ready but $PAT_FILE is missing or empty"; exit 1; }
log "Zitadel is ready"

umask 077
AUTH_HEADER=$(mktemp)
trap 'rm -f "$AUTH_HEADER"' EXIT
{ printf 'Authorization: Bearer '; tr -d '\n' < "$PAT_FILE"; printf '\n'; } > "$AUTH_HEADER"

# call METHOD PATH [JSON]: prints the body; fails on HTTP >= 400, except 409 when ALLOW_CONFLICT=1.
# RETRY=1 adds RETRY_OPTS; only the first call, an idempotent GET, uses it.
call(){
  local m=$1 p=$2 d=${3:-} tmp code
  tmp=$(mktemp)
  code=$(printf '%s' "$d" | curl "${CURL_OPTS[@]}" ${RETRY:+"${RETRY_OPTS[@]}"} -sS -o "$tmp" -w '%{http_code}' -X "$m" "$Z$p" \
    -H @"$AUTH_HEADER" -H 'Content-Type: application/json' -H 'Connect-Protocol-Version: 1' \
    ${d:+--data-binary @-}) \
    || { log "FAIL $m $p -> curl exit $?"; rm -f "$tmp"; return 1; }   # set -e does not reach inside $(call ...)
  if [ "$code" -ge 400 ]; then
    if [ "${ALLOW_CONFLICT:-0}" = 1 ] && [ "$code" = 409 ]; then rm -f "$tmp"; return 0; fi
    log "FAIL $m $p -> $code $(cat "$tmp")"; rm -f "$tmp"; return 1
  fi
  cat "$tmp"; rm -f "$tmp"
}
rpc(){ call POST "/$1" "${2:-{\}}"; }   # Connect protocol, JSON body

# upload PATH FILE: multipart POST to the Assets API, form field `file` (curl -F). `call` cannot send it, because it
# forces a JSON Content-Type. FILE is always one of the BRAND_* constants below: curl reads ; < and @ in -F values.
# Fails on HTTP >= 400 like `call`, logging only the method, path, status and response body.
upload(){
  local p=$1 f=$2 tmp code
  tmp=$(mktemp)
  code=$(curl "${CURL_OPTS[@]}" -sS -o "$tmp" -w '%{http_code}' "$Z$p" -H @"$AUTH_HEADER" -F "file=@$f")
  if [ "$code" -ge 400 ]; then log "FAIL POST $p -> $code $(cat "$tmp")"; rm -f "$tmp"; return 1; fi
  rm -f "$tmp"
}

# ------------------------------------------------------- hosted login branding: the instance label policy
# Generated from the theme file (label-policy.sh); the four brand files are mounted read-only, one by one.
# The Admin API's update fails with 400 when nothing changes, so the preview is compared first and every 400 fails.
BRAND_TOKENS=/brand/tokens.css
BRAND_FONT=/brand/nunito-sans-latin-400-normal.woff2
BRAND_LOGO=/brand/wordmark.png
BRAND_LOGO_DARK=/brand/wordmark-dark.png
LABEL_FIELDS='{primaryColor, backgroundColor, warnColor, fontColor, primaryColorDark, backgroundColorDark,
  warnColorDark, fontColorDark, themeMode} | map_values(. // "" | ascii_downcase)'

# The first call goes to the API gateway, the part of Zitadel that answers 503 (connection refused inside) for a
# moment after `ready`, so it is the one that retries.
preview=$(RETRY=1 call GET /admin/v1/policies/label/_preview | jq -c .policy)

# The issuer the Api validates is Zitadel's own answer, not the call URL: in AWS the call is plain http to this
# container and the issuer is https (ExternalSecure), at the same host.
ISSUER=$(call GET /.well-known/openid-configuration | jq -er .issuer)
log "issuer $ISSUER"

label_policy=$(bash /provision/label-policy.sh "$BRAND_TOKENS")
if [ "$(jq -c "$LABEL_FIELDS" <<< "$preview")" = "$(jq -c "$LABEL_FIELDS" <<< "$label_policy")" ]; then
  log "label policy colours are current"
else
  call PUT /admin/v1/policies/label "$label_policy" >/dev/null
  log "label policy colours updated"
fi
# shortcut: a changed font or logo file needs down -v, upgrade when rebrands are frequent
if [ -z "$(jq -r '.fontUrl // empty' <<< "$preview")" ]; then
  upload /assets/v1/instance/policy/label/font "$BRAND_FONT"
  log "font uploaded"
fi
if [ -z "$(jq -r '.logoUrl // empty' <<< "$preview")" ]; then
  upload /assets/v1/instance/policy/label/logo "$BRAND_LOGO"
  log "logo uploaded"
fi
if [ -z "$(jq -r '.logoUrlDark // empty' <<< "$preview")" ]; then
  upload /assets/v1/instance/policy/label/logo/dark "$BRAND_LOGO_DARK"
  log "dark logo uploaded"
fi
call POST /admin/v1/policies/label/_activate '{}' >/dev/null
log "label policy activated"

ME=$(call GET /auth/v1/users/me)
ORG_ID=$(jq -r '.user.details.resourceOwner' <<< "$ME")
PROVISIONER_ID=$(jq -er '.user.id' <<< "$ME")
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

# ------------------------------------------------------------------------------- the Api's web app
redirects=() logouts=()
for origin in $APP_ORIGINS; do
  # Sign-out returns to the app's root (AD-7); the handler's own signed-out callback stays registered as well.
  redirects+=("$origin/api/signin-oidc"); logouts+=("$origin/" "$origin/api/signout-callback-oidc")
done
REDIRECTS=$(printf '%s\n' "${redirects[@]}" | jq -R . | jq -sc .)
LOGOUTS=$(printf '%s\n' "${logouts[@]}" | jq -R . | jq -sc .)

find_web_app(){ rpc zitadel.application.v2.ApplicationService/ListApplications \
  "{\"filters\":[{\"projectIdFilter\":{\"projectId\":\"$PROJECT_ID\"}},{\"nameFilter\":{\"name\":\"$WEB_APP_NAME\",\"method\":\"TEXT_FILTER_METHOD_EQUALS\"}}]}" \
  | jq -c '.applications[0] // empty'; }

WEB_APP_SECRET=""
WEB_APP=$(find_web_app)
if [ -z "$WEB_APP" ]; then
  # Confidential web app: authorization code only (the Api adds PKCE and keeps no refresh token),
  # client_secret_post (the ASP.NET Core handler sends the secret in the form body), JWT access tokens, roles
  # asserted in ID and access tokens. Development mode allows plain-http redirect URIs, so only the local stack
  # turns it on (AUTH_DEVELOPMENT_MODE).
  created=$(rpc zitadel.application.v2.ApplicationService/CreateApplication "{
    \"projectId\":\"$PROJECT_ID\",\"name\":\"$WEB_APP_NAME\",
    \"oidcConfiguration\":{
      \"redirectUris\":$REDIRECTS,
      \"postLogoutRedirectUris\":$LOGOUTS,
      \"responseTypes\":[\"OIDC_RESPONSE_TYPE_CODE\"],
      \"grantTypes\":[\"OIDC_GRANT_TYPE_AUTHORIZATION_CODE\"],
      \"applicationType\":\"OIDC_APP_TYPE_WEB\",
      \"authMethodType\":\"OIDC_AUTH_METHOD_TYPE_POST\",
      \"version\":\"OIDC_VERSION_1_0\",
      \"developmentMode\":$AUTH_DEVELOPMENT_MODE,
      \"accessTokenType\":\"OIDC_TOKEN_TYPE_JWT\",
      \"accessTokenRoleAssertion\":true,
      \"idTokenRoleAssertion\":true,
      \"idTokenUserinfoAssertion\":true
    }}")
  WEB_APP_SECRET=$(jq -r '.oidcConfiguration.clientSecret' <<< "$created")
  # Taken from the create response: listing again straight away can miss the new app (the list lags behind).
  WEB_APP=$(jq -c '{applicationId, oidcConfiguration: {clientId: .oidcConfiguration.clientId}}' <<< "$created")
  log "web app created"
else log "web app exists (its settings are not updated)"; fi
WEB_APP_ID=$(jq -r '.applicationId' <<< "$WEB_APP")
WEB_APP_CLIENT_ID=$(jq -r '.oidcConfiguration.clientId' <<< "$WEB_APP")

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

# ------------------------------------------------------------ public organization registration
# Zitadel's hosted login lets anyone create an organization at /ui/login/register/org and become its owner, an
# administrator. Nothing here uses it, so it is closed: the DefaultInstance restriction in docker-compose.yml closes it
# on a new instance, and this Admin API call on one made before. Customers still sign up inside the DisputeDesk
# organization (/ui/login/register), which the restriction does not touch. Setting the current value changes nothing
# and succeeds, so it runs at every start.
call PUT /admin/v1/restrictions '{"disallowPublicOrgRegistration":true}' >/dev/null
log "public organization registration closed"

# ------------------------------------------------------------ human administrators, where the inbox may be public
# Zitadel mails password-reset links to the Mailpit inbox, which may be public in AWS, so there a reader could reset a
# human administrator's password. So each human administrator in scope is deactivated, and Zitadel refuses a
# deactivated user's sign-in. In scope: every administrator role of a DisputeDesk user, and every instance
# administrator role. A user of another organization who administers only that organization is out of scope, so it
# can never fail this step. This provisioner is a machine user with a PAT, so it keeps working. It runs at every start;
# an inactive user is skipped, because DeactivateUser fails on one. It fails closed: the administrator list must be
# whole and hold this provisioner, and a second reading afterwards must find no human administrator in scope active.
ADMIN_SCOPE="{\"filters\":[{\"or\":{\"queries\":[{\"userOrganizationId\":{\"id\":\"$ORG_ID\"}},{\"resource\":{\"instance\":true}}]}}]}"
ADMIN_IDS='[.administrators[].user.id] | unique | .[]'   # one entry per role assignment, so a user can appear twice
# GetUserByID, unlike ListUsers, brings Zitadel's user view up to date before it reads, so it sees each deactivation.
HUMAN_NOT_INACTIVE='.user | if has("human") then .state != "USER_STATE_INACTIVE"
  elif has("machine") then false else error("a user that is neither human nor machine") end'

# active_human_admins: prints the ID of each human administrator in scope that is not inactive.
active_human_admins(){
  local admins ids id active
  admins=$(rpc zitadel.internal_permission.v2.InternalPermissionService/ListAdministrators "$ADMIN_SCOPE") || return 1
  # Whole: one page (Zitadel's default limit, 100) holds every entry. Read: this provisioner, always one, is on it.
  if ! jq -e --arg provisioner "$PROVISIONER_ID" '(.pagination.totalResult | tonumber) == (.administrators | length)
      and any(.administrators[]; .user.id == $provisioner)' <<< "$admins" >/dev/null; then
    log "ListAdministrators did not return every administrator in scope, with this provisioner among them"
    return 1
  fi
  ids=$(jq -r "$ADMIN_IDS" <<< "$admins") || return 1
  for id in $ids; do
    active=$(rpc zitadel.user.v2.UserService/GetUserByID "{\"userId\":\"$id\"}" | jq -r "$HUMAN_NOT_INACTIVE") || return 1
    if [ "$active" = true ]; then printf '%s\n' "$id"; fi
  done
}

if [ "$DEACTIVATE_HUMAN_ADMINS" = true ]; then
  humans=$(active_human_admins)
  for id in $humans; do
    rpc zitadel.user.v2.UserService/DeactivateUser "{\"userId\":\"$id\"}" >/dev/null
    log "human administrator $id deactivated"
  done
  still_active=$(active_human_admins)
  if [ -n "$still_active" ]; then log "human administrators still active: $still_active"; exit 1; fi
  log "no human administrator of DisputeDesk or the instance can sign in"
fi

# --------------------------------------------------------------------- client settings for the Api
# The Zitadel:* and Operations:* keys of AD-26, plus the operations tools' client credentials (infra/tests/smoke.sh and
# scripts/operations-token.sh use them; the Api loads the whole file but binds no option to them, so they reach no
# Api code). Operations:AllowedClients lists the machine user's ID, which is its tokens' sub, as the one operations
# caller (AD-9).
# Kept when the file already describes this instance in this shape (the list exactly [the machine user's ID]); otherwise
# both secrets are (re)generated.
if [ -s "$OUT_FILE" ] \
  && [ "$(jq -r '.Zitadel.Issuer' "$OUT_FILE")" = "$ISSUER" ] \
  && [ "$(jq -r '.Zitadel.ProjectId' "$OUT_FILE")" = "$PROJECT_ID" ] \
  && [ "$(jq -r '.Zitadel.WebApp.ClientId' "$OUT_FILE")" = "$WEB_APP_CLIENT_ID" ] \
  && jq -e --arg ops "$OPS_ID" '.Operations.AllowedClients == [$ops]' "$OUT_FILE" >/dev/null; then
  log "$OUT_FILE is current; secrets not rotated"
else
  if [ -z "$WEB_APP_SECRET" ]; then
    WEB_APP_SECRET=$(call POST "/management/v1/projects/$PROJECT_ID/apps/$WEB_APP_ID/oidc_config/_generate_client_secret" '{}' \
      | jq -r .clientSecret)
  fi
  OPS_SECRET=$(rpc zitadel.user.v2.UserService/AddSecret "{\"userId\":\"$OPS_ID\"}" | jq -r .clientSecret)
  # The secrets reach jq over stdin (printf is a shell builtin), never as arguments.
  { printf '%s' "$WEB_APP_SECRET" | jq -Rs .; printf '%s' "$OPS_SECRET" | jq -Rs .; } \
    | jq -s --arg issuer "$ISSUER" --arg project "$PROJECT_ID" \
        --arg webAppId "$WEB_APP_CLIENT_ID" --arg opsId "$OPS_USERNAME" --arg opsUserId "$OPS_ID" '{
      Zitadel: {
        Issuer: $issuer, MetadataAddress: "\($issuer)/.well-known/openid-configuration", ProjectId: $project,
        WebApp: { ClientId: $webAppId, ClientSecret: .[0] },
        OperationsTools: { ClientId: $opsId, ClientSecret: .[1] } },
      Operations: { AllowedClients: [$opsUserId] } }' > "$OUT_FILE.tmp"
  # Owned by this user, readable by its group too: the api joins that group (group_add in docker-compose.yml).
  chmod 0440 "$OUT_FILE.tmp"
  mv "$OUT_FILE.tmp" "$OUT_FILE"
  log "wrote $OUT_FILE (mode 0440)"
fi
touch "$MARKER"
log "done"
