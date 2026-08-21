using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data.Import;

public enum MatchKind
{
    /// <summary>Matched by the vendor's own code — certain.</summary>
    ByVendorCode,

    /// <summary>Matched by name and pack — near certain.</summary>
    ByName,

    /// <summary>A likely match the user should confirm.</summary>
    NeedsChecking,

    /// <summary>Nothing matched; a new medicine will be created.</summary>
    New
}

/// <summary>One vendor line, resolved against the catalogue and ready to review.</summary>
public class ImportLine
{
    public required VendorBillLine Source { get; init; }
    public MatchKind Match { get; set; }
    public Product? Product { get; set; }

    /// <summary>Units in one pack. Parsed where the packing says so, else 1.</summary>
    public int UnitsPerPack { get; set; } = 1;

    /// <summary>Set when the packing text did not say, so the user can correct it.</summary>
    public bool UnitsAssumed { get; set; }

    public string ProductName => Product?.Name ?? Source.ProductName;
    public int UnitsReceived => (Source.Quantity + Source.FreeQuantity) * Math.Max(1, UnitsPerPack);

    public string Status => Match switch
    {
        MatchKind.ByVendorCode => "Matched",
        MatchKind.ByName => "Matched",
        MatchKind.NeedsChecking => "Check",
        _ => "New medicine"
    };
}

/// <summary>The whole bill, resolved. Nothing is written until Commit.</summary>
public class ImportPreview
{
    public required VendorBill Bill { get; init; }
    public required string ProfileName { get; init; }
    public required string FileName { get; init; }
    public string? SupplierName { get; set; }

    public List<ImportLine> Lines { get; } = [];
    public List<ImportIssue> Issues { get; } = [];

    public bool AlreadyImported { get; set; }
    public string? BlockedReason { get; set; }

    public bool CanImport => BlockedReason is null && !Bill.HasErrors && Lines.Count > 0;

    public int NewMedicines => Lines.Count(l => l.Match == MatchKind.New);
    public int NeedsChecking => Lines.Count(l => l.Match == MatchKind.NeedsChecking);
    public int TotalUnits => Lines.Sum(l => l.UnitsReceived);
}

public record ImportResult(string EntryNo, int Lines, int ProductsCreated, int UnitsAdded);

/// <summary>
/// Turns a parsed vendor bill into stock. Matching first so the user can see
/// what will happen, then one transaction that appends to the shelf.
/// </summary>
public class PurchaseImportService(IDbContextFactory<AppDbContext> factory, PharmacyService pharmacy, ILogger<PurchaseImportService> logger)
{
    public async Task<ImportPreview> PreviewAsync(VendorBill bill, ImportProfile profile, string fileName)
    {
        var preview = new ImportPreview
        {
            Bill = bill,
            ProfileName = profile.Name,
            FileName = fileName
        };

        preview.Issues.AddRange(bill.Issues);

        if (bill.HasErrors)
        {
            preview.BlockedReason = "The file could not be read. Fix the errors below, or pick a different profile.";
            return preview;
        }

        await using var db = await factory.CreateDbContextAsync();

        // Importing the same bill twice would double the stock, so this is checked
        // before anything else and blocks the whole import.
        var existing = await db.StockEntries
            .FirstOrDefaultAsync(s => s.SupplierInvoiceNo == bill.BillNo);

        if (existing is not null)
        {
            preview.AlreadyImported = true;
            preview.BlockedReason =
                $"Bill {bill.BillNo} was already received on {existing.EntryDate:dd MMM yyyy} as {existing.EntryNo}. " +
                "Importing it again would double the stock.";
        }

        var products = await db.Products.Where(p => !p.IsDeleted).ToListAsync();
        var codes = await db.VendorProductCodes
            .Where(c => c.VendorProfile == profile.Name)
            .ToDictionaryAsync(c => c.Code, c => c.ProductId);

        foreach (var line in bill.Lines)
            preview.Lines.Add(Resolve(line, products, codes));

        return preview;
    }

    private static ImportLine Resolve(
        VendorBillLine source, List<Product> products, Dictionary<string, Guid> codes)
    {
        var line = new ImportLine { Source = source };

        // Units per pack: only where the vendor's packing text is a count.
        // "60ML" is a bottle, not sixty sellable units.
        var parsed = PackMath.UnitsFromPacking(source.PackSize);
        line.UnitsPerPack = parsed ?? 1;
        line.UnitsAssumed = parsed is null;

        // 1. The vendor's own code, learned on a previous import.
        if (source.ProductCode is { } code && codes.TryGetValue(code, out var mappedId))
        {
            var mapped = products.FirstOrDefault(p => p.Id == mappedId);
            if (mapped is not null)
            {
                line.Product = mapped;
                line.Match = MatchKind.ByVendorCode;
                line.UnitsPerPack = mapped.UnitsPerPack;
                line.UnitsAssumed = false;
                return line;
            }
        }

        var name = Normalise(source.ProductName);

        // 2. Same name and same pack.
        var exact = products.FirstOrDefault(
            p => Normalise(p.Name) == name && Normalise(p.PackSize) == Normalise(source.PackSize));

        if (exact is not null)
        {
            line.Product = exact;
            line.Match = MatchKind.ByName;
            line.UnitsPerPack = exact.UnitsPerPack;
            line.UnitsAssumed = false;
            return line;
        }

        // 3. Same name, different pack — offered, never applied silently.
        var byName = products.FirstOrDefault(p => Normalise(p.Name) == name);
        if (byName is not null)
        {
            line.Product = byName;
            line.Match = MatchKind.NeedsChecking;
            return line;
        }

        line.Match = MatchKind.New;
        return line;
    }

