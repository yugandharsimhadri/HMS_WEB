using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SivayaanHMS.Api.Auth;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

public record SaveRoomTypeRequest(Guid? Id, string Name, decimal DailyRent, decimal NursingPerDay, decimal GstPercent, bool Active);
public record SaveBedRequest(Guid? Id, string BedNo, string Ward, Guid RoomTypeId, int SortOrder, bool Active);
public record SetBedStatusRequest(BedStatus Status, string? Note);
public record SaveIpServiceRequest(Guid? Id, string Name, IpChargeCategory Category, decimal Rate, bool Active);
public record TransferRequest(Guid BedId, DateTime? At);
public record ReasonRequest(string Reason);
public record SaveSummaryRequest(
    string? FinalDiagnosis, string? CourseInHospital, string? Treatment, string? ConditionAtDischarge,
    string? Medicines, string? Advice, DateTime? FollowUpOn, string? DoctorName);

/// <summary>Everything the IP file screen shows, in one call.</summary>
public record AdmissionDetail(
    Admission Admission, IpBillView Bill, List<IpBill> Bills, List<IpNote> Notes, DischargeSummary? Summary,
    bool CanDiscount);

/// <summary>
/// The in-patient module. Thin: <see cref="InpatientService"/> holds every rule.
/// A discount and a bill cancellation are the two admin-only actions - both
/// change what the hospital is owed, and both are kept on record with a reason.
/// </summary>
[ApiController]
[Authorize]
[Route("api/inpatient")]
public class InpatientController(InpatientService ip) : ControllerBase
{
    private bool IsAdmin => User.IsInRole(nameof(UserRole.Admin));

    // ── Masters ────────────────────────────────────────────────────────────

    [HttpGet("room-types")]
    public async Task<ActionResult<List<RoomType>>> RoomTypes() => Ok(await ip.GetRoomTypesAsync());

    [HttpPost("room-types")]
    public Task<IActionResult> SaveRoomType(SaveRoomTypeRequest r) => Run(async () => (object)await ip.SaveRoomTypeAsync(new RoomType
    {
        Id = r.Id ?? Guid.Empty, Name = r.Name, DailyRent = r.DailyRent, NursingPerDay = r.NursingPerDay,
        GstPercent = r.GstPercent, Active = r.Active
    }));

    [HttpGet("beds")]
    public async Task<ActionResult<List<Bed>>> Beds() => Ok(await ip.GetBedsAsync());

    [HttpPost("beds")]
    public Task<IActionResult> SaveBed(SaveBedRequest r) => Run(async () => (object)await ip.SaveBedAsync(new Bed
    {
        Id = r.Id ?? Guid.Empty, BedNo = r.BedNo, Ward = r.Ward, RoomTypeId = r.RoomTypeId, SortOrder = r.SortOrder, Active = r.Active
    }));

    [HttpPost("beds/{id:guid}/status")]
    public Task<IActionResult> SetBedStatus(Guid id, SetBedStatusRequest r)
        => Run(async () => { await ip.SetBedStatusAsync(id, r.Status, r.Note); return null; });

    [HttpGet("services")]
    public async Task<ActionResult<List<IpService>>> Services([FromQuery] bool activeOnly = false)
        => Ok(await ip.GetServicesAsync(activeOnly));

    [HttpPost("services")]
    public Task<IActionResult> SaveService(SaveIpServiceRequest r) => Run(async () => (object)await ip.SaveServiceAsync(new IpService
    {
        Id = r.Id ?? Guid.Empty, Name = r.Name, Category = r.Category, Rate = r.Rate, Active = r.Active
    }));

    // ── Board and lists ────────────────────────────────────────────────────

    [HttpGet("board")]
    public async Task<ActionResult<List<BedBoardItem>>> Board() => Ok(await ip.GetBoardAsync());

    [HttpGet("admissions")]
    public async Task<ActionResult<List<AdmissionRow>>> Admissions(
        [FromQuery] AdmissionStatus? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? term)
        => Ok(await ip.SearchAsync(status, from, to, term));

    /// <summary>The patient's current admission, or 204 when they are not in. The counters ask this
    /// to offer "charge to IP bill".</summary>
    [HttpGet("active")]
    public async Task<ActionResult<ActiveAdmission?>> Active([FromQuery] Guid patientId)
        => Ok(await ip.GetActiveAdmissionAsync(patientId));

    /// <summary>Every stay the patient has had, for their history.</summary>
    [HttpGet("by-patient/{patientId:guid}")]
    public async Task<ActionResult<List<PatientAdmission>>> ByPatient(Guid patientId)
        => Ok(await ip.GetAdmissionsByPatientAsync(patientId));

    // ── One admission ──────────────────────────────────────────────────────

