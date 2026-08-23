# Flows and functional test cases

Written by walking the system, not by reading it. Every reference number
below (P00005, V00005, RCP00002, GRN00007, INV00001) came out of an actual
run against a running instance on 23 Aug 2026. Section 6 lists what the walk
found, and what is fixed.

Test IDs are stable — quote them in bug reports.

---

## 1 · The spine: patient → visit → consultation → pharmacy

This is the journey that matters. Everything else hangs off it.

```
  Patients                OPD Queue              Consultation           Pharmacy
  ────────                ─────────              ────────────           ────────
  add patient  ──────▶  book visit  ──────▶  vitals + Rx  ──────▶  load by token
   P00005                V00005 tok 1          prescription          INV00001
                         collect fee                 │                    │
                         RCP00002                    │                    ▼
                              │                      │              stock 100 → 90
                              ▼                      ▼
                        fee receipt PDF        prescription PDF
```

The link that makes it one journey rather than four screens: the counter
loads a **visit's prescription by token**, so the pharmacist dispenses what
the doctor actually wrote instead of re-keying it from a piece of paper.

### F-SPINE-01 · A child is seen and dispensed to, end to end

| | |
|---|---|
| **Why** | If only one test is ever run, run this one. It crosses every module boundary the product has. |

1. Patients → **+ New patient**. Name, phone, age, guardian. Save.
   → *A patient number is allocated (P00005). Age is required only when there is no date of birth.*
2. OPD Queue → **Book visit**. Pick the patient and a doctor. Save.
   → *A visit number and a token appear (V00005, token 1). Status is `Booked`.*
3. **Collect fee** on that row. Cash, ₹300.
   → *Receipt number allocated (RCP00002). `feePaid` true. Fee receipt prints as A5.*
4. Open the consultation. Weight 16.5, temp 100.2, complaint, diagnosis.
5. Prescribe: type `cetzine`, pick **5 mg**, dose 1-0-1, 5 days.
   → *Quantity fills itself to 10. The hint reads "In our pharmacy · N in stock".*
6. Save and complete.
   → *Prescription prints as A5, naming the medicine **with its strength**.*
7. Pharmacy → load the visit by token.
   → *The prescribed line arrives on the bill with quantity already set.*
8. Save the bill.
   → *Bill number allocated (INV00001). Stock falls by exactly the quantity sold.*

**Arithmetic to check at step 8** — one strip of 10 at MRP ₹45, GST 12%:
gross `45.00`, taxable `40.18`, CGST `2.41`, SGST `2.41`, net `45`.
The net is the MRP: tax is extracted *from* the MRP, never added to it.

### F-SPINE-02 · Prescribing something the pharmacy does not stock

1. Prescribe a medicine whose stock is 0.
   → *Allowed, and the hint says "out of stock".*

**Why it must be allowed:** a doctor prescribes what the patient needs, not
what happens to be on the shelf. The parent buys it elsewhere. What must
never happen is the doctor not being *told* — hence the hint.

---

## 2 · One medicine, several strengths

Cetirizine as 5 mg, 10 mg, syrup 100 ml, syrup 200 ml and drops. This is the
ordinary case, not an edge one.

**How it is modelled:** one `Product` per sellable variant. Not a parent
record with children — batches, MRP, GST, HSN, expiry and stock are all
per-variant, so a parent "Cetirizine" would own nothing and could not be
dispensed. `Strength` is its own field so the variants can be told apart and
ordered.

### F-VAR-01 · Five strengths coexist under one name

1. Medicines → **+ New medicine**. Name `Cetzine`, generic `Cetirizine`,
   strength `5 mg`, pack `10 TAB`.
2. On that row press **+ Another strength**.
   → *Everything shared carries over — drug, maker, composition, storage,
   GST, HSN, schedule, rack. Strength and pack are blank, because they are
   what you came to type. Units-per-pack and dispensing unit are also blank:
   a syrup cloned from a tablet must not inherit "15 per pack".*
3. Enter `10 mg`, save. Repeat for `100 ml`, `200 ml`, `2.5 mg/ml`.
   → *All five exist. None is refused as a duplicate.*

### F-VAR-02 · Doses sort by dose, not alphabetically ⚠

1. Search `cetzine` anywhere a medicine is picked.
   → ***2.5 mg, 5 mg, 10 mg, 100 ml, 200 ml*** — ascending by dose.

**Why this is the most important test on the page.** Sorting on the text put
`10 mg` *above* `5 mg`, because "1" sorts before "5". That is the adult dose
at the top of the list a counter picks a child's dose from. Pinned by
`MedicineVariantTests`.

### F-VAR-03 · The strength reaches the paper

1. Prescribe `Cetzine 5 mg`, print.
2. Sell `Cetzine 5 mg`, print the invoice.
   → *Both documents read "Cetzine 5 mg", never bare "Cetzine".*

**Why:** once strength left the name, all five records are called "Cetzine".
A script or an invoice that does not say the dose is not usable by the person
holding it. Everything printed goes through `medicineDisplayName`.

### F-VAR-04 · A real duplicate is still refused

1. Add a second `Cetzine`, same maker, same pack, same `5 mg`.
   → *Refused, naming the existing record.*

