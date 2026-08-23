using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

public record LabOrderLineRequest(Guid? ReportId, string ReportName, decimal Price);

/// <summary>
/// Everything the order builder writes. Absent by design, as everywhere
/// else: OrderNo, Status and the totals.
///
/// `Discount` **is** accepted, because applying a package is expressed as a
/// discount rather than by rewriting each report's price — the printed
/// report still has to show what each report is individually worth, and
/// each report's own analytes still have to be known for result entry, so a
/// package cannot collapse into one opaque line.
/// </summary>
public record SaveLabOrderRequest(
    Guid? Id, Guid PatientId, Guid? PackageId, Guid? VisitId, string? ReferredBy,
    string? SpecimenId, decimal Discount, PaymentMode PaymentMode, string? TransactionNo,
    string? Remarks, List<LabOrderLineRequest> Lines);

public record SetLabOrderStatusRequest(LabOrderStatus Status);

/// <summary>One analyte's entered value. A blank value is skipped rather
/// than stored — a half-run panel is normal.</summary>
public record LabResultRequest(
    Guid OrderReportId, Guid? AnalyteId, string AnalyteName, string Units,
    string ResultValue, LabResultType ResultType);

public record SaveLabResultsRequest(List<LabResultRequest> Results);

/// <summary>
/// One measurable quantity — haemoglobin, TSH. <paramref name="DecimalPlaces"/>
/// is how the value prints: haemoglobin to one place, a differential count to
/// none. Getting it wrong makes a report look wrong to a clinician even when
/// the number itself is right.
/// </summary>
public record SaveLabAnalyteRequest(
    Guid? Id, string Name, string? Category, string? Units,
    int DecimalPlaces, int SequenceOrder, bool Active);

/// <summary>
/// One reference range for an analyte. Every bound is optional because real
/// ranges are ragged: some apply to one sex, some only above an age, and some
/// are not numeric at all — <paramref name="TextRange"/> ("Non-reactive")
/// takes over the printed range column for those.
///
/// <paramref name="Label"/> is what a human reads on the report — "Adult
/// male", "Child 1–5y" — and is worth setting even when the bounds look
/// self-explanatory, because the bounds are never printed alone.
/// </summary>
public record SaveReferenceRangeRequest(
    Guid? Id, Gender? Gender, decimal? MinAgeYears, decimal? MaxAgeYears,
    decimal? LowValue, decimal? HighValue, string? TextRange, string? Label);

/// <summary>
/// A report is a panel of analytes at a price — "Complete Blood Count" is one
/// report holding a dozen. <paramref name="AnalyteIds"/> is ordered, and that
/// order is the order results are entered and printed in, which is why it is
/// a list rather than a set.
/// </summary>
public record SaveLabReportRequest(
    Guid? Id, string Name, string? Category, decimal Price, int SequenceOrder,
    bool Active, List<Guid> AnalyteIds);

/// <summary>A bundle of reports at one price — a health-check package. As
/// with a dental package the price is quoted rather than summed, because the
/// discount is the point of having one.</summary>
public record SaveLabPackageRequest(
    Guid? Id, string Name, decimal PackagePrice, bool Active, List<Guid> ReportIds);

