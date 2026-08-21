# Step 03 — Product Registration Discovery (HMS_WPF)

**Project:** Sivayaan Content Engine — Discovery Phase (Step 1.2)
**Date:** 2026-08-02
**Mode:** Discovery only. Facts extracted from the repository as it exists at commit `8fda0d1`. Nothing designed, nothing recommended.

---

## 1. What uniquely identifies the product?

Facts as stated in `src/Pharma.App/Pharma.App.csproj`:

| Identity field | Value | Source |
|---|---|---|
| `<Product>` | `Twinkle Children's Hospital` | csproj line 15 |
| `<Company>` | `Twinkle Children's Hospital` | csproj line 16 |
| `<AssemblyName>` | `TwinkleHMS` | csproj line 9 |
| `<Version>` | `1.0.0.4` (format `major.minor.patch.PUBLISH`; 4th part counts customer-shipped builds; last shipped was `1.0.0.3`) | csproj lines 17–32 (comment documents the scheme) |
| `<RootNamespace>` | `Pharma.App` | csproj line 10 |
| Repository | `HMS_WPF` (git, branch `main`, remote under `yugandharsimhadri`) | working tree |
| Solution file | `HMS_WPF.slnx` (also serves as the runtime "solution root" marker — `AppFixture.FindExecutable` and `ScreenshotCapture` walk up until they find this file) | repo root |
| README title | "Twinkle Children's Hospital — HMS_WPF" | `README.md` |

Note the three-name situation that any registration must be aware of: repo/solution name **HMS_WPF**, code namespace **Pharma.\***, shipped brand **TwinkleHMS / Twinkle Children's Hospital** (plus the legacy env-var name `CLINICDESK_DB` from the pre-branding "ClinicDesk" build).

## 2. Which executable starts the application?

`TwinkleHMS.exe`, built from `src/Pharma.App` (`OutputType WinExe`, `UseWPF true`, `net10.0-windows`).

- Dev build output path (the one the UI tests resolve): `src\Pharma.App\bin\<Debug|Release>\net10.0-windows\TwinkleHMS.exe`
- Dev run command (README): `dotnet run --project src/Pharma.App`
- Published/installed location (per `scripts/publish.ps1` default): `C:\HMS\App\TwinkleHMS.exe` (self-contained, win-x64)

## 3. Which test project belongs to the product?

Two test projects, both in `HMS_WPF.slnx`:

| Project | Path | Nature |
|---|---|---|
| `Pharma.UiTests` | `tests/Pharma.UiTests` | The UAT/UI automation suite (xUnit + FlaUI; drives the real window). This is the project relevant to Content Engine test execution — see Step01/Step02. |
| `Pharma.Tests` | `tests/Pharma.Tests` | Unit tests (GST, stock, pack math, licensing, upgrades). Not UI/UAT. |

## 4. Which build configuration is required?

No configuration is mandated anywhere. Facts:

