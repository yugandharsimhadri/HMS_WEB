using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SivayaanHMS.Data;

namespace SivayaanHMS.Printing;

/// <summary>
/// The pieces every printed document shares: the clinic/pharmacy letterhead,
/// the three-across identity grid, rules, and the label/value pairs. Mirrors
/// the desktop's DocumentBuilder — same structure, same field order, so a
/// clinic that has been handing these to patients for years gets the same
/// piece of paper.
///
/// The desktop's prescription and fee receipt both print two points larger
/// than the pharmacy bill, by explicit request from the clinic; that +2 is
/// carried over here as <see cref="ClinicalSizeDelta"/> rather than being
/// quietly normalised away.
/// </summary>
public static class DocumentStyle
{
    /// <summary>Prescription and fee receipt run +2pt over the bill — the
    /// desktop's own `SizeDelta`, kept because it was asked for. It applies to
    /// the *body* of those documents only; see <see cref="Header"/>.</summary>
    public const float ClinicalSizeDelta = 2f;

    // Letterhead sizes, in absolute points, taken from the desktop's
    // DocumentBuilder so a web-printed page matches one already in a
    // patient's file. None of them takes a per-document delta.

    /// <summary>The desktop's ClinicNameFontSize — IdentityNameFontSize + 2,
    /// "a specific ask, not derived from anything else on the page".</summary>
    private const float ClinicNameSize = 15f;

    /// <summary>Address lines, phone and GSTIN.</summary>
    private const float ContactSize = 8f;

    /// <summary>"CASH RECEIPT", "TAX INVOICE". 8.6 on the desktop — a label
    /// over the rule, not a headline.</summary>
    private const float DocumentKindSize = 8.6f;

    /// <summary>The identity grid: the desktop's 6.6 label over a 7.8 value,
    /// both of which do take the clinical delta.</summary>
    private const float IdentityLabelSize = 6.6f;
    private const float IdentityValueSize = 7.8f;

    public static string Muted => Colors.Grey.Darken1;

    public static float Body(DocumentTheme theme, float baseSize, float delta = 0)
        => baseSize + (float)theme.PrintFontSizeDelta + delta;

    public static string BodyFont(DocumentTheme theme) => theme.PrintFontFamily ?? "Segoe UI";

    public static string TitleFont(DocumentTheme theme)
        => theme.TitleFontFamily ?? theme.PrintFontFamily ?? "Segoe UI";

    /// <summary>
    /// The letterhead: name, address lines, phone, optional GSTIN, and the
    /// document kind ("CASH RECEIPT") when there is one.
    ///
    /// <para>
    /// Sized in absolute points, and deliberately not offset by the caller's
    /// <c>delta</c>. The desktop applies its SizeDelta to the body of a
    /// clinical document — the identity grid, the line table, the amounts —
    /// and never to the letterhead, which prints at one size on every
    /// document it produces. Passing the delta through here is what made a
    /// receipt open with a 16pt clinic name and a 13pt "CASH RECEIPT" over
    /// half an A5 page.
    /// </para>
    /// </summary>
    public static void Header(
        ColumnDescriptor col, string name, string? addressLine, string? addressLine2,
        string? phone, string? gstin, string? documentKind, DocumentTheme theme, float delta = 0)
    {
        var logo = DecodeLogo(theme);

        if (logo is null)
        {
            Identity(col);
        }
        else
        {
            // Logo left, identity right — the desktop's DocumentBuilder gives
            // the logo the same 28% share, and matching it is what makes a
            // web-printed prescription look like the ones already in the
            // patient's file.
            col.Item().Row(row =>
            {
                row.RelativeItem(LogoColumnShare).AlignMiddle().MaxHeight(52).Image(logo).FitArea();
                row.RelativeItem(100 - LogoColumnShare).Column(Identity);
            });
        }

        if (!string.IsNullOrWhiteSpace(documentKind))
            col.Item().PaddingTop(4).AlignCenter().Text(documentKind)
                .FontSize(Body(theme, DocumentKindSize)).Bold();

        col.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Muted);

