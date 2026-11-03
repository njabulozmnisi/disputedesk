#!/usr/bin/env bash
# The deploy script's database setup (story 7.17), offline: the unchanged infra/aws/deploy.sh runs from a throwaway
# copy against a throwaway PostgreSQL 18 shaped like RDS, on an internal Docker network: TLS with a local CA and a
# server certificate for an RDS-style host name, a pg_hba.conf that admits TCP only over TLS, and a master user with
# CREATEDB and CREATEROLE but not superuser. Only aws, curl, iptables, iptables-save, systemctl and `docker compose`
# are stubbed; every `docker run` reaches the real Docker with only `--network` added, so deploy.sh's verify-full count
# query and the unchanged infra/db/10-roles-and-databases.sh run against that server for real. No AWS endpoint is
# reached.
#   scripts/test-lock.sh run stack <owner> -- infra/aws/rds-init.test.sh
# Needs docker, openssl and jq; binds no host port. Every check fails the run; everything it starts is removed.
#
# shortcut: a local CA and a CREATEROLE master stand in for RDS's certificate chain and rds_superuser; replace with README post-deploy step 6 once the account exists
set -euo pipefail
cd "$(dirname "$0")/../.."

section(){ printf '\n== %s\n' "$*"; }
fail(){ echo "RDS INIT TEST FAIL: $*" >&2; exit 1; }
ok(){ echo "ok  $*"; }

ENV_FILE=infra/aws/compose.test.env
test_value(){ grep "^$1=" "$ENV_FILE" | cut -d= -f2-; }

# The image deploy.sh runs psql in (docker-compose.yml's pinned postgres); the server runs it too.
POSTGRES_IMAGE=$(sed -n 's/^POSTGRES_IMAGE=//p' infra/aws/deploy.sh)
# The shape of an RDS endpoint (dev.tftest.hcl's), the server's alias and its certificate's only DNS name; the second
# alias reaches the same server under a name the certificate does not hold.
DB_HOST=disputedesk-dev.abcdefghijkl.eu-west-1.rds.amazonaws.com
WRONG_HOST=wrong-name.disputedesk.test
SSM_PATH=/disputedesk/dev
# deploy.test.sh's TestOnly values, within deploy.sh's bounds.
QA_PASSWORD=TestOnlyQaDbPasswordNotASecret00
MASTER_PASSWORD=TestOnlyRdsMasterPasswordNotASecret00000000000000000000000000000
# The names deploy.sh reads from SSM.
SSM_NAMES=(
  APP_DB_PASSWORD ZITADEL_DB_PASSWORD LOGIN_SESSION_COOKIE_SECRET ZITADEL_MASTERKEY ZITADEL_ADMIN_PASSWORD
  LINKING_OTP_HMAC_KEY MAILPIT_UI_AUTH APP_HOST AUTH_HOST MAILPIT_HOST DB_HOST OTEL_LOG_GROUP DATAPROTECTION_SSM_PATH
  DATAPROTECTION_KMS_KEY_ID GATEWAY_ORIGIN_VERIFY QA_DB_PASSWORD RDS_MASTER_PASSWORD
)
IMAGES=(
  "$(test_value API_IMAGE)" "$(test_value WORKER_IMAGE)" "$(test_value GATEWAY_IMAGE)" "$(test_value CORE_BANKING_IMAGE)"
  "$(test_value PROVISION_IMAGE)"
)
# Every password of the run, none of which may appear in any captured output.
SECRETS=(
  "$(test_value APP_DB_PASSWORD)" "$(test_value ZITADEL_DB_PASSWORD)" "$(test_value LOGIN_SESSION_COOKIE_SECRET)"
  "$(test_value ZITADEL_MASTERKEY)" "$(test_value ZITADEL_ADMIN_PASSWORD)" "$(test_value LINKING_OTP_HMAC_KEY)"
  "$(test_value MAILPIT_UI_AUTH)" "$(test_value GATEWAY_ORIGIN_VERIFY)" "$QA_PASSWORD" "$MASTER_PASSWORD"
)
# deploy.sh's "<roles> <databases>" count of the init's three roles and two databases.
COUNT_QUERY="SELECT (SELECT count(*) FROM pg_roles WHERE rolname IN ('disputedesk', 'zitadel', 'qa_readonly')) || ' ' || (SELECT count(*) FROM pg_database WHERE datname IN ('disputedesk', 'zitadel'))"