**Why:** widening the duplicate key by strength must not have widened it so
far that a true duplicate slips through — two records split the stock and
both appear at the counter.

---

## 3 · Receiving stock

### F-STOCK-01 · Receive by hand
Inventory → pick a medicine → **Receive stock**. 10 packs of 10, batch, expiry, rate, MRP.
→ *GRN number allocated. Stock rises by packs × units-per-pack (100).*

### F-STOCK-02 · Receive a supplier's file
Inventory → **Import a bill** → supplier format → file → **Check the file**.
→ *A preview: bill number, date, each line, units in, and any issues. Nothing is written yet.*
→ *Import into stock: GRN allocated, new medicines created, units added.*

### F-STOCK-03 · The same bill twice
Import the same file again.
→ *Refused: "Bill ER01441 was already received … Importing it again would double the stock."*

### F-STOCK-04 · Correcting a count
Inventory → **Correct count** → new quantity + reason.
→ *Stock changes and a correction appears in the trail. Receiving and selling both leave a document; a correction otherwise would not.*

### F-STOCK-05 · Pack size disagreeing with units-per-pack ⚠
Settings → Data health → **Scan**.
→ *A medicine whose pack says "15 TAB" while units-per-pack says 1 is listed as "The counter is selling whole packs to anyone asking for tablets."*
→ *Repair sets it to 15 and re-counts every batch, writing an adjustment for each.*

**Why:** the damage is silent. That medicine sells a whole strip to someone
asking for one tablet, at fifteen times the price, and nothing reports an error.

---

## 4 · Per-module flows

### OPD
Book → (arrive) Waiting → InConsultation → Completed. Fee may be collected at
any point; the receipt is independent of clinical status. Doctor-wise tabs and
morning/evening sittings filter the same queue.

### Pharmacy counter
Search → pick → quantity (units *or* packs) → add → repeat → save → print.
Batches allocate nearest-expiry-first. A Schedule H1 line prompts for the
prescriber. Loading a visit by token fills the bill from the prescription.

### Appointments
Book ahead → reminders (lead days) → arrives as an OPD visit. Booking and
visit are one record, so nothing is re-keyed on arrival.

### Diagnostics / Pathology Lab
Requested during consultation → billed → sample → results entered per analyte
→ verified → report prints. **A result cannot print unverified.**

### Pediatrics
Growth (weight/height/head, plotted) · Care (procedure + vaccine billing) ·
Immunization (card from the vaccine master, per-dose **Record**).
A dose is drafted on the bill and written **only when the bill saves** — so a
dose is never on record without the bill that charged for it.

### Dentist
Case → sittings → replacements → part-payments. Cost and payment hang off the
**case**, not a bill; the balance is derived, never stored.

### Masters
Vaccines · Procedures (shared, filtered by department pill) · dental packages,
replacements, anaesthesia · lab analytes, reports, packages. Delete is refused
once a master has been used; deactivate instead.

### Settings
Clinic · Pharmacy · Doctors · **Staff logins** · Document branding · Features ·
**Data health**. Staff logins and data health are Admin-only.

---

## 5 · Access control

### F-AUTH-01 · A non-admin cannot manage staff
Sign in as a Pharmacy-role user.
→ *`/api/users`, `/api/data-health/scan`, `/api/import/profiles` all **403**.*
→ *`/api/patients` still **200** — ordinary work is unaffected.*

### F-AUTH-02 · Support cannot read a clinic
Sign in as EnterpriseAdmin.
→ *Lands on the platform console. `/api/patients`, `/api/visits`,
`/api/settings/general` all **403**.*

### F-AUTH-03 · An Admin cannot lock the clinic out
Try to remove your own Admin role, or deactivate your own account.
→ *Both refused. Neither is recoverable from inside the clinic.*

### F-AUTH-04 · A temporary password is good once
Reset a user's password → sign in with it.
→ *Forced to the change-password screen, and cannot leave it by typing a URL.*

---

## 6 · Gaps this walk found

| # | Gap | State |
|---|---|---|
| 1 | Strengths sorted alphabetically — `10 mg` above `5 mg` at the point of dispensing | **Fixed** |
| 2 | Strength had nowhere to live, so it was folded into the name | **Fixed** — own field, in the duplicate key |
| 3 | The consultation picker showed name and pack but **not strength** — the screen where the dose is chosen | **Fixed** |
| 4 | Once strength left the name, scripts and invoices would have printed bare "Cetzine" | **Fixed** — `medicineDisplayName` |
| 5 | Pediatric procedures are not seeded, so procedure billing has nothing to offer on a fresh tenant | **Open** |
| 6 | Clinic logo: no upload, and no rendering in any PDF | **Open** — the last customer-visible gap |
| 7 | No backup or data export of any kind | **Open** — a policy decision, not a queue item |

Gaps 1–4 all came from the same root: the catalogue had no field for
strength. Fixing the model surfaced three consequences that a code reading
would not have found, and gap 4 was one my own change introduced and the walk
caught before it shipped.

---

## Running these

Nothing here needs a test harness — they are meant to be run by a person
against a real instance, which is the point. The automated tests
(`dotnet test`, 68 at the time of writing) cover the arithmetic and the
invariants underneath; these cover the journeys.

Where a case is also pinned automatically, the test class is named beside it.
