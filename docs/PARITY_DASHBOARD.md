# Dashboard — feature parity with HMS_WPF

Every behaviour the desktop has on this screen, enumerated from the source
(not from memory), with its port status.

Derived from: `DashboardViewModel` (275 lines on
`origin/Dentist_Pathology`), `ChartGeometry`, and the seven services it
reads.

> **Spec source.** This viewmodel differs between branches — 240 lines on
> `main`, 275 on `origin/Dentist_Pathology`. The branch version is the one
> ported: it is the only one that knows Pediatrics, Dentist and Pathology
> Lab exist, and those modules are now built.
>
> ```
> git show origin/Dentist_Pathology:src/Pharma.App/ViewModels/DashboardViewModel.cs
> ```

Status: ☑ done · ◻ not started · ◐ partial

**All 30 items are ☑ as of 22 Aug 2026**, driven against the running app.

---

## 1 · KPI tiles

| # | Feature | Why it exists | Status |
|---|---|---|---|
| 1.1 | **Patients today** — visits on today's date, excluding cancellations | Counts who was *seen*, so it keys off the visit date, not the fee | ☑ |
| 1.2 | Patients delta vs yesterday, as a percentage | ☑ |
| 1.3 | **In queue now** — Booked, Waiting or InConsultation | ☑ |
| 1.4 | **Revenue today** — OPD + Pharmacy + Diagnostics | ☑ |
| 1.5 | Revenue delta vs yesterday | ☑ |
| 1.6 | **Needs restocking** — count at or below reorder level | ☑ |
| 1.7 | Delta reads from a **glyph** (▲/▼), not colour alone | Colour here is decorative reinforcement, not the signal | ☑ |
| 1.8 | Yesterday at zero yields 100% when there is something today, 0% when there is not | Not a divide-by-zero and not an infinite rise | ☑ |
| 1.9 | Tiles link through to the screen behind them | Nothing on this page is a destination in its own right | ☑ |

## 2 · What counts as revenue

| # | Feature | Why | Status |
|---|---|---|---|
| 2.1 | Pharmacy counts **only Completed sales** | A returned or cancelled sale is not money the clinic kept | ☑ |
| 2.2 | OPD counts a fee **when it was paid**, not when the visit was booked | A visit booked today and paid tomorrow is tomorrow's money | ☑ |
| 2.3 | Diagnostics counts bills by bill date | ☑ |
| 2.4 | Diagnostics is excluded entirely when the module is off | ☑ |
| 2.5 | Pediatrics, Dentist and Pathology Lab money is **not** folded into the total | The KPI tile and the donut must agree with each other, and the donut is the OPD/Pharmacy/Diagnostics picture — folding in a fourth category would put money in a total the chart does not account for | ☑ |

## 3 · Today's revenue donut

| # | Feature | Why | Status |
|---|---|---|---|
| 3.1 | One segment per category, sized by share | ☑ |
| 3.2 | A gap between segments | ☑ |
| 3.3 | A zero category draws **no segment at all** | ☑ |
| 3.4 | Percentages beside each, in a legend | ☑ |
| 3.5 | Legend colours match the trend chart's exactly | ☑ |
| 3.6 | Nothing taken yet today says so, rather than drawing an empty ring | ☑ |
| 3.7 | The segments sum to the Revenue KPI **exactly** | ☑ ₹300 + ₹1150 + ₹2100 = ₹3550, the KPI figure |

## 4 · Revenue trend

| # | Feature | Why | Status |
|---|---|---|---|
| 4.1 | 14 days, one line per category | ☑ |
| 4.2 | **One shared scale across all three lines** | Each stretched to its own range would make a tiny category look exactly as busy as the whole clinic — precisely what a "which department is moving" chart must not do | ☑ |
| 4.3 | An end-point dot on each line | ☑ |
| 4.4 | 14-day total shown | ☑ |
| 4.5 | Line and donut shapes only — no bar-per-day grid | A dashboard read at a glance wants "is this going up" and "how does it split"; a bar grid asks the eye to compare seven heights first | ☑ |
| 4.6 | No revenue at all says so rather than drawing a flat line on the floor | ☑ |

