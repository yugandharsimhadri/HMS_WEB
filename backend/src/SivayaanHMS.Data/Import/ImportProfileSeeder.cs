using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data.Import;

/// <summary>
/// Ships the profiles for the vendors already seen. Both current suppliers export
/// the same column names but differ in how they write dates and expiries, which is
/// exactly the sort of thing a profile exists to absorb.
/// </summary>
public static class ImportProfileSeeder
{
    /// <summary>Shared by both known vendors: logical field = CSV column.</summary>
    private const string StandardColumnMap = """
        BillNo=FeedNo
        BillDate=FeedDate
        CustomerName=CustName
        SubTotal=SubTotal
        DiscountPercent=DisPer
        TotalDiscount=SumDis
        TaxableValue=GstVal1
        TaxAmount=Gst1
        RoundOff=Rounding
        NetAmount=NetAmt
        ProductCode=ProdCode
        ProductName=ProdName
        PackSize=Packing
        BatchNo=BatchNo
        Quantity=Qty
        FreeQuantity=Free
        Rate=Rate
        Mrp=Mrp
        LineValue=ProValue
        GstPercent=IGstPer
        Expiry=Expiry
        Manufacturer=ComName
        HsnCode=HsnCode
        PreviousMrp=Mrp_Old
        """;

    public const string ProfileA = "Profile A — slash dates, numeric expiry";
    public const string ProfileB = "Profile B — dash dates, month-name expiry";

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await EnsureAsync(db, new ImportProfile
        {
            Name = ProfileA,
            Description = "Bill date as 14/07/2026 and expiry as 4/2028. Seen on ER-series bills.",
            ColumnMap = StandardColumnMap,
            DateFormats = "dd/MM/yyyy|d/M/yyyy|dd-MM-yyyy",
            ExpiryFormats = "M/yyyy|MM/yyyy|M/yy|MM/yy",
            DefaultGstRate = 5m
        }, ct);

        await EnsureAsync(db, new ImportProfile
        {
            Name = ProfileB,
            Description = "Bill date as 04-07-2026 and expiry as Sep-27. Seen on SW-series bills.",
            ColumnMap = StandardColumnMap,
            DateFormats = "dd-MM-yyyy|d-M-yyyy|dd/MM/yyyy",
            ExpiryFormats = "MMM-yy|MMM-yyyy|MMM yy|M/yyyy|MM/yyyy",
            DefaultGstRate = 5m
        }, ct);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Inserts a missing profile but never overwrites one that is there — a profile
    /// the user has tuned for their vendor must survive an application update.
    /// </summary>
    private static async Task EnsureAsync(AppDbContext db, ImportProfile profile, CancellationToken ct)
    {
        if (await db.ImportProfiles.AnyAsync(p => p.Name == profile.Name, ct)) return;
        db.ImportProfiles.Add(profile);
    }
}
