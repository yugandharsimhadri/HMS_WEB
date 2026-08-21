using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

/// <summary>
/// Every settings group from the desktop's Settings screen, unchanged:
/// clinic identity and session hours, pharmacy identity and GST/licence,
/// document branding, and the module toggles that decide which nav items
/// even exist. All four groups are exposed here so the web edition has the
/// same configurability as HMS_WPF - an explicit requirement for this port,
/// not an afterthought.
/// </summary>
[ApiController]
[Authorize]
[Route("api/settings")]
public class SettingsController(SettingsService settings) : ControllerBase
{
    [HttpGet("clinic")]
    public async Task<ActionResult<ClinicProfile>> GetClinic() => Ok(await settings.GetClinicAsync());

    [HttpPost("clinic")]
    public async Task<IActionResult> SaveClinic(ClinicProfile profile)
    {
        await settings.SaveClinicAsync(profile);
        return NoContent();
    }

    [HttpGet("pharmacy")]
    public async Task<ActionResult<PharmacyProfile>> GetPharmacy() => Ok(await settings.GetPharmacyAsync());

    [HttpPost("pharmacy")]
    public async Task<IActionResult> SavePharmacy(PharmacyProfile profile)
    {
        await settings.SavePharmacyAsync(profile);
        return NoContent();
    }

    [HttpGet("document-theme")]
    public async Task<ActionResult<DocumentTheme>> GetDocumentTheme() => Ok(await settings.GetDocumentThemeAsync());

    [HttpPost("document-theme")]
    public async Task<IActionResult> SaveDocumentTheme(DocumentTheme theme)
    {
        await settings.SaveDocumentThemeAsync(theme);
        return NoContent();
    }

    [HttpGet("general")]
    public async Task<ActionResult<GeneralSettings>> GetGeneral() => Ok(await settings.GetGeneralAsync());

    [HttpPost("general")]
    public async Task<IActionResult> SaveGeneral(GeneralSettings general)
    {
        try
        {
            await settings.SaveGeneralAsync(general);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
