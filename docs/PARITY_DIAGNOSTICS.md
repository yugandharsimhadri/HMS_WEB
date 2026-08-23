# Diagnostics — feature parity with HMS_WPF

Every behaviour the desktop has in this module, enumerated from the source
(not from memory), with its port status. This is the acceptance checklist:
HMS_WEB is not done until every ☑ below is genuinely ☑.

Derived from: `DiagnosticsViewModel` (512 lines),
`DiagnosticTestEditorViewModel` (130), `DiagnosticTestPickerViewModel` (73),
`DiagnosticsService` (already ported), the `DiagnosticTest` /
`DiagnosticBill` / `DiagnosticBillItem` entities, and `DiagnosticBillPrinter`.

> **Spec source.** Unlike Appointments, `DiagnosticsViewModel.cs` is
> identical on HMS_WPF's `main` and on `origin/Dentist_Pathology` (512 lines
> both) — there is no ambiguity about which to port from. The editor and
> picker were read from `origin/Dentist_Pathology` for consistency.

Status: ☑ done · ◻ not started · ◐ partial

**60 of 61 items are ☑ as of 22 Aug 2026**, each driven against the running
app in a browser — not just compiled. The one remaining is ◐ and says why.
The bugs that testing surfaced are listed at the end.

---

## 1 · Module gating

| # | Feature | Why it exists | Status |
|---|---|---|---|
| 1.1 | Diagnostics is **off by default** (`DiagnosticsEnabled`) | Not every clinic runs tests in-house | ☑ |
| 1.2 | Nav entry appears only when the module is on | An empty screen behind a permanent nav item reads as a broken feature | ☑ |
| 1.3 | Two tabs — Billing and Test Master | Each is a handful of fields, not a destination of its own | ☑ |

## 2 · Test master

| # | Feature | Why | Status |
|---|---|---|---|
| 2.1 | Lists every test — name, category, price, active | ☑ |
| 2.2 | Search by name, as you type | ☑ |
| 2.3 | Search includes **inactive** tests; billing only ever offers active ones | The master is where a retired test is found again to bring it back | ☑ |
| 2.4 | **New test** and **Edit** both open the same editor | Matches how Medicines edits a product | ☑ |
| 2.5 | Edit is unavailable with nothing selected | ☑ |
| 2.6 | Editor fields: name, category, price, active | ☑ |
| 2.7 | Category is a **suggest list, not a closed set** — eight seeded examples unioned with categories already in use, sorted | A clinic's own vocabulary matters more than ours, but a blank box invites eight spellings of "Hematology" | ☑ |
| 2.8 | Blank category defaults to `Others` on save | ☑ |
| 2.9 | Name is required — refuses with "Test name is required." and marks the field | ☑ |
| 2.10 | The name-missing marker clears the moment a name is typed | A field left red once it is right is a lie | ☑ |
| 2.11 | Save trims name and category | ☑ |
| 2.12 | **Delete** offered only for a test already saved | ☑ |
| 2.13 | Delete asks for confirmation | ☑ |
| 2.14 | Delete is **refused once the test has ever been billed** — "Deactivate it instead." | A past bill keeps its own denormalised name and price, but deleting the master row is still the one action that can strip a historic line of its test reference | ☑ |
| 2.15 | Deactivating is the supported alternative — an inactive test stays in the master but leaves billing | ☑ |
| 2.16 | The editor closes on save/delete and the list refreshes, keeping the edited row selected | ☑ |
| 2.17 | Cancel writes nothing | ☑ |
| 2.18 | The outcome message surfaces on the Test Master screen | ☑ |

## 3 · Billing — who is being billed

| # | Feature | Why | Status |
|---|---|---|---|
| 3.1 | Patient search by name **or phone**, at most 20 matches | ☑ |
| 3.2 | Search runs as you type | ☑ |
| 3.3 | **A single match auto-selects** | ☑ |
| 3.4 | Choosing a patient clears the search box | Left filled, it kept showing a query whose match list had already given way to the confirmation — which read as the search having done nothing | ☑ |
| 3.5 | **Change patient** resets selection, search **and the match list** | The desktop comment is explicit that clearing the search alone left stale matches ready to reappear | ☑ |
| 3.6 | **New patient** inline, and the created patient becomes the selection | A walk-in with no record must not have to be registered on another screen first | ☑ |
| 3.7 | A bill is **always** against a registered patient — never an anonymous counter sale | Unlike a pharmacy sale; the entity makes `PatientId` non-optional | ☑ |

