using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>A lab order line as assembled on screen, before it is persisted.</summary>
public class LabOrderLine
{
    public Guid? ReportId { get; set; }
    public string ReportName { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

/// <summary>
/// The Pathology Lab module's data layer: the analyte and panel masters,
/// packages, order billing, and result entry with automatic flagging
/// against the matched reference range. Deliberately separate from
/// <see cref="DiagnosticsService"/> — see the design doc's own note on why.
/// </summary>
public class PathologyLabService(IDbContextFactory<AppDbContext> factory, IClock clock)
{
    // ── Analyte master ─────────────────────────────────────────────────────

    public async Task<List<LabAnalyte>> SearchAnalytesAsync(string? term, bool activeOnly = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.LabAnalytes.AsNoTracking().Where(a => !a.IsDeleted);

        if (activeOnly) query = query.Where(a => a.Active);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = SearchText.Pattern(term);
            query = query.Where(a => EF.Functions.Like(a.Name.ToLower(), pattern) || EF.Functions.Like(a.Category.ToLower(), pattern));
        }

        return await query.OrderBy(a => a.Category).ThenBy(a => a.SequenceOrder).ThenBy(a => a.Name).ToListAsync();
    }

    public async Task<List<string>> GetAnalyteCategoriesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.LabAnalytes.AsNoTracking().Where(a => !a.IsDeleted)
            .Select(a => a.Category).Distinct().OrderBy(c => c).ToListAsync();
    }

