using Microsoft.Extensions.Logging.Abstractions;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Tests;

/// <summary>
/// One drug in several strengths — Cetirizine as 5 mg, 10 mg, two syrups and
/// drops — which is the ordinary case, not an edge one.
///
/// Two things have to hold. The strengths must all be able to exist at once,
/// and they must come back in the order a person expects. Sorting on the text
/// alone puts "10 mg" above "5 mg" because "1" sorts before "5", which puts
/// the adult dose at the top of the list a counter picks a child's dose from.
/// </summary>
public class MedicineVariantTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Theory]
    [InlineData("5 mg", 5)]
    [InlineData("10 mg", 10)]
    [InlineData("2.5 mg", 2.5)]
    [InlineData("100 ml", 100)]
    [InlineData("250mg/5ml", 250)]     // the first figure is the dose
    [InlineData("  15 MG  ", 15)]
    public void Reads_the_dose_out_of_a_strength(string strength, decimal expected)
        => Assert.Equal(expected, StrengthParser.Value(strength));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("as directed")]
    public void A_strength_with_no_number_has_no_value(string? strength)
        => Assert.Null(StrengthParser.Value(strength));

    [Fact]
    public async Task Five_strengths_of_one_drug_coexist_and_sort_by_dose()
    {
        using var testDb = new TestDb();
        await testDb.MigrateAsync();

        var pharmacy = new PharmacyService(
            testDb.CreateFactory(Tenant), new SystemClock(), NullLogger<PharmacyService>.Instance);

        // Deliberately entered out of order, and all under one brand name —
        // only the strength tells them apart. If strength were not part of the
        // duplicate key, the second of these would be refused as a duplicate.
        foreach (var (strength, pack, unit) in new[]
                 {
                     ("200 ml", "200 ML", DispensingUnit.Syrup),
                     ("10 mg", "10 TAB", DispensingUnit.Tablet),
                     ("2.5 mg/ml", "15 ML", DispensingUnit.Bottle),
                     ("100 ml", "100 ML", DispensingUnit.Syrup),
                     ("5 mg", "10 TAB", DispensingUnit.Tablet),
                 })
        {
            await pharmacy.SaveProductAsync(new Product
            {
                Name = "Cetzine",
                GenericName = "Cetirizine",
                Manufacturer = "Dr Reddys",
                Strength = strength,
                PackSize = pack,
                DispensingUnit = unit,
                UnitsPerPack = 10,
                HsnCode = "3004",
                IsActive = true,
            });
        }

        var found = await pharmacy.SearchProductsAsync("cetzine");

        Assert.Equal(5, found.Count);

        // The order that matters: the child's 5 mg ahead of the adult 10 mg.
        Assert.Equal(
            new[] { "2.5 mg/ml", "5 mg", "10 mg", "100 ml", "200 ml" },
            found.Select(p => p.Strength).ToArray());
    }

    [Fact]
    public async Task The_same_strength_twice_is_still_a_duplicate()
    {
        using var testDb = new TestDb();
        await testDb.MigrateAsync();

        var pharmacy = new PharmacyService(
            testDb.CreateFactory(Tenant), new SystemClock(), NullLogger<PharmacyService>.Instance);

        Product Row() => new()
        {
            Name = "Cetzine", Manufacturer = "Dr Reddys", Strength = "5 mg",
            PackSize = "10 TAB", UnitsPerPack = 10, HsnCode = "3004", IsActive = true,
        };

        await pharmacy.SaveProductAsync(Row());

        // Widening the key by strength must not have widened it so far that a
        // real duplicate slips through — two records split the stock and both
        // show at the counter.
        await Assert.ThrowsAsync<DuplicateMedicineException>(
            () => pharmacy.SaveProductAsync(Row()));
    }
}