        void Identity(ColumnDescriptor c)
        {
            c.Item().AlignCenter().Text(name)
                .FontFamily(TitleFont(theme))
                .FontSize(Body(theme, ClinicNameSize, (float)theme.TitleFontSizeDelta)).Bold();

            if (!string.IsNullOrWhiteSpace(addressLine))
                c.Item().AlignCenter().Text(addressLine).FontSize(Body(theme, ContactSize)).FontColor(Muted);
            if (!string.IsNullOrWhiteSpace(addressLine2))
                c.Item().AlignCenter().Text(addressLine2).FontSize(Body(theme, ContactSize)).FontColor(Muted);
            if (!string.IsNullOrWhiteSpace(phone))
                c.Item().AlignCenter().Text($"Phone: {phone}").FontSize(Body(theme, ContactSize)).FontColor(Muted);
            if (!string.IsNullOrWhiteSpace(gstin))
                c.Item().AlignCenter().Text($"GSTIN: {gstin}").FontSize(Body(theme, ContactSize)).FontColor(Muted);
        }
    }

    /// <summary>The share of the letterhead width the logo takes, matching
    /// the desktop's DocumentBuilder.</summary>
    public const int LogoColumnShare = 28;

    /// <summary>
    /// The stored logo as bytes, or null when there isn't one.
    ///
    /// Every failure returns null rather than throwing. A logo is decoration
    /// on a document that also carries a prescription: bad base64, a
    /// truncated upload or a file that is not an image must cost the clinic
    /// its letterhead, never the ability to print at all.
    /// </summary>
    public static byte[]? DecodeLogo(DocumentTheme theme)
    {
        if (string.IsNullOrWhiteSpace(theme.LogoBase64)) return null;

        try
        {
            // Tolerates a data URI, since that is what a browser's FileReader
            // hands back and what the settings screen stores.
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

    /// <summary>
    /// One row of the identity grid — three labelled cells across. The
    /// desktop's IdentityRow; keeping the same shape is what makes the
    /// printed page recognisable.
    /// </summary>
    public static void IdentityRow(
        ColumnDescriptor col, DocumentTheme theme, float delta,
        (string Label, string Value) a, (string Label, string Value) b, (string Label, string Value) c)
    {
        col.Item().PaddingTop(2).Row(row =>
        {
            Cell(row.RelativeItem(), a);
            Cell(row.RelativeItem(), b);
            Cell(row.RelativeItem(), c);
        });

        void Cell(IContainer container, (string Label, string Value) pair)
            => container.Text(text =>
            {
                text.Span($"{pair.Label}: ").FontSize(Body(theme, IdentityLabelSize, delta)).FontColor(Muted);
                text.Span(pair.Value).FontSize(Body(theme, IdentityValueSize, delta));
            });
    }

    /// <summary>
    /// Two fields across the same width as three, for a row that would
    /// otherwise carry a blank cell. An empty pair renders as a stray ": ",
    /// which reads as a field whose value went missing rather than a field
    /// that was never there.
    /// </summary>
    public static void IdentityRow(
        ColumnDescriptor col, DocumentTheme theme, float delta,
        (string Label, string Value) a, (string Label, string Value) b)
    {
        col.Item().PaddingTop(2).Row(row =>
        {
            Cell(row.RelativeItem(), a);
            Cell(row.RelativeItem(2), b);
        });

        void Cell(IContainer container, (string Label, string Value) pair)
            => container.Text(text =>
            {
                text.Span($"{pair.Label}: ").FontSize(Body(theme, IdentityLabelSize, delta)).FontColor(Muted);
                text.Span(pair.Value).FontSize(Body(theme, IdentityValueSize, delta));
            });
    }

    public static void Rule(ColumnDescriptor col)
        => col.Item().PaddingVertical(3).LineHorizontal(0.75f).LineColor(Muted);

    public static void LabelValue(ColumnDescriptor col, DocumentTheme theme, float delta, string label, string value)
        => col.Item().PaddingTop(2).Text(text =>
        {
            text.Span($"{label}: ").FontSize(Body(theme, 8.5f, delta)).FontColor(Muted);
            text.Span(value).FontSize(Body(theme, 8.5f, delta));
        });

    /// <summary>The footer a clinic or pharmacy typed for itself, falling back
    /// to the shared document-theme one when it is blank — the desktop's own
    /// precedence, preserved so a clinic that never visits the field keeps
    /// printing what it always did.</summary>
    public static string? Footer(string? ownFooter, DocumentTheme theme)
        => string.IsNullOrWhiteSpace(ownFooter) ? theme.Footer : ownFooter;
}
