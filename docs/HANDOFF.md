# Handoff — continuing the HMS_WPF → SivayaanHMS (web SaaS) port

Read this file, then `docs/PARITY_OPD_PHARMACY.md`. That is enough to start.
**Do not read the whole repo** — it is large and most of it you will never
touch. Everything below tells you which specific file to open when you need
it.

---

## What this is

`HMS_WPF` (a .NET 10 WPF desktop hospital system for a children's clinic) is
being ported to `SivayaanHMS` — a multi-tenant web SaaS. Same product, same
clinical rules, new delivery.

Both repos sit side by side:

```
C:\Users\yugan\source\repos\yugandharsimhadri\
  HMS_WPF\      the desktop original — the specification. Read-only.
  HMS_WEB\      this repo. All work happens here.
```

`HMS_WPF` is **frozen**. It is the source of truth for behaviour. When you
are unsure what something should do, read its viewmodel — never guess.

---

## Architecture in six lines

```
backend/src/SivayaanHMS.Core      entities, enums, calculators   (ported ~verbatim)
backend/src/SivayaanHMS.Data      services, EF Core, SQLite      (ported, tenancy added)
backend/src/SivayaanHMS.Printing  server-side PDFs (QuestPDF)
backend/src/SivayaanHMS.Api       ASP.NET Core, JWT, controllers
backend/tests/SivayaanHMS.Tests   xUnit
frontend/                          React + TypeScript (Vite)
```

**Every domain service is already ported.** Most remaining work is
controllers + React screens over services that already exist and already
work.

---

## Done vs remaining

**Done (100% parity, browser-verified):** OPD (queue, booking, fee,
consultation), Pharmacy (counter, medicines, inventory), Patients, Settings,
auth/signup, and three PDFs (prescription, fee receipt, tax invoice).
See `docs/PARITY_OPD_PHARMACY.md` — 105 items, all ☑.

**Remaining** — service ported, no controller, no UI:

| Module | HMS_WPF viewmodel (lines) | Notes |
|---|---|---|
| Appointments | `AppointmentsViewModel.cs` (402) | booking, cancel/reschedule, check-in, reminders |
| Diagnostics | `DiagnosticsViewModel.cs` (512) | test master + billing |
| Pediatrics | `PediatricsViewModel.cs` (647) | vaccines, growth chart, procedure billing |
| Dentist | `DentistViewModel.cs` (375) | cases, sittings, replacements, payments |
| Pathology Lab | `PathologyLabViewModel.cs` (409) + `PathologyLabMasterViewModel.cs` (145) | analytes, panels, results |
| Dashboard | `DashboardViewModel.cs` (275) | KPIs, revenue, recent activity |
| Reports | `ReportsViewModel.cs` (489) | day book, GST, stock, H1 register |
| Masters | `GeneralMasterViewModel.cs` (299) | shared master-data screens |
| Bill import | `ImportViewModel.cs` (153) | vendor CSV → stock (parser already ported) |
| Data health | `DataHealthViewModel.cs` (138) | pack-size/duplicate repair |

**PDFs still to port** (from `HMS_WPF/src/Pharma.App/Printing/`):
`DiagnosticBillPrinter`, `ProcedureBillDocument`, `DentalReceiptDocument`,
`LabReportDocument`, `AppointmentSlipDocument`, `VaccinationHistoryDocument`.

**Suggested order:** Appointments → Diagnostics → Pediatrics → Dentist →
Pathology Lab → Dashboard → Reports. Each is independently sellable, and the
first four are the ones clinics ask for most.

---

## The method that worked — follow it

For each module, one at a time:

**1 · Inventory before you build.** Read the HMS_WPF viewmodel and write a
parity checklist as `docs/PARITY_<MODULE>.md`, in the same shape as
`PARITY_OPD_PHARMACY.md`: every behaviour, and *why it exists*. Most of
those "why"s encode a real clinical or statutory hazard (loose-sale refusal,
Schedule H1 prescriber, pack-size mismatch). If you skip this step you will
ship a screen that looks right and quietly drops a safeguard.

**2 · Build backend then frontend.** Controller (thin — the service already
holds the rules), then the React page.

**3 · Verify in a real browser. Not optional.** Six bugs in the completed
work compiled perfectly and were only caught by driving the running app.
See the post-mortem table at the end of `PARITY_OPD_PHARMACY.md`.

**4 · Commit and push after each module.** Small commits, real messages.
`git push origin main` — the remote is
`https://github.com/yugandharsimhadri/HMS_WEB.git`.

**5 · Update the parity doc statuses honestly.** ☐ until you have *seen* it
work.

---

## Conventions you must not break

**Tenancy.** Every row carries `TenantId`. `AppDbContext` applies a global
query filter (`!IsDeleted && TenantId == current`) by reflection to every
`BaseEntity` type — so a new entity is protected automatically. Never filter
by tenant in a query yourself. `TenancyIsolationTests` fails the build if an
entity escapes the filter.

**Never bind an EF entity from a request body.** Always a DTO of exactly the
editable fields, copied onto a freshly loaded row. Binding entities let a
client write `TenantId`, `IsDeleted` and audit columns — a real hole that was
found and closed. See `PatientsController.Save` for the pattern.

**Usernames are `local@clinic-slug`** (`admin@twinkle`). The suffix makes them
globally unique and lets sign-in resolve the clinic from the username alone —
which is why login has no clinic field. See `SivayaanHMS.Data/UserName.cs`.

**Money and doses are ported arithmetic, not new code.** `GstCalculator`,
`PackMath`, `DoseMath` came from Core. Where the frontend needs them live
(`frontend/src/clinical/`), they are faithful ports including .NET's
away-from-zero decimal rounding. `GstParityTests` pins client/server
agreement. If you touch one side, touch both.

**Dates are naive clinic-local.** Never `.toISOString()` a booking — the
server stores and returns local times with no offset. Sending UTC shifted
every booking by the timezone offset. A per-tenant timezone is still owed
(see `SAAS_MIGRATION.md` finding 4).

**Enums are the API contract.** Check the real values in
`SivayaanHMS.Core/Enums.cs` before typing them in TypeScript. Two were
invented during the last pass (`AdjustmentReason.Damaged` — it is `Breakage`;
`DispensingUnit.Injection` — does not exist) and both failed at runtime only.

**Print = server-side PDF**, returned inline; the browser's viewer replaces
the desktop's print preview. Fetch it as a blob so the bearer token is sent —
see `openPdf` in `frontend/src/api/client.ts`.

**Deliberate divergences from the desktop** (do not "fix" these): modal
overlays → routed pages/drawers; `MessageBox` → inline messages, except where
acknowledgement genuinely matters (cancel a visit, H1 prescriber, duplicate
medicine) which stay as confirms; `Environment.UserName` → the signed-in user.

---

## Running it

```bash
# backend  (http://localhost:5130)
cd HMS_WEB/backend && dotnet build
cd src/SivayaanHMS.Api && dotnet run

# frontend (http://localhost:5173)
cd HMS_WEB/frontend && npm install && npm run dev

# tests
cd HMS_WEB/backend && dotnet test tests/SivayaanHMS.Tests
```

Register a clinic at `/register`, or sign in to an existing one. Migrations
apply automatically in Development.

**Gotcha:** after `dotnet ef migrations add`, you must `dotnet build` before
`dotnet run --no-build`, or the app starts against a stale snapshot and dies
with `PendingModelChangesWarning`.

---

## Reference material

| File | When |
|---|---|
| `docs/PARITY_OPD_PHARMACY.md` | The pattern to copy — and the bug post-mortem |
| `docs/architecture/SAAS_MIGRATION.md` | Why the port is shaped this way; what is still owed |
| `docs/architecture/BUSINESS_RULES.md` | Invariants the system must enforce |
| `docs/architecture/SERVICE_REFERENCE.md` | The nine domain services |
| `docs/USER_GUIDE.md` | Annotated screenshot of every desktop screen — the closest thing to a UI spec |
| `docs/DATABASE_DESIGN.md` | Data dictionary |

---

## First task

Start with **Appointments**. Read
`HMS_WPF/src/Pharma.App/ViewModels/AppointmentsViewModel.cs`, write
`docs/PARITY_APPOINTMENTS.md`, then build it. `AppointmentsService` is
already ported and already enforces the module-enabled check and the
cancel/reschedule rules — you are writing a controller and a screen, not
business logic.
