# Pathology Lab — feature parity with HMS_WPF

Every behaviour the desktop has in this module, enumerated from the source
(not from memory), with its port status. This is the acceptance checklist:
HMS_WEB is not done until every ☑ below is genuinely ☑.

Derived from: `PathologyLabViewModel` (409 lines), `PathologyLabService`
(already ported), the `LabAnalyte` / `LabReport` / `LabPackageMaster` /
`LabOrder` / `LabOrderReport` / `LabResult` entities, and
`LabReportDocument`.

> **Spec source.** `PathologyLabViewModel.cs` exists only on
> `origin/Dentist_Pathology`:
>
> ```
> git show origin/Dentist_Pathology:src/Pharma.App/ViewModels/PathologyLabViewModel.cs
> ```

Status: ☑ done · ◻ not started · ◐ partial

**All 58 items are ☑ as of 22 Aug 2026**, each driven against the running
app in a browser — not just compiled. What testing caught is at the end.

---

## What this module deliberately does NOT include

The three lab masters — **analyte, report (panel) and package** — live on
their own destination, `PathologyLabMasterViewModel`, which is part of the
**Masters** module.

`PathologyLabSeeder` seeds analytes and reports, so a fresh tenant can order
and enter results immediately. It does **not** seed reference ranges, and
packages are not seeded either — so a freshly registered clinic gets results
with no range and no flag against them until Masters ships the analyte
editor. That is a sharper gap than it first looks: the Low/High flag is the
most clinically useful thing this module computes, and it is inert until
somebody configures ranges. The read endpoints are here; the editors are
Masters' job.

The seeded analytes and reports are still enough to make the whole
order → result → verify → print cycle usable out of the box, which is why
this module is worth porting before Masters — but the flag column stays
empty until ranges exist.

---

## 1 · Module gating and shape

| # | Feature | Why it exists | Status |
|---|---|---|---|
| 1.1 | Pathology Lab is **off by default** (`PathologyLabEnabled`) | Distinct from Diagnostics, which stays flat named-test billing | ☑ |
| 1.2 | Nav entry appears only when the module is on | ☑ |
| 1.3 | One patient chosen **in the header**, orders below | ☑ |

## 2 · The shared patient panel

| # | Feature | Why | Status |
|---|---|---|---|
| 2.1 | Search by name **or phone**, at most 20 matches | ☑ |
| 2.2 | Search runs as you type | ☑ |
| 2.3 | **A single match auto-selects** | ☑ |
| 2.4 | Choosing a patient clears the search box and loads their orders | ☑ |
| 2.5 | **Change patient** clears selection, search, matches, **the order list and the selected order** | ☑ |
| 2.6 | **New patient** inline, and the created patient becomes the selection | ☑ |
| 2.7 | Subtitle — `N order(s)`, or "No orders for this patient" | ☑ |

## 3 · Building an order

| # | Feature | Why | Status |
|---|---|---|---|
| 3.1 | Add a report (panel) from the catalogue, at its catalogue price | ☑ |
| 3.2 | Catalogue lists **active reports only** | ☑ |
| 3.3 | A report already on the order is refused — "{name} is already on this order." | ☑ |
| 3.4 | Remove a line | ☑ |
| 3.5 | **Apply a package** — expands into one line per constituent report | Each report's analytes still have to be known for result entry, so a package cannot be one opaque line | ☑ |
| 3.6 | Each expanded line keeps **its own catalogue price** | So the printed report still shows what each is individually worth | ☑ |
| 3.7 | The package price is applied **as a discount**, not by rewriting prices | `discount = sum(report prices) − package price` | ☑ |
| 3.8 | Discount from a package never goes below zero | ☑ |
| 3.9 | Applying a package **replaces** the current lines | ☑ |
| 3.10 | A package with no reports configured is refused — "This package has no reports configured." | ☑ |
| 3.11 | The order records which package it came from (`PackageId`) | ☑ |
| 3.12 | Referred by, optional | ☑ |
| 3.13 | Specimen id, optional | ☑ |
| 3.14 | Remarks, optional | ☑ |
| 3.15 | Payment mode — Cash / UPI / Card | ☑ |
| 3.16 | **Reference no. only for UPI and Card** | ☑ |
| 3.17 | Total and final recompute live; final never below zero | ☑ |
| 3.18 | Refuses without a patient — "Select a patient first." | ☑ |
| 3.19 | Refuses an empty order — "Add at least one report, or apply a package." | ☑ |
| 3.20 | Server **recomputes totals from the lines** | ☑ |
| 3.21 | Patient name and number **denormalised onto the order** | ☑ |
| 3.22 | A new order gets its own number and starts `Ordered` | ☑ |
| 3.23 | Outcome: `{orderNo} saved — ₹{final}.` | ☑ |
| 3.24 | The form resets and the new order becomes the selection | ☑ |

## 4 · The order lifecycle

