using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>A sale line as assembled at the counter, before it is persisted.</summary>
public class SaleLine
{
    public Guid ProductId { get; set; }
    public Guid BatchId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string BatchNo { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    public string HsnCode { get; set; } = "3004";
    /// <summary>Base units — 5 tablets, not 5 strips.</summary>
    public int Quantity { get; set; }

    public int UnitsPerPack { get; set; } = 1;
    public string? PackLabel { get; set; }
    public decimal Mrp { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal GstRate { get; set; }
    public DrugSchedule Schedule { get; set; }
}

/// <summary>Raised when a medicine already exists, carrying the one that does.</summary>
public class DuplicateMedicineException(Product existing, string message) : InvalidOperationException(message)
{
    public Product Existing { get; } = existing;
}

/// <summary>What re-counting a medicine's existing batches would do.</summary>
public class RepackPreview
{
    public int UnitsPerPack { get; init; }
    public int Batches { get; init; }
    public int QuantityBefore { get; init; }
    public int QuantityAfter { get; init; }

    public bool AnythingToDo => Batches > 0 && QuantityAfter != QuantityBefore;
}

public class PharmacyService(IDbContextFactory<AppDbContext> factory, IClock clock, ILogger<PharmacyService> logger)
{
    // ── Products ───────────────────────────────────────────────────────────

    public async Task<List<Product>> SearchProductsAsync(string? term, int take = 50)
    {
        await using var db = await factory.CreateDbContextAsync();
        var q = db.Products.AsNoTracking().Include(p => p.Batches).Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(term))
        {
            // Brand, drug, maker or rack — staff search by whichever they know.
            //
            // Like, not Contains: Contains becomes instr(), which is case
            // sensitive, so typing "cetirizine" found nothing while "Cetirizine"
            // did. Nobody at a counter types capitals.
            var pattern = $"%{term.Trim()}%";

            q = q.Where(p => EF.Functions.Like(p.Name, pattern)
                          || (p.GenericName != null && EF.Functions.Like(p.GenericName, pattern))
                          || (p.Manufacturer != null && EF.Functions.Like(p.Manufacturer, pattern))
                          || (p.RackLocation != null && EF.Functions.Like(p.RackLocation, pattern)));
        }

        return await q.OrderBy(p => p.Name).Take(take).ToListAsync();
    }

    public async Task SaveProductAsync(Product product)
    {
        await using var db = await factory.CreateDbContextAsync();

        // Say it in words rather than letting the unique index throw at the user.
        var key = product.BuildKey();

        var clash = await db.Products
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.SearchKey == key && p.Id != product.Id);

        if (clash is not null)
        {
            throw new DuplicateMedicineException(clash,
                $"{clash.Name} ({clash.Manufacturer ?? "no maker"}, {clash.PackSize ?? "no pack"}) " +
                $"is already in the catalogue. Open that one instead of adding it again — " +
                $"two records split the stock and both appear at the counter.");
        }

        product.SearchKey = key;

        if (product.Id != Guid.Empty && await db.Products.AnyAsync(p => p.Id == product.Id))
        {
            var existing = await db.Products.FirstAsync(p => p.Id == product.Id);
            existing.SearchKey = key;
            existing.Name = product.Name;
            existing.GenericName = product.GenericName;
            existing.Manufacturer = product.Manufacturer;
            existing.Composition = product.Composition;
            existing.Storage = product.Storage;
            existing.PackSize = product.PackSize;
            existing.HsnCode = product.HsnCode;
            existing.GstRate = product.GstRate;
            existing.Schedule = product.Schedule;
            existing.RackLocation = product.RackLocation;
            existing.ReorderLevel = product.ReorderLevel;
            existing.IsActive = product.IsActive;

            // These four were missing, so editing an existing medicine looked
            // like it worked and quietly changed nothing — and units-per-pack
            // is the one field that decides whether a tablet or a strip is sold.
            existing.UnitsPerPack = Math.Max(1, product.UnitsPerPack);
            existing.AllowLooseSale = product.AllowLooseSale;
            existing.DispensingUnit = product.DispensingUnit;
        }
        else
        {
            db.Products.Add(product);
        }

        await db.SaveChangesAsync();
    }

