namespace SivayaanHMS.Core;

/// <summary>
/// Reads the number out of a strength, so several strengths of one drug can
/// be ordered the way a person expects.
///
/// Sorting on the text alone puts "10 mg" above "5 mg", because "1" sorts
/// before "5". At a pharmacy counter that puts the adult dose at the top of
/// the list somebody is picking a child's dose from — the kind of ordering
/// mistake that ends in a double dose rather than an untidy screen.
/// </summary>
public static class StrengthParser
{
    /// <summary>
    /// The leading number — 5 from "5 mg", 100 from "100 ml", 2.5 from
    /// "2.5 mg", 250 from "250mg/5ml" (the first figure is the dose; the
    /// second is only the volume it arrives in).
    ///
    /// Null when there is no number at all, which sorts those rows last
    /// rather than pretending they are zero-strength and putting them first.
    /// </summary>
    public static decimal? Value(string? strength)
    {
        if (string.IsNullOrWhiteSpace(strength)) return null;

        var text = strength.Trim();
        var start = -1;
        var end = -1;

        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]))
            {
                if (start < 0) start = i;
                end = i;
            }
            else if (text[i] == '.' && start >= 0 && end == i - 1)
            {
                // A dot only counts while inside a run of digits, so the point
                // in "2.5 mg" joins the number and the one in "5 mg. tablet"
                // does not.
                end = i;
            }
            else if (start >= 0)
            {
                break;
            }
        }

        if (start < 0) return null;

        var slice = text[start..(end + 1)].TrimEnd('.');
        return decimal.TryParse(slice, out var value) ? value : null;
    }
}
