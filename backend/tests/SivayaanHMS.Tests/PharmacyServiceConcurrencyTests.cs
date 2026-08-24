using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Tests;

/// <summary>
/// SAAS_MIGRATION.md finding 2, made concrete: the desktop's stock deduction
/// read Batch.QtyOnHand, checked it, and wrote the reduced figure — two
/// counters selling the last strip at once could both pass the check and
/// drive stock negative. This buys more of a ten-unit batch than exists,
/// from many callers at once, and checks the one property that actually
/// matters: stock never goes negative, and exactly as many sales succeed as
/// there was stock to cover.
/// </summary>
public class PharmacyServiceConcurrencyTests
{
    [Fact]
    public async Task Concurrent_sales_of_the_last_units_of_a_batch_never_oversell()
    {
        using var testDb = new TestDb();
        await testDb.MigrateAsync();

        var tenant = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        const int startingStock = 10;
        const int callers = 15;

        await using (var seed = testDb.CreateContext(tenant))
        {
            seed.Products.Add(new Product
            {
                Id = productId, Name = "Test Medicine", PackSize = "10 TAB",
                UnitsPerPack = 10, AllowLooseSale = true, SearchKey = "test-medicine|generic|10 tab"
            });
            seed.Batches.Add(new Batch
            {
                Id = batchId, ProductId = productId, BatchNo = "B1",
                ExpiryDate = DateTime.Today.AddYears(1), Mrp = 10m, UnitsPerPack = 10,
                QtyOnHand = startingStock
            });
            await seed.SaveChangesAsync();
        }

        var factory = testDb.CreateFactory(tenant);
        var clock = new SystemClock();
        var logger = NullLogger<PharmacyService>.Instance;

        var results = await Task.WhenAll(Enumerable.Range(0, callers).Select(async _ =>
        {
            var service = new PharmacyService(factory, clock, logger);
            var sale = new Sale { CustomerName = "Walk-in" };
            var lines = new List<SaleLine>
            {
                new()
                {
                    ProductId = productId, BatchId = batchId, ProductName = "Test Medicine",
                    BatchNo = "B1", ExpiryDate = DateTime.Today.AddYears(1), Quantity = 1,
                    UnitsPerPack = 10, Mrp = 10m, GstRate = 0m
                }
            };

            try
            {
                await service.SaveSaleAsync(sale, lines);
                return (Sold: true, Why: (string?)null);
            }
            catch (InvalidOperationException ex)
            {
                // The reason is kept, not swallowed. "Only 0 left" is the
                // refusal this test is about; anything else means the sale
                // failed for a reason the test was never checking, and a
                // bare count of failures cannot tell them apart.
                return (Sold: false, Why: ex.Message);
            }
        }));

        await using var verify = testDb.CreateContext(tenant);
        var finalStock = (await verify.Batches.FirstAsync(b => b.Id == batchId)).QtyOnHand;

        var sold = results.Count(r => r.Sold);
        var reasons = string.Join("; ", results.Where(r => !r.Sold)
                                               .Select(r => r.Why)
                                               .Distinct());

        Assert.True(finalStock >= 0, $"Stock went negative: {finalStock}");
        Assert.True(startingStock == sold,
            $"Expected {startingStock} sales to succeed, {sold} did. Refusals: {reasons}");
        Assert.Equal(startingStock - sold, finalStock);
    }
}
