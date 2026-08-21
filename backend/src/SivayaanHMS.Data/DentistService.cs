using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>
/// The Dentist module's data layer: cases (one procedure or package, tracked
/// across however many sittings and payments it takes), the sittings and
/// replacement lines billed against them, and the four masters behind it all
/// — procedures (shared with Pediatrics via <see cref="ProcedureBillsService"/>),
/// replacements, anesthesia types, and packages.
/// </summary>
public class DentistService(IDbContextFactory<AppDbContext> factory, IClock clock)
{
    // ── Cases ──────────────────────────────────────────────────────────────

    /// <summary>Opens a case against exactly one of a procedure or a
    /// package — never both, never neither.</summary>
    public async Task<DentalCase> OpenCaseAsync(DentalCase dentalCase)
    {
        if (dentalCase.PatientId == Guid.Empty) throw new InvalidOperationException("Pick a patient before opening a case.");
        if (dentalCase.DoctorId == Guid.Empty) throw new InvalidOperationException("Pick a doctor before opening a case.");

        var hasProcedure = dentalCase.ProcedureId is not null;
        var hasPackage = dentalCase.PackageId is not null;
        if (hasProcedure == hasPackage)
            throw new InvalidOperationException("A case is opened against either a procedure or a package, not both.");

        await using var db = await factory.CreateDbContextAsync();

        decimal baseCost;
        string label;

        if (hasProcedure)
        {
            var procedure = await db.Procedures.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dentalCase.ProcedureId);
            if (procedure is null) throw new InvalidOperationException("That procedure no longer exists.");
            baseCost = procedure.Price;
            label = procedure.Name;
        }
        else
        {
            var package = await db.DentalPackageMasters.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dentalCase.PackageId);
            if (package is null) throw new InvalidOperationException("That package no longer exists.");
            baseCost = package.PackagePrice;
            label = package.Name;
        }

        var entity = new DentalCase
        {
            Id = dentalCase.Id == Guid.Empty ? Guid.NewGuid() : dentalCase.Id,
            PatientId = dentalCase.PatientId,
            PatientName = dentalCase.PatientName,
            ProcedureId = dentalCase.ProcedureId,
            ProcedureName = hasProcedure ? label : null,
            PackageId = dentalCase.PackageId,
            PackageName = hasPackage ? label : null,
            ToothNumber = dentalCase.ToothNumber,
            DoctorId = dentalCase.DoctorId,
            Status = DentalCaseStatus.Planned,
            StartedOn = dentalCase.StartedOn == default ? clock.Now.Date : dentalCase.StartedOn,
            Notes = dentalCase.Notes,
            BaseCost = baseCost
        };

        db.DentalCases.Add(entity);
        await db.SaveChangesAsync();

        return entity;
    }

    public async Task UpdateCaseStatusAsync(Guid caseId, DentalCaseStatus status)
    {
        await using var db = await factory.CreateDbContextAsync();
        var dentalCase = await db.DentalCases.FirstOrDefaultAsync(c => c.Id == caseId);
        if (dentalCase is null) return;

        dentalCase.Status = status;
        if (status == DentalCaseStatus.Completed) dentalCase.CompletedOn = clock.Now.Date;

        await db.SaveChangesAsync();
    }

    public async Task<DentalCase?> GetCaseAsync(Guid caseId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DentalCases.AsNoTracking()
            .Include(c => c.Sittings)
            .Include(c => c.Replacements)
            .Include(c => c.Payments)
            .FirstOrDefaultAsync(c => c.Id == caseId);
    }

    public async Task<List<DentalCase>> GetCasesByPatientAsync(Guid patientId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DentalCases.AsNoTracking()
            .Include(c => c.Sittings).Include(c => c.Replacements).Include(c => c.Payments)
            .Where(c => c.PatientId == patientId)
            .OrderByDescending(c => c.StartedOn)
            .ToListAsync();
    }

    /// <summary>Every case still open — Planned or InProgress — for the
    /// Cases tab's default list.</summary>
    public async Task<List<DentalCase>> GetOpenCasesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DentalCases.AsNoTracking()
            .Include(c => c.Sittings).Include(c => c.Replacements).Include(c => c.Payments)
            .Where(c => !c.IsDeleted && (c.Status == DentalCaseStatus.Planned || c.Status == DentalCaseStatus.InProgress))
            .OrderBy(c => c.StartedOn)
            .ToListAsync();
    }

    // ── Sittings ───────────────────────────────────────────────────────────

    /// <summary>Adds the next sitting to a case, numbering it in sequence,
    /// and moves the case to InProgress if it was still only Planned.</summary>
    public async Task<DentalSitting> AddSittingAsync(DentalSitting sitting)
    {
        await using var db = await factory.CreateDbContextAsync();

        var dentalCase = await db.DentalCases.Include(c => c.Sittings)
            .FirstOrDefaultAsync(c => c.Id == sitting.DentalCaseId);
        if (dentalCase is null) throw new InvalidOperationException("That case no longer exists.");

        if (dentalCase.Status is DentalCaseStatus.Completed or DentalCaseStatus.Cancelled)
            throw new InvalidOperationException($"This case is {dentalCase.Status} — no more sittings can be added.");

        var nextNumber = dentalCase.Sittings.Where(s => !s.IsDeleted).Select(s => s.SittingNumber).DefaultIfEmpty(0).Max() + 1;

        var entity = new DentalSitting
        {
            Id = sitting.Id == Guid.Empty ? Guid.NewGuid() : sitting.Id,
            DentalCaseId = sitting.DentalCaseId,
            SittingNumber = nextNumber,
            SittingDate = sitting.SittingDate == default ? clock.Now.Date : sitting.SittingDate,
            DoctorId = sitting.DoctorId == Guid.Empty ? dentalCase.DoctorId : sitting.DoctorId,
            WorkDone = sitting.WorkDone,
            AnesthesiaTypeId = sitting.AnesthesiaTypeId,
            AnesthesiaTypeName = sitting.AnesthesiaTypeName,
            AnesthesiaCost = sitting.AnesthesiaCost,
            NextSittingOn = sitting.NextSittingOn
        };

        db.DentalSittings.Add(entity);

        if (dentalCase.Status == DentalCaseStatus.Planned) dentalCase.Status = DentalCaseStatus.InProgress;

        await db.SaveChangesAsync();
        return entity;
    }

    // ── Replacements ───────────────────────────────────────────────────────

    public async Task<DentalCaseReplacement> AddReplacementAsync(Guid caseId, Guid? replacementId, string name, decimal unitCost, int quantity)
    {
        if (quantity <= 0) throw new InvalidOperationException("Quantity must be at least 1.");

        await using var db = await factory.CreateDbContextAsync();

        var dentalCase = await db.DentalCases.FirstOrDefaultAsync(c => c.Id == caseId);
        if (dentalCase is null) throw new InvalidOperationException("That case no longer exists.");
        if (dentalCase.Status is DentalCaseStatus.Completed or DentalCaseStatus.Cancelled)
            throw new InvalidOperationException($"This case is {dentalCase.Status} — no more replacements can be added.");

        var entity = new DentalCaseReplacement
        {
            DentalCaseId = caseId, ReplacementId = replacementId, Name = name,
            UnitCost = unitCost, Quantity = quantity, Amount = unitCost * quantity
        };

        db.DentalCaseReplacements.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    // ── Payments ───────────────────────────────────────────────────────────

    /// <summary>Records one payment against a case's running balance —
    /// never the whole bill in one shot. Allowed even once a case is
    /// Completed (a late payment is still a payment); refused once
    /// Cancelled.</summary>
    public async Task<DentalPayment> RecordPaymentAsync(DentalPayment payment)
    {
        if (payment.Amount <= 0) throw new InvalidOperationException("Enter an amount greater than zero.");

        await using var db = await factory.CreateDbContextAsync();

        var dentalCase = await db.DentalCases.FirstOrDefaultAsync(c => c.Id == payment.DentalCaseId);
        if (dentalCase is null) throw new InvalidOperationException("That case no longer exists.");
        if (dentalCase.Status == DentalCaseStatus.Cancelled)
            throw new InvalidOperationException("This case is cancelled — no further payment can be taken against it.");

        var entity = new DentalPayment
        {
            Id = payment.Id == Guid.Empty ? Guid.NewGuid() : payment.Id,
            DentalCaseId = payment.DentalCaseId,
            ReceiptNo = await NumberService.NextAsync(db, NumberService.DentalPayment),
            PaidOn = payment.PaidOn == default ? clock.Now : payment.PaidOn,
            Amount = payment.Amount,
            PaymentMode = payment.PaymentMode,
            TransactionNo = payment.TransactionNo
        };

        db.DentalPayments.Add(entity);
        await db.SaveChangesAsync();

        return entity;
    }

    /// <summary>Every payment recorded in a date range (inclusive), for the
    /// Dashboard's revenue split — a payment does not carry the patient's
    /// name itself, only its case does, so that is included.</summary>
    public async Task<List<DentalPayment>> SearchPaymentsAsync(DateTime from, DateTime to)
    {
        await using var db = await factory.CreateDbContextAsync();
        var toExclusive = to.Date.AddDays(1);

        return await db.DentalPayments.AsNoTracking().Include(p => p.DentalCase)
            .Where(p => p.PaidOn >= from.Date && p.PaidOn < toExclusive)
            .OrderByDescending(p => p.PaidOn)
            .ToListAsync();
    }

    // ── Replacement master ─────────────────────────────────────────────────

    public async Task<List<DentalReplacementMaster>> SearchReplacementsAsync(string? term, bool activeOnly = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.DentalReplacementMasters.AsNoTracking().Where(r => !r.IsDeleted);

        if (activeOnly) query = query.Where(r => r.Active);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";
            query = query.Where(r => EF.Functions.Like(r.Name, pattern) || EF.Functions.Like(r.Category, pattern));
        }

        return await query.OrderBy(r => r.Category).ThenBy(r => r.Name).ToListAsync();
    }

    public async Task SaveReplacementAsync(DentalReplacementMaster replacement)
    {
        if (string.IsNullOrWhiteSpace(replacement.Name))
            throw new InvalidOperationException("Replacement name is required.");

        await using var db = await factory.CreateDbContextAsync();
        var existing = replacement.Id == Guid.Empty ? null : await db.DentalReplacementMasters.FirstOrDefaultAsync(r => r.Id == replacement.Id);

        if (existing is null)
        {
            if (replacement.Id == Guid.Empty) replacement.Id = Guid.NewGuid();
            db.DentalReplacementMasters.Add(replacement);
        }
        else
        {
            existing.Name = replacement.Name.Trim();
            existing.Category = replacement.Category.Trim();
            existing.UnitCost = replacement.UnitCost;
            existing.Active = replacement.Active;
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteReplacementAsync(Guid replacementId)
    {
        await using var db = await factory.CreateDbContextAsync();

        if (await db.DentalCaseReplacements.AnyAsync(r => r.ReplacementId == replacementId))
            throw new InvalidOperationException(
                "This replacement has already been billed on a case and cannot be deleted. Deactivate it instead.");

        var replacement = await db.DentalReplacementMasters.FirstOrDefaultAsync(r => r.Id == replacementId);
        if (replacement is null) return;

        db.DentalReplacementMasters.Remove(replacement);
        await db.SaveChangesAsync();
    }

    // ── Anesthesia type master ─────────────────────────────────────────────

    public async Task<List<AnesthesiaTypeMaster>> SearchAnesthesiaTypesAsync(bool activeOnly = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.AnesthesiaTypeMasters.AsNoTracking().Where(a => !a.IsDeleted);
        if (activeOnly) query = query.Where(a => a.Active);
        return await query.OrderBy(a => a.Name).ToListAsync();
    }

    public async Task SaveAnesthesiaTypeAsync(AnesthesiaTypeMaster type)
    {
        if (string.IsNullOrWhiteSpace(type.Name))
            throw new InvalidOperationException("Anesthesia type name is required.");

        await using var db = await factory.CreateDbContextAsync();
        var existing = type.Id == Guid.Empty ? null : await db.AnesthesiaTypeMasters.FirstOrDefaultAsync(a => a.Id == type.Id);

        if (existing is null)
        {
            if (type.Id == Guid.Empty) type.Id = Guid.NewGuid();
            db.AnesthesiaTypeMasters.Add(type);
        }
        else
        {
            existing.Name = type.Name.Trim();
            existing.DefaultCost = type.DefaultCost;
            existing.Active = type.Active;
        }

        await db.SaveChangesAsync();
    }

    // ── Package master ─────────────────────────────────────────────────────

    public async Task<List<DentalPackageMaster>> SearchPackagesAsync(string? term, bool activeOnly = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.DentalPackageMasters.AsNoTracking().Where(p => !p.IsDeleted);

        if (activeOnly) query = query.Where(p => p.Active);
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(p => EF.Functions.Like(p.Name, $"%{term.Trim()}%"));

        return await query.OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<List<DentalPackageItem>> GetPackageItemsAsync(Guid packageId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DentalPackageItems.AsNoTracking().Where(i => i.PackageId == packageId).ToListAsync();
    }

    /// <summary>Saves the package header and replaces its item breakdown
    /// wholesale — the breakdown is reference-only, never billed from
    /// directly, so there is nothing to preserve line-by-line the way a
    /// bill's own items must be.</summary>
    public async Task<DentalPackageMaster> SavePackageAsync(DentalPackageMaster package, IReadOnlyList<DentalPackageItem> items)
    {
        if (string.IsNullOrWhiteSpace(package.Name))
            throw new InvalidOperationException("Package name is required.");

        await using var db = await factory.CreateDbContextAsync();
        var existing = package.Id == Guid.Empty ? null : await db.DentalPackageMasters.FirstOrDefaultAsync(p => p.Id == package.Id);

        DentalPackageMaster entity;
        if (existing is null)
        {
            entity = package;
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            db.DentalPackageMasters.Add(entity);
        }
        else
        {
            entity = existing;
            entity.Name = package.Name.Trim();
            entity.Description = package.Description;
            entity.PackagePrice = package.PackagePrice;
            entity.Active = package.Active;

            var oldItems = db.DentalPackageItems.Where(i => i.PackageId == entity.Id);
            db.DentalPackageItems.RemoveRange(oldItems);
        }

        foreach (var item in items)
            db.DentalPackageItems.Add(new DentalPackageItem
            {
                PackageId = entity.Id, ProcedureId = item.ProcedureId,
                ProcedureName = item.ProcedureName, Quantity = item.Quantity
            });

        await db.SaveChangesAsync();
        return entity;
    }

    public async Task DeletePackageAsync(Guid packageId)
    {
        await using var db = await factory.CreateDbContextAsync();

        if (await db.DentalCases.AnyAsync(c => c.PackageId == packageId))
            throw new InvalidOperationException(
                "This package has already been used to open a case and cannot be deleted. Deactivate it instead.");

        var package = await db.DentalPackageMasters.FirstOrDefaultAsync(p => p.Id == packageId);
        if (package is null) return;

        db.DentalPackageItems.RemoveRange(db.DentalPackageItems.Where(i => i.PackageId == packageId));
        db.DentalPackageMasters.Remove(package);
        await db.SaveChangesAsync();
    }
}
