namespace SivayaanHMS.Core;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Every row belongs to exactly one clinic. Set by the API layer
    /// from the caller's tenant claim, never accepted from the client, and
    /// enforced everywhere by a single global EF Core query filter on
    /// AppDbContext rather than per-query — see SAAS_MIGRATION.md decision 1.
    /// Required (not nullable): there is no such thing as a tenant-less row
    /// in this system.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Concurrency token. The desktop app never needed one — one
    /// user, one process, last-write-wins was invisible. On the web, two
    /// people can open the same case at once; EF Core throws
    /// DbUpdateConcurrencyException on a stale save instead of silently
    /// discarding one of their edits. See SAAS_MIGRATION.md finding 3.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>Who created this row, when login is on. Null for everything
    /// written before login existed, and null forever after too if login is
    /// never switched on — that is a normal, permanent state, not a gap to
    /// fill in. Not a foreign key on purpose: a plain column here avoids a
    /// navigation property and a delete-behaviour decision on every one of
    /// the twenty-odd entity types that inherit this, for a value that is
    /// read back by a simple lookup wherever it is actually shown.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Who last saved this row — which, since this app deletes by
    /// setting <see cref="IsDeleted"/> rather than removing the row, doubles
    /// as "who deleted it" for anything not otherwise edited in between.</summary>
    public Guid? UpdatedByUserId { get; set; }
}

// ── OPD ────────────────────────────────────────────────────────────────────

public class Patient : BaseEntity
{
    public string PatientNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public Gender Gender { get; set; } = Gender.Male;

    /// <summary>In years. Kept as a stored value even when a DOB is on file —
    /// computed from it at save time — so every existing Age reader (queue
    /// tiles, receipts, search results) keeps working unchanged.</summary>
    public int Age { get; set; }

    /// <summary>Optional; when given, Age is calculated from it rather than
    /// typed. One of DateOfBirth or Age is required, not both.</summary>
    public DateTime? DateOfBirth { get; set; }

    public string? BloodGroup { get; set; }

    /// <summary>Optional, and only meaningful under 18 — nobody is asked for
    /// their own guardian's name once they are an adult.</summary>
    public string? GuardianName { get; set; }

    public string? Address { get; set; }
    public string? Allergies { get; set; }

    public ICollection<Visit> Visits { get; set; } = [];

    public string Display => $"{Name} ({Age}{Gender.ToString()[0]}) · {Phone}";

    /// <summary>Whole years between a date of birth and today.</summary>
    public static int AgeFromDob(DateTime dob)
    {
        var today = DateTime.Today;
        var age = today.Year - dob.Year;
        if (dob.Date > today.AddYears(-age)) age--;
        return Math.Max(0, age);
    }
}