    // ── Stock ──────────────────────────────────────────────────────────────

    /// <summary>Batches with stock left, nearest expiry first — the order stock is dispensed in.</summary>
    public async Task<List<Batch>> GetSellableBatchesAsync(Guid productId)
    {
        await using var db = await factory.CreateDbContextAsync();

        return await db.Batches
            .AsNoTracking()
            .Where(b => !b.IsDeleted && b.ProductId == productId && b.QtyOnHand > 0)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync();
    }

    /// <summary>How many units of a requested quantity come from which batch.</summary>
    public record Allocation(Batch Batch, int Units);

    /// <summary>
    /// Works out which batches fill a requested quantity, nearest expiry first.
    ///
    /// A request can span batches — asking for 20 tablets when the oldest batch
    /// holds 15 takes those 15 and 5 from the next. Each batch stays a separate
    /// bill line because the batch number and expiry of what was actually handed
    /// over has to appear on the invoice.
    /// </summary>
    public async Task<(List<Allocation> Allocations, int Shortfall)> AllocateAsync(Guid productId, int units)
    {
        var allocations = new List<Allocation>();
        if (units <= 0) return (allocations, 0);

        // Expired stock is never dispensed, whatever else happens.
        var sellable = (await GetSellableBatchesAsync(productId)).Where(b => !b.IsExpired).ToList();

        var taken = new Dictionary<Guid, int>();
        var remaining = units;

        // Pass one: split at a pack boundary where one is available.
        //
        // The whole-pack price guarantee holds per bill line, so a split that
        // leaves a part pack on both lines loses it — 20 of a 15-strip taken as
        // 12 + 8 prices every unit loose and comes to five paise less than taking
        // 15 + 5. Whole packs first, the remainder on the last line.
        foreach (var batch in sellable)
        {
            if (remaining == 0) break;

            var take = Math.Min(remaining, batch.QtyOnHand);
            if (take <= 0) continue;

            var perPack = Math.Max(1, batch.UnitsPerPack);

            // Only when this batch cannot cover the rest. If it can, the whole
            // quantity is one line and there is nothing to protect.
            if (perPack > 1 && take < remaining && take >= perPack)
                take = take / perPack * perPack;

            taken[batch.Id] = take;
            remaining -= take;
        }

        // Pass two: top up from what pass one rounded past. Tidy pricing must
        // never cost a sale — a batch holding 13 of a 10-pack has to give all
        // thirteen when thirteen are needed, not ten.
        foreach (var batch in sellable)
        {
            if (remaining == 0) break;

            var already = taken.GetValueOrDefault(batch.Id);
            var spare = Math.Min(remaining, batch.QtyOnHand - already);

            if (spare <= 0) continue;

            taken[batch.Id] = already + spare;
            remaining -= spare;
        }

        // Nearest expiry first, as they were read.
        foreach (var batch in sellable)
        {
            if (taken.TryGetValue(batch.Id, out var units_) && units_ > 0)
                allocations.Add(new Allocation(batch, units_));
        }

        return (allocations, remaining);
    }

    /// <summary>
    /// Every batch currently on the shelf — the Stock Register's source. Reads the
    /// same Batch.QtyOnHand that Product.StockOnHand, Low Stock and Expiring Soon
    /// already read, so there is only ever one place stock is calculated from.
    /// </summary>
    public async Task<List<Batch>> GetAllBatchesAsync(bool includeZeroStock = false)
    {
        await using var db = await factory.CreateDbContextAsync();

        var q = db.Batches.AsNoTracking().Include(b => b.Product).Where(b => !b.IsDeleted);

        // The stock register can show batches that have run down to nothing;
        // everywhere else only wants what can actually be sold.
        if (!includeZeroStock) q = q.Where(b => b.QtyOnHand > 0);

        return await q.OrderBy(b => b.Product.Name).ThenBy(b => b.ExpiryDate).ToListAsync();
    }