- `AppFixture.FindExecutable` matches the AUT's configuration to **whatever configuration the tests were built in** (path contains `\Release\` → Release, otherwise Debug). Both work; they must simply agree, which building the whole solution in one configuration guarantees.
- The only recorded UAT evidence (TRX, 2026-07-29) ran from `bin\debug\`.
- Shipping uses `Release` (`dotnet publish … -c Release` in README and `scripts/publish.ps1`).

## 5. Which solution file should be built?

`HMS_WPF.slnx` at the repo root — the only solution file. It contains the three `src/` projects and both `tests/` projects (`tools/IconGen` is outside the solution). Building it in one configuration satisfies the test suite's exe-resolution contract.

## 6. Which products/modules currently exist?

One product. Its modules, as the shell itself defines them (sidebar `AutomationId`s in `src/Pharma.App/MainWindow.xaml`, each mapping to a view in `src/Pharma.App/Views/`):

| Sidebar AutomationId | Page title (asserted by tests) | View |
|---|---|---|
| `NavOpd` | OPD | `OpdView.xaml` |
| `NavPatients` | Patients | `PatientsView.xaml` |
| `NavSale` | Pharmacy counter | `SaleView.xaml` |
| `NavProducts` | Medicines | `ProductsView.xaml` |
| `NavInventory` | Inventory | `InventoryView.xaml` |
| `NavReports` | Reports | `ReportsView.xaml` |
| `NavSettings` | Settings | `SettingsView.xaml` |

Overlay/secondary surfaces (not sidebar entries): consultation (`ConsultationView`), booking (`BookVisitView`), fee collection (`CollectFeeView`), medicine editor, patient editor, receive stock, correct stock, quick stock, data health window, import window, about window, message window.

The README describes the product at a coarser grain: "Two modules only: **OPD** and **Pharmacy**." Both framings exist in the repo; which one Content Engine registration should use is an open question (carried from Step02).

## 7. Which business workflows currently exist?

Workflows factually present, as documented by the README ("What the screens do"), the user guide, and exercised by the test suite:

1. **OPD booking** — find patient by name/phone (family shares one number), or add inline; pick doctor and time; book; token allocated. Booking and visit are one record.
2. **Queue management** — waiting/completed columns (tiles or rows layout), mark done, reopen, sitting/session filter by consulting hours.
3. **Fee collection** — fee form (amount, payment mode, optional print) → confirmation → numbered receipt.
4. **Consultation** — vitals, complaint, diagnosis, advice, follow-up; editable prescription grid (stocked and unstocked medicines); prints A4 prescription with doctor's registration.
5. **Pharmacy sale** — search medicine, nearest-expiry batch auto-selected, quantity with unit (tablets/strips), GST-inclusive pricing, save + print bill; pull today's OPD prescriptions into a bill.
6. **Counter rules** — whole-pack enforcement, loose-sale flag, Schedule H1 prescriber requirement.
7. **Quick stock-in at the counter** — add stock without leaving the bill; flagged for reconciliation.
8. **Medicine catalogue management** — create/edit medicines (GST rate, pack size, flags).
9. **Inventory** — receive stock (batch, qty, purchase rate, MRP), correct stock with reason.
10. **Data health / repair** — detect and repair pack-size disagreements; recount shelf stock.
11. **Reports** — Day book, GST summary, OPD register, Expiring soon, Part packs, Stock to reconcile, Low stock, Stock Register (with zero-stock toggle + Excel export), Schedule H1 register.
12. **Reprints** — receipt/prescription/bill reprint from the patient's record; duplicates marked "duplicate".
13. **Settings** — clinic details, pharmacy details (GSTIN, drug licence, pharmacist), document branding, doctors, consulting hours, theme (light/dark), queue layout.
14. **End-to-end** — the whole visit door-to-bill is encoded as one test (`FeverVisitUiTests`).

## 8. Which folders contain screenshots / logs / reports / generated assets?

| Kind | Location | Notes |
|---|---|---|
| Screenshots (user guide, regenerated by tests) | `docs/images/` | Written by `ScreenshotCapture.Capture_screens_for_the_user_guide` (plain + `-annotated` PNGs) |
| Screenshots (UAT evidence) | `docs/screeshotUAT/UATscrenshots/` (note: folder name is misspelled "screeshotUAT"; git status shows the older flat copies deleted and this subfolder untracked) | Also `HMS_UAT_Scenarios.png`, `HMS_UAT_Summary.png` |
| Test result reports (TRX) | `tests/Pharma.UiTests/TestResults/` (`UAT_CoreFlows.trx`, `UAT_VisitAndPrintFlows.trx`, `ScreenshotCapture.trx`); copies under `docs/screeshotUAT/` | `TestResults/` is the VSTest default output dir |
| UAT test pack | `docs/screeshotUAT/HMS_UAT_TestPack_2026-07-29.xlsx` | |
| App logs (production default) | `C:\HMS\Logs` (from `appsettings.json`), falling back to `%ProgramData%\TwinkleHMS\logs` then `%TEMP%\TwinkleHMS\logs` (`AppLog.Resolve` candidate order) | Overridable via env var `TWINKLE_LOG_DIR` |
| App logs (during UI tests) | `%TEMP%\twinkle-ui-logs-<guid>` (set by `AppFixture`, deleted on dispose) | |
| Database (production default) | `C:\HMS\DB\twinkle.db` (appsettings/default root), legacy `C:\ProgramData\TwinkleHMS\twinkle.db` migrated on first launch; backups in `C:\HMS\DBBackup` | Overridable via env var `CLINICDESK_DB` |
| Database (during UI tests) | `%TEMP%\twinkle-ui-<guid>.db` (+ `-shm`/`-wal`), deleted on dispose | |
| Report exports made by the app | User's `Documents` folder — e.g. `StockRegister_<yyyy-MM-dd>.xlsx` (asserted in `ReportsUiTests`) | Test deletes it after verifying |
| Published app / installer output | `C:\HMS\App` (publish.ps1 default); installer scripts under `scripts/installer/` | |
| Marketing assets | `marketing/` (untracked) | Present in working tree; not part of the solution |

## 9. Which command executes one test?

The VSTest filter mechanism (the only selection mechanism the project supports; no scripts wrap it):

```
dotnet test tests/Pharma.UiTests --filter "FullyQualifiedName~Pharma.UiTests.OpdUiTests.Booking_a_walk_in_puts_a_tile_in_the_waiting_column"
```

Add `--logger trx` to produce a TRX result file in `tests/Pharma.UiTests/TestResults/` (this is how the existing UAT evidence looks to have been made — unconfirmed, see UNKNOWNS). `--filter "FullyQualifiedName~<Class>"` runs one class. The solution (at minimum `Pharma.App` + the test project) must be built first in a matching configuration; `dotnet test` performs the build itself unless `--no-build` is passed.

## 10. Which command executes all tests?

From the repo root (documented in README):

```
dotnet test
```

Runs both suites (unit + UI) serially. Scope to the UAT suite only with:

```
dotnet test tests/Pharma.UiTests
```

Caveats that are facts of the current implementation: running everything includes `ScreenshotCapture` (rewrites `docs/images`) and `ReportsUiTests`' export (touches the user's Documents folder); UI tests require an interactive desktop and exclusive use of it.

## 11. Which information is stable?

Judged by how the repo itself treats each item (documented contracts, things other code depends on):

- Solution file name `HMS_WPF.slnx` — load-bearing: the test fixture locates the AUT by walking up to it.
- `AssemblyName` `TwinkleHMS` / exe name `TwinkleHMS.exe` — also load-bearing (`KillStrays` kills by process name "TwinkleHMS").
- Env-var contracts `CLINICDESK_DB` and `TWINKLE_LOG_DIR` — public constants with doc comments describing exactly this external-override use.
- Project layout `src/{Pharma.Core,Pharma.Data,Pharma.App}` + `tests/{Pharma.Tests,Pharma.UiTests}` and the dependency direction (README documents it as deliberate architecture).
- The VSTest execution surface (`dotnet test`, `--filter`, TRX logger) — standard tooling, not project code.
- Sidebar AutomationIds (`NavOpd` … `NavSettings`) and the broader AutomationId instrumentation — the entire UI suite is built on them; changing one breaks tests immediately, which is the strongest stability pressure in the repo.
- Default data roots `C:\HMS\{DB,Logs,DBBackup,App}` — documented in appsettings, publish script, and code comments.

## 12. Which information is likely to change?

Judged by the repo's own history and comments:

- `<Version>` — explicitly designed to be bumped before every publish ("Bump it again the moment .4 has gone out").
- **Test names and the test inventory** — already drifted between the 2026-07-29 TRX evidence and current source (two recorded tests no longer exist under those names; a `NavInventory` module and several test classes were added after the README's "17 UI tests" count, which is also stale).
- README test counts ("13 unit tests", "17 UI tests") — already wrong; do not treat as data.
- Per-class AutomationIds for screen internals — stable *in intent* but revised whenever a screen is reworked (the git log shows active feature work: fees, prints, reports revisions around release 1.0.0.5).
- The documented release line: commit messages reference "Twinkle release 1.0.0.5" while the csproj says 1.0.0.4 — the version field is mid-cycle at any given time.
- Contents of `docs/screeshotUAT/` — currently in mid-reorganization (old flat files deleted, new `UATscrenshots/` subfolder untracked, a stray `node_modules` inside it).
- `Pharma.UiTests.csproj` package versions and target framework (`net10.0-windows` today).
- Screen inventory itself — `NavInventory` is present in the app and tests but absent from `NavigationUiTests`' sidebar Theory, evidence the Theory lags the shell.

---

## RISKS

1. **Name drift is the norm, not the exception.** Test FQNs are the only executable IDs and they have already changed once between the recorded UAT baseline and today's source. Any registration that hard-codes test names will silently rot; only discovery-at-runtime (`dotnet test --list-tests`) reflects truth.
2. **Version ambiguity at any point in time.** csproj says 1.0.0.4, commits reference release 1.0.0.5 — "which version is under test" cannot be read from a single field mid-cycle.
3. **Destructive suite members.** A blanket "run all" mutates the repo (`docs/images`), writes into the operator's `Documents` folder, and `KillStrays` kills every `TwinkleHMS.exe` on the machine — dangerous if a real clinic instance were ever co-resident.
4. **Interactive-desktop dependency.** The whole UI suite needs a visible, unlocked, single-user desktop; any scheduler/service context breaks it, and two concurrent runs sabotage each other.
5. **Working-tree dependency.** Tests cannot run from a deployed binary drop; they need the full repo checkout (slnx marker + built `src/Pharma.App`). A registration that models "product = installed exe" would be wrong for test purposes.
6. **Legacy/branding split.** `CLINICDESK_DB` (legacy name) vs `TwinkleHMS` (brand) vs `Pharma.*` (namespaces) vs `HMS_WPF` (repo): four naming systems refer to one product; picking the wrong one as "the identifier" would create mismatches with logs, process names, or env vars.
7. **Stale self-description.** README counts and module framing ("two modules only") disagree with the code (seven sidebar modules, ~117 test cases). Registration data sourced from prose rather than code would be wrong on day one.
8. **Uncommitted working tree.** `docs/discovery/`, `marketing/`, and the UAT screenshot reorganization are untracked/deleted-but-uncommitted; the "current state" documented here is partly not in version control yet.

## UNKNOWNS

1. **The authoritative product identifier for Content Engine purposes** — repo name, `<Product>` string, `AssemblyName`, or something new — is a decision nobody has recorded anywhere in the repo.
2. **How the UAT TRX files were produced** (exact command, filters, logger parameters) — still unconfirmed from Step01; no script exists.
3. **Whether Debug or Release should be the registered UAT configuration** — evidence says Debug was used; shipping uses Release; nothing mandates either.
4. **Module taxonomy for registration** — README's 2 modules vs the shell's 7 sidebar screens vs the xlsx test pack's grouping (workbook still unopened).
5. **Whether `Pharma.Tests` (unit suite) is in scope** for Content Engine execution, or only `Pharma.UiTests`.
6. **Whether `ScreenshotCapture` and `PrintDocumentTests` count as UAT** (carried from Step02).
7. **Machine registry** — which physical machine(s) the Content Engine would register as capable of running this product's UAT (desktop session, .NET 10 SDK, repo path). Only `DESKTOP-CNR9OSN` at `C:\Users\srini\source\repos\yugandharsimhadri\HMS_WPF` is evidenced.
8. **The 1.0.0.5 discrepancy** — commits say reports/prints were "updated after Twinkle release 1.0.0.5" while csproj holds 1.0.0.4. Which is the actually-shipped latest version?
9. **Licensing behavior at launch** — `Pharma.Core/Licensing` (embedded evaluation licence, clock-tamper detection) exists; whether it can ever block or alter an automated launch (fresh machine, future dates) is untested in this discovery.
10. **Stability policy for AutomationIds and test names** — no documented commitment exists; the Content Engine cannot know what the team promises to keep stable versus what merely happens to be stable today.
11. **Whether `marketing/` and `docs/discovery/` (untracked) are intended to become part of the repo** — affects what "the product's folders" means going forward.
