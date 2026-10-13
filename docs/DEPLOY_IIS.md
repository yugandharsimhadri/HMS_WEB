# Deploying the API under IIS

For a machine being built as the server from nothing: PostgreSQL, the
database, the API as an IIS site on port 6051, and the Cloudflare tunnel in
front of it. The frontend is on Cloudflare Pages and is not part of this.

Nothing about the public shape changes, so the tunnel config and the Pages
build both stay exactly as they were:

| | |
|---|---|
| API, locally | `http://127.0.0.1:6051` — an IIS site, app pool `SivayaanHMS` |
| API, publicly | `hoapi.sivayaantechnologies.com`, via `cloudflared` on this machine |
| Frontend | `healthone.sivayaantechnologies.com` on Cloudflare Pages |
| Database | PostgreSQL on `localhost:5432`, database `sivayaanhms`, role `healthone_app` |

Everything below runs **on the server, in an elevated PowerShell**, from the
unzipped release folder. Say `cd C:\SivayaanHMS\release` first.

---

## 1 · Prerequisites

Three things, in this order. The second one is the one that is always
forgotten and always costs an hour.

```powershell
# IIS itself, with the pieces this needs
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-StaticContent, IIS-DefaultDocument, IIS-HttpErrors, IIS-HttpLogging, IIS-RequestFiltering, IIS-NetFxExtensibility45, IIS-ISAPIExtensions, IIS-ISAPIFilter, IIS-ManagementConsole -All -NoRestart
```

**The ASP.NET Core 10 Hosting Bundle** — not the runtime, the *Hosting
Bundle*. It is what installs `AspNetCoreModuleV2`, the IIS module that
actually starts the application. Without it IIS returns `500.19` or
`500.21` and nothing in the application's own logs explains why, because the
application never ran.

```powershell
winget install --id Microsoft.DotNet.HostingBundle.10
iisreset
```

`iisreset` is required after installing it — IIS does not pick the module up
otherwise. Verify:

```powershell
(Get-WebGlobalModule).Name -match 'AspNetCoreModuleV2'
dotnet --list-runtimes | Select-String 'AspNetCore.App 10'
```

**PostgreSQL 16+** (18 is what this was built against), from
postgresql.org. Take the defaults — port 5432, a service that starts with
the machine — and keep the `postgres` superuser password it asks you to
choose. It is used once, in the next step, and never by the application.

---

## 2 · The database, the role, and every privilege on it

One command creates all three — the `healthone_app` login, the
`sivayaanhms` database it owns, and all 49 tables:

```powershell
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -v app_user='healthone_app' -v app_password='<the Database:Password from api\appsettings.Production.json>' -f sql\full-schema.sql
```

Take `app_password` from `Database:Password` in the release's
`api\appsettings.Production.json` — the role's password and the one the API
signs in with must be the same string, and that file is where it already
lives. Enter the `postgres` superuser password at the prompt. Adjust `18` if
another major version is installed.

It ends with the two lines that say it worked:

```
Applied migrations:
 20260920032241_InitialCreate
Tables, and who owns them:
 49 | healthone_app
```

The role **owns** the database and the schema, so it has every privilege by
construction; the script also grants them explicitly, and sets default
privileges so a table a future migration adds is covered too. It is
deliberately **not** given `CREATEDB` or superuser: the application creates
tables through migrations, never databases.

Re-running it is safe — it keeps an existing role and database and skips
migrations already applied.

> Nothing creates the tables for you later. The API does not migrate at
> startup outside Development, on purpose, so a restart can never alter the
> schema by surprise. This script is that step; later releases use
> `deploy\Migrate-Database.ps1`, which backs up first.

---

## 3 · Put the application on disk

```powershell
New-Item -ItemType Directory -Force C:\SivayaanHMS\api | Out-Null
Copy-Item api\* C:\SivayaanHMS\api -Recurse -Force
```

