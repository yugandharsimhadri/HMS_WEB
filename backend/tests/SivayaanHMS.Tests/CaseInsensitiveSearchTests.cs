using Microsoft.Extensions.Logging.Abstractions;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Tests;

/// <summary>
/// Search must not care about case, and must not care because the query says
/// so rather than because the server happens to be installed with a
/// case-insensitive collation.
///
/// Every database here is created CS_AS on purpose. On the ordinary CI_AS
/// default these tests pass whether or not the query folds case, which makes
/// them worthless as a guard: the failure they are meant to catch only
/// appears on a clinic's server, months later, when a receptionist types a
/// name in lower case and is told nobody by that name is registered.
/// </summary>
public class CaseInsensitiveSearchTests
{
    private const string CaseSensitive = "SQL_Latin1_General_CP1_CS_AS";

    private static TestDb CaseSensitiveDb() => new(CaseSensitive);

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
