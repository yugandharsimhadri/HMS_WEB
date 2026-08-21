# Migrating to multi-tenant SaaS

An assessment written at the desktop freeze, to decide what moves, what is
rewritten, and in what order. Findings marked **verified** were checked against
the source, not inferred.

Companion reading: [BUSINESS_RULES.md](BUSINESS_RULES.md) is the specification
the port must satisfy; [SERVICE_REFERENCE.md](SERVICE_REFERENCE.md) is the API
being moved.

---

## The short version

**The domain layer is already portable. The UI and printing are not.**

`Pharma.Core` and `Pharma.Data` target plain `net10.0` and contain **zero WPF
references** — verified. The entities, the nine services, the seeders and the
GST arithmetic can move behind an ASP.NET Core API largely intact. That is the
expensive half of a hospital system and it already exists.

What does not move: every View and ViewModel, and the entire printing
subsystem, which is built on `FlowDocument` — a WPF type with no server
equivalent.

---

## Four things that break under concurrency

All four are invisible on a single-user desktop, which is exactly why they are
dangerous. Each is **verified against the code**.

### 1 · Document numbering fails under load — blocker
`NumberService.NextAsync` reads `Counter.LastNumber`, increments in memory, and
saves. No lock, no retry. Two simultaneous requests both read *41* and both
format `INV00042`.

A **unique index does back every document number**, so the second save fails
with a constraint violation rather than silently duplicating. The web failure
mode is therefore *a bill that randomly refuses to save under load* — loud
rather than corrupting, and still unacceptable.

**Fix:** an atomic per-tenant allocation — `UPDATE … RETURNING`, a database
sequence, or catch-and-retry around the violation. Do not port this method as
written.

### 2 · Oversold batches — blocker
Stock deduction reads `batch.QtyOnHand`, computes
`Math.Min(remaining, QtyOnHand)`, then writes the reduced figure. Two counters
selling the last strip simultaneously both succeed and drive stock negative,
past the very check the code exists to enforce.

**Fix:** optimistic concurrency plus retry, or push the decrement into a
conditional `UPDATE` with a `WHERE QtyOnHand >= @n` the database adjudicates.

### 3 · No concurrency tokens anywhere — blocker
Not one entity carries a `RowVersion` — verified across `Pharma.Core` and
`Pharma.Data`. On the desktop nothing else writes, so last-write-wins is
invisible. On the web, two people editing the same case silently discard one
another's changes.

**Fix:** add a concurrency token to `BaseEntity` — one change that propagates to
all 45 entities — and surface the conflict in the API.

### 4 · Ambient clock, 30 call sites — high
`DateTime.Now` / `DateTime.Today` appear **30 times** across `Pharma.Data`. On a
clinic PC that is the clinic's wall clock. On a server it is the *server's*, so
a bill taken at 00:30 IST lands on the previous day's book under UTC, and every
"today" query disagrees with the front desk.

**Fix:** an injected clock plus a timezone on the tenant. Mechanical, but do all
30 before launch.

---

## What transfers

| Layer | Verdict | What it needs |
|---|---|---|
| `Pharma.Core` — entities, enums, calculators | **Keep ~95%** | Add `TenantId` and a concurrency token to `BaseEntity`; both propagate to every entity free |
| `Pharma.Data` — 9 services | **Keep 70–80%** | Fix the four findings; inject the clock; apply tenancy and soft-delete centrally |
| Seeders | **Keep ~95%** | Become tenant provisioning — a new clinic is usable seconds after signup |
| `Pharma.Data/Import/` | **Keep ~90%** | Distributor bill parsing is provider-agnostic; only the file-upload path changes |
| `Pharma.Data/Licensing/` | **Keep for on-prem** | Already handles expiry, clock-rollback and tamper detection. SaaS entitlement is a separate server-side check — but the on-prem edition needs nothing built |
| `Pharma.Data/Security/` | **Keep** | `PasswordHasher`. Re-evaluate the algorithm against current guidance before launch |
| `tests/Pharma.Tests` | **Keep most** | Service-level, so they survive and become the safety net. Add cross-tenant isolation tests |
| EF migrations | **Regenerate** | SQLite-specific in places. Fresh chain for the target engine; keep the old one to read desktop files during import |
| Printing — 10 documents | **Rewrite** | `FlowDocument` is WPF. **The most under-estimated item here** |
| Reports — PDF/Excel builders | **Rewrite** | Re-target to a server-side generator |
| Views & ViewModels — 48 XAML | **Rewrite** | Zero reuse. The *designs* transfer — every screen is captured and annotated in [USER_GUIDE.md](../USER_GUIDE.md) |
| `tests/Pharma.UiTests` — FlaUI | **Rewrite** | Playwright. The scenarios translate nearly line for line |

Roughly **40% of the codebase by volume** carries over — but it is the 40% that
took longest to get right.

---

## Four decisions to make first

### 1 · How isolated is each clinic's data?

**Shared database, `TenantId` on every row** is cheapest to run and simplest to
migrate. The risk is severe and one-directional: one missing filter leaks one
clinic's patients into another's screen.

**Database per clinic** gives real isolation, per-clinic backup and restore, and
a trivial answer to "export my data" — at the cost of migration fan-out and
connection pressure.

**Recommended:** shared schema, `TenantId` on `BaseEntity`, and a **global query
filter applied centrally in `AppDbContext`** — never per query, so it cannot be
forgotten. Then a test that enumerates every entity type and fails if one lacks
the filter. Keep `TenantId` even in a dedicated database, so moving a large
clinic out later stays a deployment change rather than a schema change.