> **The site's physical path must be this folder — the one holding
> `web.config` — and not the unzipped release folder above it.** Point IIS at
> the release root and the application never starts: there is no `web.config`
> there, so `AspNetCoreModuleV2` reports
> `Could not load configuration. Exception message: Attribute 'processPath' is required.`
> in the Application event log and every request gets a `500.30`. This is
> the single most likely way to lose half an hour here; it cost us that on
> the first real deployment.
>
> Keep the deployment tooling out of the web root for the same reason —
> `migrate.exe`, `sql\`, `deploy\` and the `.md` files belong somewhere like
> `C:\SivayaanHMS\tools`, not beside the binaries.

`web.config` is already in there and already says what it needs to:
in-process hosting, and `ASPNETCORE_ENVIRONMENT=Production` as an
environment variable of the site rather than of the machine.

---

## 4 · Settings

**Already done, if the zip came with a filled-in
`api\appsettings.Production.json`** — step 3 copied it across with it. That
file carries all three secrets, which is why the zip should be treated as
one. Check it reads what you expect and move on:

```powershell
Get-Content C:\SivayaanHMS\api\appsettings.Production.json
```

| Key | Should say |
|---|---|
| `Database:Username` / `Password` | `healthone_app`, and the password from step 2 — these two must match or the API will not start |
| `Jwt:Key` | 64 characters of base64. Changing it signs every live session out |
| `PlatformAdmin:Password` | `EnterpriseAdmin`'s password. The API **refuses to start** outside Development without one |
| `Cors:AllowedOrigins` | both frontend origins, lower case, no trailing slash |

If there is no such file, copy the template and fill in those three secrets
by hand:

```powershell
Copy-Item appsettings.Production.json.template C:\SivayaanHMS\api\appsettings.Production.json
notepad C:\SivayaanHMS\api\appsettings.Production.json
```

A fresh JWT key, if one is needed:
`[Convert]::ToBase64String((1..48 | % { Get-Random -Max 256 }))`

Either way, `Host`/`Port`/`Name` (`localhost:5432`, `sivayaanhms`) need no
change.

The `Kestrel` section is **ignored under IIS** — in-process hosting takes the
port from the site binding in the next step. It is left in the file for the
case where the API is ever run as a Windows service instead.

---

## 5 · The IIS site

```powershell
Import-Module WebAdministration

# No .NET CLR: the worker process only hosts AspNetCoreModuleV2, which starts
# the app's own executable. "No Managed Code" is correct and not a downgrade.
New-WebAppPool -Name SivayaanHMS
Set-ItemProperty IIS:\AppPools\SivayaanHMS -Name managedRuntimeVersion -Value ''

# Never idle out, never recycle on a timer. A clinic that leaves the screen
# open over lunch should not come back to a cold start, and a recycle in the
# middle of a prescription is a request that fails for no reason the person
# at the desk can understand.
Set-ItemProperty IIS:\AppPools\SivayaanHMS -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty IIS:\AppPools\SivayaanHMS -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
Set-ItemProperty IIS:\AppPools\SivayaanHMS -Name startMode -Value AlwaysRunning

