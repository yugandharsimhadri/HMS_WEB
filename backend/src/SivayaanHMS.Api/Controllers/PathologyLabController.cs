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
/// The Pathology Lab module: orders billed against reports or a package,
/// per-analyte result entry with server-matched reference ranges, and the
/// verification a report cannot print without.
///
/// The three lab masters — analyte, report, package — belong to the Masters
/// module; this exposes reads of them so an order can be built.
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
