namespace SivayaanHMS.Core;

/// <summary>
/// Aggregate figures for the Stock Register — computed from the batches actually
/// on screen (post search/filter), so the totals always match what the user sees.
/// Quantity and rate semantics are the same ones the rest of the app already uses:
/// <see cref="StockEntryItem.LineTotal"/> values stock received the same way
/// (Quantity x PurchaseRate), and <see cref="GstCalculator"/> values a sale the
/// same way (Quantity x Mrp).
/// </summary>
public readonly record struct StockSummary(
    int TotalProducts,
    int TotalBatches,
    int TotalUnits,
    decimal TotalCostValue,
    decimal TotalMrpValue)
{
    public static StockSummary From(IReadOnlyCollection<Batch> batches) => new(
        batches.Select(b => b.ProductId).Distinct().Count(),
        batches.Count,
        batches.Sum(b => b.QtyOnHand),
        batches.Sum(b => b.QtyOnHand * b.PurchaseRate),
        batches.Sum(b => b.QtyOnHand * b.Mrp));
}

/// <summary>The Stock Register's "Search Medicine" box — matches on product name or
/// batch number, the two fields staff actually recognise a pack by.</summary>
public static class StockRegisterFilter
{
    public static bool Matches(Batch batch, string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm)) return true;

        var term = searchTerm.Trim();
        return batch.Product.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || batch.BatchNo.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
