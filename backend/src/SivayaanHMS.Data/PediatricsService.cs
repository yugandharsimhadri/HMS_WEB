using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>A vaccine dose due for a patient — computed, never stored. See
/// <see cref="VaccinationRecord"/>'s own class doc for why.</summary>
public class DueVaccine
{
    public Guid PatientId { get; init; }
    public string PatientName { get; init; } = string.Empty;
    public Guid VaccineId { get; init; }
    public string VaccineName { get; init; } = string.Empty;
    public int DoseNumber { get; init; }
    public DateTime DueOn { get; init; }
}

/// <summary>Where one dose on a patient's immunization card stands.</summary>
public enum ImmunizationStatus
{
    Given,
    Overdue,
    DueSoon,
    Upcoming
}

/// <summary>One row of a patient's immunization card — a single Vaccine
/// Master dose, checked against what this patient has actually been given.
/// <see cref="RecommendedOn"/> is null when the patient has no date of
/// birth on file, in which case every not-yet-given row reads Upcoming —
/// there is nothing to compare an age-in-days recommendation against.</summary>
public class ImmunizationCardRow
{
    public Guid VaccineId { get; init; }
    public string VaccineName { get; init; } = string.Empty;
    public int DoseNumber { get; init; }
    public int RecommendedAgeDays { get; init; }
    public DateTime? RecommendedOn { get; init; }
    public ImmunizationStatus Status { get; init; }
    public VaccinationRecord? Given { get; init; }
}

/// <summary>
/// The Pediatrics module's data layer: the vaccine schedule, what has
/// actually been given, growth measurements, and the parent-details
/// extension to <see cref="Patient"/>. Procedure billing itself lives in
/// <see cref="ProcedureBillsService"/>, shared with Dentist.
/// </summary>
public class PediatricsService(IDbContextFactory<AppDbContext> factory, IClock clock, ILogger<PediatricsService> logger)
{
    // ── Vaccine master ─────────────────────────────────────────────────────

    public async Task<List<VaccineMaster>> SearchVaccinesAsync(string? term, bool activeOnly = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.VaccineMasters.AsNoTracking().Where(v => !v.IsDeleted);

        if (activeOnly) query = query.Where(v => v.Active);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = SearchText.Pattern(term);
            query = query.Where(v => EF.Functions.Like(v.Name.ToLower(), pattern) || EF.Functions.Like(v.Category.ToLower(), pattern));
        }

