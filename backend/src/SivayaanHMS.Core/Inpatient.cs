namespace SivayaanHMS.Core;

// ── In-patient (IP) ────────────────────────────────────────────────────────
//
// One Admission per stay ties together the patient, the bed, the doctor, every
// charge and every payment. Room and nursing charges are never stored while the
// patient is in: they are worked out from the BedStay periods whenever the bill
// is read (see BedDays), and copied into an IpBill only when the final bill is
// made. A bill is never deleted - it is cancelled with a reason, kept, and a new
// one is made.

public enum BedStatus
{
    Available = 1,
    Occupied = 2,
    /// <summary>Vacated, waiting for housekeeping. Never straight back to Available.</summary>
    Cleaning = 3,
    /// <summary>Out of use - repair, isolation, anything. Carries a note.</summary>
    Blocked = 4
}

public enum AdmissionStatus
{
    Admitted = 1,
    Discharged = 2,
    /// <summary>Entered by mistake. Only allowed while it has no charges and no payments.</summary>
    Cancelled = 3
}

public enum DischargeType
{
    Normal = 1,
    /// <summary>Discharged against medical advice.</summary>
    Dama = 2,
    /// <summary>Left against medical advice.</summary>
    Lama = 3,
    Referred = 4,
    Death = 5
}

public enum PayerType
{
    Self = 1,
    Insurance = 2
}

/// <summary>
/// The heads a hospital bill is summarised under. Room and Nursing are only ever
/// produced by the bed-day calculation; the rest are added by hand (and, later, by
/// the pharmacy and lab when they post to an admission).
/// </summary>
public enum IpChargeCategory
{
    Room = 1,
    Nursing = 2,
    DoctorVisit = 3,
    Procedure = 4,
    Investigation = 5,
    Medicine = 6,
    Other = 7
}

public enum IpPaymentKind
{
    /// <summary>Taken while the patient is in.</summary>
    Advance = 1,
    /// <summary>The balance collected against the final bill.</summary>
    Settlement = 2,
    /// <summary>Money given back - stored positive, counted against the patient's payments.</summary>
    Refund = 3,
    /// <summary>What the insurer or TPA paid against the bill.</summary>
    Insurance = 4
}

public enum IpBillStatus
{
    Final = 1,
    /// <summary>Kept for the audit trail, with who, when and why. Never deleted.</summary>
    Cancelled = 2
}

/// <summary>How bed days are counted. A clinic setting - see GeneralSettings.</summary>
public enum BedDayRule
{
    /// <summary>One day per midnight crossed; a same-day admission and discharge is one day.</summary>
    MidnightCount = 1,
    /// <summary>Elapsed hours less a grace period, in 24-hour blocks, rounded up.</summary>
    TwentyFourHours = 2
}

/// <summary>A price class of bed: what a day in it costs.</summary>
public class RoomType : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal DailyRent { get; set; }
    public decimal NursingPerDay { get; set; }

    /// <summary>0 for almost every room. Rooms over ₹5,000 a day (other than ICU) carry 5%.</summary>
    public decimal GstPercent { get; set; }

    public bool Active { get; set; } = true;
}

public class Bed : BaseEntity
{
    public string BedNo { get; set; } = string.Empty;

    /// <summary>Free text ("General ward", "First floor"). Beds are grouped by it on the board.</summary>
    public string Ward { get; set; } = string.Empty;

    public Guid RoomTypeId { get; set; }
    public RoomType RoomType { get; set; } = null!;

    public BedStatus Status { get; set; } = BedStatus.Available;

    /// <summary>Why a bed is blocked.</summary>
    public string? Note { get; set; }

    public bool Active { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>The IP service list: what can be added to a bill by hand, at what rate.</summary>
public class IpService : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public IpChargeCategory Category { get; set; } = IpChargeCategory.Procedure;
    public decimal Rate { get; set; }
    public bool Active { get; set; } = true;
}

public class Admission : BaseEntity
{
    public string IpNo { get; set; } = string.Empty;

    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;

    /// <summary>The OPD visit the patient was admitted from, if any.</summary>
    public Guid? VisitId { get; set; }

    public DateTime AdmittedOn { get; set; }
    public string? Reason { get; set; }

    public string? AttendantName { get; set; }
    public string? AttendantRelation { get; set; }
    public string? AttendantPhone { get; set; }

    public PayerType PayerType { get; set; } = PayerType.Self;
    public string? InsurerName { get; set; }
    public string? PolicyNo { get; set; }

    public bool IsMlc { get; set; }

    public AdmissionStatus Status { get; set; } = AdmissionStatus.Admitted;
    public DateTime? DischargedOn { get; set; }
    public DischargeType? DischargeType { get; set; }

