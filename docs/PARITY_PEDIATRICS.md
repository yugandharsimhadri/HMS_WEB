# Pediatrics — feature parity with HMS_WPF

Every behaviour the desktop has in this module, enumerated from the source
(not from memory), with its port status. This is the acceptance checklist:
HMS_WEB is not done until every ☑ below is genuinely ☑.

Derived from: `PediatricsViewModel` (647 lines),
`RecordVaccinationViewModel` (162), `RecordGrowthViewModel` (41),
`AddProcedureLineViewModel` (56), `PediatricsService` and
`ProcedureBillsService` (already ported), `GrowthReference`,
`ProcedureBillDocument` and `VaccinationHistoryDocument`.

> **Spec source.** `PediatricsViewModel.cs` exists only on
> `origin/Dentist_Pathology`, as with Appointments. Read it without
> switching HMS_WPF's working tree:
>
> ```
> git show origin/Dentist_Pathology:src/Pharma.App/ViewModels/PediatricsViewModel.cs
> ```

Status: ☑ done · ◻ not started · ◐ partial

**65 of 67 items are ☑ as of 22 Aug 2026** (two columns added by the XAML pass on 23 Aug — see the correction at the end), each driven against the running
app in a browser — not just compiled. The two remaining are ◐ and say why.
The bugs that testing surfaced are listed at the end.

---

## What this module deliberately does NOT include

The desktop puts **Vaccine Master** and **Procedure Master** on the shared
`GeneralMasterViewModel` nav destination — configuration, not per-visit
work, and explicitly "never sharing a screen with it". Those screens belong
to the **Masters** module, still to be ported.

So this module ships with the vaccine schedule already seeded
(`VaccineMasterSeeder`, WHO/IAP) and reads it, but does not offer a screen
to edit it. Procedures are a sharper edge: `DentistProcedureSeeder` seeds
Dentist procedures only, so **a fresh tenant has no Pediatrics procedures at
all** and procedure billing has nothing to offer until Masters lands. The
REST endpoints for procedure CRUD are built here (Masters will need them
anyway, and vaccination billing depends on the same bill), but the
management UI is Masters' job. See the closing note.

---

## 1 · Module gating

| # | Feature | Why it exists | Status |
|---|---|---|---|
| 1.1 | Pediatrics is **off by default** (`PediatricsEnabled`) | Not every clinic is a children's clinic | ☑ |
| 1.2 | Nav entry appears only when the module is on | ☑ |
| 1.3 | One patient chosen **in the header, above the tabs** — Growth, Care and Immunization all work on that one person | Billing and vaccination treat the same child on the same visit; asking twice is a chance to get it wrong | ☑ |

## 2 · The shared patient panel

| # | Feature | Why | Status |
|---|---|---|---|
| 2.1 | Search by name **or phone**, at most 20 matches | ☑ |
| 2.2 | Search runs as you type | ☑ |
| 2.3 | **A single match auto-selects** | ☑ |
| 2.4 | Choosing a patient clears the search box | ☑ |
| 2.5 | **Change patient** resets selection, search and matches | ☑ |
| 2.6 | **New patient** inline, and the created patient becomes the selection | ☑ |
| 2.7 | Choosing a patient loads **all four** at once — vaccination history, due vaccines, growth history, immunization card | ☑ |
| 2.8 | Choosing a patient **resets the growth chart to Weight** | The metric shown for the last child may not exist for this one; weight is the measurement most likely to be on file, so it beats landing on a blank chart | ☑ |
| 2.9 | Returning to the screen refreshes a patient already selected | The desktop page is a DI singleton and would otherwise sit stale | ☑ the web page is not a singleton; re-navigating remounts and reloads, so the staleness the desktop guards against cannot arise |

## 3 · Growth

