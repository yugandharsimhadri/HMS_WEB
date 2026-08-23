using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Api.Auth;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Data.Import;

namespace SivayaanHMS.Api.Controllers;

public record ImportProfileDto(Guid Id, string Name);

/// <summary>One line of the vendor's bill and what would become of it.
/// <paramref name="UnitsAssumed"/> matters: it means nobody told us how many
/// units are in this pack, so the count is a guess that will be wrong if the
/// pack is not singles.</summary>
public record ImportLineDto(
    int SourceLine, string ProductName, string? PackSize, string BatchNo, DateTime Expiry,
    int Quantity, int FreeQuantity, decimal Rate, decimal Mrp,
    string Status, int UnitsPerPack, bool UnitsAssumed, int UnitsReceived);

public record ImportIssueDto(ImportSeverity Severity, int Line, string Field, string Message);

/// <summary>
/// What importing this file would do, before it does it.
///
/// <paramref name="CanImport"/> is the server's decision, not a suggestion —
/// the commit endpoint re-derives it and refuses independently.
/// </summary>
public record ImportPreviewDto(
    string FileName, string ProfileName, string BillNo, DateTime BillDate, string? SupplierName,
    decimal NetAmount, bool AlreadyImported, string? BlockedReason, bool CanImport,
    int NewMedicines, int NeedsChecking, int TotalUnits,
    List<ImportLineDto> Lines, List<ImportIssueDto> Issues);

/// <summary>
/// Loading a supplier's bill straight into stock.
///
/// Preview and commit are two separate uploads of the same file rather than
/// a cached preview held between them. The server re-parses and re-previews
/// on commit, so what it writes is always something it worked out itself
/// from the file — a preview posted back could claim anything. The desktop's
/// ImportViewModel does not let a person edit lines between the two steps
/// either, so nothing is lost by re-deriving.
///
/// Admin-only: this creates medicines and moves stock in bulk.
/// </summary>
[ApiController]
[Authorize(Policy = TenantClaimTypes.ClinicAdminPolicy)]
[Route("api/import")]
public class ImportController(
    PurchaseImportService import,
    IDbContextFactory<AppDbContext> factory,
    IClock clock) : ControllerBase
{
    /// <summary>The supplier formats this clinic knows how to read. Seeded at
    /// provisioning; a clinic whose supplier is not here needs a new profile,
    /// which is a code change rather than a setting.</summary>
    [HttpGet("profiles")]
    public async Task<ActionResult<List<ImportProfileDto>>> Profiles()
    {
        await using var db = await factory.CreateDbContextAsync();
        return Ok(await db.ImportProfiles.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ImportProfileDto(p.Id, p.Name))
            .ToListAsync());
    }

    [HttpPost("preview")]
    public async Task<ActionResult<ImportPreviewDto>> Preview([FromForm] Guid profileId, IFormFile file)
    {
        var built = await BuildAsync(profileId, file);
        return built.Error is not null ? BadRequest(built.Error) : Ok(ToDto(built.Preview!));
    }

    [HttpPost("commit")]
    public async Task<ActionResult<ImportResult>> Commit([FromForm] Guid profileId, IFormFile file)
    {
        var built = await BuildAsync(profileId, file);
        if (built.Error is not null) return BadRequest(built.Error);

        var preview = built.Preview!;

        // Checked here as well as shown on the preview. The two calls are
        // independent, and the clinic's stock may have moved between them.
        if (!preview.CanImport)
            return BadRequest(preview.BlockedReason ?? "This bill cannot be imported.");

        return Ok(await import.CommitAsync(preview));
    }

    private async Task<(ImportPreview? Preview, string? Error)> BuildAsync(Guid profileId, IFormFile? file)
    {
        if (file is null || file.Length == 0) return (null, "Choose the supplier's file.");

        // Bounded before it is read into memory. A CSV bill is kilobytes; a
        // 500 MB upload is a mistake or an attack, and either way should not
        // become a string.
        const long maxBytes = 5 * 1024 * 1024;
        if (file.Length > maxBytes) return (null, "That file is larger than 5 MB — it is not a bill.");

        await using var db = await factory.CreateDbContextAsync();
        var profile = await db.ImportProfiles.FirstOrDefaultAsync(p => p.Id == profileId);
        if (profile is null) return (null, "Choose the supplier's format.");

        string content;
        using (var reader = new StreamReader(file.OpenReadStream()))
            content = await reader.ReadToEndAsync();

        VendorBill bill;
        try
        {
            var csv = CsvFile.Parse(content);
            bill = new VendorBillParser(profile, clock).Parse(csv, file.FileName);
        }
        catch (Exception ex)
        {
            // A wrong file for the chosen supplier is the commonest thing to
            // happen here, and it should read as that rather than as a crash.
            return (null, $"That file could not be read as a {profile.Name} bill. {ex.Message}");
        }

        return (await import.PreviewAsync(bill, profile, file.FileName), null);
    }

    private static ImportPreviewDto ToDto(ImportPreview p) => new(
        p.FileName, p.ProfileName, p.Bill.BillNo, p.Bill.BillDate, p.SupplierName,
        p.Bill.NetAmount, p.AlreadyImported, p.BlockedReason, p.CanImport,
        p.NewMedicines, p.NeedsChecking, p.TotalUnits,
        p.Lines.Select(l => new ImportLineDto(
            l.Source.SourceLine, l.ProductName, l.Source.PackSize, l.Source.BatchNo, l.Source.Expiry,
            l.Source.Quantity, l.Source.FreeQuantity, l.Source.Rate, l.Source.Mrp,
            l.Status, l.UnitsPerPack, l.UnitsAssumed, l.UnitsReceived)).ToList(),
        p.Issues.Select(i => new ImportIssueDto(i.Severity, i.Line, i.Field, i.Message)).ToList());
}
