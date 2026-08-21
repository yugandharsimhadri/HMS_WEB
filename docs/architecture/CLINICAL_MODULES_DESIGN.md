# Clinical modules — design (Appointments, Pediatrics, Dentist, Pathology Lab)

Status: **design only, nothing in this document is built**. It exists so the
modules below can be implemented later without re-deriving the same
decisions, and so the decisions themselves can be reviewed before any code
is written.

Every module described here — the four new ones, plus OPD and Pharmacy,
which exist today but are hardcoded rather than optional — is independently
switchable from Settings → Features. A clinic turns on only what it uses: a
dental practice runs Dentist alone, a pediatrician runs Pediatrics alone, a
small general clinic runs OPD + Pharmacy + Pathology Lab. See "Module
architecture" below for exactly how that independence holds up against the
real entities, and where the two or three genuine couplings are.

Every entity, service and screen sketched here follows patterns already living
in this codebase — `BaseEntity`, the `Diagnostics` module, `SettingsService`'s
key/value store, `NumberService`, `DocumentBuilder`/`PrintService`. Where a
pattern is reused rather than invented, the source is named so it can be
checked against the real file.

---

## Read this first — open questions

Four things below are genuine decisions, not implementation detail. Everything
else in this document assumes a specific answer to each; if the answer should
be different, the affected section needs revising before implementation starts.

1. **Resolved — a sample report was reviewed.** It was a real commercial lab
   report (BioReference Laboratories) carrying the patient's own personal
   details (name, DOB, address, phone) and actual results. Only its
   *structure* — header regions, panel grouping, column layout — is reflected
   in §4's design below; none of the personal data in it appears anywhere in
   this document. One instruction came out of reviewing it directly: **the
   sample showed a "Prior Result" and "Prior Result Date" column next to
   every result, and these are explicitly not wanted** — confirms the
   existing 3.6 decision below ("previous results are not required") rather
   than changing it.

2. **How the new Pathology Lab module relates to the Diagnostics module that
   already exists** (`DiagnosticTest` / `DiagnosticBill`, shipped and working
   today). They overlap: both bill lab work against a patient. They also
   differ sharply: Diagnostics bills a flat named test with no result capture
   at all; Pathology Lab needs analyte-level results, reference ranges, and a
   printed report. §4 (Pathology Lab, below) recommends it as the richer module for the
   clinic's **own** in-house lab, with Diagnostics kept as-is for ad-hoc or
   sent-out tests that only need billing, never a printed result. Confirm
   this before building — the alternative is retiring Diagnostics and
   migrating its data into the new module.

3. **Growth chart percentile curves** (§1.4) need a real reference dataset —
   WHO or IAP child growth standards, by age and sex. That is a data-sourcing
   task, not a schema decision, and is called out again in §1.4 so it is not
   missed at planning time.

4. **Dental billing is the first place this app bills in installments.**
   Every existing bill type (`Sale`, `DiagnosticBill`, the fee receipt) is
   paid in one shot. A root canal paid across three sittings is not, so §2.7
   introduces the app's first multi-payment-against-a-running-balance model.
   Worth a deliberate look before it is built, since nothing existing can be
   copied wholesale for it.

---

## Module architecture — integrated when combined, independent when not

