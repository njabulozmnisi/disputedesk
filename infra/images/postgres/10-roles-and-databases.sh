#!/bin/sh
# Runs once, on the first start of an empty data volume (docker-entrypoint-initdb.d).
# One shared server, one database and one least-privileged role per consumer:
#   disputedesk  the app (Api and Worker). Owns its database, so it runs its own migrations.
#   zitadel      the identity provider. Non-superuser, owns only its own database.
#   qa_readonly  QA evidence queries. SELECT on the app schema only, read-only sessions.
# Passwords arrive as environment variables. psql reads them itself (\getenv, psql 15+), so they never
# appear on a command line, and binds them as psql variables, never spliced into SQL.
set -eu

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<'SQL'
\getenv app_password APP_DB_PASSWORD
\getenv zitadel_password ZITADEL_DB_PASSWORD
\getenv qa_password QA_DB_PASSWORD
CREATE ROLE disputedesk LOGIN PASSWORD :'app_password';
CREATE DATABASE disputedesk OWNER disputedesk;
REVOKE ALL ON DATABASE disputedesk FROM PUBLIC;

CREATE ROLE zitadel LOGIN PASSWORD :'zitadel_password';
CREATE DATABASE zitadel OWNER zitadel;
REVOKE ALL ON DATABASE zitadel FROM PUBLIC;

CREATE ROLE qa_readonly LOGIN PASSWORD :'qa_password';
ALTER ROLE qa_readonly SET default_transaction_read_only = on;
GRANT CONNECT ON DATABASE disputedesk TO qa_readonly;

\connect disputedesk
-- The app schema is `public` (EF Core's default). Only its owner (the database owner) and QA may use it.
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO qa_readonly;
-- Tables created later by migrations (run as disputedesk) become readable by QA automatically.
ALTER DEFAULT PRIVILEGES FOR ROLE disputedesk IN SCHEMA public GRANT SELECT ON TABLES TO qa_readonly;
SQL