## 5 · Recent activity

| # | Feature | Why | Status |
|---|---|---|---|
| 5.1 | Mixed feed — OPD, Pharmacy, Diagnostics, Procedure, Dentist, Pathology Lab | ☑ |
| 5.2 | **Most recent 8**, newest first | ☑ |
| 5.3 | Each row: time, document number, who, department, amount | ☑ |
| 5.4 | Only paid OPD visits and Completed sales appear | ☑ |
| 5.5 | A procedure bill reads **"Procedure"**, not Pediatrics or Dentist | The bill does not carry which department raised it, so guessing would be a lie | ☑ |
| 5.6 | Pediatrics / Dentist / Pathology Lab rows appear only when those modules are on | ☑ |
| 5.7 | Nothing yet today says so | ☑ |

## 6 · Restocking list

| # | Feature | Why | Status |
|---|---|---|---|
| 6.1 | Up to 5 medicines at or below reorder level | ☑ |
| 6.2 | Says "showing 5 of N" when there are more | ☑ |
| 6.3 | Shows on-hand against reorder level | ☑ |

---

## Deliberate differences from HMS_WPF

- **One endpoint, not several.** The whole screen comes from a single
  `GET /api/dashboard` computed in one pass. The KPI tile and the donut only
  reliably agree when they come out of the same arithmetic over the same
  snapshot; splitting them would let the two be fetched either side of a
  sale.
- **The server sends numbers, the browser draws.** The desktop's
  `ChartGeometry` emits WPF `PathGeometry` strings, which mean nothing in a
  browser. The API returns the daily series and the totals; the donut arc
  maths and the trend polyline are computed client-side into SVG. The
  *shape* rules — shared scale, 2.2° gap, 58/36 radii — are the desktop's.
- **A manual Refresh button** rather than the desktop's page-navigation
  reload, since a web page is not re-entered the same way.
- **The daily figures are summed in the database, and the reads are issued
  together.** Added 23 Aug during a performance pass. The screen originally
  loaded a fortnight of sales, visits and diagnostic bills and grouped them
  in C#, then made eleven sequential round trips. The numbers are identical —
  what counts as revenue is unchanged, and the donut still sums to exactly
  its own KPI — but the page now waits for the slowest query rather than the
  sum of all of them. See `PERFORMANCE.md`.

---

## What browser testing caught

No defects. The arithmetic was the part worth checking and it holds: with
₹300 collected at OPD, a ₹1150 pharmacy sale and ₹2100 of diagnostic bills,
the Revenue KPI read **₹3550** and the donut's three segments read ₹300 (8%),
₹1150 (32%) and ₹2100 (59%) — summing to exactly the KPI figure, which is
the property the single-payload design exists to guarantee. All three donut
paths stayed inside the 124×124 viewBox, and all three trend lines carried
14 points on one shared scale with a ₹2100 peak.

The activity feed correctly showed the two newest events (the pharmacy sale
and the OPD receipt, both 11:01 PM) at the top of eight, having pushed the
older Lab and Dentist rows down — which is what "most recent 8" means and
is easy to get backwards.

One environment note, not an app defect: the browser automation's `navigate`
tool drops the path and lands on the SPA root, so every route in this pass
was reached by clicking the real nav link instead. Worth knowing for the
next module's verification.

---

## Verified against the view, 23 Aug 2026

Every item above was written from the **viewmodel**, which gives behaviour.
A later pass compared this module's **XAML view** — the thing that actually
defines the controls and columns on screen — because a column set is a fact
no viewmodel states.

**No gaps.** The activity feed's columns — TIME / BILL / PATIENT / DEPARTMENT / AMOUNT — match exactly.

The comparison used:

```
git show origin/Dentist_Pathology:src/Pharma.App/Views/DashboardView.xaml
```