The brief for this pass: a dentist-only clinic runs with nothing but the
Dentist module (plus whatever is unavoidably shared underneath it); a
pediatrician runs with nothing but Pediatrics; a small general clinic picks
OPD + Pharmacy + Pathology Lab and gets exactly that, no more. Every
module — **including OPD and Pharmacy, which are hardcoded and always-on in
the app as it exists today** — becomes one more independent toggle. When two
modules happen to both be switched on, they interoperate (a dentist's case
opens straight against a shared patient who is also on today's OPD queue);
when one of them is off, nothing in the other module ever notices it is
missing.

### Core / Platform — never a toggle

A handful of things exist unconditionally, because every module needs them
and none of them is itself a clinical capability a clinic could sensibly
switch off:

- `Patient`, `Doctor` — shared master data. Every module below bills or
  treats a patient; none of them owns the patient record.
- `SettingsService`, clinic/pharmacy identity, `DocumentTheme` — the shell's
  own configuration, not a business module.
- Printing (`DocumentBuilder`, `PrintService`), `NumberService` — shared
  infrastructure a clinic never "uses" directly.
- Users/login (`RequireLogin`) — already its own toggle, but a security
  feature orthogonal to clinical modules, not one of them.
- The shell itself: navigation, Settings, and Dashboard's *frame* — though
  Dashboard's *content* has to become module-aware, see below.

### Every business module is a toggle, including the two that are not today

| Module | Today | Design |
|---|---|---|
| OPD (queue, consultation, prescription, fee) | Always on, hardcoded | Toggleable, **defaults on** |
| Pharmacy (counter sale, inventory, stock) | Always on, hardcoded | Toggleable, **defaults on** |
| Diagnostics (flat lab-test billing) | Already toggleable, defaults off | Unchanged |
| Appointments | — | New, toggleable, defaults off |
| Pediatrics | — | New, toggleable, defaults off |
| Dentist | — | New, toggleable, defaults off |
| Pathology Lab | — | New, toggleable, defaults off |

OPD and Pharmacy are the two deliberate exceptions to "every module defaults
off" — every clinic already running this app today is already using both,
and an upgrade that silently switched either off would break them the
moment they opened it. New installs still see both on by default too;
turning either off is something a dentist- or pediatrician-only clinic does
once, during setup, not a state anyone has to discover.

### Dependency table — what each module actually needs

This is what makes "individual" true, checked against the real entities
(existing and designed): **every module's only hard dependency is Patient
and/or Doctor — both Core.** Every link to another *business* module is
already, or is made, optional:

| Module | Hard dependency | Soft link to another module | What happens when that module is off |
|---|---|---|---|
| OPD | Patient, Doctor | Diagnostics (`VisitDiagnosticRequest`) | Consultation just never offers "request a test" |
| Pharmacy | — (a walk-in sale carries no patient at all) | OPD (`Sale.VisitId`, `Sale.PatientId` — both already nullable) | Every sale is a plain counter sale, exactly as a standalone pharmacy runs today |
| Diagnostics | Patient | OPD (`DiagnosticBill.VisitId`, nullable) | `ReferredBy` is always asked instead of being skipped — already the exact rule for a walk-in referral |
| Appointments | Patient, Doctor | Whichever module it is booked for — see check-in routing, below | That module's context simply never appears in the booking screen's own list |
| Pediatrics | Patient | OPD (`GrowthMeasurement.VisitId`, `ProcedureBill.VisitId` — both nullable) | Vaccination and growth tracking read and write against the patient directly; no visit ever exists to link to |
| Dentist | Patient, Doctor | — (no `VisitId` anywhere in `DentalCase`) | Nothing changes — Dentist never touched OPD's `Visit` in the first place |
| Pathology Lab | Patient | OPD (`LabOrder.VisitId`, nullable) | Same as Diagnostics — `ReferredBy` is always asked |

Read the other way: **a dentist-only clinic needs Patient, Doctor, and the
Dentist module — OPD, Pharmacy, Diagnostics, Pediatrics and Pathology Lab
can all be off and nothing in Dentist notices.** A pediatrician-only clinic
needs Patient, Doctor, and Pediatrics, full stop. A small clinic wanting
OPD + Pharmacy + Pathology Lab gets exactly that combination — Pediatrics
and Dentist stay out of the way entirely, the same way Diagnostics already
stays out of the way today when its own toggle is off.

### The one real coupling this forces a fix to: Appointment check-in

§1.3 as first drafted always converts a checked-in appointment into an OPD
`Visit` — fine when OPD is on, wrong the moment it is not, since a
dentist-only clinic makes no use of `Visit` at all. **Check-in is
module-routed instead**: `Appointment.ModuleContext` decides which module's
own primary record gets created, never a hardcoded `Visit`:

- `General` → creates a `Visit` — only offered as a booking context at all
  when OPD is switched on.
- `Dentist` → opens or continues a `DentalCase` (§2.2) directly — never
  touches `Visit`.
- `Pediatrics` → behaves like `General` when OPD is also on (a pediatrician
  who also runs a walk-in queue); when OPD is off, check-in records the
  encounter straight onto `GrowthMeasurement`/`VaccinationRecord` — no
  `Visit` involved either way.
- `PathologyLab` → creates a `LabOrder` (§4) directly.

`Appointment` drops the dedicated `VisitId` field from §1 below in favour of
one generic `LinkedRecordId` (`Guid?`) — an advisory pointer, not a queried
foreign key, into whichever table `ModuleContext` says it points at. It is
only ever read back to jump straight to "the record this appointment turned
into," never joined against in a query, so it does not need a different
typed FK per module the way a real relationship would.

### Settings → Features needs an "at least one module" guard

With OPD and Pharmacy themselves switches now, it becomes possible to turn
every module off at once — an empty nav bar, an unusable app.
`SettingsViewModel.SaveFeaturesAsync` should refuse to save a state where
every toggle is false, the same way a form already refuses to save with a
required field blank, with a message that says so plainly rather than
silently re-enabling something behind the operator's back.

### What this changes about the *existing* app, not just the new modules

This is a bigger change than adding four optional modules, and worth being
honest about before it is built:

- **`Dashboard`** is OPD/Pharmacy-shaped today (today's queue, today's
  sales, low stock). It has to become module-aware — each tile checks its
  own module's toggle, the same as a nav button does — so a clinic running
  only Dentist sees a Dentist-shaped dashboard (today's cases, this week's
  collections, overdue balances) instead of an empty OPD one.
- **`DbBootstrapper`**'s seed data (`ImportProfileSeeder`, demo doctors,
  demo products) currently assumes a pharmacy-and-OPD clinic. It needs to
  stop seeding a module's data while that module is off, and seed a
  module's own defaults only the first time it is switched on — the same
  lazy-seed shape already designed for `DiagnosticTestSeeder`.
- **`MainViewModel`**'s startup (`GoAsync("dashboard")`) does not need to
  change — the shell's frame stays the same — but Settings → Features has
  to stay reachable regardless of what else is switched on, so a brand new
  install, or a clinic that switches everything to just one module, always
  has a way back to turn something on.

### Illustrative combinations (not special-cased — just toggles composed)

None of the following are built as named "profiles" in code — they are what
falls out of the toggle table above, listed here only to check the design
against the brief:

| Clinic | Modules on |
|---|---|
| Dentist-only practice | Dentist (+ Appointments, optional) |
| Pediatrician-only practice | Pediatrics (+ Appointments, optional) |
| Small general clinic | OPD, Pharmacy, Pathology Lab |
| Full multi-specialty clinic | Everything |

---

## 0. Conventions this design reuses, unchanged

Read in full in [Entities.cs](../../src/Pharma.Core/Entities.cs),
[AppDbContext.cs](../../src/Pharma.Data/AppDbContext.cs),
[SettingsService.cs](../../src/Pharma.Data/SettingsService.cs),
[DiagnosticsService.cs](../../src/Pharma.Data/DiagnosticsService.cs) and
[NumberService.cs](../../src/Pharma.Data/NumberService.cs). Every module below
is built the same way:

- **Every entity is a `BaseEntity`** — `Id`, `CreatedAt`/`UpdatedAt`,
  `IsDeleted` (soft delete, filtered explicitly in services, not a global EF
  query filter), `CreatedByUserId`/`UpdatedByUserId` (stamped automatically by
  `AppDbContext.Stamp()` when login is on, null forever if it never is).
