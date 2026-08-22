using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

/// <summary>
/// What opening a case may set. `ProcedureId` and `PackageId` are mutually
/// exclusive and the service enforces it — a case's base cost has to come
/// from exactly one place.
///
/// Absent by design: `BaseCost`, `ProcedureName`, `PackageName` and
/// `Status`. The cost and names are snapshotted server-side from whichever
/// master the case was opened against, so a client cannot open a case at a
/// price the clinic never charged.
/// </summary>
public record OpenDentalCaseRequest(
    Guid PatientId, Guid DoctorId, Guid? ProcedureId, Guid? PackageId,
    string? ToothNumber, string? Notes);

public record SetDentalCaseStatusRequest(DentalCaseStatus Status);

public record AddSittingRequest(
    string? WorkDone, Guid? AnesthesiaTypeId, decimal? AnesthesiaCost, DateTime? NextSittingOn);

public record AddReplacementRequest(Guid ReplacementId, int Quantity);

public record RecordDentalPaymentRequest(decimal Amount, PaymentMode PaymentMode, string? TransactionNo);

/// <summary>
/// The Dentist module: cases, the sittings and replacements that accumulate
/// against them, and the instalments paid off them.
///
/// The unit here is the **case**, not a bill — dentistry runs across several
/// visits and is paid in parts, so cost and payment both hang off the case
/// and the balance is derived, never stored.
///
/// The four dental masters belong to the Masters module; this exposes reads
/// of them so a case can be opened, not editors.
/// </summary>
[ApiController]
[Authorize]
[Route("api/dentist")]
public class DentistController(
    DentistService dentist,
    ProcedureBillsService bills,
    IDbContextFactory<AppDbContext> factory) : ControllerBase
{
    // ── Masters, read-only ─────────────────────────────────────────────────

    [HttpGet("procedures")]
    public async Task<ActionResult<List<Procedure>>> Procedures([FromQuery] string? term)
        => Ok(await bills.SearchProceduresAsync(ProcedureDepartment.Dentist, term, activeOnly: true));

    [HttpGet("packages")]
    public async Task<ActionResult<List<DentalPackageMaster>>> Packages([FromQuery] string? term)
        => Ok(await dentist.SearchPackagesAsync(term, activeOnly: true));

    [HttpGet("replacements")]
    public async Task<ActionResult<List<DentalReplacementMaster>>> Replacements([FromQuery] string? term)
        => Ok(await dentist.SearchReplacementsAsync(term, activeOnly: true));

    [HttpGet("anesthesia-types")]
    public async Task<ActionResult<List<AnesthesiaTypeMaster>>> AnesthesiaTypes()
        => Ok(await dentist.SearchAnesthesiaTypesAsync(activeOnly: true));

    // ── Cases ──────────────────────────────────────────────────────────────

    [HttpGet("cases/by-patient/{patientId:guid}")]
    public async Task<ActionResult<List<DentalCase>>> CasesByPatient(Guid patientId)
        => Ok(await dentist.GetCasesByPatientAsync(patientId));

    [HttpGet("cases/open")]
    public async Task<ActionResult<List<DentalCase>>> OpenCases()
        => Ok(await dentist.GetOpenCasesAsync());

    [HttpGet("cases/{id:guid}")]
    public async Task<ActionResult<DentalCase>> Case(Guid id)
    {
        var dentalCase = await dentist.GetCaseAsync(id);
        return dentalCase is null ? NotFound() : Ok(dentalCase);
    }

    [HttpPost("cases")]
    public async Task<ActionResult<DentalCase>> OpenCase(OpenDentalCaseRequest request)
    {
        await using var db = await factory.CreateDbContextAsync();

        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PatientId);
        if (patient is null) return BadRequest("Select a patient first.");

        try
        {
            return Ok(await dentist.OpenCaseAsync(new DentalCase
            {
                PatientId = patient.Id,
                PatientName = patient.Name,
                DoctorId = request.DoctorId,
                ProcedureId = request.ProcedureId,
                PackageId = request.PackageId,
                ToothNumber = NullIfBlank(request.ToothNumber),
                Notes = NullIfBlank(request.Notes)
            }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("cases/{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, SetDentalCaseStatusRequest request)
    {
        await dentist.UpdateCaseStatusAsync(id, request.Status);
        return NoContent();
    }

    // ── Sittings ───────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a sitting. The sitting number is allocated server-side as
    /// max+1 within the case — "sitting 2 of 4" has to be stable, not a
    /// number the client guessed and two tabs could both guess the same.
    /// The first sitting also moves the case Planned → InProgress.
    /// </summary>
    [HttpPost("cases/{id:guid}/sittings")]
    public async Task<ActionResult<DentalSitting>> AddSitting(Guid id, AddSittingRequest request)
    {
        await using var db = await factory.CreateDbContextAsync();

        // The anesthesia name is snapshotted onto the sitting, so read it
        // rather than accept it — the master row may be renamed later.
        string? anesthesiaName = null;
        if (request.AnesthesiaTypeId is { } typeId)
            anesthesiaName = (await db.AnesthesiaTypeMasters.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == typeId))?.Name;

        try
        {
            return Ok(await dentist.AddSittingAsync(new DentalSitting
            {
                DentalCaseId = id,
                WorkDone = NullIfBlank(request.WorkDone),
                AnesthesiaTypeId = request.AnesthesiaTypeId,
                AnesthesiaTypeName = anesthesiaName,
                AnesthesiaCost = request.AnesthesiaCost,
                NextSittingOn = request.NextSittingOn
            }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── Replacements ───────────────────────────────────────────────────────

    [HttpPost("cases/{id:guid}/replacements")]
    public async Task<ActionResult<DentalCaseReplacement>> AddReplacement(Guid id, AddReplacementRequest request)
    {
        await using var db = await factory.CreateDbContextAsync();

        // Name and unit cost come from the master, not the request — they
        // are snapshotted onto the case and become part of what it cost.
        var replacement = await db.DentalReplacementMasters.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.ReplacementId);
        if (replacement is null) return BadRequest("Pick a replacement first.");

        try
        {
            return Ok(await dentist.AddReplacementAsync(
                id, replacement.Id, replacement.Name, replacement.UnitCost, request.Quantity));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── Payments ───────────────────────────────────────────────────────────

    /// <summary>
    /// One instalment against the case's running balance — never the whole
    /// bill in one shot. Allowed on a Completed case (a late payment is
    /// still a payment); refused on a Cancelled one.
    /// </summary>
    [HttpPost("cases/{id:guid}/payments")]
    public async Task<ActionResult<DentalPayment>> RecordPayment(Guid id, RecordDentalPaymentRequest request)
    {
        try
        {
            return Ok(await dentist.RecordPaymentAsync(new DentalPayment
            {
                DentalCaseId = id,
                Amount = request.Amount,
                PaymentMode = request.PaymentMode,
                TransactionNo = NullIfBlank(request.TransactionNo)
            }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