REAL_DOCKER=$(command -v docker)
run=disputedesk-rds-init-test-$$
server=$run-server
work=$(mktemp -d)
out=$work/out     # every output the test captures; checked for passwords and private keys
repo=$work/repo   # the throwaway copy deploy.sh runs from
mkdir "$out" "$work/bin" "$work/tls" "$work/server"

# Succeeds if any captured output holds a password or a private key.
secret_captured(){
  local secret
  for secret in "${SECRETS[@]}" 'PRIVATE KEY'; do
    if grep -rqF -- "$secret" "$out"; then return 0; fi
  done
  return 1
}

# Fails the run if any captured output holds a password or a private key; run after every capture, so a failure
# message that shows an output never shows a secret.
no_secret_shown(){
  if secret_captured; then fail "a captured output holds a password or a private key"; fi
}

# Runs on every exit, passing or failing (set -e exits included): scans the captured outputs once more, then removes
# every container on the run's network, the network and the temporary directory, naming (never showing) anything it
# cannot remove. Either failure fails a run that would otherwise have passed, so the OK line is printed only here. It
# calls `exit` only on its last line, because an exit inside an EXIT trap skips the rest of the trap.
cleanup_failed=0
not_removed(){ echo "RDS INIT TEST FAIL: cleanup could not remove $1" >&2; cleanup_failed=1; }
finish(){
  local status=$? containers container
  if secret_captured; then
    echo "RDS INIT TEST FAIL: a captured output holds a password or a private key" >&2
    status=1
  fi
  containers=$(docker ps -aq --filter "network=$run" 2>/dev/null) || not_removed "the containers on network $run (listing them failed)"
  for container in $containers; do
    docker rm -fv "$container" >/dev/null 2>&1 || not_removed "container $container"
  done
  if docker network inspect "$run" >/dev/null 2>&1; then
    docker network rm "$run" >/dev/null 2>&1 || not_removed "network $run"
  fi
  rm -rf "$work" || not_removed "temporary directory $work"
  if [ "$cleanup_failed" -eq 1 ] && [ "$status" -eq 0 ]; then status=1; fi
  if [ "$status" -eq 0 ]; then printf '\nRDS INIT TEST OK\n'; fi
  exit "$status"
}
trap finish EXIT
trap 'exit 130' INT TERM

# server_sql DATABASE SQL: SQL over the server's socket as postgres (peer authentication), tuples only, unaligned;
# its standard error goes to the captured outputs too.
server_sql(){
  docker exec -u postgres "$server" psql -v ON_ERROR_STOP=1 -tA -d "$1" -c "$2" 2>> "$out/server-sql.err" \
    | tee -a "$out/server-sql.out"
}

section "certificates: a test CA, a server certificate for $DB_HOST, an unrelated CA (criterion 1)"
# Only req -x509, req -new and x509 -req -CA -extfile, which LibreSSL 3.3 and OpenSSL 3 and 4 all accept.
cat > "$work/tls/openssl.cnf" <<'EOF'
[req]
distinguished_name = subject
x509_extensions = ca
[subject]
[ca]
basicConstraints = critical, CA:TRUE
keyUsage = critical, keyCertSign, cRLSign
subjectKeyIdentifier = hash
EOF
for ca in ca wrong-ca; do
  openssl req -x509 -newkey rsa:2048 -nodes -days 1 -config "$work/tls/openssl.cnf" -subj "/CN=DisputeDesk $ca test" \
    -keyout "$work/tls/$ca.key" -out "$work/tls/$ca.crt" >> "$out/openssl.out" 2>&1
done
openssl req -new -newkey rsa:2048 -nodes -config "$work/tls/openssl.cnf" -subj "/CN=$DB_HOST" \
  -keyout "$work/server/server.key" -out "$work/tls/server.csr" >> "$out/openssl.out" 2>&1
