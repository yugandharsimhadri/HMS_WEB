# Architecture documentation

Written at the **desktop freeze** — the point at which the WPF product stopped
taking new features and work turned to the web/SaaS edition. Everything here
describes the system *as built*, so that the port has a specification rather
than a reading exercise.

If any document here disagrees with the code, **the code is right and the
document is stale.** Each one names the files it was derived from.

---

## The documents

| Document | What it is for | Read it when |
|---|---|---|
| [BUSINESS_RULES.md](BUSINESS_RULES.md) | Every invariant the system enforces, and why | **Before porting anything.** These rules are the product; the screens are just how they are reached |
| [SERVICE_REFERENCE.md](SERVICE_REFERENCE.md) | The nine domain services, their public API, and what each guarantees | Porting the backend — this layer moves almost intact |
| [SAAS_MIGRATION.md](SAAS_MIGRATION.md) | What transfers, what breaks, and the phased plan | Planning the web edition |
| [UI_ARCHITECTURE.md](UI_ARCHITECTURE.md) | MVVM conventions, navigation, overlays, printing | Rebuilding the front end, or maintaining the WPF one |
| [TESTING.md](TESTING.md) | Test projects, fixtures, and how the guide screenshots are produced | Adding tests, or wondering why a UI test is shaped oddly |
| [OPERATIONS.md](OPERATIONS.md) | Paths, backups, logging, configuration | Supporting an installed clinic |
| [CLINICAL_MODULES_DESIGN.md](CLINICAL_MODULES_DESIGN.md) | The original design for Appointments, Pediatrics, Dentist, Pathology Lab | Understanding *why* those modules are shaped as they are — a design doc, not an as-built record |
| [Pharma.DemoRunner.md](Pharma.DemoRunner.md) | The scripted demo tool | Producing a demo or a screencast |

Elsewhere in `docs/`:

| Document | What it is for |
|---|---|
| [../DATABASE_DESIGN.md](../DATABASE_DESIGN.md) | The data dictionary — every table, column and delete rule |
| [../DATABASE_UPGRADES.md](../DATABASE_UPGRADES.md) | Upgrading a live clinic's database |
| [../USER_GUIDE.md](../USER_GUIDE.md) | The end-user guide, with a screenshot of every screen |
| [../INSTALLING.md](../INSTALLING.md) · [../BUILDING_THE_INSTALLER.md](../BUILDING_THE_INSTALLER.md) | Shipping it |
| [../BILL_REVIEW.md](../BILL_REVIEW.md) | A review of the pharmacy bill against what the law expects |

---

## The system in one page

**Three layers, and the dependency rule that makes the migration possible:**

```
Pharma.App          net10.0-windows   WPF · views, view models, printing, reports
      ↓ references
Pharma.Data         net10.0           services, DbContext, migrations, seeders
      ↓ references
Pharma.Core         net10.0           entities, enums, pure calculators
```

`Pharma.Core` and `Pharma.Data` **target plain `net10.0` and contain no WPF
reference of any kind.** That is deliberate and it is the single most valuable
property of this codebase: the domain and the data layer can be lifted into an
ASP.NET Core process without modification to their dependencies.

Everything Windows-specific — every `Window`, `UserControl`, `FlowDocument`,
`MessageBox` — lives in `Pharma.App`.

**Supporting projects**

| Project | Target | What it is |
|---|---|---|
| `Pharma.Automation` | net10.0-windows | `AppFixture` — launches the real app for UI tests against a throwaway database |
| `tests/Pharma.Tests` | net10.0 | Service and calculator tests. No UI |
| `tests/Pharma.UiTests` | net10.0-windows | FlaUI end-to-end tests, plus the guide screenshot capture |
| `tools/Pharma.DemoRunner` | net10.0-windows | Drives the app through a scripted demo |

---

## Scale, at freeze

| | |
|---|---|
| Tables | 47 — 46 entities in `Entities.cs` plus `ImportProfile`, all deriving from `BaseEntity` |
| Enums | 20 |
| Domain services | 9, plus auth, settings, numbering, health and bootstrap |
| EF migrations | 22 |
| Screens | ~20 pages and ~20 popup editors (48 XAML files) |
| Print documents | 10 |
| Seeders | 6 — vaccines, dental procedures, lab analytes/reports, diagnostic tests, import profiles, the default user |

## Modules

Seven modules, each independently switchable under **Settings → Features**, and
each genuinely absent from the sidebar, Reports and Patients when off:

**OPD** · **Pharmacy** · **Diagnostics** · **Appointments** · **Pediatrics** ·
**Dentist** · **Pathology Lab**

OPD and Pharmacy default on; the rest default off. The toggles are stored as
plain settings keys (`features.*.enabled`) and read fresh on every navigation —
which is why a module appears without a restart.
