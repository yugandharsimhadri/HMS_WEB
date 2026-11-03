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

**All 105 items below are ☑ as of 22 Aug 2026.** Each was exercised against
the running app in a browser — not just compiled — and the bugs that testing
surfaced are listed at the end.

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

## 10 · OPD validity window and the review receipt (HMS_WPF `33e2eba`…`ff0c99d`, 9 Sep 2026)

Added to the desktop after the web port was taken from its `Dentist_Pathology`
branch, so the first port missed it. Ported 2 Nov 2026 and exercised end to
end in a browser: a ₹300 fee taken on a doctor set to 7 days opened a window
to the 9th; the same patient booked again came up as a free review with the
fee at ₹0, the Fee button gone and a Slip in its place; the receipt printed
the window and the slip quoted the original receipt number.

| # | Feature | Why | Status |
|---|---|---|---|
| 10.1 | **`Doctor.OpdValidDays`** — days one fee covers; 0 (default) charges every visit | Per doctor because it is the doctor's own money being given away | ☑ |
| 10.2 | **`Doctor.Qualification`** — free-text degrees, printed beside the name | Indian qualifications do not fit a fixed list | ☑ |
| 10.3 | **Doctor editor** — both fields, with a note that says the number back ("One fee covers this doctor for 7 days…") | "7" alone does not say whether reviews just became free | ☑ |
| 10.4 | **`Visit.FreeFollowUpUntil`** stamped once, when money is actually taken; never recomputed | The patient was told a date and it is printed; a later change to the doctor's setting must not revoke or extend it | ☑ |
| 10.5 | **Dated from payment, day-wise** — paid at 9:45 pm on the 1st with 7 days is covered through the whole of the 8th | Nothing in the arithmetic looks at a clock | ☑ |
| 10.6 | **Only a payment opens a window** — a review never re-anchors it | Otherwise a patient returning every sixth day is covered for ever | ☑ |
| 10.7 | **`Visit.FeeWaivedAgainstVisitId`** — a real link to the paying visit, not a flag | So the slip can quote the receipt the money was taken on, months later | ☑ |
| 10.8 | **`FindFeeCoverAsync`** — same patient, same doctor, dated on the visit being booked, inclusive of the last day | A fee paid to one doctor does not buy a visit with their colleague; booking on the 2nd for the 20th is outside a window ending on the 8th | ☑ |
| 10.9 | **Booking** — cover found → fee ₹0 and a banner naming the receipt and the date; the fee box stays editable | A return for a new complaint is usually still chargeable; that judgement is the desk's | ☑ |
| 10.10 | **Appointment check-in** applies the same cover | A patient who booked ahead must not be charged for what a walk-in gets free | ☑ |
| 10.11 | **Queue tile** — "Review · no fee" badge, no Fee button (`CanCollectFee`), still cancellable | A review is a booking with no money on it | ☑ |
| 10.12 | **Receipt for a review** — Visit line "Review", the original receipt's number, ₹0.00 stated, "(review — already paid)", "No fee — already paid on <date>", never stamped DUPLICATE | A receipt with no figure invites the question the figure answers; no new number is burned on a nil amount | ☑ |
| 10.13 | **Receipt that opened a window** — "Free review with Dr X until <date>"; degrees beside the doctor's name; Visit line "New" | The line the desk points at when a parent asks whether they pay again | ☑ |
| 10.14 | **Patients history** — "(review)" label and a Slip button | ☑ |

### Where the web differs from the desktop here, on purpose

- **The server decides what is free, not the client.** The desktop's booking
  viewmodel works out the cover and passes the paying visit's id to the
  service. Over HTTP that would let any caller name a payment and claim a free
  visit, so the booking endpoint takes no such id: it looks the cover up itself
  (`FindCoverForBookingAsync`) from the doctor's setting and the fee in the
  request, and a fee typed back in means "charge this one". Checked by
  sending a forged waiver id inside a live window — ignored.
- **Taking a fee on a review is refused by the service**, not only hidden on
  screen (`collect-fee` answers 400). On the desktop only the viewmodel guards
  it; the web API is reachable by anything with a token.
