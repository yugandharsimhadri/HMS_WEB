namespace SivayaanHMS.Api.Auth;

/// <summary>Custom claim types this API issues and reads, alongside the
/// standard NameIdentifier/Role ones.</summary>
public static class TenantClaimTypes
{
    /// <summary>The clinic (Tenant.Id) a token was issued for. Every
    /// authenticated request is scoped to exactly this tenant — see
    /// HttpCurrentTenantContext.</summary>
    public const string TenantId = "tenant_id";
}