# 127.0.0.1 and not *: cloudflared runs on this machine and connects over
# loopback, so binding every interface would publish the API to the clinic
# wifi, where anyone could skip the tunnel entirely.
New-Website -Name SivayaanHMS -ApplicationPool SivayaanHMS -PhysicalPath C:\SivayaanHMS\api -IPAddress 127.0.0.1 -Port 6051
```

The app pool identity (`IIS AppPool\SivayaanHMS`) needs to read the folder,
and to write the `logs` folder if stdout logging is ever turned on:

```powershell
icacls C:\SivayaanHMS\api /grant "IIS AppPool\SivayaanHMS:(OI)(CI)(RX)" /T | Out-Null
New-Item -ItemType Directory -Force C:\SivayaanHMS\api\logs | Out-Null
icacls C:\SivayaanHMS\api\logs /grant "IIS AppPool\SivayaanHMS:(OI)(CI)(M)" /T | Out-Null
```

`Default Web Site` listens on port 80 and is not wanted here; stop it so it
cannot answer anything by accident:

```powershell
Stop-Website 'Default Web Site'
Set-ItemProperty 'IIS:\Sites\Default Web Site' -Name serverAutoStart -Value $false
```

---

## 6 · Does it answer

```powershell
curl.exe -s -o NUL -w "%{http_code}`n" http://127.0.0.1:6051/api/settings/general
```

**`401` is success** — the API is listening and refusing an unauthenticated
call. `200` would mean clinic settings are readable by anyone.

If it is `500.30` or `502`, the application failed to start, and IIS will not
tell you why. Run the executable directly, which uses Kestrel and prints the
real error:

```powershell
cd C:\SivayaanHMS\api
$env:ASPNETCORE_ENVIRONMENT = 'Production'
.\SivayaanHMS.Api.exe
```

| It says | It means |
|---|---|
| `Database:Password is not configured` | step 4 was skipped, or the file is not in `C:\SivayaanHMS\api` |
| `PlatformAdmin:Password is not configured` | the same, for that key — the API will not start without it |
| `Jwt:Key is still the development placeholder` | the key was not replaced |
| `password authentication failed for user "healthone_app"` | the password in the file is not the one from step 2 |
| `Connection refused` / `No such host` | PostgreSQL is not running, or not on 5432 |
| `relation "Tenants" does not exist` | step 2's schema half did not run |
| `500.19` / `500.21` from IIS, nothing in the console | the Hosting Bundle is missing, or `iisreset` was not run after installing it |

Stop it with Ctrl-C once it says `Now listening on`. For a failure that only
happens under IIS and not in the console, turn stdout logging on in
`web.config` (`stdoutLogEnabled="true"`), reproduce it, read
`C:\SivayaanHMS\api\logs\stdout_*.log`, and turn it back off — those files
are never rotated.

Two things about that console run:

- **It will try to bind 6051 and fail**, because the `Kestrel` section of
  `appsettings.Production.json` overrides `ASPNETCORE_URLS` — Kestrel says so
  in its first log line, then dies with
  `SocketException (10013): access to a socket in a way forbidden`, which is
  http.sys already owning the port for IIS. Override the *configuration key*
  instead and it runs happily beside the live site:
  ```powershell
  $env:Kestrel__Endpoints__Http__Url = 'http://127.0.0.1:6099'
  ```
- **`curl http://127.0.0.1:6051` answers `400 Bad Request - Invalid
  Hostname`** from `Microsoft-HTTPAPI/2.0` whenever the site binding carries
  a host name. That is http.sys refusing before IIS routes anything, not an
  application error. Either send the header the binding expects —
  `curl.exe -H "Host: hoapi.sivayaantechnologies.com" …` — or add a second
  binding on `127.0.0.1:6051` with the host name left blank, which is what
  makes plain local health checks work.

---

## 7 · The tunnel

If `cloudflared` is already installed and configured on this machine with
`hoapi.sivayaantechnologies.com → http://localhost:6051`, there is nothing
to do: start it and it finds the new site.

```powershell
Start-Service cloudflared
```