/// <summary>
/// The Pathology Lab module: orders billed against reports or a package,
/// per-analyte result entry with server-matched reference ranges, and the
/// verification a report cannot print without.
///
/// The three lab masters — analyte, report, package — live here too, since a
/// lab that cannot define its own analytes and reference ranges is not a lab.
/// They are edited from the Masters screen, which composes them alongside the
/// pediatric and dental masters.
/// </summary>
[ApiController]
[Authorize]
[Route("api/lab")]
public class PathologyLabController(
    PathologyLabService lab,
    IDbContextFactory<AppDbContext> factory) : ControllerBase
{
    // ── Masters, read-only ─────────────────────────────────────────────────

    [HttpGet("reports")]
    public async Task<ActionResult<List<LabReport>>> Reports([FromQuery] string? term)
        => Ok(await lab.SearchReportsAsync(term, activeOnly: true));

    [HttpGet("reports/{reportId:guid}/analytes")]
    public async Task<ActionResult<List<LabAnalyte>>> ReportAnalytes(Guid reportId)
        => Ok(await lab.GetReportAnalytesAsync(reportId));

    [HttpGet("analytes")]
    public async Task<ActionResult<List<LabAnalyte>>> Analytes(
        [FromQuery] string? term, [FromQuery] bool activeOnly = false)
        => Ok(await lab.SearchAnalytesAsync(term, activeOnly));

    [HttpGet("analytes/categories")]
    public async Task<ActionResult<List<string>>> AnalyteCategories()
    {
        string[] examples =
            ["Hematology", "Biochemistry", "Urine", "Serology", "Hormones", "Others"];

        var used = await lab.GetAnalyteCategoriesAsync();
        return Ok(examples.Union(used).OrderBy(c => c).ToList());
    }

    [HttpPost("analytes")]
    public async Task<ActionResult<LabAnalyte>> SaveAnalyte(SaveLabAnalyteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Analyte name is required.");

        await using var db = await factory.CreateDbContextAsync();

        var analyte = request.Id is { } id
            ? await db.LabAnalytes.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id)
            : null;

        analyte ??= new LabAnalyte();

        analyte.Name = request.Name.Trim();
        analyte.Category = string.IsNullOrWhiteSpace(request.Category) ? "Others" : request.Category.Trim();
        analyte.Units = (request.Units ?? string.Empty).Trim();

        // Clamped: a negative place count is meaningless, and beyond about
        // four the printed value is noise rather than precision.
        analyte.DecimalPlaces = Math.Clamp(request.DecimalPlaces, 0, 4);
        analyte.SequenceOrder = request.SequenceOrder;
        analyte.Active = request.Active;

        try
        {
            await lab.SaveAnalyteAsync(analyte);
            return Ok(analyte);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("analytes/{id:guid}/remove")]
    public async Task<IActionResult> DeleteAnalyte(Guid id)
    {
        try
        {
            await lab.DeleteAnalyteAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── Reference ranges ───────────────────────────────────────────────────
    // Kept under their analyte rather than as a top-level resource: a range
    // has no meaning apart from the analyte it bounds.

    [HttpGet("analytes/{id:guid}/ranges")]
    public async Task<ActionResult<List<LabAnalyteReferenceRange>>> ReferenceRanges(Guid id)
        => Ok(await lab.GetReferenceRangesAsync(id));

    [HttpPost("analytes/{id:guid}/ranges")]
    public async Task<ActionResult<LabAnalyteReferenceRange>> SaveReferenceRange(
        Guid id, SaveReferenceRangeRequest request)
    {
        await using var db = await factory.CreateDbContextAsync();

        var range = request.Id is { } rangeId
            ? await db.LabAnalyteReferenceRanges.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rangeId)
            : null;

        // A range always belongs to the analyte in the route, never to one
        // named in the body — otherwise editing haemoglobin's range could
        // rewrite TSH's.
        range ??= new LabAnalyteReferenceRange();
        range.AnalyteId = id;

        range.Gender = request.Gender;
        range.MinAgeYears = request.MinAgeYears;
        range.MaxAgeYears = request.MaxAgeYears;
        range.LowValue = request.LowValue;
        range.HighValue = request.HighValue;
        range.TextRange = string.IsNullOrWhiteSpace(request.TextRange) ? null : request.TextRange.Trim();
        range.Label = (request.Label ?? string.Empty).Trim();

        try
        {
            await lab.SaveReferenceRangeAsync(range);
            return Ok(range);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("ranges/{rangeId:guid}/remove")]
    public async Task<IActionResult> DeleteReferenceRange(Guid rangeId)
    {
        await lab.DeleteReferenceRangeAsync(rangeId);
        return NoContent();
    }

    [HttpPost("reports")]
    public async Task<ActionResult<LabReport>> SaveReport(SaveLabReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Report name is required.");

        await using var db = await factory.CreateDbContextAsync();

        var report = request.Id is { } id
            ? await db.LabReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id)
            : null;

        report ??= new LabReport();

        report.Name = request.Name.Trim();
        report.Category = string.IsNullOrWhiteSpace(request.Category) ? "Others" : request.Category.Trim();
        report.Price = request.Price;
        report.SequenceOrder = request.SequenceOrder;
        report.Active = request.Active;

        // Distinct, order preserved: the same analyte twice in one panel
        // would print twice and be entered twice.
        var analyteIds = (request.AnalyteIds ?? []).Distinct().ToList();

        try
        {
            return Ok(await lab.SaveReportAsync(report, analyteIds));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("reports/{id:guid}/remove")]
    public async Task<IActionResult> DeleteReport(Guid id)
    {
        try
        {
            await lab.DeleteReportAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("packages")]
    public async Task<ActionResult<LabPackageMaster>> SavePackage(SaveLabPackageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Package name is required.");

        await using var db = await factory.CreateDbContextAsync();

        var package = request.Id is { } id
            ? await db.LabPackageMasters.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id)
            : null;

        package ??= new LabPackageMaster();

        package.Name = request.Name.Trim();
        package.PackagePrice = request.PackagePrice;
        package.Active = request.Active;

        var reportIds = (request.ReportIds ?? []).Distinct().ToList();

        try
        {
            return Ok(await lab.SavePackageAsync(package, reportIds));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("packages/{id:guid}/remove")]
    public async Task<IActionResult> DeletePackage(Guid id)
    {
        try
        {
            await lab.DeletePackageAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("packages")]
    public async Task<ActionResult<List<LabPackageMaster>>> Packages([FromQuery] string? term)
        => Ok(await lab.SearchPackagesAsync(term, activeOnly: true));

    [HttpGet("packages/{packageId:guid}/reports")]
    public async Task<ActionResult<List<LabReport>>> PackageReports(Guid packageId)
        => Ok(await lab.GetPackageReportsAsync(packageId));

    // ── Orders ─────────────────────────────────────────────────────────────

    [HttpGet("orders/by-patient/{patientId:guid}")]
    public async Task<ActionResult<List<LabOrder>>> OrdersByPatient(Guid patientId)
        => Ok(await lab.GetOrdersByPatientAsync(patientId));

    [HttpGet("orders/{id:guid}")]
    public async Task<ActionResult<LabOrder>> Order(Guid id)
    {
        var order = await lab.GetOrderAsync(id);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpGet("orders")]
    public async Task<ActionResult<List<LabOrder>>> Orders(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await lab.SearchOrdersAsync(from ?? DateTime.Today, to ?? DateTime.Today));

    [HttpPost("orders")]
    public async Task<ActionResult<LabOrder>> SaveOrder(SaveLabOrderRequest request)
    {
        if (request.Lines.Count == 0)
            return BadRequest("Add at least one report, or apply a package.");

        await using var db = await factory.CreateDbContextAsync();

        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PatientId);
        if (patient is null) return BadRequest("Select a patient first.");

        try
        {
            return Ok(await lab.SaveOrderAsync(new LabOrder
            {
                Id = request.Id ?? Guid.Empty,
                PatientId = patient.Id,
                PatientName = patient.Name,
                PatientNo = patient.PatientNo,
                PackageId = request.PackageId,
                VisitId = request.VisitId,
                ReferredBy = request.VisitId is null ? NullIfBlank(request.ReferredBy) : null,
                SpecimenId = NullIfBlank(request.SpecimenId),
                Discount = request.Discount,
                PaymentMode = request.PaymentMode,
                TransactionNo = NullIfBlank(request.TransactionNo),
                Remarks = NullIfBlank(request.Remarks)
            },
            request.Lines
                .Select(l => new LabOrderLine { ReportId = l.ReportId, ReportName = l.ReportName, Price = l.Price })
                .ToList()));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("orders/{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, SetLabOrderStatusRequest request)
    {
        await lab.UpdateOrderStatusAsync(id, request.Status);
        return NoContent();
    }

    // ── Results ────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves every entered result on the order in one call.
    ///
    /// Blank values are dropped here rather than written — a panel is often
    /// half-run, and an empty string stored as a result would print as one.
    /// The reference range and the Low/Normal/High flag are **not** accepted
    /// from the client: the service matches the range against this patient's
    /// own gender and age and computes the flag from it. The range for a
    /// six-year-old girl is not the range for a grown man, and that is not a
    /// decision a browser gets to make.
    /// </summary>
    [HttpPost("orders/{id:guid}/results")]
    public async Task<ActionResult<int>> SaveResults(Guid id, SaveLabResultsRequest request)
    {
        var entered = request.Results
            .Where(r => !string.IsNullOrWhiteSpace(r.ResultValue))
            .ToList();

        if (entered.Count == 0)
            return BadRequest("Enter at least one result before saving.");

        var by = User.Identity?.Name;

        try
        {
            foreach (var result in entered)
                await lab.SaveResultAsync(
                    result.OrderReportId, result.AnalyteId, result.AnalyteName, result.Units,
                    result.ResultValue.Trim(), result.ResultType, by);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        // Entering results moves the order along — but never backwards. An
        // order already Verified or Completed stays where it is; a
        // correction to one result must not un-verify the rest.
        var order = await lab.GetOrderAsync(id);
        if (order is { Status: LabOrderStatus.Ordered or LabOrderStatus.SampleCollected })
            await lab.UpdateOrderStatusAsync(id, LabOrderStatus.ResultEntered);

        return Ok(entered.Count);
    }

    /// <summary>
    /// Verifies every result and moves the order to Verified. Refused with
    /// nothing entered — verifying an empty order is the one action that
    /// would let a blank report print.
    /// </summary>
    [HttpPost("orders/{id:guid}/verify")]
    public async Task<IActionResult> Verify(Guid id)
    {
        try
        {
            await lab.VerifyOrderAsync(id, User.Identity?.Name ?? "Staff");
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