    /// <summary>Receives a supplier consignment. This is the only way stock enters the system.</summary>
    public async Task<StockEntry> ReceiveStockAsync(StockEntry entry, IEnumerable<StockEntryItem> items)
    {
        var received = items.ToList();

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        entry.EntryNo = await NumberService.NextAsync(db, NumberService.StockEntry);
        entry.TotalAmount = 0;
        db.StockEntries.Add(entry);

        foreach (var item in received)
        {
            // A minus anywhere on a goods-inward line is a delivery that takes
            // stock away or a price that pays the shop to sell. Refuse the whole
            // entry rather than receive part of it — the transaction is undone.
            //
            // A positive quantity with a negative free quantity used to pass the
            // "nothing on this line" check below and drain the shelf.
            if (item.Quantity < 0 || item.FreeQuantity < 0)
                throw new InvalidOperationException(
                    $"{item.BatchNo}: quantities cannot be negative " +
                    $"({item.Quantity} received, {item.FreeQuantity} free).");

            if (item.Mrp < 0 || item.PurchaseRate < 0)
                throw new InvalidOperationException(
                    $"{item.BatchNo}: prices cannot be negative " +
                    $"(MRP {item.Mrp:0.00}, rate {item.PurchaseRate:0.00}).");

            if (item.Quantity <= 0 && item.FreeQuantity <= 0)
            {
                logger.LogWarning("{EntryNo}: skipped {BatchNo} — no quantity on the line.", entry.EntryNo, item.BatchNo);
                continue;
            }

            item.StockEntryId = entry.Id;
            db.StockEntryItems.Add(item);
            entry.TotalAmount += item.Quantity * item.PurchaseRate;

            // Receiving the same drug and batch number again ADDS to what is on the
            // shelf — it never replaces it. Whether the quantity was keyed in or
            // read from a vendor file, a second delivery of batch B123 leaves you
            // holding both. Overwriting here would quietly destroy stock.
            var batch = await db.Batches.FirstOrDefaultAsync(
                b => b.ProductId == item.ProductId && b.BatchNo == item.BatchNo && !b.IsDeleted);

            if (batch is null)
            {
                db.Batches.Add(new Batch
                {
                    ProductId = item.ProductId,
                    BatchNo = item.BatchNo,
                    ExpiryDate = item.ExpiryDate,
                    Mrp = item.Mrp,
                    PurchaseRate = item.PurchaseRate,
                    UnitsPerPack = Math.Max(1, item.UnitsPerPack),
                    QtyOnHand = item.UnitsReceived,
                    SupplierName = entry.SupplierName,
                    SupplierInvoiceNo = entry.SupplierInvoiceNo,
                    PacksReceived = item.Quantity,
                    FreePacks = item.FreeQuantity,
                    ReceivedOn = entry.EntryDate
                });
            }
            else
            {
                batch.QtyOnHand += item.UnitsReceived;

                // A second delivery of the same batch adds to what it cost, so the
                // scheme on it stays true rather than being replaced.
                batch.PacksReceived += item.Quantity;
                batch.FreePacks += item.FreeQuantity;
                batch.SupplierInvoiceNo = entry.SupplierInvoiceNo ?? batch.SupplierInvoiceNo;

                // Price and expiry take the newest consignment's values; the pack
                // size does not, because stock already counted in the old units
                // would be silently repriced.
                batch.Mrp = item.Mrp;
                batch.PurchaseRate = item.PurchaseRate;
                batch.ExpiryDate = item.ExpiryDate;
            }
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation("Stock entry {EntryNo} received from {Supplier}.", entry.EntryNo, entry.SupplierName ?? "(no supplier)");
        return entry;
    }

    /// <summary>
    /// Corrects what a batch holds and records why. The audit row is written in
    /// the same transaction as the change, so a correction can never happen
    /// without leaving a trail.
    /// </summary>
    public async Task<StockAdjustment> AdjustStockAsync(
        Guid batchId, int newQuantity, AdjustmentReason reason, string? notes = null, string? by = null)
    {
        if (newQuantity < 0)
            throw new InvalidOperationException("A batch cannot hold less than nothing.");

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var batch = await db.Batches.Include(b => b.Product).FirstOrDefaultAsync(b => b.Id == batchId)
                    ?? throw new InvalidOperationException("That batch no longer exists.");

        if (batch.QtyOnHand == newQuantity)
            throw new InvalidOperationException("That is the quantity already recorded — nothing to correct.");

        var adjustment = new StockAdjustment
        {
            BatchId = batch.Id,
            ProductId = batch.ProductId,
            ProductName = batch.Product.Name,
            BatchNo = batch.BatchNo,
            QuantityBefore = batch.QtyOnHand,
            QuantityAfter = newQuantity,
            Reason = reason,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            AdjustedBy = by
        };

        batch.QtyOnHand = newQuantity;
        db.StockAdjustments.Add(adjustment);

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation("Stock corrected: {Product} batch {Batch} {Before} → {After} ({Reason}).",
            adjustment.ProductName, adjustment.BatchNo, adjustment.QuantityBefore, adjustment.QuantityAfter, reason);

        return adjustment;
    }

    /// <summary>
    /// Puts stock on the shelf from the counter, for a medicine that is
    /// physically there but not in the system.
    ///
    /// Only the pack count and the MRP are needed — the MRP because nothing can
    /// be priced without it. A missing batch number gets a traceable one of our
    /// own, and a missing expiry is taken as two years out. Both the entry and
    /// the batch are flagged provisional: purchases will not tie out against
    /// sales until the real supplier bill is reconciled against them, and that
    /// is a deliberate trade for being able to serve the patient in front of you.
    /// </summary>
    public async Task<Batch> QuickAddStockAsync(
        Guid productId, int packs, decimal mrp,
        string? batchNo = null, DateTime? expiry = null,
        decimal purchaseRate = 0m, string? by = null)
    {
        if (packs <= 0) throw new InvalidOperationException("Enter how many packs are on the shelf.");
        if (mrp <= 0) throw new InvalidOperationException("Enter the MRP printed on the pack — nothing can be sold without it.");

        if (purchaseRate < 0)
            throw new InvalidOperationException("The rate paid cannot be negative. Leave it at zero if you do not know it.");

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId)
                      ?? throw new InvalidOperationException("That medicine no longer exists.");

        var perPack = Math.Max(1, product.UnitsPerPack);
        var today = clock.Now.Date;

        var entry = new StockEntry
        {
            EntryNo = await NumberService.NextAsync(db, NumberService.StockEntry),
            EntryDate = today,
            SupplierName = null,
            IsProvisional = true,
            EnteredBy = by,
            Notes = "Entered at the counter — no supplier bill yet."
        };

        var batch = new Batch
        {
            ProductId = product.Id,
            BatchNo = string.IsNullOrWhiteSpace(batchNo)
                ? $"CTR-{clock.Now:yyMMdd-HHmmss}"
                : batchNo.Trim(),
            ExpiryDate = expiry?.Date ?? today.AddYears(2),
            Mrp = mrp,
            PurchaseRate = purchaseRate,
            QtyOnHand = packs * perPack,
            UnitsPerPack = perPack,
            PacksReceived = packs,
            ReceivedOn = today,
            IsProvisional = true
        };

        entry.Items.Add(new StockEntryItem
        {
            ProductId = product.Id,
            BatchNo = batch.BatchNo,
            ExpiryDate = batch.ExpiryDate,
            Quantity = packs,
            UnitsPerPack = perPack,
            PurchaseRate = purchaseRate,
            Mrp = mrp
        });

        db.StockEntries.Add(entry);
        db.Batches.Add(batch);

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation("Counter stock: {Qty} unit(s) of {Product} as batch {Batch} (provisional, by {By}).",
            batch.QtyOnHand, product.Name, batch.BatchNo, by ?? "unknown");

        return batch;
    }

