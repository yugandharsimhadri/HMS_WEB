using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

/// <summary>What the Doctors screen may set. A DTO rather than the entity,
/// for the same reason as <see cref="SavePatientRequest"/> — binding the
/// entity would let a client write TenantId and the audit columns.</summary>
public record SaveDoctorRequest(
    Guid? Id, string Name, string? Speciality, string? RegistrationNo,
    decimal ConsultationFee, bool IsActive);

[ApiController]
[Authorize]
[Route("api/doctors")]
public class DoctorsController(OpdService opd, IDbContextFactory<AppDbContext> factory) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Doctor>>> List() => Ok(await opd.GetDoctorsAsync());

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
        doctor.IsActive = request.IsActive;

        if (isNew) db.Doctors.Add(doctor);

        await db.SaveChangesAsync();
        return Ok(doctor);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
