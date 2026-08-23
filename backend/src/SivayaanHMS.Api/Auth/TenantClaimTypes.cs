namespace SivayaanHMS.Api.Auth;

/// <summary>Custom claim types this API issues and reads, alongside the
/// standard NameIdentifier/Role ones.</summary>
public static class TenantClaimTypes
{
    /// <summary>The clinic (Tenant.Id) a token was issued for. Every
    /// authenticated request is scoped to exactly this tenant — see
    /// HttpCurrentTenantContext.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>The role name carried by a platform-support token. It is
    /// deliberately not a member of <c>UserRole</c>: no clinic can ever
    /// assign it to one of its own users, because the enum a clinic's user
    /// editor writes from does not contain it.</summary>
    public const string PlatformAdminRole = "EnterpriseAdmin";

    /// <summary>Authorization policy names. <see cref="ClinicPolicy"/> is
    /// the default for the whole API — a bare [Authorize] means "a signed-in
    /// user of some clinic", which a platform-support token is not.</summary>
    public const string ClinicPolicy = "Clinic";

    /// <summary>
    /// A clinic's own Admin — the owner. Requires the tenant claim *as well
    /// as* the role, which is the whole reason it exists as a policy: writing
    /// <c>[Authorize(Roles = "Admin")]</c> instead would replace the default
    /// clinic policy rather than add to it, quietly dropping the tenant
    /// requirement that keeps a support token out.
    /// </summary>
    public const string ClinicAdminPolicy = "ClinicAdmin";

    public const string PlatformAdminPolicy = "PlatformAdmin";
}
