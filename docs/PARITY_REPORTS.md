# Reports — feature parity with HMS_WPF

Every behaviour the desktop has on this screen, enumerated from the source
(not from memory), with its port status.

Derived from: `ReportsViewModel` (489 lines), `ReportKind` / `ReportNaming`
(93), `ReportPdfBuilder` (350), `ReportExcelBuilder` (462), and
`StockRegisterCalculator` (already ported).

> **Spec source.** `ReportsViewModel.cs` is identical on HMS_WPF's `main` and
> on `origin/Dentist_Pathology` (489 lines both) — no ambiguity here.

Status: ☑ done · ◻ not started · ◐ partial

**All 51 items are ☑ as of 23 Aug 2026**, driven against the running app.
Five were added by the XAML pass recorded at the end of this file.

---

## 1 · The tabs

| # | Feature | Why it exists | Status |
|---|---|---|---|
| 1.1 | **Ten** tabs in a fixed order: Day Book, GST Summary, OPD Register, Expiring Soon, Part Packs, Stock to Reconcile, Low Stock, Stock Register, Schedule H1, Diagnostics | ☑ |
| 1.2 | **Part Packs, Stock to Reconcile and Diagnostics hold their place with no export**, rather than being left out | The desktop's `TabOrder` array pads them with `None` precisely so inserting or reordering a tab can never point an export at the wrong report | ☑ |
| 1.3 | Each tab shows only the filters it actually reads | A From/To pair on the day book would imply a range it does not use | ☑ |

## 2 · Filters

| # | Feature | Why | Status |
|---|---|---|---|
| 2.1 | A single **Date** picker drives day book, OPD register, expiring and low stock | ☑ |
| 2.2 | A **From/To** range drives GST summary and Schedule H1 | That is how an inspector asks for the H1 register | ☑ |
| 2.3 | A backwards range is **quietly swapped**, not refused | It is a slip, not an error worth a dialog | ☑ |
| 2.4 | Expiring window — 30 / 60 / 90 / 180 days, default 90 | ☑ 30d showed only the expired batch; 180d picked up the one due in October |
| 2.5 | Stock Register **search** on medicine name or batch number | The two fields staff actually recognise a pack by | ☑ |
| 2.6 | Stock Register **Include zero stock** toggle | ☑ proved by zeroing a batch: 3 rows without, 4 with |
| 2.7 | A filtered Stock Register's **totals describe the filtered rows**, not the whole shelf | A total that silently counts rows you cannot see is worse than no total | ☑ |
| 2.8 | Day book **bill search across every date** | A walk-in coming back for a copy rarely remembers which day they bought on, only the name or the number | ☑ |

## 3 · Day Book

| # | Feature | Why | Status |
|---|---|---|---|
| 3.1 | **Completed bills only** | A cancelled or returned sale is not revenue, and leaving it in would double count against the cards, which already exclude it | ☑ |
| 3.2 | Columns: bill no., time, customer, **doctor**, **items**, taxable, CGST, SGST, net, mode | ☑ |
| 3.3 | Newest first | ☑ |
| 3.4 | Summary cards: collected, split cash / UPI | ☑ |
| 3.5 | Card for GST within the collected total | ☑ |
| 3.6 | **Consultation fees counted separately from pharmacy**, never mixed in | A consultation is a service, not a taxable supply of goods | ☑ ₹300 shown beside ₹1150, not inside it |
| 3.7 | Patients-seen count, excluding cancellations | ☑ |
| 3.8 | Totals row: taxable, CGST, SGST, net collected | ☑ taxable 1095.24 + 27.38 + 27.38 = 1150.00 exactly |

## 4 · GST Summary

| # | Feature | Why | Status |
|---|---|---|---|
| 4.1 | Grouped **per slab, from sale items** — not per bill | One bill can carry 5% and 12% lines at once, which a per-bill split could not separate | ☑ |
| 4.2 | Slabs in ascending rate order | ☑ |
| 4.3 | CGST and SGST are **half each, with the remainder absorbed** so the two always sum to exactly the GST collected | Rounding both halves independently could leave a paisa unaccounted for | ☑ |
| 4.4 | Grand totals across slabs | ☑ |

## 5 · OPD Register

| # | Feature | Why | Status |
|---|---|---|---|
| 5.1 | **Visit no.**, token, time, patient, **age**, **gender**, doctor, status, fee, paid, receipt no. | ☑ |
| 5.2 | Ordered by token | ☑ |
| 5.3 | Totals: collected, patients seen | ☑ |

