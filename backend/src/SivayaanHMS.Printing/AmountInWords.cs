namespace SivayaanHMS.Printing;

/// <summary>Rupees in words, as an Indian receipt or invoice is expected to
/// carry. Ported verbatim from the desktop's FeeReceiptDocument.InWords —
/// pure arithmetic and string logic, no printing dependency of its own.</summary>
public static class AmountInWords
{
    public static string Convert(decimal amount)
    {
        var rupees = (long)Math.Floor(amount);
        var paise = (int)Math.Round((amount - rupees) * 100, MidpointRounding.AwayFromZero);

        var words = rupees == 0 ? "Zero" : ConvertWhole(rupees);
        var text = $"Rupees {words}";

        if (paise > 0) text += $" and {ConvertWhole(paise)} Paise";

        return text + " only";
    }

    private static string ConvertWhole(long value)
    {
        if (value == 0) return "";

        // Indian grouping: crore, lakh, thousand, hundred.
        if (value >= 10_000_000) return $"{ConvertWhole(value / 10_000_000)} Crore {ConvertWhole(value % 10_000_000)}".Trim();
        if (value >= 100_000) return $"{ConvertWhole(value / 100_000)} Lakh {ConvertWhole(value % 100_000)}".Trim();
        if (value >= 1_000) return $"{ConvertWhole(value / 1_000)} Thousand {ConvertWhole(value % 1_000)}".Trim();
        if (value >= 100) return $"{ConvertWhole(value / 100)} Hundred {ConvertWhole(value % 100)}".Trim();

        string[] ones =
        [
            "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
            "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen",
            "Eighteen", "Nineteen"
        ];

        string[] tens =
        [
            "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
        ];

        if (value < 20) return ones[value];
        return $"{tens[value / 10]} {ones[value % 10]}".Trim();
    }
}
