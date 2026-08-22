using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

/// <summary>
/// What the booking screen may actually set on an appointment.
///
/// Deliberately not the <see cref="Appointment"/> entity — the same reasoning
/// as <see cref="SavePatientRequest"/>. Note what is *not* here: the patient
/// and doctor names, the appointment number, and the status. The desktop's
/// viewmodel copies those off the objects it already has selected; over HTTP
/// they are read from the database instead, so a client cannot book a slot
/// that prints one patient's name against another's record.
/// </summary>
public record BookAppointmentRequest(
    Guid PatientId, Guid DoctorId, DateTime ScheduledOn, int DurationMinutes,
    AppointmentModuleContext ModuleContext, string? Reason, string? Notes);

public record CancelAppointmentRequest(string Reason);

/// <summary>A time is always sent; the screen falls back to the original
/// appointment's time when the user leaves the field blank, so the decision
/// is made where the original is on screen rather than here.</summary>
public record RescheduleAppointmentRequest(DateTime ScheduledOn, int? DurationMinutes);

public record ToggleReminderRequest(
    ReminderSourceKind SourceKind, Guid SourceId, Guid PatientId, DateTime DueOn, bool IsReminded);

/// <summary>What a check-in produced, so the desk can read the token back.</summary>
public record CheckInResult(Guid VisitId, int TokenNo, string PatientName);

/// <summary>
/// Advance booking shared by every clinical module: the day's list, booking,
/// cancel/reschedule, check-in, and the on-screen reminder call list.
///
/// Thin on purpose — <see cref="AppointmentsService"/> already holds every
/// rule (module-enabled refusal, the checked-in guards, reschedule-as-new-row).
/// The one piece of orchestration here is check-in, which in the desktop
/// lives in the viewmodel: creating the module's own working record and then
/// closing the loop. That has to live somewhere the browser cannot get wrong,
/// so it lives here.
/// </summary>
[ApiController]
[Authorize]
[Route("api/appointments")]
public class AppointmentsController(
    AppointmentsService appointments,
    OpdService opd,
    IDbContextFactory<AppDbContext> factory) : ControllerBase
{
    // ── The day's list ─────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<List<Appointment>>> ForDate(
        [FromQuery] DateTime? date, [FromQuery] Guid? doctorId)
        => Ok(await appointments.GetForDateAsync(date ?? DateTime.Today, doctorId));

    // ── Booking ────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<Appointment>> Book(BookAppointmentRequest request)
    {
        await using var db = await factory.CreateDbContextAsync();

        // Read the names rather than accepting them. Both lookups also serve
        // as existence checks that the global query filter scopes to this
        // tenant, so an Id belonging to another clinic simply is not found.
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PatientId);
        if (patient is null) return BadRequest("Select a patient, or add a new one.");

        var doctor = await db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DoctorId);
        if (doctor is null) return BadRequest("Select a doctor.");

        try
        {
            var saved = await appointments.BookAsync(new Appointment
            {
                PatientId = patient.Id,
                PatientName = patient.Name,
                PatientPhone = patient.Phone,
                DoctorId = doctor.Id,
                DoctorName = doctor.Name,
                ScheduledOn = request.ScheduledOn,
                DurationMinutes = request.DurationMinutes,
                ModuleContext = request.ModuleContext,
                Reason = NullIfBlank(request.Reason),
                Notes = NullIfBlank(request.Notes)
            });

            return Ok(saved);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── Cancel and reschedule ──────────────────────────────────────────────

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelAppointmentRequest request)
    {
        // Refused here as well as on the screen: a cancellation with no
        // reason recorded is one nobody can account for later.
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("Say why the appointment is being cancelled.");

        try
        {
            await appointments.CancelAsync(id, request.Reason.Trim());
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/reschedule")]
    public async Task<ActionResult<Appointment>> Reschedule(Guid id, RescheduleAppointmentRequest request)
    {
        try
        {
            return Ok(await appointments.RescheduleAsync(id, request.ScheduledOn, request.DurationMinutes));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── Check-in ───────────────────────────────────────────────────────────

    /// <summary>
    /// Turns a booked appointment into the working record its module actually
    /// uses, then marks it checked in. Only <see cref="AppointmentModuleContext.General"/>
    /// has a module to route to today; Dentist and Pathology Lab join this
    /// same switch once their own services exist.
    ///
    /// The visit is booked at <c>now</c>, not at the appointment's scheduled
    /// time — the token has to reflect when the patient actually walked in,
    /// or the queue stops ordering by arrival.
    /// </summary>
    [HttpPost("{id:guid}/check-in")]
    public async Task<ActionResult<CheckInResult>> CheckIn(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();

        var appointment = await db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (appointment is null) return NotFound();

        if (appointment.ModuleContext is not AppointmentModuleContext.General)
            return BadRequest($"Check-in for {appointment.ModuleContext} is not available yet.");

        var doctor = await db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.Id == appointment.DoctorId);

        try
        {
            var visit = await opd.BookVisitAsync(
                appointment.PatientId, appointment.DoctorId, DateTime.Now,
                appointment.Reason, doctor?.ConsultationFee ?? 0, appointment.Id);

            await appointments.MarkCheckedInAsync(appointment.Id, visit.Id);

            return Ok(new CheckInResult(visit.Id, visit.TokenNo, appointment.PatientName));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── Reminders ──────────────────────────────────────────────────────────

    [HttpGet("reminders")]
    public async Task<ActionResult<List<ReminderItem>>> Reminders([FromQuery] int leadDays = 3)
        => Ok(await appointments.GetDueRemindersAsync(Math.Max(0, leadDays)));

    /// <summary>
    /// Marks a reminder done, or undoes it. One endpoint rather than two
    /// because the screen has one button whose label flips — splitting it
    /// would let the client's idea of the current state disagree with the
    /// server's and write the wrong one.
    ///
    /// <c>IsReminded</c> is the state the row is in *now*; this moves it to
    /// the other one.
    /// </summary>
    [HttpPost("reminders/toggle")]
    public async Task<IActionResult> ToggleReminder(ToggleReminderRequest request)
    {
        if (request.IsReminded)
            await appointments.UnmarkReminderActionedAsync(request.SourceKind, request.SourceId);
        else
            await appointments.MarkReminderActionedAsync(
                request.SourceKind, request.SourceId, request.PatientId, request.DueOn,
                // The desktop stamps the literal "front desk"; the web has a
                // real authenticated user, which is strictly better.
                User.Identity?.Name);

        return NoContent();
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
