# What a real chemist's bill says about ours

Reviewed 28 July 2026 against **Apollo Pharmacy bill 26475GC0219254**, dated
8 July 2026 — a real over-the-counter purchase, five lines, two of them
part-pack sales.

The figures from that bill are in
[`tests/Pharma.Tests/ApolloInvoiceTests.cs`](../tests/Pharma.Tests/ApolloInvoiceTests.cs),
so the comparison is executable rather than an opinion. Ten tests, all passing.

Nothing in this document has been implemented. It is here to be picked up cold.

---

## What the bill confirms we already get right

Worth stating first, because it is most of the invoice:

- **Batch-wise stock**, with the batch number and expiry printed against each line.
- **GST taken out of the MRP**, never added to it. ₹739.25 of medicines splits
  into ₹704.05 taxable and ₹35.20 tax. Adding 5% on top would have charged ₹776.
- **Loose sale of part packs, priced per unit.** Their `100 tablets of a 10's
  strip at ₹1.95 = ₹195.00` and `45 of a 15's = ₹74.25` come out of our
  arithmetic to the paisa — these are the exact shape of the fault this system
  was built to fix.
- **A whole pack always costs what is printed on it.**
- Pharmacist's name and signature block, drug licence number, GSTIN, amount in
  words.

---

## The work, in the order it is worth doing

### 1. CGST and SGST must be equal halves

**Where:** `src/Pharma.Core/GstCalculator.cs`

The tax is halved per line. An odd number of paise cannot be split evenly, and
the spare paise goes to SGST every time — across this bill, four paise more SGST
than CGST. Apollo's reads `CGST: 6.39  SGST: 6.39`.

The customer pays exactly the right total and nothing at the counter is wrong,
but each half is meant to be half of the tax, and **a GST return filed from
these figures will not balance.**

Halve once at bill level and give any odd paise to one side deliberately. This
changes how every bill is taxed, so it wants its own commit and its own tests.

Covered today by `The_two_halves_of_gst_do_not_match_on_a_bill_of_odd_paise`,
which asserts the current behaviour so it cannot drift while it waits.

### 2. Print the MRP per unit, not per pack

**Where:** `src/Pharma.App/Printing/BillPrinter.cs`

Every chemist's bill in India multiplies out. Theirs:

```
Qty  Product                        MRP     Amount
100  GLYCOMET 500MG TAB 10'S        1.95    195.00
```

Ours prints `19.50` — the strip — against a quantity of `100 tablets` and a line
total of `195.00`. The arithmetic is right and **the printed line cannot be
checked by the person holding it**, which on a bill is its own kind of wrong.

`PackMath.UnitPrice` already returns the right number; this is a presentation
change, not a pricing one.

> **Decide before starting.** Reprints of bills already issued would show the new
> format against the old stored figures. Confirm that is acceptable.

### 3. HSN against each line

**Where:** `src/Pharma.App/Printing/BillPrinter.cs`

We hold `HsnCode` on every sale line but print one combined list at the foot of
the bill. Rule 46 of the CGST rules wants the HSN against each item. Their bill
has an HSN column. Ours is a valid tax invoice in every other respect, which is
what makes this the odd gap.

### 4. Repeat the header on later pages

**Where:** `src/Pharma.App/Printing/BillPrinter.cs`

Theirs prints `continued >>` at the foot and the whole header again on page 2.
Ours prints the header once, so the second page of a long bill arrives with no
shop name, no GSTIN and no bill number on it — not a document, a fragment.

Any bill over roughly twelve lines hits this.

---

## Where our data model is narrower than reality

### 5. A medicine can be on more than one schedule

**Where:** `src/Pharma.Core/Enums.cs`, `DrugSchedule`

The most surprising finding. Their bill's SCH column shows `S`, `H`, `Non` and —
against Glycomet — **`G & H`**.

Our enum is `None | H | H1 | X`. It has no Schedule G and no Schedule S, and it
cannot express a combination at all. Mis-stating a schedule on a bill is a
drug-rules problem, not a cosmetic one.

> **Needs a decision.** A `[Flags]` enum, or free text validated against a known
> set? Flags are tidier; free text survives a schedule we have not thought of.
> Either way it is a migration, since existing rows carry the old values.

### 6. Manufacturer on the sale line

**Where:** `src/Pharma.Core/Entities.cs` (`SaleLine`), `BillPrinter`

They print `USV`, `ABBO`, `RECK`, `COLG`. We hold the manufacturer on the
medicine but never copy it onto the line, so a reprint of a year-old bill cannot
say who made the strip — which is exactly when it is asked for, on a return or a
recall.

Same pattern as the other fields already copied onto the line at sale time.

> Written up more fully in [MANUFACTURER.md §1](MANUFACTURER.md), alongside the
> three other places the manufacturer is under-used. Same change — do it once.

### 7. Customer mobile number

**Where:** `Sale`, the counter, `BillPrinter`

They key the bill on the customer's mobile and print it. We have a free-text
customer name and nothing else, so a walk-in is unfindable afterwards.

This is the customer-lookup work already outstanding from the counter pass, and
this bill is the argument for it.

---

## Completeness, lower value

### 8. FSSAI number

Their header has the field. A pharmacy selling supplements or nutrition needs
one on the bill. `ShopProfile` has drug licence and GSTIN but no FSSAI.

### 9. Expiry as a date rather than a month

They print `25 Mar 28`; we print `03/28`. Theirs is what is stamped on the pack.
Our `Batch.ExpiryDate` already holds a full date, so this is a format string —
but check what the counter's expiry warnings assume before changing it.

### 10. A non-medicine charge line

Their packing and handling charge carries its own SAC code (`998549`) and its
own 18% rate, sitting alongside 5% medicines on the same bill. We cannot put a
charge on a bill at all.

Only worth building if the clinic will ever charge for delivery or packing.

### 11. Several lines of statutory footer

We have one free-text footer. Theirs carries the returns policy (*"Goods once
sold cannot be taken back or exchanged"*), a separate insulins-and-vaccines
notice, and a helpline number. Probably just needs the footer field to accept
multiple lines.

---

## Noted, not recommended

- **No round-off.** Their net is ₹282.23 — paise retained. We round to the
  rupee. That is a deliberate choice for a cash counter and worth keeping;
  recorded only so nobody "fixes" it later.
- **Discount, loyalty redemption, donation round-up.** Their bill carries all
  three. Discounts were dropped from this system on purpose.
- **QR code for payment.** Theirs says one was displayed digitally.

---

## Suggested first pass

Items **1 to 4** together: all in `GstCalculator` and `BillPrinter`, all about
the printed document being correct and defensible, roughly a day.

Then **5** on its own, after the decision on how a schedule is modelled.
