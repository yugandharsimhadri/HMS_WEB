using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/doctors")]
public class DoctorsController(OpdService opd) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Doctor>>> List() => Ok(await opd.GetDoctorsAsync());

    [HttpPost]
    public async Task<IActionResult> Save(Doctor doctor)
    {
        await opd.SaveDoctorAsync(doctor);
        return NoContent();
    }
}
