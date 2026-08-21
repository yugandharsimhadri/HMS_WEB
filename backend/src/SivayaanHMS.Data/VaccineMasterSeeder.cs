using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>
/// Preloads the standard childhood immunization schedule — WHO's Expanded
/// Programme on Immunization, as adopted into India's Universal
/// Immunization Programme (UIP) — so Vaccine Master isn't empty on day one.
/// A snapshot, not a live feed: schedules are revised periodically by
/// national health authorities, so this should be checked against the
/// current official Government of India / WHO schedule rather than treated
/// as the last word. Ages and doses are editable afterward from the
/// Vaccine Master screen, and a clinic can add its own rows freely — this
/// never overwrites or re-inserts a row that's already there (matched by
/// name + dose number), so anything already edited or added survives every
/// later run.
/// </summary>
public static class VaccineMasterSeeder
{
    private static readonly (string Name, int DoseNumber, int RecommendedAgeDays, string Category, int SequenceOrder)[] Defaults =
    [
        ("BCG", 1, 0, "Universal Immunization Programme", 1),
        ("OPV", 0, 0, "Universal Immunization Programme", 2),
        ("Hepatitis B", 1, 0, "Universal Immunization Programme", 3),

        ("DTwP", 1, 42, "Universal Immunization Programme", 4),
        ("IPV", 1, 42, "Universal Immunization Programme", 5),
        ("Hepatitis B", 2, 42, "Universal Immunization Programme", 6),
        ("Hib", 1, 42, "Universal Immunization Programme", 7),
        ("Rotavirus", 1, 42, "Universal Immunization Programme", 8),
        ("PCV", 1, 42, "Universal Immunization Programme", 9),

        ("DTwP", 2, 70, "Universal Immunization Programme", 10),
        ("IPV", 2, 70, "Universal Immunization Programme", 11),
        ("Hib", 2, 70, "Universal Immunization Programme", 12),
        ("Rotavirus", 2, 70, "Universal Immunization Programme", 13),
        ("PCV", 2, 70, "Universal Immunization Programme", 14),

        ("DTwP", 3, 98, "Universal Immunization Programme", 15),
        ("IPV", 3, 98, "Universal Immunization Programme", 16),
        ("Hepatitis B", 3, 98, "Universal Immunization Programme", 17),
        ("Hib", 3, 98, "Universal Immunization Programme", 18),
        ("Rotavirus", 3, 98, "Universal Immunization Programme", 19),
        ("PCV", 3, 98, "Universal Immunization Programme", 20),

        ("Measles / MR", 1, 270, "Universal Immunization Programme", 21),
        ("Vitamin A", 1, 270, "Universal Immunization Programme", 22),
        ("PCV Booster", 1, 300, "Universal Immunization Programme", 23),

        ("DTwP Booster", 1, 480, "Universal Immunization Programme", 24),
        ("OPV Booster", 1, 480, "Universal Immunization Programme", 25),
        ("Measles / MR", 2, 480, "Universal Immunization Programme", 26),
        ("Vitamin A", 2, 480, "Universal Immunization Programme", 27),

        ("DTwP Booster", 2, 1825, "Universal Immunization Programme", 28),
        ("Td / Tdap", 1, 3650, "Universal Immunization Programme", 29),
        ("Td", 1, 5840, "Universal Immunization Programme", 30),

        ("Typhoid Conjugate Vaccine", 1, 270, "Optional", 31),
        ("Japanese Encephalitis", 1, 270, "Optional", 32),
        ("Japanese Encephalitis", 2, 480, "Optional", 33)
    ];

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        foreach (var (name, dose, ageDays, category, sequence) in Defaults)
            await EnsureAsync(db, name, dose, ageDays, category, sequence, ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureAsync(
        AppDbContext db, string name, int doseNumber, int recommendedAgeDays, string category, int sequenceOrder,
        CancellationToken ct)
    {
        if (await db.VaccineMasters.AnyAsync(v => v.Name == name && v.DoseNumber == doseNumber, ct)) return;

        db.VaccineMasters.Add(new VaccineMaster
        {
            Name = name, DoseNumber = doseNumber, RecommendedAgeDays = recommendedAgeDays,
            Category = category, SequenceOrder = sequenceOrder
        });
    }
}
