# Performance — measured, fixed, re-measured

A performance pass on 23 Aug 2026, against a seeded two-year clinic dataset.

## Method

The shipped dev database held 3 patients and 1 sale, on which everything is
fast and nothing is revealed. So the first step was a realistic volume:

| Table | Rows |
|---|---|
| Patients | 5,003 |
| Visits | 21,540 |
| Sales | 10,915 |
| SaleItems | 37,519 |
| Batches | 1,677 |
| DiagnosticBills | 2,880 |
| DiagnosticBillItems | 7,262 |

Two years of a clinic seeing about 30 patients and taking about 15 pharmacy
bills a day. Every figure below is a median of 9 runs after 3 warm-up calls,
against that dataset on SQLite.

## Results

| Endpoint | Before | After | |
|---|---|---|---|
| GST summary, 1 year | **1,198 ms** | **110 ms** | 10.9x |
| Reminders | 184 ms / 757 KB | 37 ms / 54 KB | 5x / 14x smaller |
| Low stock report | 31 ms | 6 ms | 5x |
| Diagnostics report, 1 year | 114 ms | 32 ms | 3.6x |
| Part packs | 21 ms | 6 ms | 3.4x |
| Patients search | 32 ms | 9 ms | 3.5x |
| Schedule H1, 1 year | 8.4 ms | 3.7 ms | 2.3x |
| Stock to reconcile | 38 ms | 20 ms | 1.9x |
| Dashboard | 168 ms | 126 ms | 1.3x |

Payloads, measured separately because the endpoints are new:

| | Before | After | |
|---|---|---|---|
| Medicine picker (consultation) | 1,780,634 B | 82,013 B | 21.7x |
| Look up one product by id | 1,780,634 B | 3,832 B | 465x |

Under concurrent mixed load (queue, counter search, dashboard, patient
search, day book, reminders):

| Users | Before | After |
|---|---|---|
| 4 | 62.5 req/s, p99 354 ms | 76.7 req/s, p99 205 ms |
| 8 | 44.8 req/s, p99 1,122 ms | 75.7 req/s, p99 432 ms |
| 16 | 48.8 req/s, p99 1,834 ms | 84.9 req/s, p99 693 ms |

Throughput used to *fall* as users arrived — 62, then 45, then 49. It now
holds and climbs: 77, 76, 85, with p99 at sixteen users cut by 2.6x. No
errors at any level.

A control run confirmed the load generator was not the ceiling: against a
trivial endpoint it sustained 914 req/s.

## What was actually wrong

**Aggregates were computed in memory.** The GST summary loaded a financial
year of sales *with every line item* — 5,433 bills and 18,710 lines — and
grouped them in C# to produce five rows. The dashboard did the same for
fourteen days across three modules; the diagnostics report did it for a
year. All four now group in the database and carry back only the answer.

**The reminder call sheet had no lower bound.** `GetDueRemindersAsync` asked
for follow-ups before a cutoff and nothing else, so it returned every
follow-up ever recorded — 2,556 rows reaching back to September 2024,
presented as due. That is a correctness bug that happened to also be slow: a
follow-up missed eighteen months ago is not a call the front desk is about
to make, it is noise burying the ones that are. There is now a six-week
grace window, and the query returns 183 rows.

**Low stock loaded 46x what it needed.** It pulled all 231 active products
with all 1,677 batches, then filtered on a computed property in memory to
find the 5 that were low — on every dashboard load. The comparison is the
same arithmetic; it now happens where the rows are.

**Revenue-by-day scanned every visit.** `FeePaidOn` had no index, so the
dashboard daily takings did a full scan of 21,540 rows. Revenue is counted
on the day a fee was *paid*, which is a different column from the one that
was indexed. Added, with an index on `FollowUpOn` for the reminder query.

**The pharmacy counter downloaded the catalogue to look up one row.**
`products?take=1000` returns every product with every batch — 1.78 MB — and
the counter called it to find a single product by id on every quantity edit,
and again to match a prescription. There is now a single-product endpoint
and a lean catalogue that omits batch history for callers that only pick by
name.

**The dashboard made eleven sequential round trips.** All reads, none
depending on another. They are issued together now, so the page waits for
the slowest rather than the sum — which is most of the concurrency
improvement above.

## Correctness, checked after every rewrite

Aggregation moved from C# to SQL is exactly the kind of change that can be
fast and wrong, so each was checked against the database directly:

- GST slabs match a direct `GROUP BY` to the paisa, and CGST + SGST still
  equals the GST collected — the away-from-zero halving rule survived.
- The dashboard donut still sums to exactly its own KPI tile
  (7,800 + 202,186 + 3,817.49 = 213,803.49).
- The reminder window returns only rows inside it, verified against the
  computed bounds.

## A caveat on the numbers

The baseline was measured with a defect in the *seed data*, not the
application: EF Core stores GUIDs as uppercase text and the seeding script
wrote lowercase, so the case-sensitive comparison in SQLite made every
lookup-by-id miss. `visits/by-patient` therefore returned an empty result in
the baseline run and is not comparable with its final figure. It was
corrected before the final measurement, and no application code was
involved.

## Not done

- **`products?take=500` and `take=1000` still exist** and still return
  1.78 MB for callers that genuinely want batches. The three wasteful
  callers were moved off; the endpoint itself is unchanged.
- **The stock register is 267 KB** because it is a full stock dump — 1,677
  rows is what it is for. It materialises entities rather than projecting,
  which is worth doing if it ever becomes a complaint.
- **SQLite is the ceiling.** WAL is already on. Past roughly this volume the
  answer is a different engine, which `SAAS_MIGRATION.md` already flags as
  revisitable.
