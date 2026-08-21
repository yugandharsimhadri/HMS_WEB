# Testing

Three test surfaces, and one of them also produces the user guide's screenshots.

| Project | Target | What it covers | Speed |
|---|---|---|---|
| `tests/Pharma.Tests` | net10.0 | Services and calculators against a temp SQLite file. No UI | seconds |
| `tests/Pharma.UiTests` | net10.0-windows | FlaUI end-to-end against the real running app | minutes |
| `Pharma.Automation` | net10.0-windows | Not tests — the `AppFixture` harness the UI tests drive |

---

## Running them

```bash
dotnet build HMS_WPF.slnx

# Unit tests — fast, run these constantly
dotnet test tests/Pharma.Tests/Pharma.Tests.csproj

# One class
dotnet test tests/Pharma.Tests/Pharma.Tests.csproj --filter "FullyQualifiedName~PediatricsServiceTests"

# UI tests for one module
dotnet test tests/Pharma.UiTests/Pharma.UiTests.csproj --filter "FullyQualifiedName~DentistUiTests"
```

> **Run only the classes your change touches.** The full UI suite launches the
> application repeatedly and takes a long time; it is a pre-release gate, not an
> inner-loop tool.

---

## Unit tests

Each test class builds its own `IDbContextFactory<AppDbContext>` over a
throwaway SQLite file in the temp directory, calls `db.Database.Migrate()`, and
deletes the file (plus `-shm` and `-wal`) in `Dispose`, after
`SqliteConnection.ClearAllPools()`.

That means every class gets a **real migrated schema**, not an in-memory
substitute — so foreign keys, delete behaviour and unique indexes are genuinely
exercised.

Notable classes: `PediatricsServiceTests`, `PharmacyServiceTests`,
`GstCalculatorTests`, `DoseMathTests`, `PackMathTests`, `DataHealthTests`,
`SettingsSplitTests`, `ImportTests`.

> These are the tests that **survive the web migration** — they test services,
> not screens. Treat them as the safety net during the port, and add cross-tenant
> isolation tests to them early.

---

## UI tests and `AppFixture`

`AppFixture` launches the real executable once per test class
(`IClassFixture<AppFixture>`), pointed at a throwaway database and log directory
via environment overrides, and kills stray processes from an aborted run first.

Helpers, all addressing controls by `AutomationId`:

| Helper | Purpose |
|---|---|
| `Navigate(navId, expectedTitle)` | Clicks a nav button and waits for the page title. Dismisses leftover modals and overlays first |
| `Click` · `Type` · `SetDate` · `CheckBox` · `ComboBox` · `ListBox` · `Grid` | Control access |
| `SelectTab(tabControlId, header)` | Tab by header text |
| `SelectRowByText(gridId, text)` · `CellOf(gridId, header, row)` | Grid rows by content, cells **by column heading rather than index** — so adding a column does not break unrelated assertions |
| `WaitUntil(condition, what)` | Polls with a timeout and a readable failure message |

### Two things that will bite

**View models are DI singletons shared across every test in a class.** Whatever
patient, case or tab the previous test left selected is still selected. Every
test that needs a clean start clears it explicitly — see `RegisterAndSelectPatient`
in `PediatricsUiTests` and `DentistUiTests`.

**Template columns do not report their value.** WPF hands back an internal
placeholder (`"Item: Pharma.Core.Product, Column Display Index: 7"`) instead of
the text. `AppFixture.TextOfCell` falls through to reading the displayed text;
tests that read template columns directly must do the same.

> A known gap: there is **no working helper for typing into an editable
> DataGrid cell.** Click-then-type, click-then-F2, double-click and the cell's
> own Value pattern were each tried and each left the cell blank under
> synthetic input. Tests and the screenshot walkthrough avoid inline grid
> editing rather than pretend it works.

---

## Screenshot capture

`ScreenshotCapture.Capture_screens_for_the_user_guide` is a single sequential
walkthrough of the real application that produces **every image under
`docs/images/`** in one pass.

```bash
dotnet test tests/Pharma.UiTests --filter ScreenshotCapture
```

It sets the clinic up, switches on every module, and then works through the app
in order, capturing as it goes. `Annotate.Draw` produces the `*-annotated.png`
variants — it rings named controls on the captured bitmap and writes each note
in a margin, so the callouts cannot drift from the screen.

**Rerun it after any UI change**, then regenerate the PDF:

```bash
node scripts/docs-to-pdf/guide_to_pdf.mjs
```

### It seeds a full clinic on purpose

The walkthrough deliberately creates ten OPD patients, ten stocked medicines, a
five-line pharmacy bill, three dental cases with four sittings and three
part-payments, seven growth measurements, three lab orders and a five-test
diagnostic bill.

That is not incidental. These screenshots are shown to prospects, and a screen
with one row in it reads as an empty product however good the software behind it
is. Keep the data rich when extending the walkthrough.

---

## Print document tests

`PrintDocumentTests` builds every `FlowDocument` in-process and asserts on its
text and its colours — including that each renders **black ink on a white page**
regardless of the application theme. They need `[StaFact]`, not `[Fact]`, because
WPF document types require an STA thread.
