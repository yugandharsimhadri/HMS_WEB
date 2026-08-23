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

/// <summary>One procedure inside a package, and how many times it is
/// included — a package with two cleanings is one line of quantity 2, not
/// two lines.</summary>
public record DentalPackageItemRequest(Guid? ProcedureId, string ProcedureName, int Quantity);

/// <summary>
/// A package quotes a course of treatment at one price. That price is set
/// here directly rather than summed from the items, because quoting a
/// discount against the à-la-carte total is the whole point of offering a
/// package — the items say what is included, not what it costs.
/// </summary>
public record SaveDentalPackageRequest(
    Guid? Id, string Name, string? Description, decimal PackagePrice, bool Active,
    List<DentalPackageItemRequest> Items);

/// <summary>A crown, a bridge, an implant — priced per unit, because one
/// case may need several of the same thing.</summary>
public record SaveDentalReplacementRequest(
    Guid? Id, string Name, string? Category, decimal UnitCost, bool Active);

/// <summary><paramref name="DefaultCost"/> is a default, not a fixed price:
/// a sitting may override it, since how much anaesthetic a patient actually
/// needed is not knowable when the master is written.</summary>
public record SaveAnesthesiaTypeRequest(Guid? Id, string Name, decimal DefaultCost, bool Active);

/// <summary>
/// The Dentist module: cases, the sittings and replacements that accumulate
/// against them, and the instalments paid off them.
///
/// The unit here is the **case**, not a bill — dentistry runs across several
/// visits and is paid in parts, so cost and payment both hang off the case
/// and the balance is derived, never stored.
///
/// The dental masters live here too — packages, replacements and anaesthesia
/// types — since a clinic that cannot price its own crowns is stuck with
/// whatever the seeder guessed. They are edited from the Masters screen,
/// which composes them alongside the pediatric and lab masters.
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

    /// <summary>What a package includes, for the editor to load — the list
    /// screen shows only the package and its price.</summary>
    [HttpGet("packages/{id:guid}/items")]
    public async Task<ActionResult<List<DentalPackageItem>>> PackageItems(Guid id)
        => Ok(await dentist.GetPackageItemsAsync(id));

    [HttpPost("packages")]
    public async Task<ActionResult<DentalPackageMaster>> SavePackage(SaveDentalPackageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Package name is required.");

        await using var db = await factory.CreateDbContextAsync();

        var package = request.Id is { } id
            ? await db.DentalPackageMasters.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id)
            : null;

        package ??= new DentalPackageMaster();

        package.Name = request.Name.Trim();
        package.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        package.PackagePrice = request.PackagePrice;
        package.Active = request.Active;

        // Quantity clamped to at least 1: a package line included zero times
        // is not a line, and would price the package against nothing.
        var items = (request.Items ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i.ProcedureName))
            .Select(i => new DentalPackageItem
            {
                ProcedureId = i.ProcedureId,
                ProcedureName = i.ProcedureName.Trim(),
                Quantity = Math.Max(1, i.Quantity),
            })
            .ToList();

        try
        {
            return Ok(await dentist.SavePackageAsync(package, items));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Refused once a case has been opened against it — that case
    /// quotes this package by name and price, and deleting the row is what
    /// would strip it of what was agreed. Deactivate instead.</summary>
    [HttpPost("packages/{id:guid}/remove")]
    public async Task<IActionResult> DeletePackage(Guid id)
    {
        try
        {
            await dentist.DeletePackageAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("replacements")]
    public async Task<ActionResult<DentalReplacementMaster>> SaveReplacement(SaveDentalReplacementRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Replacement name is required.");

        await using var db = await factory.CreateDbContextAsync();

        var replacement = request.Id is { } id
            ? await db.DentalReplacementMasters.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id)
            : null;

        replacement ??= new DentalReplacementMaster();

        replacement.Name = request.Name.Trim();
        replacement.Category = string.IsNullOrWhiteSpace(request.Category) ? "Others" : request.Category.Trim();
        replacement.UnitCost = request.UnitCost;
        replacement.Active = request.Active;

        try
        {
            await dentist.SaveReplacementAsync(replacement);
            return Ok(replacement);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("replacements/{id:guid}/remove")]
    public async Task<IActionResult> DeleteReplacement(Guid id)
    {
        try
        {
            await dentist.DeleteReplacementAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// No delete, deliberately — matching the desktop. An anaesthesia type is
    /// referenced by every sitting that used it, and unlike a package there is
    /// no natural "has this been used" guard that is cheap to ask. Deactivating
    /// takes it out of the picker, which is the actual requirement.
    /// </summary>
    [HttpPost("anesthesia-types")]
    public async Task<ActionResult<AnesthesiaTypeMaster>> SaveAnesthesiaType(SaveAnesthesiaTypeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Anesthesia type name is required.");

        await using var db = await factory.CreateDbContextAsync();

        var type = request.Id is { } id
            ? await db.AnesthesiaTypeMasters.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id)
            : null;

        type ??= new AnesthesiaTypeMaster();

        type.Name = request.Name.Trim();
        type.DefaultCost = request.DefaultCost;
        type.Active = request.Active;

        try
        {
            await dentist.SaveAnesthesiaTypeAsync(type);
            return Ok(type);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

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
