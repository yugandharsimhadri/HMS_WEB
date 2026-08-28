# The first deployment

Frontend on **Cloudflare Pages**, backend and SQL Server on **your own
machine**. This is the checklist for the first time, when nothing exists yet.
`docs/SQL_SERVER_SETUP.md` covers the routine upgrades that follow.

Everything here was run against a genuinely blank database before it was
written down.

---

## The short answer on the schema

**Nothing creates it for you in production.** The application applies
migrations at startup only under `IsDevelopment()` — see the guard in
`Program.cs` — and it must stay that way. Two instances starting together
would otherwise race each other to alter the same tables.

So the database is a **deployment step you run once, before the new build
serves traffic**. You have two ways to run it, and both are safe against a
database at any version, including one that is already current.

### Either: the script a DBA can read first

```bash
dotnet ef migrations script --idempotent --project backend/src/SivayaanHMS.Data -o db/migrate.sql
```

```bash
sqlcmd -S ".\SQLEXPRESS" -d HMSLite -E -C -I -b -i db/migrate.sql
```

> **`-I` is not optional.** It sets `QUOTED_IDENTIFIER ON`. Filtered indexes
> cannot be created without it, and this schema has one — the unique index
> that stops the same medicine being stocked twice. Without `-I` that
> statement fails.
>
> `-b` makes sqlcmd stop on error rather than carry on and report success.
> Use both, every time.
>
> `-C` trusts the server certificate. ODBC Driver 18 encrypts by default
> and validates the chain, and Express presents a self-signed certificate,
> so without it sqlcmd fails on the certificate before reaching the
> database at all.

### Or: a bundle, with no SDK on the target

```bash
dotnet ef migrations bundle --self-contained -r win-x64 --project backend/src/SivayaanHMS.Data -o db/migrate.exe
```

```
migrate.exe --connection "Server=.\SQLEXPRESS;Database=HMSLite;Trusted_Connection=True;TrustServerCertificate=True"
```

The bundle sets its own connection options correctly, so there is no `-I`
equivalent to remember. It is the better choice unless somebody wants to read
the SQL first.

---

## What has to exist before either of those runs

The migration creates **tables**, not the server, the login or the database.

```sql
-- as an administrator, once
CREATE DATABASE HMSLite;
GO
CREATE LOGIN SivayaanHMS WITH PASSWORD = '<the password>';
GO
USE HMSLite;
CREATE USER SivayaanHMS FOR LOGIN SivayaanHMS;
ALTER ROLE db_datareader ADD MEMBER SivayaanHMS;
ALTER ROLE db_datawriter ADD MEMBER SivayaanHMS;
GO
```

Deliberately **not** `db_owner`: the application never changes the schema, so
it does not need rights to. Run the migration under an account that does, and
leave the app without them. If you would rather skip the password entirely,
Windows authentication works and has nothing to leak — see
`SQL_SERVER_SETUP.md` §2.

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

Put the real key in `appsettings.Local.json`, which is git-ignored, alongside
the connection string. Anyone holding that key can mint a token for any
clinic.

### 5 · `ASPNETCORE_ENVIRONMENT` must not be Development

It is what gates the startup migration, the OpenAPI endpoint and the
placeholder-key check. On the server:

```
ASPNETCORE_ENVIRONMENT = Production
```

---

## Order of operations

1. Create the database, login and user (above).
2. Run the migration — script with `-I -b`, or the bundle.
3. Set `appsettings.Local.json`: connection string, JWT key.
4. Set `ASPNETCORE_ENVIRONMENT=Production`, add the Pages origin to CORS.
5. Start the backend; expose it over a tunnel with a real certificate.
6. Set `VITE_API_URL` in Cloudflare Pages, then deploy the frontend.
7. Register the first clinic through the Register screen.

Check `__EFMigrationsHistory` has a row per migration and that
`IX_Products_TenantId_SearchKey` exists — the second is the one that goes
missing quietly if the `-I` above was skipped.

```sql
SELECT MigrationId FROM __EFMigrationsHistory;
SELECT name, filter_definition FROM sys.indexes WHERE name = 'IX_Products_TenantId_SearchKey';
```

---

## Known, and not fixed

`IX_VendorProductCodes_TenantId_VendorProfile_Code` has a maximum key length
of 1816 bytes against SQL Server's 1700-byte limit, because `VendorProfile`
and `Code` are both `nvarchar(450)`. SQL Server creates the index and warns.
Nothing fails until somebody stores a vendor profile and product code that
are long enough together to exceed the limit, at which point the *insert*
fails, not the index.

Real vendor codes are nowhere near that, so this has never bitten. The fix is
to give both columns a realistic maximum, which is a decision about what those
values are allowed to be rather than a technical one — worth making before
there is data to migrate.
