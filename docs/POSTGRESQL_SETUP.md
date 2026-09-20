# PostgreSQL: setup and configuration

SivayaanHMS runs on **PostgreSQL** (16 or later; 18 is what it was built
against). This is the one document for everything about that: installing it,
creating the application's role and database, and every setting the
application reads.

Everything the application knows about its database comes from **one
section of one file** — `Database` in `appsettings.json` — and nowhere else.
There is no connection string assembled in code, no second file to keep in
step, and every key in the section can be overridden by an environment
variable of the same name. This document is the reference for that section.

---

## 1 · Install PostgreSQL

Windows: the EnterpriseDB installer from postgresql.org. Take the defaults
— port 5432, a service named `postgresql-x64-<version>` that starts with the
machine — and remember the `postgres` superuser password it asks you to
choose. That password is used exactly once, in the next step, and is not
something the application ever knows.

The installer does not put `psql` on `PATH`. It lives in
`C:\Program Files\PostgreSQL\<version>\bin`; the deploy scripts find it
there on their own, and the commands below assume it is on `PATH` or spelled
out in full.

---

## 2 · Create the role and the database

Once per server, as the superuser:

```powershell
psql -U postgres -v app_password='<choose a password>' -f deploy\sql\01-create-database.sql
```

On a **developer's machine** add `-v developer=1`:

```powershell
psql -U postgres -v app_password='sivayaanhms-dev' -v developer=1 -f deploy\sql\01-create-database.sql
```

That creates:

| | |
|---|---|
| role `sivayaanhms` | the application signs in as this; it owns the database and nothing else on the server |
| database `sivayaanhms` | owned by that role, UTF-8 |
| `CREATEDB` on the role | **developer only** — the unit tests and the UAT suite create and drop a throwaway database per run, and need to be allowed to |

The role owns the database rather than merely reading and writing it, and
that is a deliberate difference from the SQL Server setup this replaced.
There, the app login had `db_datareader`/`db_datawriter` and the migration
ran as an administrator. PostgreSQL has no tidy equivalent — the tables a
migration creates are owned by whoever ran it, and a second role that owns
the schema means a second password to keep. One role that owns its own
database, and no superuser after this step, is simpler and no less safe:
the role cannot see, let alone touch, anything outside `sivayaanhms`.

The password `sivayaanhms-dev` is the one `appsettings.Local.json`, the unit
tests and the UAT harness all default to on a developer's machine. It is a
development convenience on a server nothing outside the machine can reach;
a production server gets its own.

---

## 3 · The `Database` section

```json
"Database": {
  "Host": "localhost",
  "Port": 5432,
  "Name": "sivayaanhms",
  "Username": "sivayaanhms",
  "Password": "",
  "SslMode": "Prefer",
  "Pooling": true,
  "MinPoolSize": 0,
  "MaxPoolSize": 50,
  "CommandTimeoutSeconds": 30,
  "ConnectionTimeoutSeconds": 30,
  "ApplicationName": "SivayaanHMS"
}
```

| Key | What it is | Notes |
|---|---|---|
| `Host` | machine running PostgreSQL | `localhost` on the clinic PC; a hostname for a managed server |
| `Port` | | 5432 unless the server says otherwise |
| `Name` | the database | lower-case, as created above |
| `Username` | the application role | never `postgres` |
| `Password` | that role's password | **empty in `appsettings.json` on purpose** — see §4 |
| `SslMode` | `Disable`, `Allow`, `Prefer`, `Require`, `VerifyCA`, `VerifyFull` | `Prefer` on the same machine; `Require` or stronger for anything over a network — cloud providers insist, and traffic without it is readable |
| `Pooling`, `MinPoolSize`, `MaxPoolSize` | connection pool | every domain service opens a short-lived context per call; without a pool each one is a fresh TCP handshake and authentication |
| `CommandTimeoutSeconds` | how long one statement may run | 30 is Npgsql's own default; the year-end GST summary is the one query that has ever approached it |
| `ConnectionTimeoutSeconds` | how long to wait for the server to accept a connection | longer than the default 15 on purpose — after a reboot the API service can start before PostgreSQL is listening |
| `ApplicationName` | shows in `pg_stat_activity` | so a DBA can tell this application's sessions from anything else on the same role |
| `MigrateOnStartup` | apply pending migrations when the API starts | **absent by default**, which means *yes in Development, no anywhere else*. A server applies migrations with `deploy\Migrate-Database.ps1`, never as a race between service starts |
| `ConnectionString` | a complete Npgsql connection string | optional; when set, replaces **every** key above. For a managed host that hands you one ready-made |

The C# behind it is `backend/src/SivayaanHMS.Data/DatabaseOptions.cs`, and
`BuildConnectionString()` there is the only place a connection string is ever
put together. It goes through `NpgsqlConnectionStringBuilder`, so a password
containing `;` or `'` is escaped rather than silently truncating the string.

---

## 4 · Where the password goes

`appsettings.json` is committed, so it carries no password and **the API
refuses to start without one** — with a message naming the key, rather than
at the first request with an authentication error that names nothing.

