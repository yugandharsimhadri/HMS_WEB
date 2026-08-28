# Deploying: Pages in front, the API at home

The shape:

```
browser ──https──> healthone.sivayaantechnologies.com   (Cloudflare Pages, static)
   │
   └────https──> hoapi.sivayaantechnologies.com          (Cloudflare edge)
                        │
                   tunnel (outbound only, no open ports)
                        │
                 cloudflared on the clinic machine
                        │
                 http://localhost:6051                   (Kestrel)
                        │
                 .\SQLEXPRESS  →  HMSLite
```

`FIRST_DEPLOYMENT.md` covers the database and the reasoning common to any
split deployment. This is the runbook for **these two hostnames and this
port**, and it only covers what that adds.

Hostnames are lower-cased throughout. DNS is case-insensitive, so
`HOAPi.sivayaantechnologies.com` resolves the same — but the **CORS
allow-list is not**, and an origin string that differs by a capital letter
will not match.

---

## Why a tunnel rather than a forwarded port

The tunnel makes an **outbound** connection to Cloudflare and traffic comes
back down it. That means no inbound firewall rule, no port forwarding on the
clinic router, and no static IP — the three things that usually make "host it
at the clinic" fail. It also means the API is never directly reachable from
the internet: the only route in is Cloudflare's edge.

The API therefore listens on `localhost:6051`, not `0.0.0.0:6051`. Binding
wider would put it on the clinic wifi with no authentication in front of it,
and gain nothing — cloudflared is on the same machine.

---

## 1 · The machine that runs the API

Everything in `FIRST_DEPLOYMENT.md` §"What has to exist" applies first:
SQL Server Express installed, `HMSLite` created, the login granted, and the
migration run. The API will not start without it.

The published build is **framework-dependent**, so the machine needs the
ASP.NET Core 10 runtime (the Hosting Bundle will do). Without it the service
installs happily and then fails to start with a missing-framework message
that reads like an application fault.

```bash
dotnet --list-runtimes
```

Publish targeted at Windows, excluding the developer's secrets (the `.csproj`
already handles the second part - verify rather than assume):

```bash
dotnet publish backend/src/SivayaanHMS.Api -c Release -r win-x64 --self-contained false -o C:\SivayaanHMS\api
```

`-r win-x64` is not cosmetic. QuestPDF's Skia binaries ship for every runtime
identifier, so a portable publish carries 109 MB of Linux and macOS native
libraries onto a Windows clinic machine - **187 MB against 55 MB** for the
same application. It also flattens the native libraries to the root rather
than `runtimes/<rid>/native`, so check `QuestPdfSkia.dll` is beside the exe:
if it goes missing the API still starts and every screen works, and only
printing fails, the first time somebody tries it.

```bash
ls C:\SivayaanHMS\api\appsettings*.json
```

You should see `appsettings.json` and `appsettings.Development.json` only.
If `appsettings.Local.json` is there, stop — it carries the SQL password and
the platform-support credential, and it would also override the production
connection string at runtime.

Then, on the server only:

```bash
cp backend/src/SivayaanHMS.Api/appsettings.Production.json.template C:\SivayaanHMS\api\appsettings.Production.json
```

Fill in the connection password, the JWT key and the platform-admin password.
The template already carries port **6051** and both frontend origins.

Generate a signing key that is actually random — not a passphrase somebody
chose:

```bash
node -e "console.log(require('crypto').randomBytes(48).toString('base64'))"
```

Run it as a Windows service so it survives a reboot and a logout:

```bash
sc.exe create SivayaanHMSApi binPath= "C:\SivayaanHMS\api\SivayaanHMS.Api.exe" start= auto DisplayName= "Sivayaan HMS API"
```

```bash
sc.exe config SivayaanHMSApi depend= "MSSQL$SQLEXPRESS"
```

The dependency matters: without it the API starts first after a reboot,
fails to reach a database that is still starting, and stays down.

`ASPNETCORE_ENVIRONMENT=Production` must be set for the service — it gates
the placeholder-key check and keeps the startup migration off.

```bash
sc.exe start SivayaanHMSApi
```

Confirm before going further:

```bash
curl -sS -o NUL -w "%{http_code}\n" http://localhost:6051/api/settings/general
```

`401` is the right answer here — the API is up and refusing an unauthenticated
call. `000` means it is not listening; check the Windows event log.

---

## 2 · The tunnel

```bash
winget install --id Cloudflare.cloudflared
```

```bash
cloudflared tunnel login
```

That opens a browser to pick the `sivayaantechnologies.com` zone. Then:

