namespace SivayaanHMS.Core;

public enum Gender
{
    Male = 1,
    Female,
    Other
}

/// <summary>Lifecycle of a single OPD visit, from booking through to completion.</summary>
public enum VisitStatus
{
    Booked = 1,
    Waiting,
    InConsultation,
    Completed,
    Cancelled
}

/// <summary>
/// Drugs and Cosmetics Act schedule. H1 sales must be recorded in a register
/// retained for three years, which is why the schedule lives on the product.
/// </summary>
public enum DrugSchedule
{
    None = 0,
    H = 1,
    H1 = 2,
    X = 3
}

/// <summary>
/// How a bill or a consultation fee was settled.
///
/// Every sale is paid in full at the counter — the clinic does not take credit
/// and does not take part payments, so there is deliberately no Credit option
/// and no outstanding balance anywhere in the system. The numbers are fixed so
/// existing records keep their meaning.
/// </summary>
public enum PaymentMode
{
    Cash = 1,
    Upi = 2,
    Card = 3
}

/// <summary>
/// What one sellable unit of a medicine actually is.
///
/// This is the thing a customer asks for — "six tablets", "one bottle" — and it
/// is deliberately separate from how many come in a pack. A strip holds 1, 3, 5,
/// 10, 15 or any other number of tablets, and that count lives on the product and
/// on each batch, not here.
/// </summary>
/// <remarks>
/// Every member is numbered explicitly. These numbers are what is written to the
/// database, so inserting a new one in the middle would silently turn every
/// tube on the shelf into a vial. New kinds are appended, never interleaved.
/// </remarks>
public enum DispensingUnit
{
    Tablet = 1,
    Capsule = 2,
    Bottle = 3,
    Sachet = 4,
    Tube = 5,
    Vial = 6,
    Piece = 7,

    // A children's clinic sells more than tablets: cough syrup by the bottle,
    // moisturiser and medicated soap over the counter.
    Syrup = 8,
    Moisturizer = 9,
    Soap = 10,

    /// <summary>Anything the list above does not cover.</summary>
    Others = 11
}

public static class DispensingUnits
{
    /// <summary>"tablet" or "tablets", for labels and printed lines.</summary>
    public static string Name(this DispensingUnit unit, int count = 2)
    {
        // "Others" is already plural and is not the name of a thing you can
        // hand over, so it reads as a unit rather than as "3 otherss".
        if (unit == DispensingUnit.Others) return count == 1 ? "unit" : "units";

        // Rows written before this column existed hold 0, which is not a member
        // and printed as "0s" — "59 0s in stock". Read as a plain unit instead,
        // so bad data reads oddly rather than looking broken.
        var single = Enum.IsDefined(unit)
            ? unit.ToString().ToLowerInvariant()
            : "unit";

        return count == 1 ? single : single + "s";
    }
}

/// <summary>Why a count on the shelf did not match the count in the system.</summary>
public enum AdjustmentReason
{
    /// <summary>Physically counted and the system was wrong.</summary>
    Recount = 1,

    Breakage,
    Expired,
    Lost,

    /// <summary>Keyed in wrongly when the stock was received.</summary>
    EntryError,

    Other
}

public enum SaleStatus
{
    Completed = 1,
    Returned,
    Cancelled
}

/// <summary>Where a diagnostic bill's sample is in the lab workflow. Editing
/// or deleting a bill is only allowed before it reaches <see cref="Completed"/>.</summary>
public enum DiagnosticBillStatus
{
    Ordered = 1,
    SampleCollected,
    ResultReceived,
    Completed
}

/// <summary>Which palette the application draws itself in.</summary>
public enum AppThemeKind
{
    Light = 0,
    Dark = 1
}

/// <summary>
/// Lifecycle of a booked-ahead appointment, before it becomes whichever
/// module's own working record check-in creates — see
/// <see cref="AppointmentModuleContext"/>.
/// </summary>
public enum AppointmentStatus
{
    Scheduled = 1,
    CheckedIn,
    Cancelled,
    Rescheduled,
    NoShow
}

/// <summary>
/// Which module's own record an appointment's check-in creates —
/// <see cref="Appointment.LinkedRecordId"/> points into that module's table,
/// not a fixed one. <see cref="General"/> is only ever offered as a booking
/// choice when OPD itself is switched on.
/// </summary>
public enum AppointmentModuleContext
{
    General = 1,
    Pediatrics,
    Dentist,
    PathologyLab
}

/// <summary>Where a row on the shared Reminders screen came from.</summary>
public enum ReminderSourceKind
{
    FollowUp = 1,
    Appointment,
    VaccineDue,
    DentalSitting
}

/// <summary>
/// How a reminder was, or will be, delivered. <see cref="OnScreen"/> is the
/// only value ever written today — the others exist so that turning on
/// WhatsApp or email later is "send instead of just log", not a migration.
/// </summary>
public enum ReminderChannel
{
    OnScreen = 1,
    Whatsapp,
    Email
}

/// <summary>
/// Which clinical module a shared <c>Procedure</c> catalogue row, and the
/// billing built from it, belongs to. <see cref="General"/> exists so any
/// future module needing plain "pick items, bill them" behaviour can reuse
/// the same catalogue rather than a fourth near-duplicate.
/// </summary>
public enum ProcedureDepartment
{
    Pediatrics = 1,
    Dentist,
    General
}

/// <summary>Lifecycle of a <c>ProcedureBill</c> — deliberately simpler than
/// <see cref="DiagnosticBillStatus"/>, since a procedure bill has no lab
/// sample workflow behind it. Editing is refused once Completed, the same
/// rule every status-gated bill in this codebase enforces.</summary>
public enum ProcedureBillStatus
{
    Ordered = 1,
    Completed
}

/// <summary>Lifecycle of a dental case — the unit one procedure (or
/// package) on one patient is tracked as, across however many sittings
/// and payments it takes.</summary>
public enum DentalCaseStatus
{
    Planned = 1,
    InProgress,
    Completed,
    Cancelled
}

/// <summary>What kind of value an analyte's result is, driving which input
/// control the result-entry form shows for it.</summary>
public enum LabResultType
{
    Numeric = 1,
    Text,
    Selection
}

/// <summary>Lifecycle of a lab order. Richer than <see cref="DiagnosticBillStatus"/>
/// because a result has to be entered and verified before it can print —
/// a real pathology-lab norm, not just a billing workflow.</summary>
public enum LabOrderStatus
{
    Ordered = 1,
    SampleCollected,
    ResultEntered,
    Verified,
    Completed
}

/// <summary>Whether one result falls inside its matched reference range.
/// Computed automatically for a numeric result; editable for a qualitative
/// one, since "Reactive" has no numeric range to compare against.</summary>
public enum LabResultFlag
{
    Normal = 1,
    Low,
    High,
    Abnormal
}

/// <summary>
/// A user's role — a label for who they are, not yet a fence around what
/// they can do. Every logged-in role has the same full access today, the
/// same as before login existed; the role exists so that changes to what
/// somebody did are attributed sensibly ("billed by a Pharmacy user") and
/// so restrictions can be added later without another migration.
/// </summary>
public enum UserRole
{
    Admin = 1,
    Doctor = 2,
    Pharmacy = 3,
    Diagnosis = 4
}
