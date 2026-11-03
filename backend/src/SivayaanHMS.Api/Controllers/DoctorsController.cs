using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

/// <summary>What the Doctors screen may set. A DTO rather than the entity,
/// for the same reason as <see cref="SavePatientRequest"/> — binding the
/// entity would let a client write TenantId and the audit columns.
///
/// <paramref name="Qualification"/> and <paramref name="OpdValidDays"/> are
/// "leave alone when absent", not "reset when absent": a browser tab still
/// running last week's screen does not know about them, and saving an
/// unrelated edit from it must not silently blank a doctor's degrees or switch
/// off the free-review window a clinic is relying on. Send an empty
/// qualification to clear it, and 0 to turn the window off.</summary>
public record SaveDoctorRequest(
    Guid? Id, string Name, string? Speciality, string? RegistrationNo,
    decimal ConsultationFee, bool IsActive,
    string? Qualification = null, int? OpdValidDays = null);

[ApiController]
[Authorize]
[Route("api/doctors")]
public class DoctorsController(OpdService opd, IDbContextFactory<AppDbContext> factory) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Doctor>>> List([FromQuery] bool includeInactive = false)
        => Ok(await opd.GetDoctorsAsync(includeInactive));

    [HttpPost]
    public async Task<ActionResult<Doctor>> Save(SaveDoctorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("The doctor's name is required.");

        await using var db = await factory.CreateDbContextAsync();

        var doctor = request.Id is { } id ? await db.Doctors.FirstOrDefaultAsync(d => d.Id == id) : null;
        var isNew = doctor is null;
        doctor ??= new Doctor();

        doctor.Name = request.Name.Trim();
        doctor.Speciality = NullIfBlank(request.Speciality);
        doctor.RegistrationNo = NullIfBlank(request.RegistrationNo);
        doctor.ConsultationFee = request.ConsultationFee;
        if (request.Qualification is not null) doctor.Qualification = NullIfBlank(request.Qualification);

        // A negative window is a typo, not a policy, and it would make every
        // visit look like it was already covered.
        if (request.OpdValidDays is { } validDays) doctor.OpdValidDays = Math.Max(0, validDays);
        doctor.IsActive = request.IsActive;

        if (isNew) db.Doctors.Add(doctor);

        await db.SaveChangesAsync();
        return Ok(doctor);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
