# The manufacturer, and whether it belongs in the medicine name

Written 31 July 2026, from the question *"manufacturer is important for medicine
— should we use medicine names with manufacturer?"*

**The answer is no, and the reason is that it is already part of a medicine's
identity in this system — just not visibly enough, and not everywhere it needs
to be.** This document says what is already there, why the name is the wrong
place to put it, and the three gaps worth closing.

Nothing here has been implemented. It is written to be picked up cold.

---

## What is already there

More than it looks like from the Medicines screen.

| | Where |
|---|---|
| Stored per medicine | `Product.Manufacturer`, nullable TEXT — [`Entities.cs:143`](../src/Pharma.Core/Entities.cs) |
| **Part of the identity key** | `BuildKey() => Name\|Manufacturer\|PackSize` — [`Entities.cs:214`](../src/Pharma.Core/Entities.cs) |
| Enforced in the database | The generated `SearchKey` column, [`20260726181238_ProductSearchKey`](../src/Pharma.Data/Migrations/20260726181238_ProductSearchKey.cs) |
| Searched | Counter and Medicines — [`PharmacyService.cs:65`](../src/Pharma.Data/PharmacyService.cs); the doctor's Rx picker — [`ConsultationViewModel.cs:213`](../src/Pharma.App/ViewModels/ConsultationViewModel.cs) |
| Shown when choosing | Second line under the brand in the counter's match list — [`SaleView.xaml:49`](../src/Pharma.App/Views/SaleView.xaml) |
| Shown in lists | `MAKER` column on Medicines and on Inventory |
| Named in the duplicate warning | `"Cetirizine 10mg (Cipla, 10 TAB) already exists"` — [`PharmacyService.cs:95`](../src/Pharma.Data/PharmacyService.cs) |
| Imported | Vendor CSV maps `Manufacturer=ComName` — [`ImportProfileSeeder.cs:36`](../src/Pharma.Data/Import/ImportProfileSeeder.cs) |
| Reported | Stock register grid and the Excel export |

The important line in that table is the second one. **The system already
believes that Cetirizine 10mg made by Cipla and Cetirizine 10mg made by Micro
Labs are two different medicines**, and it has believed it since the first
migration. That is the right model, and none of the work below changes it.

---

## Why the name is the wrong place for it

The proposal was to name medicines `Cetirizine 10mg (Cipla)`. Five reasons not
to, in the order they would bite:

**It would store the same fact twice.** The identity key is built from three
separate columns. Put the maker in the name as well and there are two copies —
and the first time somebody corrects one and not the other, the duplicate check
carries on running while quietly no longer working.

**Editing gets worse.** Correcting a maker becomes a rename. Every batch, every
past bill line and every report grouped on that name shifts underneath it.

**It is wrong on a prescription.** A doctor writes *Cetirizine 10mg*. A company
name in brackets is something the chemist has to read past.

**It is wrong on a bill for the opposite reason.** The Apollo bill in
[BILL_REVIEW.md](BILL_REVIEW.md) prints the maker as **its own column** —
`USV`, `ABBO`, `RECK`, `COLG`. That is the convention on an Indian chemist's
invoice. A column is wanted, not a longer string.

**Sorting stops working.** An alphabetical list of medicines stops being
alphabetical by brand.

The pattern already in the code is the right one, and
[`PharmacyService.cs:95`](../src/Pharma.Data/PharmacyService.cs) is the example
to copy: **store the three facts separately, compose them for display at the
point where a human has to tell two things apart.**

---

## The work, in the order it is worth doing

### 1. Copy the manufacturer onto the sale line

**Where:** `SaleLine` in [`Entities.cs`](../src/Pharma.Core/Entities.cs),
[`BillPrinter.cs`](../src/Pharma.App/Printing/BillPrinter.cs)

`SaleLine` deliberately snapshots what was sold, so a reprint shows the truth
even after the medicine is later edited. It copies four fields:

```csharp
public string ProductName { get; set; } = string.Empty;
public string BatchNo    { get; set; } = string.Empty;
public DateTime ExpiryDate { get; set; }
public string HsnCode    { get; set; } = string.Empty;
```

