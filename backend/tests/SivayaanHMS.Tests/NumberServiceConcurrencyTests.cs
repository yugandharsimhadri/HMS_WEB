using SivayaanHMS.Data;

namespace SivayaanHMS.Tests;

/// <summary>
/// SAAS_MIGRATION.md finding 1, made concrete: the desktop's NumberService
/// read Counter.LastNumber, incremented it in memory, and saved — two
/// simultaneous callers could both read 41 and both write 42. This fires a
/// real burst of concurrent callers at the ported, atomic version and
/// checks the one property that actually matters: never the same number
/// twice, for the same tenant and the same counter.
/// </summary>
public class NumberServiceConcurrencyTests
{
    [Fact]
    public async Task Concurrent_callers_never_receive_the_same_number()
    {
        using var testDb = new TestDb();
        await testDb.MigrateAsync();

        var tenant = Guid.NewGuid();
        const int callers = 40;

        var numbers = await Task.WhenAll(Enumerable.Range(0, callers).Select(async _ =>
        {
            await using var db = testDb.CreateContext(tenant);
            return await NumberService.NextAsync(db, NumberService.Bill);
        }));

        Assert.Equal(callers, numbers.Distinct().Count());
    }

    [Fact]
    public async Task Two_tenants_can_both_hold_the_same_number()
    {
        using var testDb = new TestDb();
        await testDb.MigrateAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var dbA = testDb.CreateContext(tenantA);
        await using var dbB = testDb.CreateContext(tenantB);

        var firstForA = await NumberService.NextAsync(dbA, NumberService.Bill);
        var firstForB = await NumberService.NextAsync(dbB, NumberService.Bill);

        // Both clinics' first invoice is INV00001 — a global counter would
        // instead hand tenant B "INV00002", which is what the plan's
        // "numbering prefixes" note (SAAS_MIGRATION.md) flags as confusing
        // the moment anyone compares paperwork across clinics.
        Assert.Equal(firstForA, firstForB);
    }
}
