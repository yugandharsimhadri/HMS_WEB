using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

/// <summary>One line as the Care screen has it. `ProcedureId` is nullable
/// because a vaccine line has no `Procedure` behind it — it bills on the
/// vaccine's own name.</summary>
public record ProcedureBillLineRequest(Guid? ProcedureId, string ProcedureName, decimal Price, int Quantity);

/// <summary>
/// A dose to record once the bill saves. Carried alongside the lines rather
/// than posted separately: the desktop keeps each dose as a draft on the
/// cart and writes it only after the bill it rode in on has actually saved,
/// so a dose is on record only once it is on a saved bill. Splitting this
/// into a second request would reintroduce exactly the window that design
/// closes — a recorded dose with no bill, or a bill with no dose.
/// </summary>
public record VaccinationDraftRequest(
    Guid VaccineId, DateTime GivenOn, string? Site, string? AdministeredBy,
    Guid ProductId, Guid BatchId);

/// <summary>
/// Everything the Care screen writes in one go: the bill, and any doses
/// riding on it.
///
/// Absent by design, as in Diagnostics: BillNo, Status and the totals. The
/// prices on the lines are accepted (the desk really can concede a rate),
/// but what they add up to is the server's arithmetic.
/// </summary>
public record SaveProcedureBillRequest(
    Guid? Id, Guid PatientId, PaymentMode PaymentMode, string? TransactionNo,
    decimal Discount, Guid? VisitId, string? ReferredBy,
    List<ProcedureBillLineRequest> Lines,
    List<VaccinationDraftRequest> Vaccinations);

public record SaveProcedureRequest(
    Guid? Id, string Name, string? Category, ProcedureDepartment Department, decimal Price, bool Active);

public record RecordGrowthRequest(
    DateTime MeasuredOn, decimal? WeightKg, decimal? HeightCm, decimal? HeadCircumferenceCm);

public record SetProcedureBillStatusRequest(ProcedureBillStatus Status);

/// <summary>
/// What saving produced, so the screen can report the bill and how many
/// doses actually went on record.
///
/// <paramref name="Warning"/> is set when the bill saved but a dose did not
/// record — the batch emptied between picking the brand and saving, say.
/// That is not a failure of the bill, which is a financial fact already
/// written, so it cannot be a non-2xx; but it must not pass silently
/// either, because the parent's card would then be missing a dose the bill
/// charged for.
/// </summary>
public record ProcedureBillResult(
    Guid Id, string BillNo, decimal FinalAmount, int VaccinationsRecorded, string? Warning = null);

