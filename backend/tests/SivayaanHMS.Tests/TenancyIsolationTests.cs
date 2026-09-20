using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Tests;

/// <summary>
/// SAAS_MIGRATION.md decision 1: a shared database with a global query
/// filter is only as safe as "every entity actually has the filter." These
/// tests exist to make that a fact the suite checks, not an assumption.
/// </summary>
public class TenancyIsolationTests
{
    [Fact]
    public void Every_BaseEntity_type_has_a_query_filter()
    {
        // Never opened — this test reads the model EF builds, not a
        // database, so the connection string only has to name a provider.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=nobody;Password=none")
            .Options;

        using var db = new AppDbContext(options);
        var model = db.Model;

        var missing = model.GetEntityTypes()
            .Where(t => typeof(BaseEntity).IsAssignableFrom(t.ClrType))
            .Where(t => t.GetDeclaredQueryFilters().Count == 0)
            .Select(t => t.ClrType.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            $"These BaseEntity types have no tenant/soft-delete query filter: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task A_patient_created_for_one_tenant_is_invisible_to_another()
    {
        using var testDb = new TestDb();
        await testDb.MigrateAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var db = testDb.CreateContext(tenantA))
        {
            db.Patients.Add(new Patient { Name = "Aarav Rao", PatientNo = "P00001" });
            await db.SaveChangesAsync();
        }

        await using (var asTenantA = testDb.CreateContext(tenantA))
        {
            Assert.Equal(1, await asTenantA.Patients.CountAsync());
        }

        await using (var asTenantB = testDb.CreateContext(tenantB))
        {
            Assert.Equal(0, await asTenantB.Patients.CountAsync());
        }
    }

    [Fact]
    public async Task New_rows_are_stamped_with_the_context_tenant_even_if_the_caller_set_a_different_one()
    {
        using var testDb = new TestDb();
        await testDb.MigrateAsync();

        var realTenant = Guid.NewGuid();
        var spoofedTenant = Guid.NewGuid();

        await using var db = testDb.CreateContext(realTenant);
        db.Patients.Add(new Patient { Name = "Someone", PatientNo = "P00002", TenantId = spoofedTenant });
        await db.SaveChangesAsync();

        var saved = await db.Patients.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(realTenant, saved.TenantId);
    }

    [Fact]
    public async Task Soft_deleted_rows_are_excluded_by_the_same_filter()
    {
        using var testDb = new TestDb();
        await testDb.MigrateAsync();

        var tenant = Guid.NewGuid();

        await using (var db = testDb.CreateContext(tenant))
        {
            db.Patients.Add(new Patient { Name = "Deleted Later", PatientNo = "P00003", IsDeleted = true });
            await db.SaveChangesAsync();
        }

        await using var readBack = testDb.CreateContext(tenant);
        Assert.Equal(0, await readBack.Patients.CountAsync());
        Assert.Equal(1, await readBack.Patients.IgnoreQueryFilters().CountAsync());
    }
}
