# Database releases

Everything from here is an upgrade of a database that holds a clinic's real
records. `FIRST_DEPLOYMENT.md` covers the install that has now happened once
and will not happen again on this machine.

The rule that everything below follows from:

> **The application never changes the schema in production.** Migrating is a
> release step of its own, run before the new build serves traffic, with a
> verified backup taken first.

`Program.cs` still migrates at startup, but only under `IsDevelopment()`, and
it must stay that way. Two instances starting together would race each other
to alter the same tables.

---

## 1 · The release, in order

```powershell
# 1. see what a release would do, without doing it
.\deploy\Migrate-Database.ps1 -SqlInstance SIVASQLEXPRESS -DryRun

# 2. stop serving, so nothing writes through a half-applied schema
Stop-Service SivayaanHMSApi

# 3. back up and migrate
.\deploy\Migrate-Database.ps1 -SqlInstance SIVASQLEXPRESS

# 4. publish the new build and start again
.\deploy\Deploy-Production.ps1 -SqlInstance SIVASQLEXPRESS
```

Then the frontend, which is independent: pushing to the branch Cloudflare
Pages watches rebuilds and deploys it.

**Migrate before you publish, not after.** The new build may expect a column
the old schema does not have; the old build tolerating a new column is the
easier direction, which is why the order is this way round and not the other.

### What `Migrate-Database.ps1` does

1. Checks the server is reachable and the login works, with a clearer message
   than the API gives.
2. **Backs up. Always** — even when nothing is pending — and runs
   `RESTORE VERIFYONLY` over the result. A backup nobody has read back is a
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

sqlcmd -S ".\SIVASQLEXPRESS" -C -I -b -E -d master -Q "ALTER DATABASE [HMSLite] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [HMSLite] FROM DISK = N'C:\SivayaanHMS\DBBackup\HMSLite-pre-migration-<stamp>.bak' WITH REPLACE; ALTER DATABASE [HMSLite] SET MULTI_USER;"
```

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

### Filtered indexes need `QUOTED_IDENTIFIER ON`

The unique index on `Products` that stops the same medicine being stocked
twice is a filtered index. SQL Server refuses **any write** to that table —
not just index creation — when the option is off, and `sqlcmd` defaults it
off.

`dotnet ef database update` sets its own options, so the scripted path is
immune. Anything you run by hand needs `-I`. `Migrate-Database.ps1` passes it
everywhere, and checks the index still exists afterwards, because its absence
is otherwise completely silent.

---

## 4 · Day to day, on your own machine

```powershell
# 1. describe the change
dotnet ef migrations add AddWhateverItIs --project backend/src/SivayaanHMS.Data

# 2. rebuild - see the warning
dotnet build backend

# 3. apply locally
$env:SIVAYAANHMS_CONNECTION = 'Server=.\SQLEXPRESS;Database=HMSLite;Trusted_Connection=True;TrustServerCertificate=True'
dotnet ef database update --project backend/src/SivayaanHMS.Data
```

> ⚠ **Always rebuild between adding and applying.** `dotnet ef` loads the
> compiled assembly, not the source. Skipping the build makes it read a binary
> from before your migration existed and report *"No migrations were found"* —
> or worse, *"the model has pending changes"* about a migration you are
> looking at.

Then regenerate the committed script, which is what a reviewer reads:

```powershell
dotnet ef migrations script --idempotent --project backend/src/SivayaanHMS.Data -o db/migrate.sql
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
sqlcmd -S ".\SIVASQLEXPRESS" -E -C -I -b -Q "BACKUP DATABASE [HMSLite] TO DISK = N'E:\HMSBackup\HMSLite-$stamp.bak' WITH INIT, CHECKSUM;"
```

as a Scheduled Task running as a service account, plus something that copies
it off the machine. Express has no SQL Agent, so Task Scheduler is the tool.

Two things worth insisting on: `CHECKSUM`, and a restore actually rehearsed on
another machine. An untested backup has an uncomfortable habit of being
unreadable exactly once.

---

## 6 · The pieces, and what each is for

| | |
|---|---|
| `deploy/Migrate-Database.ps1` | Back up, migrate, verify. Run before every deployment. |
| `deploy/Deploy-Production.ps1` | Publish the API and run it as a service. Explicitly does not touch the schema. |
| `db/migrate.sql` | Idempotent full script, regenerated per migration. For a DBA who wants to read the SQL before it runs — not the normal path. |
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
.\migrate.exe --connection "Server=.\SIVASQLEXPRESS;Database=HMSLite;Trusted_Connection=True;TrustServerCertificate=True"
```

One executable, no SDK, no source, and it sets its own SET options — so the
`-I` trap cannot bite. Take the backup by hand first; the bundle will not.
