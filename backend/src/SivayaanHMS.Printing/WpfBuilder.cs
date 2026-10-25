using SivayaanHMS.Data;

namespace SivayaanHMS.Printing;

/// <summary>
/// The desktop's <c>DocumentBuilder</c>, function for function, on top of
/// <see cref="Flow"/>. Same names, same parameters, same defaults and the same
/// literals — every size below is the number HMS_WPF's DocumentBuilder.cs uses,
/// in WPF's own pixels, so a printer in this project reads against that file
/// line by line. The unit conversion happens once, in the renderer.
///
/// Keep this file free of cleverness. Its whole value is that it can be read
/// alongside the WPF original and seen to agree.
/// </summary>
internal static class Wpf
{
    public const string Ink = Flow.Ink;
    public const string Muted = Flow.Secondary;

    private const double IdentityNameFontSize = 13;
    private const double ClinicNameFontSize = IdentityNameFontSize + 2;

    // ── Paragraphs and rules ─────────────────────────────────────────────

    /// <summary>DocumentBuilder.Text — margins are always explicit here (0 unless given).</summary>
    public static Para Text(string text, double size = 9, Weight weight = Weight.Normal, string? brush = null,
                            Align align = Align.Left, double topMargin = 0, double bottomMargin = 0,
                            SizeRole role = SizeRole.None)
        => new()
        {
            Runs = [new Run(text)],
            Size = size, Weight = weight, Color = brush ?? Ink, Align = align,
            MarginTop = topMargin, MarginBottom = bottomMargin,
            Role = role
        };

    /// <summary>DocumentBuilder.LabelValueText — bold label, ordinary-weight value.</summary>
    public static Para LabelValueText(string label, string value, double size = 8, string? brush = null,
                                      Align align = Align.Left, double topMargin = 0, double bottomMargin = 0,
                                      SizeRole role = SizeRole.None)
        => new()
        {
            Runs = [new Run($"{label}: ", Weight.Bold), new Run(value, Weight.Normal)],
            Size = size, Color = brush ?? Ink, Align = align,
            MarginTop = topMargin, MarginBottom = bottomMargin,
            Role = role
        };

    public static RuleBlock Rule() => new();

    // ── Tables ───────────────────────────────────────────────────────────

    public static Tbl NewTable(params double[] widths) => new(widths);

    /// <summary>DocumentBuilder.Row. Header 7.6 semibold secondary, body 8.4 ink;
    /// everything after the first two columns is numeric, so right-aligned.</summary>
    public static TblRow Row(bool header, params string[] cells) => Row(header, 0, cells);

    public static TblRow Row(bool header, double sizeDelta, params string[] cells)
    {
        var row = new TblRow();

        for (var index = 0; index < cells.Length; index++)
        {
            var cell = new TblCell { BorderBottom = header ? 1 : 0.5 };
            cell.Blocks.Add(new Para
            {
                Runs = [new Run(cells[index])],
                MarginLeft = 3, MarginRight = 3, MarginTop = 2, MarginBottom = 2,
                Size = (header ? 7.6 : 8.4) + sizeDelta,
                Weight = header ? Weight.SemiBold : Weight.Normal,
                Color = header ? Muted : Ink,
                Align = index >= 2 ? Align.Right : Align.Left,
                Role = header ? SizeRole.Header : SizeRole.Row
            });
            row.Cells.Add(cell);
        }

        return row;
    }

    /// <summary>DocumentBuilder.IdentityRow — label above value, in each cell.
    /// Label 6.6 bold secondary, value 7.8 ink. A cell's paragraphs inherit the
    /// FlowDocument default alignment, which is Justify.</summary>
    public static TblRow IdentityRow(params (string Label, string Value)[] cells) => IdentityRow(0, cells);

    public static TblRow IdentityRow(double sizeDelta, params (string Label, string Value)[] cells)
    {
        var row = new TblRow();

        foreach (var (label, value) in cells)
        {
            var cell = new TblCell();
            cell.Blocks.Add(new Para
            {
                Runs = [new Run(label)],
                Size = 6.6 + sizeDelta, Weight = Weight.Bold, Color = Muted, Align = Align.Justify,
                Role = SizeRole.Header,
                MarginLeft = 2, MarginRight = 2, MarginTop = 1, MarginBottom = 0
            });
            cell.Blocks.Add(new Para
            {
                Runs = [new Run(value)],
                Size = 7.8 + sizeDelta, Weight = Weight.Normal, Color = Ink, Align = Align.Justify,
                Role = SizeRole.Row,
                MarginLeft = 2, MarginRight = 2, MarginTop = 0, MarginBottom = 2
            });
            row.Cells.Add(cell);
        }

        return row;
    }

    /// <summary>DocumentBuilder.TotalRow — bold label, an empty middle cell, value right-aligned.</summary>
    public static TblRow TotalRow(string label, string value)
    {
        var row = new TblRow();
        row.Cells.Add(TotalCell(label, Weight.Bold, Align.Left));
        row.Cells.Add(TotalCell("", Weight.Normal, Align.Left));
        row.Cells.Add(TotalCell(value, Weight.Normal, Align.Right));
        return row;
    }

    private static TblCell TotalCell(string text, Weight weight, Align align)
    {
        var cell = new TblCell();
        cell.Blocks.Add(new Para
        {
            Runs = [new Run(text)],
            MarginLeft = 3, MarginRight = 3, MarginTop = 2, MarginBottom = 2,
            Size = 8.4, Weight = weight, Color = Ink, Align = align,
            Role = SizeRole.Row
        });
        return cell;
    }

