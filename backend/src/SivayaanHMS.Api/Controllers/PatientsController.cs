using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

/// <summary>
/// OPD patient search/registration — the first real clinical endpoint,
/// standing in as proof that the whole chain works: a request carrying a
/// token issued for one tenant only ever sees, and only ever writes,
/// that tenant's patients. OpdService itself is unchanged from the port;
/// everything tenant-related happens beneath it, in AppDbContext.
/// </summary>
[ApiController]
[Authorize]
[Route("api/patients")]
public class PatientsController(OpdService opd) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Patient>>> Search([FromQuery] string? term, [FromQuery] int take = 50)
        => Ok(await opd.SearchPatientsAsync(term, take));

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<List<Visit>>> History(Guid id)
        => Ok(await opd.GetPatientHistoryAsync(id));

    [HttpPost]
    public async Task<ActionResult<Patient>> Save(Patient patient)
        => Ok(await opd.SavePatientAsync(patient));
}
