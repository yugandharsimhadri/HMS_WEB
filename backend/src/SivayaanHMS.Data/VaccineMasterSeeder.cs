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
    public const string UipCategory = "Universal Immunization Programme";
    public const string OptionalCategory = "Optional";
    public const string IapCategory = "IAP recommended";

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

    /// <summary>
    /// The vaccines the Indian Academy of Pediatrics recommends that the
    /// government's UIP schedule above does not carry at all. A private
    /// paediatric practice generally follows IAP; UIP is the public minimum.
    ///
    /// Deliberately **additive, not a replacement**. Every row here is a
    /// vaccine with no UIP counterpart, so loading it can never put two
    /// doses of the same protection on one child's card. That is why MMR is
    /// absent even though IAP schedules it: "Measles / MR" above already
    /// covers measles at the same ages, and listing both would invite a
    /// double dose. A clinic that prefers IAP's MMR can rename that row or
    /// add its own from Masters → Vaccines.
    ///
    /// Same caveat as above, and it matters more here: this is a snapshot of
    /// published guidance, not a live feed. Check it against the current
    /// IAP/ACVIP schedule before relying on it, and edit freely afterwards.
    /// </summary>
    private static readonly (string Name, int DoseNumber, int RecommendedAgeDays, string Category, int SequenceOrder)[] IapAdditions =
    [
        // Influenza: two doses the first year, four weeks apart, then annual.
        // Only the first course is scheduled — an annual booster has no fixed
        // age to sit at, so it is given as needed rather than modelled here.
        ("Influenza", 1, 180, IapCategory, 40),
        ("Influenza", 2, 208, IapCategory, 41),

        ("Hepatitis A", 1, 365, IapCategory, 42),
        ("Hepatitis A", 2, 547, IapCategory, 43),

        ("Varicella", 1, 456, IapCategory, 44),
        ("Varicella", 2, 1825, IapCategory, 45),

        // Girls, 9–14 years, two doses six months apart.
        ("HPV", 1, 3285, IapCategory, 46),
        ("HPV", 2, 3467, IapCategory, 47)
    ];

    /// <summary>Seeds the schedule every new clinic starts with.</summary>
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
        => await SeedAsync(db, Defaults, ct);

    /// <summary>
    /// Adds the IAP-recommended vaccines on top of whatever the clinic
    /// already has. Idempotent, like every seeder here: a row already
    /// present by name and dose number is left exactly as it is, so a clinic
    /// that has edited an age or a category keeps its version.
    ///
    /// Separate from provisioning on purpose. A clinic running to the
    /// government schedule should not silently acquire eight vaccines it does
    /// not stock and cannot give; loading IAP is a decision its own admin
    /// makes, from Masters → Vaccines.
    /// </summary>
    public static async Task<int> SeedIapAdditionsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var before = await db.VaccineMasters.CountAsync(ct);
        await SeedAsync(db, IapAdditions, ct);
        return await db.VaccineMasters.CountAsync(ct) - before;
    }

    private static async Task SeedAsync(
        AppDbContext db,
        (string Name, int DoseNumber, int RecommendedAgeDays, string Category, int SequenceOrder)[] rows,
        CancellationToken ct)
    {
        foreach (var (name, dose, ageDays, category, sequence) in rows)
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
