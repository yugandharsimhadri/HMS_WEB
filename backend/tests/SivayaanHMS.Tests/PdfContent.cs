using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SivayaanHMS.Tests;

/// <summary>
/// Reads a generated PDF back the way a printer would: by pulling the numbers
/// out of its content stream rather than trusting the code that wrote it.
///
/// A font size only counts as configurable if it survives the whole trip onto
/// paper. Asserting on the DocumentTheme object proves the setting was stored,
/// not that anything printed differently — which is exactly the gap that would
/// let a size quietly stop being wired up.
/// </summary>
internal static class PdfContent
{
    /// <summary>Every distinct point size used by a text-showing operator.</summary>
    public static IReadOnlyList<double> FontSizes(byte[] pdf)
    {
        var sizes = new List<double>();
        foreach (var stream in Streams(pdf))
            foreach (Match m in Regex.Matches(stream, @"/[A-Za-z0-9_+\-]+\s+([0-9.]+)\s+Tf"))
                sizes.Add(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

        return sizes.Distinct().OrderBy(s => s).ToList();
    }

    private static IEnumerable<string> Streams(byte[] pdf)
    {
        // Latin-1 maps every byte to exactly one char, so string offsets and
        // byte offsets stay in step - which UTF-8 would not.
        var latin1 = Encoding.GetEncoding(28591);
        var text = latin1.GetString(pdf);

        foreach (Match m in Regex.Matches(text, "stream\r?\n"))
        {
            var start = m.Index + m.Length;
            var end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) continue;

            var length = end - start;
            if (length <= 2) continue;

            string decoded;
            try
            {
                // Skip the two-byte zlib header; DeflateStream wants raw deflate.
                using var raw = new MemoryStream(pdf, start + 2, length - 2);
                using var inflate = new DeflateStream(raw, CompressionMode.Decompress);
                using var reader = new StreamReader(inflate, latin1);
                decoded = reader.ReadToEnd();
            }
            catch
            {
                // Not a deflated stream (a font file, an image). Not our concern.
                continue;
            }

            yield return decoded;
        }
    }
}

internal static class PdfSizeAssert
{
    /// <summary>
    /// Sizes make the round trip through a 32-bit float, so 8.6 comes back as
    /// 8.6000004. Comparing to a hundredth of a point is well inside that
    /// noise and still far tighter than anything visible on paper.
    /// </summary>
    public static void Contains(double expected, IReadOnlyList<double> sizes)
    {
        if (sizes.Any(s => Math.Abs(s - expected) < 0.01)) return;

        throw new Xunit.Sdk.XunitException(
            $"Expected {expected}pt on the page. Sizes found: {string.Join(", ", sizes)}");
    }
}
