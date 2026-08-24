using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Data;

namespace SivayaanHMS.Tests;

/// <summary>Fixed tenant for a test — no DI container involved.</summary>
public sealed class FixedTenantContext(Guid tenantId) : ICurrentTenantContext
{
    public Guid TenantId { get; } = tenantId;
}

/// <summary>
/// One throwaway SQL Server database per test, dropped afterwards.
///
/// Deliberately the real provider rather than SQLite or the in-memory
/// provider. Two of the suites here — <see cref="NumberServiceConcurrencyTests"/>
/// and <see cref="PharmacyServiceConcurrencyTests"/> — exist to prove that two
/// simultaneous callers cannot take the same invoice number or oversell the
/// same batch. Those guarantees are made by provider-specific locking, so a
/// test of them against a different database proves something true of a
/// database nobody runs.
///
/// The server comes from SIVAYAANHMS_TEST_SQL when set, so CI can point at
/// its own instance; otherwise the local SQL Express.
/// </summary>
public sealed class TestDb : IDisposable
{
    public const string ServerOverrideVariable = "SIVAYAANHMS_TEST_SQL";

    private const string DefaultServer =
        @"Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True";

    private readonly string _database = $"SivayaanHMSTest_{Guid.NewGuid():N}";
    private readonly string _server;

    public TestDb()
    {
        _server = Environment.GetEnvironmentVariable(ServerOverrideVariable) ?? DefaultServer;
    }

    private string ConnectionString =>
        new SqlConnectionStringBuilder(_server) { InitialCatalog = _database }.ConnectionString;

    public AppDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new AppDbContext(options, currentTenant: new FixedTenantContext(tenantId));
    }

    /// <summary>
    /// Builds the schema from the model rather than by replaying migrations.
    ///
    /// A test wants today's shape, and gets it in one round trip instead of
    /// several. That migrations actually apply is a different claim, and one
    /// a passing unit test should not be trusted for — it is proved by
    /// applying them to a real database, which the deployment does every
    /// time.
    /// </summary>
    public async Task MigrateAsync()
    {
        await using var db = CreateContext(Guid.Empty);
        await db.Database.EnsureCreatedAsync();
    }

    public IDbContextFactory<AppDbContext> CreateFactory(Guid tenantId) => new FixedTenantDbContextFactory(this, tenantId);

    private sealed class FixedTenantDbContextFactory(TestDb db, Guid tenantId) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => db.CreateContext(tenantId);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(db.CreateContext(tenantId));
    }

    public void Dispose()
    {
        // Pooled connections keep the database in use, so SQL Server refuses
        // to drop it. Clearing the pool first is the equivalent of the
        // ClearAllPools this class needed under SQLite for the same reason.
        SqlConnection.ClearAllPools();

        try
        {
            using var master = new SqlConnection(
                new SqlConnectionStringBuilder(_server) { InitialCatalog = "master" }.ConnectionString);
            master.Open();

            using var drop = master.CreateCommand();

            // SINGLE_USER WITH ROLLBACK IMMEDIATE evicts anything still
            // holding the database — without it one leaked connection leaves
            // a test database behind on the server forever.
            drop.CommandText = $"""
                IF DB_ID('{_database}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_database}];
                END
                """;
            drop.ExecuteNonQuery();
        }
        catch
        {
            // A test that has already failed should report its own reason,
            // not be replaced by a cleanup error. A stray test database is
            // untidy; a misleading failure costs someone an afternoon.
        }
    }
}
