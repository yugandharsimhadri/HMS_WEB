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

    // Letterhead and grid sizes come from DocumentTheme now, so a clinic can
    // tune them from Settings > Document branding. The defaults there are the
    // desktop's own numbers, taken from its DocumentBuilder, so an untouched
    // install still prints a page that matches one already in a patient's
    // file. None of the letterhead sizes takes a per-document delta.
    //
    // The identity grid is derived rather than stored: its label sat 0.9
    // under the table header and its value 0.7 under the table row on the
    // desktop, and that relationship is what makes the grid read as a grid.
    // Deriving it keeps two fewer knobs on screen, and keeps them in step
    // when somebody moves the table.
    private const float IdentityLabelDrop = 0.9f;
    private const float IdentityValueDrop = 0.7f;

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
                .FontSize(Body(theme, (float)theme.DocumentKindSize)).Bold();

        col.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Muted);

        void Identity(ColumnDescriptor c)
        {
            c.Item().AlignCenter().Text(name)
                .FontFamily(TitleFont(theme))
                .FontSize(Body(theme, (float)theme.LetterheadNameSize, (float)theme.TitleFontSizeDelta)).Bold();

            if (!string.IsNullOrWhiteSpace(addressLine))
                c.Item().AlignCenter().Text(addressLine).FontSize(Body(theme, (float)theme.ContactLineSize)).FontColor(Muted);
            if (!string.IsNullOrWhiteSpace(addressLine2))
                c.Item().AlignCenter().Text(addressLine2).FontSize(Body(theme, (float)theme.ContactLineSize)).FontColor(Muted);
            if (!string.IsNullOrWhiteSpace(phone))
                c.Item().AlignCenter().Text($"Phone: {phone}").FontSize(Body(theme, (float)theme.ContactLineSize)).FontColor(Muted);
            if (!string.IsNullOrWhiteSpace(gstin))
                c.Item().AlignCenter().Text($"GSTIN: {gstin}").FontSize(Body(theme, (float)theme.ContactLineSize)).FontColor(Muted);
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
                text.Span($"{pair.Label}: ").FontSize(Body(theme, (float)theme.TableHeaderSize - IdentityLabelDrop, delta)).FontColor(Muted);
                text.Span(pair.Value).FontSize(Body(theme, (float)theme.TableRowSize - IdentityValueDrop, delta));
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
                text.Span($"{pair.Label}: ").FontSize(Body(theme, (float)theme.TableHeaderSize - IdentityLabelDrop, delta)).FontColor(Muted);
                text.Span(pair.Value).FontSize(Body(theme, (float)theme.TableRowSize - IdentityValueDrop, delta));
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