| # | Feature | Why | Status |
|---|---|---|---|
| 3.1 | Growth lives **on the patient tab**, under the patient — not buried in Care with everything billable | Growth is never billed | ☑ |
| 3.2 | History lists every measurement — date, weight, height, head circumference, **BMI** | ☑ |
| 3.3 | **Record growth** popup: date (defaults today), weight, height, head circumference | ☑ |
| 3.4 | Refuses an empty form — "Enter at least one measurement." | All three are optional individually; none of them together is not a measurement | ☑ |
| 3.5 | Recording is **never billed** — unlike vaccination, nothing joins the bill | ☑ structural — growth has its own endpoint and never touches a bill |
| 3.6 | Outcome: "Measurement recorded.", and the history and chart refresh | ☑ |
| 3.7 | Chart plots **expected vs this patient's own** on shared axes | Two lines each stretched to its own range are not comparable | ☑ |
| 3.8 | Metric switch — Weight / Height / Head circumference | ☑ |
| 3.9 | Expected curve from `GrowthReference` — piecewise-linear between named control points, ported verbatim | Approximate teaching milestones, explicitly **not** WHO percentile bands | ☑ |
| 3.10 | Age in months from **date of birth when on file**, falling back to whole-year age | Precision matters most under a year old, which is exactly when DOB is most likely recorded | ☑ |
| 3.11 | X axis runs 0 → the patient's own latest age, capped at `MaxAgeMonths` (144) | Past 12 years this reference stops applying and Pediatrics stops being the right module | ☑ |
| 3.12 | Y range spans both series, padded 10%, with a minimum span so a flat series is not a single line | ☑ |
| 3.13 | Each measurement is marked with a dot, not just the line | ☑ |
| 3.14 | Axis labels — top/bottom value with unit, right-hand age in months | ☑ |
| 3.15 | Measurements missing the selected metric are **skipped**, not plotted as zero | A child with no head-circumference reading has no point, not a point at 0 | ☑ |
| 3.16 | The chart hides itself when there is nothing to plot | ☑ |

## 4 · Care — procedure billing

| # | Feature | Why | Status |
|---|---|---|---|
| 4.1 | Refuses to add without a patient — "Select a patient first." | ☑ |
| 4.2 | **Add procedure** popup: pick a procedure, pick a quantity | ☑ |
| 4.3 | The picker lists **active Pediatrics procedures only** | A Dentist procedure has no business on a pediatric bill | ☑ |
| 4.4 | Refuses without a procedure — "Pick a procedure." and marks the field | ☑ |
| 4.5 | The marker clears the moment one is picked | ☑ |
| 4.6 | Quantity below 1 is coerced to 1 | ☑ |
| 4.7 | The procedure comes in **at its master price** | ☑ |
| 4.8 | A procedure already on the bill is refused — "{name} is already on this bill." | Billing it twice is a quantity, not a second line | ☑ |
| 4.9 | Price and quantity are **editable on the bill grid** afterwards | ☑ |
| 4.10 | Amount recomputes live | ☑ |
| 4.11 | Remove a line | ☑ |
| 4.12 | A **TYPE column** distinguishes Procedure from Vaccine rows | A combined bill must still read clearly at a glance | ☑ |

## 5 · Care — vaccination

| # | Feature | Why | Status |
|---|---|---|---|
| 5.1 | Refuses without a patient — "Select a patient first." | ☑ |
| 5.2 | **Record vaccination** popup: vaccine, date given, site, administered by, and the brand from Pharmacy stock | ☑ |
| 5.3 | Vaccine list is **active Vaccine Master entries** — the clinical "type" (e.g. Hepatitis B dose 2) | ☑ |
| 5.4 | Refuses without a vaccine — "Pick a vaccine." and marks the field | ☑ |
| 5.5 | Brand/batch is **picked from the Pharmacy catalogue**, never retyped | One place stock and pricing live | ☑ |
| 5.6 | Only products **actually in stock** are offered | There is nothing to draw the dose from otherwise | ☑ |
| 5.7 | Refuses without a brand — "Pick the brand given, from Pharmacy stock." | ☑ |
| 5.8 | The price charged is the **nearest-expiry batch's real unit price** — the same figure Pharmacy would quote | ☑ |
| 5.9 | The batch is shown before saving — number and expiry | ☑ |
| 5.10 | There is **no "charge or not" choice** — every dose goes on the bill, at whatever price (0 for a free dose) | A separate billing step is a step the desk can forget | ☑ |
| 5.11 | The dose is added to the bill as a **draft only** — nothing is written to `VaccinationRecord` yet | ☑ |
| 5.12 | Removing the line, or leaving without saving, **drops it entirely** — no record, no bill entry, nothing to undo | A dose is on record only once it is on a saved bill | ☑ |
| 5.13 | The line reads `{vaccine} (dose N)` | ☑ |
| 5.14 | Outcome says it is on the bill and **will be recorded when the bill is saved** | ☑ |
| 5.15 | Date given defaults to today | ☑ |

