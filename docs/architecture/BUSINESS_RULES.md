# Business rules and invariants

**The most important document in this folder.** The screens can be rebuilt from
screenshots; these rules cannot be recovered by looking at the UI. Several of
them exist because of a specific bug, a legal requirement, or a real incident at
the counter, and every one of them has to survive the port.

Derived from `src/Pharma.Data/*Service.cs`, `src/Pharma.Core/`, and
`AppDbContext.OnModelCreating`. Where a rule is enforced in more than one place,
that is noted — those are the ones most easily lost in a rewrite.

---

## 1 · Identity and deletion

### Nothing is hard-deleted by default
Every entity derives from `BaseEntity`, which carries `IsDeleted`. Queries
filter on `!x.IsDeleted` **explicitly, at each call site** — there is no EF
global query filter doing it centrally.

> **Port risk — high.** A rewrite that forgets one `!IsDeleted` silently
> resurrects deleted rows. The web port should replace this with a global query
> filter in `OnModelCreating`, so it cannot be forgotten. That is a behaviour
> improvement, not a like-for-like port.

### Ids are client-generated GUIDs
`BaseEntity.Id` defaults to `Guid.NewGuid()` at construction, so an entity has
its identity **before** it is saved. Consequences relied on throughout:

- "New or existing?" is decided by tracking state or an explicit `_existing`
  reference, **never** by testing `Id == Guid.Empty` — that test is always false.
- Parent and children can be built in memory and saved in one `SaveChangesAsync`.

### Deleting a patient is refused if they have history
`OpdService.DeletePatientAsync` returns a message instead of deleting when
visits exist. The FK is `Restrict`, so the database would refuse anyway — the
check exists to produce a sentence a receptionist can read.

---

## 2 · Document numbering

### Every number is gap-free and sequential, per document kind
`NumberService.NextAsync` reads a `Counter` row, increments `LastNumber`, and
formats `{Prefix}{LastNumber:D5}`.

| Kind | Prefix | Example |
|---|---|---|
| Patient | `P` | `P00012` |
| Visit | `V` | `V00031` |
| Sale | `INV` | `INV00042` |
| StockEntry | `GRN` | `GRN00008` |
| FeeReceipt | `RCP` | `RCP00019` |
| DiagnosticBill | `DX` | `DX00004` |
| Appointment | `APT` | `APT00001` |
| ProcedureBill | `PRC` | `PRC00007` |
| DentalPayment | `DPR` | `DPR00003` |
| LabOrder | `LAB` | `LAB00001` |

Gap-free matters legally: a missing invoice number in a sequence is a question
from an inspector, not a cosmetic issue.

**A unique index backs every one of them** — `PatientNo`, `VisitNo`, `BillNo`
(on `Sales`, `DiagnosticBills` and `ProcedureBills`), `EntryNo`,
`AppointmentNo`, `ReceiptNo`, `OrderNo`, plus `Counter.Name` and `Setting.Key`.

> **Port risk — blocker.** The allocation itself is a read-modify-write with no
> lock and no retry: two callers read the same `LastNumber` and both format the
> same string. The unique index means the second `SaveChangesAsync` **fails**
> rather than silently duplicating — so the failure mode on the web is a bill
> that randomly refuses to save under load, not a corrupted register. That is
> the better of the two failures, and still unacceptable.
>
> The method needs an atomic allocation — `UPDATE … RETURNING`, a database
> sequence, or a catch-and-retry around the constraint violation — before any
> multi-user deployment. See [SAAS_MIGRATION.md](SAAS_MIGRATION.md).

---

## 3 · Pharmacy and stock

### Stock lives on the batch, never on the product
`Product.StockOnHand` is **computed** — it sums `Batches`. There is no stored
product-level quantity, so there is nothing to drift. Every screen that shows a
stock figure, and every report, resolves to the same `Batch.QtyOnHand`.

### Everything is counted in individual units
`Batch.QtyOnHand` is in dispensable units — tablets, millilitres, pieces — not
packs. Packs are a data-entry convenience: `StockEntryItem.UnitsReceived`
multiplies packs by `UnitsPerPack` on the way in. `PackMath` holds that
arithmetic; `DoseMath` turns a prescription into a unit count.

### Expired stock cannot be sold
Checked when batches are offered and again on save. A batch is expired at the
**end** of its expiry month.

### Batches are consumed oldest-expiry-first
Selection orders by `ExpiryDate` so the shelf empties in the order it should.

### A sale can span batches
If one batch cannot cover the quantity, the sale takes what it can and continues
into the next, producing multiple `SaleItem` rows for one requested line. Each
line keeps its own batch, expiry, MRP and GST rate.

