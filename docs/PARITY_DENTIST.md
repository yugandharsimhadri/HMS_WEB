# Dentist — feature parity with HMS_WPF

Every behaviour the desktop has in this module, enumerated from the source
(not from memory), with its port status. This is the acceptance checklist:
HMS_WEB is not done until every ☑ below is genuinely ☑.

Derived from: `DentistViewModel` (375 lines), `DentistService` (already
ported), the `DentalCase` / `DentalSitting` / `DentalCaseReplacement` /
`DentalPayment` entities, and `DentalReceiptDocument`.

> **Spec source.** `DentistViewModel.cs` exists only on
> `origin/Dentist_Pathology`:
>
> ```
> git show origin/Dentist_Pathology:src/Pharma.App/ViewModels/DentistViewModel.cs
> ```

Status: ☑ done · ◻ not started · ◐ partial

**All 52 items are ☑ as of 22 Aug 2026**, each driven against the running
app in a browser — not just compiled. What testing surfaced is at the end.

---

## What this module deliberately does NOT include

The desktop's four dental masters — **procedure, replacement, anesthesia
type and package** — live on the shared General Master destination, not
here. They belong to the **Masters** module.

Unlike Pediatrics, this is not a blocking gap: `DentistProcedureSeeder`
seeds Dentist procedures, so a fresh tenant can open a case immediately.
Replacements, anesthesia types and packages are **not** seeded, so those
parts of a case stay empty until Masters lands. The read endpoints are here;
the editors are Masters' job.

---

## 1 · Module gating and shape

| # | Feature | Why it exists | Status |
|---|---|---|---|
| 1.1 | Dentist is **off by default** (`DentistEnabled`) | ☑ |
| 1.2 | Nav entry appears only when the module is on | ☑ |
| 1.3 | One patient chosen **in the header, above the tabs** | Same shape as Pediatrics — the whole screen is about one person | ☑ |
| 1.4 | Three tabs — Cases, Treatment, Payments | One task per tab | ☑ |
| 1.5 | Treatment and Payments carry a **read-only case strip** at the top | Which case is active must not be lost switching tabs | ☑ |
| 1.6 | The strip has a **Switch case** link back to Cases | Rather than leaving the user to hunt for the tab | ☑ |

## 2 · The shared patient panel

| # | Feature | Why | Status |
|---|---|---|---|
| 2.1 | Search by name **or phone**, at most 20 matches | ☑ |
| 2.2 | Search runs as you type | ☑ |
| 2.3 | **A single match auto-selects** | ☑ |
| 2.4 | Choosing a patient clears the search box and loads their cases | ☑ |
| 2.5 | **Change patient** clears selection, search, matches, **the case list and the selected case** | A case belonging to the previous patient must not survive the switch | ☑ |
| 2.6 | **New patient** inline, and the created patient becomes the selection | ☑ |
| 2.7 | Subtitle — `N case(s)`, or "No cases for this patient" | ☑ |

## 3 · Opening a case

| # | Feature | Why | Status |
|---|---|---|---|
| 3.1 | A case is opened against **either a procedure or a package, never both** | The service enforces it too — the base cost has to come from exactly one place | ☑ |
| 3.2 | Refuses with neither picked — "Pick a procedure or a package to open the case against." | ☑ |
| 3.3 | Refuses with **both** picked — "Pick either a procedure or a package, not both." | ☑ server-enforced and verified; the screen also clears one picker when the other is used, so the state cannot be reached by clicking |
| 3.4 | Refuses without a patient — "Select a patient first." | ☑ |
| 3.5 | Refuses without a doctor — "Select a doctor." | ☑ |
| 3.6 | Doctor picker defaults to the first doctor | ☑ |
| 3.7 | Tooth number, free text (FDI or Universal notation) | ☑ |
| 3.8 | Notes, optional, trimmed | ☑ |
| 3.9 | **`BaseCost` is snapshotted** from the procedure/package price at opening | A later master price change must never move the ground under an in-progress case | ☑ |
| 3.10 | The procedure/package **name is snapshotted** too | ☑ |
| 3.11 | A new case starts `Planned`, started today | ☑ |
| 3.12 | Outcome names what it was opened against **and the base cost** | ☑ |
| 3.13 | The form clears and the new case becomes the selected one | ☑ |
| 3.14 | Server refuses a procedure or package that no longer exists | ☑ |

## 4 · The case list

| # | Feature | Why | Status |
|---|---|---|---|
| 4.1 | Every case for this patient — what it is against, tooth, status, started | ☑ |
| 4.2 | Running money per case: **total cost, paid, balance** | ☑ |
| 4.3 | `TotalCost` = base + anesthesia across sittings + replacements | The case, not a bill, is the unit money is tracked against | ☑ |
| 4.4 | `Balance` never goes below zero | An overpayment is not a negative balance | ☑ |
| 4.5 | Mark a case **Completed** | ☑ |
| 4.6 | **Cancel** a case | ☑ |
| 4.7 | Selecting a case is what Treatment and Payments act on | ☑ |

## 5 · Sittings