The manufacturer is not among them. So **a reprint of a year-old bill cannot say
who made the strip** — which is exactly the moment it is asked for: a return, or
a recall. The batch number is there, so a recall by batch works; a recall by
manufacturer does not.

One column, one assignment where the other four are already made, one column on
the printed bill. It follows a pattern the same class establishes four times.

This is also [BILL_REVIEW.md §6](BILL_REVIEW.md). Do it once; the two documents
describe the same change.

### 2. Stop maker spelling variants splitting the stock

**Where:** [`DataHealthService.cs:252`](../src/Pharma.Data/DataHealthService.cs),
[`PurchaseImportService.cs`](../src/Pharma.Data/Import/PurchaseImportService.cs)

**This is the one most likely to be met in ordinary use**, because supplier
bills are imported rather than typed.

The manufacturer is part of the identity key. So if one supplier's file says
`CIPLA LTD` and the next month's says `Cipla`, the keys differ, and two
medicines are created **with the same brand name and the same pack size**. The
stock splits between them. At the counter the same medicine appears twice, each
with a different count, and neither is right.

Data health already reports duplicates, but only after normalising case and
collapsing whitespace:

```csharp
static string Norm(string? value) =>
    string.Join(' ', (value ?? "").Trim().ToLowerInvariant()
                                  .Split(' ', StringSplitOptions.RemoveEmptyEntries));
```

`cipla ltd` and `cipla` survive that untouched, so the split is never reported.

> **Needs a decision.** A maker alias table (`CIPLA LTD` → `Cipla`), applied on
> import and offered as a data-health repair? Or fuzzy matching inside the
> duplicate check, which needs no data entry but will occasionally be wrong
> about two makers that genuinely are different? An alias table is more work and
> more predictable. Whichever is chosen, the repair has to merge stock rather
> than pick a winner — the existing duplicate repair already knows how.

### 3. Decide whether a blank manufacturer is allowed

**Where:** [`MedicineEditorViewModel.cs`](../src/Pharma.App/ViewModels/MedicineEditorViewModel.cs)

`Manufacturer` is nullable and nothing asks for it. Two medicines both called
`Paracetamol 500mg` with the maker left blank produce the same key and collapse
into one another.

Identity resting on a field nobody was required to enter is weak identity. The
question is whether to require it, or merely to say so on the form — the seeded
catalogue itself uses `"Generic"`, which suggests a required field wants a
sensible default rather than a blocked save.

### 4. Show the maker where two medicines look identical

**Where:** [`ConsultationView.xaml:126`](../src/Pharma.App/Views/ConsultationView.xaml),
[`ProductsView.xaml`](../src/Pharma.App/Views/ProductsView.xaml)

The doctor's Rx picker **searches** the manufacturer but does not **show** it.
Each match renders as name, pack size, stock:

```
Cetirizine 10mg              10 TAB    240 in stock
Cetirizine 10mg              10 TAB     60 in stock
```

Two makers, two rows, nothing to choose between them. The counter's match list
already gets this right — [`SaleView.xaml:49`](../src/Pharma.App/Views/SaleView.xaml)
puts the maker under the brand — so this is making one screen agree with
another, not new design.

On the Medicines grid the maker is present but in a `MAKER` column further
right, which reads well enough when scanning and badly when two rows are
adjacent and otherwise identical. Worth showing it under the brand **when a name
is not unique**, rather than always.

---

## What this does not change

- **The manufacturer stays on the medicine, not on the batch.** It is a property
  of the brand. Two batches of the same brand from different makers is not a
  thing that happens; a brand is the maker's.
- **The identity key stays as it is.** `Name|Manufacturer|PackSize` is correct.
  Everything above makes it work better, none of it replaces it.
- **No change to the medicine name field.** That is the point of the document.

---

## Suggested order for the next version

1. **§1, the sale line.** Small, bounded, has a legal edge — a return or a
   recall against a reprinted bill — and it is already written up twice.
2. **§2, the maker variants.** Bigger, needs a decision first, but it is the one
   that silently makes stock figures wrong in day-to-day use.
3. **§4, showing it in the Rx picker.** Half an hour, and it removes a way for
   the wrong medicine to be prescribed.
4. **§3, blank makers.** Worth settling once §2 has decided how makers are
   normalised, since the two answers interact.