| Where | File | |
|---|---|---|
| developer's machine | `backend/src/SivayaanHMS.Api/appsettings.Local.json` | git-ignored; loaded last so it overrides everything; excluded from `dotnet publish` so it can never ship |
| a server | `appsettings.Production.json` in the published folder | copied from `appsettings.Production.json.template` by `Deploy-Production.ps1` on first run; never overwritten after |
| anywhere | environment variable `Database__Password` | the usual ASP.NET shape; every other key works the same way, `Database__Host` and so on |

The developer's file needs only what differs from `appsettings.json`:

```json
{
  "Database": {
    "Username": "sivayaanhms",
    "Password": "sivayaanhms-dev"
  },
  "PlatformAdmin": { "Username": "EnterpriseAdmin", "Password": "..." }
}
```

---

## 5 · Dates and times

Every `DateTime` in the schema is a `timestamp without time zone`: clinic-local
wall-clock time, stored and returned exactly as written, with no offset. That
is what the desktop application always did, what the client sends
(`2026-09-19T08:58:00`) and expects back, and it is set once for every
`DateTime` property in `AppDbContext.ConfigureConventions` rather than per
column where one could be missed.

Npgsql's *default* is the other type, `timestamp with time zone`, and it
refuses to write a `DateTime` whose `Kind` is not `Utc` — which is every value
this application produces. If a new column ever arrives as
`timestamp with time zone` in a migration, the convention has been bypassed
and the first save to it will fail with exactly that message.

The real fix — a per-tenant timezone and UTC storage — is a product decision
recorded in `SAAS_MIGRATION.md` (finding 4), and moving providers does not
change it.

---

## 6 · What differs from the SQL Server it replaced

Kept here because each one is a thing somebody will otherwise rediscover.

- **Case.** SQL Server's default collation compares case-insensitively;
  PostgreSQL's does not. The application never relied on the server for
  this — every search lowers both sides (`SearchText`), usernames and clinic
  codes are lower-cased before they are stored or looked up (`UserName`,
  `TenantsController`) — so nothing changed, and `CaseInsensitiveSearchTests`
  no longer has to force a case-sensitive collation to prove it.
- **Document numbering.** `NumberService` was a SQL Server batch with lock
  hints and `OUTPUT` into a table variable. It is now a single
  `INSERT … ON CONFLICT DO UPDATE … RETURNING`, which is the same guarantee
  in the dialect that has a word for it. `NumberServiceConcurrencyTests`
  fires forty concurrent callers at it and still expects forty distinct
  numbers.
- **The duplicate-medicine index** (`IX_Products_TenantId_SearchKey`) is a
  partial unique index with the predicate `"IsDeleted" = false`. On SQL
  Server the predicate's quoting depended on a session option that `sqlcmd`
  and `SqlClient` set differently, and the index was silently lost by one
  deployment. PostgreSQL has one quoting rule; the trap is gone.
- **Identifiers are quoted.** EF Core keeps the model's PascalCase names,
  and PostgreSQL folds anything unquoted to lower case, so in psql it is
  `SELECT * FROM "Patients"`, not `patients`. The migration script and
  `NumberService` are written that way; anything hand-typed has to be too.
- **Migrations started over.** The SQL Server migration history was
  provider-specific (`nvarchar`, `datetime2`, `uniqueidentifier`) and cannot
  apply to PostgreSQL, so the `Migrations` folder holds a single new
  `InitialCreate`. An existing SQL Server database is not upgraded in place;
  its data would be exported and loaded into a freshly migrated PostgreSQL
  database, which is a data-migration exercise this repository does not yet
  contain.

---

## 7 · Backups

`Migrate-Database.ps1` takes a verified `pg_dump` before every release. A
scheduled nightly one is still nobody's job; when it becomes somebody's, it is
one command, and the restore is one more:

```powershell
$env:PGPASSWORD = '<the password from appsettings.Production.json>'
pg_dump -h localhost -U sivayaanhms -d sivayaanhms -Fc --no-owner --no-privileges -f C:\SivayaanHMS\DBBackup\sivayaanhms-$(Get-Date -Format yyyyMMdd).dump
```

```powershell
Stop-Service SivayaanHMSApi
pg_restore -h localhost -U sivayaanhms -d sivayaanhms --clean --if-exists --no-owner --no-privileges C:\SivayaanHMS\DBBackup\<file>.dump
Start-Service SivayaanHMSApi
```

`-Fc` is the custom archive format: compressed, and restorable table-by-table,
which a plain SQL dump is not. The password goes through `PGPASSWORD`, which
the tools read, rather than a command-line argument, which the process list
would show.

---

## 8 · Checking a database by hand

```powershell
psql -h localhost -U sivayaanhms -d sivayaanhms
```

```sql
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1;
SELECT indexname, indexdef FROM pg_indexes WHERE indexname = 'IX_Products_TenantId_SearchKey';
SELECT "Slug", "ClinicName", "LicenseExpiresOn" FROM "Tenants";
```

Throwaway databases the test suites leave behind after a crash start
`sivayaanhms_test_` or `sivayaanhms_uat_`; the sweep command is in
`backend/tools/SivayaanHMS.Automation/README.md`.