| # | Feature | Why | Status |
|---|---|---|---|
| 5.1 | Add a sitting against the selected case | ☑ |
| 5.2 | **Sitting number is allocated server-side**, max+1 within the case | "Sitting 2 of 4" has to be stable, not a client's guess | ☑ |
| 5.3 | Work done, free text | ☑ |
| 5.4 | Anesthesia type from the master list | ☑ |
| 5.5 | Choosing a type **pre-fills its default cost** | ☑ |
| 5.6 | The cost stays editable after pre-fill | ☑ |
| 5.7 | Next sitting date, optional | ☑ |
| 5.8 | Adding the first sitting moves the case **Planned → InProgress** | The case is now actually underway | ☑ |
| 5.9 | Sittings are **refused on a Completed or Cancelled case** | ☑ |
| 5.10 | The sitting inherits the case's doctor when none is given | ☑ |
| 5.11 | The form clears and the case reloads after adding | ☑ |
| 5.12 | Anesthesia cost **feeds the case total** | ☑ |

## 6 · Replacements

| # | Feature | Why | Status |
|---|---|---|---|
| 6.1 | Add a replacement (crown, bridge, denture) against the case | ☑ |
| 6.2 | Refuses without one picked — "Pick a replacement first." | ☑ |
| 6.3 | Unit cost comes from the master; quantity defaults to 1 | ☑ |
| 6.4 | **Amount = unit cost × quantity**, computed server-side | ☑ |
| 6.5 | Server refuses quantity below 1 | ☑ |
| 6.6 | Replacements are **refused on a Completed or Cancelled case** | ☑ |
| 6.7 | Replacement cost **feeds the case total** | ☑ |
| 6.8 | The name is snapshotted onto the case row | ☑ |

## 7 · Payments

| # | Feature | Why | Status |
|---|---|---|---|
| 7.1 | Record a payment **against the case's running balance**, never the whole bill in one shot | Dentistry is paid in instalments across sittings | ☑ |
| 7.2 | Refuses zero or negative — "Enter an amount greater than zero." | ☑ |
| 7.3 | Payment mode — Cash / UPI / Card | ☑ |
| 7.4 | **Reference no. only for UPI and Card** | ☑ |
| 7.5 | Each payment gets its own **receipt number** | ☑ |
| 7.6 | Payment is **allowed on a Completed case** | A late payment is still a payment | ☑ |
| 7.7 | Payment is **refused on a Cancelled case** | ☑ |
| 7.8 | Payment history for the case | ☑ |
| 7.9 | Outcome: `{receipt} · ₹{amount} recorded.` | ☑ |
| 7.10 | The form clears and the case reloads, so the balance is current | ☑ |
| 7.11 | **Record** and **Record & print** are separate actions | ☑ |

## 8 · Printing

| # | Feature | Why | Status |
|---|---|---|---|
| 8.1 | **Dental receipt PDF** — port `DentalReceiptDocument` | ☑ |
| 8.2 | Shows the payment **and the case's standing** — total, paid, balance | The parent is paying an instalment and wants to know what is left | ☑ |
| 8.3 | Carries clinic header and document theme | ☑ |
| 8.4 | Printing re-reads the case rather than printing what was on screen | ☑ |
| 8.5 | Reprint an earlier receipt | ☑ Reprint present on every payment row; the print path itself verified by fetching the document |

---

## Deliberate differences from HMS_WPF

- **Popups → dialogs**, and `Dialog.Show` warnings → inline messages, as in
  every module before this.
- **The four dental masters are out of scope** — they belong to Masters, as
  they do on the desktop.
- **The case strip is rendered on both dependent tabs** rather than being a
  shared control; same information, same purpose.

---

## What browser testing caught

No defects. Every structural bug the earlier modules found had already been
fixed at its source, and this page was written against those lessons rather
than into them — the selected case is held as an **id** and derived from the
current list on every render, which is the fix Appointments paid for.

What driving it did prove is the arithmetic, which is the part of this
module most worth doubting, because unlike every other bill here the money
is derived rather than stored:

- Opening against Root Canal Treatment snapshotted **₹3500** as `BaseCost`.
- Adding a sitting with a nerve block took the total to **₹4100** and moved
  the case Planned → In progress in the same action.
- Adding 2 × Zirconia crown took it to **₹19,100**, with the ₹15,000 line
  amount computed server-side from the master's unit cost, not the client's.
- A ₹5,000 instalment left a balance of **₹14,100**, and the printed receipt
  carried all three figures — which is the whole reason this document
  differs from every other receipt in the system.
- Overpaying (₹25,000 against a ₹19,100 case) floored the balance at **0**
  rather than going negative.

The four status guards all refuse from the server, not just the screen: no
sittings or replacements on a Completed or Cancelled case, no payment on a
Cancelled one — but a payment on a **Completed** case is allowed, because a
late payment is still a payment. That asymmetry is easy to get wrong by
treating "finished" as one state, and it is the thing most worth having
checked.

**Masters that are not seeded.** `DentistProcedureSeeder` seeds procedures,
so a case can be opened on a fresh tenant — but anesthesia types,
replacements and packages are not seeded and have no editor until the
Masters module ships. The three rows used to drive sittings, replacements
and a package case were inserted directly into the database for testing.
The screen says so where each list would be, rather than showing an empty
dropdown with no explanation.
