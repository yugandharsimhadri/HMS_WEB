# Where this application stores things

Written to answer one question: *what has to change to move the deployment
and its data off `C:`?*

The short version is that **the application code holds no paths at all**, so
there is nothing in it to make configurable. What needs moving lives outside
it, and this is the list.

---

## 1 · The audit

Every place a path could hide was checked. All of these came back empty:

| Looked for | Result |
|---|---|
| Drive-letter literals (`C:\`, `D:/`) in `backend/src`, `frontend/src` | none |
| `Path.Combine`, `GetTempPath`, `Environment.SpecialFolder`, `Directory.Create*` | none |
| `ContentRootPath`, `WebRootPath`, `UseContentRoot`, a `wwwroot` folder | none |
| `File.WriteAllBytes` / `WriteAllText` / `File.Create` at runtime | none |
| File logging (Serilog, `AddFile`, a log directory setting) | none — console only |
| `AttachDbFilename` / `DataDirectory` in a connection string | none |

And the things you might expect to be on disk are not:

| Data | Where it actually lives |
|---|---|
| Clinic logo | `docs.logo.base64` in the `Settings` table — a data URI, not a file |
| Generated PDFs | built in memory, returned as a `FileContentResult`; never written down |
| Excel exports | `ReportExcelBuilder` writes to a `MemoryStream` |
| CSV imports | read from the upload's `OpenReadStream()`, never staged to disk |
| Everything else | PostgreSQL |

That is the whole answer to "list the path configurations": there are none,
because the design put every persistent thing in the database. The only
path-shaped configuration the app has is the **`Database` section** of appsettings, whose
password is in `appsettings.Local.json` — and even that names a host, not a file.

> Two methods do take a path — `CsvFile.Load(string)` and
> `VendorBillParser.Parse(string)` — but nothing calls them. They are entry
> points left from the desktop app's file-based import, superseded by the
> upload endpoint. Dead, not configuration.

### Not to be confused with the desktop app

`docs/DATABASE_DESIGN.md`, `DATABASE_UPGRADES.md` and
`BUILDING_THE_INSTALLER.md` are full of `C:\HMS\DB`, `C:\HMS\Logs`,
`C:\HMS\DBBackup`. Those describe **HMS_WPF**, the desktop application this
one replaces. None of them apply here.

---

## 2 · What actually has to move

### a. The database — the only one that matters

PostgreSQL keeps every database in its **data directory**, one folder per
cluster, chosen at install time:

```
C:\Program Files\PostgreSQL\18\data
```

There are no per-database files to move the way SQL Server's `.mdf`/`.ldf`
pair could be. Two honest ways to put the data on another drive:

**Move the whole cluster.** Stop the service, copy the data directory to the
new drive, point the service at it, start it again:

```powershell
Stop-Service postgresql-x64-18
robocopy "C:\Program Files\PostgreSQL\18\data" E:\HMS\PgData /E /COPYALL
& "C:\Program Files\PostgreSQL\18\bin\pg_ctl.exe" register -N postgresql-x64-18 -D E:\HMS\PgData -S auto   # re-registers the service with the new -D
Start-Service postgresql-x64-18
```

Confirm with `SHOW data_directory;` in psql, then delete the old folder.

**Or a tablespace for this one database**, leaving the rest of the cluster
where it is:

```sql
-- as postgres, with E:\HMS\PgData existing and writable by the service account
CREATE TABLESPACE hms LOCATION 'E:/HMS/PgData';
ALTER DATABASE sivayaanhms SET TABLESPACE hms;
```

`ALTER DATABASE … SET TABLESPACE` needs no other session on the database, so
stop the API first; it moves every table and index in one operation and the
database is back within a minute for a clinic's worth of records.

Either way, **the application's settings do not change**: the `Database`
section names a host, a port and a database, never a file. That is why this
move needs no code change and no redeploy.

### b. The published application folder

Wherever `dotnet publish` output is copied. Nothing in the code refers to its
own location, so this is purely an operational choice — copy it to
`E:\HMS\App`, point the service or the tunnel at the new binary, done.

`appsettings.Local.json` travels with it, in the same folder.

### c. Upload buffering — the one invisible path

ASP.NET Core buffers multipart uploads over 64 KB to a temporary **file**, in
the process's temp directory. The CSV import is the only upload, so this is
small and short-lived, but it is disk traffic on `C:` that nothing in the code
mentions.

Redirect it with an environment variable on the service:

```
ASPNETCORE_TEMP = E:\HMS\Temp
```


### d. Backups — do not exist yet

There is no backup schedule. `docs/GAP_ANALYSIS.md` §4 has carried this as an
open question since the app used SQLite, and `Migrate-Database.ps1` taking a
verified `pg_dump` before every release is the first answer to it, not the
last. Decide the destination at the same time as the data directory;
`E:\HMS\Backup` alongside `E:\HMS\PgData` is the obvious shape, on the
understanding that a backup on the same physical disk as the database
protects against deleting a row and not against losing the disk. The nightly
command is in `POSTGRESQL_SETUP.md` §7.

### e. New databases, so this does not recur

A database created without a tablespace lands in the cluster's data
directory, wherever that is. If the cluster was moved (option one above)
that is already the right place; if a tablespace was used instead, make it the
default for the role so nothing has to remember:

```sql
ALTER ROLE sivayaanhms SET default_tablespace = hms;
```

---

## 3 · Before any of this: the drive

**There is no `E:` on this machine, and `D:` is a removable slot with no
media in it** — 0 bytes, drive type "Removable". A database on removable
media goes offline the moment it is unplugged. The target needs to be a fixed
disk before the move is worth doing.

PostgreSQL runs as a Windows service that starts with the machine, so the
other concern the SQL Server era had — a per-user LocalDB instance that
stopped when nobody was logged in — does not arise. The remaining question is
only where the data directory lives, and that is section 2a.