printf 'subjectAltName = DNS:%s\n' "$DB_HOST" > "$work/tls/server.ext"
openssl x509 -req -in "$work/tls/server.csr" -CA "$work/tls/ca.crt" -CAkey "$work/tls/ca.key" -set_serial 1 -days 1 \
  -extfile "$work/tls/server.ext" -out "$work/server/server.crt" >> "$out/openssl.out" 2>&1
openssl x509 -in "$work/tls/ca.crt" -noout -text > "$out/ca.txt"
openssl x509 -in "$work/server/server.crt" -noout -text > "$out/server-certificate.txt"
no_secret_shown
grep -qF 'CA:TRUE' "$out/ca.txt" || fail "the test CA is not a CA: $(cat "$out/ca.txt")"
names=$(grep -A1 'X509v3 Subject Alternative Name' "$out/server-certificate.txt" | tail -n 1 | tr -d ' ')
[ "$names" = "DNS:$DB_HOST" ] || fail "the server certificate's names are not DNS:$DB_HOST alone: $names"
ok "the test CA has CA:TRUE; the server certificate's only subjectAltName is DNS:$DB_HOST"

# The only TCP rule needs TLS, standing in for rds.force_ssl = 1; the socket admits only the postgres OS user.
cat > "$work/server/pg_hba.conf" <<'EOF'
local   all  postgres        peer
hostssl all  all       all   scram-sha-256
EOF

section "a throwaway PostgreSQL 18 shaped like RDS (criterion 1)"
docker network create --internal "$run" >/dev/null
# Started as root, so it can install the key as a postgres-owned 0600 file (PostgreSQL refuses a key that group or
# world can read) whoever owns the mounted copy, then hands over to the image's entrypoint. The entrypoint initialises
# only with a superuser password or with POSTGRES_HOST_AUTH_METHOD=trust; trust leaves postgres without a password and
# reaches only the data directory's pg_hba.conf, which hba_file replaces (the refused plain connection below shows it).
docker run -d --name "$server" --network "$run" --network-alias "$DB_HOST" --network-alias "$WRONG_HOST" \
  -e POSTGRES_HOST_AUTH_METHOD=trust -v "$work/server:/run/server-files:ro" --entrypoint sh "$POSTGRES_IMAGE" -c '
    set -e
    install -d -o postgres -g postgres -m 0700 /etc/postgresql
    install -o postgres -g postgres -m 0600 /run/server-files/server.key /etc/postgresql/server.key
    install -o postgres -g postgres -m 0644 /run/server-files/server.crt /etc/postgresql/server.crt
    install -o postgres -g postgres -m 0644 /run/server-files/pg_hba.conf /etc/postgresql/pg_hba.conf
    exec docker-entrypoint.sh postgres -c ssl=on -c ssl_cert_file=/etc/postgresql/server.crt \
      -c ssl_key_file=/etc/postgresql/server.key -c hba_file=/etc/postgresql/pg_hba.conf' >/dev/null
# Over TCP, so the entrypoint's socket-only setup server does not count as ready. A server that stops fails at once,
# showing only its state and exit code, never its log.
deadline=$((SECONDS + 60))
until docker exec "$server" pg_isready -q -h "$DB_HOST" >/dev/null 2>&1; do
  state=$(docker inspect -f '{{.State.Status}} {{.State.ExitCode}}' "$server")
  case $state in
    running*) ;;
    *) fail "the PostgreSQL server stopped before it was ready: $state" ;;
  esac
  [ "$SECONDS" -lt "$deadline" ] || fail "the PostgreSQL server did not become ready"
  sleep 1
done
ok "the server answers on $DB_HOST:5432"

[ "$(docker inspect -f '{{json .HostConfig.PortBindings}}' "$server")" = '{}' ] || fail "the server publishes a host port"
ok "no host port is published, on an internal network"

status=0
RDS_MASTER_PASSWORD=$MASTER_PASSWORD docker exec -i -e RDS_MASTER_PASSWORD -u postgres "$server" \
  psql -v ON_ERROR_STOP=1 -q -d postgres > "$out/dbmaster.out" 2>&1 <<'SQL' || status=$?