## 6 · Immunization card

| # | Feature | Why | Status |
|---|---|---|---|
| 6.1 | Every active Vaccine Master dose, checked against what this child has had, with its **schedule age** | ☑ "Birth", "6 weeks" — what makes the card read as a schedule |
| 6.2 | Ordered by `SequenceOrder` then recommended age, so rows cluster by milestone | ☑ |
| 6.3 | Four statuses — Given / Overdue / Due soon / Upcoming | ☑ |
| 6.4 | Recommended date = date of birth + recommended age in days | ☑ |
| 6.5 | With **no date of birth on file**, every not-yet-given row reads Upcoming | There is nothing to compare an age recommendation against | ☑ |
| 6.6 | "Due soon" is a 14-day window | ☑ |
| 6.7 | A dose recorded more than once shows its **most recent** giving | A correction or re-entry should not read as two doses | ☑ |
| 6.8 | Counts per status, plus a total | ☑ |
| 6.9 | **Record** on a row opens the same popup, **pre-selected to that vaccine** | Recording a due dose from the card should not ask the desk to find it again in a dropdown | ☑ |
| 6.10 | Recording from the card still goes **through the bill** — it never bypasses billing | ☑ |
| 6.11 | Card, history and due list all refresh once a bill carrying doses is saved | ☑ |

## 7 · Saving the bill

| # | Feature | Why | Status |
|---|---|---|---|
| 7.1 | Refuses without a patient — "Select a patient first." | ☑ |
| 7.2 | Refuses an empty bill — "Add at least one procedure to the bill." | ☑ |
| 7.3 | A new bill gets the next number and starts `Ordered` | ☑ |
| 7.4 | Payment mode — Cash / UPI / Card | ☑ |
| 7.5 | **Reference no. only for UPI and Card** | ☑ |
| 7.6 | Referred by, optional, trimmed | ☑ |
| 7.7 | Discount subtracts; final never below zero | ☑ |
| 7.8 | Patient name and number **denormalised onto the bill** | ☑ |
| 7.9 | Server **recomputes totals from the lines** | ☑ |
| 7.10 | Server refuses quantity below 1 and negative price, naming the procedure | ☑ |
| 7.11 | Editing is refused once the bill is `Completed` | ☑ |
| 7.12 | **Vaccine drafts are recorded only after the bill saves** | The dose follows the money, not the other way round | ☑ |
| 7.13 | Recording a dose **deducts one unit from the pharmacy batch** | The dose really did leave that batch — one ledger, not a second one Pediatrics keeps on the side | ☑ |
| 7.14 | Refuses to record when the batch has no stock left | ☑ |
| 7.15 | A stock concurrency clash is **retried up to 3 times** rather than failing | Two desks recording from one batch at once is ordinary, not exceptional | ◐ the retry is in the ported service and unchanged; two desks racing one batch was not staged |
| 7.16 | The **next dose due date** is computed from the next Vaccine Master dose and the child's DOB | ☑ |
| 7.17 | Outcome: `Bill {no} saved · ₹{final}` | ☑ |
| 7.18 | The form resets after a successful save | ☑ |
| 7.19 | Drafts are captured **before** the form resets | Otherwise the reset clears the very list about to be recorded | ☑ |

## 8 · Printing

| # | Feature | Why | Status |
|---|---|---|---|
| 8.1 | **Procedure bill PDF** — port `ProcedureBillDocument` | ☑ |
| 8.2 | **Vaccination history PDF** — port `VaccinationHistoryDocument` | The card a parent carries | ☑ |
| 8.3 | Both carry clinic header and document theme | ☑ |
| 8.4 | Print vaccination history refuses without a patient | ◐ the button only renders once a patient is chosen, so the refusal has no way to fire |
| 8.5 | **Save** and **Save & print** are separate actions | ☑ |
| 8.6 | Printing re-reads the saved bill | ☑ |

---

## Deliberate differences from HMS_WPF