From a fresh machine, `docs/DEPLOY_CLOUDFLARE.md` §2 has the whole sequence
(`cloudflared tunnel login`, `create`, the config file that must go in
`C:\Windows\System32\config\systemprofile\.cloudflared\`, `route dns`,
`service install`). `cloudflared\config.yml` in this release folder is the
config to copy — the only thing to fill in is the tunnel id.

---

## 8 · End to end

From anywhere that is not this machine — a phone on mobile data is the
honest test, since nothing local can answer it:

| | Expect |
|---|---|
| `https://hoapi.sivayaantechnologies.com/api/settings/general` | `401` |
| `https://healthone.sivayaantechnologies.com` | the sign-in page |

`530` or `1033` from `hoapi` means cloudflared is not connected; `502` means
the tunnel is up and IIS is not answering on 6051.

Then **Register** on the sign-in page creates the first clinic — its admin
user, numbering counters, diagnostic tests, vaccine schedule, lab analytes
and dental procedures are all seeded at that moment. Sign in as
`<username>@<cliniccode>`, and check `EnterpriseAdmin` can sign in with the
step-4 password and see the clinic listed.

---

## 8b · If the database came from somewhere else

Step 2 creates a database from nothing. If instead you restored or copied one
— a `pg_dump` from the old server, or a copy made as the `postgres`
superuser — then **the tables exist but belong to `postgres`, and the
application role cannot read them**. The symptoms are confusing, because the
role connects perfectly and then fails on the first query:

```
Npgsql.PostgresException 42501: permission denied for table "Tenants"
```

What it looks like from psql, as the application role:

```sql
SELECT tableowner, count(*) FROM pg_tables WHERE schemaname='public' GROUP BY tableowner;
--  postgres | 49
SELECT has_table_privilege('healthone_app', '"Tenants"', 'SELECT');
--  f
```

Hand the whole database over, as `postgres`. First, from any database:

```sql
ALTER DATABASE sivayaanhms OWNER TO healthone_app;
```

That also settles schema `public`, which PostgreSQL 15+ owns via
`pg_database_owner` — a role that resolves to whoever owns the database. Then,
in a Query Tool **connected to `sivayaanhms`** (check with
`SELECT current_database();` — getting this wrong applies the rest to the
`postgres` database instead, silently):

```sql
DO $do$
DECLARE t text;
BEGIN
  FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public'
  LOOP
    EXECUTE format('ALTER TABLE public.%I OWNER TO healthone_app', t);
  END LOOP;
END
$do$;
```

> **`REASSIGN OWNED BY postgres TO healthone_app` does not work here**, which
> is the obvious thing to reach for. PostgreSQL refuses it outright —
> `2BP01: cannot reassign ownership of objects owned by role postgres because
> they are required by the database system` — because that role also owns
> pinned system objects. It is the right tool only for a non-superuser source
> role. The loop above is the way.
>
> Keep the `$do$` markers if you paste this into pgAdmin. Stripped, the bare
> `BEGIN` reads as *start a transaction* and everything after it misbehaves.
> pgAdmin's Query Tool also understands no backslash commands at all, so
> `\connect` does nothing there — switch databases by opening a Query Tool on
> the database itself.

Ownership, not merely `GRANT SELECT, INSERT, …`: grants get the app running
today, but the next release's migration runs `ALTER TABLE`, which only an
owner may do.

Finally, check the schema version matches the build you deployed:

```sql
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1;
```

A copied database can be behind the binaries — and a build whose migration is
missing fails on every request that touches the changed table, with
`column "…" does not exist`. Apply what is missing with `migrate.exe` (§9),
which works unaided once the role owns the tables.

---

## 9 · Afterwards

- **Updating the API**: stop the site, copy the new `api\*` over the folder,
  start it. `appsettings.Production.json` is not in the publish output, so it
  survives — but copy it aside first anyway.
  ```powershell
  Stop-WebSite SivayaanHMS
  Copy-Item api\* C:\SivayaanHMS\api -Recurse -Force
  Start-WebSite SivayaanHMS
  ```
- **A release with a migration** runs `deploy\Migrate-Database.ps1` between
  those two steps — it backs up with `pg_dump` and verifies the backup before
  touching the schema. `docs\DATABASE_RELEASES.md` has the reasoning.
- **Backups** are still nobody's job. `docs\POSTGRESQL_SETUP.md` §7 has the
  nightly `pg_dump` one-liner; a Scheduled Task running it to a second disk
  is the smallest honest answer.