\getenv master_password RDS_MASTER_PASSWORD
CREATE ROLE dbmaster LOGIN CREATEDB CREATEROLE PASSWORD :'master_password';
SQL
no_secret_shown
[ "$status" -eq 0 ] || fail "creating dbmaster failed: $(cat "$out/dbmaster.out")"

[ "$(server_sql postgres "SELECT rolpassword IS NULL FROM pg_authid WHERE rolname = 'postgres'")" = t ] \
  || fail "the postgres user has a password"
ok "the postgres user has no password"
master=$(server_sql postgres "SELECT rolsuper, rolreplication, rolbypassrls, rolcreatedb, rolcreaterole,
  (SELECT count(*) FROM pg_auth_members WHERE member = r.oid) FROM pg_roles r WHERE rolname = 'dbmaster'")
[ "$master" = "f|f|f|t|t|0" ] \
  || fail "dbmaster is not CREATEDB and CREATEROLE alone (super, replication, bypassrls, createdb, createrole, memberships): $master"
ok "dbmaster: CREATEDB and CREATEROLE; not superuser, REPLICATION or BYPASSRLS; no role memberships"

section "a connection without TLS is refused (criterion 2)"
status=0
PGPASSWORD=$MASTER_PASSWORD docker run --rm --network "$run" -e PGPASSWORD "$POSTGRES_IMAGE" \
  psql "host=$DB_HOST user=dbmaster dbname=postgres sslmode=disable" -c 'SELECT 1' > "$out/plain.out" 2>&1 || status=$?
no_secret_shown
[ "$status" -ne 0 ] || fail "a connection without TLS succeeded"
grep -qE 'no pg_hba.conf entry for host "[^"]+", user "dbmaster", database "postgres", no encryption' "$out/plain.out" \
  || fail "the plain connection did not fail on the hostssl rule: $(cat "$out/plain.out")"
ok "sslmode=disable as dbmaster with the right password: no pg_hba.conf entry ..., no encryption"

section "deploy.sh's harness: stubs for aws, curl, iptables, iptables-save, systemctl and docker compose"
cat > "$work/bin/stub" <<'STUB'
#!/usr/bin/env bash
# Records each call as one line, then answers only the calls deploy.sh makes; any other call fails, naming itself.
# `docker run` goes to the real Docker with only --network added after run.
set -euo pipefail
call="$(basename "$0") $*"
echo "$call" >> "$STUB_CALLS"
case $call in
  "aws ssm get-parameters-by-path --region eu-west-1 --path /disputedesk/dev --recursive --with-decryption --output json")
    cat "$STUB_PARAMETERS" ;;
  "curl -fsS --max-time 60 --retry 3 https://truststore.pki.rds.amazonaws.com/eu-west-1/eu-west-1-bundle.pem")
    cat "$STUB_CA_BUNDLE" ;;
  "curl -fsS --max-time 60 --retry 3 https://ip-ranges.amazonaws.com/ip-ranges.json")
    cat "$STUB_RANGES" ;;
  "iptables -C DOCKER-USER -s 10.201.89.0/24 -d 169.254.169.254/32 -j DROP")
    exit 1 ;; # the rule is absent
  "iptables -I DOCKER-USER 1 -s 10.201.89.10/32 -d 169.254.169.254/32 -j RETURN" \
  | "iptables -I DOCKER-USER 2 -s 10.201.89.11/32 -d 169.254.169.254/32 -j RETURN" \
  | "iptables -I DOCKER-USER 3 -s 10.201.89.0/24 -d 169.254.169.254/32 -j DROP" \
  | "iptables-save -f /etc/sysconfig/iptables" \
  | "systemctl enable iptables" \
  | "docker compose -f docker-compose.yml -f infra/aws/compose.yml up -d --wait")
    ;;
  "docker run "*)
    shift
    exec "$REAL_DOCKER" run --network "$STUB_NETWORK" "$@" ;;
  *)
    echo "stub: unexpected call: $call" >&2
    exit 97 ;;