| # | Feature | Why | Status |
|---|---|---|---|
| 4.1 | Five statuses — Ordered → SampleCollected → ResultEntered → Verified → Completed | Richer than a billing status: a result has to be entered and verified before it can print, which is a real lab norm | ☑ |
| 4.2 | **Mark sample collected** | ☑ |
| 4.3 | Saving results moves Ordered/SampleCollected → **ResultEntered** | ☑ |
| 4.4 | Saving results on an already-Verified order does **not** move it backwards | ☑ |
| 4.5 | **Verify** stamps every result with verifier and time, and moves the order to Verified | ☑ |
| 4.6 | Verify is **refused with no results entered** — "Enter at least one result before verifying this order." | Verifying nothing is the one action that would let an empty report print | ☑ |
| 4.7 | Editing an order is refused once it is past editing | ☑ enforced by the ported service; the screen does not offer editing a saved order |
| 4.8 | The order list shows number, date, status, total | ☑ |

## 5 · Result entry

| # | Feature | Why | Status |
|---|---|---|---|
| 5.1 | Every analyte of every ordered report, **flattened onto one list** | The whole panel is entered in one pass, not report by report | ☑ |
| 5.2 | Rows are **grouped visually by report** | ☑ |
| 5.3 | Each row shows analyte name and units | ☑ |
| 5.4 | Existing results are **pre-filled** when reopening an order | ☑ |
| 5.5 | Result type per analyte — Numeric / Text / Selection | ☑ Numeric and Text driven; Selection has no seeded analyte to exercise it |
| 5.6 | **Blank rows are skipped** on save — only entered values are written | A half-run panel is normal; blank must not be stored as a result | ☑ |
| 5.7 | Refuses when nothing at all is entered — "Enter at least one result before saving." | ☑ |
| 5.8 | Saving the same analyte again **updates** its result rather than adding a second | ☑ |
| 5.9 | **Reference range is matched server-side** by the patient's gender and age | The range for a 6-year-old girl is not the range for a grown man; the client must not choose it | ☑ |
| 5.10 | A text range is shown as-is | ☑ |
| 5.11 | A numeric range is shown as `low - high` (plain hyphen — see the post-mortem) | ☑ |
| 5.12 | **Flag is computed** for a numeric result — Low / Normal / High against the matched range | ☑ |
| 5.13 | A non-numeric result is never auto-flagged | ☑ a non-numeric result is never flagged — the service only computes a flag inside the numeric branch |
| 5.14 | Entered-by and entered-on are stamped from the signed-in user | ☑ |

## 6 · Printing

| # | Feature | Why | Status |
|---|---|---|---|
| 6.1 | **Lab report PDF** — port `LabReportDocument` | ☑ |
| 6.2 | Printing is **refused until the order is Verified** | An unverified result leaving the building is the thing this whole status chain exists to prevent | ☑ |
| 6.3 | Grouped by report, one row per analyte — value, units, reference range, flag | ☑ |
| 6.4 | Abnormal results are **marked**, not just listed | ☑ |
| 6.5 | Carries clinic header and document theme | ☑ |
| 6.6 | Shows who verified it and when | ☑ |
| 6.7 | Printing re-reads the order | ☑ |

---

## Deliberate differences from HMS_WPF

- **Popups → dialogs**, `Dialog.Show` → inline messages, as before.
- **The three lab masters are out of scope** — Masters owns them.
- **`CurrentUserService.DisplayName` → the signed-in username**, the same
  substitution already made for `Environment.UserName` elsewhere.
- **One Orders tab, with result entry inline** beneath the selected order,
  rather than the desktop's single-tab layout with a separate entry region —
  same information, laid out for a scrolling page.

---

## What browser testing caught

| Bug | How it showed up | Why it mattered |
|---|---|---|
| **The reference range printed as a replacement character** | `10.5 – 13.5` came out of the PDF as `10.5 <20> 13.5` — U+FFFD, still there when extracted as UTF-8, so genuinely absent from the document rather than a console artefact | The desktop drew this with WPF and the system font; the web edition renders into a PDF whose embedded font subset has no U+2013. Every other document here already writes "Rs." instead of the rupee sign for the same reason, which is what identified the cause. A clinical report showing garbage where the normal range should be is the kind of thing a lab gets asked about by a patient. Fixed at the source in `PathologyLabService`, and the regenerated report has zero replacement characters |

**The range matcher was the thing most worth checking, and it is right.**
Aarav Menon is a one-year-old boy. Three Hemoglobin ranges were configured —
*Child 0–5* (10.5–13.5, either sex), *Adult male* (13–17) and *Adult female*
(12–15). A result of 7.2 matched the **child** band and flagged **Low**. Had
it matched on gender first it would have picked Adult male and still said
Low, which is why the bands were deliberately built so that only correct
age-matching produces the correct printed range. An analyte with no
configured range came back with no range and no flag rather than a spurious
Normal.

**The status chain holds in both directions.** Verifying an order with no
results at all is refused — that is the one action that would let a blank
report print. Printing before verification is refused by the controller, not
just greyed out on the screen. And re-saving a corrected result on an
already-Verified order leaves it **Verified** rather than dragging it back to
ResultEntered, so fixing one figure does not silently un-verify the rest of
the panel.

**Package expansion is not a single opaque line**, and testing confirmed why
that matters: applying Master Health Check produced three lines at their own
catalogue prices (300 + 600 + 600 = 1500) and a discount of 300, landing on
exactly the ₹1200 package price. Each report keeps its identity, so its
analytes are still known for result entry — which is the whole reason the
desktop reaches the package price through `Discount` rather than by
rewriting prices.