```bash
cloudflared tunnel create sivayaan-hms-api
```

It prints a **tunnel id** and writes a credentials JSON beside it. Copy
`deploy/cloudflared/config.yml` from this repo to the service's config
directory and put that id in both places it appears:

```
C:\Windows\System32\config\systemprofile\.cloudflared\config.yml
```

That path is not a typo and is the usual half-hour lost here: the Windows
service runs as LocalSystem and reads config from *its* profile, not from
`%USERPROFILE%\.cloudflared` where `tunnel login` puts things. Move the
credentials JSON there too.

Point DNS at the tunnel — this creates the proxied CNAME for you, so there is
no record to add by hand:

```bash
cloudflared tunnel route dns sivayaan-hms-api hoapi.sivayaantechnologies.com
```

Install and start:

```bash
cloudflared service install
```

```bash
cloudflared tunnel run sivayaan-hms-api
```

Run it in the foreground once, first — it prints its errors plainly, and the
service swallows them. Once a request works, `net start cloudflared`.

Verify from **off** the machine, ideally on mobile data, so nothing is being
answered by a local cache:

```bash
curl -sS -o /dev/null -w "%{http_code} %{remote_ip}\n" https://hoapi.sivayaantechnologies.com/api/settings/general
```

`401` again is success. `530` or `1033` means the tunnel is not connected;
`502` means the tunnel is up but nothing is listening on 6051.

---

## 3 · The frontend on Pages

Create the Pages project from the repo with:

| | |
|---|---|
| Build command | `npm run build` |
| Output directory | `dist` |
| Root directory | `frontend` |
| Node version | 20 or later |

`VITE_API_URL` is already committed in `frontend/.env.production`, so the
build is correct without touching the dashboard. Set the same variable in
Pages only to override it for a staging build.

Two things about this that bite:

- **It is baked in at build time.** Changing the API hostname later means a
  rebuild and redeploy, not a settings change.
- **A wrong value still builds.** `client.ts` throws on load when the variable
  is *missing*, so that failure is loud; a value that is merely wrong looks
  perfectly healthy until the first request fails CORS.

Then add the custom domain `healthone.sivayaantechnologies.com` in the Pages
project. Cloudflare issues the certificate.

`frontend/public/_redirects` sends every unmatched path to `index.html` with
a **200**, not a redirect — the router reads the URL to decide what to render,
so the address has to survive. Without that file, `/patients` 404s on refresh
even though it works when clicked.

---

## 4 · Checks that actually prove it works

In order, because each one rules out the layer below:

1. `https://healthone.sivayaantechnologies.com` loads the sign-in page.
2. Refresh on `/patients` still renders — proves `_redirects`.
3. Sign in. A failure here with a **CORS** message in the console means the
   origin in `appsettings.Production.json` does not match exactly: check the
   scheme, and that there is no trailing slash.
4. Open a PDF. This is worth doing separately because it is the one response
   that is neither JSON nor small, and it exercises the tunnel's timeout.
5. Reboot the machine. Both services should come back without a login —
   this is what the service dependency in §1 is for, and the only way to find
   out is to try it.

---

## When something breaks, in the order worth checking

| Symptom | Almost always |
|---|---|
| `530` / `1033` from the API host | cloudflared not running, or running from the wrong config file |
| `502` from the API host | tunnel up, API down — check `MSSQL$SQLEXPRESS` started first |
| CORS error in the browser | origin string mismatch, or the API 307-redirecting the preflight |
| Deep links 404, clicks fine | `_redirects` missing from the build output |
| Works signed out, 401 signed in | JWT key differs from the one the token was minted with |
| Everything fine until ~100s | Cloudflare's edge timeout, not the app — the report needs paging |

The CORS row is the one that misleads. A redirected preflight reaches the
browser as an opaque CORS failure that names no redirect, which is why the
API trusts `X-Forwarded-Proto` from loopback and does not redirect behind the
tunnel — see the note above `UseForwardedHeaders` in `Program.cs`.

---

## Not covered here

- **Backups.** Nothing in this setup backs up `HMSLite`, and Cloudflare backs
  up nothing — the data lives on one machine in the clinic.
  `docs/GAP_ANALYSIS.md` §4 raises this as an open commitment and it is still
  open. A scheduled SQL Server backup to a second disk is the smallest
  honest answer.
- **Certificate on the origin.** Not needed while cloudflared is on the same
  machine and the hop is loopback. It becomes needed the moment the API moves
  to a different host from the tunnel.
- **More than one API instance.** The startup-migration guard assumes one.
