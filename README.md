# SivayaanHMS (Web / SaaS edition)

Multi-tenant web edition of Sivaayaan HMS. Independent codebase from `HMS_WPF`
(the Windows desktop product) — no shared build, no shared references.
Desktop customers keep getting `HMS_WPF`; clinics that want web access
register here instead.

Reference material carried over from the desktop product lives in
[`docs/`](docs/) — in particular
[`docs/architecture/SAAS_MIGRATION.md`](docs/architecture/SAAS_MIGRATION.md)
(the plan this port follows) and
[`docs/architecture/BUSINESS_RULES.md`](docs/architecture/BUSINESS_RULES.md)
(the spec every port must satisfy). If code and docs disagree, the docs here
are the frozen desktop reference — not necessarily what HMS_WEB does today.

## Layout

```
backend/
  SivayaanHMS.slnx
  src/SivayaanHMS.Core   domain entities, enums, calculators (ported from Pharma.Core)
  src/SivayaanHMS.Data   EF Core + SQL Server, services, migrations (ported from Pharma.Data)
  src/SivayaanHMS.Api    ASP.NET Core Web API, multi-tenant
  tests/SivayaanHMS.Tests
frontend/                React + TypeScript (Vite)
deploy/                  production deployment and database release scripts
db/                      generated idempotent migration script
docs/                    operations runbooks, parity checklists, architecture reference
```

## Decisions locked in for this build

- **Frontend:** React + TypeScript.
- **Database:** **SQL Server** (Express in production, LocalDB is not
  supported — it is per-user and stops when nobody is logged in). Shared
  schema, `TenantId` on every row. SQLite was the original choice and is
  gone: the provider, the migrations and the test harness all moved. See
  [SQL_SERVER_MIGRATION.md](docs/SQL_SERVER_MIGRATION.md) for what that
  found.
- **Tenancy:** shared database, `TenantId` + a global EF Core query filter
  applied centrally, never per-query.
- **Not doing:** the plan's WebView2-wrapped "one UI, two shells" option.
  HMS_WPF and HMS_WEB stay two separate products by design.

## Running in production

Frontend on **Cloudflare Pages**; API as a **Windows service** on the
clinic's own machine, reached over a **Cloudflare Tunnel**. The database is
on that same machine and never leaves it.

**Every release, in this order:**

```powershell
.\deploy\Migrate-Database.ps1 -SqlInstance <instance> -DryRun   # see what is pending
Stop-Service SivayaanHMSApi
.\deploy\Migrate-Database.ps1 -SqlInstance <instance>           # back up, then migrate
.\deploy\Deploy-Production.ps1 -SqlInstance <instance>          # publish and restart
```

Then push the branch Cloudflare Pages watches; the frontend deploys itself.

Migrate **before** publishing: a new build may expect a column the old schema
lacks, and the old build tolerating a new column is the easier direction.
[DATABASE_RELEASES.md](docs/DATABASE_RELEASES.md) has the reasoning, the
rollback, and the rules for writing a migration that can be released safely.

## Status

Eleven modules are ported and at feature parity with the desktop, each
verified by driving the running app in a browser rather than by compiling:

| Module | Checklist | Items |
|---|---|---|
| OPD & Pharmacy, Patients, Settings | [PARITY_OPD_PHARMACY.md](docs/PARITY_OPD_PHARMACY.md) | 105 |
| Appointments | [PARITY_APPOINTMENTS.md](docs/PARITY_APPOINTMENTS.md) | 59 |
| Diagnostics | [PARITY_DIAGNOSTICS.md](docs/PARITY_DIAGNOSTICS.md) | 61 |
| Pediatrics | [PARITY_PEDIATRICS.md](docs/PARITY_PEDIATRICS.md) | 67 |
| Dentist | [PARITY_DENTIST.md](docs/PARITY_DENTIST.md) | 52 |
| Pathology Lab | [PARITY_PATHOLOGY_LAB.md](docs/PARITY_PATHOLOGY_LAB.md) | 58 |
| Dashboard | [PARITY_DASHBOARD.md](docs/PARITY_DASHBOARD.md) | 30 |
| Reports | [PARITY_REPORTS.md](docs/PARITY_REPORTS.md) | 51 |

