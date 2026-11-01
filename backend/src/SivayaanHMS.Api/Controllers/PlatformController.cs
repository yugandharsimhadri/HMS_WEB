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

/// <summary>How many rows of one kind a clinic has, how many arrived inside the
/// window, and when the last one did. A count and a date — never a row.</summary>
public record UsageCount(int Total, int Recent, DateTime? LastAt);

/// <summary>
/// Whether a clinic is using the product, in four words a support agent can act on:
/// <c>Active</c> — records entered inside the window;
/// <c>NotStarted</c> — has never entered a single record, signed in or not;
/// <c>SigningInOnly</c> — has used it before, signed in inside the window, entered nothing;
/// <c>Dormant</c> — has used it before, nothing at all inside the window.
/// </summary>
public enum UsageStatus { Active, SigningInOnly, Dormant, NotStarted }

public record PlatformClinicUsage(
    Guid TenantId, string Slug, string ClinicName, DateTime CreatedAt, bool IsActive,
    DateTime? LicenseExpiresOn, string LicenseStatus,
    int Users, DateTime? LastSignIn, DateTime? LastActivity, int RecordsTotal, int RecordsRecent,
    UsageStatus Status,
    UsageCount Patients, UsageCount Visits, UsageCount Appointments, UsageCount PharmacyBills,
    UsageCount DiagnosticBills, UsageCount LabOrders, UsageCount ProcedureBills, UsageCount DentalCases,
    UsageCount Vaccinations, UsageCount Admissions);

public record PlatformUsage(DateTime Since, int Days, List<PlatformClinicUsage> Clinics);

public record ResetPasswordResponse(string Username, string TemporaryPassword);

/// <summary>
/// The support console — the only thing EnterpriseAdmin can reach.
///
/// Three jobs, all of which need somebody outside a clinic: putting a locked-
/// out clinic owner back in, moving a licence date, and seeing which clinics
/// are actually using what they registered for. Nothing here reads a patient,
/// a visit, a bill or a prescription, and there is deliberately no route that
/// could — support staff have no business inside a clinic's records, and a
/// console that *could* show them would eventually be asked to. The usage
/// view is counts and dates, grouped in the database: no row's contents ever
/// leave it.
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

    /// <summary>
    /// How much each clinic is using the product: records entered in total and
    /// inside the last <paramref name="days"/> days, per kind, plus the last
    /// sign-in. For spotting the clinics that registered and stopped — or never
    /// started — before their licence comes up for renewal.
    /// </summary>
    [HttpGet("usage")]
    public async Task<ActionResult<PlatformUsage>> Usage([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var today = clock.Now.Date;
        var since = today.AddDays(-(days - 1));

        await using var db = Db(Guid.Empty);

        var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.ClinicName).ToListAsync();

        // One grouped query per kind; a DbContext runs one query at a time, so in sequence.
        var patients = await TallyAsync(db.Patients, since);
        var visits = await TallyAsync(db.Visits, since);
        var appointments = await TallyAsync(db.Appointments, since);
        var sales = await TallyAsync(db.Sales, since);
        var diagnostics = await TallyAsync(db.DiagnosticBills, since);
        var lab = await TallyAsync(db.LabOrders, since);
        var procedures = await TallyAsync(db.ProcedureBills, since);
        var dental = await TallyAsync(db.DentalCases, since);
        var vaccinations = await TallyAsync(db.VaccinationRecords, since);
        var admissions = await TallyAsync(db.Admissions, since);

        var users = await db.Users.AsNoTracking().IgnoreQueryFilters()
            .Where(u => !u.IsDeleted)
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count(), LastSignIn = g.Max(u => u.LastLoginOn) })
            .ToDictionaryAsync(x => x.TenantId);

        var none = new UsageCount(0, 0, null);
        UsageCount Of(Dictionary<Guid, Tally> tallies, Guid tenant)
            => tallies.TryGetValue(tenant, out var t) ? new UsageCount(t.Total, t.Recent, t.LastAt) : none;

        var result = tenants.Select(t =>
        {
            UsageCount[] kinds =
            [
                Of(patients, t.Id), Of(visits, t.Id), Of(appointments, t.Id), Of(sales, t.Id),
                Of(diagnostics, t.Id), Of(lab, t.Id), Of(procedures, t.Id), Of(dental, t.Id), Of(vaccinations, t.Id),
                Of(admissions, t.Id),
            ];

            var total = kinds.Sum(k => k.Total);
            var recent = kinds.Sum(k => k.Recent);
            var lastActivity = kinds.Max(k => k.LastAt);
            var userInfo = users.GetValueOrDefault(t.Id);
            var lastSignIn = userInfo?.LastSignIn;

            // Never having entered a record outranks signing in: a clinic that logs in to
            // look around and leaves is exactly the one this view exists to find.
            var status = recent > 0 ? UsageStatus.Active
                : total == 0 ? UsageStatus.NotStarted
                : lastSignIn >= since ? UsageStatus.SigningInOnly
                : UsageStatus.Dormant;

            var (licence, _) = LicenseRules.Describe(t.LicenseExpiresOn, today);

            return new PlatformClinicUsage(
                t.Id, t.Slug, t.ClinicName, t.CreatedAt, t.IsActive, t.LicenseExpiresOn, licence,
                userInfo?.Count ?? 0, lastSignIn, lastActivity, total, recent, status,
                kinds[0], kinds[1], kinds[2], kinds[3], kinds[4], kinds[5], kinds[6], kinds[7], kinds[8], kinds[9]);
        }).ToList();

        return Ok(new PlatformUsage(since, days, result));
    }

    private sealed record Tally(Guid TenantId, int Total, int Recent, DateTime? LastAt);

    /// <summary>Counts per clinic, in the database. IgnoreQueryFilters because this spans
    /// clinics; deleted rows are left out, as a clinic would leave them out itself.</summary>
    private static Task<Dictionary<Guid, Tally>> TallyAsync<T>(IQueryable<T> rows, DateTime since) where T : BaseEntity
        => rows.AsNoTracking().IgnoreQueryFilters()
            .Where(r => !r.IsDeleted)
            .GroupBy(r => r.TenantId)
            .Select(g => new Tally(g.Key, g.Count(), g.Count(r => r.CreatedAt >= since), g.Max(r => (DateTime?)r.CreatedAt)))
            .ToDictionaryAsync(t => t.TenantId);

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
