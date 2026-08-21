# Step 01 — UAT Automation Discovery (HMS_WPF)

**Project:** Sivayaan Content Engine — Discovery Phase
**Date:** 2026-08-02
**Scope:** Analysis only. No changes were made to the HMS_WPF repository.

---

## 1. Where the UAT project lives

| Item | Value |
|---|---|
| Repository | `C:\Users\srini\source\repos\yugandharsimhadri\HMS_WPF` |
| Solution file | `HMS_WPF.slnx` (XML-based solution format, repo root) |
| UAT / UI automation project | `tests/Pharma.UiTests/Pharma.UiTests.csproj` |
| Project name | **Pharma.UiTests** |
| Application under test (AUT) | `src/Pharma.App` → builds **`TwinkleHMS.exe`** (WPF, "Twinkle Children's Hospital" HMS) |

There is no separately named "UAT" project. UAT is performed by the **Pharma.UiTests** suite — full end-to-end UI tests that launch the real WPF window and drive it through Windows UI Automation. The historical UAT evidence in `docs/screeshotUAT/` (`.trx` result files named `UAT_CoreFlows.trx`, `UAT_VisitAndPrintFlows.trx`, screenshots, and `HMS_UAT_TestPack_2026-07-29.xlsx`) was all produced by running this project.

---

## 2. Solution structure

```
HMS_WPF.slnx
├── src/
│   ├── Pharma.Core/      → entities, enums, GstCalculator, licensing (no dependencies)
│   ├── Pharma.Data/      → AppDbContext (SQLite), migrations, services, DbBootstrapper, AppLog
│   └── Pharma.App/       → WPF views, view models, printing → output: TwinkleHMS.exe
├── tests/
│   ├── Pharma.Tests/     → unit tests (GST, stock, pack math, licensing, upgrades…)
│   └── Pharma.UiTests/   → UAT/UI automation suite (subject of this document)
├── tools/IconGen/        → utility, not in the solution file
├── scripts/              → publish.ps1, make-installer.ps1, publish-clickonce.ps1, docs-to-pdf
└── docs/                 → user guide, DB design docs, screeshotUAT/ (past UAT evidence)
```

Dependency direction: `Pharma.UiTests` → references `Pharma.App` + `Pharma.Data`. It uses `Pharma.Data` only for two constants (environment-variable names, see §5).

---

## 3. Test framework & tooling

| Concern | What is used | Version |
|---|---|---|
| Test framework | **xUnit** | 2.9.3 |
| Runner integration | `xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk` | 3.1.4 / 17.14.1 |
| STA-thread support | `Xunit.StaFact` (WPF FlowDocument objects are thread-affine) | 1.2.69 |
| UI automation | **FlaUI** (`FlaUI.Core` + `FlaUI.UIA3` — Windows UIA3 provider) | 5.0.0 |
| Coverage | `coverlet.collector` | 6.0.4 |
| Target framework | `net10.0-windows` | — |

**Entry point:** there is no `Main`. The project is a standard VSTest-discoverable test assembly (`pharma.uitests.dll`). Execution is host-driven: `dotnet test`, `vstest.console.exe`, or Visual Studio Test Explorer. Parallelization is **disabled assembly-wide** (`AssemblyInfo.cs`): `CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)` — because UI Automation drives a single desktop.

---

## 4. Folder & test organization

The project is flat — all files sit in the project root; there is no Pages/ or PageObjects/ folder hierarchy.

### Infrastructure files (the in-house framework)

| File | Role |
|---|---|
| `AppFixture.cs` | The heart of the suite. Launches `TwinkleHMS.exe` against a throwaway temp database, attaches FlaUI UIA3 automation, finds the main window, and exposes ~40 helper methods (element lookup by AutomationId, `Navigate`, `Type`, `Click`, grid row/cell helpers by column *header* not index, queue-tile helpers, fee-taking sequences, modal/overlay dismissal, `WaitUntil` polling). Also defines `UiTestBase` (each test class gets its own app + DB via `IClassFixture<AppFixture>`). |
| `AssemblyInfo.cs` | Disables parallelization (one desktop). |
| `Annotate.cs` | Draws annotated callouts onto screenshots for documentation. |
| `ScreenshotCapture.cs` | A single `[Fact]` that drives the whole app and regenerates every screenshot in `docs/images` for the user guide. This is what produced the UAT screenshot pack. |

### Test classes (one class ≈ one screen/flow of the app)

