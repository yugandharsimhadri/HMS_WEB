using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>What is wrong with one medicine, and what putting it right would do.</summary>
public class HealthFinding
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public HealthProblem Problem { get; init; }

    /// <summary>What the record says now.</summary>
    public string Current { get; init; } = "";

    /// <summary>What it would say after the repair.</summary>
    public string Proposed { get; init; } = "";

    /// <summary>Plain words for someone who did not write the software.</summary>
    public string Explanation { get; init; } = "";

    // Only set for the pack-size problem, where stock is re-counted.
    public int UnitsPerPack { get; init; }
    public int QuantityBefore { get; init; }
    public int QuantityAfter { get; init; }
    public DispensingUnit InferredUnit { get; init; }

    public bool ChangesStock => QuantityAfter != QuantityBefore;

    /// <summary>Whether repairing this can be done without a human deciding anything.</summary>
    public bool CanRepairAutomatically => Problem != HealthProblem.Duplicate;
}

public enum HealthProblem
{
    /// <summary>Pack size states a count that units-per-pack disagrees with.</summary>
    PackSizeDisagrees = 1,

    /// <summary>Batches were received under a different pack size than the medicine now says.</summary>
    BatchPackDisagrees,

    /// <summary>Dispensing unit was never set, so quantities read as "units".</summary>
    UnitNotSet,

    /// <summary>Two medicines that are the same thing. Needs a human to choose.</summary>
    Duplicate
}

/// <summary>
/// Finds medicines whose records cannot be right, and repairs them together.
///
/// This exists because the damage is silent. A medicine whose pack size says
/// "15 TAB" while units-per-pack says 1 sells whole strips to anyone asking for
/// tablets, at fifteen times the price, and nothing anywhere reports an error.
/// Fixing them one at a time is not a fix when a shop has two hundred.
/// </summary>
public class DataHealthService(IDbContextFactory<AppDbContext> factory, PharmacyService pharmacy, ILogger<DataHealthService> logger)
{
    /// <summary>
    /// A quick look at whether anything is obviously wrong, run at startup.
    ///
    /// Cheap enough to run every day, and it turns silent damage into a sentence
    /// on the day it happens rather than a discrepancy at the annual stock take.
    /// Returns null when there is nothing to say.
    /// </summary>
    public async Task<string?> DailyCheckAsync()
    {
        var findings = await ScanAsync();
        if (findings.Count == 0) return null;

        var pack = findings.Count(f => f.Problem is HealthProblem.PackSizeDisagrees
                                                 or HealthProblem.BatchPackDisagrees);
        var duplicates = findings.Count(f => f.Problem == HealthProblem.Duplicate);
        var units = findings.Count(f => f.Problem == HealthProblem.UnitNotSet);

        var parts = new List<string>();

        if (pack > 0)
            parts.Add($"{pack} medicine(s) whose pack size and units-per-pack disagree — " +
                      $"those are being sold by the pack to anyone asking for singles");

        if (duplicates > 0) parts.Add($"{duplicates} duplicate record(s)");
        if (units > 0) parts.Add($"{units} with no dispensing unit set");

        var summary = string.Join(", ", parts);

        logger.LogWarning("Data health: {Summary}.", summary);

        return summary;
    }

    public async Task<List<HealthFinding>> ScanAsync()
    {
        await using var db = await factory.CreateDbContextAsync();

        var products = await db.Products
            .Include(p => p.Batches)
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.Name)
            .ToListAsync();

        var findings = new List<HealthFinding>();

        foreach (var product in products)
        {
            AddPackFinding(findings, product);
            AddUnitFinding(findings, product);
        }

        AddDuplicateFindings(findings, products);