> **Port risk — blocker.** Deduction reads `QtyOnHand`, computes
> `Math.Min(remaining, QtyOnHand)`, then writes the reduced value. Two
> simultaneous sales of the last strip both succeed and drive stock negative,
> defeating the check. Needs optimistic concurrency plus retry, or a conditional
> `UPDATE` the database adjudicates.

### GST is inside the MRP, never added to it
`GstCalculator` back-calculates taxable value out of the MRP. The customer pays
the printed price; tax is extracted for the return, not added at the till. CGST
and SGST each take half — and are **not always exactly equal**, because each is
rounded independently (see [BILL_REVIEW.md](../BILL_REVIEW.md)).

### The bill stores what was given, not what settings say now
`Sale.IsTaxInvoice` is written onto the bill. A reprint years later shows what
the customer actually received, even if the clinic has registered for GST since.

### Counter-added stock is marked provisional
Stock added at the till to serve a waiting patient sets `IsProvisional`, gets a
system-allocated batch number prefixed `CTR-`, and appears on the **Stock to
reconcile** report until matched to a supplier bill. The report is a worklist,
not a warning — it does not have to be empty.

### Corrections keep their reason
`StockAdjustment` records quantity before, quantity after, an
`AdjustmentReason`, free-text notes and who did it. Never edit `QtyOnHand`
without writing one — the trail is what an inspection asks for.

### A supplier bill cannot be imported twice
`PurchaseImportService` refuses a bill already received, naming the date and the
`GRN` it came in as:

> *Bill INV-8842 was already received on 14 Aug 2026 as GRN00008.*

A double import silently doubles the shelf, and nothing downstream would catch
it — which is why this refusal is worded to be unmistakable.

---

## 4 · OPD

### A visit is booked, then queued, then consulted, then completed
`VisitStatus`: `Booked → Waiting → InConsultation → Completed`. `Cancelled` is
reachable from the first three, but not once money or a consultation exists —
see below.

### A visit cannot be cancelled once it is paid or completed
`Visit.CanCancel` is `!FeePaid && Status is not (Completed or Cancelled)`.
Enforced **server-side** in `OpdService.SetStatusAsync`, which throws an
`InvalidOperationException` naming the receipt; the tile's Cancel button binds to
the same property, and `OpdViewModel.CancelVisitAsync` re-checks before asking
for confirmation, because a tile drawn before the fee was taken on another
screen is out of date by the time it is clicked.

Cancelling a paid visit would strand a numbered receipt against a visit the
register says never happened; cancelling a completed one would do the same to a
consultation. **Refund and reversal are deliberately not in the product** — the
desk settles the money on paper and the register keeps showing what happened.
A part-way, unpaid visit (`InConsultation`) can still be cancelled.

### Tokens are per day
Allocated on booking, sequential within the day, and shown on every tile.

### The fee is separate from the visit
`FeePaid`, `FeeReceiptNo`, `FeePaidOn`, `FeePaymentMode` live on `Visit`. A
patient can be seen before paying, and can pay without being seen yet — the two
are not sequenced.

### A family shares one phone number
Deliberate. Searching by phone returns every sibling, and the desk picks the
child who is actually present. This is where the wrong patient gets selected, so
the booking screen shows age and sex on every match.

### Allergies follow the patient onto every prescription
`Patient.Allergies` prints on every prescription for that patient — the one
field on the patient record that can cause harm if ignored.

---

## 5 · Billing across modules

Four modules raise bills, and they share one discipline:

| Bill | Module | Status enum |
|---|---|---|
| `Sale` | Pharmacy | `SaleStatus` |
| `DiagnosticBill` | Diagnostics | `DiagnosticBillStatus` |
| `ProcedureBill` | Pediatrics, Dentist, General | `ProcedureBillStatus` |
| `LabOrder` | Pathology Lab | `LabOrderStatus` |

### A completed bill cannot be edited or deleted
Enforced **server-side in the service**, not only by greying out the UI. Every
bill service re-checks status before writing.

### Line items keep their own price
`SaleItem`, `DiagnosticBillItem`, `ProcedureBillItem`, `LabOrderReport` and
`DentalCaseReplacement` each store the name, price and rate **as billed**. A
later change to a master price never moves a past bill. Master rows are
deactivated rather than deleted once billed.

### The clinic takes no credit — except in Dentistry
`PaymentMode` has deliberately no `Credit` member: pharmacy and OPD settle in
full at the counter, so there is no outstanding balance to reconcile.

**Dental cases are the documented exception.** A case runs across sittings and
accepts part-payments; `DentalCase.Balance` is computed as
`TotalCost − AmountPaid` and carries forward. A *completed* case still accepts
payment — finishing the work and finishing paying are different events.

---

## 6 · Pediatrics

