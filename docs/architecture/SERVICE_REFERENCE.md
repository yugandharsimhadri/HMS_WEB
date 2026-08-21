# Service reference

The application layer: `src/Pharma.Data/`. Nine domain services plus auth,
settings, numbering, health and bootstrap.

**This layer is the port's main asset.** It targets plain `net10.0`, has no WPF
dependency, and contains the rules catalogued in
[BUSINESS_RULES.md](BUSINESS_RULES.md). Most of it moves behind an HTTP API with
its logic unchanged.

---

## Shared conventions

**Construction.** Every service takes `IDbContextFactory<AppDbContext>` and
creates a short-lived context per operation:

```csharp
await using var db = await factory.CreateDbContextAsync();
```

No service holds a long-lived context. This is already the right shape for
per-request scoping on the web.

**Reads** use `.AsNoTracking()`. **Writes** load tracked, mutate, and call
`SaveChangesAsync` once — a whole document (bill and its lines, case and its
sittings) saves in one transaction.

**Refusals throw `InvalidOperationException`** with a message written for the
person at the desk, not for a developer. View models catch it and show it. The
services never reference a dialog, a message box, or any UI type.

**Soft delete** is applied per query as `!x.IsDeleted` — there is no global
filter. See the port note in [BUSINESS_RULES.md](BUSINESS_RULES.md#1--identity-and-deletion).

**Logging** goes through `AppLog.Enter(scope, detail)`, which returns a disposable
that records `Ok` / `Skip` and timing.

---

## OpdService

Patients, doctors, visits, the queue, the consultation and the fee.

| Method | Notes |
|---|---|
| `SavePatientAsync` | Add or update. Allocates `P#####` on create |
| `DeletePatientAsync` | Returns a **message** instead of deleting when visits exist; null on success |
| `SearchPatientsAsync` | Name, patient number or phone. A phone returns the whole family |
| `GetDoctorsAsync` · `SaveDoctorAsync` | At least one doctor is required before any visit can be booked |
| `BookVisitAsync` | Allocates `V#####` and the day's next token. Optionally links an `Appointment` |
| `CollectFeeAsync` | Writes `RCP#####`, `FeePaid`, mode and transaction number onto the visit |
| `SetStatusAsync` | Moves through `VisitStatus` |
| `SaveConsultationAsync` | Vitals, diagnosis, prescription items and requested diagnostic tests, in one save |
| `GetVisitAsync` | Full graph for printing |

## PharmacyService

Products, batches, goods-inward, the counter, corrections, repacking.

| Method | Notes |
|---|---|
| `SaveProductAsync` | Maintains `SearchKey` — brand, generic, maker and rack folded into one searchable string |
| `SearchProductsAsync` | Matches that key, any case |
| `GetSellableBatchesAsync` | Unexpired, `QtyOnHand > 0`, oldest expiry first |
| `SaveSaleAsync` | The counter. Prices per batch, deducts stock, spans batches, computes GST out of MRP, allocates `INV#####` |
| `ReceiveStockAsync` | Goods inward. Allocates `GRN#####`, converts packs to units, adds free quantity, refuses a duplicate supplier invoice |
| `QuickAddStockAsync` | Counter-side top-up. Marks `IsProvisional`, allocates a `CTR-` batch number |
| `AdjustStockAsync` | Writes a `StockAdjustment` with before, after and reason. **The only sanctioned way to change `QtyOnHand` outside a sale or receipt** |
| `PreviewRepackAsync` · `RepackAsync` | Splitting a pack into loose units |
| `GetSaleAsync` | Full graph for reprint |

> `SaveSaleAsync` carries most of the pharmacy's rules at once — batch pricing,
> expiry refusal, batch spanning, GST extraction and stock deduction. Port it
> deliberately rather than paraphrasing it, and see the concurrency blocker in
> [BUSINESS_RULES.md](BUSINESS_RULES.md#3--pharmacy-and-stock).

## DiagnosticsService

| Method | Notes |
|---|---|
| `SaveTestAsync` · `SetActiveAsync` · `DeleteTestAsync` | Delete is refused once the test appears on any bill — deactivate instead |
| `SaveBillAsync` | Allocates `DX#####`, recomputes totals server-side |
| `UpdateStatusAsync` | `Ordered → SampleCollected → ResultReceived → Completed` |
| `DeleteBillAsync` | Refused once `Completed` |
| `GetBillAsync` | |

## AppointmentsService

Generic booking, shared by every module.

| Method | Notes |
|---|---|
| `BookAsync` | Allocates `APT#####`. **Re-checks server-side that the requested module is enabled** |
| `RescheduleAsync` | Writes a new appointment linked by `RescheduledFromId` |
| `CancelAsync` | Requires a reason |
| `MarkCheckedInAsync` | Sets `LinkedRecordId` to the record the check-in created — today only OPD creates one |
| `GetForDateAsync` | Any day, across every module |
| `GetDueRemindersAsync` | Follow-ups, appointments, vaccine due dates and next sittings, unified |
| `MarkReminderActionedAsync` · `UnmarkReminderActionedAsync` | Writes `ReminderLog`. **Records that the desk followed up; sends nothing** |

## PediatricsService

| Method | Notes |
|---|---|
| `SearchVaccinesAsync` · `SaveVaccineAsync` · `SetVaccineActiveAsync` · `DeleteVaccineAsync` | Delete refused once a dose has been given |
| `RecordVaccinationAsync` | Writes the record, computes `NextDueOn` from the next dose in the series, and **deducts one unit from `BatchId`** |
| `GetVaccinationHistoryAsync` | |
| `GetDueVaccinesForPatientAsync` · `GetDueVaccinesAsync` | Computed from date of birth; no pending rows stored |
| `GetImmunizationCardAsync` | Every active vaccine checked against this patient's doses → `Given` / `Overdue` / `DueSoon` / `Upcoming` |
| `RecordGrowthAsync` | Computes `AgeDays` from date of birth and `BmiValue` from weight and height |
| `GetGrowthHistoryAsync` | |
| `GetProfileAsync` · `SaveProfileAsync` | Parent details. Upserts — never duplicates |

## DentistService

| Method | Notes |
|---|---|
| `OpenCaseAsync` | Refuses procedure *and* package together. Snapshots `BaseCost` |
| `AddSittingAsync` | Allocates the next sitting number; anesthesia cost joins `TotalCost` |
| `AddReplacementAsync` | Unit cost × quantity |
| `RecordPaymentAsync` | Allocates `DPR#####`. Accepted even on a completed case |
| `UpdateCaseStatusAsync` · `GetCaseAsync` | |
| Masters | `SaveReplacementAsync` · `SaveAnesthesiaTypeAsync` · `SavePackageAsync` and their searches and deletes |

## PathologyLabService

| Method | Notes |
|---|---|
| Analyte master | `SaveAnalyteAsync` · `DeleteAnalyteAsync` · reference ranges by sex and age band |
| Report master | `SaveReportAsync` · `DeleteReportAsync` — a report bundles analytes |
| Package master | `SavePackageAsync` · `DeletePackageAsync` — flat price wins over the sum |
| `SaveOrderAsync` | Allocates `LAB#####`; expands reports into their analytes as blank result rows |
| `SaveResultAsync` | Matches the reference range and sets `LabResultFlag` |
| `MatchReferenceRangeAsync` | By patient sex and age |
| `VerifyOrderAsync` | **Refused with no results entered** |
| `UpdateOrderStatusAsync` · `GetOrderAsync` | |

## ProcedureBillsService

The shared procedure catalogue and billing behind Pediatrics, Dentist and General.

| Method | Notes |
|---|---|
| `SearchProceduresAsync(department, …)` | Filtered by `ProcedureDepartment` |
| `GetCategoriesAsync(department)` | Feeds the editor's category combo |
| `SaveProcedureAsync` · `SetActiveAsync` · `DeleteProcedureAsync` | Delete refused once billed |
| `SaveBillAsync` | Allocates `PRC#####` |
| `UpdateStatusAsync` · `DeleteBillAsync` · `GetBillAsync` | Edit refused once `Completed` |

## SettingsService

Typed views over the key/value `Settings` table.

| Method | Returns |
|---|---|
| `GetGeneralAsync` · `SaveGeneralAsync` | `GeneralSettings` — every `features.*.enabled`, queue layout, theme, `auth.requirelogin` |
| `GetClinicAsync` · `SaveClinicAsync` | `ClinicProfile` — name, address, phone, GSTIN, consulting hours, footer |
| `GetPharmacyAsync` · `SavePharmacyAsync` | `PharmacyProfile` — name, drug licence, GSTIN, pharmacist |
| `GetDocumentThemeAsync` · `SaveDocumentThemeAsync` | `DocumentTheme` — logo (base64), fonts, footer |
| `EnsureSettingsSplitSeedAsync` | One-time migration of legacy `shop.*` keys, guarded by `migrations.settingssplit.done` |

## AuthService

| Method | Notes |
|---|---|
| `LoginAsync` | Returns `LoginResult`. Salted hash |
| `SaveUserAsync` | Admin creates Doctor / Pharmacy / Diagnosis accounts |
| `ChangeOwnPasswordAsync` | Forced on first sign-in via `MustChangePassword` |
| `ResetPasswordAsync` | Returns the new temporary password |

> Authenticates only. **It does not authorise** — see
> [BUSINESS_RULES.md](BUSINESS_RULES.md#10--users-and-roles).

## NumberService

`static Task<string> NextAsync(AppDbContext db, string name, CancellationToken)`.
Takes the *caller's* context so the number is allocated inside the caller's
transaction. **Not concurrency-safe** — see
[BUSINESS_RULES.md](BUSINESS_RULES.md#2--document-numbering).

## Import — `Pharma.Data/Import/`

Reading a distributor's own bill file instead of typing it.

| Type | Purpose |
|---|---|
| `CsvFile` | Tolerant CSV reader — quoting, stray columns, inconsistent line endings |
| `VendorBillParser` | Applies an `ImportProfile` to turn a file into `VendorBill` + `VendorBillLine` |
| `PurchaseImportService` | Previews and commits an import. **Refuses a bill already received**, naming the date and the `GRN` it came in as |
| `ImportProfileSeeder` | Seeds the built-in distributor profiles |

An `ImportProfile` maps column positions, date formats and expiry formats per
distributor — different suppliers ship different shapes and none of them will
change for you. `VendorProductCodes` remembers which of *their* codes maps to
which of *our* products, so the mapping is only done once per product per
supplier.

## Licensing — `Pharma.Data/Licensing/`

| Type | Purpose |
|---|---|
| `LicenseService` | `Validate()`, `IsExpired()`, `GetRemainingDays()`, `GetLicenseInfo()` |
| `LicenseStorage` | Last-successful-run record in `%ProgramData%\Sivayaan\HMS\runtime.dat` |

Two design points worth carrying forward:

- **The clock is judged before the expiry.** If the system clock has been wound
  back, the expiry it would be compared against means nothing — reporting "300
  days left" from a rolled-back clock is worse than reporting nothing.
- The state file carries a **SHA-256 of its own contents plus a salt**, so an
  edit in Notepad is detected rather than believed.

> Relevant to the migration: the on-premise edition already has licensing and
> expiry. SaaS entitlement is a *different* mechanism (a server-side
> subscription check), but the on-prem product does not need one built.

## Security — `Pharma.Data/Security/`

`PasswordHasher` — salted hashing behind `AuthService`. The only cryptography in
the codebase.

## DataHealthService

| Method | Notes |
|---|---|
| `DailyCheckAsync` | Runs at startup; returns a message when something needs attention |
| `SurvivorForAsync` · `MergeAsync` | Merging a medicine that was entered twice |
| `RepairAsync` | Repointing orphaned references |

## DbBootstrapper

Runs once at startup: resolves the database path, `MigrateAsync()`, then the
seeders — `UserSeeder`, `VaccineMasterSeeder`, `DentistProcedureSeeder`,
`PathologyLabSeeder`, `DiagnosticTestSeeder`, `ImportProfileSeeder` — then
`EnsureSettingsSplitSeedAsync`, then the daily backup.

Every seeder is **insert-if-missing, matched by name**. None overwrites an edited
row, and none re-inserts a deleted one. On the web this becomes tenant
provisioning: a new clinic is useful the moment it is created.

---

## Pure calculators — `Pharma.Core`

No database, no I/O, trivially testable, and they port unchanged.

| Type | What it does |
|---|---|
| `GstCalculator` | Back-calculates taxable value, CGST and SGST **out of** an MRP |
| `PackMath` | Packs ↔ units, part-pack labels |
| `DoseMath` | Prescription (morning/afternoon/night × days) → unit count |
| `StockRegisterCalculator` | Register totals — products, batches, units, cost value, MRP value |
