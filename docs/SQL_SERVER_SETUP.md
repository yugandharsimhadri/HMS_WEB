# SQL Server setup, and how upgrades work

The database is **SQL Server**. SQLite is gone — the provider package, the
migrations and the test harness all moved. `docs/SQL_SERVER_MIGRATION.md` is
the reasoning; this is the operating manual.

---

## 1 · What is running now

| | |
|---|---|
| Instance | `.\SQLEXPRESS` — SQL Server 2025 Express, running as a Windows service |
| Database | `HMSLite` |
| Login | `SivayaanHMS` (SQL login, `db_owner` on `HMSLite`) |
| App connects as | **Windows authentication**, until the step in §2 is done |

The connection string in `appsettings.json` uses Windows authentication
deliberately: it carries no password, so the file stays safe to commit.

### Why not the connection string you may have been handed

An SSMS connection string is not an application one. Four settings in the
pasted original were changed, and each for a reason:

| Setting | Was | Now | Why |
|---|---|---|---|
| Server | `(localdb)\MSSQLLocalDB` | `.\SQLEXPRESS` | LocalDB will not start on this machine (`SQL Server process failed to start`), and it is a per-user, on-demand instance — it stops when you log out. Express is a real service and is what a clinic would run. |
| `Pooling` | `False` | default (on) | Disabling the connection pool is fine for one SSMS window and costly for a web app, which opens a connection per request. |
| `Command Timeout` | `0` | default (30s) | `0` means *wait forever*. One runaway query would hang a request thread with nothing to time it out. |
| `Application Name` | `SQL Server Management Studio` | `SivayaanHMS` | This is what shows in `sys.dm_exec_sessions`. Traffic from the app should not be labelled as SSMS when someone is trying to work out what is hammering the server. |

`Encrypt=True;TrustServerCertificate=False` was also dropped for local use: a
default Express instance presents a self-signed certificate, which that pair
of settings is specifically designed to reject. See §5 for what to do on a
real server, where encryption **should** be on.

---

## 2 · The one manual step

**Everything below is already done except this.** It needs Windows
administrator rights, which the setup could not take.

The instance is currently **Windows-authentication only**, so the
`SivayaanHMS` SQL login exists but cannot yet sign in. The registry value has
already been set to enable mixed mode; it takes effect on restart.

Run this **as Administrator**:

```powershell
Restart-Service 'MSSQL$SQLEXPRESS' -Force
```

Confirm it worked — this should print `0`:

```powershell
sqlcmd -S ".\SQLEXPRESS" -E -Q "SELECT SERVERPROPERTY('IsIntegratedSecurityOnly');"
```

Then create `backend/src/SivayaanHMS.Api/appsettings.Local.json` — which is
**git-ignored**, so the password never reaches the repository:

```json
{
  "ConnectionStrings": {
    "Default": "Server=.\\SQLEXPRESS;Database=HMSLite;User ID=SivayaanHMS;Password=<the password>;TrustServerCertificate=True;MultipleActiveResultSets=False;Application Name=SivayaanHMS"
  }
}
```

That file is loaded last and overrides `appsettings.json`, so the app picks up
the SQL login with no code change. Until you do it, the app runs perfectly
well on Windows authentication.

> **Worth considering: don't do this at all.** Windows authentication has no
> password to store, leak, rotate or find in a config file, and it already
> works. The SQL login exists because it was asked for, but for a service
> running on the same machine as the database, integrated security is
> strictly the safer choice.

---

## 3 · How upgrades work from here

The rule: **the application never changes the schema in production.**
Migration is a deployment step that runs once, before the new build serves
traffic. `Program.cs` still migrates at startup, but only under
`IsDevelopment()`, and it must stay that way — two instances starting
together would otherwise race each other.

### Day to day, when you change an entity

```bash
# 1. describe the change
dotnet ef migrations add AddWhateverItIs --project backend/src/SivayaanHMS.Data

# 2. rebuild — see the warning below
dotnet build backend

# 3. apply it locally
$env:SIVAYAANHMS_CONNECTION = 'Server=.\SQLEXPRESS;Database=HMSLite;Trusted_Connection=True;TrustServerCertificate=True'
dotnet ef database update --project backend/src/SivayaanHMS.Data
```

> ⚠ **Always rebuild between adding and applying.** `dotnet ef` loads the
> compiled assembly, not the source. Skipping the build makes it read a
> binary from before your migration existed and report *"No migrations were
> found"* — or worse, *"the model has pending changes"* about a migration you
> are looking at. This bit us during the move itself.

### Deploying an upgrade

Produce a self-contained bundle — one executable, no .NET SDK, no EF tools
and no source needed on the target:

```bash
dotnet ef migrations bundle --self-contained -r win-x64 \
  --project backend/src/SivayaanHMS.Data -o db/migrate.exe
```

On the server:

