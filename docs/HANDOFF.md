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

**Done (100% parity, browser-verified):** OPD, Pharmacy, Patients, Settings,
auth/signup, and — added since this file was first written — **Appointments,
Diagnostics, Pediatrics, Dentist, Pathology Lab, Dashboard and Reports**.

| Module | Parity doc | Items |
|---|---|---|
| OPD & Pharmacy | `docs/PARITY_OPD_PHARMACY.md` | 105 |
| Appointments | `docs/PARITY_APPOINTMENTS.md` | 58 |
| Diagnostics | `docs/PARITY_DIAGNOSTICS.md` | 61 |
| Pediatrics | `docs/PARITY_PEDIATRICS.md` | 67 |
| Dentist | `docs/PARITY_DENTIST.md` | 52 |
| Pathology Lab | `docs/PARITY_PATHOLOGY_LAB.md` | 58 |
| Dashboard | `docs/PARITY_DASHBOARD.md` | 30 |
| Reports | `docs/PARITY_REPORTS.md` | 46 |

Every PDF the desktop had is now ported. `SivayaanHMS.Printing` holds
prescription, fee receipt, tax invoice, appointment slip, diagnostic bill,
procedure bill, vaccination history, dental receipt, lab report, and the
report PDF/Excel builders.

**Remaining** — service ported, no controller, no UI:

| Module | HMS_WPF viewmodel (lines) | Notes |
|---|---|---|
| Masters | `GeneralMasterViewModel.cs` (299) | shared master-data screens — **now the blocking one, see below** |
| Bill import | `ImportViewModel.cs` (153) | vendor CSV → stock (parser already ported) |
| Data health | `DataHealthViewModel.cs` (138) | pack-size/duplicate repair |

### Why Masters is now the one that matters

Four of the modules above ship with a real gap that only Masters closes,
because the desktop deliberately puts master data on its own destination and
the ports kept that split:

- **Pediatrics** — `DentistProcedureSeeder` seeds Dentist procedures only, so
  a fresh tenant has **no Pediatrics procedures at all** and procedure
  billing has nothing to offer. The endpoints exist; the screen does not.
- **Dentist** — anesthesia types, replacements and packages are not seeded,
  so sittings, replacements and package cases stay empty.
- **Pathology Lab** — analytes and reports are seeded, but **reference ranges
  are not**, so every result comes back with no range and no Low/High flag.
  That flag is the most clinically useful thing the module computes.
- **Diagnostics** — its own test master *is* built into the module, so it is
  unaffected. It is the exception, not the pattern.

Each parity doc's "What this module deliberately does NOT include" section
says exactly what its own gap is.

### A spec-source warning

`AppointmentsViewModel`, `PediatricsViewModel`, `DentistViewModel`,
`PathologyLabViewModel` and `GeneralMasterViewModel` **do not exist on
HMS_WPF's `main`**. They live only on the unmerged branch
`origin/Dentist_Pathology` (`3603677`, 19 Aug 2026). `DashboardViewModel`
differs between the two (240 lines on main, 275 on the branch) and the
branch version is the one ported, since it is the only one that knows the
newer modules exist.

Read from the branch without switching HMS_WPF's working tree:

```
git show origin/Dentist_Pathology:src/Pharma.App/ViewModels/GeneralMasterViewModel.cs
```

Note also that `main` carries three commits the branch lacks, touching the
OPD receipt and doctor registration number. The two disagree about
already-shipped OPD behaviour, and that is worth resolving before anyone
ports against the wrong one.


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

Start with **Masters** — `GeneralMasterViewModel.cs` (299 lines, on
`origin/Dentist_Pathology` only). Write `docs/PARITY_MASTERS.md`, then build
it.

It is the right next module for two reasons. It is the last thing standing
between four already-shipped modules and being genuinely usable on a fresh
tenant (see "Why Masters is now the one that matters" above). And nearly all
of its write endpoints already exist — Pediatrics built procedure CRUD,
Diagnostics built its own test master, and the Dentist and Pathology Lab
controllers already expose reads of every list Masters has to edit. Much of
this module is screens over endpoints that are already there.

The two after it, in order: **Bill import** (`ImportViewModel.cs`, 153 — the
CSV parser is already ported as `PurchaseImportService`), then **Data
health** (`DataHealthViewModel.cs`, 138).

### What the last seven modules taught

Worth reading before starting, because each of these cost real time to find:

- **Hold a selected row by id, never as an object.** Capturing the row means
  it goes stale the moment the list behind it reloads, and the screen then
  offers actions the server refuses. This bit Appointments; every module
  after it derives the selection from the live list.
- **Anything a document prints must be read server-side, not accepted.**
  Patient names, prices, base costs. A DTO that omits a field entirely is a
  stronger guarantee than one that validates it.
- **Leave totals off the request.** `SaveDiagnosticBillRequest` has no
  `TotalAmount`, so the server's arithmetic is the only arithmetic. Same for
  the lab order and the procedure bill.
- **The PDF font subset has no ₹ and no en dash.** Use "Rs." and a plain
  hyphen. Excel is fine with both. This cost a garbled reference range on a
  clinical report before it was caught.
- **Verify empty-vs-broken before writing "it works".** A control that looks
  dead may just have no data behind it — the Reports zero-stock toggle
  looked broken and was not.