    // ── Letterheads ──────────────────────────────────────────────────────

    /// <summary>
    /// The stored logo as bytes, or null when there isn't one.
    ///
    /// Every failure returns null rather than throwing. A logo is decoration on a document that
    /// also carries a prescription: bad base64, a truncated upload or a file that is not an image
    /// must cost the clinic its letterhead, never the ability to print at all.
    /// </summary>
    public static byte[]? DecodeLogo(DocumentTheme theme)
    {
        if (string.IsNullOrWhiteSpace(theme.LogoBase64)) return null;

        try
        {
            // Tolerates a data URI, since that is what a browser's FileReader hands back and
            // what the settings screen stores.
            var raw = theme.LogoBase64;
            var comma = raw.IndexOf(',');
            if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
                raw = raw[(comma + 1)..];

            var bytes = Convert.FromBase64String(raw.Trim());
            return bytes.Length == 0 ? null : bytes;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The boxed clinic identity a prescription and a fee receipt open with.
    /// Never a drug licence number; GSTIN only when the clinic itself is registered.</summary>
    public static void AddClinicHeader(FlowDoc doc, ClinicProfile clinic, DocumentTheme theme, string? documentTitle)
    {
        var (line1, line2) = ContactLines(clinic.AddressLine, clinic.AddressLine2, clinic.Phone);
        doc.Add(IdentityBlock(
            clinic.Name, line1, line2,
            clinic.GstRegistered && !string.IsNullOrWhiteSpace(clinic.Gstin) ? $"GSTIN: {clinic.Gstin}" : null,
            documentTitle, theme, boxed: true, nameFontSize: ClinicNameFontSize));
    }

    /// <summary>The unboxed pharmacy identity a bill opens with. The GSTIN prints here
    /// only on a tax invoice.</summary>
    public static void AddPharmacyHeader(FlowDoc doc, PharmacyProfile pharmacy, DocumentTheme theme,
                                         string? documentTitle, bool showGstin)
    {
        var licence = showGstin && !string.IsNullOrWhiteSpace(pharmacy.Gstin) ? $"GSTIN: {pharmacy.Gstin}" : null;

        var (line1, line2) = ContactLines(pharmacy.AddressLine, pharmacy.AddressLine2, pharmacy.Phone);
        doc.Add(IdentityBlock(
            pharmacy.Name, line1, line2, licence,
            documentTitle, theme, boxed: false, nameFontSize: IdentityNameFontSize));
    }

    /// <summary>Address 1, then address 2 and the phone together on one line, so a clinic
    /// with a single address line still gets a tidy two-line header.</summary>
    private static (string? Line1, string? Line2) ContactLines(string? address1, string? address2, string? phone)
    {
        var line1 = string.IsNullOrWhiteSpace(address1) ? null : address1;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(address2)) parts.Add(address2);
        if (!string.IsNullOrWhiteSpace(phone)) parts.Add($"Ph {phone}");
        var line2 = parts.Count > 0 ? string.Join("   |   ", parts) : null;

        return line1 is null ? (line2, null) : (line1, line2);
    }

    private static Sec IdentityBlock(string name, string? contactLine1, string? contactLine2, string? licenceLine,
                                     string? documentTitle, DocumentTheme theme, bool boxed, double nameFontSize)
    {
        var section = new Sec
        {
            Boxed = boxed,
            PadLeft = boxed ? 6 : 0, PadTop = boxed ? 4 : 0, PadRight = boxed ? 6 : 0, PadBottom = boxed ? 4 : 0,
            Logo = DecodeLogo(theme)
        };

        // Name: both margins unset, so it sits one line of its own size away from its neighbours.
        section.Blocks.Add(new Para
        {
            Runs = [new Run(name)],
            Size = nameFontSize + theme.TitleFontSizeDelta, Weight = Weight.Bold, Align = Align.Center, Color = Ink,
            MarginTop = null, MarginBottom = null,
            Family = string.IsNullOrWhiteSpace(theme.TitleFontFamily) ? null : theme.TitleFontFamily,
            Role = SizeRole.Letterhead
        });

        if (contactLine1 is not null)
            section.Blocks.Add(new Para
            {
                Runs = [new Run(contactLine1)], Size = 8, Align = Align.Center, Color = Muted, Role = SizeRole.Contact,
                MarginTop = 1, MarginBottom = 0
            });

        if (contactLine2 is not null)
            section.Blocks.Add(new Para
            {
                Runs = [new Run(contactLine2)], Size = 8, Align = Align.Center, Color = Muted, Role = SizeRole.Contact,
                MarginTop = null, MarginBottom = null
            });

        if (licenceLine is not null)
            section.Blocks.Add(new Para
            {
                Runs = [new Run(licenceLine)], Size = 8, Align = Align.Center, Color = Muted, Role = SizeRole.Contact,
                MarginTop = null, MarginBottom = null
            });

        if (!string.IsNullOrWhiteSpace(documentTitle))
            section.Blocks.Add(new Para
            {
                Runs = [new Run(documentTitle)], Size = 8.6, Weight = Weight.Bold, Align = Align.Center,
                Underline = boxed, Color = Ink, Role = SizeRole.Kind, MarginTop = 4, MarginBottom = 0
            });

        return section;
    }
}
