# Step 02 — UAT Test Catalog (Pharma.UiTests)

**Project:** Sivayaan Content Engine — Discovery Phase (Step 1.1)
**Date:** 2026-08-02
**Source analyzed:** `tests/Pharma.UiTests/` at commit `8fda0d1` (branch `main`)
**Scope:** Inventory only. Nothing was modified.

---

## How this catalog was built

- Test classes and methods were read directly from the `.cs` sources (`[Fact]`, `[Theory]`, `[StaFact]` attributes).
- "Module (screen driven)" is taken **only from what the code itself states**: the class's XML doc comment and the explicit `Navigate("Nav…", "…")` targets inside it. Nothing was inferred beyond that; where the code does not state it, the column says UNKNOWN.
- "Business scenario" is the class/method doc comment or the behavior-phrased test name itself (the suite's naming convention *is* its scenario description). Where no doc comment exists, the test name is the only available statement.
- Durations come from the only recorded evidence: `TestResults/UAT_CoreFlows.trx` and `TestResults/UAT_VisitAndPrintFlows.trx` (run 2026-07-29, Debug build, machine `DESKTOP-CNR9OSN`). Tests absent from both TRX files have **no known duration**.
- **Test categories:** the project uses **no `[Trait]`, no `[Collection]`, no category attributes anywhere**. The "category" concept does not exist in this codebase.
- **Execution order:** **none is encoded.** There are no `[TestCaseOrderer]`/`ITestCollectionOrderer` implementations, no ordering attributes, no numbering. xUnit's default (undefined within-class order, class-by-class serial execution due to `MaxParallelThreads = 1`) is all there is.

## Suite-wide facts (apply to every class below unless stated)

- **Fixture model:** every UI class is `IClassFixture<AppFixture>` → each **class** gets its own freshly launched `TwinkleHMS.exe` + its own throwaway temp SQLite DB. Classes are therefore fully independent of each other.
- **Within a class**, all tests share one app instance and one DB. Almost every test manufactures its own uniquely named data (timestamp-suffixed patient/medicine names), so within-class order dependence is avoided by convention, not enforced by any mechanism.
- **Parallelism:** disabled assembly-wide (`AssemblyInfo.cs`) — one desktop.
- `UiTestBase` exists in `AppFixture.cs` but **no test class actually derives from it**; all declare `IClassFixture<AppFixture>` directly.
- Exception: **`PrintDocumentTests`** uses no fixture at all — it never launches the app (headless FlowDocument checks) and is the only class that can run without a desktop app instance.

Counts: **26 test classes** (25 with the app fixture + 1 headless), **~102 test methods**, expanding to **~117+ executed test cases** after `[Theory]`/`[InlineData]` expansion.

---

## Catalog

Column legend — **Cat.**: test category attribute (none exist → "—"). **Indep.**: can the method execute on its own via `--filter` (class fixture still launches the app). **Dur.**: recorded duration from TRX evidence, else UNKNOWN.

### 1. `OpdUiTests` — screen driven: OPD (`NavOpd`), Patients (`NavPatients`)

Class scenario (doc): drives the OPD desk "exactly as a receptionist would". Contains the shared helper `internal static BookWalkIn(app, name, phone, age, at?)` reused by other classes.

| Test method | Business scenario (from name/doc) | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `Booking_a_walk_in_puts_a_tile_in_the_waiting_column` | Walk-in booking appears in waiting queue | — | Yes | 4.4 s |
| `Marking_a_visit_done_moves_the_tile_to_completed` | Visit completion moves queue tile | — | Yes | 4.2 s |
| `Reopening_a_completed_visit_moves_the_tile_back` | Completed visit can be reopened | — | Yes | 3.6 s |
| `A_booked_patient_reaches_the_patient_register_with_a_number` | Booking creates numbered patient record | — | Yes | 3.4 s |
| `A_booking_appears_in_the_patients_visit_history` | Booking visible in visit history | — | Yes | 3.7 s |
| `Editing_a_patient_saves_the_change` | Patient edit persists | — | Yes | 6.1 s |

### 2. `OpdSearchUiTests` — screen driven: OPD booking search

Class scenario (doc): finding a patient by name and by phone; digits-only phone matching; several children on one parent's number; booking must not invent a duplicate when no sibling is picked. Has a private setup that registers a three-child family per test (each test uses its own phone number because the class shares one DB).

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `A_patient_is_found_by_name` | Name search finds patient | — | Yes | UNKNOWN |
| `A_phone_number_finds_everyone_on_it_and_says_to_pick_one` | Phone search returns whole family | — | Yes | UNKNOWN |
| `A_phone_number_is_found_however_it_is_typed` (Theory ×3: spaced / country / dashed) | Phone matching ignores formatting | — | Yes | UNKNOWN |
| `Part_of_a_phone_number_still_narrows_it_down` | Partial phone search works | — | Yes | UNKNOWN |
| `Nobody_matching_offers_to_add_them` | No-match opens new-patient form | — | Yes | UNKNOWN |
| `Booking_without_choosing_a_sibling_is_refused_rather_than_duplicating` | Ambiguous booking refused | — | Yes | UNKNOWN |

### 3. `QueueLayoutUiTests` — screen driven: OPD queue layout / Settings (consulting hours)

Class scenario (doc): tiles-vs-rows layout choice and sitting (session) filter; a filter must say when it is holding patients back.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `The_chosen_layout_survives_leaving_the_screen` | Layout preference persists | — | Yes | UNKNOWN |
| `A_visit_can_be_completed_in_either_layout` | Both layouts fully usable | — | Yes | UNKNOWN |
| `A_sitting_shows_only_the_visits_booked_in_its_hours` | Sitting filter scopes queue by time | — | Yes | UNKNOWN |
| `The_consulting_hours_survive_leaving_the_screen` | Consulting-hours setting persists | — | Yes | UNKNOWN |

### 4. `DatePickerUiTests` — screen driven: OPD booking (date field)

Class scenario (doc): custom-templated date picker; calendar popup is built at click time so only a live test finds template faults. Doc explicitly notes keyboard *typing* into the field is **not** covered (harness cannot deliver synthetic keystrokes).

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `The_calendar_popup_opens_and_the_app_stays_responsive` | Calendar popup opens without hang | — | Yes | UNKNOWN |
| `The_date_field_accepts_keyboard_focus` | Date field focusable | — | Yes | UNKNOWN |

### 5. `ConsultationUiTests` — screen driven: OPD → consultation overlay

Class scenario (doc): the consultation is a layer over the shell reached from a tile — exactly where an unhandled failure would go unnoticed.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `Opening_a_consultation_from_a_tile_does_not_crash` | Consultation opens from tile | — | Yes | 5.3 s |
| `Completing_a_consultation_moves_the_tile_to_completed` | Completing consultation updates queue | — | Yes | 4.1 s |
| `Leaving_with_unsaved_notes_asks_before_discarding_them` | Unsaved-changes guard | — | Yes | 6.1 s |

### 6. `PrescriptionUiTests` — screen driven: consultation prescription grid

Class scenario (doc): choosing a medicine on a prescription; pharmacy search must work and an unstocked medicine must still be prescribable.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `Typing_part_of_a_name_searches_our_pharmacy` | Prescription search hits pharmacy catalogue | — | Yes | 5.5 s |
| `Choosing_a_result_links_the_line_to_our_stock` | Chosen result links line to stock | — | Yes | 6.9 s |
| `A_medicine_we_do_not_stock_can_still_be_prescribed` | Free-text prescribing allowed | — | Yes | 10.9 s |
| `Nothing_is_filled_in_before_the_doctor_types_it` | No pre-filled prescription values | — | Yes | 6.1 s |
| `The_course_is_worked_out_in_individual_units` | Course computed in units | — | Yes | 8.3 s |
| `A_half_dose_morning_and_night_is_understood` | Fractional dose parsing | — | Yes | 7.7 s |
| `The_consultation_cannot_be_left_open_behind_the_shell` | Overlay cannot be orphaned | — | Yes | 5.8 s |

### 7. `FeverVisitUiTests` — screens driven: OPD → Consultation → Inventory → Pharmacy counter (end-to-end)

Class scenario (doc, verbatim steps): one whole visit as it happens at the desk — book a feverish child; take fee + receipt; prescribe Paracetamol (stocked), Cetirizine syrup (out of stock), Dolo syrup (never carried); counter pulls the prescription and reports what it cannot supply; receive 5 bottles of Cetirizine; bill 9 tablets from a strip of 10 plus one bottle.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `A_fever_visit_from_the_door_to_the_bill` | Full door-to-bill patient journey | — | Yes | **47.2 s** (longest recorded test) |

### 8. `PharmacyUiTests` — screens driven: Medicines (`NavProducts`), Inventory (`NavInventory`), Pharmacy counter (`NavSale`), Reports

Class scenario (doc): "the money path" — create a medicine, receive stock, sell it, check the arithmetic. Each test first runs the private `CreateMedicineWithStock` helper (a comment notes the old standalone "medicine can be created and stocked" test was folded into this helper).

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `Selling_at_mrp_extracts_gst_rather_than_adding_it` | MRP-inclusive GST arithmetic | — | Yes | 8.7 s |
| `Saving_a_bill_numbers_it_and_deducts_the_stock` | Bill numbering + stock deduction | — | Yes | 14.0 s |
| `The_day_book_shows_the_bill_that_was_just_saved` | Day book reflects saved bill | — | Yes | 8.3 s |

*(The TRX also records `A_new_medicine_can_be_created_and_stocked` (8.4 s) — that test existed on 2026-07-29 but is no longer in the source. See OPEN QUESTIONS.)*

### 9. `CounterQuantityUiTests` — screen driven: Pharmacy counter (quantity/unit)

Class scenario (doc): the quantity box and its unit — a bare "9" once turned nine tablets into nine strips; price is never editable on the bill.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `The_unit_is_offered_beside_the_number_and_defaults_to_tablets` | Unit selector defaults to tablets | — | Yes | UNKNOWN |
| `Choosing_strips_multiplies_the_number_by_the_pack` | Strip unit multiplies by pack size | — | Yes | UNKNOWN |
| `Tablets_stay_tablets` | Tablet unit is not converted | — | Yes | UNKNOWN |
| `The_chosen_unit_is_remembered_for_that_medicine` | Per-medicine unit memory | — | Yes | UNKNOWN |
| `The_bill_price_cannot_be_edited` | Price read-only on bill | — | Yes | UNKNOWN |
| `Raising_the_quantity_past_one_batch_re_takes_the_stock` | Quantity growth re-allocates batches | — | Yes | UNKNOWN |

### 10. `CounterRulesUiTests` — screen driven: Pharmacy counter (regulatory rules)

Class scenario (doc): two rules the counter must hold, previously recorded on the medicine and ignored at the till.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `A_medicine_not_sold_loose_goes_out_in_whole_packs` | Whole-pack enforcement | — | Yes | UNKNOWN |
| `A_medicine_sold_loose_still_takes_any_quantity` | Loose sale allowed when flagged | — | Yes | UNKNOWN |
| `A_schedule_H1_sale_cannot_be_saved_without_the_prescriber` | Schedule H1 prescriber mandatory | — | Yes | UNKNOWN |

### 11. `CounterStockUiTests` — screen driven: Pharmacy counter (quick stock-in)

Class scenario (doc): putting stock on the shelf from the counter itself, without a detour to Inventory while a patient waits.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `Stock_goes_on_the_shelf_without_leaving_the_bill` | Quick stock-in from counter | — | Yes | UNKNOWN |
| `It_can_be_billed_the_moment_it_is_added` | Immediately billable | — | Yes | UNKNOWN |
| `Everything_added_this_way_is_listed_for_reconciliation` | Quick stock flagged for reconcile | — | Yes | UNKNOWN |
| `A_medicine_has_to_be_chosen_first` | Guard: medicine selection required | — | Yes | UNKNOWN |

### 12. `AfterSaveUiTests` — screens driven: Patients, Pharmacy counter, Medicines, Inventory

Class scenario (doc): what the screen looks like once the thing has been done — saving must not leave the last record loaded, or the next entry silently overwrites it.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `Saving_a_patient_closes_the_form_and_empties_the_search` | Patient form resets after save | — | Yes | UNKNOWN |
| `Cancelling_the_patient_form_writes_nothing` | Patient cancel is side-effect-free | — | Yes | UNKNOWN |
| `Clearing_the_register_empties_the_search_and_the_selection` | Register clear resets state | — | Yes | UNKNOWN |
| `The_next_patient_is_a_new_record_not_an_edit_of_the_last` | New entry never overwrites last | — | Yes | UNKNOWN |
| `Adding_to_the_bill_empties_the_counter_search` | Counter search resets after add | — | Yes | UNKNOWN |
| `Saving_a_medicine_empties_the_search_and_the_form` | Medicine form resets after save | — | Yes | UNKNOWN |
| `Cancelling_the_medicine_editor_writes_nothing` | Medicine cancel is side-effect-free | — | Yes | UNKNOWN |
| `Receiving_stock_lets_go_of_the_medicine` | Inventory releases selection after receive | — | Yes | UNKNOWN |

### 13. `InventoryPopupUiTests` — screen driven: Inventory (receive/correct popups)

Class scenario (doc): receiving and correcting are two separate forms over the shell; tests focus on what is in the form when it opens and what remains when it closes.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `The_receiving_form_names_the_medicine_it_is_receiving` | Receive form shows target medicine | — | Yes | UNKNOWN |
| `Cancelling_the_receiving_form_receives_nothing` | Receive cancel is side-effect-free | — | Yes | UNKNOWN |
| `Clearing_the_inventory_screen_empties_the_search_and_the_selection` | Inventory clear resets state | — | Yes | UNKNOWN |
| `It_reads_back_what_arrives_in_tablets_before_anything_is_saved` | Unit read-back before save | — | Yes | UNKNOWN |
| `Receiving_closes_the_form_and_leaves_the_screen_clear` | Post-receive screen state | — | Yes | UNKNOWN |
| `There_is_nothing_to_correct_before_anything_has_arrived` | Correction guard on empty stock | — | Yes | UNKNOWN |
| `A_correction_is_written_down_with_its_reason` | Stock correction requires reason | — | Yes | UNKNOWN |
| `Backing_out_of_a_correction_changes_nothing` | Correction cancel is side-effect-free | — | Yes | UNKNOWN |

### 14. `PackSizeRepairUiTests` — screens driven: Medicines, Inventory, Data health, Pharmacy counter

Class scenario (doc): a reported production fault reproduced through the real screens — pack size 15 vs units-per-pack 1, stock received as 59 strips, a child needing 9 tablets.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `The_pack_size_is_taken_from_what_is_typed` | Pack size honors typed value | — | Yes | UNKNOWN |
| `A_disagreement_is_called_out_where_stock_is_handled` | Pack disagreement surfaced | — | Yes | UNKNOWN |
| `Correcting_the_medicine_recounts_the_stock_already_on_the_shelf` | Repair recounts existing stock | — | Yes | UNKNOWN |
| `Nine_tablets_are_nine_tablets_at_the_counter` | Counter honors true unit count | — | Yes | UNKNOWN |

### 15. `DataHealthUiTests` — screen driven: Data health

Class scenario (doc): opens the data health screen for real — its grid once threw `XamlParseException` only when opened against a shop with duplicates.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `It_opens_and_says_the_shop_is_clean` | Clean-shop happy path | — | Yes | UNKNOWN |
| `The_grid_renders_when_there_is_something_to_fix` | Grid renders with faults present | — | Yes | UNKNOWN |
| `Repairing_from_the_screen_puts_the_medicine_right` | In-screen repair works | — | Yes | UNKNOWN |

### 16. `ReportsUiTests` — screens driven: Reports (+ Medicines/Counter to create data)

Class scenario (doc): drives Reports end-to-end, focused on the Stock Register; Export Excel must write a real, correctly named workbook.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `Every_report_tab_shows_its_own_content` (Theory ×9: Day book, GST summary, OPD register, Expiring soon, Part packs, Stock to reconcile, Low stock, Stock Register, Schedule H1 register) | Each report tab renders its own grid | — | Yes | 0.6–1.1 s per case |
| `Reports_screen_opens_all_seven_tabs_and_the_stock_register_lists_real_stock` | Stock register lists real stock | — | Yes | 8.6 s |
| `Include_zero_stock_hides_and_reveals_a_depleted_batch` | Zero-stock toggle | — | Yes | 9.9 s |
| `Export_excel_writes_a_correctly_named_stock_register_workbook` | Excel export (writes a file to disk) | — | Yes | 12.8 s |

### 17. `ReprintUiTests` — screens driven: OPD (fee), Patients (record), reprint search

Class scenario (doc): anything printed once must be printable again later from the patient's record.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `Taking_the_fee_issues_a_numbered_receipt` | Fee → numbered receipt | — | Yes | 3.6 s |
| `The_fee_form_shows_what_is_about_to_be_taken` | Fee form preview correctness | — | Yes | UNKNOWN |
| `An_edited_amount_is_what_the_receipt_says` | Edited fee reaches receipt | — | Yes | UNKNOWN |
| `A_receipt_can_be_printed_again_from_the_patients_record` | Receipt reprint | — | Yes | 5.9 s |
| `Printing_a_receipt_for_an_unpaid_visit_says_so_instead_of_printing` | Unpaid-visit guard | — | Yes | 4.0 s |
| `A_visit_with_no_prescription_says_so_rather_than_printing_a_blank_page` | Empty-prescription guard | — | Yes | 5.2 s |
| `Searching_for_a_bill_that_does_not_exist_says_so` | Missing-bill message | — | Yes | 1.2 s |

### 18. `PrintDocumentTests` — **headless**; module: printed documents (receipt / prescription / bill)

Class scenario (doc): builds each printed FlowDocument and reads the text back — no printer, no app launch; guards that statutory details reach the paper and each document carries only its own identity. Uses `[StaFact]` (WPF thread affinity) and constructs `ClinicProfile` / `PharmacyProfile` / `Visit` objects directly from `Pharma.Core` + `Pharma.App.Printing`.

22 test methods, including the `Amounts_are_written_out_in_indian_words` Theory (×7 cases): receipt carries number/amount/mode; duplicates marked; Indian-words amounts incl. paise; prescription carries doctor's registration and every medicine, never the drug licence, GSTIN only when registered; bill shows batch/expiry/HSN, licences and GST split; tax-invoice vs plain-invoice titling incl. zero-rated; long bills keep every line; every document is black-on-white with per-paragraph brushes. All durations UNKNOWN (not in the TRX evidence). **All fully independent — no app, no DB, no desktop window required.**

### 19. `NavigationUiTests` — screen driven: shell sidebar (all modules)

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `App_opens_on_the_opd_screen` | Landing page is OPD (uses `InitialPageTitle` recorded at fixture launch) | — | Yes* | UNKNOWN |
| `Every_module_opens_from_the_sidebar` (Theory ×6: Patients, Pharmacy counter, Medicines, Reports, Settings, OPD) | Each sidebar entry opens its screen | — | Yes | UNKNOWN |
| `The_catalogue_lists_the_seeded_medicines` | Seed data visible in Medicines | — | Yes | UNKNOWN |

\* Depends on the fixture-recorded initial title, which only exists because the fixture captures it at launch — it still passes when run alone.

### 20. `SettingsUiTests` — screen driven: Settings tabs

Class scenario (doc): Settings split into tabs (General, Clinic, Pharmacy, Doctors, Reports), each backed by its own Settings keys; each must persist independently.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `Clinic_details_persist_across_screens` | Clinic tab persistence | — | Yes | UNKNOWN |
| `Pharmacy_details_persist_independently_of_the_clinics` | Pharmacy tab persistence | — | Yes | UNKNOWN |
| `Document_branding_persists_across_screens` | Branding persistence | — | Yes | UNKNOWN |

*(The TRX records `Shop_details_persist_across_screens` (3.4 s) — renamed/split since 2026-07-29; see OPEN QUESTIONS.)*

### 21. `ShellCreditUiTests` — screen driven: shell footer (all pages)

Class scenario (doc): the credit and build number in the navigation foot — must be visible on every page and match what the build stamped on the exe (read from the assembly on disk via `AppFixture.ApplicationDirectory`, not typed into the test).

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `The_developer_is_credited` | Credit text present | — | Yes | UNKNOWN |
| `The_version_is_the_one_the_build_stamped_on_the_exe` | On-screen version == exe version | — | Yes | UNKNOWN |
| `It_stays_visible_on_every_page` (Theory ×4) | Footer visible on every page | — | Yes | UNKNOWN |

### 22. `ThemeUiTests` — screen driven: Settings (theme)

Class scenario (doc): light/dark switch must repaint at runtime and be remembered.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `The_theme_can_be_switched_and_the_window_repaints` | Runtime theme swap repaints | — | Yes | UNKNOWN |
| `The_choice_is_remembered` | Theme persisted | — | Yes† | UNKNOWN |

† "Remembered" across what boundary is determined by the test body (same app instance/DB); persistence across an app restart is not something the shared-fixture model can show within one class.

### 23. `ScreenshotCapture` — screens driven: entire application

Class scenario (doc): a single test that drives the whole app to regenerate every screenshot in `docs/images` for the user guide — documentation generation, not verification. **Writes into the repo working tree** (`docs/images/*.png`). Reuses `OpdUiTests.BookWalkIn`.

| Test method | Business scenario | Cat. | Indep. | Dur. |
|---|---|---|---|---|
| `Capture_screens_for_the_user_guide` | Regenerate user-guide screenshots | — | Yes (but side-effectful on the repo) | ~2 min+ (TRX `ScreenshotCapture.trx` exists; single long run) |

### Non-test support files

| File | Role |
|---|---|
| `AppFixture.cs` | Launch/teardown + the entire helper API (see Step01 §4) |
| `Annotate.cs` | Screenshot annotation drawing (used only by `ScreenshotCapture`) |
| `AssemblyInfo.cs` | Disables parallelization |

---

## Dependencies summary

| Dependency kind | Detail |
|---|---|
| Between classes | **None.** Each class boots its own app + DB. |
| Within a class | Shared app + DB; tests avoid collisions via timestamp-suffixed data names. No enforced ordering — any within-class order works by design, but this is convention, not guarantee. |
| Cross-class code reuse | `OpdUiTests.BookWalkIn` (internal static) is called by `ConsultationUiTests`-style flows, `ReprintUiTests`, `FeverVisitUiTests`, `QueueLayoutUiTests`, `ScreenshotCapture`. Reuse is of *code*, not of runtime state. |
| External | Built `TwinkleHMS.exe` in matching configuration; solution root locatable (`HMS_WPF.slnx`); interactive desktop; .NET 10; env vars `CLINICDESK_DB` / `TWINKLE_LOG_DIR` honored by the app. `PrintDocumentTests` needs none of the app-process prerequisites. |
| File-system side effects | `ReportsUiTests.Export_excel…` writes a workbook; `ScreenshotCapture` writes PNGs into `docs/images`. Everything else confines writes to temp DB/logs. |

## Recommended execution order

**None is explicitly encoded.** No orderer, no priorities, no numbering, no `[Collection]` grouping. The only encoded execution constraints are: serial execution (no parallelism) and per-class fixture lifetime. Any ordering beyond that would be an invention of this document and is therefore omitted.

## Existing metadata usable by Content Engine

What exists today, verbatim:

1. **Fully qualified test names** — stable, filterable identifiers (`Pharma.UiTests.<Class>.<Method>`), usable with `dotnet test --filter`.
2. **Behavior-phrased method names** — human-readable scenario titles requiring no extra mapping table.
3. **XML doc comments** on classes and many methods — rich business rationale (extractable from source; they are *not* emitted into any XML doc file — `GenerateDocumentationFile` is not set).
4. **`[Theory]`/`[InlineData]` case parameters** — enumerate concrete sub-scenarios (report tab names, phone formats, amount-in-words pairs, nav targets).
5. **TRX evidence files** — per-test outcome + duration + timestamps (schema: VSTest TRX 2010), already parsed successfully for this catalog.
6. **AutomationIds** referenced in test bodies — a de-facto map of every interactive element per screen.
7. **What does *not* exist:** traits/categories, priorities, ordering, test-plan IDs, links to the UAT xlsx workbook, coverage-to-requirement mapping.

---

## OPEN QUESTIONS

1. **Source vs. TRX drift.** The 2026-07-29 TRX evidence names two tests that no longer exist in source: `PharmacyUiTests.A_new_medicine_can_be_created_and_stocked` (folded into the `CreateMedicineWithStock` helper per a source comment) and `SettingsUiTests.Shop_details_persist_across_screens` (source now has three differently named persistence tests). Which is the authoritative UAT baseline — the current source, or the recorded 2026-07-29 run?
2. **Business-module taxonomy.** The code identifies *screens* (OPD, Consultation, Pharmacy counter, Medicines, Inventory, Reports, Settings, Data health, Shell). Whether the Content Engine's "business modules" should equal these screens, or follow the two-module framing in the README ("OPD and Pharmacy"), or the scenario grouping in `HMS_UAT_TestPack_2026-07-29.xlsx` (not opened during this analysis), is undetermined.
3. **Category scheme.** No categories exist. If the Content Engine needs smoke/regression/UAT tiers, that classification does not currently exist anywhere in code and would have to be defined by a person.
4. **Durations for ~60% of tests are UNKNOWN** — only the 40 cases in the two TRX files have recorded timings. Is a full timed baseline run wanted before implementation, and on which machine/configuration?
5. **Is `ScreenshotCapture` in scope for the Content Engine?** It is documentation generation that mutates the repo (`docs/images`), not a verification test; a naive "run everything" would trigger it.
6. **Is `PrintDocumentTests` considered UAT?** It is headless unit-style verification living inside the UI test project. It appears in neither UAT TRX file. In or out of the UAT catalog?
7. **Within-class independence is by convention only.** Tests avoid each other via timestamped names, but nothing enforces it; is per-method isolation (one class-fixture boot per single test, ~10–30 s overhead each) acceptable to the Content Engine, or will it run whole classes?
8. **`Every_module_opens_from_the_sidebar` includes `NavReports` with a `null` expected element** — the Theory's contract for that case differs from the others (title check only). Whether this is intentional coverage or a gap is not stated in code.
9. **The `theme is remembered` boundary** (†, §22): remembered across navigation within one app instance is what the fixture model can test; whether the business expectation is persistence across restart (untestable in the current shared-fixture design) is unstated.
10. **`UiTestBase` is dead code** — declared in `AppFixture.cs` but never inherited. Intentional leftover or a signal that classes were meant to migrate to it?
11. **Filter-name stability guarantee.** Test names are the only machine-usable IDs. Renames happen (see #1). Is there any policy on name stability the Content Engine can rely on, or must it re-discover the catalog on every run (e.g. `dotnet test --list-tests`)?