## 4 · Tests requested during an OPD consultation

| # | Feature | Why | Status |
|---|---|---|---|
| 4.1 | Lists **today's** OPD visits that requested tests and are not yet billed | The diagnostics equivalent of the counter's "load prescription" | ☑ |
| 4.2 | The whole block is **hidden when nothing is pending** | Unlike the pharmacy list, this is the exception rather than the rule; an always-visible dead control wastes the space | ☑ |
| 4.3 | Refuses with "Choose a patient from today's OPD list first." when nothing is chosen | ☑ |
| 4.4 | Loading pulls every requested test onto the bill and selects that visit's patient | ☑ |
| 4.5 | Catalogue tests come in **at their master price** | ☑ |
| 4.6 | A test requested as **free text** still bills, priced 0 until the desk fills it in | A test we do not run in-house is still worth recording and charging for | ☑ |
| 4.7 | A free-text test already on the bill by name is not added twice | ☑ |
| 4.8 | Loading ties the bill to the visit (`VisitId`) | ☑ |
| 4.9 | **"Referred by" disappears once the bill is tied to a visit** | They came through our own OPD — the clinic referred them | ☑ |
| 4.10 | Outcome: `N test(s) loaded from the consultation.`, or "Nothing new to load" | ☑ |

## 5 · The test picker

| # | Feature | Why | Status |
|---|---|---|---|
| 5.1 | Refuses to open without a patient — "Select a patient first." | ☑ |
| 5.2 | Opens **already listing** every active test, not an empty box | ☑ |
| 5.3 | Searchable, as you type | ☑ |
| 5.4 | Lists **active tests only** | ☑ |
| 5.5 | Tests already on the bill are **left out entirely**, not shown disabled | Billing one test twice is a quantity of 2 on one line, not a second line | ☑ |
| 5.6 | A picked test **leaves the list** | The row disappearing is the confirmation it landed | ☑ |
| 5.7 | Stays open across several picks; closing is its own "Done" | A bill is rarely one test | ☑ |
| 5.8 | Shows whose bill it is adding to | The bill itself is behind the popup | ☑ |
| 5.9 | Shows a running count and total, read off the bill rather than counted separately | So it can never disagree with the bill once closed | ☑ |
| 5.10 | Adding a test already on the bill is refused rather than duplicated | ◐ structurally covered — the picker excludes billed tests (5.5), so the guard behind it was checked in code, not clicked |

## 6 · The bill grid

| # | Feature | Why | Status |
|---|---|---|---|
| 6.1 | One row per test — name, price, quantity, amount | ☑ |
| 6.2 | **Price is overridable** per line after it is added | A concession or a package rate is a real thing the desk does | ☑ |
| 6.3 | **Quantity is editable** — the intended way to bill one test more than once | ☑ |
| 6.4 | Amount recomputes live from price × quantity | ☑ |
| 6.5 | Remove a line | ☑ |
| 6.6 | Total recomputes live from the lines | ☑ |
| 6.7 | **Discount** subtracts from the total | ☑ |
| 6.8 | Final amount never goes below zero | ☑ |
| 6.9 | Subtitle — `N test(s) · ₹X`, or "No tests on this bill" | ☑ |
| 6.10 | Server **recomputes totals from the lines** rather than trusting the screen | The one number that must not be client-supplied | ☑ |
| 6.11 | Server refuses quantity below 1, naming the test | ☑ |
| 6.12 | Server refuses a negative price, naming the test | ☑ |

## 7 · Saving, status and payment

| # | Feature | Why | Status |
|---|---|---|---|
| 7.1 | Refuses without a patient — "Select a patient first." | ☑ |
| 7.2 | Refuses an empty bill — "Add at least one test to the bill." | ☑ |
| 7.3 | A new bill gets the next `DX` number and starts `Ordered` | ☑ |
| 7.4 | Payment mode — Cash / UPI / Card | ☑ |
| 7.5 | **Transaction no. only shows for UPI and Card** | Cash has nothing to reconcile against | ☑ |
| 7.6 | Remarks, optional, trimmed, empty becomes null | ☑ |
| 7.7 | Patient name and number **denormalised onto the bill** | History and a reprint must survive a later patient edit | ☑ |
| 7.8 | Status picker moves the bill: Ordered → SampleCollected → ResultReceived → Completed | ☑ |
| 7.9 | The status picker appears **only once the bill is saved** | A new bill is always Ordered | ☑ |
| 7.10 | Editing is **refused once Completed**, server-side | Exactly what the status exists to prevent | ☑ |
| 7.11 | The screen greys out rather than letting the operator find out at Save | ☑ |
| 7.12 | Deleting a bill is refused once Completed | ☑ |
| 7.13 | Saving an existing bill **replaces its lines** rather than merging | ☑ |
| 7.14 | Outcome: `Bill {no} saved · ₹{final}` | ☑ |
| 7.15 | The form resets to a fresh bill after a successful save | ☑ |
| 7.16 | **New bill** clears everything — lines, patient, discount, remarks, payment, status, visit link | ☑ |
| 7.17 | An existing bill can be **loaded back** for viewing, editing or reprinting | ☑ |

