using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>
/// Preloads the common procedures a dental clinic bills for, so Procedure
/// Master isn't empty on day one. Prices are reasonable defaults, editable
/// afterward from the Procedure Master screen — this never overwrites a row
/// that's already there, so a price someone has since changed survives
/// every later run.
/// </summary>
public static class DentistProcedureSeeder
{
    private static readonly (string Name, string Category, decimal Price)[] Defaults =
    [
        ("Consultation", "Diagnostic", 200),
        ("X-Ray (IOPA)", "Diagnostic", 150),

        ("Scaling & Polishing", "Preventive", 800),
        ("Fluoride Treatment", "Preventive", 500),

        ("Composite Filling", "Restorative", 1000),
        ("Amalgam Filling", "Restorative", 600),
        ("Dental Crown (Metal)", "Restorative", 4000),
        ("Dental Crown (Ceramic)", "Restorative", 6000),
        ("Denture (Partial)", "Restorative", 8000),
        ("Denture (Complete)", "Restorative", 15000),

        ("Root Canal Treatment (RCT)", "Endodontic", 3500),

        ("Gum Treatment (Periodontal)", "Periodontal", 2000),

        ("Teeth Whitening", "Cosmetic", 5000),

        ("Tooth Extraction (Simple)", "Minor surgery", 500),
        ("Tooth Extraction (Surgical)", "Minor surgery", 1500),
        ("Wisdom Tooth Removal", "Minor surgery", 2500),
        ("Dental Implant", "Minor surgery", 25000),

        ("Braces Consultation", "Orthodontic", 300),

        ("Pulpotomy (Children)", "Pediatric", 1500),
        ("Space Maintainer", "Pediatric", 2000)
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
        if (await db.Procedures.AnyAsync(p => p.Name == name && p.Department == ProcedureDepartment.Dentist, ct)) return;

        db.Procedures.Add(new Procedure
        {
            Name = name, Category = category, Department = ProcedureDepartment.Dentist, Price = price
        });
    }
}
