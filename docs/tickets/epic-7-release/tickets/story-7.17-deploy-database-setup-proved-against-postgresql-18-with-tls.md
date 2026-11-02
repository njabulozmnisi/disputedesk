---
id: 17
type: story
title: "Deploy database setup proved against PostgreSQL 18 with TLS"
parent: epic-7-release
covers: [R38, R33]
after: [5]
hitl: false
risk: medium
refined: true
---

# Deploy database setup proved against PostgreSQL 18 with TLS

## Description

`infra/deploy/aws/deploy.sh` connects to RDS with `sslmode=verify-full` and the region's CA bundle, then on the first deploy runs the unchanged `infra/images/postgres/10-roles-and-databases.sh` as RDS's master user, which is not a superuser (story 7.5, AD-28). Today that path is checked only by `infra/deploy/aws/deploy.test.sh`, which stubs `docker` and therefore never opens a TLS connection or runs a SQL statement; the README's post-deploy step 6 is the only real proof, and it needs an AWS account that does not exist. This story adds `infra/deploy/aws/rds-init.test.sh`. It starts a throwaway PostgreSQL 18 container shaped like RDS: a local CA and a server certificate for an RDS-style host name, a `pg_hba.conf` that admits TCP only over TLS, and a master user with `CREATEDB` and `CREATEROLE` but not superuser, as RDS gives. It then runs the real `deploy.sh`, unchanged, with only its AWS, download, firewall and Compose calls stubbed, so its `verify-full` count query and the database init run against that server for real. It also proves that a plain connection is refused and that a wrong CA or a wrong host name fails `verify-full`. If the unchanged init fails under a non-superuser master, the builder stops and reports `blocked` with the output; the fix is a human decision.

Human decision, 2026-11-01: deploy.sh's verify-full connection and the database setup are proved against a throwaway `postgres:18` with a local CA and a server certificate for the RDS-like host name, a hostssl-only `pg_hba.conf` and a non-superuser master with CREATEDB and CREATEROLE, running the relevant part of `deploy.sh` and the init unchanged, proving a plain connection is refused and a wrong CA fails, under the test lock, locally and in CI, citing the PostgreSQL 18 and RDS docs. There is no AWS account, and no story runs any AWS command or calls AWS.

## Requirements

Verify: `scripts/test-lock.sh run stack <owner> -- infra/deploy/aws/rds-init.test.sh` exits 0 locally and leaves no container, network or temporary file behind; the CI `platform` job runs it and passes; `infra/deploy/aws/deploy.test.sh` still passes; `shellcheck` passes on the new script locally; `actionlint` passes; `git diff` shows no change to `infra/deploy/aws/deploy.sh`, `infra/deploy/aws/input-bounds.sh` or `infra/images/postgres/10-roles-and-databases.sh`; no step reaches an AWS endpoint.

One server serves the whole run, in this order: start it (criterion 1), the refusals (criteria 2 and 3, while it holds no roles or databases), the first deploy (criterion 4), the later deploy (criterion 5).