- **Popups → dialogs**, as in Diagnostics.
- **`Dialog.Show` warnings → inline messages.**
- **`GrowthReference` ported to TypeScript**, into `frontend/src/clinical/`,
  alongside `gst.ts` and `doseMath.ts` — the chart is drawn client-side, so
  the reference has to live where the chart is, exactly as the GST and dose
  arithmetic already do. The control points are copied verbatim.
- **`ChartGeometry` (WPF path strings) → inline SVG.** The geometry is
  recomputed for SVG rather than ported; a WPF `PathGeometry` string has no
  meaning in a browser. The axes, padding and sampling rules are kept.
- **Vaccine Master and Procedure Master screens are out of scope** — they
  belong to the Masters module, as they do on the desktop. The endpoints
  exist; the screens do not.

---

## What browser testing caught

No new defects this pass. The three structural bugs earlier modules found —
the stale sidebar, the stale selected row, the refusal that outlived its
cause — were all fixed where they were found, and this module inherited
every one. What driving it did do was confirm the two behaviours most likely
to have been ported wrong, and force one design decision into the open:

**The draft-dose rule holds, both ways.** Adding a dose to the bill wrote
*nothing*: still zero `VaccinationRecord` rows and pharmacy stock untouched
at 10. Removing that line dropped it with no trace. Saving a bill carrying a
dose then recorded it *and* moved stock 10 → 9 in the same breath. That is
the whole design — a dose is on record only once it is on a saved bill — and
it is the sort of thing that compiles perfectly while being wired backwards.

**`NextDueOn` is computed, not guessed.** Recording DTwP dose 1 for a child
born 2025-02-10 stamped a next-due of 2025-04-21 — dose 2's 70-day
recommendation applied to that child's own birth date.

**A dose that cannot be recorded needed a decision, and testing forced it.**
Emptying the batch between picking the brand and saving is a real race: the
counter can sell the last vial while the form is open. The bill is already a
financial fact by then, so this cannot be a 4xx and cannot roll back. It also
must not pass silently, or the parent's card is short a dose the bill
charged for. `ProcedureBillResult.Warning` carries it: the save returns 200
with the bill, `vaccinationsRecorded: 0`, and *"Bill PRC00002 saved, but IPV
could not be recorded: No stock left of Pentavac PFS (batch PVX2291)"*.
Verified by actually emptying the batch and saving.

One thing the port had to decide rather than copy: the desktop's
`ChartGeometry` emits WPF `PathGeometry` strings, which mean nothing in a
browser. The geometry is recomputed as SVG, but the rules it encodes — 24
samples, 10% padding, the 0.5 minimum span, the 144-month cap — are the
desktop's. Checked numerically rather than by eye, since the browser pane in
this environment does not composite frames: the patient series runs
(0, 174.17) → (480, 25.34) against the reference's (0, 171.27) → (480,
15.83), i.e. a child tracking just below the expected line, every point in
bounds.

**The gap this module ships with** is stated at the top and worth repeating:
a fresh tenant has **no Pediatrics procedures**, because only Dentist ones
are seeded. Procedure billing therefore has nothing to offer until the
Masters module lands its Procedure Master screen. The endpoints work — the
two procedures used in testing were created through them — but a clinic
cannot create one from the UI yet. That is faithful to where the desktop
puts the screen, not an oversight, but it does mean this module is not
independently sellable in the way Appointments and Diagnostics are.

---

## Correction — the XAML pass, 23 Aug 2026

Every item above was originally derived from the **viewmodel**, which gives
behaviour. A second pass compared the **views** (`*.xaml`), which is what
actually defines the controls and columns on screen. That found gaps this
checklist had not been written to catch, because a column set is a
view-level fact the viewmodel never states.

What was missing and has now been added:

- **`BMI`** on the growth grid. `GrowthMeasurement.BmiValue` was already
  computed and stored on every save and had simply never been surfaced. It
  is deliberately blank when only one of weight or height was taken that
  visit, which is the entity's own stated reason for storing it rather than
  recomputing it.
- **`AGE`** on the immunization card — the schedule age ("Birth", "6 weeks",
  "9 months"). It is what makes the card read as a schedule rather than a
  list, and it is the only column that means anything for a child with no
  date of birth on file, since every dated column is blank for them.