    [HttpPost("admissions")]
    public Task<IActionResult> Admit(AdmitRequest r) => Run(async () => (object)new { (await ip.AdmitAsync(r)).Id });

    [HttpGet("admissions/{id:guid}")]
    public async Task<ActionResult<AdmissionDetail>> Get(Guid id, [FromQuery] DateTime? upTo)
    {
        var admission = await ip.GetAdmissionAsync(id);
        if (admission is null) return NotFound("Admission not found.");

        return Ok(new AdmissionDetail(
            admission,
            await ip.GetBillAsync(id, upTo),
            await ip.GetBillsAsync(id),
            await ip.GetNotesAsync(id),
            await ip.GetSummaryAsync(id),
            IsAdmin));
    }

    [HttpPut("admissions/{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateAdmissionRequest r)
        => Run(async () => { await ip.UpdateAsync(id, r); return null; });

    [HttpPost("admissions/{id:guid}/transfer")]
    public Task<IActionResult> Transfer(Guid id, TransferRequest r)
        => Run(async () => { await ip.TransferAsync(id, r.BedId, r.At); return null; });

    [HttpPost("admissions/{id:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid id, ReasonRequest r)
        => Run(async () => { await ip.CancelAdmissionAsync(id, r.Reason); return null; });

    [HttpPost("admissions/{id:guid}/charges")]
    public Task<IActionResult> AddCharge(Guid id, AddChargeRequest r) => Run(async () => (object)await ip.AddChargeAsync(id, r));

    [HttpDelete("admissions/{id:guid}/charges/{chargeId:guid}")]
    public Task<IActionResult> RemoveCharge(Guid id, Guid chargeId)
        => Run(async () => { await ip.RemoveChargeAsync(id, chargeId); return null; });

    [HttpPost("admissions/{id:guid}/payments")]
    public Task<IActionResult> AddPayment(Guid id, AddPaymentRequest r) => Run(async () => (object)await ip.AddPaymentAsync(id, r));

    [HttpGet("admissions/{id:guid}/bill")]
    public Task<IActionResult> Bill(Guid id, [FromQuery] DateTime? upTo) => Run(async () => (object)await ip.GetBillAsync(id, upTo));

    [HttpPost("admissions/{id:guid}/final-bill")]
    public Task<IActionResult> FinalBill(Guid id, FinalBillRequest r)
        => Run(async () => (object)new { (await ip.MakeFinalBillAsync(id, r, IsAdmin)).Id });

    /// <summary>"Delete" a bill: it is kept as cancelled, with the reason, and a new one can be made.</summary>
    [HttpPost("admissions/{id:guid}/bills/{billId:guid}/cancel")]
    [Authorize(Policy = TenantClaimTypes.ClinicAdminPolicy)]
    public Task<IActionResult> CancelBill(Guid id, Guid billId, ReasonRequest r)
        => Run(async () => { await ip.CancelBillAsync(id, billId, r.Reason); return null; });

    [HttpPost("admissions/{id:guid}/bills/{billId:guid}/adjust-to-insurance")]
    public Task<IActionResult> AdjustToInsurance(Guid id, Guid billId)
        => Run(async () => { await ip.AdjustToInsuranceAsync(id, billId); return null; });

    [HttpPost("admissions/{id:guid}/bills/{billId:guid}/clear-adjustment")]
    public Task<IActionResult> ClearAdjustment(Guid id, Guid billId)
        => Run(async () => { await ip.ClearInsuranceAdjustmentAsync(id, billId); return null; });

    [HttpPost("admissions/{id:guid}/discharge")]
    public Task<IActionResult> Discharge(Guid id, DischargeRequest r)
        => Run(async () => { await ip.DischargeAsync(id, r); return null; });

    [HttpPost("admissions/{id:guid}/notes")]
    public Task<IActionResult> AddNote(Guid id, AddNoteRequest r) => Run(async () => (object)await ip.AddNoteAsync(id, r));

    [HttpPut("admissions/{id:guid}/summary")]
    public Task<IActionResult> SaveSummary(Guid id, SaveSummaryRequest r) => Run(async () => (object)await ip.SaveSummaryAsync(id, new DischargeSummary
    {
        FinalDiagnosis = r.FinalDiagnosis, CourseInHospital = r.CourseInHospital, Treatment = r.Treatment,
        ConditionAtDischarge = r.ConditionAtDischarge, Medicines = r.Medicines, Advice = r.Advice,
        FollowUpOn = r.FollowUpOn, DoctorName = r.DoctorName
    }));

    /// <summary>A rule the service refuses is the user's to fix, so it comes back as a 400 with the reason.</summary>
    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        try
        {
            var result = await action();
            return result is null ? NoContent() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