- **A billed/printed line always denormalizes** the name and price it billed
  at (`SaleItem.ProductName`, `DiagnosticBillItem.TestName`). A later master
  edit, or the master row being deleted outright, must never change what an
  already-printed document shows. Every new bill/result line below follows
  this without exception.
- **A master row referenced by history is never hard-deleted** —
  `DiagnosticsService.DeleteTestAsync` refuses once the test has been billed
  and tells the operator to deactivate it instead. Every new master (vaccine,
  procedure, analyte, package) gets the same guard.
- **Sequential document numbers** come from `NumberService.NextAsync`, one
  `Counter` row per document type, incremented inside the same transaction
  that writes the document — never a gap. New prefixes are listed in §5.
- **A settings-gated optional module** is one `bool` on `GeneralSettings`
  (e.g. `DiagnosticsEnabled`), a matching key in `SettingsService`, a
  checkbox on Settings → Features (`SettingsView.xaml`, the `ToggleSwitch`
  style), and a mirrored `bool` on `MainViewModel` that a nav button's
  `Visibility` binds to — pushed live the moment Features is saved, via the
  same "reach the shell through the service locator" call
  `SettingsViewModel.SaveFeaturesAsync` already makes. No restart needed.
  "Module architecture" above extends this to six toggles total — the four
  new modules, plus OPD and Pharmacy themselves — the same way.
- **A screen is an `IPage`** (`Title`, `Subtitle`, `LoadAsync`) registered in
  `MainViewModel`'s constructor and `GoAsync` switch, exactly like
  `DiagnosticsViewModel` is today.
- **A printed document** is a `FlowDocument` built by a static `…Printer`
  class in `Pharma.App/Printing`, using the shared `DocumentBuilder` helpers
  (`AddClinicHeader`/`AddPharmacyHeader`, `IdentityRow`, `Row`, `TotalRow`,
  `ApplyPrintSettings`), shown via `PrintService.Preview`. Every new document
  below reuses this rather than building its own layout code.

---

## 1. Generic Appointments (used by every module, including plain OPD)

### What exists today and why this is additive, not a rewrite