```
migrate.exe --connection "Server=.\SQLEXPRESS;Database=HMSLite;Trusted_Connection=True;TrustServerCertificate=True"
```

It is safe to run against a database at any version, including one already
current — it applies only what is missing. Run it *before* starting the new
build.

The alternative, when a DBA wants to read the SQL first:

```bash
dotnet ef migrations script --idempotent --project backend/src/SivayaanHMS.Data -o db/migrate.sql
```

`--idempotent` guards every step against `__EFMigrationsHistory`, so the file
is safe to run repeatedly.

> Run it as `sqlcmd -I -b -i db/migrate.sql`. `-I` sets `QUOTED_IDENTIFIER
> ON`, without which the filtered index on `Products` cannot be created;
> `-b` makes sqlcmd stop on the first error instead of continuing and
> exiting zero. The bundle above needs neither — it sets its own options.
> See `docs/FIRST_DEPLOYMENT.md`.

### The two CI guards that keep this honest

Both are cheap and catch different mistakes.

```bash
# 1. An entity changed but nobody added a migration.
#    Non-zero exit = the model has moved without one.
dotnet ef migrations has-pending-model-changes --project backend/src/SivayaanHMS.Data

# 2. The committed script has fallen behind the migrations.
dotnet ef migrations script --idempotent --project backend/src/SivayaanHMS.Data -o db/migrate.generated.sql
git diff --exit-code db/migrate.sql db/migrate.generated.sql
```

The first is the one that matters most. The commonest failure is not a stale
script — it is somebody editing an entity, everything building, every test
passing, and the column simply being absent in production.

---

## 4 · Running the tests

They run against **real SQL Server**, each creating a throwaway database and
dropping it afterwards. About 12 seconds for all 68.

```bash
dotnet test backend
```

Point them elsewhere — CI, another machine — with:

```
SIVAYAANHMS_TEST_SQL = "Server=some\instance;Trusted_Connection=True;TrustServerCertificate=True"
```

They are deliberately **not** on SQLite or the in-memory provider.
`NumberServiceConcurrencyTests` and `PharmacyServiceConcurrencyTests` exist to
prove two callers cannot take the same invoice number or oversell a batch,
and those guarantees are made by provider-specific locking. Run anywhere
else, they would prove something true of a database nobody uses — and in
fact, moving them to SQL Server immediately found three real bugs that had
been invisible. See §6.

---

## 5 · Moving to a real server

What changes when this stops being a local instance:

- **Encryption on.** `Encrypt=True`, and `TrustServerCertificate=False` once
  the server has a certificate a client will actually trust. The pairing that
  fails locally is correct there.
- **Not `db_owner`.** The application does not need to create or drop tables
  at runtime — migrations do that, as a separate step, under a separate
  account. Give the app `db_datareader`, `db_datawriter` and execute rights,
  and keep schema rights for the migration account.
- **A real backup schedule.** This is the point at which the open question in
  `docs/GAP_ANALYSIS.md` §4 can finally be answered: SQL Server has a proper
  backup and restore story where a single SQLite file had none.
- **Turn on retries — but read §1.2 of the migration plan first.**
  `EnableRetryOnFailure()` is deliberately *not* enabled today, because
  switching it on breaks nine transaction sites at runtime rather than at
  compile time. It is a piece of work, not a flag.
- **Express limits**: 10 GB per database, ~1.4 GB RAM, one socket. Ample for
  a clinic. Worth knowing before one instance is proposed for a hundred of
  them, since every tenant shares one database.

---

## 6 · What the move actually found

Recorded because each was invisible under SQLite and none would have been
caught by reading the code.

| Found | Why SQLite hid it |
|---|---|
| `NumberService` read `LastNumber` with `GetInt64`, but the column is `int` | SQLite returns every INTEGER as 64-bit; SQL Server returns Int32 and throws |
| Its batch returned **two result sets**, and the reader landed on the empty one whenever the counter already existed | The old single-statement `ON CONFLICT … RETURNING` had only ever produced one |
| It ran a raw command on a connection with EF's transaction already open, **without enrolling in it** — which SQL Server refuses outright. This broke every numbered document created inside the nine transaction sites | SQLite's provider never enforced the rule |
| `Appointments.RescheduledFromId` was `ON DELETE SET NULL` on a **self-referencing** key, which SQL Server rejects as a possible cycle | SQLite does not check for cascade cycles at all |
| Optimistic-concurrency retry gave up after 3 immediate attempts | SQLite serialised writers, so a retry almost always won. Real parallelism makes several callers collide, then retry in lockstep and collide again — now 10 attempts with jittered backoff |

The last one is worth a follow-up rather than a fix: retrying is optimistic
concurrency working as designed and nothing is ever oversold, but the durable
answer for a hot batch row is a pessimistic update lock during a sale, the
way `NumberService` locks a counter.
