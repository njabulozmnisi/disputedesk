#!/usr/bin/env bash
# Prints an access token for operations tools on the local stack: the OAuth 2.0 client-credentials grant
# (RFC 6749 section 4.4) as the operations machine user, with the AD-9 scope, so the token carries this project's
# audience and roles. Paste it as the bearer token, for example:
#   curl -H "Authorization: Bearer $(scripts/operations-token.sh)" http://localhost:8080/api/v1/operations/disputes/<disputeId>
# Needs the stack running (docker compose up --wait), curl and jq. The machine user's client ID and secret are read
# from the zitadel-clients volume through the provisioner image, as infra/smoke.sh does; they reach curl as a config on
# stdin, never as an argument, and only the access token is printed.
set -euo pipefail

AUTH="${AUTH_ORIGIN:-http://auth.localhost:8080}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# docker's own stderr is dropped and the file is never echoed: either could hold a secret.
if ! clients=$(docker compose -f "$ROOT/docker-compose.yml" run --rm --no-deps -T --entrypoint cat provision /out/zitadel-clients.json 2>/dev/null); then
  echo "could not read the clients file from the provision volume; is the stack up?" >&2
  exit 1
fi
project=$(jq -er .Zitadel.ProjectId <<< "$clients")

jq -er '.Zitadel.OperationsTools | "user = \"\(.ClientId):\(.ClientSecret)\""' <<< "$clients" \
  | curl -fsS --max-time 30 -K - "$AUTH/oauth/v2/token" --data-urlencode grant_type=client_credentials \
      --data-urlencode "scope=openid urn:zitadel:iam:org:project:id:$project:aud urn:zitadel:iam:org:projects:roles" \
  | jq -er .access_token
