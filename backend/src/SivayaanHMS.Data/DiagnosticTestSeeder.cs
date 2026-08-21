using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>
/// Preloads the common tests a pediatric clinic bills for, so Test Master
/// isn't empty on day one. Prices are reasonable defaults, editable
/// afterward from the Test Master screen — this never overwrites a row
/// that's already there, so a price someone has since changed survives
/// every later run.
/// </summary>
public static class DiagnosticTestSeeder
{
    private static readonly (string Name, string Category, decimal Price)[] Defaults =
    [
        ("Complete Blood Picture (CBP/CBC)", "Hematology", 300),
        ("ESR", "Hematology", 150),

        ("Fasting Blood Glucose", "Biochemistry", 100),
        ("Post Prandial Blood Glucose", "Biochemistry", 100),
        ("Random Blood Sugar", "Biochemistry", 100),
        ("HbA1c", "Biochemistry", 500),
        ("CRP", "Biochemistry", 400),
        ("LFT", "Biochemistry", 600),
        ("KFT", "Biochemistry", 600),
        ("Lipid Profile", "Biochemistry", 600),

        ("Urine Routine", "Urine", 150),
        ("Urine Culture", "Urine", 500),

        ("Stool Routine", "Stool", 150),

        ("T3", "Thyroid", 250),
        ("T4", "Thyroid", 250),
        ("TSH", "Thyroid", 300),

        ("Dengue NS1", "Serology", 600),
        ("Dengue IgM", "Serology", 600),
        ("Malaria Parasite", "Serology", 200),
        ("Widal", "Serology", 200),
        ("Typhi Dot", "Serology", 500),

        ("Vitamin D", "Vitamins", 1200),
        ("Vitamin B12", "Vitamins", 1000)
    ];

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        foreach (var (name, category, price) in Defaults)
            await EnsureAsync(db, name, category, price, ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureAsync(
        AppDbContext db, string name, string category, decimal price, CancellationToken ct)
    {
        if (await db.DiagnosticTests.AnyAsync(t => t.Name == name, ct)) return;
        db.DiagnosticTests.Add(new DiagnosticTest { Name = name, Category = category, Price = price });
    }
}
