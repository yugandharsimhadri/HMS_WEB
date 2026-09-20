# Moving from SQLite to SQL Server Express

> **Historical.** The application has since moved again, from SQL Server to
> PostgreSQL — see `POSTGRESQL_SETUP.md` for the current database, and its
> §6 for what changed in that second move. This document is kept because the
> reasoning in it outlived the provider: what a numbering allocator has to
> guarantee (§1.1), why `EnableRetryOnFailure` is still off (§1.2), and why
> the migration history had to start over — which it did again.


A plan, and the reasoning behind each decision. Grounded in what this
codebase actually does — every file named below was read, not assumed.

The headline: **the schema is the easy part.** EF Core will generate it. What
needs thought is four things — the numbering allocator, transactions under a
retry policy, the migration history, and how scripts stay in step afterwards.

---

## 1 · What actually blocks it

Four items, in descending order of danger.

### 1.1 `NumberService` is written in SQLite ⚠ the real work

`backend/src/SivayaanHMS.Data/NumberService.cs` casts straight to
`SqliteConnection` and issues SQLite-only SQL:

```sql
INSERT INTO Counters (...) VALUES (...)
ON CONFLICT(TenantId, Name) DO UPDATE SET LastNumber = LastNumber + 1
RETURNING Prefix, LastNumber;
```

plus `PRAGMA busy_timeout` and `PRAGMA journal_mode=WAL`.

None of that exists on SQL Server. This is the one piece that has to be
rewritten rather than reconfigured, and it is the piece where a mistake is
worst: it allocates patient numbers, visit numbers, invoice numbers and GRN
numbers. A duplicate invoice number in a statutory register is not a bug you
can apologise for.

**The SQL Server equivalent**, and why not the obvious one:

```sql
SET XACT_ABORT ON;
BEGIN TRANSACTION;

UPDATE Counters WITH (UPDLOCK, HOLDLOCK)
SET LastNumber = LastNumber + 1
OUTPUT inserted.Prefix, inserted.LastNumber
WHERE TenantId = @tenantId AND Name = @name;

IF @@ROWCOUNT = 0
    INSERT INTO Counters (Id, TenantId, Name, Prefix, LastNumber, CreatedAt, IsDeleted, RowVersion)
    OUTPUT inserted.Prefix, inserted.LastNumber
    VALUES (@id, @tenantId, @name, @prefix, 1, SYSDATETIME(), 0, @rowVersion);

COMMIT;
```

`UPDLOCK, HOLDLOCK` is what serialises two callers on the same counter — it
takes an update lock and holds it to the end of the transaction, so the
second caller waits rather than reading a stale `LastNumber`.

**Not `MERGE`.** It reads more neatly and has a long history of concurrency
and correctness bugs under exactly this pattern. The update-then-insert above
is duller and better understood.

The existing trade stays: a number allocated for a document that then fails
to save is burned, not reused. Gaps are fine; duplicates are not.

### 1.2 Explicit transactions vs. the retry strategy ⚠ easy to miss

There are **nine** `BeginTransactionAsync` call sites across
`PharmacyService`, `DiagnosticsService`, `PathologyLabService`,
`ProcedureBillsService` and `DataHealthService`.

The standard advice for SQL Server is to switch on
`EnableRetryOnFailure()` for transient faults. **Doing that breaks all nine
at runtime**, not at compile time: with a retrying execution strategy EF Core
throws `InvalidOperationException` the moment user code starts its own
transaction, because it cannot safely replay a transaction it did not open.

Each site has to become:

```csharp
var strategy = db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync();
    // ... existing body ...
    await tx.CommitAsync();
});
```

Two consequences worth stating plainly. The body may now run **more than
once**, so anything non-idempotent inside it — a number allocated, a counter
bumped, a file written — has to be re-examined. And the wrapped body cannot
close over state mutated by an earlier attempt.

This is the item most likely to reach production unnoticed, because it only
fires under transient failure, which is exactly when nobody is watching.

### 1.3 Migration history is provider-specific

Five migrations exist under `SivayaanHMS.Data/Migrations`, all emitting
SQLite DDL. EF Core migrations are not portable between providers — the
snapshot alone contains ~600 `"TEXT"` column types, because SQLite stores
decimals and dates as text.

### 1.4 Existing SQLite data has to move

Five clinics exist in the dev database today. Real deployments will have
more, and their decimals and dates are stored as text.

---

## 2 · Decisions I would take

### 2.1 One provider, not two

