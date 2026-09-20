# Releasing the PostgreSQL build — step by step

For the machine that already runs the API behind the Cloudflare tunnel, and
already has PostgreSQL running. A **fresh database**: nothing is carried over
from SQL Server, the first clinic registers itself afterwards.

Nothing about the network changes. The same ports, the same hostnames:

| | |
|---|---|
| API | `http://localhost:6051`, as the Windows service `SivayaanHMSApi` |
| Tunnel | `hoapi.sivayaantechnologies.com` → `localhost:6051`, unchanged |
| Frontend | `healthone.sivayaantechnologies.com` on Cloudflare Pages, unchanged |
| PostgreSQL | `localhost:5432`, database `sivayaanhms`, role `sivayaanhms` |

The frontend is not touched by this release — the database provider is
invisible to it — so nothing on Pages needs redeploying. A `frontend\` build
is in the release folder anyway, for a direct upload if that is ever wanted.

---

## 0 · What you are copying

Build the release folder on a development machine (already done for this
release; the command is here for the next one):

```powershell
.\deploy\New-Release.ps1
```

That produces `backend\artifacts\release\` — zip it, or copy it as a folder:

```
release\
  api\                         published API, framework-dependent, win-x64
  frontend\                    Pages build (optional — see above)
  deploy\                      Deploy-Production.ps1, PostgresSettings.ps1, Migrate-Database.ps1
  sql\01-create-database.sql   role + database, run once
  migrate.exe                  schema, self-contained — no SDK needed on the server
  appsettings.Production.json.template
  RELEASE_POSTGRESQL.md        this file
```

Copy it to the server as `C:\SivayaanHMS\release`. Everything below runs
**on the server, in an elevated PowerShell**, from that folder.

```powershell
cd C:\SivayaanHMS\release
```

---

## 1 · Stop the old API

```powershell
Stop-Service SivayaanHMSApi -ErrorAction SilentlyContinue
```

The tunnel can stay up; `hoapi` answers `502` until step 6, which is the
right thing for it to say.

---

## 2 · Create the role and the database

Once. Choose a real password — this is the production one, and it goes into
`appsettings.Production.json` in step 5.

```powershell
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -v app_password='<choose a password>' -f sql\01-create-database.sql
```

Enter the `postgres` superuser password when prompted (the one chosen when
PostgreSQL was installed). Adjust `18` if a different major version is
installed. It ends with:

```
Role sivayaanhms and database sivayaanhms are in place.
```

The superuser is not used again after this line, and the application never
knows its password.

---

## 3 · Create the schema

```powershell
.\migrate.exe --connection "Host=localhost;Port=5432;Database=sivayaanhms;Username=sivayaanhms;Password=<the password from step 2>"
```

One executable, no .NET SDK needed. It applies the one migration this
release carries and records it. Check:

```powershell
$env:PGPASSWORD = '<the password from step 2>'
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h localhost -U sivayaanhms -d sivayaanhms -c 'select "MigrationId" from "__EFMigrationsHistory";'
```

One row, `20260920032241_InitialCreate`. Every release after this one uses
`deploy\Migrate-Database.ps1` instead, which backs up first — it refuses to
run against a database with no history table, which is why this first one is
the bundle.

---

## 4 · Put the new build in place

```powershell
New-Item -ItemType Directory -Force C:\SivayaanHMS\api | Out-Null
Copy-Item C:\SivayaanHMS\api\appsettings.Production.json C:\SivayaanHMS\api\appsettings.Production.SQLSERVER.bak -ErrorAction SilentlyContinue
Remove-Item C:\SivayaanHMS\api\* -Recurse -Force -Exclude appsettings.Production.SQLSERVER.bak
Copy-Item api\* C:\SivayaanHMS\api -Recurse -Force
```

The old settings file is kept as `.bak` for its JWT key and platform-admin
password, which step 5 carries forward. Everything else in the folder is
replaced.

---

## 5 · Settings

```powershell
Copy-Item appsettings.Production.json.template C:\SivayaanHMS\api\appsettings.Production.json
notepad C:\SivayaanHMS\api\appsettings.Production.json
```

Fill in:

| Key | Value |
|---|---|
| `Database:Password` | the password from step 2 |
| `Jwt:Key` | the one from the `.bak` file, so nothing changes; or a fresh 48-byte random string — there are no live sessions to preserve |
| `PlatformAdmin:Password` | the one from the `.bak` file (`EnterpriseAdmin`'s password) |

Leave the rest: `Host`/`Port`/`Name`/`Username` already say
`localhost:5432`, `sivayaanhms`, `sivayaanhms`; `Kestrel` already says
`http://localhost:6051`; `Cors` already lists both frontend origins.