## 6 · Expiring Soon

| # | Feature | Why | Status |
|---|---|---|---|
| 6.1 | Batches expiring within the chosen window | ☑ |
| 6.2 | **Already-expired stock says "Expired" in its own column** | Read out in words rather than left to a red tint to say alone — a colour-blind or low-vision reader gets the same signal, and a printed report has no tint at all | ☑ |
| 6.3 | The row is also emphasised | Reinforcement, never the only signal | ☑ |
| 6.4 | Soonest expiry first | ☑ |
| 6.5 | Value at MRP per row and in total | What walking away from it actually costs | ☑ |
| 6.6 | **`RETURNABLE`** — sealed packs against loose units to write off | Only sealed packs go back to the distributor | ☑ reads "2 sealed + 4 loose to write off" |
| 6.7 | **`SUPPLIER`** and **`DAYS REMAINING`** | Who to claim from, and how long there is to do it | ☑ |

## 7 · The stock tabs

| # | Feature | Why | Status |
|---|---|---|---|
| 7.1 | **Part Packs** — tail ends of opened strips, less than a full pack left | They expire where they sit unless somebody pushes them | ☑ |
| 7.2 | **Stock to Reconcile** — provisional batches put on the shelf with no supplier bill behind them | Purchases will not tie out against sales until each is matched to the real bill, so the list is the reconciliation worklist | ☑ |
| 7.3 | **Low Stock** — at or below reorder level, with how far short | ☑ |
| 7.4 | **Stock Register** — every batch on the shelf, a **live snapshot** not tied to any date picker | ☑ its date label reads "As of {now}", not the Date picker's value |
| 7.5 | Stock Register totals: medicines, batches, units, value at cost, value at MRP | ☑ 8 × ₹900 = ₹7,200 and 8 × ₹1,150 = ₹9,200 |

## 8 · Schedule H1 Register

| # | Feature | Why | Status |
|---|---|---|---|
| 8.1 | Date, bill, medicine, batch, quantity, patient, prescriber, over a range | A statutory record of who was given which H1 drug on whose prescription | ☑ |
| 8.2 | Totals: entries and units dispensed | ☑ |

## 9 · Export

| # | Feature | Why | Status |
|---|---|---|---|
| 9.1 | **Export PDF** for every report except Stock Register | ☑ |
| 9.2 | **Stock Register is Excel-only** | A wide, analysis-oriented dump, not a printable statement | ☑ PDF button disabled, and the endpoint refuses too |
| 9.3 | **Export Excel** for every exportable report | ☑ |
| 9.4 | Both exports are **disabled on the two placeholder tabs** | ☑ |
| 9.5 | Both are **disabled when the report is empty**, and the endpoint refuses with "No data available to export." | ☑ |
| 9.6 | Filenames follow `ReportNaming` — `DayBook_2026-08-22.pdf`, `StockRegister_2026-08-23.xlsx` | ☑ |
| 9.7 | A range-based report's filename carries **both ends** when they differ | ☑ |
| 9.8 | Stock Register's filename uses **today**, not the unrelated Date picker | ☑ |
| 9.9 | The PDF carries **no letterhead** | That belongs on what a patient is handed. These are the clinic's own working reports, and a letterhead on every page of a twenty-page register is paper and toner for nothing | ☑ |
| 9.10 | PDF is landscape A4 with page numbers and a generated-at stamp | ☑ |
| 9.11 | The workbook writes **typed cells** — money, integer and date, not text that looks like them | The PDF is for reading; the workbook is for working with, and a column of text will not sum | ☑ verified in the XML: dates as serials, money as bare numbers with a `"₹"#,##0.00` format |
| 9.12 | The workbook has a frozen header row and an autofilter | ☑ |
| 9.13 | Columns sized to content | ☑ |
| 9.14 | The export always matches what is on screen | ☑ by construction — see below |
| 9.15 | **Reprint the selected row's document**, marked duplicate — a bill from the day book, a receipt from the OPD register, a diagnostic bill from Diagnostics | ☑ the button re-labels per report, and an unpaid visit offers nothing |
| 9.16 | **Enter submits the bill search**, as the view's `KeyBinding` does | ☑ |

---

## Deliberate differences from HMS_WPF

- **One `ReportTable`, three renderers.** The desktop has a bespoke builder
  per report for the screen, the PDF and the workbook — nine reports × three
  surfaces — and a comment on `ReportNaming` explaining that the naming is
  shared "so both always agree with what is on screen". Here each report is
  built **once** into a `ReportTable` (columns, rows, totals) and handed to
  whichever renderer is asked for. They cannot disagree, because there is
  only one set of rows. Item 9.14 is therefore structural rather than
  something to keep re-checking.
