using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Data;

namespace SivayaanHMS.Tests;

/// <summary>Fixed tenant for a test — no DI container involved.</summary>
public sealed class FixedTenantContext(Guid tenantId) : ICurrentTenantContext
{
    public Guid TenantId { get; } = tenantId;
}

/// <summary>
/// One throwaway SQLite file per test, migrated fresh. A file rather than
/// ":memory:" — the concurrency tests open several connections at once, and
/// SQLite's in-memory mode is one private database per connection unless
/// wired up with a shared-cache connection string, which is more moving
/// parts than a temp file for no benefit here.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"sivayaanhms-test-{Guid.NewGuid():N}.db");

    public AppDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_path}")
            .Options;

        return new AppDbContext(options, currentTenant: new FixedTenantContext(tenantId));
    }

    public async Task MigrateAsync()
    {
        await using var db = CreateContext(Guid.Empty);
        await db.Database.MigrateAsync();
    }

    /// <summary>A factory bound to one fixed tenant — what a service under
    /// test (IDbContextFactory&lt;AppDbContext&gt; constructor parameter)
    /// gets instead of the real DI-resolved one.</summary>
    public IDbContextFactory<AppDbContext> CreateFactory(Guid tenantId) => new FixedTenantDbContextFactory(this, tenantId);

    private sealed class FixedTenantDbContextFactory(TestDb db, Guid tenantId) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => db.CreateContext(tenantId);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(db.CreateContext(tenantId));
    }

    public void Dispose()
    {
        // Microsoft.Data.Sqlite pools the native connection by default, so
        // disposing an AppDbContext returns the handle to the pool instead
        // of releasing the file — without this the temp file is still
        // locked here and cleanup throws.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var file = _path + suffix;
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