- **Doctor save leaves the new fields alone when they are absent.** A browser
  tab still running the previous release does not send them, and an unrelated
  edit from it must not blank a doctor's degrees or switch off a window the
  clinic is relying on. The editor always sends both, so clearing is still
  possible (empty string, and 0).

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

---

## What browser testing caught

Every one of these compiled cleanly and would have shipped unnoticed without
being driven through the real app.

| Bug | How it showed up | Why it mattered |
|---|---|---|
| **No CORS policy on the API** | Every cross-origin request blocked before reaching a controller | The frontend could not talk to the backend at all |
| **Circular JSON on `Visit`** | `Visit.Patient ↔ Patient.Visits` threw "possible object cycle detected" | Ported from a desktop app that never serialised its entities; several more bidirectional navigations would have hit it |
| **Booking shifted by the timezone offset** | An 08:58 visit came back reading 03:28 | Client sent UTC, server stores naive clinic-local. The queue would have disagreed with the front desk all day |
| **Invented enum values** | `AdjustmentReason.Damaged` (it is `Breakage`), `DispensingUnit.Injection` (does not exist; `Piece`/`Syrup`/`Moisturizer`/`Soap`/`Others` were missing) | Correcting stock failed with a 400 |
| **Patient edit always failed** | `DbUpdateConcurrencyException` on every save | Client never round-tripped `RowVersion`, so the UPDATE matched zero rows |
| **Mass assignment on four endpoints** | Found while fixing the above | A request could write `TenantId` (moving a patient into another clinic), `IsDeleted`, or a Product's `SearchKey` — the column the duplicate-medicine index is built on |

The GST arithmetic was checked the other way round: the counter's TypeScript
preview and the server's C# were compared on a real bill and agreed to the
paisa (gross 10.02, taxable 8.95, CGST 0.53, SGST 0.54, round-off −0.02, net
10.00). `GstParityTests` pins that contract so a change to one side fails
loudly instead of quietly quoting one number and printing another.

---

## Correction — the XAML pass, 23 Aug 2026

This checklist, like every other, was derived from the **viewmodels**. A
later pass compared the **views** (`*.xaml`), which is what actually defines
the controls and columns on screen. That found gaps on screens marked done
here, because a column set is a view-level fact no viewmodel states.

Fixed:

| Screen | What was missing |
|---|---|
| Patients | **Three whole grids** — diagnostic bills, vaccinations and growth. The desktop shows six histories on this screen; the web showed two |
| Patients → Growth | `AGE (DAYS)` and `BMI` |
| Medicines | `RACK` |
| Inventory → medicines | `MAKER`, `PER PACK`, `RACK` |
| Inventory → corrections | `WAS` and `NOW` as their own columns, plus a signed delta |

The Patients gap was the substantial one. A patient's diagnostic bills,
doses given and growth measurements belong to the person, not to the module
that happened to record them, and Patients is the one screen that reads the
whole person. Each of the three is hidden when its own module has never been
switched on, so a clinic that does not run a lab or see children is not
shown empty tables.

### Still outstanding on these screens

Not fixed, and not to be mistaken for done:

- **Patients → `New diagnostic bill` and `New vaccination`** — two buttons
  that jump to another module pre-filled for this patient. They correspond
  to the desktop's `SelectPatientAsync` entry points, which the Diagnostics
  and Pediatrics parity docs record as "route parameters rather than public
  methods". The routes do not take a patient yet.
- **Patients history columns** — `DIAGNOSIS`, `RX`, `STATUS` on visits, and
  `ITEMS`, `DOCTOR`, `MODE`, `STATUS` on medicine bills.
- **Inventory → `Import supplier bill`** — that is the Bill Import module,
  not yet ported.
- **`Enter` to submit** the search boxes on Patients, Medicines, Inventory,
  the OPD book-visit dialog, the pharmacy counter and the edit-quantity
  dialog. The desktop binds it on all six; only Reports has it on the web.
