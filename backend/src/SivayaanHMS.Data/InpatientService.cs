using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>A bill line as the screens and prints show it - running or final.</summary>
public record IpBillLineView(
    IpChargeCategory Category, DateTime? ChargeDate, string Description,
    decimal Quantity, decimal Rate, decimal Amount, decimal GstPercent,
    Guid? ChargeId);

/// <summary>
/// The money on an admission at a glance. <see cref="Balance"/> is positive when
/// the patient (or insurer) still owes, negative when the hospital owes a refund.
/// </summary>
public record IpMoney(
    decimal Gross, decimal Gst, decimal Discount, decimal Net, decimal InsuranceAdjustment,
    decimal Advances, decimal Settlements, decimal Insurance, decimal Refunds, decimal Paid, decimal Balance);

/// <summary>The bill as it stands: the running bill while there is no final bill, the final bill once there is.</summary>
public record IpBillView(
    bool IsFinal, Guid? BillId, string? BillNo, DateTime UpTo, int BedDays,
    List<IpBillLineView> Lines, IpMoney Money);

public record AdmitRequest(
    Guid PatientId, Guid DoctorId, Guid BedId, DateTime? AdmittedOn, string? Reason,
    string? AttendantName, string? AttendantRelation, string? AttendantPhone,
    PayerType PayerType, string? InsurerName, string? PolicyNo, bool IsMlc, Guid? VisitId,
    decimal AdvanceAmount, PaymentMode? AdvanceMode, string? AdvanceReference);

public record UpdateAdmissionRequest(
    Guid DoctorId, DateTime AdmittedOn, string? Reason,
    string? AttendantName, string? AttendantRelation, string? AttendantPhone,
    PayerType PayerType, string? InsurerName, string? PolicyNo, bool IsMlc);

public record AddChargeRequest(
    Guid? IpServiceId, IpChargeCategory Category, string Description, DateTime? ChargeDate,
    Guid? DoctorId, int Quantity, decimal Rate);

public record AddPaymentRequest(
    IpPaymentKind Kind, decimal Amount, PaymentMode? Mode, string? Reference, string? Remarks, DateTime? PaidOn);

public record FinalBillRequest(DateTime DischargeOn, decimal Discount, string? DiscountReason);

public record DischargeRequest(DischargeType DischargeType);

public record IpPaymentRow(IpPayment Payment, string PatientName);

public record AddNoteRequest(
    string Text, string? Author, decimal? TemperatureF, int? Pulse, string? BloodPressure, int? Spo2, int? RespiratoryRate);

/// <summary>A bed on the board, and who is in it.</summary>
public record BedBoardItem(
    Guid BedId, string BedNo, string Ward, string RoomType, decimal DailyRent, BedStatus Status, string? Note,
    Guid? AdmissionId, string? IpNo, string? PatientName, string? PatientAge, string? Gender,
    DateTime? AdmittedOn, bool HasFinalBill);

/// <summary>A row of the census and the discharged list.</summary>
public record AdmissionRow(
    Guid Id, string IpNo, string PatientName, string PatientNo, string Age, string Gender,
    string DoctorName, string BedNo, string Ward, DateTime AdmittedOn, DateTime? DischargedOn,
    AdmissionStatus Status, PayerType PayerType, decimal Balance, bool HasFinalBill);

/// <summary>
/// The in-patient module: masters, admissions, beds, charges, payments, notes,
/// the bill and discharge. Every rule about money lives here, not in the controller.
/// </summary>
public class InpatientService(IDbContextFactory<AppDbContext> factory, SettingsService settings, IClock clock)
{
    // ── Masters ────────────────────────────────────────────────────────────