    /// <summary>The bed the patient is in now (or was in when discharged).</summary>
    public Guid BedId { get; set; }
    public Bed Bed { get; set; } = null!;

    public List<BedStay> Stays { get; set; } = [];
    public List<IpCharge> Charges { get; set; } = [];
    public List<IpPayment> Payments { get; set; } = [];
}

/// <summary>
/// One period in one bed. A transfer closes the current stay and opens another.
/// The rates are copied from the room type when the stay opens, so a later price
/// change never rewrites what a patient already stayed under.
/// </summary>
public class BedStay : BaseEntity
{
    public Guid AdmissionId { get; set; }

    public Guid BedId { get; set; }
    public Bed Bed { get; set; } = null!;

    public string BedNo { get; set; } = string.Empty;
    public string RoomTypeName { get; set; } = string.Empty;
    public decimal DailyRent { get; set; }
    public decimal NursingPerDay { get; set; }
    public decimal GstPercent { get; set; }

    public DateTime From { get; set; }

    /// <summary>Null while the patient is still in this bed.</summary>
    public DateTime? To { get; set; }
}

/// <summary>A line added by hand: a visit, a procedure, a test, medicines, anything else.</summary>
public class IpCharge : BaseEntity
{
    public Guid AdmissionId { get; set; }

    public DateTime ChargeDate { get; set; }
    public Guid? IpServiceId { get; set; }
    public IpChargeCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>For doctor visits: whose visit it was.</summary>
    public Guid? DoctorId { get; set; }

    public int Quantity { get; set; } = 1;
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
}

public class IpPayment : BaseEntity
{
    public Guid AdmissionId { get; set; }
    public string ReceiptNo { get; set; } = string.Empty;
    public DateTime PaidOn { get; set; }
    public IpPaymentKind Kind { get; set; }

    /// <summary>Always positive. A refund's direction comes from its Kind.</summary>
    public decimal Amount { get; set; }

    /// <summary>How the money moved. Null for an insurance payment, which arrives by the insurer's transfer.</summary>
    public PaymentMode? Mode { get; set; }

    /// <summary>UPI/card reference, or the insurer's claim / UTR number.</summary>
    public string? Reference { get; set; }

    public string? Remarks { get; set; }
}

/// <summary>
/// The final bill: the running bill frozen at discharge, with its own number.
/// Cancelling keeps it (status, reason, who and when) and a new bill takes its
/// place; payments belong to the admission, so they carry over to the new bill.
/// </summary>
public class IpBill : BaseEntity
{
    public string BillNo { get; set; } = string.Empty;
    public Guid AdmissionId { get; set; }
    public Admission Admission { get; set; } = null!;

    public DateTime BilledOn { get; set; }

    /// <summary>Bed days were counted up to this moment - the discharge time.</summary>
    public DateTime BilledUpTo { get; set; }
    public int BedDays { get; set; }

    public decimal Gross { get; set; }
    public decimal Gst { get; set; }
    public decimal Discount { get; set; }
    public string? DiscountReason { get; set; }

    /// <summary>Gross + GST − discount: what the hospital charged.</summary>
    public decimal Net { get; set; }

    /// <summary>
    /// The part of the bill the hospital wrote off to match what the insurer paid
    /// ("adjust the bill to the insurance amount"). Zero unless that was chosen.
    /// </summary>
    public decimal InsuranceAdjustment { get; set; }

    public IpBillStatus Status { get; set; } = IpBillStatus.Final;
    public DateTime? CancelledOn { get; set; }
    public string? CancelReason { get; set; }

    public List<IpBillLine> Lines { get; set; } = [];
}

public class IpBillLine : BaseEntity
{
    public Guid BillId { get; set; }
    public int Sort { get; set; }
    public IpChargeCategory Category { get; set; }
    public DateTime? ChargeDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public decimal GstPercent { get; set; }
}

/// <summary>
/// A doctor's or nurse's note, with optional vitals. Never edited: a correction is
/// a new note, which keeps the record as it was written.
/// </summary>
public class IpNote : BaseEntity
{
    public Guid AdmissionId { get; set; }
    public DateTime WrittenOn { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    public decimal? TemperatureF { get; set; }
    public int? Pulse { get; set; }
    public string? BloodPressure { get; set; }
    public int? Spo2 { get; set; }
    public int? RespiratoryRate { get; set; }
}

public class DischargeSummary : BaseEntity
{
    public Guid AdmissionId { get; set; }
    public string? FinalDiagnosis { get; set; }
    public string? CourseInHospital { get; set; }
    public string? Treatment { get; set; }
    public string? ConditionAtDischarge { get; set; }
    public string? Medicines { get; set; }
    public string? Advice { get; set; }
    public DateTime? FollowUpOn { get; set; }
    public string? DoctorName { get; set; }
}