`Visit` already conflates booking and consultation for **same-day** bookings —
"a booking that the patient turns up for simply moves Booked → Waiting" (see
the class doc on `Visit`). That is correct for a walk-in and stays completely
unchanged. What is missing is booking **ahead** — next Tuesday, not right now
— without creating a `Visit` row (and cluttering today's OPD queue) until the
patient actually arrives.

**Design: a new `Appointment` entity is the pre-check-in intent. Check-in
converts it into the existing `Visit` row.** `Visit` gains one nullable
back-reference (`AppointmentId`) and nothing else changes about it.

```
Appointment : BaseEntity
    AppointmentNo        string      // sequential, prefix "APT"
    PatientId             Guid        // required — see note below
    PatientName           string      // denormalized, survives a later name edit
    PatientPhone          string      // denormalized — needed to remind/call without a join
    DoctorId              Guid
    DoctorName             string      // denormalized
    ScheduledOn            DateTime    // date + time of the slot
    DurationMinutes         int         // default from Settings (§5), overridable per booking
    ModuleContext           enum        // General = 0, Pediatrics, Dentist, PathologyLab —
                                        // which module's "book appointment" entry point created
                                        // this, so the daily screen can badge/filter by it
    Status                 enum        // Scheduled, CheckedIn, Cancelled, Rescheduled, NoShow
    Reason                 string?     // what for — free text
    Notes                  string?
    RescheduledFromId       Guid?       // self-referencing FK, SetNull — see Rescheduling below
    LinkedRecordId           Guid?       // set once checked in — an advisory pointer, not a queried
                                        // FK, into whichever table ModuleContext says it points at
                                        // (Visit / DentalCase / LabOrder / …). See "Module
                                        // architecture — Appointment check-in" above for why this is
                                        // not a typed VisitId.
```

A new patient calling ahead to book, who is not registered yet, is the exact
scenario `BookVisitViewModel` already solves for a walk-in (`AddingPatient`,
`StartNewPatient`, the inline "found 1 / found several / add new" search).
The Appointment booking screen reuses that same search-or-add flow rather
than inventing a second one — `PatientId` stays required, satisfied either
way before the appointment can be saved.

### 1.2 Cancellation and rescheduling

- **Cancel** sets `Status = Cancelled` on the row. It is never deleted — a
  cancelled appointment is still a fact (a no-show pattern, a "how many
  cancellations this week" report), the same reasoning `StockAdjustment`'s
  class doc gives for writing a document rather than silently correcting a
  number. A reason is asked for and stored in `Notes`.
- **Reschedule** does **not** mutate `ScheduledOn` in place. It creates a
  **new** `Appointment` row with the new date/time and
  `RescheduledFromId = <old row's Id>`, and sets the old row's
  `Status = Rescheduled`. This keeps a queryable trail ("this slot moved
  three times") instead of losing the original time the moment it changes —
  consistent with how this codebase already prefers a written trail over a
  silent overwrite.

### 1.3 Daily appointments screen → OPD queue, with additional data collection

One new screen, "Today's appointments" (or a tab beside the existing OPD
queue): every `Appointment` for the selected date, `Scheduled` or
`CheckedIn`, sorted by time, doctor as a filter. Each row gets a **Check in**
action.

Check-in opens a small form — what "additional data collection" means in the
brief — collecting whatever the front desk normally asks for at arrival
(vitals, updated complaint) plus anything the originating module wants
(§1.7's Pediatrics fields, for instance).

**Which record check-in actually creates depends on `Appointment.ModuleContext`
— see "Module architecture — the one real coupling this forces a fix to"
above.** For a `General` appointment (only ever booked when OPD is on):

1. Creates the `Visit` row exactly as `BookVisitViewModel.BookAsync` does
   today, `ScheduledOn` and `DoctorId` carried over from the appointment.
2. Sets `Visit.AppointmentId` to the appointment's `Id` (new nullable FK on
   `Visit`, `OnDelete(SetNull)` — deleting an appointment later must never
   cascade into deleting the visit it produced).
3. Sets `Appointment.Status = CheckedIn` and `Appointment.LinkedRecordId`.

Nothing about the existing walk-in flow (`BookVisitViewModel`) changes — an
appointment is simply a second way to arrive at the same `Visit` row. A
`Dentist` or `PathologyLab` appointment follows the identical three-step
shape, but step 1 creates a `DentalCase`/`LabOrder` instead of a `Visit`,
and step 2 sets that record's own back-reference rather than
`Visit.AppointmentId` — the daily appointments screen never needs to know
which, since it only ever reads `Appointment.Status`.

### 1.4 Follow-up reminders (on screen now, messaging later)

This turns out to have **two** existing sources of "something is due", both
already modeled, plus new ones the clinical modules add:

- **Visit follow-ups** — `Visit.FollowUpOn` already exists and is already
  printed on the fee receipt ("Review on …"). Nothing new to store; a query
  surfaces visits whose `FollowUpOn` has arrived or falls inside the
  reminder window.
- **Upcoming appointments** — `Appointment.ScheduledOn` inside the reminder
  window and not yet checked in.
- **Vaccine doses due** (§1.2 Pediatrics) — computed, not stored (§1.3).
- **Next dental sitting due** (`DentalSitting.NextSittingOn`, §2.2).

Rather than one module owning all of this, a single **Reminders** screen
(`IPage`) aggregates rows from each source through a small
`IReminderSource`-shaped service call per module — each module contributes
its own "what's due" query, the screen just lists and sorts them together by
date. A `ReminderLog` table records what was shown and actioned:

```
ReminderLog : BaseEntity
    SourceKind      enum      // FollowUp, Appointment, VaccineDue, DentalSitting
    SourceId        Guid      // the Visit/Appointment/VaccinationRecord/DentalSitting id
    PatientId       Guid
    DueOn           DateTime
    Channel         enum      // OnScreen = 0 (only value used today), Whatsapp, Email — reserved
    ActionedOn      DateTime?
    ActionedBy      string?
```

`Channel` and the unused enum values exist purely so that turning on
WhatsApp/email later is "send instead of just log", not a migration. **The
actual messaging integration is explicitly out of scope for this design** —
this table is the seam it will plug into, nothing more.

### Settings this module needs (§5 has the full list)

`DefaultSlotMinutes`, a reminder lead time, and the appointment booking
window — the last one is **not** new: `ClinicProfile.Window(session)`
(morning/evening sitting times) already exists and the appointment slot
picker validates against it directly rather than duplicating a second
"working hours" setting.

---

## 2. Pediatrics module

### 1.1 Vaccine Master (configurable)

```
VaccineMaster : BaseEntity
    Name              string    // "BCG", "OPV-1", "DPT Booster"
    DoseNumber         int       // which dose in the series
    RecommendedAgeDays  int       // age at which due, in days — "6 weeks" is 42, not "1.5 months",
                                  // so every schedule comparison is one integer subtraction
    Category           string    // free text, same convention as DiagnosticTest.Category —
                                  // "Universal Immunization Programme", "Optional" — clinics add
                                  // their own without a code change
    Price               decimal   // default price if this dose is charged (many UIP doses are free —
                                  // see §1.3, a given dose is not forced to be billed)
    SequenceOrder        int
    Active               bool
```

Test Master-style screen (search, add/edit inline, deactivate rather than
delete once a dose referencing it has been given — same guard shape as
`DiagnosticsService.DeleteTestAsync`).

### 1.2 Vaccine reminders

Computed, not stored: for each pediatric patient, diff `VaccineMaster`
(filtered to `Active`) against that patient's `VaccinationRecord` rows
(§1.3) by `DoseNumber`; a vaccine with no matching record whose
`RecommendedAgeDays` has arrived, or falls inside the reminder window, is
"due". This feeds the generic Reminders screen (§1.4 above) as one more
source — no separate reminder table for vaccines specifically.

### 1.3 Vaccine history

```
VaccinationRecord : BaseEntity
    PatientId          Guid
    PatientName         string    // denormalized
    VaccineId            Guid?     // SetNull — a deleted master never breaks history
    VaccineName           string    // denormalized
    DoseNumber            int       // denormalized — printed even if the master row changes later
    GivenOn                DateTime  // a row only exists once actually given; "due" is always computed
    BatchNo                string?   // vial batch/lot — worth capturing, some states require it
    SiteOfInjection         string?   // free text, e.g. "Left thigh"
    AdministeredBy           string?
    NextDueOn                DateTime? // stored hint for the next dose in this vaccine's series,
                                       // computed and snapshotted at save time — cheap to read back
                                       // on the Reminders screen instead of recomputing the whole
                                       // schedule for every patient on every screen load
    ProcedureBillItemId       Guid?     // set only if this dose was actually charged — see below
```

Administering a dose and **billing** for it are kept as two separate facts on
purpose: a free UIP dose is still clinically recorded, but never touches a
bill. `ProcedureBillItemId` links to §1.6 only when the clinic actually
charges for that particular dose.

### 1.4 Baby growth chart

```
GrowthMeasurement : BaseEntity
    PatientId               Guid
    VisitId                  Guid?     // optional — set when measured during an OPD visit
    MeasuredOn                DateTime
    AgeDays                    int       // computed from Patient.DateOfBirth at save time — Patient.Age
                                         // is whole years, not enough precision for an infant chart
    WeightKg                   decimal?
    HeightCm                    decimal?  // length under 2y, standing height after
    HeadCircumferenceCm          decimal?
    BmiValue                     decimal?  // computed weight / (height/100)^2, stored rather than
                                           // recomputed since weight and height are not always both
                                           // present on the same visit
```

The **chart itself** plots these points against standard percentile curves
(5th/50th/95th, etc.) — weight-for-age, height-for-age,
head-circumference-for-age, BMI-for-age, separate boys/girls tables. That
reference data does not exist anywhere in this codebase today and has to be
sourced (WHO Child Growth Standards is the usual choice) and shipped as
embedded reference data, the same way the compiled-in letterhead default
ships today — **this is a real data-acquisition task, not a schema
decision**, flagged again here because it is easy to scope as "just draw a
chart" and miss.

### 1.5 Individual baby history

No new schema — a screen, not a table. One more tab on the existing Patients
screen (beside History/Bills/Diagnostics history, which already exist),
shown when the Pediatrics module is on, aggregating: Visits, `VaccinationRecord`
rows, `GrowthMeasurement` rows, and Pediatric `ProcedureBill`s (§1.6) for that
one patient. Not gated by the patient's age — always available as a tab when
the module is enabled, since gating it would mean deciding an age cutoff that
varies clinic to clinic.

### 1.6 Small procedure billing (Diagnostics model, adopted)

The brief says explicitly to reuse the Diagnostics shape, and the fit is
close. Rather than copy it a second time for Dentist's very similar ask
(§2.3), this design shares **one** catalogue table across both modules with a
`Department` tag, and gives each module its **own** billing pair (Pediatrics'
needs stay simple; Dentist's do not — see the Dentist module's own billing, §2.7):

```
Procedure : BaseEntity                    // shared catalogue, Pediatrics + Dentist
    Name             string
    Category          string    // free text, same convention throughout
    Department         enum      // Pediatrics, Dentist, General
    Price               decimal
    Active               bool
```

```
ProcedureBill : BaseEntity                  // mirrors DiagnosticBill exactly
    BillNo            string    // sequential, prefix "PRC"
    BillDate           DateTime
    PatientId           Guid      // always required, same reasoning as DiagnosticBill
    PatientName          string    // denormalized
    PatientNo             string    // denormalized
    TotalAmount            decimal
    Discount                decimal
    FinalAmount              decimal
    PaymentMode              enum
    TransactionNo             string?
    Status                     enum      // Ordered, Completed — same status-gates-edits pattern
    VisitId                     Guid?     // set when raised from an OPD consultation
    ReferredBy                   string?   // only asked when VisitId is null — identical to
                                           // DiagnosticBill.ReferredBy's own reasoning

ProcedureBillItem : BaseEntity              // mirrors DiagnosticBillItem exactly
    BillId          Guid
    ProcedureId       Guid?     // SetNull
    ProcedureName      string    // denormalized
    Price               decimal   // denormalized
    Quantity             int
    Amount                decimal
```

`ProcedureBill`/`ProcedureBillItem` is deliberately **not** named
"Pediatric…" — Dentist's own billing (§2.7) is structurally different enough
to need its own entities, but this pair is generic enough that any future
module needing flat "pick items, bill them" behaviour reuses it rather than
a fourth near-duplicate. `DiagnosticsService.SaveBillAsync`'s transaction
shape (recompute totals from lines, refuse edits once `Completed`) is copied
line for line into the new `ProcedureBillsService`.

### 1.7 Parent details, head circumference, BMI, weight/height-for-age

"Head circumference, BMI, weight-for-age, height-for-age" are exactly the
`GrowthMeasurement` fields from §1.4 — nothing new needed there. "Parent
details" is genuinely new and Pediatrics-only, so it does **not** go on the
shared `Patient` entity (used by every module, including a 70-year-old
pharmacy customer) — it is a 1:1 extension table instead:

```
PediatricProfile : BaseEntity
    PatientId          Guid      // 1:1 with Patient
    FatherName           string?
    MotherName            string?
    ParentPhone            string?   // separate from Patient.Phone — an infant has no phone of its own
    ParentOccupation         string?
    BirthWeightKg              decimal?
    Notes                       string?
```

---

## 3. Dentist module

This module is structurally the most involved of the three, because a single
billed procedure can span weeks and several visits, and payment against it is
often partial. The design below introduces one new concept
(`DentalCase` — the "one root canal, several sittings, several payments" unit)
that everything else in the module hangs off.

### 2.1 / 2.3 Procedure and small-surgery master

Both reuse the shared `Procedure` catalogue from §1.6
(`Department = Dentist`), distinguished only by `Category`
("Restorative", "Endodontic", "Minor Surgery", …) — no new schema for either
2.1 or 2.3.

### 2.2 Multiple sittings for one procedure

```
DentalCase : BaseEntity
    PatientId           Guid
    PatientName            string    // denormalized
    ProcedureId              Guid?     // set for a single-procedure case — mutually exclusive with PackageId
    PackageId                  Guid?     // set for a package case (§2.6) — enforced in the service layer
    ProcedureName / PackageName  string  // denormalized, whichever applies
    ToothNumber                   string?   // free text — FDI or Universal notation, e.g. "36"
    DoctorId                        Guid      // the treating dentist
    Status                           enum      // Planned, InProgress, Completed, Cancelled
    StartedOn                         DateTime
    CompletedOn                        DateTime?
    Notes                                string?

    // Computed, not stored — see below
    TotalCost      => Procedure/Package price + Σ DentalSitting.AnesthesiaCost
                                             + Σ DentalCaseReplacement.Amount
    AmountPaid     => Σ DentalPayment.Amount           (§2.7)
    Balance        => TotalCost - AmountPaid
```

```
DentalSitting : BaseEntity
    DentalCaseId          Guid      // cascade
    SittingNumber           int       // sequential within the case — "Sitting 2 of 4"
    SittingDate               DateTime
    DoctorId                    Guid      // usually the case's own doctor; can differ
    WorkDone                     string    // free text — what happened this visit
    AnesthesiaTypeId               Guid?     // SetNull — §2.5
    AnesthesiaTypeName               string?   // denormalized
    AnesthesiaCost                     decimal?
    NextSittingOn                        DateTime? // feeds the Reminders screen (§1.4) and the
                                                    // Appointments module (§1) directly — "book the
                                                    // next sitting" reads straight off this
```

### 2.4 Dental replacements (crowns, dentures, bridges, …)

```
DentalReplacementMaster : BaseEntity
    Name         string    // "Zirconia Crown", "Acrylic Partial Denture", "Metal Bridge unit"
    Category      string    // free text — Crown / Bridge / Denture / Implant, clinic's own words
    UnitCost       decimal
    Active

DentalCaseReplacement : BaseEntity            // billed line — quantity-based, e.g. "3-unit bridge"
    DentalCaseId       Guid      // cascade
    ReplacementId         Guid?     // SetNull
    Name                    string    // denormalized
    UnitCost                  decimal   // denormalized
    Quantity                    int
    Amount                        decimal   // Quantity × UnitCost, snapshotted
```

### 2.5 Anesthesia cost

Modeled at the **sitting** level (`DentalSitting.AnesthesiaCost`), not once
per case — anesthesia is typically administered fresh at whichever sittings
need it, not a single flat add-on. A small configurable lookup keeps the
sitting form from free-typing a cost every time:

```
AnesthesiaTypeMaster : BaseEntity
    Name           string    // "Local — Lignocaine", "Sedation"
    DefaultCost      decimal
    Active
```

### 2.6 Packages (multiple procedures bundled)

```
DentalPackageMaster : BaseEntity
    Name              string    // "Full Mouth Rehabilitation", "Smile Makeover"
    Description
    PackagePrice        decimal   // the bundled price — normally less than summing the parts
    Active

DentalPackageItem : BaseEntity                // master-side — what a package is made of, for
    PackageId         Guid      // cascade    // reference/estimate breakdown
    ProcedureId          Guid?     // SetNull
    ProcedureName           string    // denormalized
    Quantity                  int       // e.g. "4 crowns"
```

A `DentalCase` opened against a package pulls its procedure breakdown from
`DentalPackageItem` for the clinician's reference, but bills the flat
`PackagePrice`, not the sum of the parts — the same "package price wins"
decision made again identically in §3.5 for lab packages, so both modules
handle bundling the same way.

### 2.7 Individual billing — the app's first installment model

Every existing bill in this app (`Sale`, `DiagnosticBill`, the fee receipt)
is paid in one shot at the moment it is raised. A dental case paid across
three sittings genuinely cannot be — it needs its own payment log against a
running balance:

```
DentalPayment : BaseEntity
    DentalCaseId       Guid      // cascade
    ReceiptNo             string    // sequential, prefix "DPR"
    PaidOn                  DateTime
    Amount                    decimal
    PaymentMode                 enum
    TransactionNo                 string?
```

Each payment prints its own receipt (`DentalReceiptDocument`, built the same
way `FeeReceiptDocument` is — clinic header, `IdentityRow` for patient/date,
`FeeReceiptDocument.InWords` for the amount) and shows the case's
`Balance` as "amount still due" on the slip, so a partial-payment clinic
workflow is fully supported from the first receipt onward.

---

## 4. Pathology Lab module

Read the open question at the top of this document before this section — it
recommends this module sit **alongside** the existing Diagnostics module
rather than replace it, since Diagnostics has no result-capture or
reference-range concept at all today.

### 3.2 Test Master (individual analytes)

```
LabAnalyte : BaseEntity
    Name              string    // "Glucose Fasting", "Albumin", "Sodium", "TSH"
    Category            string    // free text — "Biochemistry", "Hematology", "Hormone"
    Units                 string    // "mg/dL", "mmol/L", "µIU/mL"
    ResultType               enum      // Numeric, Text, Selection — drives the result-entry control
    DecimalPlaces               int       // printed precision, e.g. Hemoglobin → 1, Sodium → 0
    SequenceOrder                 int
    Active
```

Reference ranges are **not** a single low/high pair on the analyte —
Hemoglobin differs by sex, many ranges differ by age band, and some results
are qualitative ("Reactive/Non-Reactive") rather than numeric at all:

```
LabAnalyteReferenceRange : BaseEntity
    AnalyteId          Guid      // cascade
    Gender               enum?     // null = applies to all
    MinAgeYears             decimal?  // null = no lower bound
    MaxAgeYears                decimal?  // null = no upper bound
    LowValue                     decimal?  // numeric range
    HighValue                       decimal?
    TextRange                         string?   // qualitative override, e.g. "Non-reactive" —
                                                 // takes over the printed range column when set
    Label                                 string    // "Adult Male", "Child 1–5y" — shown on the Test
                                                     // Master screen so multiple rows per analyte
                                                     // stay legible
```

### 3.1 / 3.3 Report configuration (panels made of analytes)

```
LabReport : BaseEntity                        // the panel/profile itself — "CBP", "Thyroid Profile"
    Name              string
    Category            string
    Price                 decimal   // default price for ordering the whole panel
    SequenceOrder
    Active

LabReportAnalyte : BaseEntity                  // many-to-many — the same analyte (Sodium) can sit
    ReportId          Guid      // cascade    // inside several different panels
    AnalyteId            Guid      // Restrict — a report definition still referencing an analyte
                                              // blocks that analyte's deletion, same guard shape as
                                              // DiagnosticsService.DeleteTestAsync
    SequenceOrder
```

### 3.5 Packages (multiple reports bundled)

```
LabPackageMaster : BaseEntity
    Name             string    // "Full Body Checkup", "Diabetes Panel"
    PackagePrice        decimal
    Active

LabPackageReport : BaseEntity                  // master-side — what a package contains
    PackageId          Guid      // cascade
    ReportId              Guid      // Restrict
```

Same "package price overrides the sum of its parts" rule as §2.6.

### 3.4 / 3.6 Billing, patient, referring doctor

```
LabOrder : BaseEntity                          // mirrors DiagnosticBill's shape closely
    OrderNo             string    // sequential, prefix "LAB"
    OrderDate              DateTime
    PatientId                Guid      // always required
    PatientName                 string    // denormalized
    PatientNo                     string    // denormalized
    PackageId                       Guid?     // set when ordered as a package
    VisitId                           Guid?     // set when ordered through our own OPD consultation
    ReferredBy                          string?   // only asked when VisitId is null — identical
                                                   // reasoning to DiagnosticBill.ReferredBy
    SpecimenId                            string?   // the lab's own sample/barcode id, optional
    CollectedOn                             DateTime? // when the sample was actually drawn — the
                                                       // sample report showed Collected, Received and
                                                       // Reported as three distinct timestamps, not one
    ReceivedOn                                DateTime? // when the sample reached the lab bench
    TotalAmount / Discount / FinalAmount   decimal
    PaymentMode / TransactionNo
    Status                                     enum      // Ordered, SampleCollected, ResultEntered,
                                                          // Verified, Completed — richer than
                                                          // DiagnosticBillStatus because a report has
                                                          // to be reviewed before it can print, see below
    Remarks                                      string?   // e.g. "Patient fasting" — an order-level
                                                            // note the sample report also carried

LabOrderReport : BaseEntity                    // one panel ordered on this order — the bill line
    OrderId          Guid      // cascade
    ReportId            Guid?     // SetNull
    ReportName             string    // denormalized
    Price                    decimal   // denormalized
    Amount
    Notes                      string?   // free-text interpretation, filled in at verification —
                                          // see §3.7's note on what this stands in for
```

"Previous results are not required" (3.6) means `LabOrder` deliberately does
**not** carry any comparison-to-last-time logic — each order stands alone.

### 3.7 Result entry and the printed report

```
LabResult : BaseEntity
    OrderReportId          Guid      // cascade — which ordered panel this result belongs to
    AnalyteId                 Guid?     // SetNull
    AnalyteName                  string    // denormalized
    Units                          string    // denormalized
    ResultValue                      string    // stored as text regardless of ResultType — a numeric
                                              // value is formatted using LabAnalyte.DecimalPlaces at
                                              // the presentation layer, not parsed back out of this
    ReferenceRangeDisplay              string    // snapshotted at entry time from the matching
                                                  // LabAnalyteReferenceRange (by the patient's age and
                                                  // sex then) — e.g. "13.0 – 17.0 g/dL" — frozen so a
                                                  // later range edit never changes an already-printed
                                                  // report
    Flag                                     enum      // Normal, Low, High, Abnormal — computed
                                                        // automatically for a numeric result outside
                                                        // the matched range; editable for a
                                                        // qualitative one
    EnteredOn / EnteredBy
    VerifiedOn / VerifiedBy                              // a report does not print until Verified —
                                                          // a real pathology-lab norm, and the reason
                                                          // LabOrder.Status has its own Verified step
```

**Printed report** (`LabReportDocument`, new). Laid out against the actual
sample reviewed (see the open question at the top — structure only, no
personal data from it appears here):

- **Header**: clinic identity via `DocumentBuilder.AddClinicHeader` (boxed,
  same as the prescription and the diagnostic bill today — the sample's own
  header instead showed the *ordering physician's* clinic, because it came
  from an external reference lab; our own in-house lab is printing under its
  own name, so the existing clinic header is the right building block, not a
  second layout).
- **Patient / order identity block**, as an `IdentityRow` grid, Date and time
  kept adjacent per the fix already made across every other printed document
  in this app: Name / Age / Gender / PatientNo on one row; Referred by /
  Doctor on another; then — this is what the sample got right and is worth
  keeping — **Specimen ID, Collected on, Received on, and Reported on as
  their own row**, four distinct timestamps rather than one generic "date".
- **Body**, grouped by panel name (`LabReport.Name` as the section heading —
  "CHEMISTRY", "HEMATOLOGY" and so on in the sample), one table per panel:
  **Test | Result | Flag | Reference Range | Units** — `Flag` its own column
  (H/L, blank when normal), exactly as the sample showed it, rather than
  only bolding the result. **No "Prior Result" or "Prior Result date"
  columns** — the sample carried both, and the explicit instruction on
  reviewing it was that these are not wanted (confirms §3.6 above).
- **Footer**: a "Verified by" line (pathologist/technician name from
  `LabResult.VerifiedBy`), the clinic's own `DocumentTheme.Footer`, and a
  "Printed on {timestamp}" line distinct from the report date — the sample
  stamped every page with both, and on a document a patient may carry to
  another doctor weeks later, printed-on-review is a real, useful distinction
  from reported-on.

Two things the sample also does that are **not** part of this design, flagged
as optional/later rather than built now: a leading "abnormal results only"
summary panel gathering every flagged value from every panel into one
quick-read block before the detail, and rich embedded interpretive tables for
specific analytes (the sample's HbA1c-to-eAG conversion chart, its Hepatitis
B marker matrix). Both are real, but add enough scope — cross-panel
aggregation for the first, a whole separate structured-content model for the
second — that they belong in a later pass once the core report prints
correctly. A single free-text `Notes`/interpretation field per
`LabOrderReport`, filled in by whoever verifies the panel, covers the same
need in the meantime without the extra schema.

---

## 5. Cross-cutting: settings, navigation, numbering, migrations, testing

### Settings → Features

Six toggles on `GeneralSettings` in total — the four new modules plus OPD
and Pharmacy themselves, per "Module architecture" above. All follow
`DiagnosticsEnabled`'s exact existing shape — new key, new checkbox on the
Features tab, new bool mirrored on `MainViewModel`, pushed live on save —
except for their default value, which is not the same for all six:

```
OpdEnabled                bool   // defaults TRUE — every existing install already uses this
PharmacyEnabled             bool   // defaults TRUE — same reason
AppointmentsEnabled            bool   // defaults false
PediatricsEnabled                 bool   // defaults false
DentistEnabled                       bool   // defaults false
PathologyLabEnabled                     bool   // defaults false
```

`SaveFeaturesAsync` gains the "at least one module on" guard described
above — the one behavioural difference from how `DiagnosticsEnabled` saves
today, since turning off a single existing toggle was never previously able
to empty the whole nav bar.

A small **Appointments** settings section (its own tab, or folded into
Features) holds the generic knobs from §1: `DefaultSlotMinutes`, the
reminder lead time. Nothing else needs a new settings surface — each
module's own configuration (vaccine schedule, procedure prices, analyte
ranges) lives in that module's own master screens, the same way Diagnostics'
test prices live in its own Test Master rather than in Settings.

### Navigation

Every nav item is gated by its own module's toggle now, OPD and Pharmacy
included — not just the four new ones:

| Nav item | Tabs | Gated by |
|---|---|---|
| Dashboard | — (module-aware content, see "Module architecture") | always shown |
| OPD | Queue · Consultation | `OpdEnabled` |
| Pharmacy counter / Products / Inventory | — | `PharmacyEnabled` |
| Appointments | Today's appointments · Book appointment · Reminders | `AppointmentsEnabled` |
| Diagnostics | Billing · Test Master | `DiagnosticsEnabled` (unchanged) |
| Pediatrics | Vaccine Master · Vaccination / Growth (per-patient, reached from Patients) | `PediatricsEnabled` |
| Dentist | Cases · Procedure Master · Package Master | `DentistEnabled` |
| Pathology Lab | Orders · Test Master · Report Master · Package Master | `PathologyLabEnabled` |
| Patients, Settings, Reports | — | always shown |

Pediatrics' per-patient screens (§1.5 baby history, growth chart) are reached
from the existing Patients screen as additional tabs, not from the
Pediatrics nav item itself — consistent with how Patients already hosts
History/Bills/Diagnostics-history tabs today, and shown only when
`PediatricsEnabled`. Patients, Settings and Reports themselves are never
gated — Patients is the one screen every module needs, and Settings has to
stay reachable so a module can be turned back on.

### Numbering (`NumberService`)

New prefixes, same `NextAsync` gap-free pattern:

| Constant | Prefix | Document |
|---|---|---|
| `Appointment` | `APT` | Appointment |
| `ProcedureBill` | `PRC` | Pediatric procedure bill |
| `DentalPayment` | `DPR` | Dental payment receipt |
| `LabOrder` | `LAB` | Pathology lab order |

### Printing

New `FlowDocument` builders, all built the same way as
`BillPrinter`/`DiagnosticBillPrinter`/`FeeReceiptDocument`/`PrescriptionPrinter`
are today (`DocumentBuilder` helpers, `ApplyPrintSettings` as the last step,
shown via `PrintService.Preview`):

- `AppointmentSlipDocument` — simple, optional (a printed appointment
  confirmation slip).
- `ProcedureBillDocument` — near-identical to `DiagnosticBillPrinter`.
- `DentalReceiptDocument` — near-identical to `FeeReceiptDocument`, printed
  per `DentalPayment`, shows the case's running `Balance`.
- `LabReportDocument` — new layout, described in §3.7.

### Migrations

Proposed as independent, additive migrations, in this order — each module
can ship and be turned on without the others existing yet, the same way
Diagnostics tables already exist today whether or not `DiagnosticsEnabled`
is on:

1. `AddAppointments` — `Appointment`, `Visit.AppointmentId`, `ReminderLog`.
2. `AddPediatricsModule` — `VaccineMaster`, `VaccinationRecord`,
   `GrowthMeasurement`, `PediatricProfile`, `Procedure` (shared table,
   created here since Pediatrics is listed first), `ProcedureBill`,
   `ProcedureBillItem`.
3. `AddDentistModule` — `DentalCase`, `DentalSitting`,
   `DentalReplacementMaster`, `DentalCaseReplacement`,
   `AnesthesiaTypeMaster`, `DentalPackageMaster`, `DentalPackageItem`,
   `DentalPayment`.
4. `AddPathologyLabModule` — `LabAnalyte`, `LabAnalyteReferenceRange`,
   `LabReport`, `LabReportAnalyte`, `LabPackageMaster`, `LabPackageReport`,
   `LabOrder`, `LabOrderReport`, `LabResult`.

Settings toggles need no migration at all — `Settings` is already a
key/value table, exactly as `DiagnosticsEnabled` shipped without one.

### Testing

Same two-tier convention as every existing module:

- `Pharma.Tests` — one service-test class per module, covering: master-row
  seed/search, the delete-guard once a master row has been used (mirrors
  `DiagnosticsServiceTests`), status-gated edits (`Completed`/`Verified`
  refuses further changes), sequential numbering, and — new to this batch —
  `DentalCase.Balance` arithmetic across multiple `DentalPayment` rows, and
  `LabResult.Flag` auto-computation against `LabAnalyteReferenceRange`.
- `Pharma.UiTests` — a handful of smoke tests per module: the Features
  toggle appears/disappears live, one full happy-path (book → check-in →
  bill → print) per module, and — specifically for Reminders — that a due
  follow-up, vaccine, and appointment all surface on the one screen.

### Reports

Each module gets one more tab on the existing Reports screen, following
`ReportsViewModel`'s own per-`ReportKind` pattern (one enum value, one
`Load…ReportAsync`, one `TabOrder` slot) — already extended once for
Diagnostics, extended six more times the same way, OPD's and Pharmacy's
existing report tabs included: `TabOrder` filters to enabled modules only,
so a dentist-only clinic's Reports screen shows just the Dentist tab, not
the OPD/Pharmacy sales tabs it has no data for.