    public async Task<List<RoomType>> GetRoomTypesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.RoomTypes.AsNoTracking().OrderBy(r => r.DailyRent).ThenBy(r => r.Name).ToListAsync();
    }

    public async Task<RoomType> SaveRoomTypeAsync(RoomType input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new InvalidOperationException("Room type name is required.");
        if (input.DailyRent < 0 || input.NursingPerDay < 0) throw new InvalidOperationException("Rates cannot be negative.");
        if (input.GstPercent is < 0 or > 28) throw new InvalidOperationException("GST must be between 0 and 28%.");

        await using var db = await factory.CreateDbContextAsync();
        var entity = input.Id == Guid.Empty ? null : await db.RoomTypes.FirstOrDefaultAsync(r => r.Id == input.Id);

        if (entity is null)
        {
            entity = new RoomType();
            db.RoomTypes.Add(entity);
        }

        // A changed rate applies to stays that start after it; a patient already in
        // the bed keeps the rate copied onto their BedStay.
        entity.Name = input.Name.Trim();
        entity.DailyRent = input.DailyRent;
        entity.NursingPerDay = input.NursingPerDay;
        entity.GstPercent = input.GstPercent;
        entity.Active = input.Active;

        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<List<Bed>> GetBedsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Beds.AsNoTracking().Include(b => b.RoomType)
            .OrderBy(b => b.Ward).ThenBy(b => b.SortOrder).ThenBy(b => b.BedNo).ToListAsync();
    }

    public async Task<Bed> SaveBedAsync(Bed input)
    {
        if (string.IsNullOrWhiteSpace(input.BedNo)) throw new InvalidOperationException("Bed number is required.");
        if (string.IsNullOrWhiteSpace(input.Ward)) throw new InvalidOperationException("Ward is required.");

        await using var db = await factory.CreateDbContextAsync();

        if (!await db.RoomTypes.AnyAsync(r => r.Id == input.RoomTypeId))
            throw new InvalidOperationException("Choose a room type for the bed.");

        var bedNo = input.BedNo.Trim();
        if (await db.Beds.AnyAsync(b => b.BedNo.ToLower() == bedNo.ToLower() && b.Id != input.Id))
            throw new InvalidOperationException($"There is already a bed {bedNo}.");

        var entity = input.Id == Guid.Empty ? null : await db.Beds.FirstOrDefaultAsync(b => b.Id == input.Id);
        if (entity is null)
        {
            entity = new Bed { Status = BedStatus.Available };
            db.Beds.Add(entity);
        }
        else if (!input.Active && entity.Status == BedStatus.Occupied)
        {
            throw new InvalidOperationException($"Bed {entity.BedNo} has a patient in it. Discharge or transfer them first.");
        }

        entity.BedNo = bedNo;
        entity.Ward = input.Ward.Trim();
        entity.RoomTypeId = input.RoomTypeId;
        entity.SortOrder = input.SortOrder;
        entity.Active = input.Active;

        await db.SaveChangesAsync();
        return entity;
    }

    /// <summary>The housekeeping and maintenance moves. Occupied is never set by hand.</summary>
    public async Task SetBedStatusAsync(Guid bedId, BedStatus status, string? note)
    {
        await using var db = await factory.CreateDbContextAsync();
        var bed = await db.Beds.FirstOrDefaultAsync(b => b.Id == bedId)
                  ?? throw new InvalidOperationException("Bed not found.");

        var allowed = (bed.Status, status) switch
        {
            (BedStatus.Cleaning, BedStatus.Available) => true,
            (BedStatus.Available, BedStatus.Blocked) => true,
            (BedStatus.Cleaning, BedStatus.Blocked) => true,
            (BedStatus.Blocked, BedStatus.Available) => true,
            _ => false
        };

        if (!allowed)
            throw new InvalidOperationException($"Bed {bed.BedNo} cannot go from {bed.Status} to {status}.");

        if (status == BedStatus.Blocked && string.IsNullOrWhiteSpace(note))
            throw new InvalidOperationException("Say why the bed is blocked.");

        bed.Status = status;
        bed.Note = status == BedStatus.Blocked ? note!.Trim() : null;
        await SaveGuardedAsync(db, $"Bed {bed.BedNo} was just changed by someone else. Refresh and try again.");
    }

    public async Task<List<IpService>> GetServicesAsync(bool activeOnly = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.IpServices.AsNoTracking();
        if (activeOnly) query = query.Where(s => s.Active);
        return await query.OrderBy(s => s.Category).ThenBy(s => s.Name).ToListAsync();
    }

    public async Task<IpService> SaveServiceAsync(IpService input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new InvalidOperationException("Service name is required.");
        if (input.Rate < 0) throw new InvalidOperationException("Rate cannot be negative.");
        if (input.Category is IpChargeCategory.Room or IpChargeCategory.Nursing)
            throw new InvalidOperationException("Room and nursing charges come from the bed; they are not services.");

        await using var db = await factory.CreateDbContextAsync();
        var entity = input.Id == Guid.Empty ? null : await db.IpServices.FirstOrDefaultAsync(s => s.Id == input.Id);
        if (entity is null)
        {
            entity = new IpService();
            db.IpServices.Add(entity);
        }

        entity.Name = input.Name.Trim();
        entity.Category = input.Category;
        entity.Rate = input.Rate;
        entity.Active = input.Active;

        await db.SaveChangesAsync();
        return entity;
    }

    // ── Bed board and lists ────────────────────────────────────────────────

    public async Task<List<BedBoardItem>> GetBoardAsync()
    {
        await using var db = await factory.CreateDbContextAsync();

        var beds = await db.Beds.AsNoTracking().Include(b => b.RoomType)
            .Where(b => b.Active)
            .OrderBy(b => b.Ward).ThenBy(b => b.SortOrder).ThenBy(b => b.BedNo)
            .ToListAsync();

        var inBeds = await db.Admissions.AsNoTracking().Include(a => a.Patient)
            .Where(a => a.Status == AdmissionStatus.Admitted)
            .ToListAsync();

        var billed = await ActiveBillAdmissionIdsAsync(db, inBeds.Select(a => a.Id));

        return beds.Select(b =>
        {
            var a = inBeds.FirstOrDefault(x => x.BedId == b.Id);
            return new BedBoardItem(
                b.Id, b.BedNo, b.Ward, b.RoomType.Name, b.RoomType.DailyRent, b.Status, b.Note,
                a?.Id, a?.IpNo, a?.Patient.Name, a?.Patient.AgeText, a?.Patient.Gender.ToString(),
                a?.AdmittedOn, a is not null && billed.Contains(a.Id));
        }).ToList();
    }

    /// <summary>
    /// The census (status Admitted) or the discharged list over a date range,
    /// with each admission's balance worked out the same way the bill does.
    /// </summary>
    public async Task<List<AdmissionRow>> SearchAsync(
        AdmissionStatus? status, DateTime? from, DateTime? to, string? term)
    {
        await using var db = await factory.CreateDbContextAsync();

        var query = db.Admissions.AsNoTracking()
            .Include(a => a.Patient).Include(a => a.Doctor).Include(a => a.Bed)
            .AsQueryable();

        if (status is { } s) query = query.Where(a => a.Status == s);

        if (from is { } f && to is { } t)
        {
            var (start, end) = f <= t ? (f.Date, t.Date.AddDays(1)) : (t.Date, f.Date.AddDays(1));
            query = status == AdmissionStatus.Discharged
                ? query.Where(a => a.DischargedOn >= start && a.DischargedOn < end)
                : query.Where(a => a.AdmittedOn >= start && a.AdmittedOn < end);
        }

        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = SearchText.Pattern(term);
            query = query.Where(a =>
                EF.Functions.Like(a.Patient.Name.ToLower(), pattern) ||
                EF.Functions.Like(a.Patient.PatientNo.ToLower(), pattern) ||
                EF.Functions.Like(a.IpNo.ToLower(), pattern) ||
                EF.Functions.Like(a.Patient.Phone, pattern));
        }

        var admissions = await query.OrderByDescending(a => a.AdmittedOn).Take(500).ToListAsync();
        var rows = new List<AdmissionRow>(admissions.Count);

        foreach (var a in admissions)
        {
            var bill = await GetBillInternalAsync(db, a, null);
            rows.Add(new AdmissionRow(
                a.Id, a.IpNo, a.Patient.Name, a.Patient.PatientNo, a.Patient.AgeText, a.Patient.Gender.ToString(),
                a.Doctor.Name, a.Bed.BedNo, a.Bed.Ward, a.AdmittedOn, a.DischargedOn, a.Status, a.PayerType,
                bill.Money.Balance, bill.IsFinal));
        }

        return rows;
    }

    // ── Admission ──────────────────────────────────────────────────────────

    public async Task<Admission> AdmitAsync(AdmitRequest r)
    {
        if (r.AdvanceAmount < 0) throw new InvalidOperationException("The advance cannot be negative.");
        if (r.AdvanceAmount > 0 && r.AdvanceMode is null) throw new InvalidOperationException("Choose how the advance was paid.");

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == r.PatientId)
                      ?? throw new InvalidOperationException("Choose the patient.");

        if (!await db.Doctors.AnyAsync(d => d.Id == r.DoctorId))
            throw new InvalidOperationException("Choose the treating doctor.");

        var current = await db.Admissions
            .Where(a => a.PatientId == r.PatientId && a.Status == AdmissionStatus.Admitted)
            .Select(a => a.IpNo).FirstOrDefaultAsync();
        if (current is not null)
            throw new InvalidOperationException($"{patient.Name} is already admitted ({current}).");

        var bed = await db.Beds.Include(b => b.RoomType).FirstOrDefaultAsync(b => b.Id == r.BedId && b.Active)
                  ?? throw new InvalidOperationException("Choose a bed.");
        if (bed.Status != BedStatus.Available)
            throw new InvalidOperationException($"Bed {bed.BedNo} is not available ({bed.Status}). Choose another bed.");

        var admittedOn = r.AdmittedOn ?? clock.Now;
        if (admittedOn > clock.Now.AddMinutes(5))
            throw new InvalidOperationException("The admission time cannot be in the future.");

        var admission = new Admission
        {
            IpNo = await NumberService.NextAsync(db, NumberService.Admission),
            PatientId = patient.Id,
            DoctorId = r.DoctorId,
            VisitId = r.VisitId,
            AdmittedOn = admittedOn,
            Reason = Clean(r.Reason),
            AttendantName = Clean(r.AttendantName),
            AttendantRelation = Clean(r.AttendantRelation),
            AttendantPhone = Clean(r.AttendantPhone),
            PayerType = r.PayerType,
            InsurerName = r.PayerType == PayerType.Insurance ? Clean(r.InsurerName) : null,
            PolicyNo = r.PayerType == PayerType.Insurance ? Clean(r.PolicyNo) : null,
            IsMlc = r.IsMlc,
            BedId = bed.Id,
        };
        db.Admissions.Add(admission);

        db.BedStays.Add(OpenStay(admission.Id, bed, admittedOn));
        bed.Status = BedStatus.Occupied;
        bed.Note = null;

        if (r.AdvanceAmount > 0)
        {
            db.IpPayments.Add(new IpPayment
            {
                AdmissionId = admission.Id,
                ReceiptNo = await NumberService.NextAsync(db, NumberService.IpAdvance),
                PaidOn = clock.Now,
                Kind = IpPaymentKind.Advance,
                Amount = r.AdvanceAmount,
                Mode = r.AdvanceMode,
                Reference = Clean(r.AdvanceReference),
            });
        }

        await SaveGuardedAsync(db, $"Bed {bed.BedNo} was just taken. Choose another bed.");
        await tx.CommitAsync();
        return admission;
    }

    public async Task UpdateAsync(Guid admissionId, UpdateAdmissionRequest r)
    {
        await using var db = await factory.CreateDbContextAsync();
        var a = await db.Admissions.Include(x => x.Stays).FirstOrDefaultAsync(x => x.Id == admissionId)
                ?? throw new InvalidOperationException("Admission not found.");

        if (a.Status == AdmissionStatus.Cancelled) throw new InvalidOperationException("This admission was cancelled.");
        if (!await db.Doctors.AnyAsync(d => d.Id == r.DoctorId)) throw new InvalidOperationException("Choose the treating doctor.");

        if (r.AdmittedOn != a.AdmittedOn)
        {
            if (await HasActiveBillAsync(db, a.Id))
                throw new InvalidOperationException("The final bill is made. Cancel it before changing the admission time.");
            if (r.AdmittedOn > clock.Now.AddMinutes(5))
                throw new InvalidOperationException("The admission time cannot be in the future.");

            // The first stay starts with the admission - moving one moves the other,
            // as long as it stays before the first transfer.
            var first = a.Stays.Where(s => !s.IsDeleted).OrderBy(s => s.From).First();
            if (first.To is { } firstEnd && r.AdmittedOn >= firstEnd)
                throw new InvalidOperationException("The admission time must be before the first bed transfer.");
            first.From = r.AdmittedOn;
            a.AdmittedOn = r.AdmittedOn;
        }

        a.DoctorId = r.DoctorId;
        a.Reason = Clean(r.Reason);
        a.AttendantName = Clean(r.AttendantName);
        a.AttendantRelation = Clean(r.AttendantRelation);
        a.AttendantPhone = Clean(r.AttendantPhone);
        a.PayerType = r.PayerType;
        a.InsurerName = r.PayerType == PayerType.Insurance ? Clean(r.InsurerName) : null;
        a.PolicyNo = r.PayerType == PayerType.Insurance ? Clean(r.PolicyNo) : null;
        a.IsMlc = r.IsMlc;

        await SaveGuardedAsync(db, "This admission was just changed by someone else. Refresh and try again.");
    }

    /// <summary>Moves the patient to another bed. The old bed goes for cleaning.</summary>
    public async Task TransferAsync(Guid admissionId, Guid toBedId, DateTime? at)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var a = await db.Admissions.Include(x => x.Bed).Include(x => x.Stays)
                    .FirstOrDefaultAsync(x => x.Id == admissionId)
                ?? throw new InvalidOperationException("Admission not found.");

        if (a.Status != AdmissionStatus.Admitted) throw new InvalidOperationException("Only an admitted patient can be moved.");
        if (await HasActiveBillAsync(db, a.Id))
            throw new InvalidOperationException("The final bill is made. Cancel it before moving the patient.");
        if (a.BedId == toBedId) throw new InvalidOperationException("The patient is already in that bed.");

        var to = await db.Beds.Include(b => b.RoomType).FirstOrDefaultAsync(b => b.Id == toBedId && b.Active)
                 ?? throw new InvalidOperationException("Choose a bed.");
        if (to.Status != BedStatus.Available)
            throw new InvalidOperationException($"Bed {to.BedNo} is not available ({to.Status}).");

        var when = at ?? clock.Now;
        var open = a.Stays.Where(s => !s.IsDeleted && s.To is null).OrderByDescending(s => s.From).FirstOrDefault()
                   ?? throw new InvalidOperationException("The patient's current bed stay was not found.");
        if (when <= open.From)
            throw new InvalidOperationException("The transfer time must be after the patient moved into the current bed.");
        if (when > clock.Now.AddMinutes(5))
            throw new InvalidOperationException("The transfer time cannot be in the future.");

        open.To = when;
        a.Bed.Status = BedStatus.Cleaning;
        to.Status = BedStatus.Occupied;
        to.Note = null;
        a.BedId = to.Id;
        db.BedStays.Add(OpenStay(a.Id, to, when));

        await SaveGuardedAsync(db, $"Bed {to.BedNo} was just taken. Choose another bed.");
        await tx.CommitAsync();
    }

    /// <summary>
    /// For an admission entered by mistake. Refused once anything has been charged
    /// or paid: money taken has to be refunded first, so it never vanishes from the
    /// day's collections.
    /// </summary>
    public async Task CancelAdmissionAsync(Guid admissionId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Say why the admission is being cancelled.");

        await using var db = await factory.CreateDbContextAsync();
        var a = await db.Admissions.Include(x => x.Bed).Include(x => x.Stays)
                    .FirstOrDefaultAsync(x => x.Id == admissionId)
                ?? throw new InvalidOperationException("Admission not found.");

        if (a.Status != AdmissionStatus.Admitted) throw new InvalidOperationException("Only a current admission can be cancelled.");
        if (await db.IpCharges.AnyAsync(c => c.AdmissionId == a.Id))
            throw new InvalidOperationException("This admission has charges. Remove them first, or discharge the patient.");
        if (await db.IpPayments.AnyAsync(p => p.AdmissionId == a.Id))
            throw new InvalidOperationException("Money has been taken on this admission. Refund it and discharge instead.");

        a.Status = AdmissionStatus.Cancelled;
        a.DischargedOn = clock.Now;
        a.Reason = $"{a.Reason} [Cancelled: {reason.Trim()}]".Trim();
        foreach (var stay in a.Stays.Where(s => s.To is null)) stay.To = clock.Now;
        a.Bed.Status = BedStatus.Available;

        await SaveGuardedAsync(db, "This admission was just changed by someone else. Refresh and try again.");
    }

    // ── Charges ────────────────────────────────────────────────────────────

    public async Task<IpCharge> AddChargeAsync(Guid admissionId, AddChargeRequest r)
    {
        await using var db = await factory.CreateDbContextAsync();
        var a = await db.Admissions.FirstOrDefaultAsync(x => x.Id == admissionId)
                ?? throw new InvalidOperationException("Admission not found.");

        if (a.Status == AdmissionStatus.Cancelled) throw new InvalidOperationException("This admission was cancelled.");
        if (await HasActiveBillAsync(db, a.Id))
            throw new InvalidOperationException("The final bill is made. Cancel it to add more charges.");

        var quantity = r.Quantity <= 0 ? 1 : r.Quantity;
        if (r.Rate < 0) throw new InvalidOperationException("Rate cannot be negative.");

        var category = r.Category;
        var description = r.Description?.Trim() ?? "";

        if (r.IpServiceId is { } serviceId)
        {
            var service = await db.IpServices.FirstOrDefaultAsync(s => s.Id == serviceId)
                          ?? throw new InvalidOperationException("That service is not in the list.");
            category = service.Category;
            if (description.Length == 0) description = service.Name;
        }

        if (category is IpChargeCategory.Room or IpChargeCategory.Nursing)
            throw new InvalidOperationException("Room and nursing charges come from the bed automatically.");

        if (category == IpChargeCategory.DoctorVisit && r.DoctorId is { } doctorId)
        {
            var doctor = await db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.Id == doctorId)
                         ?? throw new InvalidOperationException("Doctor not found.");
            if (description.Length == 0 || description.Equals("Doctor visit", StringComparison.OrdinalIgnoreCase))
                description = $"{doctor.Name} - visit";
        }

        if (description.Length == 0) throw new InvalidOperationException("Say what the charge is for.");

        var charge = new IpCharge
        {
            AdmissionId = a.Id,
            ChargeDate = (r.ChargeDate ?? clock.Now).Date,
            IpServiceId = r.IpServiceId,
            Category = category,
            Description = description,
            DoctorId = category == IpChargeCategory.DoctorVisit ? r.DoctorId : null,
            Quantity = quantity,
            Rate = r.Rate,
            Amount = Math.Round(quantity * r.Rate, 2),
        };

        db.IpCharges.Add(charge);
        await db.SaveChangesAsync();
        return charge;
    }

    /// <summary>A soft delete: the row stays, with who removed it and when, for the audit trail.</summary>
    public async Task RemoveChargeAsync(Guid admissionId, Guid chargeId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var charge = await db.IpCharges.FirstOrDefaultAsync(c => c.Id == chargeId && c.AdmissionId == admissionId)
                     ?? throw new InvalidOperationException("Charge not found.");

        if (await HasActiveBillAsync(db, admissionId))
            throw new InvalidOperationException("The final bill is made. Cancel it to change the charges.");

        charge.IsDeleted = true;
        await db.SaveChangesAsync();
    }

    // ── Payments ───────────────────────────────────────────────────────────

    public async Task<IpPayment> AddPaymentAsync(Guid admissionId, AddPaymentRequest r)
    {
        if (r.Amount <= 0) throw new InvalidOperationException("Enter an amount.");
        if (r.Kind != IpPaymentKind.Insurance && r.Mode is null) throw new InvalidOperationException("Choose how it was paid.");

        await using var db = await factory.CreateDbContextAsync();
        var a = await db.Admissions.FirstOrDefaultAsync(x => x.Id == admissionId)
                ?? throw new InvalidOperationException("Admission not found.");
        if (a.Status == AdmissionStatus.Cancelled) throw new InvalidOperationException("This admission was cancelled.");

        if ((r.Kind is IpPaymentKind.Settlement or IpPaymentKind.Insurance) && !await HasActiveBillAsync(db, a.Id))
            throw new InvalidOperationException("Make the final bill first. Before that, take the money as an advance.");

        if (r.Kind == IpPaymentKind.Refund)
        {
            var bill = await GetBillInternalAsync(db, a, null);
            var refundable = -bill.Money.Balance;
            if (refundable <= 0)
                throw new InvalidOperationException("Nothing is due back to the patient.");
            if (r.Amount > refundable)
                throw new InvalidOperationException($"At most {refundable:0.00} can be refunded.");
        }

        var payment = new IpPayment
        {
            AdmissionId = a.Id,
            ReceiptNo = await NumberService.NextAsync(db, r.Kind switch
            {
                IpPaymentKind.Advance => NumberService.IpAdvance,
                IpPaymentKind.Settlement => NumberService.IpSettlement,
                IpPaymentKind.Refund => NumberService.IpRefund,
                _ => NumberService.IpInsurance
            }),
            PaidOn = r.PaidOn ?? clock.Now,
            Kind = r.Kind,
            Amount = r.Amount,
            Mode = r.Kind == IpPaymentKind.Insurance ? null : r.Mode,
            Reference = Clean(r.Reference),
            Remarks = Clean(r.Remarks),
        };

        db.IpPayments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    /// <summary>IP money that moved in a date range, for the Collections report.</summary>
    public async Task<List<IpPaymentRow>> GetPaymentsAsync(DateTime from, DateTime to)
    {
        await using var db = await factory.CreateDbContextAsync();
        var start = from.Date;
        var end = to.Date.AddDays(1);

        return await db.IpPayments.AsNoTracking()
            .Where(p => p.PaidOn >= start && p.PaidOn < end)
            .Join(db.Admissions, p => p.AdmissionId, a => a.Id, (p, a) => new { p, a.Patient.Name })
            .OrderBy(x => x.p.PaidOn)
            .Select(x => new IpPaymentRow(x.p, x.Name))
            .ToListAsync();
    }

    // ── The bill ───────────────────────────────────────────────────────────

    /// <summary>The bill as it stands. <paramref name="upTo"/> previews the running bill to a discharge time.</summary>
    public async Task<IpBillView> GetBillAsync(Guid admissionId, DateTime? upTo = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var a = await db.Admissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == admissionId)
                ?? throw new InvalidOperationException("Admission not found.");
        return await GetBillInternalAsync(db, a, upTo);
    }

    /// <summary>
    /// Freezes the running bill to the discharge time, with a discount if one is
    /// given, under a new bill number. The patient is not discharged yet - that is
    /// the next step, once the money is settled or the dues accepted.
    /// </summary>
    public async Task<IpBill> MakeFinalBillAsync(Guid admissionId, FinalBillRequest r, bool canDiscount)
    {
        if (r.Discount < 0) throw new InvalidOperationException("The discount cannot be negative.");
        if (r.Discount > 0 && !canDiscount) throw new InvalidOperationException("Only an admin can give a discount.");
        if (r.Discount > 0 && string.IsNullOrWhiteSpace(r.DiscountReason))
            throw new InvalidOperationException("Give a reason for the discount.");

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var a = await db.Admissions.FirstOrDefaultAsync(x => x.Id == admissionId)
                ?? throw new InvalidOperationException("Admission not found.");
        if (a.Status == AdmissionStatus.Cancelled) throw new InvalidOperationException("This admission was cancelled.");
        if (await HasActiveBillAsync(db, a.Id))
            throw new InvalidOperationException("There is already a final bill. Cancel it to make a new one.");

        // A discharged patient's bill is made to the time they left; a current one's to
        // the discharge time chosen now, which cannot be before admission or in the future.
        var upTo = a.Status == AdmissionStatus.Discharged && a.DischargedOn is { } left ? left : r.DischargeOn;
        if (upTo < a.AdmittedOn) throw new InvalidOperationException("The discharge time is before the admission time.");
        if (upTo > clock.Now.AddMinutes(5)) throw new InvalidOperationException("The discharge time cannot be in the future.");

        var running = await GetBillInternalAsync(db, a, upTo);
        if (running.Lines.Count == 0) throw new InvalidOperationException("There is nothing to bill.");
        if (r.Discount > running.Money.Gross + running.Money.Gst)
            throw new InvalidOperationException("The discount is more than the bill.");

        var bill = new IpBill
        {
            BillNo = await NumberService.NextAsync(db, NumberService.IpBill),
            AdmissionId = a.Id,
            BilledOn = clock.Now,
            BilledUpTo = upTo,
            BedDays = running.BedDays,
            Gross = running.Money.Gross,
            Gst = running.Money.Gst,
            Discount = r.Discount,
            DiscountReason = r.Discount > 0 ? r.DiscountReason!.Trim() : null,
            Net = running.Money.Gross + running.Money.Gst - r.Discount,
            Status = IpBillStatus.Final,
        };

        var sort = 0;
        foreach (var line in running.Lines)
        {
            bill.Lines.Add(new IpBillLine
            {
                Sort = sort++,
                Category = line.Category,
                ChargeDate = line.ChargeDate,
                Description = line.Description,
                Quantity = line.Quantity,
                Rate = line.Rate,
                Amount = line.Amount,
                GstPercent = line.GstPercent,
            });
        }

        db.IpBills.Add(bill);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return bill;
    }

    /// <summary>
    /// "Delete" a final bill: it is kept, marked cancelled with who, when and why,
    /// and the admission is open for a new bill. Payments stay on the admission and
    /// count towards the next bill.
    /// </summary>
    public async Task CancelBillAsync(Guid admissionId, Guid billId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Say why the bill is being cancelled.");

        await using var db = await factory.CreateDbContextAsync();
        var bill = await db.IpBills.FirstOrDefaultAsync(b => b.Id == billId && b.AdmissionId == admissionId)
                   ?? throw new InvalidOperationException("Bill not found.");
        if (bill.Status == IpBillStatus.Cancelled) throw new InvalidOperationException("This bill is already cancelled.");

        bill.Status = IpBillStatus.Cancelled;
        bill.CancelledOn = clock.Now;
        bill.CancelReason = reason.Trim();

        await SaveGuardedAsync(db, "This bill was just changed by someone else. Refresh and try again.");
    }

    /// <summary>
    /// The insurer paid less than the bill and the hospital accepts it as full
    /// settlement: whatever is still due is written off as an insurance adjustment,
    /// the bill now stands at what was paid, and the balance is zero.
    /// </summary>
    public async Task AdjustToInsuranceAsync(Guid admissionId, Guid billId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var a = await db.Admissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == admissionId)
                ?? throw new InvalidOperationException("Admission not found.");
        var bill = await db.IpBills.FirstOrDefaultAsync(b => b.Id == billId && b.AdmissionId == admissionId)
                   ?? throw new InvalidOperationException("Bill not found.");

        if (bill.Status != IpBillStatus.Final) throw new InvalidOperationException("This bill is cancelled.");
        if (!await db.IpPayments.AnyAsync(p => p.AdmissionId == admissionId && p.Kind == IpPaymentKind.Insurance))
            throw new InvalidOperationException("Record the insurance payment first.");

        var view = await GetBillInternalAsync(db, a, null);
        if (view.Money.Balance <= 0) throw new InvalidOperationException("Nothing is due on this bill.");

        bill.InsuranceAdjustment += view.Money.Balance;
        await SaveGuardedAsync(db, "This bill was just changed by someone else. Refresh and try again.");
    }

    /// <summary>Undoes an insurance adjustment, if it was chosen by mistake.</summary>
    public async Task ClearInsuranceAdjustmentAsync(Guid admissionId, Guid billId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var bill = await db.IpBills.FirstOrDefaultAsync(b => b.Id == billId && b.AdmissionId == admissionId)
                   ?? throw new InvalidOperationException("Bill not found.");
        if (bill.Status != IpBillStatus.Final) throw new InvalidOperationException("This bill is cancelled.");

        bill.InsuranceAdjustment = 0;
        await SaveGuardedAsync(db, "This bill was just changed by someone else. Refresh and try again.");
    }

    // ── Discharge ──────────────────────────────────────────────────────────

    public async Task DischargeAsync(Guid admissionId, DischargeRequest r)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var a = await db.Admissions.Include(x => x.Bed).Include(x => x.Stays)
                    .FirstOrDefaultAsync(x => x.Id == admissionId)
                ?? throw new InvalidOperationException("Admission not found.");

        if (a.Status != AdmissionStatus.Admitted) throw new InvalidOperationException("This patient is not admitted.");

        var bill = await db.IpBills.AsNoTracking()
                       .FirstOrDefaultAsync(b => b.AdmissionId == a.Id && b.Status == IpBillStatus.Final)
                   ?? throw new InvalidOperationException("Make the final bill before discharging.");

        var view = await GetBillInternalAsync(db, a, null);
        var general = await settings.GetGeneralAsync();
        if (view.Money.Balance > 0 && !general.IpAllowDischargeWithDues)
            throw new InvalidOperationException($"{view.Money.Balance:0.00} is still due. Collect it before discharge.");
        if (view.Money.Balance < 0)
            throw new InvalidOperationException($"{-view.Money.Balance:0.00} is due back to the patient. Refund it before discharge.");

        a.Status = AdmissionStatus.Discharged;
        a.DischargedOn = bill.BilledUpTo;
        a.DischargeType = r.DischargeType;
        foreach (var stay in a.Stays.Where(s => s.To is null)) stay.To = bill.BilledUpTo;
        a.Bed.Status = BedStatus.Cleaning;

        await SaveGuardedAsync(db, "This admission was just changed by someone else. Refresh and try again.");
        await tx.CommitAsync();
    }

    // ── Notes and summary ──────────────────────────────────────────────────

    public async Task<IpNote> AddNoteAsync(Guid admissionId, AddNoteRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Text) && r.TemperatureF is null && r.Pulse is null
            && string.IsNullOrWhiteSpace(r.BloodPressure) && r.Spo2 is null && r.RespiratoryRate is null)
            throw new InvalidOperationException("Write the note or enter a vital.");

        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Admissions.AnyAsync(a => a.Id == admissionId))
            throw new InvalidOperationException("Admission not found.");

        var note = new IpNote
        {
            AdmissionId = admissionId,
            WrittenOn = clock.Now,
            Author = Clean(r.Author) ?? "",
            Text = r.Text?.Trim() ?? "",
            TemperatureF = r.TemperatureF,
            Pulse = r.Pulse,
            BloodPressure = Clean(r.BloodPressure),
            Spo2 = r.Spo2,
            RespiratoryRate = r.RespiratoryRate,
        };
        db.IpNotes.Add(note);
        await db.SaveChangesAsync();
        return note;
    }

    public async Task<DischargeSummary> SaveSummaryAsync(Guid admissionId, DischargeSummary input)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Admissions.AnyAsync(a => a.Id == admissionId))
            throw new InvalidOperationException("Admission not found.");

        var summary = await db.DischargeSummaries.FirstOrDefaultAsync(s => s.AdmissionId == admissionId);
        if (summary is null)
        {
            summary = new DischargeSummary { AdmissionId = admissionId };
            db.DischargeSummaries.Add(summary);
        }

        summary.FinalDiagnosis = Clean(input.FinalDiagnosis);
        summary.CourseInHospital = Clean(input.CourseInHospital);
        summary.Treatment = Clean(input.Treatment);
        summary.ConditionAtDischarge = Clean(input.ConditionAtDischarge);
        summary.Medicines = Clean(input.Medicines);
        summary.Advice = Clean(input.Advice);
        summary.FollowUpOn = input.FollowUpOn?.Date;
        summary.DoctorName = Clean(input.DoctorName);

        await db.SaveChangesAsync();
        return summary;
    }

    // ── Reading one admission ──────────────────────────────────────────────

    public async Task<Admission?> GetAdmissionAsync(Guid admissionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Admissions.AsNoTracking()
            .Include(a => a.Patient).Include(a => a.Doctor)
            .Include(a => a.Bed).ThenInclude(b => b.RoomType)
            .Include(a => a.Stays.OrderBy(s => s.From))
            .Include(a => a.Charges.OrderBy(c => c.ChargeDate).ThenBy(c => c.CreatedAt))
            .Include(a => a.Payments.OrderBy(p => p.PaidOn))
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == admissionId);
    }

    public async Task<List<IpNote>> GetNotesAsync(Guid admissionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.IpNotes.AsNoTracking().Where(n => n.AdmissionId == admissionId)
            .OrderByDescending(n => n.WrittenOn).ToListAsync();
    }

    public async Task<DischargeSummary?> GetSummaryAsync(Guid admissionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DischargeSummaries.AsNoTracking().FirstOrDefaultAsync(s => s.AdmissionId == admissionId);
    }

    /// <summary>Every bill ever made on the admission, cancelled ones included - the audit trail.</summary>
    public async Task<List<IpBill>> GetBillsAsync(Guid admissionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.IpBills.AsNoTracking().Include(b => b.Lines.OrderBy(l => l.Sort))
            .Where(b => b.AdmissionId == admissionId)
            .OrderByDescending(b => b.BilledOn).ToListAsync();
    }

    // ── Internals ──────────────────────────────────────────────────────────

    private async Task<IpBillView> GetBillInternalAsync(AppDbContext db, Admission a, DateTime? upTo)
    {
        var payments = await db.IpPayments.AsNoTracking().Where(p => p.AdmissionId == a.Id).ToListAsync();

        var bill = await db.IpBills.AsNoTracking().Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.AdmissionId == a.Id && b.Status == IpBillStatus.Final);

        if (bill is not null)
        {
            var lines = bill.Lines.OrderBy(l => l.Sort)
                .Select(l => new IpBillLineView(l.Category, l.ChargeDate, l.Description, l.Quantity, l.Rate, l.Amount, l.GstPercent, null))
                .ToList();
            return new IpBillView(true, bill.Id, bill.BillNo, bill.BilledUpTo, bill.BedDays, lines,
                Money(bill.Gross, bill.Gst, bill.Discount, bill.InsuranceAdjustment, payments));
        }

        if (a.Status == AdmissionStatus.Cancelled)
            return new IpBillView(false, null, null, a.DischargedOn ?? a.AdmittedOn, 0, [], Money(0, 0, 0, 0, payments));

        var end = a.Status == AdmissionStatus.Discharged && a.DischargedOn is { } left ? left : upTo ?? clock.Now;
        if (end < a.AdmittedOn) end = a.AdmittedOn;

        var general = await settings.GetGeneralAsync();
        var stays = await db.BedStays.AsNoTracking().Where(s => s.AdmissionId == a.Id).OrderBy(s => s.From).ToListAsync();
        var charges = await db.IpCharges.AsNoTracking().Where(c => c.AdmissionId == a.Id)
            .OrderBy(c => c.ChargeDate).ThenBy(c => c.CreatedAt).ToListAsync();

        var running = BuildRunningLines(a.AdmittedOn, end, stays, charges, general.IpBedDayRule, general.IpGraceHours);
        var gross = running.Lines.Sum(l => l.Amount);
        var gst = running.Lines.Sum(l => Math.Round(l.Amount * l.GstPercent / 100m, 2));

        return new IpBillView(false, null, null, end, running.BedDays, running.Lines, Money(gross, gst, 0, 0, payments));
    }

    /// <summary>Room and nursing from the stays, then the manual charges - the order a bill is read in.</summary>
    public static (List<IpBillLineView> Lines, int BedDays) BuildRunningLines(
        DateTime admittedOn, DateTime upTo, IReadOnlyList<BedStay> stays, IReadOnlyList<IpCharge> charges,
        BedDayRule rule, int graceHours)
    {
        var spans = stays.Select(s => new StaySpan(s.BedNo, s.RoomTypeName, s.DailyRent, s.NursingPerDay, s.GstPercent, s.From, s.To)).ToList();
        var days = BedDays.Count(admittedOn, upTo, spans, rule, graceHours);
        var runs = BedDays.Runs(days);

        var lines = new List<IpBillLineView>();

        foreach (var run in runs)
        {
            lines.Add(new IpBillLineView(IpChargeCategory.Room, run.FirstDate,
                $"{run.Stay.RoomTypeName} ({run.Stay.BedNo}) {Dates(run)}",
                run.Days, run.Stay.DailyRent, run.Days * run.Stay.DailyRent, run.Stay.GstPercent, null));
        }

        foreach (var run in runs.Where(r => r.Stay.NursingPerDay > 0))
        {
            lines.Add(new IpBillLineView(IpChargeCategory.Nursing, run.FirstDate,
                $"Nursing - {run.Stay.RoomTypeName} {Dates(run)}",
                run.Days, run.Stay.NursingPerDay, run.Days * run.Stay.NursingPerDay, 0, null));
        }

        lines.AddRange(charges
            .OrderBy(c => c.Category).ThenBy(c => c.ChargeDate)
            .Select(c => new IpBillLineView(c.Category, c.ChargeDate, c.Description, c.Quantity, c.Rate, c.Amount, 0, c.Id)));

        return (lines, days.Count);
    }

    private static string Dates(BedDayRun run)
        => run.Days == 1 ? $"{run.FirstDate:dd MMM}" : $"{run.FirstDate:dd MMM} - {run.LastDate:dd MMM}";

    public static IpMoney Money(decimal gross, decimal gst, decimal discount, decimal adjustment, IReadOnlyList<IpPayment> payments)
    {
        decimal Sum(IpPaymentKind k) => payments.Where(p => p.Kind == k).Sum(p => p.Amount);

        var advances = Sum(IpPaymentKind.Advance);
        var settlements = Sum(IpPaymentKind.Settlement);
        var insurance = Sum(IpPaymentKind.Insurance);
        var refunds = Sum(IpPaymentKind.Refund);
        var paid = advances + settlements + insurance - refunds;
        var net = gross + gst - discount;

        return new IpMoney(gross, gst, discount, net, adjustment, advances, settlements, insurance, refunds, paid,
            net - adjustment - paid);
    }

    private static BedStay OpenStay(Guid admissionId, Bed bed, DateTime from) => new()
    {
        AdmissionId = admissionId,
        BedId = bed.Id,
        BedNo = bed.BedNo,
        RoomTypeName = bed.RoomType.Name,
        DailyRent = bed.RoomType.DailyRent,
        NursingPerDay = bed.RoomType.NursingPerDay,
        GstPercent = bed.RoomType.GstPercent,
        From = from,
    };

    private static Task<bool> HasActiveBillAsync(AppDbContext db, Guid admissionId)
        => db.IpBills.AnyAsync(b => b.AdmissionId == admissionId && b.Status == IpBillStatus.Final);

    private static async Task<HashSet<Guid>> ActiveBillAdmissionIdsAsync(AppDbContext db, IEnumerable<Guid> ids)
    {
        var list = ids.ToList();
        return (await db.IpBills.AsNoTracking()
            .Where(b => list.Contains(b.AdmissionId) && b.Status == IpBillStatus.Final)
            .Select(b => b.AdmissionId).ToListAsync()).ToHashSet();
    }

    private static async Task SaveGuardedAsync(AppDbContext db, string conflictMessage)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException(conflictMessage);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