    public async Task SaveAnalyteAsync(LabAnalyte analyte)
    {
        if (string.IsNullOrWhiteSpace(analyte.Name))
            throw new InvalidOperationException("Analyte name is required.");

        await using var db = await factory.CreateDbContextAsync();
        var existing = analyte.Id == Guid.Empty ? null : await db.LabAnalytes.FirstOrDefaultAsync(a => a.Id == analyte.Id);

        if (existing is null)
        {
            if (analyte.Id == Guid.Empty) analyte.Id = Guid.NewGuid();
            db.LabAnalytes.Add(analyte);
        }
        else
        {
            existing.Name = analyte.Name.Trim();
            existing.Category = analyte.Category.Trim();
            existing.Units = analyte.Units.Trim();
            existing.ResultType = analyte.ResultType;
            existing.DecimalPlaces = analyte.DecimalPlaces;
            existing.SequenceOrder = analyte.SequenceOrder;
            existing.Active = analyte.Active;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Refuses once used in a report definition or actually
    /// resulted — same guard shape as <see cref="DiagnosticsService.DeleteTestAsync"/>.</summary>
    public async Task DeleteAnalyteAsync(Guid analyteId)
    {
        await using var db = await factory.CreateDbContextAsync();

        if (await db.LabReportAnalytes.AnyAsync(x => x.AnalyteId == analyteId))
            throw new InvalidOperationException(
                "This analyte is used in a report definition and cannot be deleted. Deactivate it instead.");
        if (await db.LabResults.AnyAsync(x => x.AnalyteId == analyteId))
            throw new InvalidOperationException(
                "This analyte already has results on file and cannot be deleted. Deactivate it instead.");

        var analyte = await db.LabAnalytes.FirstOrDefaultAsync(a => a.Id == analyteId);
        if (analyte is null) return;

        db.LabAnalytes.Remove(analyte);
        await db.SaveChangesAsync();
    }

    // ── Reference ranges ───────────────────────────────────────────────────

    public async Task<List<LabAnalyteReferenceRange>> GetReferenceRangesAsync(Guid analyteId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.LabAnalyteReferenceRanges.AsNoTracking()
            .Where(r => r.AnalyteId == analyteId).OrderBy(r => r.Label).ToListAsync();
    }

    public async Task SaveReferenceRangeAsync(LabAnalyteReferenceRange range)
    {
        await using var db = await factory.CreateDbContextAsync();

        // BaseEntity.Id defaults to a fresh random Guid, not Guid.Empty, so
        // "new or existing" is decided by whether that id is actually in
        // the database yet — not by whether it happens to equal Guid.Empty.
        var existing = await db.LabAnalyteReferenceRanges.FirstOrDefaultAsync(r => r.Id == range.Id);

        if (existing is null)
        {
            db.LabAnalyteReferenceRanges.Add(range);
        }
        else
        {
            existing.Gender = range.Gender;
            existing.MinAgeYears = range.MinAgeYears;
            existing.MaxAgeYears = range.MaxAgeYears;
            existing.LowValue = range.LowValue;
            existing.HighValue = range.HighValue;
            existing.TextRange = range.TextRange;
            existing.Label = range.Label;
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteReferenceRangeAsync(Guid rangeId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var range = await db.LabAnalyteReferenceRanges.FirstOrDefaultAsync(r => r.Id == rangeId);
        if (range is null) return;

        db.LabAnalyteReferenceRanges.Remove(range);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The range that applies to one patient for one analyte, right now —
    /// the first row whose gender (if set) and age band (if set) both admit
    /// the patient. Not ordered by "most specific first"; a clinic with
    /// overlapping ranges for the same analyte should keep them
    /// non-overlapping in the first place.
    /// </summary>
    public async Task<LabAnalyteReferenceRange?> MatchReferenceRangeAsync(Guid analyteId, Gender gender, decimal ageYears)
    {
        await using var db = await factory.CreateDbContextAsync();
        var ranges = await db.LabAnalyteReferenceRanges.AsNoTracking()
            .Where(r => r.AnalyteId == analyteId).ToListAsync();

        return ranges.FirstOrDefault(r =>
            (r.Gender is null || r.Gender == gender) &&
            (r.MinAgeYears is null || ageYears >= r.MinAgeYears) &&
            (r.MaxAgeYears is null || ageYears <= r.MaxAgeYears));
    }

    // ── Report (panel) master ──────────────────────────────────────────────

    public async Task<List<LabReport>> SearchReportsAsync(string? term, bool activeOnly = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.LabReports.AsNoTracking().Where(r => !r.IsDeleted);

        if (activeOnly) query = query.Where(r => r.Active);
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(r => EF.Functions.Like(r.Name.ToLower(), SearchText.Pattern(term)));

        return await query.OrderBy(r => r.SequenceOrder).ThenBy(r => r.Name).ToListAsync();
    }

    public async Task<List<LabAnalyte>> GetReportAnalytesAsync(Guid reportId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.LabReportAnalytes.AsNoTracking()
            .Where(x => x.ReportId == reportId)
            .OrderBy(x => x.SequenceOrder)
            .Select(x => x.Analyte)
            .ToListAsync();
    }

    /// <summary>Saves the report header and replaces its analyte membership
    /// wholesale — the membership defines what the panel is, so there is
    /// nothing to preserve line-by-line the way a bill's own items must be.</summary>
    public async Task<LabReport> SaveReportAsync(LabReport report, IReadOnlyList<Guid> analyteIds)
    {
        if (string.IsNullOrWhiteSpace(report.Name))
            throw new InvalidOperationException("Report name is required.");

        await using var db = await factory.CreateDbContextAsync();
        var existing = report.Id == Guid.Empty ? null : await db.LabReports.FirstOrDefaultAsync(r => r.Id == report.Id);

        LabReport entity;
        if (existing is null)
        {
            entity = report;
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            db.LabReports.Add(entity);
        }
        else
        {
            entity = existing;
            entity.Name = report.Name.Trim();
            entity.Category = report.Category.Trim();
            entity.Price = report.Price;
            entity.SequenceOrder = report.SequenceOrder;
            entity.Active = report.Active;

            db.LabReportAnalytes.RemoveRange(db.LabReportAnalytes.Where(x => x.ReportId == entity.Id));
        }

        var order = 0;
        foreach (var analyteId in analyteIds)
            db.LabReportAnalytes.Add(new LabReportAnalyte { ReportId = entity.Id, AnalyteId = analyteId, SequenceOrder = order++ });

        await db.SaveChangesAsync();
        return entity;
    }

    public async Task DeleteReportAsync(Guid reportId)
    {
        await using var db = await factory.CreateDbContextAsync();

        if (await db.LabOrderReports.AnyAsync(x => x.ReportId == reportId))
            throw new InvalidOperationException(
                "This report has already been ordered and cannot be deleted. Deactivate it instead.");
        if (await db.LabPackageReports.AnyAsync(x => x.ReportId == reportId))
            throw new InvalidOperationException(
                "This report is used in a package and cannot be deleted. Remove it from the package first.");

        var report = await db.LabReports.FirstOrDefaultAsync(r => r.Id == reportId);
        if (report is null) return;

        db.LabReportAnalytes.RemoveRange(db.LabReportAnalytes.Where(x => x.ReportId == reportId));
        db.LabReports.Remove(report);
        await db.SaveChangesAsync();
    }

    // ── Package master ─────────────────────────────────────────────────────

    public async Task<List<LabPackageMaster>> SearchPackagesAsync(string? term, bool activeOnly = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.LabPackageMasters.AsNoTracking().Where(p => !p.IsDeleted);

        if (activeOnly) query = query.Where(p => p.Active);
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(p => EF.Functions.Like(p.Name.ToLower(), SearchText.Pattern(term)));

        return await query.OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<List<LabReport>> GetPackageReportsAsync(Guid packageId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.LabPackageReports.AsNoTracking()
            .Where(x => x.PackageId == packageId).Select(x => x.Report).ToListAsync();
    }

    public async Task<LabPackageMaster> SavePackageAsync(LabPackageMaster package, IReadOnlyList<Guid> reportIds)
    {
        if (string.IsNullOrWhiteSpace(package.Name))
            throw new InvalidOperationException("Package name is required.");

        await using var db = await factory.CreateDbContextAsync();
        var existing = package.Id == Guid.Empty ? null : await db.LabPackageMasters.FirstOrDefaultAsync(p => p.Id == package.Id);

        LabPackageMaster entity;
        if (existing is null)
        {
            entity = package;
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            db.LabPackageMasters.Add(entity);
        }
        else
        {
            entity = existing;
            entity.Name = package.Name.Trim();
            entity.PackagePrice = package.PackagePrice;
            entity.Active = package.Active;

            db.LabPackageReports.RemoveRange(db.LabPackageReports.Where(x => x.PackageId == entity.Id));
        }

        foreach (var reportId in reportIds)
            db.LabPackageReports.Add(new LabPackageReport { PackageId = entity.Id, ReportId = reportId });

        await db.SaveChangesAsync();
        return entity;
    }

    public async Task DeletePackageAsync(Guid packageId)
    {
        await using var db = await factory.CreateDbContextAsync();

        if (await db.LabOrders.AnyAsync(o => o.PackageId == packageId))
            throw new InvalidOperationException(
                "This package has already been ordered and cannot be deleted. Deactivate it instead.");

        var package = await db.LabPackageMasters.FirstOrDefaultAsync(p => p.Id == packageId);
        if (package is null) return;

        db.LabPackageReports.RemoveRange(db.LabPackageReports.Where(x => x.PackageId == packageId));
        db.LabPackageMasters.Remove(package);
        await db.SaveChangesAsync();
    }

    // ── Orders (billing) ───────────────────────────────────────────────────

    public async Task<LabOrder> SaveOrderAsync(LabOrder order, IReadOnlyList<LabOrderLine> lines)
    {
        if (lines.Count == 0) throw new InvalidOperationException("Add at least one report to the order.");

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var existing = order.Id == Guid.Empty
            ? null
            : await db.LabOrders.Include(o => o.Reports).ThenInclude(r => r.Results).FirstOrDefaultAsync(o => o.Id == order.Id);

        LabOrder entity;

        if (existing is null)
        {
            entity = order;
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            entity.OrderNo = await NumberService.NextAsync(db, NumberService.LabOrder);
            entity.Status = LabOrderStatus.Ordered;
            db.LabOrders.Add(entity);
        }
        else
        {
            if (existing.Status is LabOrderStatus.Verified or LabOrderStatus.Completed)
                throw new InvalidOperationException($"This order is {existing.Status} and can no longer be edited.");

            entity = existing;
            entity.PatientId = order.PatientId;
            entity.PatientName = order.PatientName;
            entity.PatientNo = order.PatientNo;
            entity.PackageId = order.PackageId;
            entity.VisitId = order.VisitId;
            entity.ReferredBy = order.ReferredBy;
            entity.SpecimenId = order.SpecimenId;
            entity.CollectedOn = order.CollectedOn;
            entity.ReceivedOn = order.ReceivedOn;
            entity.PaymentMode = order.PaymentMode;
            entity.TransactionNo = order.TransactionNo;
            entity.Discount = order.Discount;
            entity.Remarks = order.Remarks;

            db.LabOrderReports.RemoveRange(existing.Reports);
            entity.Reports.Clear();
        }

        decimal total = 0;
        foreach (var line in lines)
        {
            total += line.Price;
            db.LabOrderReports.Add(new LabOrderReport
            {
                OrderId = entity.Id, ReportId = line.ReportId, ReportName = line.ReportName, Price = line.Price, Amount = line.Price
            });
        }

        entity.TotalAmount = total;
        entity.FinalAmount = Math.Max(0, total - entity.Discount);

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return entity;
    }

    public async Task<LabOrder?> GetOrderAsync(Guid orderId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.LabOrders.AsNoTracking()
            .Include(o => o.Reports).ThenInclude(r => r.Results)
            .FirstOrDefaultAsync(o => o.Id == orderId);
    }

    public async Task<List<LabOrder>> GetOrdersByPatientAsync(Guid patientId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.LabOrders.AsNoTracking()
            .Include(o => o.Reports).ThenInclude(r => r.Results)
            .Where(o => o.PatientId == patientId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();
    }

    public async Task<List<LabOrder>> SearchOrdersAsync(DateTime from, DateTime to)
    {
        await using var db = await factory.CreateDbContextAsync();
        var toExclusive = to.Date.AddDays(1);

        return await db.LabOrders.AsNoTracking()
            .Include(o => o.Reports).ThenInclude(r => r.Results)
            .Where(o => o.OrderDate >= from.Date && o.OrderDate < toExclusive)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();
    }

    public async Task UpdateOrderStatusAsync(Guid orderId, LabOrderStatus status)
    {
        await using var db = await factory.CreateDbContextAsync();
        var order = await db.LabOrders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return;

        order.Status = status;
        await db.SaveChangesAsync();
    }

    // ── Result entry ───────────────────────────────────────────────────────

    /// <summary>
    /// Records or updates one analyte's result within an ordered panel.
    /// Computes <see cref="LabResult.Flag"/> automatically for a numeric
    /// result against the patient's matched reference range; a qualitative
    /// result's flag is left as given, since "Reactive" has no numeric
    /// range to compare against.
    /// </summary>
    public async Task<LabResult> SaveResultAsync(
        Guid orderReportId, Guid? analyteId, string analyteName, string units,
        string resultValue, LabResultType resultType, string? enteredBy)
    {
        await using var db = await factory.CreateDbContextAsync();

        var orderReport = await db.LabOrderReports.Include(r => r.Order)
            .FirstOrDefaultAsync(r => r.Id == orderReportId);
        if (orderReport is null) throw new InvalidOperationException("That ordered report no longer exists.");

        string rangeDisplay = "";
        var flag = LabResultFlag.Normal;

        if (analyteId is { } id)
        {
            var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == orderReport.Order.PatientId);
            if (patient is not null)
            {
                var ageYears = patient.Age;
                var range = await MatchReferenceRangeAsync(id, patient.Gender, ageYears);

                if (range is not null)
                {
                    if (!string.IsNullOrWhiteSpace(range.TextRange))
                    {
                        rangeDisplay = range.TextRange;
                    }
                    else if (range.LowValue is not null && range.HighValue is not null)
                    {
                        // A plain hyphen, not an en dash. The desktop drew
                        // this with WPF and the system font; the web edition
                        // renders it into a PDF whose embedded font subset
                        // has no U+2013, so an en dash printed as a
                        // replacement character on the report itself — the
                        // same reason every document here writes "Rs."
                        // rather than the rupee sign.
                        rangeDisplay = $"{range.LowValue} - {range.HighValue}";

                        if (resultType == LabResultType.Numeric && decimal.TryParse(resultValue, out var numeric))
                        {
                            flag = numeric < range.LowValue ? LabResultFlag.Low
                                 : numeric > range.HighValue ? LabResultFlag.High
                                 : LabResultFlag.Normal;
                        }
                    }
                }
            }
        }

        var existing = await db.LabResults.FirstOrDefaultAsync(r =>
            r.OrderReportId == orderReportId && r.AnalyteId == analyteId && analyteId != null);

        LabResult entity;
        if (existing is null)
        {
            entity = new LabResult
            {
                OrderReportId = orderReportId, AnalyteId = analyteId, AnalyteName = analyteName, Units = units
            };
            db.LabResults.Add(entity);
        }
        else
        {
            entity = existing;
        }

        entity.ResultValue = resultValue;
        entity.ReferenceRangeDisplay = rangeDisplay;
        entity.Flag = flag;
        entity.EnteredOn = clock.Now;
        entity.EnteredBy = enteredBy;

        await db.SaveChangesAsync();
        return entity;
    }

    /// <summary>
    /// Verifies every result on an order and moves it to
    /// <see cref="LabOrderStatus.Verified"/> — a report does not print
    /// until this has happened.
    /// </summary>
    public async Task VerifyOrderAsync(Guid orderId, string verifiedBy)
    {
        await using var db = await factory.CreateDbContextAsync();

        var order = await db.LabOrders.Include(o => o.Reports).ThenInclude(r => r.Results)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return;

        var results = order.Reports.SelectMany(r => r.Results).ToList();
        if (results.Count == 0)
            throw new InvalidOperationException("Enter at least one result before verifying this order.");

        var now = clock.Now;
        foreach (var result in results)
        {
            result.VerifiedOn = now;
            result.VerifiedBy = verifiedBy;
        }

        order.Status = LabOrderStatus.Verified;
        await db.SaveChangesAsync();
    }
}
