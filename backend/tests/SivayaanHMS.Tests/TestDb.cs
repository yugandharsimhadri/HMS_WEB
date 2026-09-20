using Microsoft.EntityFrameworkCore;
using Npgsql;
using SivayaanHMS.Data;

namespace SivayaanHMS.Tests;

/// <summary>Fixed tenant for a test — no DI container involved.</summary>
public sealed class FixedTenantContext(Guid tenantId) : ICurrentTenantContext
{
    public Guid TenantId { get; } = tenantId;
}

/// <summary>
/// One throwaway PostgreSQL database per test, dropped afterwards.
///
/// Deliberately the real provider rather than SQLite or the in-memory
/// provider. Two of the suites here — <see cref="NumberServiceConcurrencyTests"/>
/// and <see cref="PharmacyServiceConcurrencyTests"/> — exist to prove that two
/// simultaneous callers cannot take the same invoice number or oversell the
/// same batch. Those guarantees are made by provider-specific locking, so a
/// test of them against a different database proves something true of a
/// database nobody runs.
///
/// The server comes from SIVAYAANHMS_TEST_PG when set — an Npgsql connection
/// string naming a host and a role allowed to CREATE DATABASE, with no
/// Database key — so CI can point at its own instance; otherwise the local
/// server and the same development role docs/POSTGRESQL_SETUP.md creates.
/// </summary>
public sealed class TestDb : IDisposable
{
    public const string ServerOverrideVariable = "SIVAYAANHMS_TEST_PG";

    private const string DefaultServer =
        "Host=localhost;Port=5432;Username=sivayaanhms;Password=sivayaanhms-dev";

    // Lower-case, like every PostgreSQL identifier that should never need
    // quoting. Prefixed so the sweep command in the automation README can
    // find any that a crashed run left behind.
    private readonly string _database = $"sivayaanhms_test_{Guid.NewGuid():N}";
    private readonly string _server;

    public TestDb()
    {
        _server = Environment.GetEnvironmentVariable(ServerOverrideVariable) ?? DefaultServer;
    }

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_server) { Database = _database, ApplicationName = "SivayaanHMS-Tests" }.ConnectionString;

    /// <summary>The maintenance database every PostgreSQL server has, from
    /// which the throwaway one is created and dropped.</summary>
    private string MaintenanceConnectionString =>
        new NpgsqlConnectionStringBuilder(_server) { Database = "postgres", Pooling = false }.ConnectionString;

    public AppDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
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
        // Pooled connections keep the database in use and PostgreSQL refuses
        // to drop one with a session still attached. Clearing the pool first
        // is the equivalent of the ClearAllPools this class needed under
        // SQLite for the same reason; WITH (FORCE) below then evicts anything
        // a leaked context is still holding.
        NpgsqlConnection.ClearAllPools();

        try
        {
            using var maintenance = new NpgsqlConnection(MaintenanceConnectionString);
            maintenance.Open();

            using var drop = maintenance.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE);";
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