/// <summary>
/// The Pediatrics module: growth, vaccination and procedure billing for one
/// child at a time.
///
/// Vaccine Master and Procedure Master are configuration and belong to the
/// Masters module — the procedure endpoints here exist because billing
/// needs them and Masters will reuse them, not because this screen manages
/// them.
/// </summary>
[ApiController]
[Authorize]
[Route("api/pediatrics")]
public class PediatricsController(
    PediatricsService pediatrics,
    ProcedureBillsService bills,
    IDbContextFactory<AppDbContext> factory) : ControllerBase
{
    // ── Vaccine master (read-only here; Masters owns the editing) ──────────

    [HttpGet("vaccines")]
    public async Task<ActionResult<List<VaccineMaster>>> Vaccines(
        [FromQuery] string? term, [FromQuery] bool activeOnly = true)
        => Ok(await pediatrics.SearchVaccinesAsync(term, activeOnly));

    // ── Procedures ─────────────────────────────────────────────────────────

    [HttpGet("procedures")]
    public async Task<ActionResult<List<Procedure>>> Procedures(
        [FromQuery] ProcedureDepartment department = ProcedureDepartment.Pediatrics,
        [FromQuery] string? term = null,
        [FromQuery] bool activeOnly = true)
        => Ok(await bills.SearchProceduresAsync(department, term, activeOnly));

    [HttpPost("procedures")]
    public async Task<ActionResult<Procedure>> SaveProcedure(SaveProcedureRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Procedure name is required.");

        await using var db = await factory.CreateDbContextAsync();

        var procedure = request.Id is { } id
            ? await db.Procedures.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id)
            : null;

        procedure ??= new Procedure();

        procedure.Name = request.Name.Trim();
        procedure.Category = string.IsNullOrWhiteSpace(request.Category) ? "Others" : request.Category.Trim();
        procedure.Department = request.Department;
        procedure.Price = request.Price;
        procedure.Active = request.Active;

        try
        {
            await bills.SaveProcedureAsync(procedure);
            return Ok(procedure);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── Per-patient reads ──────────────────────────────────────────────────

    [HttpGet("patients/{patientId:guid}/vaccinations")]
    public async Task<ActionResult<List<VaccinationRecord>>> VaccinationHistory(Guid patientId)
        => Ok(await pediatrics.GetVaccinationHistoryAsync(patientId));

    [HttpGet("patients/{patientId:guid}/due-vaccines")]
    public async Task<ActionResult<List<DueVaccine>>> DueVaccines(
        Guid patientId, [FromQuery] int leadDays = 14)
        => Ok(await pediatrics.GetDueVaccinesForPatientAsync(patientId, leadDays));

    [HttpGet("patients/{patientId:guid}/immunization-card")]
    public async Task<ActionResult<List<ImmunizationCardRow>>> ImmunizationCard(Guid patientId)
        => Ok(await pediatrics.GetImmunizationCardAsync(patientId));

    [HttpGet("patients/{patientId:guid}/growth")]
    public async Task<ActionResult<List<GrowthMeasurement>>> GrowthHistory(Guid patientId)
        => Ok(await pediatrics.GetGrowthHistoryAsync(patientId));

    /// <summary>Growth is never billed — recording it is its own action,
    /// with nothing riding along.</summary>
    [HttpPost("patients/{patientId:guid}/growth")]
    public async Task<ActionResult<GrowthMeasurement>> RecordGrowth(Guid patientId, RecordGrowthRequest request)
    {
        if (request.WeightKg is null && request.HeightCm is null && request.HeadCircumferenceCm is null)
            return BadRequest("Enter at least one measurement.");

        return Ok(await pediatrics.RecordGrowthAsync(new GrowthMeasurement
        {
            PatientId = patientId,
            MeasuredOn = request.MeasuredOn,
            WeightKg = request.WeightKg,
            HeightCm = request.HeightCm,
            HeadCircumferenceCm = request.HeadCircumferenceCm
        }));
    }

    // ── Bills ──────────────────────────────────────────────────────────────

    [HttpGet("bills/{id:guid}")]
    public async Task<ActionResult<ProcedureBill>> Bill(Guid id)
    {
        var bill = await bills.GetBillAsync(id);
        return bill is null ? NotFound() : Ok(bill);
    }

    [HttpGet("bills")]
    public async Task<ActionResult<List<ProcedureBill>>> Bills(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await bills.SearchBillsAsync(from ?? DateTime.Today, to ?? DateTime.Today));

    /// <summary>
    /// Saves the bill, then records each dose that rode on it.
    ///
    /// Order matters and is the desktop's: the bill first, the doses after.
    /// A dose recorded before the bill saved would survive a refused bill as
    /// a phantom vaccination — and each recording also decrements real
    /// pharmacy stock, which must not move for a bill that never existed.
    /// </summary>
    [HttpPost("bills")]
    public async Task<ActionResult<ProcedureBillResult>> SaveBill(SaveProcedureBillRequest request)
    {
        if (request.Lines.Count == 0)
            return BadRequest("Add at least one procedure to the bill.");

        await using var db = await factory.CreateDbContextAsync();

        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PatientId);
        if (patient is null) return BadRequest("Select a patient first.");

        var bill = new ProcedureBill
        {
            Id = request.Id ?? Guid.Empty,
            PatientId = patient.Id,
            PatientName = patient.Name,
            PatientNo = patient.PatientNo,
            PaymentMode = request.PaymentMode,
            TransactionNo = NullIfBlank(request.TransactionNo),
            Discount = request.Discount,
            VisitId = request.VisitId,
            ReferredBy = request.VisitId is null ? NullIfBlank(request.ReferredBy) : null
        };

        var lines = request.Lines
            .Select(l => new ProcedureBillLine
            {
                ProcedureId = l.ProcedureId,
                ProcedureName = l.ProcedureName,
                Price = l.Price,
                Quantity = l.Quantity
            })
            .ToList();

        ProcedureBill saved;
        try
        {
            saved = await bills.SaveBillAsync(bill, lines);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var recorded = 0;
        foreach (var draft in request.Vaccinations)
        {
            // Read the vaccine and product server-side: the record carries
            // denormalised names that outlive both master rows, and they are
            // printed on the parent's vaccination card.
            var vaccine = await db.VaccineMasters.AsNoTracking().FirstOrDefaultAsync(v => v.Id == draft.VaccineId);
            if (vaccine is null) continue;

            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == draft.ProductId);
            var batch = await db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == draft.BatchId);

            try
            {
                await pediatrics.RecordVaccinationAsync(new VaccinationRecord
                {
                    PatientId = patient.Id,
                    PatientName = patient.Name,
                    VaccineId = vaccine.Id,
                    VaccineName = vaccine.Name,
                    DoseNumber = vaccine.DoseNumber,
                    GivenOn = draft.GivenOn,
                    BatchNo = batch?.BatchNo,
                    SiteOfInjection = NullIfBlank(draft.Site),
                    AdministeredBy = NullIfBlank(draft.AdministeredBy),
                    ProductId = product?.Id,
                    ProductName = product?.Name,
                    Manufacturer = product?.Manufacturer,
                    BatchId = batch?.Id
                });
                recorded++;
            }
            catch (InvalidOperationException ex)
            {
                // The bill is already saved and is a financial fact, so this
                // cannot roll back into a 400. Report it alongside the bill
                // instead and stop — the desk needs to know which dose is
                // missing from the card before giving another.
                return Ok(new ProcedureBillResult(
                    saved.Id, saved.BillNo, saved.FinalAmount, recorded,
                    $"Bill {saved.BillNo} saved, but {vaccine.Name} could not be recorded: {ex.Message}"));
            }
        }

        return Ok(new ProcedureBillResult(saved.Id, saved.BillNo, saved.FinalAmount, recorded));
    }

    [HttpPost("bills/{id:guid}/status")]
    public async Task<IActionResult> SetBillStatus(Guid id, SetProcedureBillStatusRequest request)
    {
        await bills.UpdateStatusAsync(id, request.Status);
        return NoContent();
    }

    [HttpPost("bills/{id:guid}/remove")]
    public async Task<IActionResult> DeleteBill(Guid id)
    {
        try
        {
            await bills.DeleteBillAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