esac
STUB
chmod +x "$work/bin/stub"
for tool in aws curl docker iptables iptables-save systemctl; do ln -s stub "$work/bin/$tool"; done
echo '{ "prefixes": [{ "ip_prefix": "192.0.2.0/26", "service": "CLOUDFRONT_ORIGIN_FACING" }], "ipv6_prefixes": [] }' \
  > "$work/ranges.json"

# write_parameters DB_HOST: the aws stub's answer, every name deploy.sh reads at its test value, with DB_HOST as given.
write_parameters(){
  local name value
  for name in "${SSM_NAMES[@]}"; do
    case $name in
      DB_HOST) value=$1 ;;
      QA_DB_PASSWORD) value=$QA_PASSWORD ;;
      RDS_MASTER_PASSWORD) value=$MASTER_PASSWORD ;;
      *) value=$(test_value "$name") ;;
    esac
    printf '%s\t%s\n' "$SSM_PATH/$name" "$value"
  done | jq -R -s '{ Parameters: [split("\n")[] | select(. != "") | split("\t") | { Name: .[0], Value: .[1], Type: "SecureString" }] }' \
    > "$work/parameters.json"
}

# run_deploy NAME DB_HOST CA_FILE: runs deploy.sh by its path in a fresh copy of the three scripts, with the stubs on
# PATH, the SSM answer holding DB_HOST and the truststore URL answering CA_FILE; its output and the stubs' calls go to
# $out/NAME.out and $out/NAME.calls. Sets status.
run_deploy(){
  write_parameters "$2"
  rm -rf "$repo"
  mkdir -p "$repo/infra/aws" "$repo/infra/db"
  cp infra/aws/deploy.sh infra/aws/input-bounds.sh "$repo/infra/aws/"
  cp infra/db/10-roles-and-databases.sh "$repo/infra/db/"
  status=0
  env PATH="$work/bin:$PATH" AWS_REGION=eu-west-1 SSM_PATH="$SSM_PATH" REAL_DOCKER="$REAL_DOCKER" STUB_NETWORK="$run" \
    STUB_CALLS="$out/$1.calls" STUB_PARAMETERS="$work/parameters.json" STUB_CA_BUNDLE="$3" STUB_RANGES="$work/ranges.json" \
    "$repo/infra/aws/deploy.sh" "${IMAGES[@]}" > "$out/$1.out" 2>&1 || status=$?
  no_secret_shown
}

section "a wrong CA or a wrong host name fails verify-full before the init (criterion 3)"
# expect_refused NAME DB_HOST CA_FILE LIBPQ_ERROR
expect_refused(){
  run_deploy "$1" "$2" "$3"
  [ "$status" -ne 0 ] || fail "$1: the deploy succeeded"
  grep -qF 'reading the roles and databases on RDS failed' "$out/$1.out" \
    || fail "$1: the deploy did not fail on its count query: $(cat "$out/$1.out")"
  grep -qF -- "$4" "$out/$1.out" || fail "$1: psql did not fail with '$4': $(cat "$out/$1.out")"
  ! grep -qF 10-roles-and-databases.sh "$out/$1.calls" || fail "$1: the init ran"
  ! grep -q '^docker compose' "$out/$1.calls" || fail "$1: Compose was started"
  [ "$(server_sql postgres "$COUNT_QUERY")" = "0 0" ] || fail "$1: the server holds some of the roles or databases"
  ok "$1: deploy.sh exits $status, 'reading the roles and databases on RDS failed', with libpq's '$4';"
  ok "  no init, no Compose, and no disputedesk, zitadel or qa_readonly role or database on the server"
}
expect_refused wrong-ca "$DB_HOST" "$work/tls/wrong-ca.crt" 'certificate verify failed'
expect_refused wrong-name "$WRONG_HOST" "$work/tls/ca.crt" \
  "server certificate for \"$DB_HOST\" does not match host name \"$WRONG_HOST\""

