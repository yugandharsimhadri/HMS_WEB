# Running the API in Docker, on Linux or Windows

The API in a container, PostgreSQL on its own Linux VM, the frontend still on
Cloudflare Pages. Same image on a Linux server and on a Windows box — one
artifact, built and tested once.

## The decision, and why

**One Linux image, run on both hosts.** Not a Linux image *and* a Windows
image.

A .NET container image is per-operating-system: there is no single image that
runs as both a Linux and a Windows container, and a Docker daemon serves one
kind at a time. A Windows image would also be 2–5 GB against ~250 MB. The
question is therefore only whether anything in this application *needs*
Windows — and nothing does. The codebase was audited for it: no
`System.Drawing`, no registry access, no Windows paths, no
`RuntimeIdentifier` in any `.csproj` (the `-r win-x64` is only in the
deploy scripts), and the sole disk write in the whole application is to a
`MemoryStream`. **The API container is stateless: no volumes, nothing to back
up in it.**

Docker Desktop on Windows runs Linux containers through WSL2, so the same
image and the same `compose.yaml` serve the Windows box today and a Linux VM
tomorrow.

| | |
|---|---|
| API | `sivayaanhms-api`, Linux container, published on `127.0.0.1:6051` |
| PostgreSQL | native on its own Linux VM, reached over the network |
| Frontend | Cloudflare Pages, unchanged — so **CORS still applies** |
| Tunnel | `hoapi.sivayaantechnologies.com` → `localhost:6051`, unchanged |

---

## 1 · Four things that bite, specific to this application

Each of these is a silent failure, not an error, which is why they are first.

### Timezone — the one that corrupts data rather than breaking it

`SystemClock.Now` returns `DateTime.Now`, the *server's* local time, and the
schema stores clinic-local wall-clock with no offset:

```csharp
// TODO(SAAS_MIGRATION.md #4): this is the server's local time, not the tenant's.
public DateTime Now => DateTime.Now;
```

A container defaults to **UTC**. Every visit time, token time, fee-paid date
and day-book boundary would shift by 5½ hours and nothing would throw — the
day book would simply start rolling over at 05:30. `TZ=Asia/Kolkata` is set
in both the image and `compose.yaml`; change it in `.env` for a clinic
elsewhere, and never leave it unset.

### `localhost` means the container

`appsettings.Production.json` on the IIS box says
`Kestrel:Endpoints:Http:Url = http://localhost:6051`. Inside a container that
binds the *container's* loopback, so a published port reaches nothing. The
image sets `Kestrel__Endpoints__Http__Url=http://0.0.0.0:8080` — as the
configuration key, not `ASPNETCORE_URLS`, because an appsettings `Kestrel`
section overrides that one (`docs/DEPLOY_IIS.md` §6 has the hour this cost).

The same applies to the database: `Host=localhost` in a connection string
inside a container is the container, not the VM. Use the VM's address, or
`host.docker.internal` when PostgreSQL is on the machine running Docker.

### Fonts — every PDF asks for a Windows font

`DocumentStyle.BodyFont` falls back to **`"Segoe UI"`** (five call sites
across three files) when a clinic has not set a print font. Segoe UI exists
only on Windows. The image therefore installs `libfontconfig1` — Skia
resolves fonts through fontconfig on Linux and throws without it — plus
`fonts-dejavu-core`, and `deploy/docker/fonts-local.conf` aliases Segoe UI,
Calibri and Arial to DejaVu Sans so a receipt prints.

That is a safety net: the documents will render, in a slightly different
typeface from the Windows box. **For output identical on both, set Settings →
Document branding → print font to `Lato`.** QuestPDF bundles Lato and
registers it in-process, so it resolves the same on every operating system
and needs no system font at all.

### Proxy trust

`Program.cs` trusts `X-Forwarded-Proto` only from loopback, which is right
for the IIS and Windows-service deployments where cloudflared is on the same
machine. In a container the tunnel arrives from the Docker bridge instead, the
header is ignored, the app concludes every request came over `http`, and
`UseHttpsRedirection` begins redirecting — which reaches a browser as an
opaque CORS failure naming no redirect. `ForwardedHeaders__KnownNetworks`
(set by `compose.yaml` to `172.16.0.0/12`) is what prevents that. Confirm your
bridge subnet with:

```bash
docker network inspect bridge --format '{{(index .IPAM.Config 0).Subnet}}'
```

---

## 2 · PostgreSQL on the Linux VM

The API is no longer on the same machine as the database, so three things that
were free before have to be configured. As root on the VM:

**Listen on the network.** The default is localhost only.

```bash
sudo -u postgres psql -c "ALTER SYSTEM SET listen_addresses = '*';"
sudo systemctl restart postgresql
```