| Module of the app | Test classes |
|---|---|
| OPD desk / queue | `OpdUiTests`, `OpdSearchUiTests`, `QueueLayoutUiTests`, `DatePickerUiTests` |
| Consultation / prescription | `ConsultationUiTests`, `PrescriptionUiTests`, `FeverVisitUiTests` (door-to-bill end-to-end) |
| Pharmacy counter | `PharmacyUiTests`, `CounterQuantityUiTests`, `CounterRulesUiTests`, `CounterStockUiTests`, `AfterSaveUiTests` |
| Inventory / medicines | `InventoryPopupUiTests`, `PackSizeRepairUiTests`, `DataHealthUiTests` |
| Reports | `ReportsUiTests` |
| Printing / reprints | `PrintDocumentTests` (headless FlowDocument checks — no app launch), `ReprintUiTests` |
| Shell / navigation / settings | `NavigationUiTests`, `ShellCreditUiTests`, `SettingsUiTests`, `ThemeUiTests` |
| Documentation | `ScreenshotCapture` |

Roughly **117 `[Fact]`/`[Theory]`/`[StaFact]` attributes** across 26 test classes. Test names are behavior-phrased snake case, e.g. `Booking_a_walk_in_puts_a_tile_in_the_waiting_column`, `Saving_a_bill_numbers_it_and_deducts_the_stock`.

Shared reusable flows are `internal static` helpers on test classes — e.g. `OpdUiTests.BookWalkIn(app, name, phone, age)` is called by `ScreenshotCapture` and the session tests.

---

## 5. How a single UAT test is executed today

### Lifecycle

```
dotnet test  (or VS Test Explorer / vstest.console)
   │
   ├─ builds Pharma.App first (project reference) → TwinkleHMS.exe
   │
   ├─ xUnit creates AppFixture for the test class:
   │     1. Generates temp DB path:  %TEMP%\twinkle-ui-<guid>.db
   │     2. Kills stray TwinkleHMS processes (leftovers lock build output)
   │     3. Sets env vars on the child process:
   │           CLINICDESK_DB   = temp DB path   (DbBootstrapper.PathOverrideVariable)
   │           TWINKLE_LOG_DIR = temp log dir   (AppLog.DirectoryOverrideVariable)
   │     4. Walks up from test bin dir to find HMS_WPF.slnx, then launches
   │        src\Pharma.App\bin\<Debug|Release>\net10.0-windows\TwinkleHMS.exe
   │        (configuration matched to how the tests were built)
   │     5. FlaUI UIA3 attaches; waits ≤30 s for the main window,
   │        then waits for the "PageTitle" label to be non-empty
   │
   ├─ each [Fact] in the class drives the live window via AutomationIds
   │     (Navigate → Type → Click → WaitUntil assertions; polling, no sleep-and-hope)
   │
   └─ Dispose: close app (kill if needed), delete temp DB (+ -shm/-wal), delete temp logs
```

Key isolation facts:

- **One app instance and one throwaway SQLite DB per test *class***, not per test. Tests inside a class share state; classes are independent.
- The suite **never touches the live database** at `C:\ProgramData\TwinkleHMS\twinkle.db` — redirection is purely via the `CLINICDESK_DB` env var, honored by `DbBootstrapper` (`src/Pharma.Data/DbBootstrapper.cs:12`).
- Tests locate the AUT executable **relative to the solution root** (found by walking up for `HMS_WPF.slnx`) — the solution must be built first, in the same configuration.
- Everything is found by **AutomationId** (the app was instrumented for this); grid cells are addressed by column *header* to survive layout changes.
- Tests run **strictly serially** and require an **interactive Windows desktop** (real mouse-less UIA invocations, but a real visible window).

### Running one specific test today

The mechanism actually used (evidenced by the `.trx` files under `TestResults/` and `docs/screeshotUAT/`) is the standard VSTest pipeline with a trx logger. A single test is selected with a filter, e.g.:

```
dotnet test tests/Pharma.UiTests --filter "FullyQualifiedName~OpdUiTests.Booking_a_walk_in_puts_a_tile_in_the_waiting_column" --logger trx
```

(The `.trx` names `UAT_CoreFlows` / `UAT_VisitAndPrintFlows` appear to be manually renamed/parameterised runs of subsets of this suite; no script in the repo produces them — see UNKNOWNS.)

---

## 6. Safest way for an external application (Content Engine) to execute an existing UAT test

*Analysis of the current implementation only — no changes proposed.*

The suite has **no programmatic API, no CLI of its own, and no Main entry point**. As built today, there is exactly one supported execution surface: the **VSTest protocol**. Given that, the observations relevant to an external caller are:

1. **Process-level invocation of `dotnet test` (or `vstest.console.exe`) with a `--filter` is the only path that preserves every safety property the suite depends on.** All of the suite's protections live inside `AppFixture`'s lifecycle — stray-process cleanup, temp-DB redirection, log redirection, modal/overlay dismissal, guaranteed teardown. Any host that goes through xUnit (which `dotnet test` does) gets all of this for free. Anything that tries to bypass the runner (e.g. loading `pharma.uitests.dll` and calling helpers directly) would bypass fixture construction/disposal and could touch the live ProgramData database or leak `TwinkleHMS.exe` processes.

