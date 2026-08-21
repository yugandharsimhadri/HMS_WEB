using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

public record BookVisitRequest(Guid PatientId, Guid DoctorId, DateTime ScheduledOn, string? Complaint, decimal Fee);
public record CollectFeeRequest(PaymentMode Mode, decimal? Amount, string? TransactionNo);
public record SetStatusRequest(VisitStatus Status);

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
}