public class Doctor : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? RegistrationNo { get; set; }
    public string? Speciality { get; set; }
    public string? Phone { get; set; }
    public decimal ConsultationFee { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// One OPD appointment. Booking and visit are deliberately the same record — a
/// booking that the patient turns up for simply moves Booked → Waiting, so the
/// front desk never re-keys anything.
/// </summary>
public class Visit : BaseEntity
{
    public string VisitNo { get; set; } = string.Empty;
    public int TokenNo { get; set; }
    public DateTime ScheduledOn { get; set; }
    public VisitStatus Status { get; set; } = VisitStatus.Booked;

    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;

    public string? Complaint { get; set; }
    public string? Diagnosis { get; set; }
    public string? Notes { get; set; }

    // Vitals — all optional, entered only if the clinic bothers with them.
    public decimal? WeightKg { get; set; }
    public string? BloodPressure { get; set; }
    public decimal? TemperatureF { get; set; }
    public decimal? HeightCm { get; set; }
    public int? HeartRateBpm { get; set; }
    public int? Spo2Percent { get; set; }

    public decimal Fee { get; set; }
    public bool FeePaid { get; set; }

    // A receipt has to carry its own number and date, or it cannot be reprinted
    // or reconciled against the day's collection.
    public string? FeeReceiptNo { get; set; }
    public DateTime? FeePaidOn { get; set; }
    public PaymentMode? FeePaymentMode { get; set; }

    /// <summary>UPI/card reference, for reconciling against the payment
    /// gateway or bank statement. Never required — a lot of desks still take
    /// a phone screenshot as proof and move on.</summary>
    public string? FeeTransactionNo { get; set; }

    public DateTime? FollowUpOn { get; set; }

    /// <summary>Set when this visit was created by checking in an
    /// <see cref="Appointment"/> rather than as a walk-in — the reverse of
    /// that appointment's own <see cref="Appointment.LinkedRecordId"/>.
    /// Null for the ordinary walk-in flow, which is unchanged by
    /// Appointments existing at all.</summary>
    public Guid? AppointmentId { get; set; }

    public ICollection<PrescriptionItem> Prescription { get; set; } = [];

    /// <summary>Tests the doctor wants run, written down during the
    /// consultation the same way a medicine is — picked from the catalogue or
    /// typed free-text — and picked up later at the Diagnostics desk instead
    /// of being asked for again.</summary>
    public ICollection<VisitDiagnosticRequest> DiagnosticRequests { get; set; } = [];

    // ── Display helpers for the queue tiles ────────────────────────────────

    /// <summary>Still to be seen — everything that has not finished or been cancelled.</summary>
    public bool IsWaiting => Status is VisitStatus.Booked or VisitStatus.Waiting or VisitStatus.InConsultation;

    public string PatientLine => Patient is null
        ? ""
        : $"{Patient.Age}{Patient.Gender.ToString()[0]} · {ScheduledOn:HH:mm}";

    public string FeeBadge => FeePaid ? "Fee paid" : "Fee due";

    /// <summary>
    /// Whether this visit may still be called off.
    ///
    /// Once the fee is taken a numbered receipt exists, and once the patient has
    /// been seen there is a consultation on record — cancelling either would
    /// leave a receipt or a prescription pointing at a visit the register says
    /// never happened. Refund and reversal are deliberately not part of this
    /// product, so the answer is simply no. <c>OpdService.SetStatusAsync</c>
    /// enforces the same rule server-side; this is what lets the screen stop
    /// offering it in the first place.
    /// </summary>
    public bool CanCancel => !FeePaid && Status is not (VisitStatus.Completed or VisitStatus.Cancelled);

    /// <summary>One line for the row layout, with no dangling separator when
    /// the complaint was left blank.</summary>
    public string RowSummary => string.IsNullOrWhiteSpace(Complaint)
        ? PatientLine
        : $"{PatientLine}  ·  {Complaint}";

    /// <summary>How long they have been sitting there, in words the desk uses.</summary>
    public string WaitedFor
    {
        get
        {
            if (!IsWaiting) return "";

            var minutes = (int)(DateTime.Now - ScheduledOn).TotalMinutes;
            return minutes switch
            {
                < 1 => "just arrived",
                < 60 => $"waiting {minutes}m",
                _ => $"waiting {minutes / 60}h {minutes % 60}m"
            };
        }
    }
}

public class PrescriptionItem : BaseEntity
{
    public Guid VisitId { get; set; }
    public Visit Visit { get; set; } = null!;

    /// <summary>Set when the drug was picked from the catalogue; null for free-text.</summary>
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    public string MedicineName { get; set; } = string.Empty;
    public string? Dosage { get; set; }
    public string? Frequency { get; set; }
    public int Days { get; set; }
    public int Quantity { get; set; }
    public string? Instructions { get; set; }
}

/// <summary>One test requested during a consultation — the diagnostics
/// equivalent of <see cref="PrescriptionItem"/>. Picked up at the
/// Diagnostics desk to build the actual bill; requesting one here does not
/// bill it by itself.</summary>
public class VisitDiagnosticRequest : BaseEntity
{
    public Guid VisitId { get; set; }
    public Visit Visit { get; set; } = null!;

    /// <summary>Set when chosen from the test catalogue; null for free-text —
    /// a test we do not run in-house, sent out but still worth recording.</summary>
    public Guid? TestId { get; set; }
    public DiagnosticTest? Test { get; set; }

    public string TestName { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

// ── Appointments ─────────────────────────────────────────────────────────
//
// Generic booking infrastructure every clinical module can use — OPD's own
// walk-in flow (BookVisitViewModel) is completely unchanged by this existing;
// an Appointment is a second, optional way to arrive at the same working
// record a module already produces (a Visit, a DentalCase, a LabOrder, …).

/// <summary>
/// A booked-ahead intent to be seen — "next Tuesday", not right now — kept
/// deliberately separate from the record a module actually works from
/// (<see cref="Visit"/> for OPD, <c>DentalCase</c> for Dentist, <c>LabOrder</c>
/// for Pathology Lab) so booking ahead never creates a queue entry before the
/// patient has actually arrived.
/// </summary>
public class Appointment : BaseEntity
{
    public string AppointmentNo { get; set; } = string.Empty;

    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    // Denormalized so the daily list and a reminder both read correctly
    // without a join, and survive a later name/phone edit — same reasoning
    // as every other billed/printed line in this codebase.
    public string PatientName { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;

    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public string DoctorName { get; set; } = string.Empty;

    public DateTime ScheduledOn { get; set; }
    public int DurationMinutes { get; set; } = 15;

    /// <summary>Which module this was booked for — decides what check-in
    /// actually creates. See <see cref="LinkedRecordId"/>.</summary>
    public AppointmentModuleContext ModuleContext { get; set; } = AppointmentModuleContext.General;

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;

    public string? Reason { get; set; }
    public string? Notes { get; set; }

    /// <summary>Set when this appointment was created by rescheduling an
    /// earlier one — the old row is marked <see cref="AppointmentStatus.Rescheduled"/>
    /// rather than mutated in place, so the original time stays on record
    /// instead of being silently overwritten.</summary>
    public Guid? RescheduledFromId { get; set; }

    /// <summary>
    /// Set once checked in — an advisory pointer, not a queried foreign key,
    /// into whichever table <see cref="ModuleContext"/> says it points at
    /// (<see cref="Visit"/>, a Dentist <c>DentalCase</c>, a Pathology Lab
    /// <c>LabOrder</c>). Only ever read back to jump straight to "the record
    /// this appointment turned into"; never joined against in a query, so it
    /// does not need a different typed FK per module.
    /// </summary>
    public Guid? LinkedRecordId { get; set; }

    /// <summary>Still to be seen — mirrors <see cref="Visit.IsWaiting"/>'s
    /// own reasoning for the pre-check-in list.</summary>
    public bool IsPending => Status is AppointmentStatus.Scheduled;
}

/// <summary>
/// One row on the shared Reminders screen — a follow-up visit, an upcoming
/// appointment, a vaccine dose due, or a dental sitting due, all read
/// together by date rather than each module keeping its own separate list.
/// <see cref="Channel"/> exists purely so that turning on WhatsApp or email
/// later is "send instead of just log" — no messaging integration is built
/// today, this table is only the seam it will plug into.
/// </summary>
public class ReminderLog : BaseEntity
{
    public ReminderSourceKind SourceKind { get; set; }

    /// <summary>The Visit/Appointment/VaccinationRecord/DentalSitting id
    /// this reminder is about — not a typed FK, for the same reason
    /// <see cref="Appointment.LinkedRecordId"/> is not one.</summary>
    public Guid SourceId { get; set; }

    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public DateTime DueOn { get; set; }
    public ReminderChannel Channel { get; set; } = ReminderChannel.OnScreen;

    public DateTime? ActionedOn { get; set; }
    public string? ActionedBy { get; set; }

    public bool IsActioned => ActionedOn is not null;
}

// ── Pharmacy ───────────────────────────────────────────────────────────────

public class Product : BaseEntity
{
    /// <summary>The brand on the pack — what a customer usually asks for.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The drug itself, e.g. "Paracetamol 500mg" behind the brand "Calpol".
    /// Searched alongside the brand, because staff know medicines by both.
    /// </summary>
    public string? GenericName { get; set; }

    public string? Manufacturer { get; set; }

    /// <summary>
    /// The salts and strengths, e.g. "Paracetamol 500mg + Caffeine 30mg". What a
    /// pharmacist needs to substitute a brand that has run out.
    /// </summary>
    public string? Composition { get; set; }

    /// <summary>
    /// "Below 25°C", "2–8°C, do not freeze". Matters in a children's clinic:
    /// vaccines, insulin and some syrups are ruined by a warm shelf.
    /// </summary>
    public string? Storage { get; set; }

    /// <summary>Printed pack, e.g. "10 TAB" or "100 ML". Free text — shops describe packs their own way.</summary>
    public string? PackSize { get; set; }

    /// <summary>What one sellable unit is — a tablet, a bottle, a sachet.</summary>
    public DispensingUnit DispensingUnit { get; set; } = DispensingUnit.Tablet;

    /// <summary>
    /// Sellable units in one pack. There is no standard: strips come in 1, 3, 5,
    /// 10, 15 and more, and a syrup bottle is 1. Stock is counted in units so part
    /// of a strip can be sold, and each batch keeps the count it arrived with.
    /// </summary>
    /// <remarks>
    /// Clamped, because zero is never a legitimate pack. Rows written before the
    /// guards existed hold 0, which showed as "PER PACK 0" and made every
    /// quantity derived from it meaningless. Reading it as 1 keeps the arithmetic
    /// honest until the repair sweep corrects the stored value.
    /// </remarks>
    public int UnitsPerPack
    {
        get => Math.Max(1, _unitsPerPack);
        set => _unitsPerPack = Math.Max(1, value);
    }

    private int _unitsPerPack = 1;

    /// <summary>"10 tablets per strip", or just "bottle" when a pack is one unit.</summary>
    public string PackDescription => UnitsPerPack > 1
        ? $"{UnitsPerPack} {DispensingUnit.Name(UnitsPerPack)} per pack"
        : $"sold as single {DispensingUnit.Name(1)}";

    /// <summary>Off for anything that must leave the shop whole.</summary>
    public bool AllowLooseSale { get; set; } = true;
    public string HsnCode { get; set; } = "3004";
    public decimal GstRate { get; set; } = 12m;
    public DrugSchedule Schedule { get; set; } = DrugSchedule.None;
    public string? RackLocation { get; set; }
    public int ReorderLevel { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Brand, maker and pack, normalised — trimmed, case-folded, whitespace
    /// collapsed. A unique index sits on this, so "Cetirizine 10mg" and
    /// "cetirizine  10MG" from the same maker cannot both exist. Kept as a
    /// stored column because a database constraint is a guarantee and a check in
    /// the screen is only a suggestion.
    /// </summary>
    public string SearchKey { get; set; } = "";

    public static string KeyFor(string? name, string? manufacturer, string? packSize)
    {
        static string Norm(string? value) =>
            string.Join(' ', (value ?? "").Trim().ToLowerInvariant()
                                          .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return $"{Norm(name)}|{Norm(manufacturer)}|{Norm(packSize)}";
    }

    public string BuildKey() => KeyFor(Name, Manufacturer, PackSize);

    public ICollection<Batch> Batches { get; set; } = [];

    public int StockOnHand => Batches.Where(b => !b.IsDeleted).Sum(b => b.QtyOnHand);

    /// <summary>The batch that would actually be dispensed — nearest expiry with stock.
    /// Public because Pediatrics vaccination now sources its brand/batch/price
    /// straight from here too, the same batch a pharmacy sale would use,
    /// rather than collecting any of that a second time.</summary>
    public Batch? NextBatch => Batches
        .Where(b => !b.IsDeleted && b.QtyOnHand > 0)
        .OrderBy(b => b.ExpiryDate)
        .FirstOrDefault();

    /// <summary>What one unit costs, so the counter can quote without opening anything.</summary>
    public string UnitPriceLabel => NextBatch is { } b
        ? $"₹{b.UnitPrice:0.00}"
        : "—";

    /// <summary>The pack price behind it, shown only when a pack is more than one unit.</summary>
    public string PackPriceLabel => NextBatch is { } b && b.UnitsPerPack > 1
        ? $"₹{b.Mrp:0.00} / pack"
        : "";

    /// <summary>Blank rather than a dangling "rack" when no location is recorded.</summary>
    public string RackLabel => string.IsNullOrWhiteSpace(RackLocation) ? "" : $"rack {RackLocation}";
    /// <summary>How far under the reorder level current stock has fallen. Zero when not low.</summary>
    public int Shortage => Math.Max(0, ReorderLevel - StockOnHand);

    /// <summary>
    /// How the shelf stands, for anything that has to show it at a glance.
    ///
    /// In a column of figures a "0" looks exactly like a "10", and whether
    /// there is any left is the one thing on that screen somebody has to
    /// notice. Running low only means anything once a reorder level has been
    /// set, so a medicine without one is never called low — only empty.
    /// </summary>
    public StockLevel Level => StockOnHand <= 0 ? StockLevel.Empty
                             : Shortage > 0 ? StockLevel.Low
                             : StockLevel.Fine;
}

public enum StockLevel { Fine, Low, Empty }

/// <summary>
/// Stock is always batch-wise: MRP and expiry are printed on the strip and differ
/// between consignments of the same drug, so both must come from the batch.
/// </summary>
public class Batch : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string BatchNo { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }

    /// <summary>The price printed on the pack. What a tax invoice must show.</summary>
    public decimal Mrp { get; set; }

    public decimal PurchaseRate { get; set; }

    /// <summary>Stock in base units, not packs.</summary>
    public int QtyOnHand { get; set; }

    /// <summary>
    /// Snapshotted from the product when the batch was received. A manufacturer
    /// changes pack size between consignments, and old stock has to keep pricing
    /// against the pack it actually came in.
    /// </summary>
    /// <remarks>Clamped for the same reason as on the product: zero is never a pack.</remarks>
    public int UnitsPerPack
    {
        get => Math.Max(1, _unitsPerPack);
        set => _unitsPerPack = Math.Max(1, value);
    }

    private int _unitsPerPack = 1;

    public string? SupplierName { get; set; }

    /// <summary>
    /// The supplier's bill this batch came in on. Without it the reconciliation
    /// list can show that something needs a bill but not whose, which is most of
    /// the work of reconciling.
    /// </summary>
    public string? SupplierInvoiceNo { get; set; }

    /// <summary>
    /// Packs received free on a scheme — the "+1" in 10+1. Captured at receiving
    /// and then lost, which overstated cost per unit on every margin figure: ten
    /// paid for and eleven received makes the real cost rate × 10 ÷ 11.
    /// </summary>
    public int FreePacks { get; set; }

    /// <summary>What a pack really cost, once free goods are counted.</summary>
    public decimal EffectivePackCost
    {
        get
        {
            var paid = QtyOnHand > 0 ? PurchaseRate : PurchaseRate;
            var packs = PacksReceived;

            return packs + FreePacks <= 0 ? paid : Round(paid * packs / (packs + FreePacks));
        }
    }

    /// <summary>Paid-for packs behind this batch, as far as it can be told.</summary>
    public int PacksReceived { get; set; }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public DateTime ReceivedOn { get; set; } = DateTime.Today;

    /// <summary>
    /// Entered at the counter because the medicine was on the shelf but not in
    /// the system, rather than received against a supplier bill. Purchases will
    /// not tie out against sales until it is reconciled, so it is flagged rather
    /// than hidden — see the Stock to reconcile report.
    /// </summary>
    public bool IsProvisional { get; set; }

    public bool IsExpired => ExpiryDate.Date < DateTime.Today;

    public decimal UnitPrice => PackMath.UnitPrice(Mrp, UnitsPerPack);

    public string OnHand => PackMath.Describe(QtyOnHand, UnitsPerPack, Product?.PackSize, Product?.DispensingUnit.Name(QtyOnHand));

    public string Display => $"{BatchNo} · exp {ExpiryDate:MM'/'yy} · ₹{Mrp:0.00} · {OnHand} left";

    /// <summary>
    /// What can go back to the distributor. A broken strip cannot — so a batch
    /// with 47 of a ten-strip is four returnable strips and seven to write off,
    /// and the expiry report has to say so in those terms.
    /// </summary>
    public string Returnable
    {
        get
        {
            if (UnitsPerPack <= 1) return $"{QtyOnHand} sealed";

            var sealed_ = QtyOnHand / UnitsPerPack;
            var open = QtyOnHand % UnitsPerPack;

            return open == 0
                ? $"{sealed_} sealed"
                : $"{sealed_} sealed + {open} loose to write off";
        }
    }

    /// <summary>Days until it expires. Negative once it has.</summary>
    public int DaysToExpiry => (int)(ExpiryDate.Date - DateTime.Today).TotalDays;
}

public class StockEntry : BaseEntity
{
    public string EntryNo { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; } = DateTime.Today;
    public string? SupplierName { get; set; }
    public string? SupplierInvoiceNo { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }

    // Set when the entry came from a vendor file rather than being keyed in.
    // The invoice number is what stops the same bill being imported twice.
    public string? ImportedFile { get; set; }
    public string? ImportProfile { get; set; }
    public decimal NetAmount { get; set; }
    public decimal DiscountPercent { get; set; }

    public bool WasImported => ImportedFile is not null;

    /// <summary>
    /// Keyed at the counter to get a sale through, without a supplier bill
    /// behind it. Kept as a first-class entry so the stock has a document, and
    /// flagged so it can be reconciled against the real bill when it turns up.
    /// </summary>
    public bool IsProvisional { get; set; }

    /// <summary>Who keyed it. Blank for anything received the normal way.</summary>
    public string? EnteredBy { get; set; }

    public ICollection<StockEntryItem> Items { get; set; } = [];
}

/// <summary>
/// A vendor's own code for a medicine. Two suppliers call the same drug 000071
/// and 31435, so the mapping is per vendor. Recording it on the first import is
/// what makes every later import of that vendor match without being asked.
/// </summary>
public class VendorProductCode : BaseEntity
{
    public string VendorProfile { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
}

public class StockEntryItem : BaseEntity
{
    public Guid StockEntryId { get; set; }
    public StockEntry StockEntry { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string BatchNo { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    /// <summary>Packs received, as the vendor billed them.</summary>
    public int Quantity { get; set; }

    /// <summary>Scheme quantity (the "+1" in 10+1). Adds to stock, costs nothing.</summary>
    public int FreeQuantity { get; set; }

    /// <summary>Units in each pack received; multiplies into base-unit stock.</summary>
    public int UnitsPerPack { get; set; } = 1;

    public decimal PurchaseRate { get; set; }
    public decimal Mrp { get; set; }

    public decimal LineTotal => Quantity * PurchaseRate;

    /// <summary>Base units this line puts on the shelf, free goods included.</summary>
    public int UnitsReceived => (Quantity + FreeQuantity) * Math.Max(1, UnitsPerPack);
}

public class Sale : BaseEntity
{
    public string BillNo { get; set; } = string.Empty;
    public DateTime BillDate { get; set; } = DateTime.Now;

    /// <summary>Null for a walk-in cash sale — most counter sales have no patient record.</summary>
    public Guid? PatientId { get; set; }
    public Patient? Patient { get; set; }

    /// <summary>Set when the bill was raised from an OPD prescription.</summary>
    public Guid? VisitId { get; set; }
    public Visit? Visit { get; set; }

    public string CustomerName { get; set; } = "Guest";
    public string? DoctorName { get; set; }

    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal NetAmount { get; set; }

    public PaymentMode PaymentMode { get; set; } = PaymentMode.Cash;
    public SaleStatus Status { get; set; } = SaleStatus.Completed;

    /// <summary>UPI/card reference, for reconciling against the payment
    /// gateway or bank statement. Never required.</summary>
    public string? TransactionNo { get; set; }

    /// <summary>
    /// Whether this was issued as a tax invoice. Recorded on the bill rather than
    /// read from settings, so a reprint years later shows what was actually given
    /// to the customer even if the clinic has registered since.
    ///
    /// Not the same as "carries tax" — a registered clinic selling only zero-rated
    /// goods still issues a tax invoice.
    /// </summary>
    public bool IsTaxInvoice { get; set; }

    public ICollection<SaleItem> Items { get; set; } = [];
}

public class SaleItem : BaseEntity
{
    public Guid SaleId { get; set; }
    public Sale Sale { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Guid BatchId { get; set; }

    // Denormalised so a reprinted bill always shows what was actually sold,
    // even if the product or batch is later edited.
    public string ProductName { get; set; } = string.Empty;
    public string BatchNo { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    public string HsnCode { get; set; } = string.Empty;

    /// <summary>Base units sold — 5 means five tablets, not five strips.</summary>
    public int Quantity { get; set; }

    /// <summary>Snapshotted so a reprint shows what the pack was at the time.</summary>
    public int UnitsPerPack { get; set; } = 1;

    public decimal Mrp { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal GstRate { get; set; }

    public decimal TaxableAmount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>"2 × 10 TAB + 5" for the printed bill.</summary>
    public string QuantityDescription => PackMath.Describe(Quantity, UnitsPerPack, PackLabel);

    public string? PackLabel { get; set; }
}

// ── Diagnostics ──────────────────────────────────────────────────────────

/// <summary>
/// One lab test the clinic can bill for. Category is plain text rather than
/// an enum on purpose — clinic staff add tests and categories from the Test
/// Master screen, and a fixed enum would mean a code change every time.
/// </summary>
public class DiagnosticTest : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool Active { get; set; } = true;
}

public class DiagnosticBill : BaseEntity
{
    public string BillNo { get; set; } = string.Empty;
    public DateTime BillDate { get; set; } = DateTime.Now;

    /// <summary>Always set — unlike a pharmacy sale, a diagnostic bill is
    /// always raised against a registered patient.</summary>
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    // Denormalised so history and a reprint still read correctly even if the
    // patient record is edited later — same reasoning as SaleItem.ProductName.
    public string PatientName { get; set; } = string.Empty;
    public string PatientNo { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
    public decimal Discount { get; set; }
    public decimal FinalAmount { get; set; }

    public PaymentMode PaymentMode { get; set; } = PaymentMode.Cash;

    /// <summary>UPI/card reference, for reconciling against the payment
    /// gateway or bank statement. Never required.</summary>
    public string? TransactionNo { get; set; }

    public DiagnosticBillStatus Status { get; set; } = DiagnosticBillStatus.Ordered;
    public string? Remarks { get; set; }

    /// <summary>Set when this bill was raised for tests requested during an
    /// OPD consultation — loaded from that visit rather than picked from
    /// scratch. Null for a walk-in booked directly at the Diagnostics desk.</summary>
    public Guid? VisitId { get; set; }
    public Visit? Visit { get; set; }

    /// <summary>Who sent an outside patient in for these tests. Only asked
    /// for when <see cref="VisitId"/> is null — a patient who came through
    /// our own OPD is referred by the clinic itself, and asking again is
    /// noise.</summary>
    public string? ReferredBy { get; set; }

    public ICollection<DiagnosticBillItem> Items { get; set; } = [];
}

public class DiagnosticBillItem : BaseEntity
{
    public Guid BillId { get; set; }
    public DiagnosticBill Bill { get; set; } = null!;

    /// <summary>Null once the test master row it was billed from is deleted —
    /// the bill keeps its own name/price below regardless.</summary>
    public Guid? TestId { get; set; }

    // Denormalised: a later master price change, or the test being deleted,
    // must never change what an already-printed bill shows.
    public string TestName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal Amount { get; set; }
}

// ── Pediatrics ───────────────────────────────────────────────────────────

/// <summary>Configurable immunisation schedule row. Category is plain text,
/// same reasoning as <see cref="DiagnosticTest.Category"/> — a clinic adds
/// its own vaccines and categories without a code change.</summary>
public class VaccineMaster : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int DoseNumber { get; set; } = 1;

    /// <summary>Age at which due, in days from birth — "6 weeks" is 42, not
    /// "1.5 months", so every schedule comparison is one integer subtraction.</summary>
    public int RecommendedAgeDays { get; set; }

    public string Category { get; set; } = string.Empty;

    public int SequenceOrder { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// One dose actually given. A row only exists once administered — "due" is
/// always computed by diffing <see cref="VaccineMaster"/> against a
/// patient's own rows, never stored as a pending record.
/// </summary>
public class VaccinationRecord : BaseEntity
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public string PatientName { get; set; } = string.Empty;

    /// <summary>Null once the master row it was given from is deleted — the
    /// record keeps its own denormalised name and dose number regardless.</summary>
    public Guid? VaccineId { get; set; }
    public VaccineMaster? Vaccine { get; set; }
    public string VaccineName { get; set; } = string.Empty;
    public int DoseNumber { get; set; }

    public DateTime GivenOn { get; set; } = DateTime.Today;
    public string? BatchNo { get; set; }
    public string? SiteOfInjection { get; set; }
    public string? AdministeredBy { get; set; }

    /// <summary>Which pharmacy product (brand) was actually given — cost
    /// varies by manufacturer, so the charge is picked from here at
    /// vaccination time rather than fixed on <see cref="VaccineMaster"/>.
    /// Null once the product it was picked from is deleted, same reasoning
    /// as <see cref="VaccineId"/> — the denormalised name/manufacturer
    /// below survive regardless.</summary>
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }
    public string? ProductName { get; set; }
    public string? Manufacturer { get; set; }

    /// <summary>The exact batch the dose was drawn from — its stock is
    /// decremented the same way a pharmacy sale would, since this dose
    /// really did leave that batch. Null once the batch is gone (used up
    /// and later pruned); <see cref="BatchNo"/> above keeps the printed
    /// number regardless.</summary>
    public Guid? BatchId { get; set; }
    public Batch? Batch { get; set; }

    /// <summary>Stored hint for the next dose in this vaccine's series,
    /// computed and snapshotted at save time — cheap to read back on the
    /// Reminders screen instead of recomputing the whole schedule for every
    /// patient on every load.</summary>
    public DateTime? NextDueOn { get; set; }

    /// <summary>Set only when this dose was actually charged for — giving a
    /// dose and billing for it are two separate facts, since a free UIP dose
    /// is still clinically recorded but never touches a bill.</summary>
    public Guid? ProcedureBillItemId { get; set; }
}

/// <summary>One anthropometric reading — weight, height, head circumference —
/// for the growth chart. <see cref="AgeDays"/> is computed from
/// <see cref="Patient.DateOfBirth"/> at save time, since <see cref="Patient.Age"/>
/// is whole years only, not enough precision for an infant chart.</summary>
public class GrowthMeasurement : BaseEntity
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    /// <summary>Set when measured during an OPD visit; null for a standalone
    /// growth-chart entry — only ever populated when OPD is switched on.</summary>
    public Guid? VisitId { get; set; }

    public DateTime MeasuredOn { get; set; } = DateTime.Today;
    public int AgeDays { get; set; }

    public decimal? WeightKg { get; set; }

    /// <summary>Length under 2 years, standing height after.</summary>
    public decimal? HeightCm { get; set; }
    public decimal? HeadCircumferenceCm { get; set; }

    /// <summary>Computed weight / (height/100)^2, stored rather than
    /// recomputed since weight and height are not always both present on the
    /// same visit.</summary>
    public decimal? BmiValue { get; set; }
}

/// <summary>
/// Pediatrics-only parent details, kept off the shared <see cref="Patient"/>
/// entity — used by every module, including a pharmacy customer who is
/// nobody's child — as a 1:1 extension instead.
/// </summary>
public class PediatricProfile : BaseEntity
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public string? FatherName { get; set; }
    public string? MotherName { get; set; }

    /// <summary>Separate from <see cref="Patient.Phone"/> — an infant has no
    /// phone of their own.</summary>
    public string? ParentPhone { get; set; }
    public string? ParentOccupation { get; set; }
    public decimal? BirthWeightKg { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Shared small-procedure catalogue — Pediatrics and Dentist both bill flat,
/// priced procedures, so this one table serves both rather than being copied
/// a second time. <see cref="Department"/> is what keeps each module's own
/// Test-Master-style screen showing only its own rows.
/// </summary>
public class Procedure : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public ProcedureDepartment Department { get; set; }
    public decimal Price { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Mirrors <see cref="DiagnosticBill"/>'s shape closely — a flat
/// "pick items, bill them" document, deliberately not named "Pediatric…"
/// since Dentist's own richer billing (a running case balance across many
/// sittings) needs its own entities entirely, but this pair is generic
/// enough for any module needing the simple case.</summary>
public class ProcedureBill : BaseEntity
{
    public string BillNo { get; set; } = string.Empty;
    public DateTime BillDate { get; set; } = DateTime.Now;

    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public string PatientName { get; set; } = string.Empty;
    public string PatientNo { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }
    public decimal Discount { get; set; }
    public decimal FinalAmount { get; set; }

    public PaymentMode PaymentMode { get; set; } = PaymentMode.Cash;
    public string? TransactionNo { get; set; }
    public ProcedureBillStatus Status { get; set; } = ProcedureBillStatus.Ordered;

    /// <summary>Set when raised from an OPD consultation.</summary>
    public Guid? VisitId { get; set; }
    public Visit? Visit { get; set; }

    /// <summary>Only asked for when <see cref="VisitId"/> is null — a
    /// patient who came through our own OPD is referred by the clinic
    /// itself, same reasoning as <see cref="DiagnosticBill.ReferredBy"/>.</summary>
    public string? ReferredBy { get; set; }

    public ICollection<ProcedureBillItem> Items { get; set; } = [];
}

public class ProcedureBillItem : BaseEntity
{
    public Guid BillId { get; set; }
    public ProcedureBill Bill { get; set; } = null!;

    /// <summary>Null once the master row it was billed from is deleted — the
    /// bill keeps its own name/price below regardless.</summary>
    public Guid? ProcedureId { get; set; }

    public string ProcedureName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal Amount { get; set; }
}

// ── Dentist ──────────────────────────────────────────────────────────────

/// <summary>
/// The unit a dental procedure (or package) is tracked as, across however
/// many sittings and payments it takes — the one genuinely new billing shape
/// in this codebase, since every other bill here is paid in one shot.
/// <see cref="TotalCost"/>, <see cref="AmountPaid"/> and <see cref="Balance"/>
/// are computed from the case's own sittings, replacements and payments,
/// never stored — the same "denormalize the fact, compute the gap" approach
/// <c>Product.Shortage</c> already uses.
/// </summary>
public class DentalCase : BaseEntity
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public string PatientName { get; set; } = string.Empty;

    /// <summary>Set for a single-procedure case — mutually exclusive with
    /// <see cref="PackageId"/>, enforced in the service layer.</summary>
    public Guid? ProcedureId { get; set; }
    public string? ProcedureName { get; set; }

    /// <summary>Set for a package case (2.6) — mutually exclusive with
    /// <see cref="ProcedureId"/>.</summary>
    public Guid? PackageId { get; set; }
    public string? PackageName { get; set; }

    /// <summary>Free text — FDI or Universal notation, e.g. "36".</summary>
    public string? ToothNumber { get; set; }

    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;

    public DentalCaseStatus Status { get; set; } = DentalCaseStatus.Planned;
    public DateTime StartedOn { get; set; } = DateTime.Today;
    public DateTime? CompletedOn { get; set; }
    public string? Notes { get; set; }

    /// <summary>The procedure or package price this case was opened
    /// against — snapshotted so a later master price change never moves
    /// the ground under an in-progress case, same discipline as every
    /// other billed line in this codebase.</summary>
    public decimal BaseCost { get; set; }

    public ICollection<DentalSitting> Sittings { get; set; } = [];
    public ICollection<DentalCaseReplacement> Replacements { get; set; } = [];
    public ICollection<DentalPayment> Payments { get; set; } = [];

    public decimal TotalCost => BaseCost
        + Sittings.Where(s => !s.IsDeleted).Sum(s => s.AnesthesiaCost ?? 0)
        + Replacements.Where(r => !r.IsDeleted).Sum(r => r.Amount);

    public decimal AmountPaid => Payments.Where(p => !p.IsDeleted).Sum(p => p.Amount);
    public decimal Balance => Math.Max(0, TotalCost - AmountPaid);
}

/// <summary>One visit toward a <see cref="DentalCase"/> — "sitting 2 of 4",
/// what was done, and what it cost in anesthesia that day.</summary>
public class DentalSitting : BaseEntity
{
    public Guid DentalCaseId { get; set; }
    public DentalCase DentalCase { get; set; } = null!;

    public int SittingNumber { get; set; }
    public DateTime SittingDate { get; set; } = DateTime.Today;

    /// <summary>Usually the case's own doctor; can differ if an associate
    /// handles a particular sitting.</summary>
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;

    public string? WorkDone { get; set; }

    public Guid? AnesthesiaTypeId { get; set; }
    public string? AnesthesiaTypeName { get; set; }
    public decimal? AnesthesiaCost { get; set; }

    /// <summary>Feeds the shared Reminders screen and the Appointments
    /// module directly — "book the next sitting" reads straight off this.</summary>
    public DateTime? NextSittingOn { get; set; }
}

/// <summary>Configurable catalogue of replacement products — crowns,
/// dentures, bridge units.</summary>
public class DentalReplacementMaster : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal UnitCost { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>One billed replacement line on a case — quantity-based, e.g. "3-unit bridge".</summary>
public class DentalCaseReplacement : BaseEntity
{
    public Guid DentalCaseId { get; set; }
    public DentalCase DentalCase { get; set; } = null!;

    /// <summary>Null once the master row it was billed from is deleted —
    /// the line keeps its own name/cost below regardless.</summary>
    public Guid? ReplacementId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal UnitCost { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal Amount { get; set; }
}

/// <summary>Configurable lookup so a sitting's anesthesia cost is picked,
/// not free-typed every time.</summary>
public class AnesthesiaTypeMaster : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal DefaultCost { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>A bundled price for several procedures — "Full Mouth
/// Rehabilitation". A case opened against a package bills the flat
/// <see cref="PackagePrice"/>, not the sum of its <see cref="DentalPackageItem"/>s.</summary>
public class DentalPackageMaster : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal PackagePrice { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Master-side only — what a package is made of, for the
/// clinician's own reference/estimate breakdown. Billing itself always
/// uses <see cref="DentalPackageMaster.PackagePrice"/>, never a sum of these.</summary>
public class DentalPackageItem : BaseEntity
{
    public Guid PackageId { get; set; }
    public DentalPackageMaster Package { get; set; } = null!;

    public Guid? ProcedureId { get; set; }
    public string ProcedureName { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// One payment against a <see cref="DentalCase"/>'s running balance — this
/// app's first installment-billing model. Every other bill here is paid in
/// one shot; a root canal paid across three sittings genuinely cannot be.
/// </summary>
public class DentalPayment : BaseEntity
{
    public Guid DentalCaseId { get; set; }
    public DentalCase DentalCase { get; set; } = null!;

    public string ReceiptNo { get; set; } = string.Empty;
    public DateTime PaidOn { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }
    public PaymentMode PaymentMode { get; set; } = PaymentMode.Cash;
    public string? TransactionNo { get; set; }
}

// ── Pathology Lab ────────────────────────────────────────────────────────
//
// A richer, separate module from Diagnostics — Diagnostics stays the flat
// named-test billing module for ad-hoc or sent-out work; this one is for a
// clinic's own in-house lab, with analyte-level results, reference ranges,
// and a printed report. See CLINICAL_MODULES_DESIGN.md for the reasoning.

/// <summary>One analyte the lab can test for — Glucose, Sodium, TSH.
/// Reference ranges live separately, in <see cref="LabAnalyteReferenceRange"/>,
/// since a single low/high pair cannot express "differs by sex", "differs by
/// age band", or a qualitative result at all.</summary>
public class LabAnalyte : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Units { get; set; } = string.Empty;

    /// <summary>Drives which control the result-entry form shows.</summary>
    public LabResultType ResultType { get; set; } = LabResultType.Numeric;

    /// <summary>Printed precision — Hemoglobin to 1 decimal, Sodium to 0.</summary>
    public int DecimalPlaces { get; set; } = 1;

    public int SequenceOrder { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>One reference range for an analyte — by sex, by age band, or
/// both. Several rows can exist for the same analyte; the matching one is
/// picked by the patient's own age and sex at result-entry time.</summary>
public class LabAnalyteReferenceRange : BaseEntity
{
    public Guid AnalyteId { get; set; }
    public LabAnalyte Analyte { get; set; } = null!;

    /// <summary>Null applies to all.</summary>
    public Gender? Gender { get; set; }
    public decimal? MinAgeYears { get; set; }
    public decimal? MaxAgeYears { get; set; }

    public decimal? LowValue { get; set; }
    public decimal? HighValue { get; set; }

    /// <summary>Qualitative override, e.g. "Non-reactive" — takes over the
    /// printed range column when set, for an analyte whose result is not a
    /// number at all.</summary>
    public string? TextRange { get; set; }

    /// <summary>"Adult Male", "Child 1–5y" — shown on the Test Master screen
    /// so multiple rows per analyte stay legible.</summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>A panel/profile — "CBP", "Thyroid Profile" — made of several
/// analytes via <see cref="LabReportAnalyte"/>.</summary>
public class LabReport : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    /// <summary>Default price for ordering the whole panel.</summary>
    public decimal Price { get; set; }

    public int SequenceOrder { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Many-to-many — the same analyte (Sodium) can sit inside several
/// different panels, each with its own display order.</summary>
public class LabReportAnalyte : BaseEntity
{
    public Guid ReportId { get; set; }
    public LabReport Report { get; set; } = null!;

    public Guid AnalyteId { get; set; }
    public LabAnalyte Analyte { get; set; } = null!;

    public int SequenceOrder { get; set; }
}

/// <summary>A bundled price for several reports — "Full Body Checkup".
/// Same "package price wins over the sum of its parts" rule as Dentist's
/// own packages.</summary>
public class LabPackageMaster : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal PackagePrice { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Master-side only — what a package contains.</summary>
public class LabPackageReport : BaseEntity
{
    public Guid PackageId { get; set; }
    public LabPackageMaster Package { get; set; } = null!;

    public Guid ReportId { get; set; }
    public LabReport Report { get; set; } = null!;
}

/// <summary>
/// One lab order — mirrors <see cref="DiagnosticBill"/>'s shape closely, plus
/// the specimen timestamps and richer status a real in-house lab needs.
/// Deliberately carries no comparison-to-last-time logic — "previous results
/// are not required" — each order stands alone.
/// </summary>
public class LabOrder : BaseEntity
{
    public string OrderNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; } = DateTime.Now;

    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public string PatientName { get; set; } = string.Empty;
    public string PatientNo { get; set; } = string.Empty;

    /// <summary>Set when ordered as a package.</summary>
    public Guid? PackageId { get; set; }

    /// <summary>Set when ordered through our own OPD consultation.</summary>
    public Guid? VisitId { get; set; }
    public Visit? Visit { get; set; }

    /// <summary>Only asked for when <see cref="VisitId"/> is null — same
    /// reasoning as <see cref="DiagnosticBill.ReferredBy"/>.</summary>
    public string? ReferredBy { get; set; }

    public string? SpecimenId { get; set; }
    public DateTime? CollectedOn { get; set; }
    public DateTime? ReceivedOn { get; set; }

    public decimal TotalAmount { get; set; }
    public decimal Discount { get; set; }
    public decimal FinalAmount { get; set; }

    public PaymentMode PaymentMode { get; set; } = PaymentMode.Cash;
    public string? TransactionNo { get; set; }
    public LabOrderStatus Status { get; set; } = LabOrderStatus.Ordered;

    /// <summary>Order-level note — e.g. "Patient fasting".</summary>
    public string? Remarks { get; set; }

    public ICollection<LabOrderReport> Reports { get; set; } = [];
}

/// <summary>One panel ordered on this order — the bill line.</summary>
public class LabOrderReport : BaseEntity
{
    public Guid OrderId { get; set; }
    public LabOrder Order { get; set; } = null!;

    /// <summary>Null once the master row it was ordered from is deleted —
    /// the line keeps its own name/price below regardless.</summary>
    public Guid? ReportId { get; set; }
    public string ReportName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Free-text interpretation, filled in at verification —
    /// stands in for the sample's richer embedded interpretive tables
    /// (an HbA1c/eAG chart, a Hepatitis marker matrix) without the extra
    /// schema those would need. See CLINICAL_MODULES_DESIGN.md §3.7.</summary>
    public string? Notes { get; set; }

    public ICollection<LabResult> Results { get; set; } = [];
}

/// <summary>One analyte's result within an ordered panel.</summary>
public class LabResult : BaseEntity
{
    public Guid OrderReportId { get; set; }
    public LabOrderReport OrderReport { get; set; } = null!;

    /// <summary>Null once the master row it was entered against is
    /// deleted — the result keeps its own name/units below regardless.</summary>
    public Guid? AnalyteId { get; set; }
    public string AnalyteName { get; set; } = string.Empty;
    public string Units { get; set; } = string.Empty;

    /// <summary>Stored as text regardless of <see cref="LabResultType"/> — a
    /// numeric value is formatted using the analyte's own decimal places at
    /// the presentation layer, not parsed back out of this.</summary>
    public string ResultValue { get; set; } = string.Empty;

    /// <summary>Snapshotted at entry time from the matching
    /// <see cref="LabAnalyteReferenceRange"/> — frozen so a later range edit
    /// never changes an already-printed report.</summary>
    public string ReferenceRangeDisplay { get; set; } = string.Empty;

    public LabResultFlag Flag { get; set; } = LabResultFlag.Normal;

    public DateTime EnteredOn { get; set; } = DateTime.Now;
    public string? EnteredBy { get; set; }

    /// <summary>A report does not print until this is set — see
    /// <see cref="LabOrderStatus.Verified"/>.</summary>
    public DateTime? VerifiedOn { get; set; }
    public string? VerifiedBy { get; set; }
}

// ── Shared ─────────────────────────────────────────────────────────────────

/// <summary>Shop identity and print details. In the database, not appsettings,
/// so a second branch is a data change rather than a redeploy.</summary>
public class Setting : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>Sequential document numbers. Tax invoice numbers must not have gaps.</summary>
public class Counter : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public int LastNumber { get; set; }
}

/// <summary>
/// Who is sitting at this PC. Not a multi-user access system — this app is
/// still one PC, one till — just an identity for the person currently using
/// it, so what they do can be attributed to them.
/// </summary>
public class User : BaseEntity
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Pharmacy;
    public bool IsActive { get; set; } = true;

    /// <summary>Forces the change-password screen on next login — set on the
    /// seeded Admin account, on every new user Admin creates, and again by
    /// EnterpriseAdmin whenever it resets somebody's password.</summary>
    public bool MustChangePassword { get; set; }

    public DateTime? LastLoginOn { get; set; }

    /// <summary>"Admin — Front Desk (Pharmacy)", for the recovery screen's user picker.</summary>
    public string Display => $"{Username} — {DisplayName} ({Role})";
}

/// <summary>
/// A correction to what the shelf holds, and why.
///
/// Stock only ever moves by receiving or selling, both of which leave a document.
/// A manual correction has no document, so it writes one — otherwise a shortfall
/// is indistinguishable from theft, and nobody can answer what happened.
/// </summary>
public class StockAdjustment : BaseEntity
{
    public DateTime AdjustedOn { get; set; } = DateTime.Now;

    public Guid BatchId { get; set; }
    public Batch Batch { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // Recorded rather than derived, so the trail survives later changes.
    public string ProductName { get; set; } = string.Empty;
    public string BatchNo { get; set; } = string.Empty;

    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }

    public AdjustmentReason Reason { get; set; } = AdjustmentReason.Recount;
    public string? Notes { get; set; }
    public string? AdjustedBy { get; set; }

    public int Change => QuantityAfter - QuantityBefore;
    public string Direction => Change >= 0 ? $"+{Change}" : Change.ToString();
}

/// <summary>Schedule H1 sales register — statutory, retained three years.</summary>
public class H1RegisterEntry : BaseEntity
{
    public DateTime SoldOn { get; set; }
    public string BillNo { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string BatchNo { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? DoctorName { get; set; }
}

// ── Tenancy ──────────────────────────────────────────────────────────────
// The desktop has no equivalent of this: one install was always exactly one
// clinic, so nothing needed to register which clinic a login belonged to.
// The web edition's every other table is scoped by BaseEntity.TenantId, but
// a tenant registry has to sit outside that scoping — it is what defines a
// tenant, not something that belongs to one. Deliberately NOT a BaseEntity:
// AppDbContext's global query filter is applied by reflection to every
// BaseEntity-derived type, and a tenant row filtered by its own TenantId
// would never be readable by anyone.

/// <summary>One registered clinic. <see cref="Id"/> is the TenantId every
/// other table's rows carry.</summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Globally unique across the whole platform — how a login
    /// page resolves which clinic a request is for, before any user or
    /// password has been checked. Chosen at signup; a URL-safe slug, not a
    /// display name (ClinicProfile.Name, set later under Settings, is the
    /// one that changes freely).</summary>
    public string Slug { get; set; } = string.Empty;

    public string ClinicName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = true;
}
