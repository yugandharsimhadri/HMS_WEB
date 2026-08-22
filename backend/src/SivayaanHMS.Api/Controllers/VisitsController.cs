using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

public record BookVisitRequest(Guid PatientId, Guid DoctorId, DateTime ScheduledOn, string? Complaint, decimal Fee);
public record CollectFeeRequest(PaymentMode Mode, decimal? Amount, string? TransactionNo);
public record SetStatusRequest(VisitStatus Status);

public record PrescriptionLineRequest(
    Guid? ProductId, string MedicineName, string? Dosage, string? Frequency,
    int Days, int Quantity, string? Instructions);

public record DiagnosticRequestLine(Guid? TestId, string TestName);

/// <summary>Everything the consultation screen writes in one go — the
/// clinical notes, the vitals, the fee as the doctor may have revised it,
/// the follow-up, and the two lists (prescription and requested tests),
/// which replace whatever was there rather than merging.</summary>
public record SaveConsultationRequest(
    string? Complaint, string? Diagnosis, string? Notes,
    decimal? WeightKg, string? BloodPressure, decimal? TemperatureF,
    decimal? HeightCm, int? HeartRateBpm, int? Spo2Percent,
    decimal Fee, DateTime? FollowUpOn,
    List<PrescriptionLineRequest> Prescription,
    List<DiagnosticRequestLine> DiagnosticRequests,
    bool Complete);

/// <summary>The OPD queue: today's visits, booking, status changes and fee
/// collection. The consultation itself (SaveConsultationAsync) is deferred
/// to a later pass - this is the front-desk half of the module.</summary>
[ApiController]
[Authorize]
[Route("api/visits")]
public class VisitsController(OpdService opd) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Visit>>> ForDate([FromQuery] DateTime? date)
        => Ok(await opd.GetVisitsAsync(date ?? DateTime.Today));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Visit>> Get(Guid id)
    {
        var visit = await opd.GetVisitAsync(id);
        return visit is null ? NotFound() : Ok(visit);
    }

    [HttpPost]
    public async Task<ActionResult<Visit>> Book(BookVisitRequest request)
    {
        try
        {
            var visit = await opd.BookVisitAsync(
                request.PatientId, request.DoctorId, request.ScheduledOn, request.Complaint, request.Fee);
            return Ok(visit);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, SetStatusRequest request)
    {
        try
        {
            await opd.SetStatusAsync(id, request.Status);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/collect-fee")]
    public async Task<ActionResult<Visit>> CollectFee(Guid id, CollectFeeRequest request)
    {
        var visit = await opd.CollectFeeAsync(id, request.Mode, request.Amount, request.TransactionNo);
        return visit is null ? NotFound() : Ok(visit);
    }

    /// <summary>Saves the consultation. <c>Complete</c> also moves the visit
    /// to Completed, which is what drops its tile out of the waiting column.</summary>
    [HttpPost("{id:guid}/consultation")]
    public async Task<IActionResult> SaveConsultation(Guid id, SaveConsultationRequest request)
    {
        var visit = await opd.GetVisitAsync(id);
        if (visit is null) return NotFound();

        visit.Complaint = Trim(request.Complaint);
        visit.Diagnosis = Trim(request.Diagnosis);
        visit.Notes = Trim(request.Notes);
        visit.WeightKg = request.WeightKg;
        visit.BloodPressure = Trim(request.BloodPressure);
        visit.TemperatureF = request.TemperatureF;
        visit.HeightCm = request.HeightCm;
        visit.HeartRateBpm = request.HeartRateBpm;
        visit.Spo2Percent = request.Spo2Percent;
        visit.Fee = request.Fee;
        visit.FollowUpOn = request.FollowUpOn;

        var items = request.Prescription
            .Where(l => !string.IsNullOrWhiteSpace(l.MedicineName))
            .Select(l => new PrescriptionItem
            {
                ProductId = l.ProductId,
                MedicineName = l.MedicineName.Trim(),
                Dosage = Trim(l.Dosage),
                Frequency = Trim(l.Frequency),
                Days = l.Days,
                Quantity = l.Quantity,
                Instructions = Trim(l.Instructions),
            })
            .ToList();

        var tests = request.DiagnosticRequests
            .Where(t => !string.IsNullOrWhiteSpace(t.TestName))
            .Select(t => new VisitDiagnosticRequest { TestId = t.TestId, TestName = t.TestName.Trim() })
            .ToList();

        try
        {
            await opd.SaveConsultationAsync(visit, items, tests, request.Complete);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Every visit this patient has ever made — the history panel
    /// on the Patients screen, and where a months-old receipt gets
    /// reprinted from.</summary>
    [HttpGet("by-patient/{patientId:guid}")]
    public async Task<ActionResult<List<Visit>>> ByPatient(Guid patientId)
        => Ok(await opd.GetPatientHistoryAsync(patientId));

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
