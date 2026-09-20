-- The one-time server setup: the application's role and its database.
-- Run as the postgres superuser, once, on a fresh server:
--
--     psql -U postgres -v app_password='<choose a password>' -f 01-create-database.sql
--
-- For a developer's machine, add -v developer=1 so the role may also CREATE
-- DATABASE - the unit tests and the UAT suite each create a throwaway
-- database per run. A production role is deliberately not allowed to.
--
-- Safe to re-run: an existing role gets the password given, an existing
-- database is left alone.
--
-- The role OWNS the database. That is what lets `dotnet ef database update`
-- (deploy\Migrate-Database.ps1) create and alter tables as this same role,
-- with no superuser involved after this script. The application itself only
-- ever reads and writes rows, but PostgreSQL has no clean way to hand a role
-- migration rights and take them back between releases short of a second
-- role, and two passwords to keep is worse than one owner.
--
-- Lower-case names throughout: PostgreSQL folds unquoted identifiers, and a
-- name with capitals has to be double-quoted in every command forever after.
--
-- Each statement is a SELECT that produces the DDL and \gexec that runs it:
-- psql substitutes :'app_password' in a query but never inside a $$ block,
-- and CREATE DATABASE refuses to run inside one anyway.

\set ON_ERROR_STOP on

\if :{?app_password}
\else
    \echo Pass the password: psql -U postgres -v app_password='...' -f 01-create-database.sql
    \quit 1
\endif

SELECT format('CREATE ROLE sivayaanhms LOGIN PASSWORD %L', :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sivayaanhms')
\gexec

SELECT format('ALTER ROLE sivayaanhms WITH LOGIN PASSWORD %L', :'app_password')
\gexec

SELECT 'CREATE DATABASE sivayaanhms OWNER sivayaanhms ENCODING ''UTF8'' TEMPLATE template0'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'sivayaanhms')
\gexec

-- Developer only: throwaway databases for the test suites.
\if :{?developer}
    ALTER ROLE sivayaanhms CREATEDB;
\endif

\echo Role sivayaanhms and database sivayaanhms are in place.