**Recommendation: move to SQL Server everywhere, including tests. Drop
SQLite entirely.**

Dual-provider looks attractive — keep SQLite for the zero-setup developer
experience — and is a permanent tax:

- two migration trees to keep in step, forever
- two dialects of the numbering SQL, of which only one is ever exercised by
  the concurrency tests that matter
- a whole class of "passes on SQLite, fails on SQL Server" bugs, which are
  the most expensive kind because they surface only in the environment where
  they cost money

The zero-setup argument is real but cheap to replace: **SQL Server LocalDB**
on a developer's Windows machine, or a container elsewhere. That is a
one-line connection string, not a design constraint.

**The concurrency tests are the deciding argument.**
`NumberServiceConcurrencyTests` and `PharmacyServiceConcurrencyTests` exist
to prove that two simultaneous callers cannot get the same number or oversell
the same batch. Run against SQLite, they would prove that about a database
nobody uses. They must run against SQL Server or they are theatre.

### 2.2 A fresh baseline, not a converted history

**Recommendation: delete the five SQLite migrations, generate one
`InitialCreate` for SQL Server.**

Migration history exists to get an *existing* database from one shape to
another. No SQL Server database exists yet, so the five SQLite migrations
have exactly one job — get from nothing to the current model — and a single
generated `InitialCreate` does that job better and more legibly.

Existing SQLite **data** is a separate concern, handled by section 3, not by
migrations.

Keep the old migrations on a tag or a branch if anyone wants the archaeology.

### 2.3 Leave `RowVersion` alone for now

SQL Server has a native `rowversion` type that the database maintains itself,
and `AppDbContext.Stamp()` currently regenerates a `byte[]` by hand because
SQLite has no such thing.

Native `rowversion` is genuinely better — the database enforces it rather
than trusting every write path to remember. **But not in the same change.**
Provider migration is already touching the numbering allocator and nine
transaction sites; adding a change to the concurrency token means that when
something goes wrong there are three candidates instead of one.

The hand-generated `byte[]` works correctly on SQL Server. Treat native
`rowversion` as a deliberate follow-up with its own tests.

### 2.4 Keep `decimal(12,2)`

`AppDbContext.OnModelCreating` already forces precision 12, scale 2 on every
decimal in the model. That maps straight onto SQL Server `decimal(12,2)` with
no ambiguity — and it means money arrives as a real decimal rather than the
text SQLite was storing. This is a straight improvement and needs no work.

---

## 3 · Moving the data

All primary keys are client-generated `Guid`s. That removes the usual worst
part of a database move — there are no identity columns to reseed and no
key remapping.

**Recommendation: a small one-off console tool**, not a SQL script.

It opens an `AppDbContext` on SQLite and one on SQL Server and copies entity
by entity in dependency order, per tenant. Reasons to prefer it over
`bcp`/SSIS/a hand-written script:

- The text→decimal and text→datetime conversions come free: EF materialises
  from SQLite into real CLR types and writes real SQL Server types.
- It can run **one clinic at a time**, so each customer is verified before
  the next is touched.
- It can assert as it goes — row counts per table, and the money totals per
  tenant. A clinic whose collections total differs by a paisa after the copy
  has not been migrated.

Order matters: `Tenants` first, then master data, then transactional rows
whose foreign keys point at them.

Turn off the tenant query filter for the read side (`IgnoreQueryFilters`) and
set the tenant explicitly on the write side — the copier is the one
legitimate cross-tenant caller, alongside the platform console.

**Verification before cutover**, per tenant:

| Check | Why |
|---|---|
| Row count per table | Catches a silently truncated copy |
| Sum of `Sale.NetAmount` | Catches decimal conversion loss |
| Max of each counter vs. max document number | Catches numbering that would collide after cutover |
| A generated PDF, compared to one from before | End-to-end proof, in the form a clinic would notice |

The counter check is the one people forget. If `Counters.LastNumber` does not
match the highest existing document number, the first invoice after cutover
duplicates an old one.

---

## 4 · Keeping the scripts up to date — the part you asked about

Three mechanisms, and I would use all three because they catch different
failures.

### 4.1 An idempotent script, regenerated by CI

```bash
dotnet ef migrations script --idempotent --output db/migrate.sql
```

`--idempotent` wraps every migration in a check against
`__EFMigrationsHistory`, so the file is safe to run against a database at any
version — including one already up to date. That is what makes it handable to
a DBA or a customer's IT person without a runbook.

