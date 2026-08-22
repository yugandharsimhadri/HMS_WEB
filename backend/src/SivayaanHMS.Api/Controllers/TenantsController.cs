using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Data.Security;

namespace SivayaanHMS.Api.Controllers;

public record RegisterTenantRequest(string ClinicName, string Slug, string AdminPassword);
public record RegisterTenantResponse(Guid TenantId, string Slug, string AdminUsername);

/// <summary>
/// Clinic signup — the SaaS entry point the desktop never had. A new
/// clinic is usable within seconds: provisioning runs every master-data
/// seeder plus the starter catalogue, exactly as Settings → Features
/// already worked on the desktop, just triggered by registration instead
/// of first launch. See TenantProvisioner and SAAS_MIGRATION.md's own
/// framing of the seeders as tenant provisioning.
/// </summary>
[ApiController]
[Route("api/tenants")]
public partial class TenantsController(DbContextOptions<AppDbContext> dbOptions, ILoggerFactory loggerFactory) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<RegisterTenantResponse>> Register(RegisterTenantRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClinicName))
            return BadRequest("Clinic name is required.");

        var slug = NormaliseSlug(request.Slug);
        if (slug.Length < 3)
            return BadRequest("Choose a clinic URL of at least 3 characters (letters, numbers, hyphens).");

        if (string.IsNullOrWhiteSpace(request.AdminPassword) || request.AdminPassword.Length < 8)
            return BadRequest("Choose an admin password of at least 8 characters.");

        // Slug uniqueness is checked against the Tenants table, which is
        // deliberately not tenant-filtered — any pinned context can read
        // it, since it is what tenant-scoping is scoped against.
        var tenantId = Guid.NewGuid();
        await using var db = new TenantScopedDbContextFactory(dbOptions, tenantId).CreateDbContext();

        if (await db.Tenants.AnyAsync(t => t.Slug == slug))
            return Conflict($"'{slug}' is already taken. Choose another clinic URL.");

        db.Tenants.Add(new Tenant { Id = tenantId, Slug = slug, ClinicName = request.ClinicName.Trim() });
        await db.SaveChangesAsync();

        var logger = loggerFactory.CreateLogger("TenantProvisioning");
        var admin = await TenantProvisioner.ProvisionAsync(db, slug, logger);

        // The seeder gives every clinic the same starter password (see
        // UserSeeder) — reset it to the one the signer-up actually chose
        // before handing the account over. MustChangePassword stays true
        // either way.
        var (hash, salt) = PasswordHasher.Hash(request.AdminPassword);
        admin.PasswordHash = hash;
        admin.PasswordSalt = salt;
        await db.SaveChangesAsync();

        return Ok(new RegisterTenantResponse(tenantId, slug, admin.Username));
    }

    private static string NormaliseSlug(string raw)
    {
        var lowered = (raw).Trim().ToLowerInvariant();
        var slug = NonSlugCharacters().Replace(lowered, "-").Trim('-');
        return CollapseHyphens().Replace(slug, "-");
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugCharacters();

    [GeneratedRegex("-{2,}")]
    private static partial Regex CollapseHyphens();
}
