using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>OPD: patients, doctors and the single Visit record that covers
/// booking, queue and consultation.</summary>
public class OpdService(IDbContextFactory<AppDbContext> factory, IClock clock, ILogger<OpdService> logger)
{
    // ── Patients ───────────────────────────────────────────────────────────

    /// <summary>
    /// Finds patients by name, patient number or phone. A phone number is matched
    /// on its digits alone, so "98765 00011", "+91 9876500011" and "9876500011"
    /// all return the same family — and all of them, not just the first.
    /// </summary>
    public async Task<List<Patient>> SearchPatientsAsync(string? term, int take = 50)
    {
        if (LooksLikePhone(term))
        {
            var family = await GetPatientsByPhoneAsync(term);
            if (family.Count > 0) return family.Take(take).ToList();
        }

        await using var db = await factory.CreateDbContextAsync();
        var q = db.Patients.AsNoTracking().Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(term))
        {
            // Like, not Contains — Contains becomes instr(), which is case
            // sensitive, so a name typed in lower case found nothing.
            var pattern = $"%{term.Trim()}%";

            q = q.Where(p => EF.Functions.Like(p.Name, pattern)
                          || EF.Functions.Like(p.Phone, pattern)
                          || EF.Functions.Like(p.PatientNo, pattern));
        }

