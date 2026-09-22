# Database releases

Everything from here is an upgrade of a database that holds a clinic's real
records. `FIRST_DEPLOYMENT.md` covers the install that has now happened once
and will not happen again on this machine.

The rule that everything below follows from:

> **The application never changes the schema in production.** Migrating is a
> release step of its own, run before the new build serves traffic, with a
> verified backup taken first.

`Program.cs` still migrates at startup, but only in Development — or when
`Database:MigrateOnStartup` says so explicitly, which a server's settings never
do — and it must stay that way. Two instances starting together would race
each other to alter the same tables.

Where the database is comes from one place: the `Database` section of
`appsettings.Production.json`, described in `POSTGRESQL_SETUP.md`. The
scripts below read it from there rather than taking a server as a parameter,
so a backup, a migration and the API can never mean different databases.

---

## 1 · The release, in order

```powershell
# 1. see what a release would do, without doing it
.\deploy\Migrate-Database.ps1 -DryRun

# 2. stop serving, so nothing writes through a half-applied schema
Stop-Service SivayaanHMSApi

# 3. back up and migrate
.\deploy\Migrate-Database.ps1

# 4. publish the new build and start again
.\deploy\Deploy-Production.ps1
```

Then the frontend, which is independent: pushing to the branch Cloudflare
Pages watches rebuilds and deploys it.

**Migrate before you publish, not after.** The new build may expect a column
the old schema does not have; the old build tolerating a new column is the
easier direction, which is why the order is this way round and not the other.

### What `Migrate-Database.ps1` does

1. Reads the `Database` section of `appsettings.Production.json`, and checks
   the server is reachable and the role signs in, with a clearer message than
   the API gives.
2. **Backs up. Always** — even when nothing is pending — and runs
   `pg_restore --list` over the result. A backup nobody has read back is a
   hope, not a rollback.
3. Lists what is pending. Stops here on `-DryRun`.
4. Applies through `dotnet ef database update`.
5. Verifies nothing is still pending, and that
   `IX_Products_TenantId_SearchKey` survived.

It is safe to run on every release whether or not that release contains a
migration: with nothing pending it takes a backup, says *already current* and
changes nothing. That is deliberate — deciding "does this release have a
migration?" is exactly the judgement that gets made wrong at seven in the
evening.

It never drops, never resets, never calls `EnsureCreated`.

---

## 2 · When it goes wrong

The script prints the exact command, filled in with the backup it just took.
It looks like this:

```powershell
Stop-Service SivayaanHMSApi

$env:PGPASSWORD = '<the password from appsettings.Production.json>'
pg_restore -h localhost -p 5432 -U sivayaanhms -d sivayaanhms --clean --if-exists --no-owner --no-privileges "C:\SivayaanHMS\DBBackup\sivayaanhms-pre-migration-<stamp>.dump"
```

PostgreSQL runs each migration in its own transaction, so a failed one rolls
itself back and the schema is at whichever migration last succeeded — but
check that before trusting it, and restore if in doubt.

Then redeploy the **previous** build, because the running one now expects a
schema that no longer exists.

**Restore the backup; do not down-migrate.** EF can generate a `Down` for a
schema change, but it cannot generate one for the data that change destroyed.
A dropped column's contents are gone whatever the `Down` says. The backup is
the only rollback that returns the data as well as the shape.

This whole path was rehearsed on a restored copy before it was written down —
apply, verify, restore, confirm the row counts came back.

---

## 3 · Writing a migration that can be released safely

Production data changes what is a safe migration. These are the rules that
matter now and did not before.

### Additive first, destructive later — never in the same release

A release that adds a column and a release that drops one are different
risks. Split anything that renames or removes into two, with a deployment in
between:

| | Release N | Release N+1 |
|---|---|---|
| **Rename a column** | add the new one, write to both | stop writing the old one, drop it |
| **Drop a column** | stop reading it in code | drop it |
| **Tighten a constraint** | add it as nullable, backfill | make it required |

The reason is rollback. If release N only added things, rolling the *code*
back still works against the new schema — no database restore needed, and the
clinic is down for the length of a redeploy rather than a restore. A release
that drops a column forecloses that.

### Never edit a migration that has been applied anywhere

`InitialCreate` was edited in place, once, deliberately — when the schema
existed on no machine but a developer's. That door is now shut. A migration
recorded in `__EFMigrationsHistory` will not be re-run, so editing it changes
the repository and not the database, and the two drift apart silently.

Add a new migration instead. Always.

### Nullable, or with a default

A `NOT NULL` column added to a table with rows fails unless it has a default.
Add it nullable, backfill in the same migration, then tighten in a later one.

### The one piece of raw SQL in the model