The old file's `ConnectionStrings` block has no equivalent and is simply
gone — the `Database` section replaces it. Do **not** copy it across: the
API ignores it, and a copied SQL Server string sitting in the file misleads
the next person to read it.

---

## 6 · Install / update the service and start it

```powershell
.\deploy\Deploy-Production.ps1 -Root C:\SivayaanHMS\api -SkipPublish
```

`-SkipPublish` because the build is already in place. The script:

- finds the PostgreSQL service (`postgresql-x64-18`) and makes `SivayaanHMSApi`
  depend on it — replacing the old dependency on `MSSQL$SQLEXPRESS` — so the
  API never loses the startup race after a reboot;
- refuses to continue if `appsettings.Production.json` still says
  `REPLACE-ME` anywhere;
- sets `ASPNETCORE_ENVIRONMENT=Production` on the service;
- starts it and polls `http://localhost:6051/api/settings/general` until it
  answers `401` — the healthy answer: listening, and refusing an
  unauthenticated call.

If it stops at the health check, run the API in the foreground to see the
real error:

```powershell
cd C:\SivayaanHMS\api
$env:ASPNETCORE_ENVIRONMENT = 'Production'
.\SivayaanHMS.Api.exe
```

| It says | It means |
|---|---|
| `Database:Password is not configured` | step 5 was skipped, or the file is not in `C:\SivayaanHMS\api` |
| `password authentication failed for user "sivayaanhms"` | the password in the file is not the one from step 2 |
| `Connection refused` | PostgreSQL is not running, or is not on 5432 |
| `relation "Tenants" does not exist` | step 3 was skipped |

---

## 7 · Check it end to end

From the server:

```powershell
curl.exe -s -o NUL -w "%{http_code}`n" http://localhost:6051/api/settings/general
```

`401`. Then from anywhere else — a phone on mobile data is the honest test:

```
https://hoapi.sivayaantechnologies.com/api/settings/general   → 401
https://healthone.sivayaantechnologies.com                    → the sign-in page
```

`530`/`1033` from `hoapi` means cloudflared is not running on the server
(`net start cloudflared`); `502` means the tunnel is up and the API is not.

Then register the first clinic through **Register** on the sign-in page, sign
in as `<username>@<cliniccode>`, and confirm `EnterpriseAdmin` can sign in with
the platform-admin password and see that clinic listed.

---

## 8 · Afterwards

- **SQL Server** is no longer used. Leave it stopped
  (`Stop-Service 'MSSQL$SQLEXPRESS'; Set-Service 'MSSQL$SQLEXPRESS' -StartupType Disabled`)
  until you are sure nothing else on the machine wants it, then uninstall.
  `C:\SivayaanHMS\DB` and `DBBackup` hold the old `HMSLite` files — they are
  not needed and can be deleted once the clinic has been registered afresh.
- **Every later release** is `docs\DATABASE_RELEASES.md`: `Migrate-Database.ps1`
  (backs up, migrates), then `Deploy-Production.ps1`.
- **Backups** are still nobody's job. `docs\POSTGRESQL_SETUP.md` §7 has the
  one-line nightly `pg_dump`.
