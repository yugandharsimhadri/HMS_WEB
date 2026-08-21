using System.Text.RegularExpressions;

namespace SivayaanHMS.Core;

/// <summary>
/// Packs and loose units.
///
/// Stock is held in base units — tablets, not strips — so a customer can buy
/// five out of a ten-tablet strip. The MRP stays the price printed on the pack,
/// because that is what a tax invoice has to show.
/// </summary>
public static partial class PackMath
{
    /// <summary>
    /// Reads a countable pack size out of a vendor's free-text packing, or null
    /// when it does not describe a count.
    ///
    /// "10 TAB" and "30s" are counts. "60ML" and "1GR" are a volume and a weight —
    /// a syrup bottle is not five sellable units, so those deliberately return
    /// null rather than a guess. Guessing here would wreck both stock and price.
    /// </summary>
    public static int? UnitsFromPacking(string? packing)
    {
        if (string.IsNullOrWhiteSpace(packing)) return null;

        var text = packing.Trim().ToUpperInvariant();

        // A measured pack is one sellable thing however large the number is.
        if (Measure().IsMatch(text)) return null;

        // "1X10", "2 X 15" — the second number is what is in the pack.
        var multiplied = Multiplied().Match(text);
        if (multiplied.Success && int.TryParse(multiplied.Groups[2].Value, out var perPack))
            return Sane(perPack);

        // "30S", "10'S", "10 TAB", "15 CAPSULES", "6 PCS"
        var counted = Counted().Match(text);
        if (counted.Success && int.TryParse(counted.Groups[1].Value, out var count))
            return Sane(count);

        return null;
    }

    private static int? Sane(int units) => units is > 1 and <= 1000 ? units : null;

    /// <summary>
    /// What a quantity of base units costs before discount.
    ///
    /// Whole packs are priced from the pack MRP rather than by multiplying a
    /// rounded unit price, so a full strip always costs exactly what is printed
    /// on it. Only the remainder is priced per unit.
    /// </summary>
    public static decimal Gross(decimal packMrp, int unitsPerPack, int quantityUnits)
    {
        // Nothing sold, nothing charged. A negative quantity must never come back
        // as a negative price — that is money out of the till, not a discount.
        if (quantityUnits <= 0 || packMrp <= 0) return 0m;

        if (unitsPerPack <= 1) return Round(packMrp * quantityUnits);

        var packs = quantityUnits / unitsPerPack;
        var loose = quantityUnits % unitsPerPack;

        var loosePrice = Round(loose * UnitPrice(packMrp, unitsPerPack));

        // The unit price is rounded to the paisa, and rounding up can make the
        // remainder dearer than the pack it came out of: 17 of an 18-pack at
        // ₹1.00 came to ₹1.02. Nobody may be charged more for part of something
        // than for all of it, so the remainder is capped at the pack price.
        if (loosePrice > packMrp) loosePrice = packMrp;

        return Round(packs * packMrp + loosePrice);
    }

    public static decimal UnitPrice(decimal packMrp, int unitsPerPack)
        => unitsPerPack <= 1 ? packMrp : Round(packMrp / unitsPerPack);

    /// <summary>"3 strips + 4" — how stock and bill lines read to a human.</summary>
    public static string Describe(int quantityUnits, int unitsPerPack,
                                  string? packLabel = null, string? unitName = null)
    {
        // A negative count is not a quantity anyone can hand over, and reading it
        // back as "-1 × 10 TAB" would suggest it is. Zero is left alone — "0
        // tablets" is a true and useful thing to say.
        if (quantityUnits < 0) return "0";

        if (unitsPerPack <= 1) return quantityUnits.ToString();

        var packs = quantityUnits / unitsPerPack;
        var loose = quantityUnits % unitsPerPack;
        var pack = string.IsNullOrWhiteSpace(packLabel) ? "pack" : packLabel.Trim();

        // "9 loose" says nothing about what nine of. Name the thing being handed
        // over — the caller passes it when it knows, and the pack tells us when
        // it does not.
        var each = string.IsNullOrWhiteSpace(unitName) ? UnitWordFrom(packLabel) : unitName.Trim();

        return (packs, loose) switch
        {
            (0, _) => $"{loose} {each}",
            (_, 0) => $"{packs} × {pack}",
            _ => $"{packs} × {pack} + {loose} {each}"
        };
    }

    /// <summary>Reads the unit off what is printed on the pack: "10 TAB" is tablets.</summary>
    private static string UnitWordFrom(string? packLabel)
    {
        var text = (packLabel ?? "").ToUpperInvariant();

        if (text.Contains("CAP")) return "capsules";
        if (text.Contains("TAB")) return "tablets";
        if (text.Contains("ML")) return "bottles";

        return "units";
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    [GeneratedRegex(@"\d\s*(ML|L|GM|GR|G|MG|KG|MCG)$")]
    private static partial Regex Measure();

    [GeneratedRegex(@"^(\d+)\s*X\s*(\d+)")]
    private static partial Regex Multiplied();

    [GeneratedRegex(@"^(\d+)\s*'?\s*(S|TAB|TABS|TABLET|TABLETS|CAP|CAPS|CAPSULE|CAPSULES|PC|PCS|NO|NOS)\.?$")]
    private static partial Regex Counted();
}