The unique index on `Products` that stops the same medicine being stocked
twice is a *partial* index, and its predicate — `"IsDeleted" = false` — is a
string of PostgreSQL in `AppDbContext`, not something EF Core generates. On
SQL Server that string's quoting depended on a session option that two tools
set differently, and one deployment lost the index without a word.
PostgreSQL has one quoting rule everywhere, so that trap is gone;
`Migrate-Database.ps1` still checks the index exists afterwards, because its
absence would be just as silent.

Every identifier in that predicate, and in any SQL written by hand, is
double-quoted: EF Core keeps the model's PascalCase names, and PostgreSQL
folds anything unquoted to lower case.

---

## 4 · Day to day, on your own machine

```powershell
# 1. describe the change
dotnet ef migrations add AddWhateverItIs --project backend/src/SivayaanHMS.Data

# 2. rebuild - see the warning
dotnet build backend

# 3. apply locally — or just start the API, which migrates on startup in Development
$env:SIVAYAANHMS_CONNECTION = 'Host=localhost;Port=5432;Database=sivayaanhms;Username=sivayaanhms;Password=sivayaanhms-dev'
dotnet ef database update --project backend/src/SivayaanHMS.Data
```

> ⚠ **Always rebuild between adding and applying.** `dotnet ef` loads the
> compiled assembly, not the source. Skipping the build makes it read a binary
> from before your migration existed and report *"No migrations were found"* —
> or worse, *"the model has pending changes"* about a migration you are
> looking at.

Then regenerate the committed script, which is what a reviewer reads:

```powershell
.\deploy\New-FullSchemaScript.ps1   # regenerates db/migrate.sql AND db/full-schema.sql
```

### The guard that matters most

```powershell
dotnet ef migrations has-pending-model-changes --project backend/src/SivayaanHMS.Data
```

Non-zero means an entity changed and nobody added a migration. This is the
commonest failure by a distance — everything builds, every test passes, and
the column is simply absent in production. Worth running before every push.

---

## 5 · Backups beyond the release

`Migrate-Database.ps1` takes one before every migration. That covers "the
release broke it". It does not cover "the disk died", "somebody deleted a
patient last Tuesday", or "the machine was stolen" — and the data lives on
one machine in a clinic.

`DEPLOY_CLOUDFLARE.md` lists this as not covered, and it is still the largest
open risk in the deployment. The smallest honest answer:

```powershell
# a nightly full backup, retained for a fortnight, on a different disk
$stamp = Get-Date -Format 'yyyyMMdd'
$env:PGPASSWORD = '<the password from appsettings.Production.json>'
& "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe" -h localhost -U sivayaanhms -d sivayaanhms -Fc --no-owner --no-privileges -f "E:\HMSBackup\sivayaanhms-$stamp.dump"
```

as a Scheduled Task running as a service account, plus something that copies
it off the machine. PostgreSQL has no scheduler of its own, so Task Scheduler
is the tool.

Two things worth insisting on: `pg_restore --list` over every file it writes,
and a restore actually rehearsed on another machine. An untested backup has an
uncomfortable habit of being unreadable exactly once.

---

## 6 · The pieces, and what each is for

| | |
|---|---|
| `deploy/Migrate-Database.ps1` | Back up, migrate, verify. Run before every deployment. |
| `deploy/Deploy-Production.ps1` | Publish the API and run it as a service. Explicitly does not touch the schema. |
| `db/migrate.sql` | Idempotent schema script, regenerated per migration. For a DBA who wants to read the SQL before it runs — not the normal path. |
| `db/full-schema.sql` | The same, wrapped with role and database creation: one `psql -U postgres -f` takes a blank server to a ready database. For a teammate setting up locally without the SDK. Regenerated by `deploy/New-FullSchemaScript.ps1`. |
| `backend/src/SivayaanHMS.Data/Migrations/` | The migrations themselves. Append only. |
| `DesignTimeDbContextFactory` | How `dotnet ef` finds a connection: the `SIVAYAANHMS_CONNECTION` variable, never `appsettings`. |

### If the server ever loses its SDK

`Migrate-Database.ps1` needs `dotnet` and the `dotnet-ef` tool, because the
repository is checked out on the server and `Deploy-Production.ps1` publishes
there. If that stops being true, build a self-contained bundle on a machine
that does have them:

```powershell
dotnet ef migrations bundle --self-contained -r win-x64 --project backend/src/SivayaanHMS.Data -o db/migrate.exe
```

```powershell
.\migrate.exe --connection "Host=localhost;Port=5432;Database=sivayaanhms;Username=sivayaanhms;Password=<the password>"
```

One executable, no SDK, no source. Take the backup by hand first; the bundle
will not.
