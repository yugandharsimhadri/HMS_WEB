# The first deployment

Frontend on **Cloudflare Pages**, backend and PostgreSQL on **your own
machine**. This is the checklist for the first time, when nothing exists yet.
`docs/POSTGRESQL_SETUP.md` is the reference for the database itself and every
setting the application reads about it; `docs/DATABASE_RELEASES.md` covers
the routine upgrades that follow.

---

## The short answer on the schema

**Nothing creates it for you in production.** The application applies
migrations at startup only in Development (or when `Database:MigrateOnStartup`
says so explicitly) — see `Program.cs` — and it must stay that way. Two
instances starting together would otherwise race each other to alter the same
tables.

So the database is a **deployment step you run once, before the new build
serves traffic**. `deploy\Migrate-Database.ps1` is that step for every release
after the first; on the very first one, with no database to back up and no
`__EFMigrationsHistory` to check, it refuses to run — deliberately — and one of
the two commands below is used instead. Both are safe against a database at
any version, including one that is already current.

### Either: the script a DBA can read first

```bash
dotnet ef migrations script --idempotent --project backend/src/SivayaanHMS.Data -o db/migrate.sql
```

```powershell
$env:PGPASSWORD = '<the sivayaanhms password>'
psql -h localhost -U sivayaanhms -d sivayaanhms -v ON_ERROR_STOP=1 -f db/migrate.sql
```

> `-v ON_ERROR_STOP=1` makes psql stop on the first error rather than carry
> on and report success. The script itself runs every migration inside one
> transaction, so a failure part-way leaves nothing behind.

### Or: a bundle, with no SDK on the target

```bash
dotnet ef migrations bundle --self-contained -r win-x64 --project backend/src/SivayaanHMS.Data -o db/migrate.exe
```

```
migrate.exe --connection "Host=localhost;Port=5432;Database=sivayaanhms;Username=sivayaanhms;Password=<the password>"
```

The bundle is the better choice unless somebody wants to read the SQL first.

---

## What has to exist before either of those runs

The migration creates **tables**, not the server, the role or the database.
`POSTGRESQL_SETUP.md` §1–2 is the whole of that: install PostgreSQL, then
once, as the superuser,

```powershell
psql -U postgres -v app_password='<choose a password>' -f deploy\sql\01-create-database.sql
```

which creates the role `sivayaanhms` and the database it owns. The
application never touches anything else on the server, and the superuser
password is never written anywhere the application can read.

There is no seed step. The first clinic registers itself through the
Register screen, and that creates its own tenant, admin user and numbering
counters.

---

## The split deployment: what it needs beyond the database

A page served from Cloudflare talking to a backend at home is a different
shape from everything that has run so far. Five things need saying out loud.

### 1 · The browser will not call an http:// backend

The Pages site is served over HTTPS. A page on HTTPS may not make requests to
`http://`; the browser blocks it as mixed content, and no CORS setting
changes that. The backend needs a real HTTPS origin.

**Cloudflare Tunnel is the natural fit** — it gives the machine a public
hostname with a valid certificate, and needs no port forwarding and no static
IP, which is what makes a home or clinic connection workable at all.

### 2 · CORS has to name the Pages origin

`appsettings.json` currently allows `http://localhost:5173` and nothing else.
That is a deliberate allow-list rather than `*`: a bearer-token API allowing
any origin would let any page on the internet read a signed-in clinic's data
through a visitor's browser.

```json
"Cors": { "AllowedOrigins": ["https://your-app.pages.dev"] }
```

Include the custom domain too if you add one, and remember Pages gives every
preview branch its own hostname — those will not be allowed unless listed.

### 3 · `VITE_API_URL` is baked in at build time

It is substituted into the bundle by Vite, so it comes from the **Cloudflare
Pages build environment**, not from a file on a server. Set it in the Pages
project settings before the first build:

```
VITE_API_URL = https://api.your-clinic.example
```

Get this wrong and the build still succeeds. `client.ts` throws on load when
it is missing, so the failure is one clear message rather than every screen
failing separately — but a *wrong* value looks fine until the first request.

### 4 · Replace the JWT signing key

`appsettings.json` ships a placeholder containing
`REPLACE-BEFORE-PRODUCTION`. `Program.cs` refuses to start outside
Development if it is still there, so this cannot be forgotten silently — but
it will stop the first production start until you set it.

Put the real key in `appsettings.Production.json` beside the published API,
alongside the database password — the same file, the same two secrets.
Anyone holding that key can mint a token for any clinic.

### 5 · `ASPNETCORE_ENVIRONMENT` must not be Development

It is what gates the startup migration, the OpenAPI endpoint and the
placeholder-key check. On the server:

```
ASPNETCORE_ENVIRONMENT = Production
```

---

## Order of operations

1. Install PostgreSQL; create the role and database (`POSTGRESQL_SETUP.md` §1–2).
2. Run the migration — the script with `-v ON_ERROR_STOP=1`, or the bundle.
3. Copy `appsettings.Production.json.template` to `appsettings.Production.json`
   beside the published API; fill in the `Database` password and the JWT key.
4. Set `ASPNETCORE_ENVIRONMENT=Production`, add the Pages origin to CORS.
5. Start the backend (`deploy\Deploy-Production.ps1`); expose it over a tunnel
   with a real certificate.
6. Set `VITE_API_URL` in Cloudflare Pages, then deploy the frontend.
7. Register the first clinic through the Register screen.

Check `__EFMigrationsHistory` has a row per migration and that
`IX_Products_TenantId_SearchKey` exists — the duplicate-medicine guard, and
the one index whose absence is silent.

```sql
SELECT "MigrationId" FROM "__EFMigrationsHistory";
SELECT indexname, indexdef FROM pg_indexes WHERE indexname = 'IX_Products_TenantId_SearchKey';
```

---

## Known, and not fixed

`IX_VendorProductCodes_TenantId_VendorProfile_Code` indexes two unbounded
`text` columns. PostgreSQL's B-tree limit is about 2700 bytes per index
entry, so a vendor profile and product code long enough together to exceed
it would fail the *insert*, not the index. Real vendor codes are nowhere near
that, so this has never bitten. The fix is to give both columns a realistic
maximum, which is a decision about what those values are allowed to be
rather than a technical one — worth making before there is data to migrate.