        return await q.OrderByDescending(p => p.CreatedAt).Take(take).ToListAsync();
    }

    /// <summary>Digits and phone punctuation only, and enough of them to be a number.</summary>
    public static bool LooksLikePhone(string? term)
        => !string.IsNullOrWhiteSpace(term)
           && term.All(c => char.IsDigit(c) || c is ' ' or '-' or '+' or '(' or ')')
           && term.Count(char.IsDigit) >= 6;

    /// <summary>
    /// Everyone registered on one phone number. A family shares a number, so a
    /// paediatric clinic routinely has three or four children behind one contact.
    /// </summary>
    public async Task<List<Patient>> GetPatientsByPhoneAsync(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return [];

        var digits = Digits(phone);
        if (digits.Length < 6) return [];

        await using var db = await factory.CreateDbContextAsync();

        // Numbers get stored with spaces, dashes or a +91, so compare on digits.
        var candidates = await db.Patients
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.Phone != "")
            .ToListAsync();

        return candidates
            .Where(p => Digits(p.Phone).EndsWith(digits, StringComparison.Ordinal)
                     || digits.EndsWith(Digits(p.Phone), StringComparison.Ordinal))
            .OrderBy(p => p.Name)
            .ToList();
    }

    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());

    public async Task<Patient> SavePatientAsync(Patient patient)
    {
        await using var db = await factory.CreateDbContextAsync();

        if (patient.Id != Guid.Empty && await db.Patients.AnyAsync(p => p.Id == patient.Id))
        {
            db.Patients.Update(patient);
        }
        else
        {
            patient.PatientNo = await NumberService.NextAsync(db, NumberService.Patient);
            db.Patients.Add(patient);
        }

        await db.SaveChangesAsync();
        return patient;
    }

    /// <summary>Every visit this patient has made, newest first.</summary>
    public async Task<List<Visit>> GetPatientHistoryAsync(Guid patientId)
    {
        await using var db = await factory.CreateDbContextAsync();

        return await db.Visits
            .AsNoTracking()
            .Include(v => v.Doctor)
            .Include(v => v.Prescription)
            .Where(v => !v.IsDeleted && v.PatientId == patientId)
            .OrderByDescending(v => v.ScheduledOn)
            .ToListAsync();
    }

    /// <summary>
    /// Finds a visit by its number or receipt number, whatever date it was on.
    /// This is how a receipt is reprinted for someone who lost theirs months ago.
    /// </summary>
    public async Task<List<Visit>> SearchVisitsAsync(string? term, int take = 100)
    {
        await using var db = await factory.CreateDbContextAsync();
        var q = db.Visits.AsNoTracking().Include(v => v.Patient).Include(v => v.Doctor).Where(v => !v.IsDeleted);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";

            q = q.Where(v => EF.Functions.Like(v.VisitNo, pattern)
                          || (v.FeeReceiptNo != null && EF.Functions.Like(v.FeeReceiptNo, pattern))
                          || EF.Functions.Like(v.Patient.Name, pattern)
                          || EF.Functions.Like(v.Patient.Phone, pattern));
        }

        return await q.OrderByDescending(v => v.ScheduledOn).Take(take).ToListAsync();
    }

    /// <summary>Soft-deletes a patient. Refused while visits still reference them.</summary>
    public async Task<string?> DeletePatientAsync(Guid patientId)
    {
        await using var db = await factory.CreateDbContextAsync();

        if (await db.Visits.AnyAsync(v => v.PatientId == patientId && !v.IsDeleted))
            return "This patient has visits on record and cannot be removed.";

        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == patientId);
        if (patient is null) return "Patient not found.";

        patient.IsDeleted = true;
        await db.SaveChangesAsync();
        return null;
    }

    // ── Doctors ────────────────────────────────────────────────────────────

    public async Task<List<Doctor>> GetDoctorsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Doctors.AsNoTracking().Where(d => !d.IsDeleted && d.IsActive)
                                .OrderBy(d => d.Name).ToListAsync();
    }

    public async Task SaveDoctorAsync(Doctor doctor)
    {
        await using var db = await factory.CreateDbContextAsync();

        if (doctor.Id != Guid.Empty && await db.Doctors.AnyAsync(d => d.Id == doctor.Id))
            db.Doctors.Update(doctor);
        else
            db.Doctors.Add(doctor);

        await db.SaveChangesAsync();
    }

    // ── Visits ─────────────────────────────────────────────────────────────

    public async Task<List<Visit>> GetVisitsAsync(DateTime date)
    {
        await using var db = await factory.CreateDbContextAsync();
        var from = date.Date;
        var to = from.AddDays(1);

        return await db.Visits
            .AsNoTracking()
            .Include(v => v.Patient)
            .Include(v => v.Doctor)
            .Where(v => !v.IsDeleted && v.ScheduledOn >= from && v.ScheduledOn < to)
            .OrderBy(v => v.TokenNo)
            .ToListAsync();
    }

    /// <summary>Visits across a date range, for trend reports — the OPD queue
    /// itself still uses the single-day <see cref="GetVisitsAsync(DateTime)"/>
    /// above, ordered for token display rather than a trend. No Patient/Doctor
    /// include here: a trend only ever sums Fee/FeePaid, never displays a name.</summary>
    public async Task<List<Visit>> GetVisitsAsync(DateTime from, DateTime to)
    {
        await using var db = await factory.CreateDbContextAsync();
        var start = from.Date;
        var end = to.Date.AddDays(1);

        return await db.Visits
            .AsNoTracking()
            .Where(v => !v.IsDeleted && v.ScheduledOn >= start && v.ScheduledOn < end)
            .OrderBy(v => v.ScheduledOn)
            .ToListAsync();
    }

    public async Task<Visit?> GetVisitAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();

        return await db.Visits
            .AsNoTracking()
            .Include(v => v.Patient)
            .Include(v => v.Doctor)
            .Include(v => v.Prescription)
            .Include(v => v.DiagnosticRequests)
            .FirstOrDefaultAsync(v => v.Id == id);
    }

    /// <summary>
    /// Books a visit and allocates the next token for that day. <paramref name="appointmentId"/>
    /// is set only when this visit is the check-in of a previously booked
    /// <c>Appointment</c> (the Appointments module's <c>General</c> context) —
    /// null for the ordinary walk-in flow, which this parameter leaves
    /// completely unchanged.
    /// </summary>
    public async Task<Visit> BookVisitAsync(
        Guid patientId, Guid doctorId, DateTime scheduledOn, string? complaint, decimal fee,
        Guid? appointmentId = null)
    {
        await using var db = await factory.CreateDbContextAsync();

        var from = scheduledOn.Date;
        var to = from.AddDays(1);
        var lastToken = await db.Visits
            .Where(v => v.ScheduledOn >= from && v.ScheduledOn < to)
            .MaxAsync(v => (int?)v.TokenNo) ?? 0;

        var visit = new Visit
        {
            VisitNo = await NumberService.NextAsync(db, NumberService.Visit),
            TokenNo = lastToken + 1,
            PatientId = patientId,
            DoctorId = doctorId,
            ScheduledOn = scheduledOn,
            Complaint = complaint,
            Fee = fee,
            Status = VisitStatus.Booked,
            AppointmentId = appointmentId
        };

        db.Visits.Add(visit);
        await db.SaveChangesAsync();

        logger.LogInformation("Visit {VisitNo} booked, token {Token} for {ScheduledOn}.",
            visit.VisitNo, visit.TokenNo, scheduledOn);

        return visit;
    }

    public async Task SetStatusAsync(Guid visitId, VisitStatus status)
    {
        await using var db = await factory.CreateDbContextAsync();
        var visit = await db.Visits.FirstOrDefaultAsync(v => v.Id == visitId);
        if (visit is null) return;

        // Refused here and not only on screen: a paid or completed visit has a
        // numbered receipt or a consultation hanging off it, and cancelling
        // would strand them against a visit the register says never happened.
        if (status == VisitStatus.Cancelled && !visit.CanCancel)
        {
            var why = visit.FeePaid
                ? $"the fee has already been taken{(string.IsNullOrWhiteSpace(visit.FeeReceiptNo) ? "" : $" on receipt {visit.FeeReceiptNo}")}"
                : $"it is already {visit.Status.ToString().ToLowerInvariant()}";

            throw new InvalidOperationException(
                $"Token {visit.TokenNo} cannot be cancelled — {why}.");
        }

        visit.Status = status;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Records the consultation fee and allocates a receipt number. Collecting
    /// twice is a no-op so a double click cannot burn a number or restate the date.
    /// </summary>
    /// <param name="amount">
    /// What was actually taken, when it differs from what was quoted at booking
    /// — a follow-up seen at half fee, a rounding down, a family concession.
    /// Null keeps the booked fee. The receipt must say what changed hands, so
    /// this writes the visit's fee as well as the receipt.
    /// </param>
    public async Task<Visit?> CollectFeeAsync(
        Guid visitId, PaymentMode mode = PaymentMode.Cash, decimal? amount = null, string? transactionNo = null)
    {
        await using var db = await factory.CreateDbContextAsync();

        var visit = await db.Visits
            .Include(v => v.Patient)
            .Include(v => v.Doctor)
            .FirstOrDefaultAsync(v => v.Id == visitId);

        if (visit is null) return null;
        if (visit.FeePaid) return visit;

        // A negative fee is not a concession, it is a typo, and it would print a
        // receipt the clinic owes money on.
        if (amount is { } corrected && corrected >= 0 && corrected != visit.Fee)
        {
            logger.LogInformation("Fee on {VisitNo} corrected at the desk: {Old:0.00} -> {New:0.00}.",
                visit.VisitNo, visit.Fee, corrected);
            visit.Fee = corrected;
        }

        visit.FeePaid = true;
        visit.FeeReceiptNo = await NumberService.NextAsync(db, NumberService.FeeReceipt);
        visit.FeePaidOn = clock.Now;
        visit.FeePaymentMode = mode;
        visit.FeeTransactionNo = string.IsNullOrWhiteSpace(transactionNo) ? null : transactionNo.Trim();

        await db.SaveChangesAsync();

        logger.LogInformation("Receipt {ReceiptNo} for {Amount:0.00} ({Mode}) against visit {VisitNo}.",
            visit.FeeReceiptNo, visit.Fee, mode, visit.VisitNo);

        return visit;
    }

    /// <summary>Saves the consultation and replaces the prescription and the
    /// requested-tests list in one step.</summary>
    public async Task SaveConsultationAsync(
        Visit edited, IEnumerable<PrescriptionItem> prescription,
        IEnumerable<VisitDiagnosticRequest> diagnosticRequests, bool complete)
    {
        var items = prescription.ToList();
        var tests = diagnosticRequests.ToList();

        await using var db = await factory.CreateDbContextAsync();

        var visit = await db.Visits.Include(v => v.Prescription).Include(v => v.DiagnosticRequests)
                                   .FirstOrDefaultAsync(v => v.Id == edited.Id)
                    ?? throw new InvalidOperationException("Visit not found.");

        visit.Complaint = edited.Complaint;
        visit.Diagnosis = edited.Diagnosis;
        visit.Notes = edited.Notes;
        visit.WeightKg = edited.WeightKg;
        visit.BloodPressure = edited.BloodPressure;
        visit.TemperatureF = edited.TemperatureF;
        visit.HeightCm = edited.HeightCm;
        visit.HeartRateBpm = edited.HeartRateBpm;
        visit.Spo2Percent = edited.Spo2Percent;
        visit.Fee = edited.Fee;
        visit.FollowUpOn = edited.FollowUpOn;

        db.PrescriptionItems.RemoveRange(visit.Prescription);

        foreach (var item in items)
        {
            db.PrescriptionItems.Add(new PrescriptionItem
            {
                VisitId = visit.Id,
                ProductId = item.ProductId,
                MedicineName = item.MedicineName,
                Dosage = item.Dosage,
                Frequency = item.Frequency,
                Days = item.Days,
                Quantity = item.Quantity,
                Instructions = item.Instructions
            });
        }

        db.VisitDiagnosticRequests.RemoveRange(visit.DiagnosticRequests);

        foreach (var test in tests)
        {
            db.VisitDiagnosticRequests.Add(new VisitDiagnosticRequest
            {
                VisitId = visit.Id,
                TestId = test.TestId,
                TestName = test.TestName
            });
        }

        if (complete) visit.Status = VisitStatus.Completed;

        await db.SaveChangesAsync();
    }
}