### A dose is only on record once its bill is saved
Recording a vaccination adds a **draft** to the running bill, exactly like
adding a procedure. No `VaccinationRecord` is written, and no stock is deducted,
until `SaveBillAsync` succeeds. Clearing the bill or navigating away drops it
with nothing left behind.

> A free dose still needs a saved bill, at ₹0, to count as given. This is the
> single most easily-lost rule in the module: it makes "give a dose" atomic with
> "bill for it", and it is why the two cannot be split in the port.

### The vial comes out of pharmacy stock
The brand and price are read from a `Batch`; saving deducts one unit through the
same path a counter sale uses. Vaccination is not a parallel inventory.

### Due dates are computed, never stored as pending rows
Due, overdue and upcoming are derived from the patient's date of birth against
`VaccineMaster.RecommendedAgeDays` at query time. There is no table of
outstanding doses to keep in step.

### A patient with no date of birth has no schedule
With nothing to compare an age-in-days recommendation against, every not-yet-given
dose reads **Upcoming** regardless of the child's actual age. Correct, and
surprising — worth preserving the behaviour and the explanation together.

---

## 7 · Dentist

### A case, not a bill
`DentalCase` is the unit: one procedure *or* one package on one patient, tracked
across however many sittings, replacements and payments it takes.

### Procedure and package are mutually exclusive
Enforced in `OpenCaseAsync`. A case opened against a package bills the flat
`PackagePrice` — **not** the sum of its items.

### The price is snapshotted the day the work starts
`BaseCost` is written at open. `TotalCost` then adds anesthesia from each sitting
and each replacement's amount. A later master price change never moves an
in-progress case.

---

## 8 · Pathology Lab

### An order walks a fixed path
`Ordered → SampleCollected → ResultEntered → Verified → Completed`.

- **Save results** needs at least one row filled in.
- **Verify** is refused until at least one result exists.
- **Print report** is refused until the order is `Verified`.

That last rule is a real pathology-lab norm, not a billing convenience.

### Flags are computed against the matched reference range
`MatchReferenceRangeAsync` picks the range for the patient's sex and age, and the
result is flagged `Normal` / `Low` / `High` automatically for a numeric analyte.
A qualitative result ("Reactive") has no numeric range, so its flag is editable.

---

## 9 · Settings and modules

### A module that is off is absent, not hidden
Off removes it from the sidebar, from Reports, from the Patients history tabs,
and from the Appointments module picker. `AppointmentsService.BookAsync`
re-checks server-side — the picker never offers a context that would be refused.

### Toggles are read fresh on every navigation
`IPage.LoadAsync` runs on each visit and re-reads settings, which is why a module
appears the moment it is saved with no restart. **Never cache a toggle in a
constructor** — view models are DI singletons and a constructor runs once per
process.

### Settings are a key/value table
`Setting` rows keyed `clinic.*`, `pharmacy.*`, `docs.*`, `features.*`, `opd.*`,
`auth.*`, `ui.*`. Adding a setting needs no migration. `shop.*` keys are the
pre-split legacy names, migrated once and guarded by
`migrations.settingssplit.done`.

---

## 10 · Users and roles

### Roles are attribution, not authorisation
**Every logged-in role has full access today.** `UserRole` (`Admin`, `Doctor`,
`Pharmacy`, `Diagnosis`) exists so that actions are attributed sensibly and so
that restrictions can be added later without a migration. `CreatedByUserId` and
`UpdatedByUserId` on `BaseEntity` carry the attribution.

> **Port risk — high, and easy to get wrong in the opposite direction.** Do not
> assume the desktop enforces role permissions; it does not. On the web, hiding
> a nav button is not access control — every endpoint needs a server-side check,
> and that is **new behaviour to design**, not behaviour to port.

### Sign-in is off by default
With `auth.requirelogin` off the app runs unauthenticated, and `CreatedByUserId`
stays null. That is a permanent, normal state for a clinic that never turns
login on — not a gap to backfill.

---

## 11 · Time

### Everything uses the machine's local clock
`DateTime.Now` / `DateTime.Today` appear in **30 places** across the domain
services. On a clinic PC that is the clinic's own wall clock, which is what
"today's day book" means to the person reading it.

> **Port risk — high.** On a server this resolves to the *server's* timezone. A
> bill taken at 00:30 IST lands on the previous day's book if the server runs
> UTC, and every "today" query quietly disagrees with the front desk. The port
> needs an injected clock and a timezone stored per clinic. Mechanical, but all
> 30 call sites must be done before launch, not after.

---

## 12 · Money and precision

`decimal` throughout, precision 12 scale 2 declared in `OnModelCreating` —
**which SQLite ignores.** Decimals are stored as TEXT; any external query must
`CAST(... AS REAL)` or it will sort and sum text. EF converts correctly in both
directions, so the application is unaffected. A port to a database with real
decimal support removes this hazard entirely.
