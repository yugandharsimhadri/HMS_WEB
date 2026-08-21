# UI architecture

`src/Pharma.App` — WPF, MVVM via CommunityToolkit.Mvvm. 48 XAML files, ~20 page
view models and ~20 popup editors.

This layer does **not** port to the web; it is documented so the WPF edition
stays maintainable, and so the conventions worth keeping are recognisable when
the front end is rebuilt.

---

## The shell

`MainWindow.xaml` is the only top-level window in normal operation. It holds:

- the **left navigation** — a `ScrollViewer` over grouped `NavButton`s, with
  collapsible groups for Pharmacy and Pathology Lab;
- the **page host** — a `ContentControl` bound to `MainViewModel.CurrentPage`,
  resolved to a view by `DataTemplate` in `MainWindow.xaml`;
- the **overlay host** — where popup editors appear;
- the clinic name, the signed-in user, and the page title and subtitle.

### Navigation

`MainViewModel.GoAsync(string key)` maps a nav key to a page view model:

```
"opd" → _opd    "sale" → _sale    "pediatrics" → _pediatrics
"general-master" → _generalMaster            … and so on
```

Every page implements **`IPage`**:

```csharp
public interface IPage
{
    string Title { get; }
    string Subtitle { get; }
    Task LoadAsync();
}
```

`GoAsync` sets `ActiveNav`, swaps `CurrentPage`, subscribes to the page's
`PropertyChanged` so a live subtitle keeps updating, and awaits `LoadAsync()`.

> **The rule that matters:** page view models are **DI singletons**, so a
> constructor runs once per process but `LoadAsync()` runs on *every*
> navigation. Anything that can change while the user is elsewhere — feature
> toggles, master lists, settings — must be read in `LoadAsync`, never cached in
> the constructor. This is why a module switched on under Settings appears
> without a restart.

Active state is expressed twice, never by colour alone: a background tint via
`NavBrushConverter` and a left bar plus bold text via `NavActiveConverter`
driving a `Tag`-triggered template.

### Overlays

Editors open *over* the shell rather than as separate windows:

```csharp
var editor = new PatientEditorViewModel(opd);
var shell  = App.Services.GetRequiredService<MainViewModel>();
await shell.ShowOverlayAsync(editor, close => editor.RequestClose += () => close());
if (editor.Saved is { } patient) { … }
```

The pattern: the editor exposes `RequestClose`, and an `Outcome` or `Saved`
property the caller reads once the await returns. The overlay disables the
navigation behind it — a UI test that leaves one open will fail the next
navigation, which is why `AppFixture.Navigate` dismisses stragglers first.

---

## Conventions

**View models** are `partial class : ObservableObject` using
`[ObservableProperty]` and `[RelayCommand]` source generators. `partial void
OnXChanged(...)` hooks react to property changes — searches are debounced this
way rather than with a timer.

**Fire-and-forget** work uses the `.Forget("what it was doing")` extension so a
failure is logged rather than swallowed.

**Every interactive control carries `AutomationProperties.AutomationId`.** Not
optional — the UI tests and the guide screenshot capture both address controls
by it. A new control without one is untestable.

**Refusals** surface through `Dialog.Show(...)` from the view model. Services
throw; view models catch and show. No service references a dialog.

**Theme.** `Theme.xaml` holds styles and converter registrations;
`Theme.Light.xaml` and `Theme.Dark.xaml` hold the two palettes with identical
keys, swapped at runtime. Anything that should follow the theme uses
`DynamicResource`, never `StaticResource`.

**Numeric input** uses the `NumericInput.Mode` attached property (`Money`,
`Whole`) rather than per-box validation.

---

## Printing

`src/Pharma.App/Printing/` — **WPF `FlowDocument`, and therefore the part of
this layer with no server equivalent.**

`DocumentBuilder` provides the shared vocabulary: `NewDocument`,
`AddClinicHeader`, `NewTable`, `Row`, `TotalRow`, `Text`, `Rule`,
`ApplyPrintSettings`. Each document composes those:

| Document | Produces |
|---|---|
| `BillPrinter` | The pharmacy tax invoice — GST breakdown, HSN, batch, expiry |
| `PrescriptionPrinter` | The prescription, with allergies |
| `FeeReceiptDocument` | Consultation fee receipt; also holds `InWords` |
| `DiagnosticBillPrinter` | Diagnostic bill |
| `ProcedureBillDocument` | Pediatrics / Dentist / General procedure bill |
| `DentalReceiptDocument` | Dental part-payment receipt |
| `LabReportDocument` | Pathology report with reference ranges and flags |
| `VaccinationHistoryDocument` | Vaccination certificate |
| `AppointmentSlipDocument` | Appointment slip |

`PrintService.Preview(() => doc, title)` opens `PrintPreviewWindow`. Documents
are built **lazily inside the lambda** so the preview can rebuild on a page-size
change.

Branding — logo, fonts, footer — comes from `DocumentTheme`
(`SettingsService.GetDocumentThemeAsync`), so every document looks like one
clinic. `PrintDocumentTests` asserts each renders black-on-white regardless of
the app theme.

## Reports

`src/Pharma.App/Reports/` — `ReportKind` enumerates the tabs;
`ReportsViewModel` loads each; `ReportPdfBuilder` and `ReportExcelBuilder`
export. Adding a report means a new `ReportKind`, a load method, a tab, and a
case in each builder.

---

## Screen inventory

**Pages** — Dashboard · OPD · Patients · Pharmacy counter · Medicines ·
Inventory · Reports · Diagnostics · Appointments · Pediatrics · Dentist ·
Pathology Lab · Pathology Lab Master · General Master · Settings

**Popup editors** — patient · medicine · receive stock · correct stock · quick
stock · edit quantity · book visit · collect fee · consultation · diagnostic
test · test picker · vaccine · record vaccination · record growth · procedure ·
add procedure line · dental replacement · anesthesia type · dental package ·
lab analyte · lab report · lab package

**Windows** — Login · Print preview · Import · Data health · About · Message

Every one of these is screenshotted and annotated in
[USER_GUIDE.md](../USER_GUIDE.md), which is the best available specification for
a rebuild.