**Automate it as a guard, not just a generator.** A CI step that regenerates
the script and fails the build if it differs from the committed one:

```bash
dotnet ef migrations script --idempotent --output db/migrate.generated.sql
git diff --exit-code db/migrate.sql db/migrate.generated.sql
```

This is what makes it *automatic* in the sense that matters: the script
cannot silently fall behind the model, because the build stops.

### 4.2 A pending-changes guard ⚠ the highest-value one

The commonest failure is not a stale script. It is somebody editing an entity
and forgetting to add a migration at all — everything builds, everything
passes, and the column is missing in production.

```bash
dotnet ef migrations has-pending-model-changes
```

Non-zero exit means the model has moved without a migration. Run it on every
pull request. This one check would have caught more real incidents than the
other two combined.

*(This repo has already been bitten by the neighbouring problem — a
`PendingModelChangesWarning` at startup caused by `dotnet run --no-build`
using a binary compiled before the migration existed. It is written up in
`docs/HANDOFF.md`.)*

### 4.3 Migration bundles, for anywhere without the SDK

```bash
dotnet ef migrations bundle --self-contained -r win-x64 -o db/migrate.exe
```

A single executable that applies migrations and needs no .NET SDK, no EF
tools and no source on the target machine. This is the right shape for **SQL
Express on a clinic's own server**, which is presumably where this is going —
you cannot ask a clinic's IT contractor to install the .NET SDK.

### 4.4 Stop migrating at startup

`Program.cs` currently calls `db.Database.MigrateAsync()` on boot in
Development. That is fine for one developer and wrong for a deployment: two
instances starting together race each other, and the application needs schema
rights it should not hold at runtime.

Migration becomes a **deploy step** — the bundle or the idempotent script —
run once, before the new build serves traffic. Keep the startup call behind
`IsDevelopment()` where it already is, and never let it past that.

---

## 5 · Suggested order

Each phase leaves the system working.

| # | Phase | Why here |
|---|---|---|
| 1 | Add the SQL Server package; make the provider configuration-driven | Nothing changes yet; both can be exercised |
| 2 | **Rewrite `NumberService`** and run its concurrency tests against real SQL Server | The hardest and most dangerous piece, done while everything else is still known-good |
| 3 | Wrap the nine transaction sites in an execution strategy; enable retries | Must land with, or before, retries — never after |
| 4 | Fresh `InitialCreate`; point tests at SQL Server; delete the SQLite provider | The point of no return, taken once the risky code is proven |
| 5 | Build and rehearse the data copier against a copy of real data | Rehearsal is not optional; the counter check is the one that bites |
| 6 | Wire the three CI guards from section 4 | Now that migrations matter, stop them drifting |
| 7 | Cut over one clinic, verify, then the rest | Small blast radius first |

Phases 2 and 3 are where the real risk is. Phase 4 feels like the big one and
is mostly mechanical.

---

## 6 · Worth knowing, not blocking

- **`.ToLower()` on both sides** (three sites, `AuthService` among them) was
  written because SQLite's `instr()` is case-sensitive. SQL Server's default
  collation is already case-insensitive, so it is redundant — and worse than
  redundant: it makes the predicate non-sargable, so the index on `Username`
  cannot be sought. Remove it once on SQL Server, and confirm the collation
  rather than assuming it.
- **`EF.Functions.Like("%term%")`** — a leading wildcard cannot use an index
  on any provider. Fine at a clinic's data volume; worth revisiting with
  full-text search if a catalogue grows into six figures.
- **Connection string** needs `TrustServerCertificate=True` against a default
  local Express instance, and `Encrypt` set deliberately rather than by
  accident.
- **Express limits** — 10 GB per database, 1 GB RAM, one socket. Ample for a
  clinic; worth knowing before it is proposed for a hundred of them on one
  box, since this is a single shared database for all tenants.
- **Backups become possible**, which is the open question in
  `docs/GAP_ANALYSIS.md` §4. SQL Server has a real backup story where a
  single SQLite file had none. Worth folding that decision into this work
  rather than leaving it open.

---

## 7 · What I would not do

- **Do not run both providers "just in case".** It doubles the migration
  surface and halves the value of every concurrency test.
- **Do not port the five SQLite migrations by hand.** Regenerating is
  minutes; hand-porting is hours and gets the column types wrong.
- **Do not enable `EnableRetryOnFailure` before phase 3.** It fails at
  runtime, under load, in the nine places that move stock and money.
- **Do not migrate on startup in production**, however convenient it looks.