> Note this also fixes the existing per-call-site `!IsDeleted` pattern, which has
> the same forgettability problem. Do both filters in the same place.

### 2 · What does the web UI use?

Dense grids, keyboard-driven billing, heavy printing.

- **React + TypeScript** — strongest ecosystem for exactly this shape (virtualised
  grids, keyboard flows), larger hiring pool; a new toolchain for a .NET team.
- **Blazor WebAssembly** — stay in C#, share validation and models with the
  backend; smaller grid ecosystem, larger initial download.

**Recommended:** React + TypeScript if this is to be sold broadly and hired for.
Blazor if the team is small and shipping speed beats ecosystem. The honest
tiebreaker is who maintains it in three years.

### 3 · What happens to "runs offline"?

*Runs offline, no internet needed* is currently one of the strongest sales
lines — it is in the product film and in the user guide. SaaS spends it. For a
clinic on an unreliable connection, a browser tab that stops taking payments
during an outage is not a smaller product, it is an unusable one.

**Recommended:** do not frame SaaS as the replacement. It is the edition for
clinics that want access anywhere and no IT; the on-premise edition stays a
deliberate, priced product for everyone else.

### 4 · One codebase or two?

The trap is maintaining two UIs forever — every feature built twice, every bug
fixed twice, and the desktop quietly rotting.

**Recommended, and the strategic move:** build the web UI once and ship the
desktop edition as **that same UI inside a WebView2 shell, with the API and
database running locally**. One UI codebase, one API contract, two deployment
targets. The desktop keeps working offline because everything is on the machine.
The current WPF app is then frozen honestly — supported, not extended.

---

## Phased plan

### Phase 0 — Freeze *(days)*
Tag the release as the supported on-prem line. Be strict: every feature added to
WPF from here is a feature built twice.

### Phase 1 — Harden the domain *(4–6 weeks)*
- `TenantId` and a concurrency token on `BaseEntity`.
- Fix the numbering and stock races. **Write the concurrent test first and watch
  it fail against the current implementation** — otherwise the fix is unverified.
- Replace the 30 ambient clock calls with an injected clock.
- Move to the target database provider; regenerate migrations.
- **Prove printing.** Port one document — the pharmacy invoice — end to end on
  the server. Now, not in month five.

### Phase 2 — Platform *(4–6 weeks)*
- ASP.NET Core API over the existing services; auth with a tenant claim.
- Signup provisions a tenant and runs the seeders.
- Plan entitlements reuse the existing module toggles.
- Backups, restore drills, structured logging — previously the clinic's problem,
  now the vendor's.

### Phase 3 — The UI *(the long pole)*
Rebuild in revenue order: **OPD and Pharmacy first** — that is most clinics and
most of the daily value. Then Patients and Reports, then the specialty modules,
each separately sellable. Ship to friendly clinics after OPD + Pharmacy rather
than waiting for parity.

The annotated screenshots in [USER_GUIDE.md](../USER_GUIDE.md) are the closest
thing to a UI specification available; use them as the acceptance reference.

### Phase 4 — On-premise edition
WebView2 over a local API and SQLite. If the API contract has been kept honest,
this is packaging and installer work rather than product work.

### Phase 5 — Conversion
An importer that takes a clinic's `twinkle.db` and provisions a tenant from it.
The schemas share a lineage, so this is achievable — and it is the difference
between "start again" and "you will be live this afternoon" when calling
existing customers.

---

## Packaging is already built

The feature toggles are the most SaaS-ready thing in the product: seven modules
that already switch independently and already hide themselves everywhere when
off. That is a plan-tier system wearing a checkbox.

| Tier | Modules |
|---|---|
| **Core** | OPD + Pharmacy |
| **Clinic** | + Appointments, Diagnostics |
| **Specialty** | + Pediatrics / Dentistry / Pathology, priced per module |
| **On-premise** | The same product, annual licence |

Move entitlement from a local setting to a server-side subscription check and
the pricing model is done. Keep data export prominent and free — *one file holds
your whole clinic* is a trust argument already being made, and it costs nothing
to keep making.

---

## Also worth planning for

| | |
|---|---|
| **Compliance** | Health records under India's DPDP Act 2023 — consent, retention, breach notification, data residency. Cheaper to design in than to retrofit under procurement review |
| **Authorisation** | Roles are attribution only today. Server-side enforcement is **new behaviour to design**, not behaviour to port |
| **Numbering prefixes** | Global today. Clinics will want their own, and two clinics sharing `INV` confuses the moment anyone compares paperwork |
| **Logos and files** | A local file today. On the web: object storage, size limits, content-type checks, unguessable per-tenant paths |
| **Money precision** | SQLite stores decimals as TEXT. A real decimal type removes a whole class of external-reporting hazard |

---

## If you do one thing first

**Prove the printing.** Everything else here can be estimated. Printing is the
one item where the unknown is large and late discovery is expensive — ten
documents, all on a WPF type with no server equivalent, all of them things a
clinic legally hands to a patient.

Take the pharmacy invoice, with its GST breakdown and amount-in-words, and
produce it from a web request as a PDF a pharmacist would accept. Perhaps a
week. If it goes smoothly the rest is sequencing; if it does not, that is known
in week one rather than month five, while the PDF strategy is still cheap to
change.
