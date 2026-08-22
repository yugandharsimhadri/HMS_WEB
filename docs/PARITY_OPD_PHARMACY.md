# OPD & Pharmacy — feature parity with HMS_WPF

Every behaviour the desktop has in these two modules, enumerated from the
source (not from memory), with its port status. This is the acceptance
checklist: HMS_WEB is not done until every ☑ below is genuinely ☑.

Derived from: `OpdViewModel`, `BookVisitViewModel`, `CollectFeeViewModel`,
`ConsultationViewModel`, `PatientsViewModel`, `PatientEditorViewModel`,
`SaleViewModel`, `ProductsViewModel`, `MedicineEditorViewModel`,
`InventoryViewModel`, `ReceiveStockViewModel`, `QuickStockViewModel`,
`CorrectStockViewModel`, `EditQuantityViewModel`, `BillPrinter`,
`PrescriptionPrinter`, `FeeReceiptDocument`.

Status: ☑ done · ◻ not started · ◐ partial

---

## 1 · OPD queue (`OpdViewModel`)

| # | Feature | Why it exists | Status |
|---|---|---|---|
| 1.1 | **Waiting / Completed split** — two columns, not one list | The desk asks "who is left", not "who came" | ☑ |
| 1.2 | **Doctor tabs** — one tab per doctor plus "All" | Repeating the doctor on every row was the bulk of the old screen's duplication | ☑ |
| 1.3 | **Session filter** — Full day / Morning / Evening, from clinic hours | Indian clinics run two sittings; "who is left this evening" is the real question | ☑ |
| 1.4 | **Hidden count** — "N more today outside these hours" | A 2pm walk-in belongs to neither sitting and must not silently vanish | ☑ |
| 1.5 | **Subtitle line** — `N waiting · N completed · date [· session, hours]` | ☑ |
| 1.6 | **Date picker** — any day, not just today | ☑ |
| 1.7 | **Tiles vs Rows layout** — from Settings → `QueueLayout` | Short list reads better as tiles; busy day fits more as rows | ☑ |
| 1.8 | Action: **Arrived** (Booked → Waiting) | ☑ |
| 1.9 | Action: **Consult** (→ InConsultation, opens consultation) | ☑ |
| 1.10 | Action: **Complete** (→ Completed) | ☑ |
| 1.11 | Action: **Reopen** (Completed → Waiting) | ☑ |
| 1.12 | Action: **Cancel** with confirmation, refused once paid/completed | Refusal is server-side too (`CanCancel`) | ☑ |
| 1.13 | Action: **Collect fee** → opens fee dialog (not instant) | A receipt is numbered as written; a wrong fee is reversed on paper | ☑ |
| 1.14 | Action: **Print receipt** (duplicate) | ☑ |
| 1.15 | Action: **Print prescription** | ☑ |
| 1.16 | Already-paid guard: "use the receipt button to reprint" | ☑ |

## 2 · Book a visit (`BookVisitViewModel`)

| # | Feature | Why | Status |
|---|---|---|---|
| 2.1 | Patient search by name **or phone** | ☑ |
| 2.2 | **Family disambiguation** — "N people on this number, select which one" | Siblings share a phone; auto-picking sends the wrong child in | ☑ |
| 2.3 | **Inline new-patient** when search finds nobody | ☑ |
| 2.4 | Auto-fills name **or** phone into the new-patient form from what was typed | ☑ |
| 2.5 | Refuses to book when matches exist but none selected | Creating a duplicate child record instead of picking | ☑ |
| 2.6 | Doctor picker, defaults to the active doctor tab | ☑ |
| 2.7 | **Fee auto-fills** from doctor's consultation fee | ☑ |
| 2.8 | Time field (defaults to now), booked onto the chosen date | ☑ |
| 2.9 | Complaint field | ☑ |
| 2.10 | Clear form / Cancel | ☑ |
| 2.11 | Outcome message: "Token N booked for X" | ☑ |

## 3 · Collect fee (`CollectFeeViewModel`)