Every PDF the desktop produced is ported, plus report export to PDF and
Excel. [PERFORMANCE.md](docs/PERFORMANCE.md) records a measured pass against
a seeded two-year dataset.

**Platform support** has no desktop equivalent — a SaaS needs somebody who
can put a locked-out clinic owner back in without being able to read that
clinic's patients. See
[PLATFORM_ADMIN.md](docs/PLATFORM_ADMIN.md). Its password is deliberately
**not in this repository**: set it in the git-ignored `appsettings.Local.json`
or via `PlatformAdmin__Password`, or the account cannot sign in at all.

**Masters, Bill import and Data health have since landed** — the three the
handoff listed as outstanding. Every desktop module now has a web equivalent.

## Where to look

| I want to… | Read |
|---|---|
| Deploy a release | [DATABASE_RELEASES.md](docs/DATABASE_RELEASES.md), then [DEPLOY_CLOUDFLARE.md](docs/DEPLOY_CLOUDFLARE.md) |
| Set up a brand new machine | [FIRST_DEPLOYMENT.md](docs/FIRST_DEPLOYMENT.md) |
| Understand the database, or upgrade it | [SQL_SERVER_SETUP.md](docs/SQL_SERVER_SETUP.md), [DATABASE_DESIGN.md](docs/DATABASE_DESIGN.md) |
| Know where data is stored on disk | [STORAGE_PATHS.md](docs/STORAGE_PATHS.md) |
| Pick up the codebase cold | [HANDOFF.md](docs/HANDOFF.md) |
| Check a module against the desktop | the `PARITY_*.md` files below |
| Reset a locked-out clinic admin | [PLATFORM_ADMIN.md](docs/PLATFORM_ADMIN.md) |
| See what is still missing | [GAP_ANALYSIS.md](docs/GAP_ANALYSIS.md) |
| Run or add a real-browser end-to-end test | [SivayaanHMS.Automation/README.md](backend/tools/SivayaanHMS.Automation/README.md) |

## Testing

```bash
dotnet test backend                              # 85 unit/integration tests, real SQL Server
dotnet test backend/tests/SivayaanHMS.UatTests    # 8 real-browser journeys, real API, throwaway database
```

The second suite is not a mock of the app — it publishes the actual API, creates a throwaway SQL
Server database, starts the actual Vite client, registers a clinic through the real sign-up
endpoint, and drives Playwright through eight business journeys (signing in, registering a
patient, booking a visit, reading back today's OPD register, and more) exactly as a person would.
See its own README for the shape and the reasoning; it is built on the same tools as the sibling
TransTrack and ABPS_WEB projects' own UAT suites.

## Known open risks

- **No routine backup.** `Migrate-Database.ps1` takes one before every
  migration, which covers a bad release. Nothing covers a failed disk, a
  deletion noticed next week, or a stolen machine — and the data lives on one
  machine in a clinic. This is the largest open item; see
  [DATABASE_RELEASES.md §5](docs/DATABASE_RELEASES.md).
- **Frontend testing is browser-driven, not component-level.** The 8-workflow
  UAT suite above proves the real journeys end to end; there is still no
  Vitest/RTL layer for testing a single component in isolation.
- **Pediatric procedures are not seeded.** A new clinic gets 20 dental
  procedures and zero pediatric ones.

## Working on this

Start at [docs/HANDOFF.md](docs/HANDOFF.md). It carries the working method,
the conventions that must not be broken, and — importantly — the fact that
five of the desktop viewmodels being ported from exist **only** on HMS_WPF's
unmerged `origin/Dentist_Pathology` branch, not on its `main`.

```bash
cd backend && dotnet build SivayaanHMS.slnx     # then: cd src/SivayaanHMS.Api && dotnet run
cd frontend && npm install && npm run dev
cd backend && dotnet test tests/SivayaanHMS.Tests
```

Register a clinic at `/register`, or sign in. Migrations apply automatically
in Development.