        return await query.OrderBy(v => v.SequenceOrder).ThenBy(v => v.RecommendedAgeDays).ToListAsync();
    }

    public async Task<List<string>> GetVaccineCategoriesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.VaccineMasters.AsNoTracking().Where(v => !v.IsDeleted)
            .Select(v => v.Category).Distinct().OrderBy(c => c).ToListAsync();
    }

    public async Task SaveVaccineAsync(VaccineMaster vaccine)
    {
        if (string.IsNullOrWhiteSpace(vaccine.Name))
            throw new InvalidOperationException("Vaccine name is required.");

        await using var db = await factory.CreateDbContextAsync();
        var existing = vaccine.Id == Guid.Empty ? null : await db.VaccineMasters.FirstOrDefaultAsync(v => v.Id == vaccine.Id);

        if (existing is null)
        {
            if (vaccine.Id == Guid.Empty) vaccine.Id = Guid.NewGuid();
            db.VaccineMasters.Add(vaccine);
        }
        else
        {
            existing.Name = vaccine.Name.Trim();
            existing.DoseNumber = vaccine.DoseNumber;
            existing.RecommendedAgeDays = vaccine.RecommendedAgeDays;
            existing.Category = vaccine.Category.Trim();
            existing.SequenceOrder = vaccine.SequenceOrder;
            existing.Active = vaccine.Active;
        }

        await db.SaveChangesAsync();
    }

    public async Task SetVaccineActiveAsync(Guid vaccineId, bool active)
    {
        await using var db = await factory.CreateDbContextAsync();
        var vaccine = await db.VaccineMasters.FirstOrDefaultAsync(v => v.Id == vaccineId);
        if (vaccine is null) return;

        vaccine.Active = active;
        await db.SaveChangesAsync();
    }

    /// <summary>Refuses once a dose has been given from it — same guard
    /// shape as <see cref="DiagnosticsService.DeleteTestAsync"/>.</summary>
    public async Task DeleteVaccineAsync(Guid vaccineId)
    {
        await using var db = await factory.CreateDbContextAsync();

        if (await db.VaccinationRecords.AnyAsync(r => r.VaccineId == vaccineId))
            throw new InvalidOperationException(
                "This vaccine has already been given to a patient and cannot be deleted. Deactivate it instead.");

        var vaccine = await db.VaccineMasters.FirstOrDefaultAsync(v => v.Id == vaccineId);
        if (vaccine is null) return;

        db.VaccineMasters.Remove(vaccine);
        await db.SaveChangesAsync();
    }

    // ── Vaccination history ────────────────────────────────────────────────

    /// <summary>Records a dose given. Computes and snapshots
    /// <see cref="VaccinationRecord.NextDueOn"/> from the next dose in the
    /// same vaccine's series, if there is one.
    ///
    /// Retries on <see cref="DbUpdateConcurrencyException"/> when a batch is
    /// involved, same reasoning and same fix as
    /// <see cref="PharmacyService.SaveSaleAsync"/> — this deducts from the
    /// same Batch.QtyOnHand a pharmacy sale does, so it is exposed to the
    /// identical two-counters-one-batch race (SAAS_MIGRATION.md finding 2).
    /// </summary>
    public async Task<VaccinationRecord> RecordVaccinationAsync(VaccinationRecord record)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RecordVaccinationAttemptAsync(record);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts)
            {
                logger.LogWarning(
                    "Vaccination record save hit a stock concurrency conflict, retrying (attempt {Attempt} of {Max}).",
                    attempt, maxAttempts);
            }
        }
    }

    private async Task<VaccinationRecord> RecordVaccinationAttemptAsync(VaccinationRecord record)
    {
        await using var db = await factory.CreateDbContextAsync();

        if (record.Id == Guid.Empty) record.Id = Guid.NewGuid();

        if (record.VaccineId is { } vaccineId)
        {
            var master = await db.VaccineMasters.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vaccineId);
            if (master is not null)
            {
                var next = await db.VaccineMasters.AsNoTracking()
                    .Where(v => !v.IsDeleted && v.Active && v.Name == master.Name && v.DoseNumber == master.DoseNumber + 1)
                    .FirstOrDefaultAsync();

                if (next is not null)
                {
                    var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == record.PatientId);
                    if (patient?.DateOfBirth is { } dob)
                        record.NextDueOn = dob.Date.AddDays(next.RecommendedAgeDays);
                }
            }
        }

        // The dose really did leave this batch, so its stock moves the same
        // way a pharmacy sale would move it — one place stock is ever
        // deducted from, not a second ledger Pediatrics keeps on the side.
        if (record.BatchId is { } batchId)
        {
            var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == batchId)
                        ?? throw new InvalidOperationException($"Batch not found for {record.ProductName}.");

            if (batch.QtyOnHand < 1)
                throw new InvalidOperationException(
                    $"No stock left of {record.ProductName} (batch {batch.BatchNo}) to record this dose.");

            batch.QtyOnHand -= 1;
        }

        db.VaccinationRecords.Add(record);
        await db.SaveChangesAsync();

        return record;
    }

    public async Task<List<VaccinationRecord>> GetVaccinationHistoryAsync(Guid patientId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.VaccinationRecords.AsNoTracking()
            .Where(r => !r.IsDeleted && r.PatientId == patientId)
            .OrderByDescending(r => r.GivenOn)
            .ToListAsync();
    }

    /// <summary>Doses due for one patient — every active vaccine dose whose
    /// age has arrived and has no matching given record yet.</summary>
    public async Task<List<DueVaccine>> GetDueVaccinesForPatientAsync(Guid patientId, int leadDays = 0)
    {
        await using var db = await factory.CreateDbContextAsync();

        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId);
        if (patient?.DateOfBirth is not { } dob) return [];

        var vaccines = await db.VaccineMasters.AsNoTracking().Where(v => !v.IsDeleted && v.Active).ToListAsync();
        var given = await db.VaccinationRecords.AsNoTracking()
            .Where(r => !r.IsDeleted && r.PatientId == patientId && r.VaccineId != null)
            .Select(r => r.VaccineId!.Value)
            .ToListAsync();

        var cutoff = clock.Now.Date.AddDays(leadDays + 1);

        return vaccines
            .Where(v => !given.Contains(v.Id) && dob.Date.AddDays(v.RecommendedAgeDays) < cutoff)
            .Select(v => new DueVaccine
            {
                PatientId = patientId, PatientName = patient.Name,
                VaccineId = v.Id, VaccineName = v.Name, DoseNumber = v.DoseNumber,
                DueOn = dob.Date.AddDays(v.RecommendedAgeDays)
            })
            .OrderBy(d => d.DueOn)
            .ToList();
    }

    /// <summary>
    /// Doses due across every patient with a date of birth on file — what
    /// the shared Reminders screen's vaccine source pulls from. Only
    /// patients with a <see cref="Patient.DateOfBirth"/> are considered:
    /// <see cref="Patient.Age"/> alone is whole years, not enough precision
    /// to say a dose is due this week rather than sometime this year.
    /// </summary>
    public async Task<List<DueVaccine>> GetDueVaccinesAsync(int leadDays)
    {
        await using var db = await factory.CreateDbContextAsync();

        var vaccines = await db.VaccineMasters.AsNoTracking().Where(v => !v.IsDeleted && v.Active).ToListAsync();
        if (vaccines.Count == 0) return [];

        var today = clock.Now.Date;
        var cutoff = today.AddDays(leadDays + 1);
        var earliestRelevantDob = today.AddDays(-vaccines.Max(v => v.RecommendedAgeDays));

        var patients = await db.Patients.AsNoTracking()
            .Where(p => !p.IsDeleted && p.DateOfBirth != null && p.DateOfBirth >= earliestRelevantDob)
            .ToListAsync();
        if (patients.Count == 0) return [];

        var patientIds = patients.Select(p => p.Id).ToList();
        var givenByPatient = await db.VaccinationRecords.AsNoTracking()
            .Where(r => !r.IsDeleted && patientIds.Contains(r.PatientId) && r.VaccineId != null)
            .Select(r => new { r.PatientId, VaccineId = r.VaccineId!.Value })
            .ToListAsync();
        var givenLookup = givenByPatient.Select(g => (g.PatientId, g.VaccineId)).ToHashSet();

        var due = new List<DueVaccine>();
        foreach (var patient in patients)
        {
            var dob = patient.DateOfBirth!.Value.Date;
            foreach (var vaccine in vaccines)
            {
                if (givenLookup.Contains((patient.Id, vaccine.Id))) continue;

                var dueOn = dob.AddDays(vaccine.RecommendedAgeDays);
                if (dueOn >= cutoff) continue;

                due.Add(new DueVaccine
                {
                    PatientId = patient.Id, PatientName = patient.Name,
                    VaccineId = vaccine.Id, VaccineName = vaccine.Name, DoseNumber = vaccine.DoseNumber,
                    DueOn = dueOn
                });
            }
        }

        return due.OrderBy(d => d.DueOn).ToList();
    }

    /// <summary>
    /// Every active Vaccine Master dose, checked against what this patient
    /// has actually been given — the single comparison the Immunization tab
    /// is built from, against this clinic's own Vaccine Master (seeded from
    /// the WHO / IAP schedule) rather than a separately maintained
    /// reference table, so "given" and "recommended" are never two lists
    /// that can drift apart. Ordered the same way Vaccine Master itself is,
    /// so it reads as one continuous schedule rather than shuffled by
    /// given/not-given.
    /// </summary>
    public async Task<List<ImmunizationCardRow>> GetImmunizationCardAsync(Guid patientId, int dueSoonLeadDays = 14)
    {
        await using var db = await factory.CreateDbContextAsync();

        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId);
        var dob = patient?.DateOfBirth?.Date;

        var vaccines = await db.VaccineMasters.AsNoTracking()
            .Where(v => !v.IsDeleted && v.Active)
            .OrderBy(v => v.SequenceOrder).ThenBy(v => v.RecommendedAgeDays)
            .ToListAsync();

        var given = await db.VaccinationRecords.AsNoTracking()
            .Where(r => !r.IsDeleted && r.PatientId == patientId && r.VaccineId != null)
            .ToListAsync();

        // A dose recorded more than once against the same Vaccine Master
        // row (a correction, a re-entry) shows its most recent giving.
        var givenByVaccine = given
            .GroupBy(r => r.VaccineId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.GivenOn).First());

        var today = clock.Now.Date;
        var dueSoonCutoff = today.AddDays(dueSoonLeadDays);
        var rows = new List<ImmunizationCardRow>();

        foreach (var v in vaccines)
        {
            var recommendedOn = dob?.AddDays(v.RecommendedAgeDays);

            if (givenByVaccine.TryGetValue(v.Id, out var record))
            {
                rows.Add(new ImmunizationCardRow
                {
                    VaccineId = v.Id, VaccineName = v.Name, DoseNumber = v.DoseNumber,
                    RecommendedAgeDays = v.RecommendedAgeDays, RecommendedOn = recommendedOn,
                    Status = ImmunizationStatus.Given, Given = record
                });
                continue;
            }

            var status = recommendedOn switch
            {
                null => ImmunizationStatus.Upcoming,
                { } d when d < today => ImmunizationStatus.Overdue,
                { } d when d <= dueSoonCutoff => ImmunizationStatus.DueSoon,
                _ => ImmunizationStatus.Upcoming
            };

            rows.Add(new ImmunizationCardRow
            {
                VaccineId = v.Id, VaccineName = v.Name, DoseNumber = v.DoseNumber,
                RecommendedAgeDays = v.RecommendedAgeDays, RecommendedOn = recommendedOn,
                Status = status
            });
        }

        return rows;
    }

    // ── Growth chart ───────────────────────────────────────────────────────

    public async Task<GrowthMeasurement> RecordGrowthAsync(GrowthMeasurement measurement)
    {
        await using var db = await factory.CreateDbContextAsync();

        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == measurement.PatientId);
        if (patient?.DateOfBirth is { } dob)
            measurement.AgeDays = Math.Max(0, (measurement.MeasuredOn.Date - dob.Date).Days);

        if (measurement.WeightKg is { } kg && measurement.HeightCm is { } cm && cm > 0)
        {
            var metres = cm / 100m;
            measurement.BmiValue = Math.Round(kg / (metres * metres), 1);
        }

        if (measurement.Id == Guid.Empty) measurement.Id = Guid.NewGuid();
        db.GrowthMeasurements.Add(measurement);
        await db.SaveChangesAsync();

        return measurement;
    }

    public async Task<List<GrowthMeasurement>> GetGrowthHistoryAsync(Guid patientId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.GrowthMeasurements.AsNoTracking()
            .Where(m => !m.IsDeleted && m.PatientId == patientId)
            .OrderBy(m => m.MeasuredOn)
            .ToListAsync();
    }

    // ── Parent details ─────────────────────────────────────────────────────

    public async Task<PediatricProfile?> GetProfileAsync(Guid patientId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.PediatricProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.PatientId == patientId);
    }

    public async Task SaveProfileAsync(PediatricProfile profile)
    {
        await using var db = await factory.CreateDbContextAsync();
        var existing = await db.PediatricProfiles.FirstOrDefaultAsync(p => p.PatientId == profile.PatientId);

        if (existing is null)
        {
            if (profile.Id == Guid.Empty) profile.Id = Guid.NewGuid();
            db.PediatricProfiles.Add(profile);
        }
        else
        {
            existing.FatherName = profile.FatherName;
            existing.MotherName = profile.MotherName;
            existing.ParentPhone = profile.ParentPhone;
            existing.ParentOccupation = profile.ParentOccupation;
            existing.BirthWeightKg = profile.BirthWeightKg;
            existing.Notes = profile.Notes;
        }

        await db.SaveChangesAsync();
    }
}
