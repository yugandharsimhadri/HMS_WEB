namespace SivayaanHMS.Core;

/// <summary>One computed sale line. Amounts are in rupees, rounded to paise.</summary>
public readonly record struct LineAmounts(
    decimal Gross,
    decimal Discount,
    decimal Net,
    decimal Taxable,
    decimal Gst,
    decimal Cgst,
    decimal Sgst);

/// <summary>Bill-level totals, including the round-off shown on the printed invoice.</summary>
public readonly record struct BillAmounts(
    decimal Gross,
    decimal Discount,
    decimal Taxable,
    decimal Cgst,
    decimal Sgst,
    decimal RoundOff,
    decimal Net);

/// <summary>
/// Indian retail pharmacy GST. Medicines are sold at the MRP printed on the pack,
/// and that MRP is inclusive of GST — so tax is back-calculated out of the line
/// value, never added on top of it.
///
///     taxable = net x 100 / (100 + gstRate)
///     gst     = net - taxable
///
/// For an intra-state sale (the normal case for a shop billing walk-in customers)
/// the GST splits equally into CGST and SGST. Inter-state IGST is not modelled in
/// v1 — a retail counter almost never raises one.
/// </summary>
public static class GstCalculator
{
    /// <summary>
    /// A line sold in base units. Whole packs price from the pack MRP and only
    /// the remainder is priced per unit — see <see cref="PackMath.Gross"/>. GST
    /// still comes back out of the total, exactly as before.
    ///
    /// <para><b>unitsPerPack has no default on purpose.</b> There used to be an
    /// overload without it that assumed 1, and the counter called it: every
    /// tablet priced at the full strip MRP, so nine tablets of a ten-strip came
    /// to nine strips. Pass 1 explicitly for anything genuinely sold singly.</para>
    /// </summary>
    public static LineAmounts Line(decimal mrp, int unitsPerPack, int quantity, decimal discountPercent, decimal gstRate)
    {
        var gross = PackMath.Gross(mrp, unitsPerPack, quantity);
        var discount = R(gross * discountPercent / 100m);
        var net = gross - discount;

        var taxable = R(net * 100m / (100m + gstRate));
        var gst = net - taxable;
        var half = R(gst / 2m);

        // Give any half-paise remainder to CGST so cgst + sgst == gst exactly.
        return new LineAmounts(gross, discount, net, taxable, gst, gst - half, half);
    }

    public static BillAmounts Bill(IEnumerable<LineAmounts> lines)
    {
        decimal gross = 0, discount = 0, taxable = 0, cgst = 0, sgst = 0, net = 0;

        foreach (var l in lines)
        {
            gross += l.Gross;
            discount += l.Discount;
            taxable += l.Taxable;
            cgst += l.Cgst;
            sgst += l.Sgst;
            net += l.Net;
        }

        var rounded = Math.Round(net, 0, MidpointRounding.AwayFromZero);
        return new BillAmounts(gross, discount, taxable, cgst, sgst, rounded - net, rounded);
    }

    private static decimal R(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
