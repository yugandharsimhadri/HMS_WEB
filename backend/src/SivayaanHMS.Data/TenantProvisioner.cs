using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SivayaanHMS.Core;
using SivayaanHMS.Data.Import;

namespace SivayaanHMS.Data;

/// <summary>
/// What runs once, the moment a new clinic registers: the master-data
/// seeders (each idempotent, so re-running them is harmless), a starter
/// medicine catalogue, one starter doctor, and the default Admin account —
/// so a clinic is genuinely usable within seconds of signing up rather than
/// staring at an empty system.
///
/// This is the direct replacement for the desktop's DbBootstrapper, minus
/// everything that only made sense for one file on one clinic PC: resolving
/// a local database path, carrying an old file over under a previous
/// product name, and rotating dated file-copy backups on the C: drive. On
/// the web edition, "the database" is one shared table set for every
/// tenant and backups/restore are the vendor's server-side job, not
/// something modelled here — see SAAS_MIGRATION.md's Phase 2 notes.
///
/// Call <see cref="ProvisionAsync"/> with a db already scoped to the new
/// tenant (its AppDbContext built with that tenant's id on
/// ICurrentTenantContext) — every row this seeds is stamped with that
/// tenant automatically, the same as any other save.
/// </summary>
public static class TenantProvisioner
{
    /// <summary>Returns the admin account it seeded, so signup can hand the
    /// person the username they are about to sign in with.</summary>
    public static async Task<User> ProvisionAsync(
        AppDbContext db, string clinicSlug, ILogger logger,
        string? adminLocalPart = null, CancellationToken ct = default)
    {
        await SeedStarterDataAsync(db);
        await ImportProfileSeeder.SeedAsync(db, ct);
        await DiagnosticTestSeeder.SeedAsync(db, ct);
        await VaccineMasterSeeder.SeedAsync(db, ct);
        await DentistProcedureSeeder.SeedAsync(db, ct);
        await PathologyLabSeeder.SeedAsync(db, ct);
        var admin = await UserSeeder.SeedAsync(db, clinicSlug, logger, adminLocalPart, ct);

        logger.LogInformation("Tenant {TenantId} provisioned.", db.TenantId);
        return admin;
    }

    private static async Task SeedStarterDataAsync(AppDbContext db)
    {
        if (!await db.Doctors.AnyAsync())
        {
            db.Doctors.Add(new Doctor
            {
                Name = "Dr. A. Kumar",
                Speciality = "Paediatrics",
                RegistrationNo = "REG-00000",
                ConsultationFee = 300m,
                IsActive = true
            });
        }

        if (!await db.Products.AnyAsync())
        {
            var catalogue = StarterCatalogue();

            // The catalogue is keyed on brand, maker and pack, and a unique index
            // enforces it. Seeding is a way into the catalogue like any other, so
            // it sets the key — without it all six collide on an empty one and
            // the tenant cannot open its own catalogue.
            foreach (var product in catalogue) product.SearchKey = product.BuildKey();

            db.Products.AddRange(catalogue);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A handful of everyday drugs so the counter is usable on first launch.
    ///
    /// UnitsPerPack is stated on every one of them. Leaving it at its default of
    /// 1 while the pack size says "15 TAB" makes the shop sell whole strips to
    /// anyone who asks for tablets, at fifteen times the price, and nothing
    /// anywhere reports an error.
    /// </summary>
    public static Product[] StarterCatalogue() =>
    [
        new Product { Name = "Paracetamol 500mg", GenericName = "Paracetamol", Manufacturer = "Generic",
                      PackSize = "15 TAB", UnitsPerPack = 15, AllowLooseSale = true,
                      GstRate = 12m, RackLocation = "A1", ReorderLevel = 100 },

        new Product { Name = "Amoxicillin 500mg", GenericName = "Amoxicillin", Manufacturer = "Generic",
                      PackSize = "10 CAP", UnitsPerPack = 10, AllowLooseSale = true,
                      DispensingUnit = DispensingUnit.Capsule,
                      GstRate = 12m, Schedule = DrugSchedule.H, RackLocation = "A2", ReorderLevel = 50 },

        new Product { Name = "Cetirizine 10mg", GenericName = "Cetirizine", Manufacturer = "Generic",
                      PackSize = "10 TAB", UnitsPerPack = 10, AllowLooseSale = true,
                      GstRate = 12m, RackLocation = "B1", ReorderLevel = 50 },

        new Product { Name = "Pantoprazole 40mg", GenericName = "Pantoprazole", Manufacturer = "Generic",
                      PackSize = "15 TAB", UnitsPerPack = 15, AllowLooseSale = true,
                      GstRate = 12m, RackLocation = "B2", ReorderLevel = 50 },

        // A sachet and a bottle really are one unit each — half of either is
        // not something a shop can hand over.
        new Product { Name = "ORS Powder", GenericName = "Oral rehydration salts", Manufacturer = "Generic",
                      PackSize = "21.8 G", UnitsPerPack = 1, AllowLooseSale = false,
                      DispensingUnit = DispensingUnit.Sachet,
                      GstRate = 5m, RackLocation = "C1", ReorderLevel = 30 },

        new Product { Name = "Cough Syrup 100ml", GenericName = "Dextromethorphan", Manufacturer = "Generic",
                      PackSize = "100 ML", UnitsPerPack = 1, AllowLooseSale = false,
                      DispensingUnit = DispensingUnit.Bottle,
                      GstRate = 12m, RackLocation = "C2", ReorderLevel = 20 }
    ];
}