section "the first deploy connects with verify-full and creates the roles and databases (criterion 4)"
repository_env(){ if [ -f .env ]; then cksum < .env; else echo absent; fi; }
repository_env_before=$(repository_env)
run_deploy first-deploy "$DB_HOST" "$work/tls/ca.crt"
[ "$status" -eq 0 ] || fail "the first deploy failed: $(cat "$out/first-deploy.out")"
grep -qF 10-roles-and-databases.sh "$out/first-deploy.calls" || fail "the init did not run"
! grep -qF WARNING "$out/first-deploy.out" || fail "the init warned: $(cat "$out/first-deploy.out")"
ok "deploy.sh exits 0; the init ran as dbmaster with no WARNING"

[ "$(server_sql postgres "$COUNT_QUERY")" = "3 2" ] || fail "deploy.sh's count query does not return 3 2"
owners=$(server_sql postgres "SELECT datname || ' ' || pg_get_userbyid(datdba) FROM pg_database
  WHERE datname IN ('disputedesk', 'zitadel') ORDER BY datname")
[ "$owners" = "disputedesk disputedesk
zitadel zitadel" ] || fail "disputedesk and zitadel do not each own their database: $owners"
ok "the count query returns 3 2; disputedesk and zitadel each own their database"

[ "$(server_sql postgres "SELECT rolconfig FROM pg_roles WHERE rolname = 'qa_readonly'")" = '{default_transaction_read_only=on}' ] \
  || fail "qa_readonly does not have default_transaction_read_only=on"
connect=$(server_sql postgres "SELECT has_database_privilege('qa_readonly', 'disputedesk', 'CONNECT'),
  has_database_privilege('qa_readonly', 'zitadel', 'CONNECT'), has_database_privilege('zitadel', 'disputedesk', 'CONNECT')")
[ "$connect" = "t|f|f" ] \
  || fail "CONNECT for qa_readonly on disputedesk and zitadel, and for zitadel on disputedesk, is not t|f|f: $connect"
# PostgreSQL 15+ gives every role USAGE on public by default, so zitadel's f shows the init's REVOKE took effect.
usage=$(server_sql disputedesk "SELECT has_schema_privilege('qa_readonly', 'public', 'USAGE'),
  has_schema_privilege('zitadel', 'public', 'USAGE')")
[ "$usage" = "t|f" ] || fail "USAGE on schema public in disputedesk for qa_readonly and zitadel is not t|f: $usage"
default_acl=$(server_sql disputedesk "SELECT defaclacl FROM pg_default_acl")
[[ $default_acl == *qa_readonly=r/disputedesk* ]] || fail "pg_default_acl in disputedesk is not qa_readonly=r/disputedesk: $default_acl"
ok "qa_readonly: default_transaction_read_only=on, CONNECT on disputedesk not zitadel, USAGE on public;"
ok "  zitadel: neither CONNECT on disputedesk nor USAGE on public there; pg_default_acl holds qa_readonly=r/disputedesk"

[ -f "$repo/.env" ] || fail "the run wrote no .env in the copy"
cmp -s "$work/tls/ca.crt" "$repo/rds-ca-bundle.pem" || fail "the copy's rds-ca-bundle.pem is not the test CA"
[ "$(repository_env)" = "$repository_env_before" ] || fail "the repository's own .env changed"
ok "the copy holds the .env and rds-ca-bundle.pem the run wrote; the repository's .env is unchanged"

section "a later deploy finds the roles and databases and skips the init (criterion 5)"
run_deploy later-deploy "$DB_HOST" "$work/tls/ca.crt"
[ "$status" -eq 0 ] || fail "the later deploy failed: $(cat "$out/later-deploy.out")"
! grep -qF 10-roles-and-databases.sh "$out/later-deploy.calls" || fail "the init ran again"
grep -qxF 'docker compose -f docker-compose.yml -f infra/aws/compose.yml up -d --wait' "$out/later-deploy.calls" \
  || fail "Compose up was not called"
ok "deploy.sh exits 0, skips the init and calls docker compose ... up -d --wait"

section "no secret in any captured output (criterion 6)"
no_secret_shown
ok "no password and no PRIVATE KEY in any deploy output, stub call record, psql or openssl output"
