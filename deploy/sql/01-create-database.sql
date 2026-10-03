-- The one-time server setup: the application's role and its database.
-- Run as the postgres superuser:
--
--     psql -U postgres -v app_user='healthone_app' -v app_password='<choose one>' -f 01-create-database.sql
--
-- Variables, all optional:
--
--   app_user      the login the application signs in as.    default sivayaanhms
--   app_password  that role's password.                      default sivayaanhms-dev
--   db_name       the database it owns.                      default sivayaanhms
--   developer     set to anything to also grant CREATEDB, which the unit tests
--                 and the UAT suite need (each creates a throwaway database per
--                 run). A production role is deliberately not given it.
--
-- Safe to re-run: an existing role gets the password given, an existing
-- database is left alone, and the grants are re-applied.
--
-- The role OWNS the database. That is what lets `dotnet ef database update`
-- (deploy\Migrate-Database.ps1), or migrate.exe, create and alter tables as
-- this same role with no superuser involved after this script. The explicit
-- GRANTs below add nothing for a database this role already owns; they are
-- what make the script also correct against a database that already existed
-- under another owner.
--
-- Lower-case names throughout: PostgreSQL folds unquoted identifiers, and a
-- name with capitals has to be double-quoted in every command forever after.
--
-- Each statement is a SELECT that produces the DDL and \gexec that runs it:
-- psql substitutes :'variables' in a query but never inside a $$ block, and
-- CREATE DATABASE refuses to run inside one anyway. format()'s %I quotes an
-- identifier and %L a literal, so a role name or password needing quotes is
-- escaped rather than breaking the statement.

\set ON_ERROR_STOP on

\if :{?app_user}
\else
    \set app_user 'sivayaanhms'
\endif

\if :{?app_password}
\else
    \set app_password 'sivayaanhms-dev'
\endif

\if :{?db_name}
\else
    \set db_name 'sivayaanhms'
\endif

\echo Role :app_user, database :db_name

-- ------------------------------------------------------------------- role

SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'app_user', :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_user')
\gexec

SELECT format('ALTER ROLE %I WITH LOGIN PASSWORD %L', :'app_user', :'app_password')
\gexec

\if :{?developer}
    SELECT format('ALTER ROLE %I CREATEDB', :'app_user')
    \gexec
\endif

-- --------------------------------------------------------------- database

SELECT format('CREATE DATABASE %I OWNER %I ENCODING ''UTF8'' TEMPLATE template0',
              :'db_name', :'app_user')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = :'db_name')
\gexec

-- Ownership, in case the database existed already under someone else. The
-- application's migrations alter tables, which only an owner may do.
SELECT format('ALTER DATABASE %I OWNER TO %I', :'db_name', :'app_user')
\gexec

SELECT format('GRANT ALL PRIVILEGES ON DATABASE %I TO %I', :'db_name', :'app_user')
\gexec

-- ------------------------------------------- inside the database: schema

\connect :db_name

-- From PostgreSQL 15 on, schema public no longer grants CREATE to everyone,
-- so a role that is not its owner cannot create a table in it. Granting
-- ownership of the schema to the application role is what makes the
-- migration work on a database this script did not create.
SELECT format('ALTER SCHEMA public OWNER TO %I', :'app_user')
\gexec

SELECT format('GRANT ALL ON SCHEMA public TO %I', :'app_user')
\gexec

-- Anything already in there, from an earlier owner.
SELECT format('GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO %I', :'app_user')
\gexec

SELECT format('GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO %I', :'app_user')
\gexec

-- And anything a future migration creates, whoever runs it.
SELECT format('ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO %I', :'app_user')
\gexec

SELECT format('ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO %I', :'app_user')
\gexec

\echo Done. Role :app_user owns database :db_name and has every privilege on it.
\echo Next: create the tables - migrate.exe, or db\full-schema.sql which does both halves.