| # | Feature | Why | Status |
|---|---|---|---|
| 3.1 | Editable **amount** (concession, rounding) | ☑ |
| 3.2 | **Payment mode** Cash/UPI/Card | ☑ |
| 3.3 | **Transaction no.** shown only for UPI/Card | Cash has nothing to reconcile against | ☑ |
| 3.4 | **Fee-changed note** — "Booked at ₹X. This receipt will say ₹Y" | A concession is a decision; a typo is not | ☑ |
| 3.5 | **Confirmation** naming amount, mode and patient | Three things that get mixed up with two people at the desk | ☑ |
| 3.6 | **Print receipt** toggle (on by default) | ☑ |
| 3.7 | Header: token + patient; summary: age/sex/time + doctor | ☑ |
| 3.8 | Money shown to 2dp always (`300` → `300.00`) | ☑ |

## 4 · Consultation (`ConsultationViewModel`)

| # | Feature | Why | Status |
|---|---|---|---|
| 4.1 | Header: token · patient · age/sex · doctor | ☑ |
| 4.2 | Complaint / Diagnosis / Notes | ☑ |
| 4.3 | **Vitals**: weight, BP, temp, height, heart rate, SpO2 | ☑ |
| 4.4 | Editable fee | ☑ |
| 4.5 | Follow-up date | ☑ |
| 4.6 | **Medicine autocomplete** over the catalogue (name + manufacturer) | ☑ |
| 4.7 | **Free-text medicines allowed** — never added to our catalogue | Parent buys it outside | ☑ |
| 4.8 | Stock hint on picked medicine ("N in stock" / "out of stock") | ☑ |
| 4.9 | **Dose pickers** M-A-N with `0, 1/4, 1/2, 1, 2` | Paediatric halves; picking rules out "1-0-l" | ☑ |
| 4.10 | **Auto quantity** from frequency × days (`DoseMath`) | ☑ |
| 4.11 | **Course hint** in packs ("6 units · 1 × 10 TAB minus 4") | Doctor writes units; pharmacy hands strips | ☑ |
| 4.12 | Per-line instructions | ☑ |
| 4.13 | Entry row **fully clears** after Add (dose and days included) | A leftover dose reads as chosen for the next medicine | ☑ |
| 4.14 | **Investigations** — test autocomplete + free text, dedup | ☑ |
| 4.15 | Save (stay) / Complete (close) / Print | ☑ |
| 4.16 | **Unsaved-changes guard** on close (snapshot comparison) | ☑ |

## 5 · Patients (`PatientsViewModel`, `PatientEditorViewModel`)

| # | Feature | Status |
|---|---|---|
| 5.1 | Search, list | ☑ |
| 5.2 | Full editor: name, phone, age, **DOB**, blood group, guardian, gender, address, **allergies** | ☑ |
| 5.3 | **DOB drives age** and locks the age box | ☑ |
| 5.4 | **Guardian shown only for minors** (age < 18) | ☑ |
| 5.5 | Requires name **and** (DOB or age) | ☑ |
| 5.6 | **Remove patient**, refused when visits exist | ☑ |
| 5.7 | **Visit history** per patient | ☑ |
| 5.8 | **Bill history** per patient | ☑ |
| 5.9 | Reprint prescription / receipt / bill from history | ☑ |

## 6 · Pharmacy counter (`SaleViewModel`)

| # | Feature | Why | Status |
|---|---|---|---|
| 6.1 | Live medicine search (filters as typed) | ☑ |
| 6.2 | Auto-select on single match | ☑ |
| 6.3 | **Quantity unit picker** — "tablets" vs "strips of 15" | "9" alone is what turned 9 tablets into 9 strips | ☑ |
| 6.4 | **Remembers unit per medicine** for the session | ☑ |
| 6.5 | Selected summary: stock + unit price each | ☑ |
| 6.6 | **Loose-sale refusal** — sealed packs must go whole, suggests the round number | ☑ |
| 6.7 | Nearest-expiry allocation, **auto-splits across batches** | ☑ |
| 6.8 | **Re-lays lines on re-add** (no double-count against one batch) | ☑ |
| 6.9 | **Edit quantity** popup with live packs + amount preview | ☑ |
| 6.10 | **Re-allocates on quantity edit** (may span a new batch, or give stock back) | ☑ |
| 6.11 | **Expiry warning** when soonest batch ≤ 30 days | Handing over a fortnight of shelf life without saying so | ☑ |
| 6.12 | Multi-batch / multi-price notice | ☑ |
| 6.13 | **Schedule H1 → prescriber required** before saving | Statutory register kept 3 years | ☑ |
| 6.14 | **Load prescription** from today's OPD visits | ☑ |
| 6.15 | Load reports missing / short items separately from status | Empty bill reads as "nothing happened" | ☑ |
| 6.16 | **Quick stock** from the counter (provisional) | Don't send the operator away mid-queue | ☑ |
| 6.17 | Customer name, doctor name, payment mode, transaction no. | ☑ |
| 6.18 | Transaction no. only for UPI/Card | ☑ |
| 6.19 | **Live totals**: gross, discount, taxable, CGST, SGST, round-off, net | ☑ |
| 6.20 | **GST off entirely when pharmacy not registered** | Can't issue a tax invoice you aren't registered for | ☑ |
| 6.21 | Save / **Save & print** | ☑ |
| 6.22 | New bill (clears everything) | ☑ |
| 6.23 | Links bill to patient + visit when loaded from OPD | ☑ |

