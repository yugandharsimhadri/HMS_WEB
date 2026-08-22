using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

/// <summary>
/// What the register screen may actually set on a patient.
///
/// Deliberately not the <see cref="Patient"/> entity itself. Binding an EF
/// entity straight from a request body hands the client every column on it —
/// including TenantId, IsDeleted and the audit stamps — so a caller could
/// post a patient into another clinic's register or resurrect a deleted one.
/// A DTO of exactly the editable fields makes that impossible by
/// construction rather than by remembering to check.
/// </summary>
public record SavePatientRequest(
    Guid? Id, string Name, string? Phone, int Age, DateTime? DateOfBirth,
    string? BloodGroup, string? GuardianName, Gender Gender,
    string? Address, string? Allergies);

/// <summary>
/// OPD patient register: search, the visit history behind each patient, and
/// adding or editing them. A request carrying a token issued for one tenant
/// only ever sees, and only ever writes, that tenant's patients —
/// AppDbContext's global filter, not anything in this file.
/// </summary>
[ApiController]
[Authorize]
[Route("api/patients")]
public class PatientsController(OpdService opd, IDbContextFactory<AppDbContext> factory) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Patient>>> Search([FromQuery] string? term, [FromQuery] int take = 50)
        => Ok(await opd.SearchPatientsAsync(term, take));

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<List<Visit>>> History(Guid id)
        => Ok(await opd.GetPatientHistoryAsync(id));

    /// <summary>
    /// Adds a patient, or updates one when <c>Id</c> names an existing row.
    ///
    /// An update loads the tracked row and copies the editable fields onto
    /// it rather than attaching what the client sent. Two reasons: the
    /// client never gets to write TenantId or the audit columns, and the
    /// row keeps its own RowVersion — attaching a detached entity whose
    /// token the client did not round-trip made every edit fail as a
    /// spurious concurrency conflict.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Patient>> Save(SavePatientRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Patient name is required.");

        // Age is not required here on purpose. The register's own editor asks
        // for a date of birth or an age and refuses without one, but booking
        // a walk-in does not — the desk has a queue and the child's exact age
        // can be filled in later. The desktop draws the line in the same
        // place, and moving it would make quick booking impossible.
        var age = request.DateOfBirth is { } dob ? Patient.AgeFromDob(dob) : request.Age;

        await using var db = await factory.CreateDbContextAsync();

        var patient = request.Id is { } id
            ? await db.Patients.FirstOrDefaultAsync(p => p.Id == id)
            : null;

        var isNew = patient is null;
        patient ??= new Patient();

        patient.Name = request.Name.Trim();
        patient.Phone = request.Phone?.Trim() ?? "";
        patient.Age = age;
        patient.DateOfBirth = request.DateOfBirth;
        patient.BloodGroup = NullIfBlank(request.BloodGroup);
        // A guardian only means anything for a minor; keeping one on an adult
        // is a leftover from when they were not.
        patient.GuardianName = age < 18 ? NullIfBlank(request.GuardianName) : null;
        patient.Gender = request.Gender;
        patient.Address = NullIfBlank(request.Address);
        patient.Allergies = NullIfBlank(request.Allergies);

        if (isNew)
        {
            patient.PatientNo = await NumberService.NextAsync(db, NumberService.Patient);
            db.Patients.Add(patient);
        }

        await db.SaveChangesAsync();
        return Ok(patient);
    }

    /// <summary>
    /// Soft-deletes a patient, refused while visits still reference them —
    /// a register that can lose somebody who has been seen is a register
    /// nobody can rely on. Returns the reason when it is refused, null when
    /// it went through.
    /// </summary>
    [HttpPost("{id:guid}/remove")]
    public async Task<ActionResult<string?>> Remove(Guid id)
        => Ok(await opd.DeletePatientAsync(id));

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
