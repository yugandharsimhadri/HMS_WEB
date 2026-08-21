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
  SivayaanHMS.sln
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

Scaffold only. Backend builds; no domain code ported yet. See
`SAAS_MIGRATION.md`'s phased plan — this repo is at the start of Phase 1.
