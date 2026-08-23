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
  src/SivayaanHMS.Data   EF Core + SQLite, services, migrations (ported from Pharma.Data)
  src/SivayaanHMS.Api    ASP.NET Core Web API, multi-tenant
  tests/SivayaanHMS.Tests
frontend/                React + TypeScript (Vite)
docs/                    architecture reference, carried over from HMS_WPF at freeze
```

## Decisions locked in for this build

- **Frontend:** React + TypeScript.
- **Database:** SQLite for now (shared schema, `TenantId` on every row —
  revisit the engine only if/when a tenant's scale demands it).
- **Tenancy:** shared database, `TenantId` + a global EF Core query filter
  applied centrally, never per-query.
- **Not doing:** the plan's WebView2-wrapped "one UI, two shells" option.
  HMS_WPF and HMS_WEB stay two separate products by design.

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

**Still to port:** Masters (`GeneralMasterViewModel`), Bill import
(`ImportViewModel`), Data health (`DataHealthViewModel`). Masters is the one
that matters — four shipped modules have master data that cannot be edited
until it lands, which
[HANDOFF.md](docs/HANDOFF.md) explains.

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
