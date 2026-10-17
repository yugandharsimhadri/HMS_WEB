# Moving to an Azure Linux VM: PostgreSQL, Docker, tunnel

Step by step, with the tests that prove each step before the next one depends
on it. Everything runs on **one Ubuntu VM**: PostgreSQL native, the API in a
container, `cloudflared` as a service. The frontend stays on Cloudflare Pages.

```
browser ──https──> healthone.sivayaantechnologies.com    (Cloudflare Pages)
   └──────https──> hoapi.sivayaantechnologies.com        (Cloudflare edge)
                        │  tunnel, outbound only
                   cloudflared  (systemd, on the VM)
                        │  http://localhost:6051
                   API container  (Docker, 0.0.0.0:8080)
                        │  172.17.0.1:5432
                   PostgreSQL 18  (native, on the same VM)
```

**The security win worth noticing:** `cloudflared` dials *out* to Cloudflare,
so this VM needs **no inbound rule at all** — no 443, no 6051, and certainly
no 5432. Lock the Azure NSG down to nothing but the SSH you need to build it,
and the API is still reachable worldwide through the tunnel.

Facts this was written against, measured on the Windows box: **PostgreSQL
18.6**, 49 tables, 1 tenant, 1 user, **11 MB**, no extensions. The data move
is therefore seconds, not an outage.

---

## 1 · The Azure VM

| | |
|---|---|
| Image | Ubuntu Server 24.04 LTS |
| Size | `Standard_B2s` (2 vCPU, 4 GB) is enough; `B2ms` (8 GB) is comfortable |
| OS disk | Premium SSD. 11 MB of data — a separate data disk is tidier but not needed yet |
| Inbound ports | **none**, once you are done. SSH only while building, restricted to your own IP |
| Outbound | default (allow all). The tunnel and `apt` need it |