2. **Machine-readable results already exist**: `--logger trx` emits a TRX file the external application can parse (this is precisely how the existing UAT evidence in `docs/screeshotUAT/` was captured).

3. **Preconditions the external caller must respect** (all facts of the current implementation):
   - The solution must be **built first**, same configuration the tests will resolve (`Debug`/`Release`) — `AppFixture.FindExecutable` throws otherwise.
   - The repo layout matters: the exe is found by walking up to `HMS_WPF.slnx`; the tests cannot run from a detached copy of the DLL.
   - An **interactive desktop session** is required (real WPF window + UIA3). A service/session-0 or locked-screen context will fail.
   - **No concurrency**: one run at a time per machine (parallelization is disabled for a reason — one desktop). Two overlapping runs would fight over the screen and `KillStrays()` would kill each other's app.
   - .NET 10 SDK (Windows), and nothing else holding `TwinkleHMS.exe`'s build output open.

4. **Test selection granularity**: because state is shared within a class, the natural safe unit of external execution is a *test class* (or a single self-contained `[Fact]`); `--filter FullyQualifiedName~<Class>` or `~<Class>.<Method>` maps directly onto that.

In short: the current implementation already treats "an external process invoking the VSTest runner" as its only client — Visual Studio and the developer's shell are just two such clients — so a Content Engine acting as a third such client, shelling out to `dotnet test --filter … --logger trx` and parsing the TRX, exercises exactly the paths that already exist and nothing more.

---

## 7. Existing UAT evidence artifacts

| Artifact | Location |
|---|---|
| TRX results | `tests/Pharma.UiTests/TestResults/{UAT_CoreFlows,UAT_VisitAndPrintFlows,ScreenshotCapture}.trx` and copies under `docs/screeshotUAT/` |
| UAT screenshots (annotated + plain) | `docs/screeshotUAT/UATscrenshots/*.png` |
| UAT summary images | `HMS_UAT_Scenarios.png`, `HMS_UAT_Summary.png` |
| UAT test pack workbook | `docs/screeshotUAT/HMS_UAT_TestPack_2026-07-29.xlsx` |

`UAT_CoreFlows.trx` (2026-07-29) covers 27 results across Opd/Consultation/Pharmacy/Reports/Settings tests; `UAT_VisitAndPrintFlows.trx` covers 13 results across FeverVisit/Prescription/Reprint tests — i.e. the "UAT runs" were two filtered executions of the ordinary `Pharma.UiTests` suite.

---

## UNKNOWNS

The following require clarification before any Content Engine implementation:

1. **How the UAT TRX files were actually produced.** No script, CI workflow, or documented command in the repo generates `UAT_CoreFlows.trx` / `UAT_VisitAndPrintFlows.trx`. Were these manual `dotnet test --filter … --logger "trx;LogFileName=…"` invocations? What exact filters defined "CoreFlows" vs "VisitAndPrintFlows"?
2. **The authoritative UAT scenario list.** Is `HMS_UAT_TestPack_2026-07-29.xlsx` the source of truth mapping business UAT scenarios → automated tests? (The workbook was not opened during this analysis.)
3. **What the Content Engine actually needs from a "test execution."** Pass/fail only? TRX? Screenshots? Live progress? Log files? This determines whether TRX parsing suffices.
4. **Target execution machine.** Will the Content Engine run tests on this same dev machine (interactive desktop, .NET 10 SDK, repo checked out and buildable), or on a separate agent/VM? The suite cannot run headless or from a detached binary drop as currently written.
5. **Concurrency expectations.** Can the Content Engine guarantee at most one UAT run at a time per machine? `KillStrays()` kills *all* `TwinkleHMS.exe` processes — including a clinician's live session if the app were ever run on the same box.
6. **Build responsibility.** Who builds the solution before a run — the Content Engine, a scheduled job, or is a pre-built tree assumed? Which configuration (Debug vs Release) should UAT runs use? (Existing TRX evidence points at `bin\debug`.)
7. **Expected run duration/budget.** The full evidenced UAT runs took ~2.5 min (CoreFlows) on the recorded machine; is per-test or per-class invocation expected, and what timeout should the Content Engine apply?
8. **Whether `ScreenshotCapture` counts as UAT.** It doubles as documentation generation and writes into `docs/images` in the repo — should an external runner ever trigger it?
9. **Licensing/evaluation constraints of the AUT.** `Pharma.Core/Licensing` (embedded evaluation license, clock-tampering detection) exists; unknown whether license state can ever block a UAT launch on a fresh machine or future date.
10. **.NET 10 SDK availability guarantee** on whatever machine the Content Engine targets (`net10.0-windows` is required for both build and test host).
11. **Naming/branding**: the app is branded *TwinkleHMS* but the DB env var is legacy `CLINICDESK_DB`. Confirm both names are stable contracts before an external tool depends on them.