    /// <summary>
    /// Batches holding less than one full pack — the tail ends of opened strips.
    ///
    /// They sit there until they expire unless someone happens to want exactly
    /// that many, so they are worth pushing first or writing off deliberately
    /// rather than quietly.
    /// </summary>
    public async Task<List<Batch>> GetPartPacksAsync()
    {
        await using var db = await factory.CreateDbContextAsync();

        return await db.Batches
            .AsNoTracking()
            .Include(b => b.Product)
            .Where(b => !b.IsDeleted && b.QtyOnHand > 0 && b.UnitsPerPack > 1 && b.QtyOnHand < b.UnitsPerPack)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync();
    }

    /// <summary>
    /// Everything put on the shelf at the counter and not yet matched to a
    /// supplier bill. This is the list to work through when reconciling.
    /// </summary>
    public async Task<List<Batch>> GetProvisionalBatchesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();

        return await db.Batches
            .AsNoTracking()
            .Include(b => b.Product)
            .Where(b => !b.IsDeleted && b.IsProvisional)
            .OrderByDescending(b => b.ReceivedOn)
            .ToListAsync();
    }

    /// <summary>
    /// How many batches of this medicine were received under a different
    /// units-per-pack than it now says, and what re-counting them would do.
    ///
    /// This is the shape of the "9 tablets took 9 strips" fault: a batch keeps
    /// the pack size it arrived with, so correcting the medicine alone leaves
    /// the stock on the shelf still being sold by the strip.
    /// </summary>
    public async Task<RepackPreview> PreviewRepackAsync(Guid productId, int unitsPerPack)
    {
        await using var db = await factory.CreateDbContextAsync();

        var perPack = Math.Max(1, unitsPerPack);

        var batches = await db.Batches
            .Where(b => b.ProductId == productId && b.QtyOnHand > 0 && b.UnitsPerPack != perPack)
            .ToListAsync();

        return new RepackPreview
        {
            UnitsPerPack = perPack,
            Batches = batches.Count,
            QuantityBefore = batches.Sum(b => b.QtyOnHand),
            QuantityAfter = batches.Sum(b => b.QtyOnHand / Math.Max(1, b.UnitsPerPack) * perPack)
        };
    }

    /// <summary>
    /// Re-counts every batch of a medicine that was received under the wrong
    /// units-per-pack: 59 packs recorded as 59 units becomes 885 tablets.
    ///
    /// The packs on the shelf do not change — only what the software believes
    /// one of them holds. Each batch still gets its own audit row, because the
    /// numbers move and nobody should have to guess why later.
    /// </summary>
    public async Task<int> RepackAsync(Guid productId, int unitsPerPack, string? by = null)
    {
        var perPack = Math.Max(1, unitsPerPack);

        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId)
                      ?? throw new InvalidOperationException("That medicine no longer exists.");

        var batches = await db.Batches
            .Where(b => b.ProductId == productId && b.UnitsPerPack != perPack)
            .ToListAsync();

        foreach (var batch in batches)
        {
            var packs = batch.QtyOnHand / Math.Max(1, batch.UnitsPerPack);
            var after = packs * perPack;

            if (batch.QtyOnHand != after)
            {
                db.StockAdjustments.Add(new StockAdjustment
                {
                    BatchId = batch.Id,
                    ProductId = product.Id,
                    ProductName = product.Name,
                    BatchNo = batch.BatchNo,
                    QuantityBefore = batch.QtyOnHand,
                    QuantityAfter = after,
                    Reason = AdjustmentReason.EntryError,
                    Notes = $"Pack size corrected: {packs} pack(s) recounted at " +
                            $"{batch.UnitsPerPack} → {perPack} per pack.",
                    AdjustedBy = by
                });
            }

            batch.UnitsPerPack = perPack;
            batch.QtyOnHand = after;
        }

        product.UnitsPerPack = perPack;

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation("Repacked {Product}: {Count} batch(es) recounted at {PerPack} per pack.",
            product.Name, batches.Count, perPack);

        return batches.Count;
    }

    /// <summary>The correction trail, newest first.</summary>
    public async Task<List<StockAdjustment>> GetAdjustmentsAsync(int take = 200)
    {
        await using var db = await factory.CreateDbContextAsync();

        return await db.StockAdjustments
            .AsNoTracking()
            .OrderByDescending(a => a.AdjustedOn)
            .Take(take)
            .ToListAsync();
    }

    // ── Sales ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves a bill: validates stock, computes GST out of the MRP, deducts the
    /// batches and records any Schedule H1 lines. All or nothing.
    ///
    /// Retries on <see cref="DbUpdateConcurrencyException"/>
    /// (SAAS_MIGRATION.md finding 2 — two counters selling the last strip of a
    /// batch at once). The desktop version read QtyOnHand, checked it, and
    /// wrote the reduced figure with nothing stopping two callers from doing
    /// that against the same starting value and driving stock negative. Now
    /// every Batch carries a concurrency token (see AppDbContext), so the
    /// second writer's SaveChangesAsync throws instead of succeeding wrongly;
    /// retrying re-reads QtyOnHand fresh and re-validates against it, which is
    /// a live re-check against current stock rather than a blind repeat.
    /// </summary>
    public async Task<Sale> SaveSaleAsync(Sale sale, IReadOnlyList<SaleLine> lines)
    {
        if (lines.Count == 0) throw new InvalidOperationException("Add at least one medicine to the bill.");

        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SaveSaleAttemptAsync(sale, lines);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts)
            {
                logger.LogWarning(
                    "Sale save hit a stock concurrency conflict, retrying (attempt {Attempt} of {Max}).",
                    attempt, maxAttempts);
            }
        }
    }

    private async Task<Sale> SaveSaleAttemptAsync(Sale sale, IReadOnlyList<SaleLine> lines)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var computed = new List<LineAmounts>(lines.Count);

        foreach (var line in lines)
        {
            var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == line.BatchId)
                        ?? throw new InvalidOperationException($"Batch not found for {line.ProductName}.");

            if (line.Quantity <= 0)
                throw new InvalidOperationException($"Quantity must be at least 1 for {line.ProductName}.");

            // A negative price hands money over with the medicine; a negative
            // discount is a surcharge nobody agreed to. Neither reaches a bill.
            if (line.Mrp < 0)
                throw new InvalidOperationException($"The price of {line.ProductName} cannot be negative.");

            if (line.DiscountPercent < 0 || line.DiscountPercent > 100)
                throw new InvalidOperationException(
                    $"The discount on {line.ProductName} must be between 0 and 100 percent.");

            if (line.GstRate < 0)
                throw new InvalidOperationException($"The GST rate on {line.ProductName} cannot be negative.");

            if (batch.QtyOnHand < line.Quantity)
                throw new InvalidOperationException(
                    $"Only {PackMath.Describe(batch.QtyOnHand, batch.UnitsPerPack, line.PackLabel)} " +
                    $"left of {line.ProductName} (batch {batch.BatchNo}).");

            var amounts = GstCalculator.Line(line.Mrp, line.UnitsPerPack, line.Quantity,
                                             line.DiscountPercent, line.GstRate);
            computed.Add(amounts);

            batch.QtyOnHand -= line.Quantity;

            db.SaleItems.Add(new SaleItem
            {
                SaleId = sale.Id,
                ProductId = line.ProductId,
                BatchId = line.BatchId,
                ProductName = line.ProductName,
                BatchNo = line.BatchNo,
                ExpiryDate = line.ExpiryDate,
                HsnCode = line.HsnCode,
                Quantity = line.Quantity,
                UnitsPerPack = line.UnitsPerPack,
                PackLabel = line.PackLabel,
                Mrp = line.Mrp,
                DiscountPercent = line.DiscountPercent,
                GstRate = line.GstRate,
                TaxableAmount = amounts.Taxable,
                GstAmount = amounts.Gst,
                LineTotal = amounts.Net
            });

            if (line.Schedule == DrugSchedule.H1)
            {
                db.H1Register.Add(new H1RegisterEntry
                {
                    SoldOn = sale.BillDate,
                    BillNo = sale.BillNo,
                    ProductName = line.ProductName,
                    BatchNo = line.BatchNo,
                    Quantity = line.Quantity,
                    PatientName = sale.CustomerName,
                    DoctorName = sale.DoctorName
                });
            }
        }

        var bill = GstCalculator.Bill(computed);
        sale.GrossAmount = bill.Gross;
        sale.DiscountAmount = bill.Discount;
        sale.TaxableAmount = bill.Taxable;
        sale.CgstAmount = bill.Cgst;
        sale.SgstAmount = bill.Sgst;
        sale.RoundOff = bill.RoundOff;
        sale.NetAmount = bill.Net;
        sale.BillNo = await NumberService.NextAsync(db, NumberService.Bill);

        // The H1 rows were staged before the number existed — backfill them.
        foreach (var h1 in db.ChangeTracker.Entries<H1RegisterEntry>()
                             .Where(e => e.State == EntityState.Added))
        {
            h1.Entity.BillNo = sale.BillNo;
        }

        db.Sales.Add(sale);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation("Bill {BillNo} saved: {Lines} line(s), net {Net:0.00}, {PaymentMode}.",
            sale.BillNo, lines.Count, sale.NetAmount, sale.PaymentMode);

        return sale;
    }

    public async Task<Sale?> GetSaleAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id);
    }

    /// <summary>Every medicine bill for a patient, newest first, regardless of date.</summary>
    public async Task<List<Sale>> GetSalesByPatientAsync(Guid patientId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Sales.AsNoTracking().Include(s => s.Items)
            .Where(s => s.PatientId == patientId)
            .OrderByDescending(s => s.BillDate)
            .ToListAsync();
    }

    /// <summary>
    /// Finds a bill by number or customer across all dates. A walk-in sale has no
    /// patient record, so the bill number and the name on it are the only handles
    /// anyone has when they come back asking for a copy.
    /// </summary>
    public async Task<List<Sale>> SearchSalesAsync(string? term, int take = 100)
    {
        await using var db = await factory.CreateDbContextAsync();
        var q = db.Sales.AsNoTracking().Include(s => s.Items).AsQueryable();

        if (!string.IsNullOrWhiteSpace(term))
        {
            term = term.Trim();
            var pattern = $"%{term}%";

            q = q.Where(s => EF.Functions.Like(s.BillNo, pattern)
                          || EF.Functions.Like(s.CustomerName, pattern));
        }

        return await q.OrderByDescending(s => s.BillDate).Take(take).ToListAsync();
    }

    public Task<List<Sale>> GetSalesAsync(DateTime date) => GetSalesAsync(date, date);

    /// <summary>Sales whose bill date falls within [from, to], both dates inclusive.</summary>
    public async Task<List<Sale>> GetSalesAsync(DateTime from, DateTime to)
    {
        await using var db = await factory.CreateDbContextAsync();
        var start = from.Date;
        var end = to.Date.AddDays(1);

        return await db.Sales.AsNoTracking().Include(s => s.Items)
            .Where(s => s.BillDate >= start && s.BillDate < end)
            .OrderByDescending(s => s.BillDate)
            .ToListAsync();
    }

    /// <summary>Schedule H1 statutory register entries within [from, to], both dates inclusive.</summary>
    public async Task<List<H1RegisterEntry>> GetH1RegisterAsync(DateTime from, DateTime to)
    {
        await using var db = await factory.CreateDbContextAsync();
        var start = from.Date;
        var end = to.Date.AddDays(1);

        return await db.H1Register
            .AsNoTracking()
            .Where(h => h.SoldOn >= start && h.SoldOn < end)
            .OrderBy(h => h.SoldOn)
            .ToListAsync();
    }

    // ── Alerts ─────────────────────────────────────────────────────────────

    public async Task<List<Batch>> GetExpiringAsync(int withinDays = 90)
    {
        await using var db = await factory.CreateDbContextAsync();
        var cutoff = clock.Now.Date.AddDays(withinDays);

        return await db.Batches.AsNoTracking().Include(b => b.Product)
            .Where(b => !b.IsDeleted && b.QtyOnHand > 0 && b.ExpiryDate <= cutoff)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync();
    }

    public async Task<List<Product>> GetLowStockAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var products = await db.Products.AsNoTracking().Include(p => p.Batches)
            .Where(p => !p.IsDeleted && p.IsActive && p.ReorderLevel > 0)
            .ToListAsync();

        return products.Where(p => p.StockOnHand <= p.ReorderLevel)
                       .OrderBy(p => p.Name)
                       .ToList();
    }
}