**Let the Docker host in.** Add one line to `pg_hba.conf` — the narrowest
subnet that covers the machine running Docker, never `0.0.0.0/0`:

```
# /etc/postgresql/18/main/pg_hba.conf
host    sivayaanhms    healthone_app    192.168.1.0/24    scram-sha-256
```

```bash
sudo -u postgres psql -c "SELECT pg_reload_conf();"
```

**Open the firewall**, again scoped to the one source:

```bash
sudo ufw allow from 192.168.1.0/24 to any port 5432 proto tcp
```

Then create the role and schema exactly as on any other host — one command,
from the repository:

```bash
psql -U postgres -v app_user='healthone_app' -v app_password='…' -f db/full-schema.sql
```

`docs/POSTGRESQL_SETUP.md` is the reference; `docs/DEPLOY_IIS.md` §8b covers
the case where you restore a copied database instead and the tables end up
owned by `postgres`.

**On SSL:** `SSL Mode=Prefer` is acceptable only on a private virtual network
between these two machines. If the traffic crosses anything else, PostgreSQL
needs `ssl = on` with a certificate and the connection string needs
`SSL Mode=Require` — unencrypted, a clinic's entire patient register is
readable by anything on the path.

---

## 3 · Build and run

On either host, from the repository root:

```bash
cp deploy/docker/.env.example deploy/docker/.env
```

Fill in `.env`: the connection string (the VM's address), `Jwt__Key`,
`PlatformAdmin__Password`, and the two Pages origins. It is git-ignored.

```bash
docker compose build
```

```bash
docker compose run --rm migrate
```

That applies pending migrations and exits — a release step of its own, using
the same image the API runs, so the schema and the binaries cannot disagree
about which migrations exist. It prints what it applied, says
`Already current` when there is nothing to do, and **exits non-zero on
failure**, so a deploy script stops rather than starting a server against a
schema it does not match.

```bash
docker compose up -d
```

```bash
docker compose ps
```

Wait for `healthy`. Then:

| | Expect |
|---|---|
| `curl -fsS http://127.0.0.1:6051/health` | `ok` — the process is alive |
| `curl -o /dev/null -w '%{http_code}' http://127.0.0.1:6051/health/ready` | `200` — it can reach PostgreSQL |
| `curl -o /dev/null -w '%{http_code}' http://127.0.0.1:6051/api/settings/general` | `401` — listening and refusing anonymous callers |

`/health/ready` is the one that matters. The old check — treating `401` on a
real endpoint as healthy — is also true of an application whose database is
unreachable, which is how a server once sat "healthy" while every request
failed.

Then from outside, with the tunnel up:

```bash
curl -o /dev/null -w '%{http_code}\n' https://hoapi.sivayaantechnologies.com/health/ready
```

---

## 4 · The tunnel

Nothing changes on a host that already runs `cloudflared`: the API is still
published on `localhost:6051`, so the existing config and credentials keep
working. `systemctl start cloudflared` (or `net start cloudflared`) and it
finds the container.

On a bare Linux VM, the tidiest answer is to run the tunnel as a container
too — the commented `cloudflared` service at the bottom of `compose.yaml`
takes a tunnel token from the Cloudflare dashboard and talks to `api:8080`
over the compose network, which makes the published port unnecessary.

---

## 5 · Upgrading

```bash
git pull
docker compose build
docker compose stop api
docker compose run --rm migrate
docker compose up -d
```

Migrate between stopping and starting, for the same reason the Windows
deployment does: the new build may expect a column the old schema lacks, and
the old build tolerating a new column is the easier direction. Roll back by
checking out the previous tag and rebuilding — but a migration that dropped a
column is only recovered from a `pg_dump`, which is why
`docs/DATABASE_RELEASES.md` §3 insists destructive changes go in their own
release.

Pin a release rather than overwriting `:latest` by setting `TAG` in `.env`.

---

## 6 · What is still not covered

- **Backups.** The container holds nothing, which is the good news; the VM
  holds everything. `docs/POSTGRESQL_SETUP.md` §7 has the nightly `pg_dump`,
  and it is still nobody's job.
- **One instance only.** `Counters` is serialised with a row lock so document
  numbering is safe under concurrency, but nothing else has been examined for
  running two API containers at once. Scale out needs a pass over the nine
  places that open their own transactions first — `Program.cs` says why
  `EnableRetryOnFailure` is off, and the same list applies.
- **The image is unverified by execution.** It was written and reviewed, and
  the code changes in it were tested directly (`/health`, `/health/ready` and
  `--migrate` all exercised against a live and a dead database) — but the
  machine it was written on has neither Docker nor WSL, so `docker build` has
  never run. Expect to iterate once on the first build; the apt packages in
  the runtime stage are the likely spot.