- **The rupee sign in Excel, "Rs." in the PDF.** Excel handles Unicode
  properly; the PDF's embedded font subset has no ₹, exactly as the
  Pathology Lab port found for the en dash. Each surface uses what it can
  actually render.
- **`ClosedXML` added as a dependency** to `SivayaanHMS.Printing`, which is
  what the desktop uses for the same job. A CSV would have avoided the
  dependency but would have thrown away the typed cells that are the whole
  reason to offer a workbook at all.
- **Excel downloads, PDF opens.** `openPdf` hands bytes to the browser's
  viewer, which is right for a PDF and useless for an .xlsx, so a
  `downloadFile` helper was added alongside it. Both send the bearer token;
  neither can be a plain navigation.
- **Diagnostics reporting is an endpoint without a tab.** The desktop has a
  tenth Diagnostics tab with no export. `GET /api/reports/diagnostics`
  returns today's bills, revenue by day and the top fifteen tests over the
  range; the screen for it is owed.

---

## What browser testing caught

No defects. The arithmetic and the export rules were the parts worth
doubting and both hold.

The GST split is the one to have checked: a single 5% slab on a ₹1,150 bill
produced taxable ₹1,095.24, CGST ₹27.38 and SGST ₹27.38 — summing to exactly
₹1,150.00 with nothing lost to rounding, which is the reason the half is
taken away-from-zero and the remainder absorbed rather than rounding both
sides independently.

Two behaviours were confirmed by making them fail first rather than assuming
them:

- **The Include-zero-stock toggle** produced identical output on both
  settings, which looked like a dead control. It was not: there simply were
  no zero-quantity batches. Zeroing one showed 3 rows without and 4 with.
  Worth recording because "the toggle did nothing" was the wrong conclusion
  and was one step away from being written down as fact.
- **The typed Excel cells** appeared absent on a first inspection of the
  sheet XML. The regex was wrong — ClosedXML writes the `x:` namespace
  prefix. The cells were correctly typed all along: `46568` as a date serial
  resolving to 30 June 2027, the batch's real expiry.

One environment note carried forward from the Dashboard pass: the browser
automation's `navigate` tool drops the path and lands on the SPA root, so
routes are reached by clicking the real nav link. The session also does not
survive a server restart, so signing back in is part of each verification
round.

---

## Correction — the XAML pass, 23 Aug 2026

Every item above was originally derived from the **viewmodel**, which gives
behaviour. A second pass compared the **views** (`*.xaml`), which is what
actually defines the controls and columns on screen. That found gaps this
checklist had not been written to catch, because a column set is a
view-level fact the viewmodel never states.

What was missing and has now been added:

| Report | Columns that were missing |
|---|---|
| Day Book | `DOCTOR`, `ITEMS` |
| OPD Register | `VISIT NO`, `AGE`, `GENDER` |
| Expiring Soon | `RETURNABLE`, `SUPPLIER`, `DAYS REMAINING` |
| Stock to Reconcile | `MRP`, `RATE PAID`, `SUPPLIER`, `THEIR BILL` |
| Part Packs | `MRP` |
| Low Stock | `PACK` |
| Stock Register | `MANUFACTURER`, `PACK`, `RACK`, `REORDER LEVEL`, `SHORTAGE` |

Two of those were more than cosmetic. **`RETURNABLE`** renders
`Batch.Returnable`, which reads "2 sealed + 4 loose to write off" — only
sealed packs go back to the distributor, and without that column the report
says "24 left" and leaves the pharmacist to work out what is actually
claimable. **`SUPPLIER` / `THEIR BILL`** on Stock to Reconcile are what the
entity's own comment calls out: without them the list "can show that
something needs a bill but not whose, which is most of the work of
reconciling".

Also added, all present on the desktop and absent here:

- **The Diagnostics tab** — the desktop's tenth. The endpoint existed and was
  recorded as owed; the tab, its three grids and its own From/To range are
  now built.
- **Reprint from a report row** — `ReprintBillCommand`,
  `ReprintReceiptCommand` and `ReprintDiagnosticBillCommand` are three
  viewmodel commands that were read during the port and never built. Rows
  now carry an optional record id and the button re-labels itself per report.
  An unpaid OPD visit carries no id, so it cannot offer a receipt that does
  not exist.
- **Enter submits the bill search**, matching the view's `KeyBinding`.
