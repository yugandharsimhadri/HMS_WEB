using Microsoft.EntityFrameworkCore;

namespace SivayaanHMS.Data;

/// <summary>
/// An IDbContextFactory pinned to one specific tenant, independent of
/// whatever the ambient request's tenant claim says. The normal, DI-
/// registered factory resolves ICurrentTenantContext from the current
/// HTTP request — exactly right for every authenticated endpoint, and
/// exactly wrong for the two places that run before a request has a
/// tenant at all: signing up a brand-new clinic, and logging in (the
/// tenant has to be resolved from the login form itself, not a token
/// that doesn't exist yet). Both build a factory pinned like this instead
/// of going through DI, so a bug elsewhere can never make either of them
/// silently operate against the wrong clinic — or an empty one.
/// </summary>
public sealed class TenantScopedDbContextFactory(DbContextOptions<AppDbContext> options, Guid tenantId)
    : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
        => new(options, currentTenant: new StaticTenantContext(tenantId));

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
        => Task.FromResult(CreateDbContext());
}