        return findings;
    }

    /// <summary>
    /// "15 TAB" and one-unit-per-pack cannot both be true. The pack size is what
    /// is printed on the box, so it wins.
    /// </summary>
    private static void AddPackFinding(List<HealthFinding> findings, Product product)
    {
        var stated = PackMath.UnitsFromPacking(product.PackSize);
        var live = product.Batches.Where(b => !b.IsDeleted).ToList();

        if (stated is { } n && n != product.UnitsPerPack)
        {
            var before = live.Sum(b => b.QtyOnHand);
            var after = live.Sum(b => b.QtyOnHand / Math.Max(1, b.UnitsPerPack) * n);

            findings.Add(new HealthFinding
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Problem = HealthProblem.PackSizeDisagrees,
                Current = $"pack says {n}, medicine says {product.UnitsPerPack} per pack",
                Proposed = $"{n} per pack",
                UnitsPerPack = n,
                QuantityBefore = before,
                QuantityAfter = after,
                Explanation = before == after
                    ? $"The counter is selling whole packs to anyone asking for " +
                      $"{product.DispensingUnit.Name(2)}."
                    : $"The counter is selling whole packs at {n} times the price. " +
                      $"Stock is re-counted from {before} to {after} — the same packs on the shelf."
            });

            return;
        }

        // The medicine may be right while stock received earlier is not.
        var stale = live.Where(b => b.UnitsPerPack != product.UnitsPerPack).ToList();
        if (stale.Count == 0) return;

        var staleBefore = stale.Sum(b => b.QtyOnHand);
        var staleAfter = stale.Sum(b => b.QtyOnHand / Math.Max(1, b.UnitsPerPack) * product.UnitsPerPack);

        findings.Add(new HealthFinding
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Problem = HealthProblem.BatchPackDisagrees,
            Current = $"{stale.Count} batch(es) at a different pack size",
            Proposed = $"{product.UnitsPerPack} per pack",
            UnitsPerPack = product.UnitsPerPack,
            QuantityBefore = staleBefore,
            QuantityAfter = staleAfter,
            Explanation = $"Stock received before the medicine was corrected is still " +
                          $"sold by the pack. Re-counting brings it in line."
        });
    }

    /// <summary>
    /// Rows written before the dispensing unit existed hold nothing, so every
    /// quantity on screen reads "units" instead of tablets or bottles.
    /// </summary>
    private static void AddUnitFinding(List<HealthFinding> findings, Product product)
    {
        if (Enum.IsDefined(product.DispensingUnit)) return;

        var inferred = InferUnit(product.PackSize);

        findings.Add(new HealthFinding
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Problem = HealthProblem.UnitNotSet,
            Current = "not set",
            Proposed = inferred.Name(1),
            InferredUnit = inferred,
            Explanation = $"Quantities read as \"units\" instead of {inferred.Name(2)}."
        });
    }

    /// <summary>Taken from what is printed on the pack: "10 TAB", "100 ML", "21.8 G".</summary>
    public static DispensingUnit InferUnit(string? packSize)
    {
        var text = (packSize ?? "").ToUpperInvariant();

        if (text.Contains("CAP")) return DispensingUnit.Capsule;
        if (text.Contains("TAB")) return DispensingUnit.Tablet;
        if (text.Contains("ML")) return DispensingUnit.Bottle;
        if (text.Contains("VIAL")) return DispensingUnit.Vial;
        if (text.Contains("GM") || text.Contains(" G")) return DispensingUnit.Sachet;

        return DispensingUnit.Tablet;
    }

    /// <summary>
    /// Same name, same maker, same pack is the same medicine twice. Reported but
    /// never repaired automatically — which one survives is a decision, and the
    /// wrong choice loses stock.
    /// </summary>
    private static void AddDuplicateFindings(List<HealthFinding> findings, List<Product> products)
    {
        var groups = products
            .GroupBy(Key)
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            foreach (var product in group.OrderByDescending(p => p.StockOnHand).Skip(1))
            {
                var keeper = group.OrderByDescending(p => p.StockOnHand).First();

                findings.Add(new HealthFinding
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    Problem = HealthProblem.Duplicate,
                    Current = $"{group.Count()} records of this medicine",
                    Proposed = $"merge into the one holding {keeper.StockOnHand}",
                    Explanation = "It appears twice at the counter, and stock is split " +
                                  "between them. Merging has to be done by hand."
                });
            }
        }
    }

    /// <summary>Trimmed, case-folded, whitespace-collapsed — so near-misses collide.</summary>
    public static string Key(Product product)
    {
        static string Norm(string? value) =>
            string.Join(' ', (value ?? "").Trim().ToLowerInvariant()
                                          .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return $"{Norm(product.Name)}|{Norm(product.Manufacturer)}|{Norm(product.PackSize)}";
    }

    /// <summary>
    /// Applies the chosen repairs. Each medicine is done on its own so one
    /// failure cannot take the rest with it, and anything that moves a stock
    /// figure writes its own audit row.
    /// </summary>
    public async Task<int> RepairAsync(IEnumerable<HealthFinding> findings, string? by = null)
    {
        var repaired = 0;

        foreach (var finding in findings.Where(f => f.CanRepairAutomatically))
        {
            switch (finding.Problem)
            {
                case HealthProblem.PackSizeDisagrees:
                case HealthProblem.BatchPackDisagrees:
                    // Re-counts the batches and writes an adjustment for each.
                    await pharmacy.RepackAsync(finding.ProductId, finding.UnitsPerPack, by);
                    break;

                case HealthProblem.UnitNotSet:
                    await SetUnitAsync(finding.ProductId, finding.InferredUnit);
                    break;
            }

            repaired++;
        }

        return repaired;
    }

    /// <summary>
    /// Which record a duplicate should be folded into — the one of its group
    /// holding the most stock, so the least has to move.
    /// </summary>
    public async Task<Guid?> SurvivorForAsync(Guid duplicateId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var duplicate = await db.Products.Include(p => p.Batches)
                                         .FirstOrDefaultAsync(p => p.Id == duplicateId);
        if (duplicate is null) return null;

        var key = Key(duplicate);

        var group = await db.Products.Include(p => p.Batches)
                                     .Where(p => !p.IsDeleted && p.Id != duplicateId)
                                     .ToListAsync();

        return group.Where(p => Key(p) == key)
                    .OrderByDescending(p => p.StockOnHand)
                    .FirstOrDefault()?.Id;
    }

    /// <summary>
    /// Folds one medicine record into another: batches, purchase lines, sold
    /// lines, prescriptions, corrections and vendor codes all move across, and
    /// the emptied record is retired.
    ///
    /// Everything is moved rather than deleted. A duplicate usually holds real
    /// stock and real history, and losing either is worse than the duplicate.
    /// </summary>
    public async Task<string> MergeAsync(Guid survivorId, Guid duplicateId, string? by = null)
    {
        if (survivorId == duplicateId)
            throw new InvalidOperationException("A medicine cannot be merged into itself.");

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var survivor = await db.Products.Include(p => p.Batches).FirstOrDefaultAsync(p => p.Id == survivorId)
                       ?? throw new InvalidOperationException("The medicine to keep no longer exists.");

        var duplicate = await db.Products.Include(p => p.Batches).FirstOrDefaultAsync(p => p.Id == duplicateId)
                        ?? throw new InvalidOperationException("The duplicate no longer exists.");

        var moved = duplicate.Batches.Count(b => !b.IsDeleted);
        var stock = duplicate.Batches.Where(b => !b.IsDeleted).Sum(b => b.QtyOnHand);

        foreach (var batch in await db.Batches.Where(b => b.ProductId == duplicateId).ToListAsync())
            batch.ProductId = survivorId;

        foreach (var item in await db.StockEntryItems.Where(i => i.ProductId == duplicateId).ToListAsync())
            item.ProductId = survivorId;

        foreach (var item in await db.SaleItems.Where(i => i.ProductId == duplicateId).ToListAsync())
            item.ProductId = survivorId;

        foreach (var item in await db.PrescriptionItems.Where(i => i.ProductId == duplicateId).ToListAsync())
            item.ProductId = survivorId;

        foreach (var item in await db.StockAdjustments.Where(a => a.ProductId == duplicateId).ToListAsync())
            item.ProductId = survivorId;

        // A vendor code can only point at one medicine, so drop any that would
        // collide with one the survivor already has.
        var survivorCodes = await db.VendorProductCodes
            .Where(c => c.ProductId == survivorId)
            .Select(c => c.VendorProfile + "|" + c.Code)
            .ToListAsync();

        foreach (var code in await db.VendorProductCodes.Where(c => c.ProductId == duplicateId).ToListAsync())
        {
            if (survivorCodes.Contains(code.VendorProfile + "|" + code.Code)) db.VendorProductCodes.Remove(code);
            else code.ProductId = survivorId;
        }

        // Retired, not destroyed, and its key freed so the name can be reused.
        duplicate.IsDeleted = true;
        duplicate.IsActive = false;
        duplicate.SearchKey = $"merged:{duplicate.Id}";

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        var summary = $"{duplicate.Name} folded into {survivor.Name}: " +
                      $"{moved} batch(es), {stock} unit(s) moved across.";

        logger.LogInformation("Merged {DuplicateId} into {SurvivorId}. {Summary} (by {By})",
            duplicate.Id, survivor.Id, summary, by ?? "unknown");

        return summary;
    }

    private async Task SetUnitAsync(Guid productId, DispensingUnit unit)
    {
        await using var db = await factory.CreateDbContextAsync();

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product is null) return;

        product.DispensingUnit = unit;
        await db.SaveChangesAsync();

        logger.LogInformation("Dispensing unit set: {Product} → {Unit}.", product.Name, unit);
    }
}