## 7 · Medicines catalogue (`ProductsViewModel`, `MedicineEditorViewModel`)

| # | Feature | Why | Status |
|---|---|---|---|
| 7.1 | Catalogue list + search | ☑ |
| 7.2 | Full editor: name, generic, manufacturer, composition, storage, pack size, HSN, GST, schedule, rack, reorder level, active, units/pack, loose sale, dispensing unit | ☑ |
| 7.3 | **Pack size auto-fills units-per-pack** for new medicines only | "15 TAB" already says fifteen | ☑ |
| 7.4 | Never auto-changes units/pack on an **existing** medicine | Stock is already counted against it | ☑ |
| 7.5 | **Pack mismatch warning** ("says 15 but set to 1") | The one combination that silently overcharges everyone | ☑ |
| 7.6 | **Duplicate detection** → offer to open the existing one | Nobody adds a duplicate on purpose | ☑ |
| 7.7 | **Offer to re-count stock** when units/pack changes | Batches keep the pack size they arrived under | ☑ |

## 8 · Inventory (`InventoryViewModel`)

| # | Feature | Status |
|---|---|---|
| 8.1 | Medicine search, batch list per medicine | ☑ |
| 8.2 | **Pack-mismatch warning** on the page | ☑ |
| 8.3 | **Stale-batch warning** (received at a different pack size) | ☑ |
| 8.4 | **Receive stock** — batch, expiry, packs, free packs, rate, MRP, supplier, invoice no. | ☑ |
| 8.5 | Receive: **intake preview** ("10 packs × 15 = 150 tablets onto the shelf") | ☑ |
| 8.6 | Receive: validation — batch, packs, MRP, future expiry | ☑ |
| 8.7 | **Correct stock** — batch, corrected qty, reason, notes | ☑ |
| 8.8 | Correct: pre-fills current quantity | ☑ |
| 8.9 | **Adjustment trail** (last 100) | ☑ |
| 8.10 | Low stock / expiring lists | ☑ |

## 9 · Printing — server-side PDF

| # | Document | Status |
|---|---|---|
| 9.1 | **Pharmacy bill** — GST summary by slab, amount in words, multi-batch grouping, duplicate watermark | ☑ |
| 9.2 | **Prescription** — clinic header, doctor credentials, 3-row identity grid, vitals, Rx table, instructions, investigations, advice, follow-up, signature | ☑ |
| 9.3 | **Fee receipt** — identity grid, particulars, received amount, in words, duplicate | ☑ |

---

## Deliberate differences from HMS_WPF

Not gaps — decisions, recorded so they aren't "fixed" by mistake later.

- **Modal dialogs → routed pages/drawers.** The desktop shows forms as an
  overlay over the shell because a separate window can get lost behind
  another application. On the web the equivalent problem doesn't exist;
  a drawer or route is the native idiom and keeps the back button working.
- **`Dialog.Show` warnings → inline messages.** A browser `alert()` blocks
  the whole tab and can be permanently suppressed by the user; the desktop's
  MessageBox cannot. Warnings that must be *acknowledged* (cancel a visit,
  H1 prescriber, duplicate medicine) stay as real confirm dialogs.
- **Print preview → PDF in a new tab.** `PrintService.Preview` is a WPF
  document viewer. The browser's own PDF viewer is the direct equivalent and
  already has print/save.
- **`Environment.UserName` → the signed-in user.** The desktop stamps the
  Windows account; the web has a real authenticated user, which is better.
