using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SivayaanHMS.Api.Auth;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

/// <summary>One thing wrong with one medicine, and what fixing it would do.
/// <paramref name="ChangesStock"/> is the one to read first: a repair that
/// moves a count is a different decision from one that only relabels.</summary>
public record HealthFindingDto(
    Guid ProductId, string ProductName, HealthProblem Problem, string ProblemLabel,
    string Current, string Proposed, string Explanation,
    int QuantityBefore, int QuantityAfter, bool ChangesStock, bool CanRepairAutomatically);

/// <summary>
/// Which medicines to repair — ids only.
///
/// Deliberately not the findings themselves. Posting back a finding would
/// let a caller name any units-per-pack it liked and have the server repack
/// stock to it; instead the server re-scans and repairs only what it
/// independently found wrong about these products.
/// </summary>
public record RepairRequest(List<Guid> ProductIds);

public record RepairResult(int Repaired, int RemainingFindings);

/// <summary>
/// Finds medicines whose records cannot be right, and repairs them in bulk.
///
/// The reason this matters more than its size suggests: a medicine whose
/// pack size says "15 TAB" while units-per-pack says 1 sells a whole strip
/// to anyone asking for one tablet, at fifteen times the price, and nothing
/// reports an error. InventoryPage already warns about exactly that
/// condition — until now without offering the fix.
///
/// Admin-only. A repair rewrites stock counts across the catalogue and
/// writes an adjustment for each, which is not something to leave on a
/// counter login.
/// </summary>
[ApiController]
[Authorize(Policy = TenantClaimTypes.ClinicAdminPolicy)]
[Route("api/data-health")]
public class DataHealthController(DataHealthService health) : ControllerBase
{
    private static string Describe(HealthProblem problem) => problem switch
    {
        HealthProblem.PackSizeDisagrees => "Pack size disagrees with units per pack",
        HealthProblem.BatchPackDisagrees => "Batches received at a different pack size",
        HealthProblem.UnitNotSet => "No dispensing unit set",
        HealthProblem.Duplicate => "Possible duplicate medicine",
        _ => problem.ToString(),
    };

    private static HealthFindingDto ToDto(HealthFinding f) => new(
        f.ProductId, f.ProductName, f.Problem, Describe(f.Problem),
        f.Current, f.Proposed, f.Explanation,
        f.QuantityBefore, f.QuantityAfter, f.ChangesStock, f.CanRepairAutomatically);

    [HttpGet("scan")]
    public async Task<ActionResult<List<HealthFindingDto>>> Scan()
        => Ok((await health.ScanAsync()).Select(ToDto).ToList());

    /// <summary>
    /// Repairs the named medicines and reports what is left.
    ///
    /// Duplicates are never repaired here however they are asked for —
    /// choosing which of two records to keep is a judgement about a clinic's
    /// own catalogue, and <c>CanRepairAutomatically</c> already says so.
    /// </summary>
    [HttpPost("repair")]
    public async Task<ActionResult<RepairResult>> Repair(RepairRequest request)
    {
        var wanted = (request.ProductIds ?? []).ToHashSet();
        if (wanted.Count == 0) return BadRequest("Choose at least one medicine to repair.");

        // Re-scanned rather than trusting the request: this is the same
        // "re-read the record, don't believe the client" rule the rest of the
        // API follows, and here it is what stops a crafted call repacking
        // stock to an arbitrary number.
        var findings = await health.ScanAsync();
        var target = findings.Where(f => wanted.Contains(f.ProductId) && f.CanRepairAutomatically).ToList();

        if (target.Count == 0)
            return Ok(new RepairResult(0, findings.Count));

        var repaired = await health.RepairAsync(target, User.Identity?.Name);

        // Scanned again so the caller sees the true state afterwards rather
        // than a number it has to subtract for itself.
        var remaining = (await health.ScanAsync()).Count;
        return Ok(new RepairResult(repaired, remaining));
    }
}
