using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

/// <summary>
/// What the test editor may actually set. Not the <see cref="DiagnosticTest"/>
/// entity — the same reasoning as <see cref="SavePatientRequest"/>.
/// </summary>
public record SaveDiagnosticTestRequest(
    Guid? Id, string Name, string? Category, decimal Price, bool Active);

public record SetTestActiveRequest(bool Active);

/// <summary>One line as the bill screen has it. `TestId` is nullable because
/// a test requested as free text during a consultation — one we do not run
/// in-house — still bills on its own name.</summary>
public record DiagnosticBillLineRequest(Guid? TestId, string TestName, decimal Price, int Quantity);

/// <summary>
/// Everything the bill screen writes in one go. The lines replace whatever
/// was there rather than merging, matching the service.
///
/// Note what is absent: BillNo, Status, TotalAmount and FinalAmount. The
/// number and status are the server's to allocate and move, and the totals
/// are recomputed from the lines — a bill whose total came from the client
/// is a bill the client can understate.
/// </summary>
public record SaveDiagnosticBillRequest(
    Guid? Id, Guid PatientId, PaymentMode PaymentMode, string? TransactionNo,
    decimal Discount, string? Remarks, Guid? VisitId, string? ReferredBy,
    List<DiagnosticBillLineRequest> Lines);

public record SetBillStatusRequest(DiagnosticBillStatus Status);

/// <summary>
/// The Diagnostics module: the test master billing draws from, and the bills
/// themselves. Thin — <see cref="DiagnosticsService"/> already holds every
/// rule (the never-billed delete guard, the Completed edit refusal, the
/// server-side totals).
/// </summary>
[ApiController]
[Authorize]
[Route("api/diagnostics")]
public class DiagnosticsController(
    DiagnosticsService diagnostics,
    IDbContextFactory<AppDbContext> factory) : ControllerBase
{
    // ── Test master ────────────────────────────────────────────────────────

    /// <summary>
    /// The master search returns inactive tests too; the picker passes
    /// <c>activeOnly</c>. The master is where a retired test is found again
    /// to bring it back, so hiding it there would make that impossible.
    /// </summary>
    [HttpGet("tests")]
    public async Task<ActionResult<List<DiagnosticTest>>> Tests(
        [FromQuery] string? term, [FromQuery] bool activeOnly = false)
        => Ok(await diagnostics.SearchTestsAsync(term, activeOnly));

    /// <summary>
    /// The categories already in use, unioned with a seeded set of examples.
    /// A closed list would not survive one clinic's own vocabulary; a blank
    /// box invites eight spellings of "Hematology".
    /// </summary>
    [HttpGet("tests/categories")]
    public async Task<ActionResult<List<string>>> Categories()
    {
        string[] examples =
            ["Hematology", "Biochemistry", "Urine", "Stool", "Serology", "Thyroid", "Vitamins", "Others"];

        var used = await diagnostics.GetCategoriesAsync();
        return Ok(examples.Union(used).OrderBy(c => c).ToList());
    }

    [HttpPost("tests")]
    public async Task<ActionResult<DiagnosticTest>> SaveTest(SaveDiagnosticTestRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Test name is required.");

        await using var db = await factory.CreateDbContextAsync();

        var test = request.Id is { } id
            ? await db.DiagnosticTests.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id)
            : null;

        test ??= new DiagnosticTest();

        test.Name = request.Name.Trim();
        // Blank falls back rather than being refused — a test nobody has
        // categorised is still a test, and "Others" is where it belongs.
        test.Category = string.IsNullOrWhiteSpace(request.Category) ? "Others" : request.Category.Trim();
        test.Price = request.Price;
        test.Active = request.Active;

        try
        {
            await diagnostics.SaveTestAsync(test);
            return Ok(test);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("tests/{id:guid}/active")]
    public async Task<IActionResult> SetTestActive(Guid id, SetTestActiveRequest request)
    {
        await diagnostics.SetActiveAsync(id, request.Active);
        return NoContent();
    }

    /// <summary>
    /// Refused once the test has ever been billed. The bill keeps its own
    /// denormalised name and price either way, but deleting the master row
    /// is still the one action that strips a historic line of the test it
    /// points at. Deactivating is the supported alternative.
    /// </summary>
    [HttpPost("tests/{id:guid}/remove")]
    public async Task<IActionResult> DeleteTest(Guid id)
    {
        try
        {
            await diagnostics.DeleteTestAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── Tests requested during a consultation ──────────────────────────────

    /// <summary>Visits that requested tests and have not been billed yet —
    /// the diagnostics equivalent of the counter's "load prescription".</summary>
    [HttpGet("pending-requests")]
    public async Task<ActionResult<List<Visit>>> PendingRequests([FromQuery] DateTime? date)
        => Ok(await diagnostics.GetVisitsWithPendingRequestsAsync(date ?? DateTime.Today));

    // ── Bills ──────────────────────────────────────────────────────────────

    [HttpGet("bills/{id:guid}")]
    public async Task<ActionResult<DiagnosticBill>> Bill(Guid id)
    {
        var bill = await diagnostics.GetBillAsync(id);
        return bill is null ? NotFound() : Ok(bill);
    }

    [HttpGet("bills")]
    public async Task<ActionResult<List<DiagnosticBill>>> Bills(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await diagnostics.SearchBillsAsync(from ?? DateTime.Today, to ?? DateTime.Today));

    [HttpGet("bills/by-patient/{patientId:guid}")]
    public async Task<ActionResult<List<DiagnosticBill>>> BillsByPatient(Guid patientId)
        => Ok(await diagnostics.GetBillsByPatientAsync(patientId));

    [HttpPost("bills")]
    public async Task<ActionResult<DiagnosticBill>> SaveBill(SaveDiagnosticBillRequest request)
    {
        if (request.Lines.Count == 0)
            return BadRequest("Add at least one test to the bill.");

        await using var db = await factory.CreateDbContextAsync();

        // The patient's name and number are read, not accepted — they are
        // denormalised onto the bill and printed on it. The lookup is also a
        // tenant-scoped existence check.
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PatientId);
        if (patient is null) return BadRequest("Select a patient first.");

        var bill = new DiagnosticBill
        {
            Id = request.Id ?? Guid.Empty,
            PatientId = patient.Id,
            PatientName = patient.Name,
            PatientNo = patient.PatientNo,
            PaymentMode = request.PaymentMode,
            TransactionNo = NullIfBlank(request.TransactionNo),
            Discount = request.Discount,
            Remarks = NullIfBlank(request.Remarks),
            VisitId = request.VisitId,
            // A patient who came through our own OPD was referred by the
            // clinic itself; the field only means anything for one who did not.
            ReferredBy = request.VisitId is null ? NullIfBlank(request.ReferredBy) : null
        };

        var lines = request.Lines
            .Select(l => new DiagnosticBillLine
            {
                TestId = l.TestId,
                TestName = l.TestName,
                Price = l.Price,
                Quantity = l.Quantity
            })
            .ToList();

        try
        {
            return Ok(await diagnostics.SaveBillAsync(bill, lines));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("bills/{id:guid}/status")]
    public async Task<IActionResult> SetBillStatus(Guid id, SetBillStatusRequest request)
    {
        await diagnostics.UpdateStatusAsync(id, request.Status);
        return NoContent();
    }

    [HttpPost("bills/{id:guid}/remove")]
    public async Task<IActionResult> DeleteBill(Guid id)
    {
        try
        {
            await diagnostics.DeleteBillAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
