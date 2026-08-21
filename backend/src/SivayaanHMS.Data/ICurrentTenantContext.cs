namespace SivayaanHMS.Data;

/// <summary>
/// The clinic making the current request. Populated by the API layer from
/// the caller's auth claim — never from a request body or query string, so
/// a client can never ask to see, or write into, another tenant's data by
/// simply changing an id. <see cref="AppDbContext"/> uses this for both the
/// global read filter and to stamp new rows on save; see the tenancy
/// decision in SAAS_MIGRATION.md.
/// </summary>
public interface ICurrentTenantContext
{
    Guid TenantId { get; }
}

/// <summary>The default for design-time and any context built without a
/// real request — resolves to a tenant nothing belongs to, so a missing DI
/// registration reads back zero rows rather than leaking across tenants.</summary>
public class NullCurrentTenantContext : ICurrentTenantContext
{
    public Guid TenantId => Guid.Empty;
}