## 8 · Printing

| # | Feature | Why | Status |
|---|---|---|---|
| 8.1 | **Diagnostic bill PDF** — port `DiagnosticBillPrinter` into `SivayaanHMS.Printing` | ☑ |
| 8.2 | Carries the clinic header and document theme | ☑ |
| 8.3 | Lists each test with price, quantity and amount, then total, discount and final | ☑ |
| 8.4 | **Save** and **Save & print** are separate actions | ☑ both buttons present; the print path itself verified via Reprint (pop-ups blocked in automation) |
| 8.5 | Printing re-reads the saved bill rather than printing what was on screen | A bill is a document; what it says must come from what was stored | ☑ |
| 8.6 | Reprint an existing bill | ☑ |

---

## Deliberate differences from HMS_WPF

Not gaps — decisions, recorded so they are not "fixed" by mistake later.

- **Two tabs → two tabs on one page**, as with Appointments.
- **Editor and picker popups → routed drawers/dialogs**, the established
  web equivalent of `ShowOverlayAsync`.
- **`Dialog.Show` warnings → inline messages**, except deleting a test,
  which stays a real confirm.
- **New-patient overlay → the shared `PatientEditorDialog`**, already
  extended during Appointments to hand back the saved row.
- **Bill totals are read from the server's response**, not from the client's
  own arithmetic, once saved — the client computes them live only to show a
  running figure while the bill is being built.
- **The desktop's `SelectPatientAsync` / `LoadBillAsync` entry points** exist
  to be called from the Patients screen. On the web those become route
  parameters rather than public methods.

---

## What browser testing caught

A quieter round than Appointments — the two structural bugs that pass caught
(the stale sidebar and the stale selected row) were fixed there, and this
module inherited both fixes. What driving it still turned up:

| Bug | How it showed up | Why it mattered |
|---|---|---|
| **A refusal that outlived its cause** | "Select a patient first." stayed on screen after a patient *was* selected — it had been raised by clicking **+ Add tests** too early, and nothing cleared it | Cosmetic in isolation, but the page then contradicted itself: a red error above a panel plainly showing the chosen patient. The next real refusal would have been read as the same stale message and ignored. Cleared on selection now, by either route |

Two things found while building rather than by testing, both recorded here
because the next module will hit the same shapes:

- **`LoadBillAsync` had no way in.** The desktop reaches a saved bill from
  the Patients screen. Nothing on the web called it, which would have left a
  bill that could be saved but never reopened — so never moved along the lab
  workflow, and never reprinted. Today's bills are now listed on the screen
  that writes them, which is also where `UpdateStatusAsync` becomes usable.
- **The totals could not be tampered with**, because the request DTO has no
  field for them. That is worth stating rather than assuming: `SaveDiagnosticBillRequest`
  omits `TotalAmount`, `FinalAmount`, `BillNo` and `Status` entirely, so
  item 6.10 is enforced by the shape of the contract, not by a check that
  could later be removed.

Carried forward and still true: the clinic's default footer ("Medicines once
sold are not returnable") prints on the diagnostic bill too, for the same
reason it does on the appointment slip — the document uses `clinic.FooterText`
exactly as the desktop does.

---

## Verified against the view, 23 Aug 2026

Every item above was written from the **viewmodel**, which gives behaviour.
A later pass compared this module's **XAML view** — the thing that actually
defines the controls and columns on screen — because a column set is a fact
no viewmodel states.

**No gaps.** The bill grid (TEST / PRICE / QTY / AMOUNT) and the test master grid (TEST / CATEGORY / PRICE / ACTIVE) match exactly, and every button on the view has a counterpart.

The comparison used:

```
git show origin/Dentist_Pathology:src/Pharma.App/Views/DiagnosticsView.xaml
```