Inputs (test values, none of them secrets; every value `deploy.sh` reads is within its existing bound in `input-bounds.sh` or `deploy.sh`):
- Server image: `deploy.sh`'s `POSTGRES_IMAGE` line, read from the script (the same pinned image as `docker-compose.yml`'s postgres service); no other image is pulled.
- Network: one `docker network create --internal` network named with the run's PID, with no fixed subnet, so no container on it reaches outside DNS or any other host; no container publishes a host port.
- `DB_HOST`: `disputedesk-dev.abcdefghijkl.eu-west-1.rds.amazonaws.com` (the shape `dev.tftest.hcl` uses; within the 253-character host-name bound), the server's network alias and the server certificate's only `subjectAltName` DNS name. A second alias, `wrong-name.disputedesk.test`, reaches the same server but is not in the certificate.
- Certificates: generated per run with `openssl` into a `mktemp -d` directory, valid for 1 day: a test CA (`req -x509`), a server key and certificate signed by it (`req -new`, then `x509 -req -CA ... -extfile` carrying the one DNS SAN), and a second, unrelated CA for the wrong-CA case. Only commands that LibreSSL 3.3.6 (macOS), OpenSSL 3.0 (the `ubuntu-24.04` runner) and OpenSSL 4.1 all accept.
- Passwords: the committed TestOnly literals, not generated values: `RDS_MASTER_PASSWORD` and `QA_DB_PASSWORD` as `deploy.test.sh` sets them (64 and 32 `[A-Za-z0-9]` characters, within `deploy.sh`'s bounds), the rest from `infra/tests/fixtures/compose.test.env`. Reason: they are already public test values, the server is internal and removed at exit, and fixed values let the no-secret check search for each one.
- The SSM answer the `aws` stub returns: every name `deploy.sh` reads, at those values, with `DB_HOST` as above (or the wrong name, criterion 3).
- `AWS_REGION=eu-west-1`, `SSM_PATH=/disputedesk/dev` and the five image arguments from `compose.test.env`, as in `deploy.test.sh`.

1. **A throwaway PostgreSQL 18 server shaped like RDS** (R38; NFR-INFRA-3, AD-28 Database)
   **Given** a container from `POSTGRES_IMAGE` on the internal network, started as root with the certificate directory mounted read-only; its command installs the server key as a `postgres`-owned `0600` file inside the container (PostgreSQL 18, §18.9.1: the key must not allow group or world access), with the certificate and `pg_hba.conf` beside it, then execs `docker-entrypoint.sh postgres -c ssl=on -c ssl_cert_file=... -c ssl_key_file=... -c hba_file=...` (PostgreSQL 18, §18.9); the `hba_file`'s only TCP line is `hostssl all all all scram-sha-256` and its only `local` line is `local all postgres peer` (PostgreSQL 18, §20.1); the `postgres` user has no password; host file permissions are never loosened
   **When** the script waits for `pg_isready -h <DB_HOST>` inside the container, for at most 60 seconds, then creates `dbmaster` over the socket with the password read by `\getenv` from `-e RDS_MASTER_PASSWORD`
   **Then** a server that is not ready in time fails the run with `the PostgreSQL server did not become ready`, before any deploy; once ready, `openssl` shows the CA with `CA:TRUE` and the server certificate with the one DNS SAN; `docker inspect` shows no port bindings; and `pg_roles` shows `dbmaster` with `rolsuper`, `rolreplication` and `rolbypassrls` false, `rolcreatedb` and `rolcreaterole` true, and no role memberships, matching the RDS master user's documented privileges (Amazon RDS, *Master user account privileges*); the hostssl-only file stands in for `rds.force_ssl = 1` (Amazon RDS, *Using SSL with a PostgreSQL DB instance*)
   **And** the script carries `# shortcut: a local CA and a CREATEROLE master stand in for RDS's certificate chain and rds_superuser; replace with README post-deploy step 6 once the account exists`

2. **A connection without TLS is refused** (R38; NFR-INFRA-3)
   **Given** the server from criterion 1
   **When** `psql` in `POSTGRES_IMAGE` on the network connects to `DB_HOST` as `dbmaster` with the right password and `sslmode=disable`
   **Then** it fails with the server's `no pg_hba.conf entry for host ... user "dbmaster", database "postgres", no encryption`, so the refusal comes from the hostssl-only rule, not from a wrong password

3. **A wrong CA or a wrong host name fails verify-full before the init** (R38; NFR-INFRA-3)
   **Given** the server from criterion 1, still with no roles or databases, and the harness of criterion 4
   **When** `deploy.sh` runs once with the `curl` stub answering the truststore URL with the unrelated CA, and once with `DB_HOST` set to `wrong-name.disputedesk.test` and the right CA
   **Then** each run exits non-zero with `deploy.sh`'s `reading the roles and databases on RDS failed`, the init never runs, `docker compose` is never called, and the server still has no `disputedesk`, `zitadel` or `qa_readonly` role and no `disputedesk` or `zitadel` database
   **And** the wrong-CA output holds libpq's `certificate verify failed`, and the wrong-name output holds `server certificate for "disputedesk-dev.abcdefghijkl.eu-west-1.rds.amazonaws.com" does not match host name "wrong-name.disputedesk.test"` (PostgreSQL 18, §32.19.1), so each run fails for its own reason

4. **The unchanged deploy.sh connects with verify-full and creates the roles and databases with their privileges** (R38; NFR-INFRA-3, AD-28 Database and Deploy)
   **Given** a fresh throwaway copy under the run's temporary directory holding `infra/deploy/aws/deploy.sh`, `infra/deploy/aws/input-bounds.sh` and `infra/images/postgres/10-roles-and-databases.sh` byte for byte, run only by its path in the copy, with stubs on `PATH`: `aws` answers only `ssm get-parameters-by-path` with the Inputs; `curl` answers only the RDS truststore URL (the test CA) and `ip-ranges.json` (a fixed list); `iptables`, `iptables-save` and `systemctl` accept only the calls `deploy.sh` makes; `docker` is a wrapper that passes `docker run` to the real Docker with only `--network <the run's network>` inserted after `run`, and answers only `docker compose -f docker-compose.yml -f docker-compose.aws.yml up -d --wait` by exiting 0; every stub and the wrapper record every call, and any other call exits non-zero naming it
   **When** `deploy.sh` runs (a first deploy)
   **Then** it exits 0; the init output holds no `WARNING`; over the server socket, `deploy.sh`'s count query returns `3 2`, `disputedesk` and `zitadel` each own their database, `qa_readonly` has `default_transaction_read_only=on`, CONNECT on `disputedesk` but not on `zitadel`, and USAGE on schema `public` in `disputedesk`, and `pg_default_acl` in `disputedesk` holds `qa_readonly=r/disputedesk`; the copy holds the `.env` and `rds-ca-bundle.pem` the run wrote, and the repository's own `.env`, if any, has the same checksum as before the run
   **And** if the init fails or warns under the non-superuser master, the builder changes neither script and reports `blocked` with the output

5. **A later deploy finds the roles and databases and skips the init** (R38; AD-28 Deploy)
   **Given** the server after criterion 4
   **When** `deploy.sh` runs again on a fresh copy
   **Then** it exits 0, the init does not run, and the wrapper records `docker compose ... up -d --wait`

6. **It runs under the stack lock locally and in CI, shows no secret, and cleans up** (R33; NFR-INFRA-2)
   **Given** the script
   **When** it is run locally as `scripts/test-lock.sh run stack <owner> -- infra/deploy/aws/rds-init.test.sh`, and in the CI `platform` job as a step after *AWS deploy script*
   **Then** it passes in both; every output it captures (each deploy run's standard output and error, the init output, every stub's and the wrapper's call record, every `psql` and `openssl` output), in passing and failing runs alike, holds none of the run's passwords and no `PRIVATE KEY`, and it never prints the server's log; and on exit, passing or failing, it removes every container on the run's network (`docker ps -aq --filter network=<network>`), then the network, then its temporary directory
   **And** the script's header names the lock command, as `gateway.test.sh`'s does; the `platform` job keeps `timeout-minutes: 15`

## Boundaries

- Must not change: `infra/deploy/aws/deploy.sh`, `infra/deploy/aws/input-bounds.sh`, `infra/images/postgres/10-roles-and-databases.sh`, `infra/deploy/aws/deploy.test.sh`, the database module, `docker-compose.yml`, `docker-compose.aws.yml`, the README.
- Not proved, and not claimed: RDS's own certificate chain (no server presents the real bundle), AWS-LC TLS on RDS 18, the exact `rds_superuser` privileges (an approximation), the `postgres18` parameter group family, and the Api's and Zitadel's own `verify-full` connections from the instance; these wait on the README procedure (P46).
- No Compose stack, no Testcontainers library, no new image beyond the pinned postgres, no new package or tool (`openssl`, `docker` and `jq` are on the `ubuntu-24.04` runner and the development machines), no published host port, no `-P`.
- Stubs replace only `aws`, `curl`, `iptables`, `iptables-save`, `systemctl` and `docker compose`; every `docker run` reaches the real Docker with no argument changed except the inserted `--network`. No label is added; cleanup finds containers by the network filter.
- Passwords reach containers only by `-e NAME` and are read in SQL only by `\getenv`; none appears in any command's arguments.
- bash 3.2-safe (macOS): no `mapfile`, associative arrays or `${var,,}`, and every array expansion guarded under `set -u` when it can be empty.

## Supervisor checks

Deliberate-break proofs, run by the supervisor in a throwaway copy, not by the verifier.

- Add `host all all all scram-sha-256` to the test's `pg_hba.conf`: criterion 2 fails.
- `PGSSLMODE=require` in the copied `deploy.sh`: with `PGSSLROOTCERT` set, libpq still checks the chain (PostgreSQL 18, §32.19.1), so the wrong-CA run still fails and only the wrong-name run succeeds; criterion 3 fails.
- `PGSSLMODE=verify-ca` in the copied `deploy.sh`: the wrong-name run succeeds; criterion 3 fails.
- `createrole_self_grant=set` (no `inherit`) in the copied `deploy.sh`'s `PGOPTIONS`: `CREATE DATABASE ... OWNER` still succeeds (it needs only SET), but the GRANT and REVOKE statements on objects `dbmaster` does not own print `WARNING: no privileges were granted` (or `could be revoked`), because `dbmaster` no longer has the owner's privileges; criterion 4 fails on the `WARNING` and privilege checks.
- Kill the script mid-run (Ctrl-C after the server starts): `docker ps -a` and `docker network ls` show nothing from the run.

## Build facts

- Owner: backend (backend-builder).
- Flags: none (test and CI only; no production code, but it exercises the database credentials path, so security reads the diff in the review blocks if the supervisor asks).
- Lane: B.
- Lock: `stack` locally: build-story puts any container a builder starts directly under `stack`, `gateway.test.sh` names `stack`, and the verifier holds `testcontainers` for its own run. None in CI: it runs directly in the `platform` job, as `gateway.test.sh` does, because a hosted runner runs one job.
- Mechanisms and sources:
  - PostgreSQL 18 server TLS and key file ownership and permissions (<https://www.postgresql.org/docs/18/ssl-tcp.html>);
  - `pg_hba.conf` and `hostssl` (<https://www.postgresql.org/docs/18/auth-pg-hba-conf.html>);
  - libpq `sslmode`, `verify-ca`, `verify-full` and the root-certificate rule for `require` (<https://www.postgresql.org/docs/18/libpq-ssl.html>);
  - `pg_isready` (<https://www.postgresql.org/docs/18/app-pg-isready.html>);
  - psql `\getenv` (<https://www.postgresql.org/docs/18/app-psql.html>);
  - `createrole_self_grant` (<https://www.postgresql.org/docs/18/runtime-config-client.html>);
  - RDS master user privileges (<https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/UsingWithRDS.MasterAccounts.html>, <https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/Appendix.PostgreSQL.CommonDBATasks.Roles.html>);
  - `rds.force_ssl` (<https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/PostgreSQL.Concepts.General.SSL.html>);
  - the RDS CA bundle path `deploy.sh` fetches (<https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/UsingWithRDS.SSL.html>);
  - Docker internal networks and aliases (<https://docs.docker.com/engine/network/drivers/bridge/>);
  - OWASP ASVS 5.0 V12.3.1, V12.3.2 (TLS to the database with certificate validation) and V13.2.2 (least-privileged service accounts) (<https://github.com/OWASP/ASVS>).
- On macOS the run's temporary directory lives under `$TMPDIR`, a path Docker Desktop shares by default, because the server mounts the certificates and `deploy.sh` mounts its CA bundle and the init script from the copy. The certificates are read as root inside the container, so the same mounts work on the Linux runner, where the host owner differs from the image's `postgres` user (uid 70).
- Packages: none.
- Touches:
  - `infra/deploy/aws/rds-init.test.sh` (new; criteria 1 to 6)
  - `.github/workflows/ci.yml` (one step in the `platform` job only; criterion 6)

## References

- parent — docs/tickets/epic-7-release/epic-7-release.md, section Requirements
- story — docs/tickets/epic-7-release/tickets/story-7.5-terraform-dev-environment-and-oidc-deploy.md, criteria 3 and 6
- architecture — docs/architecture/architecture.md, AD-28 (Database, Deploy), AD-27
- prd — docs/prd/prd.md, NFR-INFRA-2, NFR-INFRA-3
- runbook — infra/terraform/README.md, *A half-done database init* and post-deploy check 6
- production path — docs/production-path.md, P46
- research — ~/disputedesk-research/research_notes/LocalStack testing/offline-checks.md, Risk-by-tool table (RDS rows) and *Recommended minimal set* item 3; emulator-alternatives.md and report.md (no emulator enforces `rds.force_ssl` or supports `verify-full`) (outside the repo)

## Notes

- Risk medium (proposed): a first deploy that fails at the database init leaves RDS half-initialized (the README's recovery procedure); the test changes nothing that runs in AWS, and a false pass would only keep today's gap. A check beyond the criteria: the supervisor reproduces criterion 3's wrong-CA run and reads the psql error.
- Human decision, 2026-11-01: prove `deploy.sh`'s verify-full connection and the database setup against a throwaway PostgreSQL 18 with a local CA and server certificate for the RDS-like host name, a hostssl-only `pg_hba.conf` and a non-superuser master with CREATEDB and CREATEROLE; run the verify-full part of `deploy.sh` and the init unchanged; prove a plain connection is refused and a wrong CA fails; run under the test lock locally and in CI; cite the PostgreSQL 18 and RDS docs. No story runs any AWS command or calls AWS.
- Decision: 2026-11-01 (Sofia, 7.17 write) — the whole `deploy.sh` runs, not an extracted copy of its database lines, so "unchanged" holds byte for byte; its AWS, download, firewall and Compose calls are stubbed as in `deploy.test.sh`, and the only change to a real call is the `--network` the `docker` wrapper inserts so `psql` can reach the throwaway server by its host name.
- Decision: 2026-11-01 (Sofia, 7.17 refine) — the lock is `stack` locally, not `testcontainers` (pre-build review 4), and none in CI, matching `gateway.test.sh`.
- Decision: 2026-11-01 (Sofia, 7.17 refine) — one server for the whole run, with the refusals run before the first deploy, instead of a fresh server for the certificate failures; the extra `verify-full` query after the deploy is cut, since the `verify-full` claim rests on `deploy.test.sh`, the hostssl-only rule and criterion 3.
- Decision: 2026-11-01 (Sofia, 7.17 refine) — the committed TestOnly passwords, not generated ones (reason in Inputs).
- Decision: 2026-11-01 (Sofia, 7.17 refine) — the circular SUPERUSER break is dropped; its outcome is asserted in criterion 1.
- Decision: 2026-11-01 (Sofia, 7.17 write) — the wrong host name case is included with the wrong CA: it is the check `verify-full` adds over `verify-ca`, at no extra setup.
- Collision: one step in the `platform` job; 7.15 adds a new job and 7.16 a `terraform` step, in other sections of `ci.yml`. It shares no file with 7.15 or 7.16 otherwise.
