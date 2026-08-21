namespace SivayaanHMS.Core;

/// <summary>
/// Prescriptions are written in individual units — six tablets, not half a strip.
/// The pharmacy buys and prices in strips. This works out how many units a course
/// comes to so the two sides never have to be reconciled by hand.
/// </summary>
public static class DoseMath
{
    /// <summary>
    /// Doses a day from the way it was written. Understands "1-0-1" style and the
    /// usual Latin abbreviations. Returns null when it cannot tell, e.g. "SOS".
    /// </summary>
    public static decimal? DosesPerDay(string? frequency)
    {
        if (string.IsNullOrWhiteSpace(frequency)) return null;

        var text = frequency.Trim().ToUpperInvariant();

        var known = text switch
        {
            "OD" or "HS" or "QD" => 1m,
            "BD" or "BID" => 2m,
            "TDS" or "TID" => 3m,
            "QID" or "QDS" => 4m,
            "SOS" or "PRN" or "STAT" => (decimal?)null,
            _ => -1m
        };

        if (known != -1m) return known;

        // "1-0-1", "1-1-1", "1/2-0-1/2"
        var parts = text.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        decimal total = 0;
        foreach (var part in parts)
        {
            if (!TryDose(part, out var dose)) return null;
            total += dose;
        }

        return total > 0 ? total : null;
    }

    private static bool TryDose(string text, out decimal dose)
    {
        dose = 0;
        text = text.Trim();

        // Halves and quarters are written as fractions on a prescription.
        var slash = text.IndexOf('/');
        if (slash > 0)
        {
            var top = text[..slash];
            var bottom = text[(slash + 1)..];

            if (decimal.TryParse(top, out var numerator) &&
                decimal.TryParse(bottom, out var denominator) && denominator != 0)
            {
                dose = numerator / denominator;
                return true;
            }

            return false;
        }

        return decimal.TryParse(text, out dose);
    }

    /// <summary>
    /// Units to dispense for a course. Rounded up, because half a tablet cannot
    /// be handed over and running short mid-course is worse than one spare.
    /// </summary>
    public static int? UnitsForCourse(string? frequency, int days)
    {
        if (days <= 0) return null;

        var perDay = DosesPerDay(frequency);
        if (perDay is null or <= 0) return null;

        return (int)Math.Ceiling(perDay.Value * days);
    }
}