Set the clock's timezone so logs and `cron` read sensibly. The *container*
sets its own `TZ` (which is what the application's timestamps depend on), but
a host on UTC makes every log line an exercise in mental arithmetic:

```bash
sudo timedatectl set-timezone Asia/Kolkata
```

```bash
sudo apt update && sudo apt upgrade -y
```

**Test 1** — `timedatectl` shows `Asia/Kolkata` and `date` agrees with your
watch.

---

## 2 · PostgreSQL 18

**Not `apt install postgresql`.** Ubuntu 24.04 ships PostgreSQL 16, and a
dump taken from 18.6 **cannot** be restored into 16 — `pg_restore` fails on
the archive version. Add the PostgreSQL project's own repository:

```bash
sudo apt install -y curl ca-certificates
sudo install -d /usr/share/postgresql-common/pgdg
sudo curl -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc --fail https://www.postgresql.org/media/keys/ACCC4CF8.asc
echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" | sudo tee /etc/apt/sources.list.d/pgdg.list
sudo apt update && sudo apt install -y postgresql-18
```

**Test 2**

```bash
sudo -u postgres psql -c "select version();"
```

Must say **18.x**. On Ubuntu the `postgres` superuser is reached by peer
authentication through `sudo -u postgres`, so there is no superuser password
to set or keep — a genuine simplification over Windows.

### Let the container reach it

The container connects from the Docker bridge (`172.17.0.1`), not loopback, so
two settings change. Both are inside the VM; nothing is exposed to the
internet because the NSG has no inbound rule.

```bash
sudo -u postgres psql -c "ALTER SYSTEM SET listen_addresses = '*';"
```

```bash
echo "host    sivayaanhms    healthone_app    172.16.0.0/12    scram-sha-256" | sudo tee -a /etc/postgresql/18/main/pg_hba.conf
```

```bash
sudo systemctl restart postgresql
```

Belt and braces with the host firewall — the bridge may reach 5432, nothing
else may:

```bash
sudo ufw allow from 172.16.0.0/12 to any port 5432 proto tcp
sudo ufw allow OpenSSH
sudo ufw --force enable
```

### The role and the empty database

```bash
sudo -u postgres psql -v app_user='healthone_app' -v app_password='<the same password the Windows box uses>' -f deploy/sql/01-create-database.sql
```

Use `01-create-database.sql`, **not** `full-schema.sql`: this one creates only
the role, the database and the privileges. The tables are about to arrive in
the restore, and creating them first would collide.

Keep the password identical to the Windows box's — then the `.env` you already
have needs only its `Host=` changed.

**Test 3**

```bash
PGPASSWORD='<that password>' psql -h localhost -U healthone_app -d sivayaanhms -c "select current_user, current_database();"
```

---

## 3 · Move the data

**On the Windows box**, with the API still serving — `pg_dump` takes a
consistent snapshot and blocks nothing:

```powershell
$pw = (Get-Content C:\Server\hoapi\appsettings.Production.json -Raw | ConvertFrom-Json).Database.ConnectionString
$pw = ($pw -split ';' | Where-Object { $_ -like 'Password=*' }) -replace '^Password=',''
$env:PGPASSWORD = $pw
& "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe" -h localhost -U healthone_app -d sivayaanhms -Fc --no-owner --no-privileges -f "$env:USERPROFILE\sivayaanhms.dump"
```

`-Fc` is the compressed custom archive (restorable table by table);
`--no-owner --no-privileges` means the restore does not try to recreate a
`healthone_app` that the new server grants differently — objects end up owned
by whoever runs the restore, which is exactly what you want.

Verify the file reads back *before* trusting it:

```powershell
& "C:\Program Files\PostgreSQL\18\bin\pg_restore.exe" --list "$env:USERPROFILE\sivayaanhms.dump" | Select-Object -First 5
```

Copy it over:

```powershell
scp "$env:USERPROFILE\sivayaanhms.dump" azureuser@<vm-ip>:~/
```

**On the VM**, restore as the application role so it owns everything:

```bash
PGPASSWORD='<that password>' pg_restore -h localhost -U healthone_app -d sivayaanhms --no-owner --no-privileges ~/sivayaanhms.dump
```

**Test 4 — the one that matters.** Row counts, ownership and schema version,
all as the application role:

```bash
PGPASSWORD='<that password>' psql -h localhost -U healthone_app -d sivayaanhms <<'SQL'
SELECT tableowner, count(*) AS tables FROM pg_tables WHERE schemaname='public' GROUP BY tableowner;
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1;
SELECT (SELECT count(*) FROM "Tenants") AS tenants,
       (SELECT count(*) FROM "Users")   AS users,
       (SELECT count(*) FROM "Patients") AS patients;
SELECT "Slug", "ClinicName", "LicenseExpiresOn" FROM "Tenants";
SQL
```

Expect **49 tables owned by `healthone_app`**, `1 | 1 | 0`, your clinic's slug,
and one migration row (`20260920032241_InitialCreate`). If the owner column
says `postgres`, you restored as the superuser — `docs/DEPLOY_IIS.md` §8b has
the fix.

---

## 4 · Docker

```bash
sudo apt install -y ca-certificates curl gnupg
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" | sudo tee /etc/apt/sources.list.d/docker.list
sudo apt update && sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo usermod -aG docker $USER && newgrp docker
```

**Test 5** — `docker run --rm hello-world` prints its greeting, and
`docker compose version` answers.

### Get the source onto the VM and build

The image is built on the VM (the Windows box has no working engine, so
`docker save` is not an option). Either clone — a GitHub fine-grained PAT or
deploy key, since the repository is private:

```bash
sudo apt install -y git
git clone https://github.com/yugandharsimhadri/HMS_WEB.git
cd HMS_WEB
```

— or, with no git credentials on the VM, send a snapshot from Windows:

```powershell
git -C C:\Users\yugan\source\repos\yugandharsimhadri\HMS_WEB archive --format=tar.gz -o $env:USERPROFILE\hms.tar.gz HEAD
scp $env:USERPROFILE\hms.tar.gz azureuser@<vm-ip>:~/
```

```bash
mkdir -p HMS_WEB && tar xzf ~/hms.tar.gz -C HMS_WEB && cd HMS_WEB
```

```bash
docker compose build
```

First build pulls the .NET SDK and runtime images — a few minutes and about
1.5 GB of download, once.

**Test 6** — `docker images | grep sivayaanhms` shows the image, a couple of
hundred MB.

---

## 5 · Configure

```bash
cp deploy/docker/.env.example deploy/docker/.env
nano deploy/docker/.env
```

Only four values matter, and the database is one line:

| Key | Value |
|---|---|
| `Database__ConnectionString` | `Host=host.docker.internal;Port=5432;Database=sivayaanhms;Username=healthone_app;Password=<the password>;Command Timeout=60;Application Name=SivayaanHMS` |
| `Jwt__Key` | **the key the Windows box uses**, copied across — a different one signs every live session out |
| `PlatformAdmin__Password` | the `EnterpriseAdmin` password |
| `Cors__AllowedOrigins__0/1` | the two Pages origins, unchanged |

`host.docker.internal` works on Linux because `compose.yaml` maps it with
`extra_hosts: host-gateway`; it resolves to the bridge gateway, the address
the `pg_hba` line in §2 admits. Never `localhost` — inside a container that is
the container.

**Test 7** — the connection string is right before anything else runs:

```bash
docker compose run --rm --entrypoint sh api -c 'apt-get -qq update >/dev/null 2>&1; getent hosts host.docker.internal'
```

An address means the container can see the host. (The real proof is Test 9.)

---

## 6 · Bring the schema up to the build

Your restored database is at `InitialCreate`; the image is built from current
`main`, which has a second migration (`PasswordResetByPhone` — a `Phone`
column on `Users` and a `PasswordResetCodes` table). Without this step every
login fails with `column "Phone" does not exist`:

```bash
docker compose run --rm migrate
```

It names what it applies and exits non-zero on failure. It works unaided
because `healthone_app` owns the tables.

**Test 8**

```bash
PGPASSWORD='<that password>' psql -h localhost -U healthone_app -d sivayaanhms -Atc 'select "MigrationId" from "__EFMigrationsHistory" order by 1'
```

Two rows now.

---

## 7 · Start the API

```bash
docker compose up -d
docker compose ps
```

Wait for `healthy` (the healthcheck gives it 20 seconds to start).

**Test 9 — the real one.** Three checks, in order; each tells you something
different:

```bash
curl -fsS http://127.0.0.1:6051/health            # ok      → the process is alive
curl -o /dev/null -w '%{http_code}\n' http://127.0.0.1:6051/health/ready   # 200 → it reached PostgreSQL
curl -o /dev/null -w '%{http_code}\n' http://127.0.0.1:6051/api/settings/general  # 401 → serving, refusing anonymous
```

`/health/ready` returning 503 means the database is unreachable — check the
`pg_hba` line, the password, and `docker compose logs api`, which names the
reason.

**Test 10 — timezone, which nothing else will catch.** The application stores
clinic-local wall-clock time, so a container on UTC silently shifts every
visit time and day-book boundary by 5½ hours:

```bash
docker compose exec api date
```

Must print IST (`+0530`), not UTC.

---

## 8 · The tunnel

```bash
curl -fsSL https://pkg.cloudflare.com/cloudflare-main.gpg | sudo tee /usr/share/keyrings/cloudflare-main.gpg >/dev/null
echo "deb [signed-by=/usr/share/keyrings/cloudflare-main.gpg] https://pkg.cloudflare.com/cloudflared any main" | sudo tee /etc/apt/sources.list.d/cloudflared.list
sudo apt update && sudo apt install -y cloudflared
```

Use a **dashboard-managed tunnel** rather than a `config.yml` — it is one
command on the VM and the DNS record is created for you, which is where the
Windows setup lost half an hour:

1. Cloudflare dashboard → Zero Trust → Networks → Tunnels → **Create a
   tunnel** → Cloudflared → name it `hms-azure`.
2. Copy the install command it shows; the token is in it.

```bash
sudo cloudflared service install <TOKEN>
```

3. In the tunnel's **Public Hostname** tab: hostname
   `hoapi.sivayaantechnologies.com`, service `http://localhost:6051`.

Adding that hostname **moves the DNS record to this tunnel** — which is the
cutover moment, so do it after Test 9 passes. The old Windows tunnel keeps
running and simply stops receiving traffic, which is what makes rollback easy.

**Test 11**, from your laptop or a phone on mobile data, never from the VM:

```bash
curl -o /dev/null -w '%{http_code}\n' https://hoapi.sivayaantechnologies.com/health/ready    # 200
curl -o /dev/null -w '%{http_code}\n' https://hoapi.sivayaantechnologies.com/api/settings/general   # 401
```

`530`/`1033` means the tunnel is not connected (`systemctl status cloudflared`);
`502` means the tunnel is up and the container is not.

---

## 9 · Test the application itself

The checks above prove plumbing. These prove the product.

**Test 12 — sign in.** Open `https://healthone.sivayaantechnologies.com`,
sign in as the clinic admin that came across in the restore. The dashboard
should load with your data.

**Test 13 — print something. Do not skip this one.** It is the only test that
exercises Linux-specific code. Every document falls back to `"Segoe UI"`, a
Windows font that does not exist here; the image installs DejaVu and aliases
it, but the alias is a safety net, not a guarantee.

Before testing, make the output deterministic: **Settings → Document branding
→ print font → `Lato`.** QuestPDF bundles Lato and registers it in-process, so
it renders identically on Windows and Linux and needs no system font.

Then: book a visit, collect the fee, print the receipt, print a prescription,
and export one report as PDF and one as Excel. Check the PDFs actually have
text in them rather than boxes or blanks.

**Test 14 — EnterpriseAdmin.** Sign in with the platform-admin credentials;
the clinic should be listed, and patient screens should refuse you.

**Test 15 — survive a reboot.** The step everyone skips, and the one that
decides whether a clinic can power-cycle the VM on a Monday morning:

```bash
sudo reboot
```

After it comes back, with nothing typed: `docker compose ps` shows the API
healthy again (`restart: unless-stopped`), `systemctl status cloudflared` and
`systemctl status postgresql` are both active, and Test 11 passes from
outside.

---

## 10 · Cutover and rollback

The cutover is step 8's hostname assignment, and it is reversible in about a
minute: in the dashboard, move `hoapi.sivayaantechnologies.com` back to the
old Windows tunnel. Keep the Windows box exactly as it is — IIS running,
PostgreSQL running — for a week or two, as a rollback that needs no rebuild.

One thing to be careful about: **once clinic staff start using the Azure VM,
the Windows database is stale.** Rolling back after real use means losing
whatever was entered, so if you do roll back, dump from Azure and restore onto
Windows rather than just flipping DNS.

When you are confident, on the Windows box: stop the IIS site, disable its
autostart, and stop the old `cloudflared` service. Delete the old tunnel in
the dashboard.

---

## 11 · Afterwards

- **Backups now matter more, not less.** Everything is on one VM. The nightly
  `pg_dump` in `docs/POSTGRESQL_SETUP.md` §7 belongs in `cron`, writing
  somewhere off the VM — Azure Blob with `azcopy`, or Azure Backup on the VM
  itself. 11 MB compresses to nothing; there is no excuse.
- **Updating the API** is `git pull`, `docker compose build`,
  `docker compose stop api`, `docker compose run --rm migrate`,
  `docker compose up -d` — migrate between stop and start, for the reason
  `docs/DATABASE_RELEASES.md` gives.
- **The frontend is untouched** by all of this. It deploys to Cloudflare Pages
  exactly as before.
- **Still one instance only.** Document numbering is safe under concurrency,
  but running two API containers has not been examined — see the note at the
  end of `docs/DEPLOY_DOCKER.md`.
