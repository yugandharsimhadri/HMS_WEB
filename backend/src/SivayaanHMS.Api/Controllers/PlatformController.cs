using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Api.Auth;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Data.Security;

namespace SivayaanHMS.Api.Controllers;

public record PlatformAdminAccount(
    Guid UserId, string Username, string DisplayName, bool IsActive, DateTime? LastLoginOn);

public record PlatformClinic(
    Guid TenantId, string Slug, string ClinicName, DateTime CreatedAt, bool IsActive,
    DateTime? LicenseExpiresOn, int DaysRemaining, string LicenseStatus,
    IReadOnlyList<PlatformAdminAccount> Admins);

public record SetLicenseRequest(DateTime? ExpiresOn);

public record ResetPasswordResponse(string Username, string TemporaryPassword);

/// <summary>
/// The support console — the only thing EnterpriseAdmin can reach.
///
/// Two jobs, both of which need somebody outside a clinic: putting a locked-
/// out clinic owner back in, and moving a licence date. Nothing here reads a
/// patient, a visit, a bill or a prescription, and there is deliberately no
/// route that could — support staff have no business inside a clinic's
/// records, and a console that *could* show them would eventually be asked
/// to.
///
/// Cross-tenant reads use IgnoreQueryFilters because this is the one caller
/// in the system that legitimately spans tenants; note that everything it
/// selects is registration metadata, never clinical data.
/// </summary>
[ApiController]
[Route("api/platform")]
[Authorize(Policy = TenantClaimTypes.PlatformAdminPolicy)]
public class PlatformController(
    DbContextOptions<AppDbContext> dbOptions,
    IClock clock,
    ILogger<PlatformController> logger) : ControllerBase
{
    /// <summary>Every registered clinic and its admin accounts, so support
    /// can find the caller by clinic name rather than asking a locked-out
    /// person to recite a slug they cannot look up.</summary>
    [HttpGet("clinics")]
    public async Task<ActionResult<List<PlatformClinic>>> Clinics()
    {
        await using var db = Db(Guid.Empty);

        var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.ClinicName).ToListAsync();
        var tenantIds = tenants.Select(t => t.Id).ToList();

        // Admins only. A clinic's receptionists are its own business — this
        // console exists to restore the owner, who is the person who can then
        // fix everyone else from Settings.
        var admins = await db.Users.AsNoTracking().IgnoreQueryFilters()
            .Where(u => !u.IsDeleted && u.Role == UserRole.Admin && tenantIds.Contains(u.TenantId))
            .ToListAsync();

        var today = clock.Now.Date;

        return Ok(tenants.Select(t =>
        {
            var (status, days) = LicenseRules.Describe(t.LicenseExpiresOn, today);
            return new PlatformClinic(
                t.Id, t.Slug, t.ClinicName, t.CreatedAt, t.IsActive, t.LicenseExpiresOn, days, status,
                admins.Where(u => u.TenantId == t.Id)
                    .OrderBy(u => u.Username)
                    .Select(u => new PlatformAdminAccount(
                        u.Id, u.Username, u.DisplayName, u.IsActive, u.LastLoginOn))
                    .ToList());
        }).ToList());
    }

    /// <summary>
    /// Issues a one-time password for a clinic admin who cannot get in.
    ///
    /// Generated here rather than typed by the support agent: a human under
    /// time pressure picks a password they can say quickly, and that is
    /// exactly the password worth guessing. It is returned once, in this
    /// response, and never stored in readable form — if it is lost, the fix
    /// is to issue another, not to look the old one up.
    /// </summary>
    [HttpPost("clinics/{tenantId:guid}/admins/{userId:guid}/reset-password")]
    public async Task<ActionResult<ResetPasswordResponse>> ResetPassword(Guid tenantId, Guid userId)
    {
        // Scoped to the tenant being repaired, not Guid.Empty: the write
        // then goes through the same tenant filter and audit stamping as any
        // other change to that clinic's data.
        await using var db = Db(tenantId);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);

        // The role check matters as much as the id: without it, a support
        // agent who pasted the wrong guid could reset any user in the system
        // rather than the one admin this console is scoped to.
        if (user is null || user.Role != UserRole.Admin)
            return NotFound("No clinic admin with that id.");

        var temporary = GenerateTemporaryPassword();
        var (hash, salt) = PasswordHasher.Hash(temporary);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;

        // Always. The support agent has just seen this password, so it is
        // good for exactly one sign-in and no longer.
        user.MustChangePassword = true;
        user.IsActive = true;

        await db.SaveChangesAsync();

        logger.LogWarning(
            "Platform support reset the password for clinic admin {Username} (tenant {TenantId}).",
            user.Username, tenantId);

        return Ok(new ResetPasswordResponse(user.Username, temporary));
    }

    /// <summary>Moves a clinic's licence date, or clears it for a clinic on
    /// no fixed term. Nothing about the clinic's data changes either way —
    /// see Tenant.LicenseExpiresOn.</summary>
    [HttpPut("clinics/{tenantId:guid}/license")]
    public async Task<ActionResult<PlatformClinic>> SetLicense(Guid tenantId, SetLicenseRequest request)
    {
        await using var db = Db(Guid.Empty);

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return NotFound("No clinic with that id.");

        // Date only: a licence runs to the end of its last day, so storing a
        // time here would expire someone at whatever hour the box was ticked.
        tenant.LicenseExpiresOn = request.ExpiresOn?.Date;
        await db.SaveChangesAsync();

        logger.LogWarning(
            "Platform support set the licence for {Slug} to {Expiry}.",
            tenant.Slug, tenant.LicenseExpiresOn?.ToString("yyyy-MM-dd") ?? "no expiry");

        var today = clock.Now.Date;
        var (status, days) = LicenseRules.Describe(tenant.LicenseExpiresOn, today);

        return Ok(new PlatformClinic(
            tenant.Id, tenant.Slug, tenant.ClinicName, tenant.CreatedAt, tenant.IsActive,
            tenant.LicenseExpiresOn, days, status, []));
    }

    private AppDbContext Db(Guid tenantId)
        => new TenantScopedDbContextFactory(dbOptions, tenantId).CreateDbContext();

    /// <summary>
    /// A temporary password that survives being read aloud down a phone
    /// line. No 0/O, 1/l/I, 5/S — the characters people mishear are the ones
    /// that turn one support call into three. Grouped for the same reason.
    /// </summary>
    private static string GenerateTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKMNPQRTUVWXYZabcdefghijkmnpqrtuvwxyz23456789";
        var chars = RandomNumberGenerator.GetString(alphabet, 12);
        return $"{chars[..4]}-{chars[4..8]}-{chars[8..]}";
    }
}
