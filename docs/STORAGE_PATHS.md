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
| Everything else | SQL Server |

That is the whole answer to "list the path configurations": there are none,
because the design put every persistent thing in the database. The only
path-shaped configuration the app has is the **connection string**, which is
already in `appsettings.Local.json`.

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

### a. The database files — the only one that matters

They are currently at:

```
C:\Users\yugan\HMSLite.mdf        72 MB
C:\Users\yugan\HMSLite_log.ldf     8 MB
```

Inside a **user profile**, which is LocalDB's default and a poor place for a
clinic's records: it is tied to one Windows account, and it is the folder most
likely to be swept up by a profile reset or a backup tool that thinks it knows
what a profile contains.

Four steps, in this order. The order is the whole trick — the catalog is told
where the files *will* be before they are moved, and the database must not be
brought back online until they have actually arrived.

```sql
-- 1. tell SQL Server where the files are going
ALTER DATABASE HMSLite MODIFY FILE (NAME = 'HMSLite',     FILENAME = 'E:\HMS\DB\HMSLite.mdf');
ALTER DATABASE HMSLite MODIFY FILE (NAME = 'HMSLite_log', FILENAME = 'E:\HMS\DB\HMSLite_log.ldf');

-- 2. take it offline
ALTER DATABASE HMSLite SET OFFLINE WITH ROLLBACK IMMEDIATE;
```

Then move the two files with the application stopped, **confirm both arrived**,
and only then:

```sql
-- 4. back online
ALTER DATABASE HMSLite SET ONLINE;
```

If you bring it online before the files are in place, SQL Server fails with
*"Unable to open the physical file… operating system error 2"* and leaves the
database in RECOVERY_PENDING. It is recoverable — put the files where the
catalog now says they are and run `SET ONLINE` again — but it is avoidable by
checking first.

Verify:

```sql
SELECT physical_name FROM sys.master_files WHERE database_id = DB_ID('HMSLite');
```

The connection string does **not** change: it names a server and a database,
never a file. That is why this move needs no code change and no redeploy.

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
open question since the app used SQLite, and moving to SQL Server is what
makes it answerable. Decide the destination at the same time as the data
directory; `E:\HMS\Backup` alongside `E:\HMS\DB` is the obvious shape, on the
understanding that a backup on the same physical disk as the database
protects against deleting a row and not against losing the disk.

### e. New databases, so this does not recur

The instance's defaults still point into the user profile:

```
InstanceDefaultDataPath = C:\Users\yugan\
InstanceDefaultLogPath  = C:\Users\yugan\
```

Any database created without an explicit `FILENAME` lands there. Change the
defaults on the instance once the target drive exists, and the next one is in
the right place without anybody remembering.

---

## 3 · Before any of this: the drive, and the instance

Two things are worth settling first, because they matter more than the drive
letter.

**There is no `E:` on this machine, and `D:` is a removable slot with no
media in it** — 0 bytes, drive type "Removable". A database on removable
media goes offline the moment it is unplugged. The target needs to be a fixed
disk before the move is worth doing.

**The application is running on LocalDB, not SQL Express.**
`appsettings.Local.json` points at `(localdb)\MSSQLLocalDB`, while
`appsettings.json` and `SQL_SERVER_SETUP.md` both describe `.\SQLEXPRESS`.
LocalDB is a per-user, on-demand instance: it starts when that user connects
and shuts down when they log out. For a backend that is about to be exposed
through a tunnel and serve a clinic, that is the wrong host — it will stop
when nobody is logged in.

Moving the files to another drive does not fix that. Moving the database onto
a real SQL Server service does, and the two are best done as one operation:
back up from LocalDB, restore onto SQL Express with the files where you want
them, change one line of `appsettings.Local.json`.