    private static string Normalise(string? value)
        => VendorBillParser.Squash(value ?? "").ToUpperInvariant();

    /// <summary>
    /// Creates any new medicines, then receives the whole bill as one stock entry.
    /// Stock is added to what is already there — never replaced.
    /// </summary>
    public async Task<ImportResult> CommitAsync(ImportPreview preview)
    {
        if (preview.BlockedReason is not null)
            throw new InvalidOperationException(preview.BlockedReason);

        if (preview.Lines.Count == 0)
            throw new InvalidOperationException("There is nothing to import.");

        await using var db = await factory.CreateDbContextAsync();
        var created = 0;

        // One medicine can appear on several lines of the same bill — the same
        // drug delivered on two batches. Codes staged earlier in this loop are not
        // in the database yet, so they have to be remembered here as well.
        var codesStaged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Likewise for the medicines themselves: CALPOL arrives on two batches, so
        // two lines both want creating. Without this the catalogue ends up holding
        // the same drug twice and the batches split across the copies.
        var createdByKey = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in preview.Lines)
        {
            var key = $"{Normalise(line.Source.ProductName)}|{Normalise(line.Source.PackSize)}";

            if (line.Product is null && createdByKey.TryGetValue(key, out var alreadyCreated))
                line.Product = alreadyCreated;

            if (line.Product is null)
            {
                var product = new Product
                {
                    Name = line.Source.ProductName,
                    Manufacturer = line.Source.Manufacturer,
                    PackSize = line.Source.PackSize,
                    HsnCode = line.Source.HsnCode ?? "3004",
                    GstRate = line.Source.GstPercent,
                    UnitsPerPack = line.UnitsPerPack,
                    AllowLooseSale = line.UnitsPerPack > 1
                };

                // The catalogue is keyed on brand, maker and pack. Importing is a
                // way into the catalogue like any other, so it sets the key too —
                // without it every imported medicine collides on an empty key.
                product.SearchKey = product.BuildKey();

                db.Products.Add(product);
                line.Product = product;
                createdByKey[key] = product;
                created++;
            }
            else if (line.Product.UnitsPerPack != line.UnitsPerPack)
            {
                // The user corrected the pack size on the review screen.
                var tracked = await db.Products.FirstAsync(p => p.Id == line.Product.Id);
                tracked.UnitsPerPack = line.UnitsPerPack;
                tracked.AllowLooseSale = line.UnitsPerPack > 1;
                line.Product = tracked;
            }

            // Remember the vendor's code so the next bill matches without asking.
            if (line.Source.ProductCode is { } code && !string.IsNullOrWhiteSpace(code))
            {
                var known = codesStaged.Contains(code)
                            || await db.VendorProductCodes.AnyAsync(
                                c => c.VendorProfile == preview.ProfileName && c.Code == code);

                if (!known)
                {
                    codesStaged.Add(code);

                    db.VendorProductCodes.Add(new VendorProductCode
                    {
                        VendorProfile = preview.ProfileName,
                        Code = code,
                        ProductId = line.Product.Id
                    });
                }
            }
        }

        await db.SaveChangesAsync();

        var entry = new StockEntry
        {
            EntryDate = preview.Bill.BillDate,
            SupplierName = preview.SupplierName,
            SupplierInvoiceNo = preview.Bill.BillNo,
            ImportedFile = preview.FileName,
            ImportProfile = preview.ProfileName,
            NetAmount = preview.Bill.NetAmount,
            // Kept for reconciliation only — the vendor's discount is the store's
            // margin and must not reduce the recorded cost per unit.
            DiscountPercent = preview.Bill.DiscountPercent,
            Notes = $"Imported from {preview.FileName}"
        };

        var items = preview.Lines.Select(l => new StockEntryItem
        {
            ProductId = l.Product!.Id,
            BatchNo = l.Source.BatchNo,
            ExpiryDate = l.Source.Expiry,
            Quantity = l.Source.Quantity,
            FreeQuantity = l.Source.FreeQuantity,
            UnitsPerPack = l.UnitsPerPack,
            PurchaseRate = l.Source.Rate,
            Mrp = l.Source.Mrp
        }).ToList();

        var saved = await pharmacy.ReceiveStockAsync(entry, items);

        var units = preview.TotalUnits;
        logger.LogInformation(
            "Imported {FileName} as {EntryNo}: bill {BillNo}, {Lines} line(s), {Created} new medicine(s), {Units} unit(s) added.",
            preview.FileName, saved.EntryNo, preview.Bill.BillNo, items.Count, created, units);

        return new ImportResult(saved.EntryNo, items.Count, created, units);
    }
}
