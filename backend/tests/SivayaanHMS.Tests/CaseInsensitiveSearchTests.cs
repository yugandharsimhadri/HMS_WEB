using Microsoft.Extensions.Logging.Abstractions;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Tests;

/// <summary>
/// Search must not care about case, and must not care because the query says
/// so rather than because the server happens to be installed with a
/// case-insensitive collation.
///
/// On SQL Server every database here had to be created with a case-sensitive
/// collation on purpose: on the ordinary CI_AS default these tests passed
/// whether or not the query folded case, which made them worthless as a
/// guard. PostgreSQL's default collation compares and matches case-sensitively
/// — "bhavya" does not LIKE "Bhavya" unless the query lowers both sides — so
/// an ordinary database is already the strict one, and there is nothing to
/// force. The failure these tests exist to catch is the same: a receptionist
/// typing a name in lower case and being told nobody by that name is
/// registered.
/// </summary>
public class CaseInsensitiveSearchTests
{
    private static TestDb CaseSensitiveDb() => new();

    [Theory]
    [InlineData("bhavya")]
    [InlineData("BHAVYA")]
    [InlineData("BhAvYa")]
    [InlineData("reddy")]
    [InlineData("REDDY")]
    public async Task A_patient_is_found_whatever_case_the_name_is_typed_in(string term)
    {
        using var testDb = CaseSensitiveDb();
        await testDb.MigrateAsync();
        var tenant = Guid.NewGuid();

        await using (var db = testDb.CreateContext(tenant))
        {
            db.Patients.Add(new Patient { Name = "Bhavya Reddy", PatientNo = "P00001", Phone = "9876500011" });
            await db.SaveChangesAsync();
        }

        var opd = new OpdService(
            testDb.CreateFactory(tenant), new SystemClock(), NullLogger<OpdService>.Instance);
        var found = await opd.SearchPatientsAsync(term);

        Assert.Single(found);
        Assert.Equal("Bhavya Reddy", found[0].Name);
    }

    [Theory]
    [InlineData("p00001")]
    [InlineData("P00001")]
    public async Task A_patient_number_is_found_in_either_case(string term)
    {
        using var testDb = CaseSensitiveDb();
        await testDb.MigrateAsync();
        var tenant = Guid.NewGuid();

        await using (var db = testDb.CreateContext(tenant))
        {
            db.Patients.Add(new Patient { Name = "Bhavya Reddy", PatientNo = "P00001", Phone = "9876500011" });
            await db.SaveChangesAsync();
        }

        var opd = new OpdService(
            testDb.CreateFactory(tenant), new SystemClock(), NullLogger<OpdService>.Instance);
        Assert.Single(await opd.SearchPatientsAsync(term));
    }

    [Theory]
    [InlineData("cetirizine")]
    [InlineData("CETIRIZINE")]
    [InlineData("cipla")]
    [InlineData("CIPLA")]
    public async Task A_medicine_is_found_whatever_case_the_counter_types(string term)
    {
        using var testDb = CaseSensitiveDb();
        await testDb.MigrateAsync();
        var tenant = Guid.NewGuid();

        await using (var db = testDb.CreateContext(tenant))
        {
            db.Products.Add(new Product
            {
                Name = "Cetirizine",
                GenericName = "Cetirizine Hydrochloride",
                Manufacturer = "Cipla",
                Strength = "10mg",
            });
            await db.SaveChangesAsync();
        }

        var pharmacy = new PharmacyService(
            testDb.CreateFactory(tenant), new SystemClock(), NullLogger<PharmacyService>.Instance);
        var found = await pharmacy.SearchProductsAsync(term);

        Assert.Single(found);
    }
}
